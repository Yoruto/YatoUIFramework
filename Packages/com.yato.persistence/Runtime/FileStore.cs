using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yato.Core;

namespace Yato.Persistence
{
    /// <summary>
    /// <see cref="IFileStore"/> 默认实现（D04）。
    /// <para>写入：同目录唯一临时文件 → 完整写入并刷盘 → 原子替换目标；提交失败不会降级为直接覆盖。</para>
    /// <para>取消：临时文件写完、提交之前可取消；一旦进入提交阶段，报告真实提交结果。</para>
    /// </summary>
    public sealed class FileStore : IFileStore
    {
        /// <summary>临时文件后缀；枚举与清理据此识别残留临时文件。</summary>
        public const string TempFileSuffix = ".yato-tmp";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        private readonly PersistencePaths m_paths;

        /// <summary>正在进行操作的绝对路径集合（同一路径互斥）。</summary>
        private readonly HashSet<string> m_active = new HashSet<string>(PathComparer);

        private readonly object m_gate = new object();

        public FileStore(string rootPath = null)
            : this(new PersistencePaths(rootPath))
        {
        }

        public FileStore(PersistencePaths paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException("paths");
            }

            m_paths = paths;
        }

        public string RootPath
        {
            get { return m_paths.RootPath; }
        }

        public bool Exists(string relativePath)
        {
            return File.Exists(m_paths.Resolve(relativePath));
        }

        public long GetFileSize(string relativePath)
        {
            var info = new FileInfo(m_paths.Resolve(relativePath));
            if (!info.Exists)
            {
                throw new FileNotFoundException("文件不存在：" + relativePath, info.FullName);
            }

            return info.Length;
        }

        public DateTime GetLastWriteTimeUtc(string relativePath)
        {
            var info = new FileInfo(m_paths.Resolve(relativePath));
            if (!info.Exists)
            {
                throw new FileNotFoundException("文件不存在：" + relativePath, info.FullName);
            }

            return info.LastWriteTimeUtc;
        }

