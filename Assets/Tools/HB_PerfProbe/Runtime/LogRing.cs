using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HB.PerfProbe
{
    /// <summary>
    /// 최근 로그 N줄. Application.logMessageReceivedThreaded는 다른 스레드에서도 오므로 잠근다.
    /// 오류 · 예외만 스택 트레이스를 같이 남긴다.
    /// </summary>
    internal sealed class LogRing
    {
        readonly Ring<string> _lines;
        readonly object _lock = new object();

        public LogRing(int capacity)
        {
            _lines = new Ring<string>(capacity);
        }

        public void Attach() => Application.logMessageReceivedThreaded += OnLog;
        public void Detach() => Application.logMessageReceivedThreaded -= OnLog;

        void OnLog(string condition, string stackTrace, LogType type)
        {
            Add(DateTime.Now, condition, stackTrace, type);
        }

        internal void Add(DateTime time, string condition, string stackTrace, LogType type)
        {
            string head = time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + type + "] " + condition;
            bool withStack = (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !string.IsNullOrEmpty(stackTrace);
            string line = withStack ? head + "\n" + stackTrace.TrimEnd() : head;
            lock (_lock) _lines.Add(line);
        }

        public void WriteTo(string path)
        {
            using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            {
                lock (_lock)
                {
                    for (int i = 0; i < _lines.Count; i++) writer.WriteLine(_lines[i]);
                }
            }
        }
    }
}
