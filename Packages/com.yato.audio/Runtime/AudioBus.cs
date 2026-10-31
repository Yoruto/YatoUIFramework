namespace Yato.Audio
{
    /// <summary>
    /// 音频通道（D06）：默认 Master / Music / SFX / UI，Voice 为可扩展选项。
    /// <para>Music/SFX/UI/Voice 的实际音量 = Master × 通道音量。</para>
    /// </summary>
    public enum AudioBus
    {
        Master = 0,
        Music = 1,
        Sfx = 2,
        Ui = 3,
        Voice = 4
    }
}