        public byte[] ReadAllBytes(string relativePath)
        {
            string fullPath = m_paths.Resolve(relativePath);

            using (AcquireResolved(fullPath))
            {
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("文件不存在：" + relativePath, fullPath);
                }

                return File.ReadAllBytes(fullPath);
            }
        }

        public string ReadAllText(string relativePath)
        {
            return Utf8NoBom.GetString(ReadAllBytes(relativePath));
        }

        public void WriteAllBytes(
            string relativePath,
            byte[] data,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            string fullPath = m_paths.Resolve(relativePath);
            using (AcquireResolved(fullPath))
            {
                WriteBytesCore(fullPath, data, cancellationToken);
            }
        }

        public void WriteAllText(
            string relativePath,
            string text,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            WriteAllBytes(relativePath, Utf8NoBom.GetBytes(text), cancellationToken);
        }

        public async Task<byte[]> ReadAllBytesAsync(
            string relativePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string fullPath = m_paths.Resolve(relativePath);

            using (AcquireResolved(fullPath))
            {
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("文件不存在：" + relativePath, fullPath);
                }

                using (var stream = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }
        }

        public async Task<string> ReadAllTextAsync(
            string relativePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            byte[] bytes = await ReadAllBytesAsync(relativePath, cancellationToken).ConfigureAwait(false);
            return Utf8NoBom.GetString(bytes);
        }

        public async Task WriteAllBytesAsync(
            string relativePath,
            byte[] data,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            string fullPath = m_paths.Resolve(relativePath);
            using (AcquireResolved(fullPath))
            {
                await WriteBytesCoreAsync(fullPath, data, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task WriteAllTextAsync(
            string relativePath,
            string text,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            await WriteAllBytesAsync(relativePath, Utf8NoBom.GetBytes(text), cancellationToken)
                .ConfigureAwait(false);
        }

        public bool Delete(string relativePath)
        {
            string fullPath = m_paths.Resolve(relativePath);

            using (AcquireResolved(fullPath))
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                File.Delete(fullPath);
                return true;
            }
        }

        public void EnsureDirectory(string relativeDirectory)
        {
            string fullPath = string.IsNullOrEmpty(relativeDirectory)
                ? m_paths.RootPath
                : m_paths.Resolve(relativeDirectory);

            Directory.CreateDirectory(fullPath);
        }

        public IReadOnlyList<string> EnumerateFiles(
            string relativeDirectory,
            string searchPattern = "*",
            bool recursive = false)
        {
            string directory = string.IsNullOrEmpty(relativeDirectory)
                ? m_paths.RootPath
                : m_paths.Resolve(relativeDirectory);

            if (!Directory.Exists(directory))
            {
                return new List<string>();
            }

            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files = Directory.GetFiles(directory, string.IsNullOrEmpty(searchPattern) ? "*" : searchPattern, option);

            var result = new List<string>(files.Length);
            string rootPrefix = m_paths.RootPath + Path.DirectorySeparatorChar;

            for (int i = 0; i < files.Length; i++)
            {
                // 残留临时文件永远不能当作正式文件
                if (files[i].EndsWith(TempFileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = files[i].StartsWith(rootPrefix, PersistencePaths.PathComparison)
                    ? files[i].Substring(rootPrefix.Length)
                    : files[i];

                result.Add(relative.Replace('\\', '/'));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public IDisposable AcquireLock(string relativePath)
        {
            return AcquireResolved(m_paths.Resolve(relativePath));
        }

        /// <summary>
        /// 清理残留临时文件；存在进行中的操作时拒绝执行（D04-6 的“确定没有活动操作”）。
        /// 返回清理的文件数量。
        /// </summary>
        public int CleanupTemporaryFiles(string relativeDirectory = null, bool recursive = true)
        {
            lock (m_gate)
            {
                if (m_active.Count > 0)
                {
                    throw new InvalidOperationException("存在进行中的文件操作，不能清理临时文件");
                }
            }

            string directory = string.IsNullOrEmpty(relativeDirectory)
                ? m_paths.RootPath
                : m_paths.Resolve(relativeDirectory);

            if (!Directory.Exists(directory))
            {
                return 0;
            }

            string[] files = Directory.GetFiles(
                directory,
                "*" + TempFileSuffix,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            int removed = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);
                    removed++;
                }
                catch (Exception e)
                {
                    Log.Warn(string.Format("[Yato.Persistence] 清理临时文件失败：{0}（{1}）", files[i], e.Message));
                }
            }

            return removed;
        }

        private IDisposable AcquireResolved(string fullPath)
        {
            lock (m_gate)
            {
                if (!m_active.Add(fullPath))
                {
                    throw new FileBusyException(fullPath);
                }
            }

            return new PathLease(this, fullPath);
        }

        private void ReleaseResolved(string fullPath)
        {
            lock (m_gate)
            {
                m_active.Remove(fullPath);
            }
        }

        private static void WriteBytesCore(string fullPath, byte[] data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryFor(fullPath);

            string tempPath = BuildTempPath(fullPath);
            bool committed = false;

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }

                // 提交前最后一个取消点：此时原文件仍然完整
                cancellationToken.ThrowIfCancellationRequested();

                Commit(tempPath, fullPath);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    TryDelete(tempPath);
                }
            }
        }

        private static async Task WriteBytesCoreAsync(
            string fullPath,
            byte[] data,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryFor(fullPath);

            string tempPath = BuildTempPath(fullPath);
            bool committed = false;

            try
            {
                using (var stream = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                {
                    await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    stream.Flush(true);
                }

                // 进入提交阶段后不再响应取消：报告真实提交结果（D04-3）
                cancellationToken.ThrowIfCancellationRequested();

                Commit(tempPath, fullPath);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    TryDelete(tempPath);
                }
            }
        }

        private static void Commit(string tempPath, string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    // 原子替换；平台不支持时直接失败，绝不静默降级为直接覆盖
                    File.Replace(tempPath, destinationPath, null);
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }
            }
            catch (Exception e)
            {
                throw new FileCommitException(destinationPath, e);
            }
        }

        private static void EnsureDirectoryFor(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string BuildTempPath(string fullPath)
        {
            return string.Format("{0}.{1}{2}", fullPath, Guid.NewGuid().ToString("N"), TempFileSuffix);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                // 尽力清理：失败也不能掩盖原始错误
                Log.Warn(string.Format("[Yato.Persistence] 清理临时文件失败：{0}（{1}）", path, e.Message));
            }
        }

        private sealed class PathLease : IDisposable
        {
            private FileStore m_owner;
            private readonly string m_path;

            public PathLease(FileStore owner, string path)
            {
                m_owner = owner;
                m_path = path;
            }

            public void Dispose()
            {
                FileStore owner = Interlocked.Exchange(ref m_owner, null);
                if (owner != null)
                {
                    owner.ReleaseResolved(m_path);
                }
            }
        }
    }
}
