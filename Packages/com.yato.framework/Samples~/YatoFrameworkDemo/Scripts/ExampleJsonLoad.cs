using UnityEngine;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// JSON 读取示例。框架不再绑定任何 JSON 实现（已移除 LitJson），
    /// 示例使用 Unity 自带的 JsonUtility；需要复杂特性时请自行引入 Newtonsoft。
    /// </summary>
    public class ExampleJsonLoad : MonoBehaviour
    {
        private void Start()
        {
            TextAsset json = Resources.Load<TextAsset>("player");
            if (json == null)
            {
                Debug.LogWarning("[Demo] 未找到 Resources/player.json");
                return;
            }

            var data = JsonUtility.FromJson<Data>(json.text);
            if (data == null || data.Player == null)
            {
                Debug.LogWarning("[Demo] player.json 解析失败");
                return;
            }

            for (int i = 0; i < data.Player.Length; i++)
            {
                PlayerData item = data.Player[i];
                Debug.LogFormat("ID:{0}  Name:{1}  Job:{2}", item.id, item.name, item.job);
            }
        }
    }

    [System.Serializable]
    public class PlayerData
    {
        public int id;
        public string name;
        public int job;
    }

    [System.Serializable]
    public class Data
    {
        public PlayerData[] Player;
    }
}
