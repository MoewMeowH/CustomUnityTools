using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace HB.PerfProbe
{
    /// <summary>
    /// 타입별 오브젝트 수 · 메모리 집계. 두 장을 비교해 늘어난 타입을 찾는 용도다 (PC 쪽 probe_report.py).
    /// <see cref="Take"/>는 모든 오브젝트를 훑어 무겁다 — 찍는 순간 끊김이 생긴다.
    /// </summary>
    internal sealed class Census
    {
        internal struct Entry
        {
            public string Type;
            public int Count;
            public long Bytes;
        }

        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<Entry> _entries = new List<Entry>();

        public int TotalObjects { get; private set; }
        public long TotalBytes { get; private set; }
        public IReadOnlyList<Entry> Entries => _entries;

        public void Add(string typeName, long bytes)
        {
            TotalObjects++;
            TotalBytes += bytes;
            if (_index.TryGetValue(typeName, out int i))
            {
                Entry e = _entries[i];
                e.Count++;
                e.Bytes += bytes;
                _entries[i] = e;
            }
            else
            {
                _index[typeName] = _entries.Count;
                _entries.Add(new Entry { Type = typeName, Count = 1, Bytes = bytes });
            }
        }

        /// <summary>메모리 많은 순, 같으면 개수 많은 순, 같으면 이름 순.</summary>
        public string ToJson(string time)
        {
            var sorted = new List<Entry>(_entries);
            sorted.Sort((a, b) =>
            {
                int c = b.Bytes.CompareTo(a.Bytes);
                if (c != 0) return c;
                c = b.Count.CompareTo(a.Count);
                return c != 0 ? c : string.CompareOrdinal(a.Type, b.Type);
            });

            var w = new ProbeJson(new System.Text.StringBuilder(64 * (sorted.Count + 4)));
            w.BeginObject()
                .Prop("time", time)
                .Prop("totalObjects", TotalObjects)
                .Prop("totalBytes", TotalBytes)
                .Key("types").BeginArray();
            foreach (Entry e in sorted)
                w.BeginObject().Prop("type", e.Type).Prop("count", e.Count).Prop("bytes", e.Bytes).EndObject();
            w.EndArray().EndObject();
            return w.ToString();
        }

        public static Census Take()
        {
            var census = new Census();
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            foreach (UnityEngine.Object o in all)
            {
                if (o == null) continue;
                census.Add(o.GetType().FullName, Profiler.GetRuntimeMemorySizeLong(o));
            }
            return census;
        }
    }
}
