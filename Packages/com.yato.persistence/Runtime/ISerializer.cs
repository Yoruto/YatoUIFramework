namespace Yato.Persistence
{
    /// <summary>
    /// 文本序列化契约（D04-04）：默认 <see cref="JsonUtilitySerializer"/>，游戏可替换为其它实现。
    /// 版本号、兼容性与迁移由游戏自行负责，框架不添加统一 Envelope。
    /// </summary>
    public interface ISerializer
    {
        string Serialize<T>(T value);

        T Deserialize<T>(string text);
    }
}
