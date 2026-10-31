using System.IO;

namespace Yato.Persistence
{
    /// <summary>同一路径已有文件操作在进行；按 D04 立即失败，不排队。</summary>
    public sealed class FileBusyException : IOException
    {
        public FileBusyException(string fullPath)
            : base(string.Format("同一路径的文件操作正在进行：{0}", fullPath))
        {
            FullPath = fullPath;
        }

        public string FullPath { get; private set; }
    }
}
