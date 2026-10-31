using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Yato.Resources.Internal
{
    /// <summary>
    /// 极简 JSON 读取器：只服务于 manifest / version 文件，避免框架绑定具体 JSON 库。
    /// 支持对象、数组、字符串（含转义）、数字、布尔、null。
    /// 解析结果：Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.IsEnd)
            {
                throw new FormatException(string.Format("JSON 解析结束位置不合法，index={0}", parser.Index));
            }

            return value;
        }

        internal static string GetString(IDictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as string : null;
        }

        internal static long GetLong(IDictionary<string, object> dict, string key, long fallback = 0L)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            if (value is double)
            {
                return (long)(double)value;
            }

            long parsed;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        internal static List<string> GetStringList(IDictionary<string, object> dict, string key)
        {
            var result = new List<string>();
            object value;
            if (dict == null || !dict.TryGetValue(key, out value))
            {
                return result;
            }

            var list = value as List<object>;
            if (list == null)
            {
                return result;
            }

            for (int i = 0; i < list.Count; i++)
            {
                string item = list[i] as string;
                if (!string.IsNullOrEmpty(item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        private sealed class Parser
        {
            private readonly string m_text;
            private int m_index;

            public Parser(string text)
            {
                m_text = text;
                m_index = 0;
            }

            public int Index
            {
                get { return m_index; }
            }

            public bool IsEnd
            {
                get { return m_index >= m_text.Length; }
            }

            public void SkipWhitespace()
            {
                while (m_index < m_text.Length)
                {
                    char c = m_text[m_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                    {
                        m_index++;
                        continue;
                    }

                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (IsEnd)
                {
                    throw new FormatException("JSON 意外结束");
                }

                char c = m_text[m_index];
                switch (c)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return ParseString();
                    case 't':
                        Expect("true");
                        return true;
                    case 'f':
                        Expect("false");
                        return false;
                    case 'n':
                        Expect("null");
                        return null;
                    default:
                        return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                m_index++; // '{'
                SkipWhitespace();
                if (!IsEnd && m_text[m_index] == '}')
                {
                    m_index++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (IsEnd || m_text[m_index] != ':')
                    {
                        throw new FormatException(string.Format("JSON 对象缺少冒号，index={0}", m_index));
                    }

                    m_index++;
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    if (IsEnd)
                    {
                        throw new FormatException("JSON 对象未闭合");
                    }

                    char c = m_text[m_index];
                    m_index++;
                    if (c == '}')
                    {
                        return dict;
                    }

                    if (c != ',')
                    {
                        throw new FormatException(string.Format("JSON 对象分隔符非法：{0}", c));
                    }
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                m_index++; // '['
                SkipWhitespace();
                if (!IsEnd && m_text[m_index] == ']')
                {
                    m_index++;
                    return list;
                }

                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (IsEnd)
                    {
                        throw new FormatException("JSON 数组未闭合");
                    }

                    char c = m_text[m_index];
                    m_index++;
                    if (c == ']')
                    {
                        return list;
                    }

                    if (c != ',')
                    {
                        throw new FormatException(string.Format("JSON 数组分隔符非法：{0}", c));
                    }
                }
            }

            private string ParseString()
            {
                if (IsEnd || m_text[m_index] != '"')
                {
                    throw new FormatException(string.Format("JSON 期望字符串，index={0}", m_index));
                }

                m_index++;
                var builder = new StringBuilder(32);
                while (m_index < m_text.Length)
                {
                    char c = m_text[m_index];
                    m_index++;

                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (IsEnd)
                    {
                        break;
                    }

                    char escape = m_text[m_index];
                    m_index++;
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (m_index + 4 > m_text.Length)
                            {
                                throw new FormatException("JSON \\u 转义不完整");
                            }

                            string hex = m_text.Substring(m_index, 4);
                            m_index += 4;
                            builder.Append((char)Convert.ToInt32(hex, 16));
                            break;
                        default:
                            throw new FormatException(string.Format("JSON 非法转义：\\{0}", escape));
                    }
                }

                throw new FormatException("JSON 字符串未闭合");
            }

            private double ParseNumber()
            {
                int start = m_index;
                while (m_index < m_text.Length)
                {
                    char c = m_text[m_index];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    {
                        m_index++;
                        continue;
                    }

                    break;
                }

                string raw = m_text.Substring(start, m_index - start);
                double value;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    throw new FormatException(string.Format("JSON 非法数字：{0}", raw));
                }

                return value;
            }

            private void Expect(string literal)
            {
                if (m_index + literal.Length > m_text.Length ||
                    string.CompareOrdinal(m_text, m_index, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException(string.Format("JSON 期望 {0}，index={1}", literal, m_index));
                }

                m_index += literal.Length;
            }
        }
    }
}
