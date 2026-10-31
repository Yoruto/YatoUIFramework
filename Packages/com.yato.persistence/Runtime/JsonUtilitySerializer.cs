using System;
using System.Runtime.Serialization;
using UnityEngine;

namespace Yato.Persistence
{
    /// <summary>
    /// 默认序列化实现：UTF-8 文本 + Unity <see cref="JsonUtility"/>（D04-04）。
    /// 不引入第三方序列化依赖；无法序列化的类型需由游戏自行提供 <see cref="ISerializer"/>。
    /// </summary>
    public sealed class JsonUtilitySerializer : ISerializer
    {
        private readonly bool m_prettyPrint;

        public JsonUtilitySerializer(bool prettyPrint = true)
        {
            m_prettyPrint = prettyPrint;
        }

        public string Serialize<T>(T value)
        {
            if (object.ReferenceEquals(value, null))
            {
                throw new ArgumentNullException("value");
            }

            string json = JsonUtility.ToJson(value, m_prettyPrint);
            if (string.IsNullOrEmpty(json))
            {
                throw new SerializationException(string.Format(
                    "类型 {0} 无法被 JsonUtility 序列化（需要 [Serializable] 与公开字段）", typeof(T).FullName));
            }

            return json;
        }

        public T Deserialize<T>(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new SerializationException("内容为空，无法反序列化");
            }

            T value;
            try
            {
                value = JsonUtility.FromJson<T>(text);
            }
            catch (Exception e)
            {
                throw new SerializationException(string.Format(
                    "JSON 反序列化失败（{0}）", typeof(T).FullName), e);
            }

            if (object.ReferenceEquals(value, null) && !typeof(T).IsValueType)
            {
                throw new SerializationException(string.Format(
                    "JSON 反序列化结果为空（{0}），内容可能已损坏", typeof(T).FullName));
            }

            return value;
        }
    }
}
