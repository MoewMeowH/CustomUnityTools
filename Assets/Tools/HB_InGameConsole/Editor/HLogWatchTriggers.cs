#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace InGameConsoleSystem.Editor
{
    /// <summary>
    /// HLog Console 창의 Condition Triggers. 창이 열려 있지 않아도 에디터에서 Watch 값이 바뀔 때마다 검사한다
    /// (예전에는 창이 열려 있을 때만 검사해서, 창을 닫으면 조용히 꺼졌다). 목록은 PC별(EditorPrefs)로 저장된다.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class HLogWatchTriggers
    {
        // EditorPrefs는 PC의 모든 프로젝트가 같이 쓴다 → 프로젝트마다 키를 나눈다 (다른 프로젝트 트리거가 보이지 않게 발동하지 않도록)
        private const string LegacyPrefsKey = "HLog_WatchTriggers_Data";
        private static string PrefsTriggersKey => $"{LegacyPrefsKey}_{PlayerSettings.productGUID}";
        private static string LegacyImportedKey => $"{PrefsTriggersKey}_LegacyImported";

        private static readonly List<HLogConsoleWindow.WatchTrigger> s_Triggers = new List<HLogConsoleWindow.WatchTrigger>();

        /// <summary> 편집 가능한 트리거 목록 (바꾼 뒤 <see cref="Save"/>) </summary>
        public static List<HLogConsoleWindow.WatchTrigger> Triggers => s_Triggers;

        /// <summary> 트리거가 발동했을 때 (창 다시 그리기용) </summary>
        public static event Action Changed;

        static HLogWatchTriggers()
        {
            Load();
            HLogCore.OnWatchUpdated -= CheckTriggers;
            HLogCore.OnWatchUpdated += CheckTriggers;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            for (int i = 0; i < s_Triggers.Count; i++)
            {
                s_Triggers[i].hasTriggered = false;
            }
            Save();
            Changed?.Invoke();
        }

        private static void Load()
        {
            s_Triggers.Clear();
            s_Triggers.AddRange(Parse(EditorPrefs.GetString(PrefsTriggersKey, string.Empty)));
        }

        private static List<HLogConsoleWindow.WatchTrigger> Parse(string json)
        {
            var result = new List<HLogConsoleWindow.WatchTrigger>();
            if (string.IsNullOrEmpty(json)) return result;
            try
            {
                var container = JsonUtility.FromJson<HLogConsoleWindow.WatchTriggerContainer>(json);
                if (container != null && container.triggers != null)
                {
                    result.AddRange(container.triggers);
                }
            }
            catch
            {
                // 손상된 저장 값은 무시 (빈 목록)
            }
            return result;
        }

        /// <summary>
        /// 예전(PC 공용 키)에 저장된 트리거를 이 프로젝트로 한 번 가져온다. 창을 열 때만 불러서, 가져온 트리거가 창에 보인 뒤부터 검사되게 한다.
        /// 가져온 개수를 돌려준다 (이미 가져왔거나 이 프로젝트에 트리거가 있으면 0).
        /// </summary>
        public static int ImportLegacyOnce()
        {
            if (EditorPrefs.GetBool(LegacyImportedKey, false)) return 0;
            EditorPrefs.SetBool(LegacyImportedKey, true);
            if (s_Triggers.Count > 0) return 0;

            var legacy = Parse(EditorPrefs.GetString(LegacyPrefsKey, string.Empty));
            if (legacy.Count == 0) return 0;
            s_Triggers.AddRange(legacy);
            Save();
            return legacy.Count;
        }

        public static void Save()
        {
            try
            {
                var container = new HLogConsoleWindow.WatchTriggerContainer { triggers = s_Triggers };
                EditorPrefs.SetString(PrefsTriggersKey, JsonUtility.ToJson(container));
            }
            catch
            {
                // 저장 실패는 다음 저장에서 다시 시도
            }
        }

        private static void CheckTriggers(string key, string value)
        {
            for (int i = 0; i < s_Triggers.Count; i++)
            {
                var tr = s_Triggers[i];
                if (!tr.enabled) continue;
                if (tr.triggerOnce && tr.hasTriggered) continue;
                if (!KeyMatches(key, tr.watchKey)) continue;

                if (EvaluateTrigger(value, tr.op, tr.targetValue))
                {
                    tr.hasTriggered = true;
                    string opStr = GetOpSymbol(tr.op);
                    string conditionText = (tr.op == HLogConsoleWindow.TriggerOp.IsNull || tr.op == HLogConsoleWindow.TriggerOp.IsNotNull)
                        ? $"{key} ({value}) {opStr}"
                        : $"{key} ({value}) {opStr} '{tr.targetValue}'";

                    string reportPath = null;
                    if (tr.exportReport)
                    {
                        reportPath = ExportWatchSnapshotReport(conditionText, key, value);
                    }

                    string logMsg = tr.exportReport && !string.IsNullOrEmpty(reportPath)
                        ? $"[Watch Trigger] {conditionText} 조건 만족!\nSaved Report: {reportPath}"
                        : $"[Watch Trigger] {conditionText} 조건 만족!";

                    Debug.LogError(logMsg);

                    if (tr.pauseEditor)
                    {
                        Debug.Break();
                    }

                    Save();
                    Changed?.Invoke();
                }
            }
        }

        private static string ExportWatchSnapshotReport(string conditionText, string triggerKey, string triggerVal)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string reportsDir = Path.Combine(projectRoot, "Reports");
                if (!Directory.Exists(reportsDir))
                {
                    Directory.CreateDirectory(reportsDir);
                }

                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeKey = Regex.Replace(triggerKey, @"[^\w]", "_");
                if (safeKey.Length > 20) safeKey = safeKey.Substring(0, 20);
                string fileName = $"WatchReport_{safeKey}_{timeStamp}.txt";
                string fullPath = Path.Combine(reportsDir, fileName);

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine(" HLOG WATCH TRIGGER SNAPSHOT REPORT");
                sb.AppendLine("================================================================================");
                sb.AppendLine($"Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Trigger Key : {triggerKey}");
                sb.AppendLine($"Cur Value   : {triggerVal}");
                sb.AppendLine($"Condition   : {conditionText}");
                sb.AppendLine($"Unity Ver   : {Application.unityVersion}");
                sb.AppendLine($"Frame Count : {Time.frameCount}");
                sb.AppendLine($"Game Time   : {Time.time:F2}s (Realtime: {Time.realtimeSinceStartup:F2}s)");
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine(" ALL WATCH VALUES SNAPSHOT");
                sb.AppendLine("--------------------------------------------------------------------------------");
                var watches = HLogCore.Watches;
                if (watches.Count == 0)
                {
                    sb.AppendLine("  (No Watch variables registered)");
                }
                else
                {
                    foreach (var kvp in watches)
                    {
                        string mark = string.Equals(kvp.Key, triggerKey, StringComparison.OrdinalIgnoreCase) ? ">> " : "   ";
                        sb.AppendLine($"{mark}{kvp.Key,-30} : {kvp.Value.Value} (at {kvp.Value.Timestamp:HH:mm:ss})");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine(" RECENT LOGS (LAST 20 ENTRIES)");
                sb.AppendLine("--------------------------------------------------------------------------------");
                var history = HLogCore.History;
                int startIdx = Math.Max(0, history.Count - 20);
                for (int i = startIdx; i < history.Count; i++)
                {
                    var entry = history[i];
                    string tagStr = string.IsNullOrEmpty(entry.Tag) ? "" : $"[{entry.Tag}] ";
                    sb.AppendLine($"[{entry.TimeString}] [{entry.LogType}] {tagStr}{entry.Message}");
                }
                sb.AppendLine("================================================================================");

                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
                return fullPath;
            }
            catch (Exception ex)
            {
                return $"Failed to save report: {ex.Message}";
            }
        }

        /// <summary> 같은 키이거나, 한쪽이 "…/다른쪽"으로 끝나면 (채널 접두사 생략 허용). 문자열을 만들지 않고 비교한다 </summary>
        internal static bool KeyMatches(string key, string watchKey)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(watchKey)) return false;
            return string.Equals(key, watchKey, StringComparison.OrdinalIgnoreCase)
                   || EndsWithSegment(key, watchKey)
                   || EndsWithSegment(watchKey, key);
        }

        private static bool EndsWithSegment(string text, string suffix)
        {
            int start = text.Length - suffix.Length;
            return start > 0
                   && text[start - 1] == '/'
                   && string.Compare(text, start, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        internal static string GetOpSymbol(HLogConsoleWindow.TriggerOp op)
        {
            switch (op)
            {
                case HLogConsoleWindow.TriggerOp.GreaterThan: return ">";
                case HLogConsoleWindow.TriggerOp.LessThan: return "<";
                case HLogConsoleWindow.TriggerOp.Equals: return "==";
                case HLogConsoleWindow.TriggerOp.NotEquals: return "!=";
                case HLogConsoleWindow.TriggerOp.Contains: return "contains";
                case HLogConsoleWindow.TriggerOp.IsNull: return "is null";
                case HLogConsoleWindow.TriggerOp.IsNotNull: return "is not null";
                default: return "=";
            }
        }

        internal static bool EvaluateTrigger(string curVal, HLogConsoleWindow.TriggerOp op, string targetVal)
        {
            bool isCurNull = string.IsNullOrEmpty(curVal) ||
                             string.Equals(curVal, "null", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(curVal, "none", StringComparison.OrdinalIgnoreCase);

            if (op == HLogConsoleWindow.TriggerOp.IsNull) return isCurNull;
            if (op == HLogConsoleWindow.TriggerOp.IsNotNull) return !isCurNull;

            if (curVal == null) return false;

            // Target이 "null"을 검사하는 경우 (예: == "null", != "null")
            bool isTargetNull = string.IsNullOrEmpty(targetVal) ||
                                string.Equals(targetVal, "null", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetVal, "none", StringComparison.OrdinalIgnoreCase);

            if (isTargetNull)
            {
                if (op == HLogConsoleWindow.TriggerOp.Equals) return isCurNull;
                if (op == HLogConsoleWindow.TriggerOp.NotEquals) return !isCurNull;
            }

            // 1. 숫자 비교 시도
            if (double.TryParse(curVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numCur) &&
                double.TryParse(targetVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numTarget))
            {
                switch (op)
                {
                    case HLogConsoleWindow.TriggerOp.GreaterThan: return numCur > numTarget;
                    case HLogConsoleWindow.TriggerOp.LessThan: return numCur < numTarget;
                    case HLogConsoleWindow.TriggerOp.Equals: return Math.Abs(numCur - numTarget) < 0.0001;
                    case HLogConsoleWindow.TriggerOp.NotEquals: return Math.Abs(numCur - numTarget) >= 0.0001;
                    case HLogConsoleWindow.TriggerOp.Contains: return curVal.IndexOf(targetVal, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            // 2. 문자열 비교 폴백
            switch (op)
            {
                case HLogConsoleWindow.TriggerOp.Equals: return string.Equals(curVal, targetVal, StringComparison.OrdinalIgnoreCase);
                case HLogConsoleWindow.TriggerOp.NotEquals: return !string.Equals(curVal, targetVal, StringComparison.OrdinalIgnoreCase);
                case HLogConsoleWindow.TriggerOp.Contains: return curVal.IndexOf(targetVal, StringComparison.OrdinalIgnoreCase) >= 0;
                default: return false;
            }
        }
    }
}
#endif
