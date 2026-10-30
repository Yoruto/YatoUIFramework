using System;

namespace Yato.Resources
{
    /// <summary>
    /// 逻辑资源路径。规范：全小写、<c>/</c> 分隔、无扩展名，例如 <c>ui/bag/bagform</c>。
    /// 构造时自动规范化，避免调用方各自拼写导致同一资源出现多个 key（RES-11）。
    /// </summary>
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        /// <summary>规范化后的 key；非法输入时为空串。</summary>
        public readonly string Value;

        public AssetKey(string value)
        {
            Value = Normalize(value);
        }

        /// <summary>是否是可用的 key（非空且不含非法字符）。</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(Value); }
        }

        public bool Equals(AssetKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AssetKey && Equals((AssetKey)obj);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(AssetKey left, AssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AssetKey left, AssetKey right)
        {
            return !left.Equals(right);
        }

        /// <summary>从字符串构造（会规范化）。</summary>
        public static AssetKey From(string value)
        {
            return new AssetKey(value);
        }

        /// <summary>
        /// 规范化：去空白、反斜杠转正斜杠、合并重复斜杠、去首尾斜杠、
        /// 去掉文件扩展名、统一小写。
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            string value = raw.Trim().Replace('\\', '/');

            // 先合并重复斜杠并修剪首尾斜杠；否则 "a.png/" 这类输入会让
            // lastSlash 落在扩展名之后，导致 lastDot > lastSlash 不成立、扩展名漏剥
            while (value.Contains("//"))
            {
                value = value.Replace("//", "/");
            }

            value = value.Trim('/');

            // 去掉扩展名（只处理最后一段，且必须是 xxx.ext 形式）
            int lastSlash = value.LastIndexOf('/');
            int lastDot = value.LastIndexOf('.');
            if (lastDot > lastSlash && lastDot >= 0)
            {
                value = value.Substring(0, lastDot);
            }

            return value.ToLowerInvariant();
        }
    }
}
