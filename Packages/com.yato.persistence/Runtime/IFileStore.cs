using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yato.Persistence
{
    /// <summary>
    /// 基础文件存储（D04）：同步与异步读写共用同一路径互斥与安全写入语义。
    /// <para>同一路径的操作立即失败（<see cref="FileBusyException"/>），不排队；不同路径可并发。</para>
    /// <para>路径一律为相对应用数据根目录的安全路径；写入先落同目录唯一临时文件再原子提交。</para>
    /// </summary>
    public interface IFileStore
    {
        /// <summary>应用数据根目录（规范化绝对路径）。</summary>
        string RootPath { get; }

        bool Exists(string relativePath);

        long GetFileSize(string relativePath);

        DateTime GetLastWriteTimeUtc(string relativePath);

        byte[] ReadAllBytes(string relativePath);

        string ReadAllText(string relativePath);

        void WriteAllBytes(string relativePath, byte[] data, CancellationToken cancellationToken = default(CancellationToken));

        void WriteAllText(string relativePath, string text, CancellationToken cancellationToken = default(CancellationToken));

        Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default(CancellationToken));

        Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default(CancellationToken));

        Task WriteAllBytesAsync(string relativePath, byte[] data, CancellationToken cancellationToken = default(CancellationToken));

        Task WriteAllTextAsync(string relativePath, string text, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>删除文件；文件不存在返回 false（不视为错误）。</summary>
        bool Delete(string relativePath);

        void EnsureDirectory(string relativeDirectory);

        /// <summary>枚举文件，返回相对根目录、以 '/' 分隔的路径；残留临时文件不会出现在结果中。</summary>
        IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern = "*", bool recursive = false);

        /// <summary>
        /// 显式持有某路径的互斥标记，用于读-改-写等复合操作；已占用时抛 <see cref="FileBusyException"/>。
        /// 使用 <c>using</c> 释放。
        /// </summary>
        IDisposable AcquireLock(string relativePath);
    }
}
