using System;

namespace Yato.App
{
    /// <summary>场景操作结果：五态 + 具体阶段 + 上下文 + 错误。</summary>
    public sealed class SceneLoadResult
    {
        private SceneLoadResult()
        {
        }

        public SceneLoadStatus Status { get; private set; }

        public SceneLoadStage Stage { get; private set; }

        public SceneLoadRequest Request { get; private set; }

        /// <summary>成功时为加载后的上下文；卸载成功时为被卸载的上下文。</summary>
        public SceneContext Context { get; private set; }

        /// <summary>失败原因；Busy/Canceled/InvalidScene 通常为 null，说明见 <see cref="Message"/>。</summary>
        public Exception Error { get; private set; }

        /// <summary>可读说明（拒绝原因、取消原因等）。</summary>
        public string Message { get; private set; }

        public bool IsSuccess
        {
            get { return Status == SceneLoadStatus.Success; }
        }

        public string SceneName
        {
            get
            {
                if (Request != null)
                {
                    return Request.SceneName;
                }

                return Context != null ? Context.SceneName : string.Empty;
            }
        }

        public static SceneLoadResult Create(
            SceneLoadStatus status,
            SceneLoadStage stage,
            SceneLoadRequest request,
            SceneContext context,
            Exception error,
            string message)
        {
            return new SceneLoadResult
            {
                Status = status,
                Stage = stage,
                Request = request,
                Context = context,
                Error = error,
                Message = message
            };
        }

        public override string ToString()
        {
            return string.Format(
                "{0} at {1} : {2}{3}",
                Status,
                Stage,
                SceneName,
                string.IsNullOrEmpty(Message) ? string.Empty : " (" + Message + ")");
        }
    }
}
