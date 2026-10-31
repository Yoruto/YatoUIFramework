using System.Collections.Generic;

namespace Yato.Resources
{
    /// <summary>
    /// 句柄创建堆栈追踪（仅编辑器）。退出 Play 时由 LeakReportWindow 汇总未释放句柄（T-P4-06）。
    /// </summary>
    public static class ResourceTracker
    {
        public sealed class HandleRecord
        {
            public int Id;
            public string Key;
            public string StackTrace;
            public string CreatedAt;
        }

        private static readonly Dictionary<int, HandleRecord> s_records =
            new Dictionary<int, HandleRecord>();

        private static int s_nextId = 1;

        public static int Count
        {
            get { return s_records.Count; }
        }

        /// <summary>登记一个句柄，返回追踪 id（非编辑器返回 0）。</summary>
        public static int Track(string key)
        {
#if UNITY_EDITOR
            int id = s_nextId++;
            s_records[id] = new HandleRecord
            {
                Id = id,
                Key = key,
                StackTrace = new System.Diagnostics.StackTrace(2, true).ToString(),
                CreatedAt = System.DateTime.Now.ToString("HH:mm:ss.fff")
            };
            return id;
#else
            return 0;
#endif
        }

        public static void Untrack(int id)
        {
            if (id <= 0)
            {
                return;
            }

            s_records.Remove(id);
        }

        /// <summary>当前未释放的句柄快照。</summary>
        public static List<HandleRecord> Snapshot()
        {
            return new List<HandleRecord>(s_records.Values);
        }

        public static void Clear()
        {
            s_records.Clear();
        }
    }
}
