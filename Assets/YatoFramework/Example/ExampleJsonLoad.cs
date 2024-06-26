using LitJson;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YatoUIFramework
{

    public class ExampleJsonLoad : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
            //加载资源
            TextAsset obj = Resources.Load<TextAsset>("player");
            if (obj != null)
            {
                //使用JsonMapper.ToObject解析
                Data m_Data = JsonMapper.ToObject<Data>(obj.text);
                //打印
                foreach (PlayerData item in m_Data.Player)
                {
                    Debug.Log("ID:" + item.id + "  Name:" + item.name + "  Job:" + item.job);

                }
            }
        }


        // Update is called once per frame
        void Update()
        {

        }
    }

    [System.Serializable]
    public class PlayerData
    {
        //玩家ID
        public int id;
        //玩家名字
        public string name;
        //玩家职业
        public int job;
    }
    [System.Serializable]
    public class Data
    {
        public PlayerData[] Player;
    }
}

