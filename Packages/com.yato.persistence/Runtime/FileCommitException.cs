using System;
using System.IO;

namespace Yato.Persistence
{
    /// <summary>
    /// 临时文件已写完但提交失败。按 D04 必须报告真实结果，
    /// 不会静默降级为直接覆盖已存在的目标文件。
    /// </summary>
    public sealed class FileCommitException : IOException
    {
        public FileCommitException(string destinationPath, Exception innerException)
            : base(string.Format("文件提交失败（未降级为直接覆盖）：{0}", destinationPath), innerException)
        {
            DestinationPath = destinationPath;
        }

        public string DestinationPath { get; private set; }
    }
}
