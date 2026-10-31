using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Yato.Persistence
{
    /// <summary>
    /// 应用数据根目录与安全相对路径（D04-02/03）。
    /// <para>默认根目录为 <see cref="Application.persistentDataPath"/>，游戏只提供安全相对路径。</para>
    /// <para>拒绝绝对路径、<c>..</c> 越界、盘符/数据流以及经由符号链接逃逸根目录。</para>
    /// </summary>
    public sealed class PersistencePaths
    {
        /// <summary>Windows 下路径大小写不敏感，其它平台按序数比较。</summary>
        public static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly string m_rootPath;
        private readonly string m_rootPrefix;

        public PersistencePaths(string rootPath = null)
        {
            string root = string.IsNullOrEmpty(rootPath) ? Application.persistentDataPath : rootPath;
            if (string.IsNullOrEmpty(root))
            {
                throw new InvalidOperationException("[Yato.Persistence] 无法确定应用数据根目录（persistentDataPath 为空）");
            }

            m_rootPath = NormalizeRoot(root);
            m_rootPrefix = m_rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? m_rootPath
                : m_rootPath + Path.DirectorySeparatorChar;
        }

        /// <summary>规范化后的根目录绝对路径（无尾随分隔符）。</summary>
        public string RootPath
        {
            get { return m_rootPath; }
        }

        /// <summary>把安全相对路径解析为绝对路径；非法路径抛 <see cref="ArgumentException"/>，链接逃逸抛 <see cref="IOException"/>。</summary>
        public string Resolve(string relativePath)
        {
            string normalized = Normalize(relativePath);
            string fullPath = Path.GetFullPath(Path.Combine(m_rootPath, normalized));

            if (!Contains(fullPath))
            {
                throw new ArgumentException(
                    string.Format("相对路径越出应用数据根目录：{0}", relativePath), "relativePath");
            }

            EnsureNoLinkEscape(fullPath);
            return fullPath;
        }

        /// <summary>绝对路径是否位于根目录之内。</summary>
        public bool Contains(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

            string normalized = Path.GetFullPath(fullPath);
            if (string.Equals(normalized, m_rootPath, PathComparison))
            {
                return true;
            }

            return normalized.StartsWith(m_rootPrefix, PathComparison);
        }

        /// <summary>
        /// 规范化相对路径：反斜杠转正斜杠、合并重复分隔符、去掉 "." 段。
        /// 空路径、绝对路径、包含 ".." 或 ":" 的路径一律拒绝。
        /// </summary>
        public static string Normalize(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("相对路径不能为空", "relativePath");
            }

            string value = relativePath.Replace('\\', '/').Trim();
            if (value.Length == 0)
            {
                throw new ArgumentException("相对路径不能为空", "relativePath");
            }

            if (value.IndexOf(':') >= 0)
            {
                throw new ArgumentException(
                    string.Format("相对路径不能包含盘符或数据流分隔符：{0}", relativePath), "relativePath");
            }

            if (value[0] == '/')
            {
                throw new ArgumentException(
                    string.Format("只接受相对路径，不能是绝对路径或 UNC 路径：{0}", relativePath), "relativePath");
            }

            string[] parts = value.Split('/');
            var segments = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    throw new ArgumentException(
                        string.Format("相对路径不能包含 .. 越界段：{0}", relativePath), "relativePath");
                }

                segments.Add(part);
            }

            if (segments.Count == 0)
            {
                throw new ArgumentException(
                    string.Format("相对路径没有有效路径段：{0}", relativePath), "relativePath");
            }

            return string.Join("/", segments.ToArray());
        }

        private static string NormalizeRoot(string root)
        {
            string full = Path.GetFullPath(root);

            // 保留 "C:\" 与 "/" 这类根路径的尾随分隔符
            if (full.Length > 3)
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }

        /// <summary>
        /// 拒绝经由符号链接/联接点逃逸根目录：逐段检查根目录之下的已存在节点。
        /// 注意：检查与实际写入之间存在 TOCTOU 窗口，仅承诺尽力而为。
        /// </summary>
        private void EnsureNoLinkEscape(string fullPath)
        {
            if (fullPath.Length <= m_rootPrefix.Length - 1)
            {
                return;
            }

            string relative = fullPath.Substring(m_rootPrefix.Length);
            if (relative.Length == 0)
            {
                return;
            }

            string[] parts = relative.Split(Path.DirectorySeparatorChar);
            string current = m_rootPath;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }

                current = Path.Combine(current, parts[i]);

                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    // 尚不存在的部分由创建流程负责，无法也不应提前判定
                    return;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (FileNotFoundException)
                {
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    return;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(string.Format(
                        "拒绝经由符号链接访问根目录之外的路径：{0}", relative));
                }
            }
        }
    }
}
