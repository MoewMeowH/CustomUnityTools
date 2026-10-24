#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HB.Cheat
{
    /// <summary>
    /// 인자 후보 목록 (테이블 조회 등). 메인 스레드에서 호출된다.
    /// 치트를 고를 때(패널) · 실행할 때 · CLI로 후보를 조회할 때 다시 수집하고, 같은 치트를 편집하는 동안은 결과를 캐시한다 (의존 인자 값이 바뀌면 다시 수집).
    /// </summary>
    public interface ICheatOptionProvider
    {
        /// <summary>results에 후보를 채운다. Value는 인자 타입과 같아야 한다 (int 인자면 int).</summary>
        void Collect(CheatOptionContext context, List<CheatOption> results);
    }

    /// <summary>Provider에 넘기는 현재 선택 상태</summary>
    public readonly struct CheatOptionContext
    {
        private readonly object[] m_Values;

        public CheatDefinition Definition { get; }
        public int ArgIndex { get; }

        public CheatOptionContext(CheatDefinition definition, int argIndex, object[] values)
        {
            Definition = definition;
            ArgIndex = argIndex;
            m_Values = values;
        }

        /// <summary>앞 인자 값. 이름이 없거나 타입이 다르면 false.</summary>
        public bool TryGet<T>(string argName, out T value)
        {
            int index = Definition.IndexOfArg(argName);
            if (index >= 0 && m_Values != null && index < m_Values.Length && m_Values[index] is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public T Get<T>(string argName) => TryGet(argName, out T value) ? value : default;
    }

    /// <summary>
    /// Provider 결과 캐시. 키: (치트, 인자) / 무효화: 의존 인자 값, Invalidate(치트 선택 · 실행 · CLI 조회), Clear.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [AutoStaticsCleanup]
#endif
    internal static partial class CheatOptionCache
    {
        private sealed class Entry
        {
            public object[] DependencyValues;
            public readonly List<CheatOption> Options = new List<CheatOption>();
            public string Error;
        }

        private static readonly Dictionary<(string, int), Entry> s_Entries = new Dictionary<(string, int), Entry>();

        public static IReadOnlyList<CheatOption> Get(CheatDefinition def, int argIndex, object[] values, out string error)
        {
            var arg = def.Args[argIndex];
            error = null;

            if (arg.Provider == null)
                return (IReadOnlyList<CheatOption>)arg.FixedOptions ?? Array.Empty<CheatOption>();

            var key = (def.Id, argIndex);
            if (!s_Entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                s_Entries.Add(key, entry);
            }
            else if (entry.DependencyValues != null && SameDependencies(arg, values, entry.DependencyValues))
            {
                error = entry.Error;
                return entry.Options;
            }

            entry.DependencyValues = CaptureDependencies(arg, values);
            entry.Options.Clear();
            entry.Error = null;
            try
            {
                arg.Provider.Collect(new CheatOptionContext(def, argIndex, values), entry.Options);
            }
            catch (Exception e)
            {
                entry.Options.Clear();
                entry.Error = $"{arg.Provider.GetType().Name}: {e.Message}";
                Debug.LogException(e);
            }

            error = entry.Error;
            return entry.Options;
        }

        public static void Clear() => s_Entries.Clear();

        private static readonly List<(string, int)> s_RemoveBuffer = new List<(string, int)>();

        /// <summary>이 치트의 후보 캐시만 버린다 (다음 Get에서 다시 수집).</summary>
        public static void Invalidate(string cheatId)
        {
            if (cheatId == null) return;
            s_RemoveBuffer.Clear();
            foreach (var key in s_Entries.Keys)
            {
                if (key.Item1 == cheatId) s_RemoveBuffer.Add(key);
            }
            foreach (var key in s_RemoveBuffer) s_Entries.Remove(key);
            s_RemoveBuffer.Clear();
        }

        private static object[] CaptureDependencies(CheatArgDefinition arg, object[] values)
        {
            var captured = new object[arg.DependsOn.Length];
            for (int i = 0; i < captured.Length; i++)
            {
                int index = arg.DependsOn[i];
                captured[i] = values != null && index < values.Length ? values[index] : null;
            }
            return captured;
        }

        private static bool SameDependencies(CheatArgDefinition arg, object[] values, object[] captured)
        {
            if (captured.Length != arg.DependsOn.Length) return false;
            for (int i = 0; i < captured.Length; i++)
            {
                int index = arg.DependsOn[i];
                object current = values != null && index < values.Length ? values[index] : null;
                if (!Equals(current, captured[i])) return false;
            }
            return true;
        }
    }
}
