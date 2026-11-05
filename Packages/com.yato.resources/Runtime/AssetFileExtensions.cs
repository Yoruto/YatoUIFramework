namespace Yato.Resources
{
    /// <summary>资源 key 路径解析和 Bundle 收集共用的文件后缀。</summary>
    public static class AssetFileExtensions
    {
        public static readonly string[] Buildable =
        {
            ".prefab", ".asset", ".mat", ".shader", ".compute",
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".exr",
            ".wav", ".mp3", ".ogg",
            ".txt", ".json", ".bytes", ".csv",
            ".anim", ".controller", ".spriteatlas", ".spriteatlasv2",
            ".unity", ".ttf", ".otf", ".shadervariants"
        };
    }
}
