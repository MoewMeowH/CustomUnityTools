#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HB.Cheat
{
    /// <summary>
    /// 등록된 치트 목록. 생성 레지스트리가 SubsystemRegistration에서 Register를 호출한다.
    /// 같은 Id는 교체하므로 (도메인 리로드 없이 Play를 다시 눌러도) 중복되지 않는다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class CheatRegistry
    {
        private static readonly Dictionary<string, CheatDefinition> s_ById = new Dictionary<string, CheatDefinition>(StringComparer.Ordinal);
        private static readonly List<CheatDefinition> s_Sorted = new List<CheatDefinition>();
        private static bool s_Dirty;

        public static event Action Changed;

        public static int Count => s_ById.Count;

        public static void Register(CheatDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                Debug.LogError("[HB_Cheat] Register: definition or Id is empty.");
                return;
            }

            if (s_ById.TryGetValue(definition.Id, out var existing) && existing.Path != definition.Path)
            {
                Debug.LogWarning($"[HB_Cheat] Duplicate cheat id '{definition.Id}' ('{existing.Path}' → '{definition.Path}'). The last one wins.");
            }

            s_ById[definition.Id] = definition;
            s_Dirty = true;
            RaiseChanged();
        }

        /// <summary>테스트가 등록한 정의를 지운다</summary>
        internal static void Unregister(string id)
        {
            if (id == null || !s_ById.Remove(id)) return;
            s_Dirty = true;
            RaiseChanged();
        }

        // 구독자(패널 등) 하나의 예외가 등록 · 해제를 끊지 않도록 구독자마다 격리한다 (CheatSystem 이벤트와 같은 규칙)
        private static void RaiseChanged()
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public static bool TryGet(string id, out CheatDefinition definition)
        {
            if (id == null)
            {
                definition = null;
                return false;
            }
            return s_ById.TryGetValue(id, out definition);
        }

        /// <summary>경로 → Order → Id 순으로 정렬된 목록</summary>
        public static IReadOnlyList<CheatDefinition> All
        {
            get
            {
                if (s_Dirty)
                {
                    s_Sorted.Clear();
                    s_Sorted.AddRange(s_ById.Values);
                    s_Sorted.Sort(Compare);
                    s_Dirty = false;
                }
                return s_Sorted;
            }
        }

        // (부모 경로, Order, 경로, Id) 순. Order는 같은 탭 안에서만 의미가 있다.
        private static int Compare(CheatDefinition a, CheatDefinition b)
        {
            int c = string.CompareOrdinal(ParentOf(a.Path), ParentOf(b.Path));
            if (c != 0) return c;
            c = a.Order.CompareTo(b.Order);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Path, b.Path);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        private static string ParentOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : string.Empty;
        }
    }
}
