#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace InGameConsoleSystem.Editor
{
    public class HLogConsoleWindow : EditorWindow
    {
        private enum ConsoleTab
        {
            Logs,
            Watch
        }

        public enum TriggerOp
        {
            GreaterThan, // >
            LessThan,    // <
            Equals,      // ==
            NotEquals,   // !=
            Contains,    // contains
            IsNull,      // is null
            IsNotNull    // is not null
        }

        [Serializable]
        public class WatchTrigger
        {
            public bool enabled = true;
            public string watchKey = "";
            public TriggerOp op = TriggerOp.GreaterThan;
            public string targetValue = "10";
            public bool pauseEditor = true;
            public bool triggerOnce = true;
            public bool exportReport = true;
            public bool hasTriggered = false;

            public WatchTrigger() { }

            public WatchTrigger(string key)
            {
                watchKey = key;
                enabled = true;
                op = TriggerOp.GreaterThan;
                targetValue = "10";
                pauseEditor = true;
                triggerOnce = true;
                exportReport = true;
                hasTriggered = false;
            }
        }

        [Serializable]
        private class WatchTriggerContainer
        {
            public List<WatchTrigger> triggers = new List<WatchTrigger>();
        }

        private ConsoleTab currentTab = ConsoleTab.Logs;

        // Log Level Filter
        private bool showInfo = true;
        private bool showWarning = true;
        private bool showError = true;
        private bool autoScroll = true;
        // 맨 아래를 보고 있는지 여부. 위로 스크롤하면 false가 되어 새 로그를 따라가지 않는다
        private bool isStickToBottom = true;
        private bool isScrollToBottomPending = true;
        private float logContentHeight;
        private float logViewHeight;
        private const float BottomTolerance = 4f;
        private bool pauseOnError = false;

        // Search & Channel Filter
        private string searchQuery = string.Empty;
        private readonly HashSet<string> subscribedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> disabledTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // GUI State
        private Vector2 logScroll;
        private Vector2 detailScroll;
        private Vector2 watchScroll;
        private Vector2 tagBarScroll;
        private float detailPanelHeight = 160f;
        private bool isResizingDetail = false;

        // 상세 패널에 보이는 줄 (마지막에 클릭한 줄)
        private HLogCore.LogEntry selectedEntry;
        // 다중 선택 (Ctrl/Cmd+클릭 · Shift+클릭 · Ctrl/Cmd+A) → Ctrl/Cmd+C로 한꺼번에 복사
        private readonly HashSet<HLogCore.LogEntry> selectedEntries = new HashSet<HLogCore.LogEntry>();
        // UpdateFilteredLogs의 선택 정리용 (재사용)
        private readonly List<HLogCore.LogEntry> visibleSelectedBuffer = new List<HLogCore.LogEntry>();
        private HLogCore.LogEntry selectionAnchor;
        private readonly List<HLogCore.LogEntry> filteredLogs = new List<HLogCore.LogEntry>();
        private bool isDirty = true;

        // Watch 탭은 리페인트마다 그리므로, 값이 바뀌었을 때만 코어 스냅샷(복사)을 다시 가져온다
        private IReadOnlyDictionary<string, HLogCore.WatchEntry> cachedWatches;
        private bool isWatchesDirty = true;

        // Watch Triggers
        private int watchSubTab = 0; // 0: Watch Values, 1: Condition Triggers
        private readonly List<WatchTrigger> watchTriggers = new List<WatchTrigger>();
        private const string PrefsTriggersKey = "HLog_WatchTriggers_Data";

        private static readonly Regex PatternCompileError = new Regex(@"([a-zA-Z0-9_\-\.\/\\:]+\.cs)\s*\(([0-9]+)(?:,\s*[0-9]+)?\)", RegexOptions.Compiled);
        private static readonly Regex PatternAt = new Regex(@"\(at (.+):(\d+)\)", RegexOptions.Compiled);
        private static readonly Regex PatternInLine = new Regex(@"in (.+):line (\d+)", RegexOptions.Compiled);
        private static readonly Regex PatternFileColon = new Regex(@"([a-zA-Z0-9_\-\.\/\\:]+\.cs):(\d+)", RegexOptions.Compiled);

        #region Scripting Define Symbols Helper

        public static bool IsScriptingDefineSymbolDefined(string symbol)
        {
            try
            {
                var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2023_1_OR_NEWER
                var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                string defines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
#else
                string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#endif
                var list = new HashSet<string>(defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
                return list.Contains(symbol);
            }
            catch
            {
                return false;
            }
        }

        public static void SetScriptingDefineSymbol(string symbol, bool enable)
        {
            try
            {
                var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2023_1_OR_NEWER
                var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                string defines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
#else
                string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#endif
                var list = new HashSet<string>(defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
                if (enable) list.Add(symbol);
                else list.Remove(symbol);

                string newDefines = string.Join(";", list);
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HLog] Failed to update Scripting Define Symbols: {ex.Message}");
            }
        }

        #endregion

        [MenuItem("Tools/HB/In-Game Console/Console Window", priority = 90)]
        [MenuItem("Window/General/HLog Console", priority = 200)]
        public static void OpenWindow()
        {
            var window = GetWindow<HLogConsoleWindow>("HLog Console");
            window.minSize = new Vector2(560, 400);
            window.Show();
        }

        private void OnEnable()
        {
            HLogCore.Initialize();
            HLogCore.DiscoverAllChannels();
            LoadWatchTriggers();

            HLogCore.OnLogAdded += OnLogAdded;
            HLogCore.OnLogsCleared += OnLogsCleared;
            HLogCore.OnWatchUpdated += OnWatchUpdated;
            HLogCore.OnWatchRemoved += OnWatchRemoved;
            HLogCore.OnTagDiscovered += OnTagDiscovered;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            isDirty = true;
        }

        private void OnDisable()
        {
            SaveWatchTriggers();

            HLogCore.OnLogAdded -= OnLogAdded;
            HLogCore.OnLogsCleared -= OnLogsCleared;
            HLogCore.OnWatchUpdated -= OnWatchUpdated;
            HLogCore.OnWatchRemoved -= OnWatchRemoved;
            HLogCore.OnTagDiscovered -= OnTagDiscovered;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            // 플레이 진입 때 코어 정적 상태가 초기화되므로(도메인 리로드를 꺼도) 창의 목록 캐시도 버린다
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                isDirty = true;
                isWatchesDirty = true;
                Repaint();
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                for (int i = 0; i < watchTriggers.Count; i++)
                {
                    watchTriggers[i].hasTriggered = false;
                }
                SaveWatchTriggers();
                Repaint();
            }
        }

        private void LoadWatchTriggers()
        {
            watchTriggers.Clear();
            string json = EditorPrefs.GetString(PrefsTriggersKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var container = JsonUtility.FromJson<WatchTriggerContainer>(json);
                    if (container != null && container.triggers != null)
                    {
                        watchTriggers.AddRange(container.triggers);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private void SaveWatchTriggers()
        {
            try
            {
                var container = new WatchTriggerContainer { triggers = watchTriggers };
                string json = JsonUtility.ToJson(container);
                EditorPrefs.SetString(PrefsTriggersKey, json);
            }
            catch
            {
                // ignored
            }
        }

        private void OnLogAdded(HLogCore.LogEntry entry)
        {
            if (pauseOnError && (entry.LogType == LogType.Error || entry.LogType == LogType.Exception))
            {
                Debug.Break();
            }

            isDirty = true;
            if (autoScroll && isStickToBottom)
                isScrollToBottomPending = true;
            Repaint();
        }

        private void OnLogsCleared()
        {
            isWatchesDirty = true;
            isStickToBottom = true;
            isScrollToBottomPending = true;
            selectedEntry = null;
            selectedEntries.Clear();
            selectionAnchor = null;
            isDirty = true;
            Repaint();
        }

        private void OnWatchRemoved(string key)
        {
            isWatchesDirty = true;
            if (currentTab == ConsoleTab.Watch)
            {
                Repaint();
            }
        }

        private void OnWatchUpdated(string key, string value)
        {
            isWatchesDirty = true;
            CheckWatchTriggers(key, value);

            if (currentTab == ConsoleTab.Watch)
            {
                Repaint();
            }
        }

        private void CheckWatchTriggers(string key, string value)
        {
            for (int i = 0; i < watchTriggers.Count; i++)
            {
                var tr = watchTriggers[i];
                if (!tr.enabled) continue;
                if (tr.triggerOnce && tr.hasTriggered) continue;
                bool keyMatches = string.Equals(tr.watchKey, key, StringComparison.OrdinalIgnoreCase)
                                  || key.EndsWith("/" + tr.watchKey, StringComparison.OrdinalIgnoreCase)
                                  || tr.watchKey.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase);
                if (!keyMatches) continue;

                if (EvaluateTrigger(value, tr.op, tr.targetValue))
                {
                    tr.hasTriggered = true;
                    string opStr = GetOpSymbol(tr.op);
                    string conditionText = (tr.op == TriggerOp.IsNull || tr.op == TriggerOp.IsNotNull)
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

                    SaveWatchTriggers();
                    Repaint();
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

        private static string GetOpSymbol(TriggerOp op)
        {
            switch (op)
            {
                case TriggerOp.GreaterThan: return ">";
                case TriggerOp.LessThan: return "<";
                case TriggerOp.Equals: return "==";
                case TriggerOp.NotEquals: return "!=";
                case TriggerOp.Contains: return "contains";
                case TriggerOp.IsNull: return "is null";
                case TriggerOp.IsNotNull: return "is not null";
                default: return "=";
            }
        }

        private static bool EvaluateTrigger(string curVal, TriggerOp op, string targetVal)
        {
            bool isCurNull = string.IsNullOrEmpty(curVal) ||
                             string.Equals(curVal, "null", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(curVal, "none", StringComparison.OrdinalIgnoreCase);

            if (op == TriggerOp.IsNull) return isCurNull;
            if (op == TriggerOp.IsNotNull) return !isCurNull;

            if (curVal == null) return false;

            // Target이 "null"을 검사하는 경우 (예: == "null", != "null")
            bool isTargetNull = string.IsNullOrEmpty(targetVal) ||
                                string.Equals(targetVal, "null", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(targetVal, "none", StringComparison.OrdinalIgnoreCase);

            if (isTargetNull)
            {
                if (op == TriggerOp.Equals) return isCurNull;
                if (op == TriggerOp.NotEquals) return !isCurNull;
            }

            // 1. 숫자 비교 시도
            if (double.TryParse(curVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numCur) &&
                double.TryParse(targetVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numTarget))
            {
                switch (op)
                {
                    case TriggerOp.GreaterThan: return numCur > numTarget;
                    case TriggerOp.LessThan: return numCur < numTarget;
                    case TriggerOp.Equals: return Math.Abs(numCur - numTarget) < 0.0001;
                    case TriggerOp.NotEquals: return Math.Abs(numCur - numTarget) >= 0.0001;
                    case TriggerOp.Contains: return curVal.IndexOf(targetVal, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            // 2. 문자열 비교 폴백
            switch (op)
            {
                case TriggerOp.Equals: return string.Equals(curVal, targetVal, StringComparison.OrdinalIgnoreCase);
                case TriggerOp.NotEquals: return !string.Equals(curVal, targetVal, StringComparison.OrdinalIgnoreCase);
                case TriggerOp.Contains: return curVal.IndexOf(targetVal, StringComparison.OrdinalIgnoreCase) >= 0;
                default: return false;
            }
        }

        private void OnTagDiscovered(string tag)
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (isDirty)
            {
                UpdateFilteredLogs();
                isDirty = false;
            }

            if (currentTab == ConsoleTab.Logs) HandleSelectionCommands();

            DrawMainToolbar();
#if !USE_HLOG
            EditorGUILayout.HelpBox(
                $"{LogChannelGeneratorSettings.ConditionalSymbol} 심볼이 꺼져 있어 HLog 수집 · Watch가 동작하지 않습니다 (에디터 포함). " +
                "툴바의 심볼 버튼으로 켜세요.", MessageType.Warning);
#endif
            DrawSubToolbar();

            if (currentTab == ConsoleTab.Logs)
            {
                DrawLogsView();
                DrawDetailSplitter();
                DrawDetailPanel();
            }
            else
            {
                DrawWatchView();
            }
        }

        private void UpdateFilteredLogs()
        {
            filteredLogs.Clear();
            var history = HLogCore.History;

            // 검색어는 한 번만 해석한다 ("!단어"는 제외 검색)
            bool isExclude = searchQuery.Length > 1 && searchQuery[0] == '!';
            string term = isExclude ? searchQuery.Substring(1) : searchQuery;

            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];

                // 1. Level Filter
                if (entry.LogType == LogType.Log && !showInfo) continue;
                if (entry.LogType == LogType.Warning && !showWarning) continue;
                if ((entry.LogType == LogType.Error || entry.LogType == LogType.Exception || entry.LogType == LogType.Assert) && !showError) continue;

                // 2. Tag Filter (Multi-Channel Subscription)
                if (!string.IsNullOrEmpty(entry.Tag) && disabledTags.Contains(entry.Tag)) continue;
                if (subscribedTags.Count > 0)
                {
                    if (string.IsNullOrEmpty(entry.Tag))
                    {
                        if (!subscribedTags.Contains("[Untagged]"))
                            continue;
                    }
                    else
                    {
                        if (!subscribedTags.Contains(entry.Tag))
                            continue;
                    }
                }

                // 3. Search Query Filter
                if (!string.IsNullOrEmpty(term))
                {
                    bool contains = entry.RawMessage.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isExclude ? contains : !contains)
                        continue;
                }

                filteredLogs.Add(entry);
            }

            // 필터로 가려지거나 기록에서 밀려난 줄은 선택 · 기준에서 뺀다 (보이지 않는 줄이 복사되지 않도록).
            // 로그가 들어올 때마다 도는 곳이라, 선택이 실제로 줄었을 때만 정리한다
            if (selectedEntries.Count > 0 || selectionAnchor != null)
            {
                // 기록이 가득 찬 채 전체 선택하면 새 로그마다 줄어든다 → 정리용 목록은 재사용한다 (할당 · 람다 없음)
                visibleSelectedBuffer.Clear();
                bool anchorVisible = false;
                for (int i = 0; i < filteredLogs.Count; i++)
                {
                    var entry = filteredLogs[i];
                    if (selectedEntries.Contains(entry)) visibleSelectedBuffer.Add(entry);
                    if (ReferenceEquals(entry, selectionAnchor)) anchorVisible = true;
                }

                if (!anchorVisible) selectionAnchor = null;
                if (visibleSelectedBuffer.Count != selectedEntries.Count)
                {
                    // HashSet은 Clear 뒤에도 용량을 유지한다
                    selectedEntries.Clear();
                    for (int i = 0; i < visibleSelectedBuffer.Count; i++) selectedEntries.Add(visibleSelectedBuffer[i]);
                }
                visibleSelectedBuffer.Clear();
            }
        }

        #region Selection

        private void HandleSelectionCommands()
        {
            var e = Event.current;
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;
            // 검색창 · 상세 패널 텍스트에 포커스가 있으면 그쪽 복사 · 전체 선택을 그대로 둔다
            if (GUIUtility.keyboardControl != 0) return;

            bool execute = e.type == EventType.ExecuteCommand;
            if (e.commandName == "Copy" && selectedEntries.Count > 0)
            {
                if (execute) CopySelection(withStack: false);
                e.Use();
            }
            else if (e.commandName == "SelectAll" && filteredLogs.Count > 0)
            {
                if (execute)
                {
                    selectedEntries.Clear();
                    selectedEntries.UnionWith(filteredLogs);
                    selectionAnchor = filteredLogs[0];
                    Repaint();
                }
                e.Use();
            }
        }

        private void CopySelection(bool withStack)
        {
            var entries = HLogConsoleSelection.InDisplayOrder(filteredLogs, selectedEntries);
            if (entries.Count == 0) return;
            EditorGUIUtility.systemCopyBuffer = HLogConsoleSelection.Format(entries, withStack);
        }

        private void ClearSelection()
        {
            selectedEntries.Clear();
            selectionAnchor = null;
            Repaint();
        }

        /// <param name="range">Shift</param>
        /// <param name="toggle">Ctrl(맥 Cmd) — EditorGUI.actionKey</param>
        private void OnRowClicked(HLogCore.LogEntry entry, int index, bool range, bool toggle)
        {
            // 줄을 누르면 검색창 등에서 포커스를 빼서 Ctrl/Cmd+C가 줄 복사로 가게 한다
            GUIUtility.keyboardControl = 0;

            if (range && selectionAnchor != null)
            {
                // 기준 줄이 가려졌거나 밀려났으면 누른 줄을 새 기준으로 (다음 Shift+클릭부터 범위가 된다)
                if (!HLogConsoleSelection.SelectRange(filteredLogs, selectionAnchor, index, selectedEntries))
                    selectionAnchor = entry;
            }
            else if (toggle)
            {
                selectionAnchor = entry;
                // 뺀 줄은 상세 패널에 새로 띄우지 않는다
                if (selectedEntries.Remove(entry)) return;
                selectedEntries.Add(entry);
            }
            else
            {
                selectedEntries.Clear();
                selectedEntries.Add(entry);
                selectionAnchor = entry;
            }
            selectedEntry = entry;
        }

        private void ShowRowContextMenu(HLogCore.LogEntry entry)
        {
            // 선택 밖의 줄을 오른쪽 클릭하면 그 줄만 선택
            if (!selectedEntries.Contains(entry))
            {
                selectedEntries.Clear();
                selectedEntries.Add(entry);
                selectionAnchor = entry;
                selectedEntry = entry;
            }

            int count = selectedEntries.Count;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"선택 복사 ({count})"), false, () => CopySelection(withStack: false));
            menu.AddItem(new GUIContent($"선택 복사 - 스택 포함 ({count})"), false, () => CopySelection(withStack: true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("선택 해제"), false, ClearSelection);
            menu.ShowAsContext();
            Repaint();
        }

        #endregion

        #region Toolbar Drawing

        private void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                HLogCore.Clear();
            }

            GUILayout.Space(6);

            // Tab Buttons
            bool isLogs = currentTab == ConsoleTab.Logs;
            bool isWatch = currentTab == ConsoleTab.Watch;

            if (GUILayout.Toggle(isLogs, "Logs", EditorStyles.toolbarButton, GUILayout.Width(50)) != isLogs)
            {
                currentTab = ConsoleTab.Logs;
            }

            int watchCount = HLogCore.WatchCount;
            int triggerCount = watchTriggers.Count;
            string watchLabel = triggerCount > 0 ? $"Watch [{watchCount} / Triggers: {triggerCount}]" : $"Watch [{watchCount}]";
            if (GUILayout.Toggle(isWatch, watchLabel, EditorStyles.toolbarButton, GUILayout.Width(140)) != isWatch)
            {
                currentTab = ConsoleTab.Watch;
            }

            GUILayout.Space(10);

            // Options
            bool newAutoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", EditorStyles.toolbarButton, GUILayout.Width(75));
            if (newAutoScroll && !autoScroll)
            {
                isStickToBottom = true;
                isScrollToBottomPending = true;
            }
            autoScroll = newAutoScroll;
            pauseOnError = GUILayout.Toggle(pauseOnError, "Pause on Error", EditorStyles.toolbarButton, GUILayout.Width(95));

            GUILayout.Space(6);

            // Log Enable Toggle (Runtime & Editor)
            bool prevLogEnable = HLogCore.IsLogEnabled;
            GUI.color = prevLogEnable ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.45f);
            if (GUILayout.Button(prevLogEnable ? "Log: ON" : "Log: OFF", EditorStyles.toolbarButton, GUILayout.Width(66)))
            {
                HLogCore.IsLogEnabled = !prevLogEnable;
            }
            GUI.color = Color.white;

            // Scripting Define Symbol Toggle
            string condSymbol = LogChannelGeneratorSettings.ConditionalSymbol;
            bool isSymbolDefined = IsScriptingDefineSymbolDefined(condSymbol);
            GUI.color = isSymbolDefined ? new Color(0.35f, 0.8f, 1f) : new Color(0.7f, 0.7f, 0.7f);
            if (GUILayout.Button(isSymbolDefined ? $"{condSymbol}: ON" : $"{condSymbol}: OFF", EditorStyles.toolbarButton, GUILayout.Width(105)))
            {
                SetScriptingDefineSymbol(condSymbol, !isSymbolDefined);
            }
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();

            // Log Count Stats & Level Toggles
            HLogCore.GetLogTypeCounts(out int infoCount, out int warnCount, out int errCount);

            EditorGUI.BeginChangeCheck();
            showInfo = GUILayout.Toggle(showInfo, $"Info: {infoCount}", EditorStyles.toolbarButton, GUILayout.Width(75));
            showWarning = GUILayout.Toggle(showWarning, $"Warn: {warnCount}", EditorStyles.toolbarButton, GUILayout.Width(75));
            showError = GUILayout.Toggle(showError, $"Error: {errCount}", EditorStyles.toolbarButton, GUILayout.Width(75));
            if (EditorGUI.EndChangeCheck())
            {
                isDirty = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSubToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Search Field
            EditorGUI.BeginChangeCheck();
            searchQuery = EditorGUILayout.TextField(searchQuery, EditorStyles.toolbarSearchField, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck())
            {
                isDirty = true;
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                if (GUILayout.Button(GUIContent.none, "ToolbarSearchCancelButton"))
                {
                    searchQuery = string.Empty;
                    GUIUtility.keyboardControl = 0;
                    isDirty = true;
                }
            }

            GUILayout.Space(8);

            // Channel / Tag Quick Filter Bar (Multi-Channel Subscription)
            int subCount = subscribedTags.Count;
            string channelHeader = subCount > 0 ? $"Channels ({subCount}):" : "Channels:";
            EditorGUILayout.LabelField(channelHeader, EditorStyles.miniLabel, GUILayout.Width(subCount > 0 ? 82 : 58));

            tagBarScroll = EditorGUILayout.BeginScrollView(tagBarScroll, GUIStyle.none, GUIStyle.none, GUILayout.Height(18));
            EditorGUILayout.BeginHorizontal();

            bool isAllSelected = subscribedTags.Count == 0;
            GUI.color = isAllSelected ? new Color(0.45f, 0.85f, 1f) : Color.white;
            if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(35)))
            {
                if (subscribedTags.Count > 0)
                {
                    subscribedTags.Clear();
                    isDirty = true;
                }
            }
            GUI.color = Color.white;

            var knownTags = HLogCore.KnownTags;
            bool hasUntaggedLogs = HLogCore.HasUntaggedLogs;

            var tagsToDisplay = new List<string>(knownTags);
            if (hasUntaggedLogs && !tagsToDisplay.Contains("[Untagged]"))
            {
                tagsToDisplay.Add("[Untagged]");
            }

            foreach (var tag in tagsToDisplay)
            {
                bool isSubscribed = subscribedTags.Contains(tag);
                bool isDisabled = disabledTags.Contains(tag);

                string label;
                if (isDisabled)
                {
                    label = $"[Off] {tag}";
                    GUI.color = new Color(0.55f, 0.55f, 0.55f);
                }
                else if (isSubscribed)
                {
                    label = $"✓ {tag}";
                    GUI.color = new Color(0.35f, 0.95f, 0.6f);
                }
                else
                {
                    label = tag;
                    GUI.color = isAllSelected ? Color.white : new Color(0.7f, 0.75f, 0.85f);
                }

                if (GUILayout.Button(label, EditorStyles.toolbarButton))
                {
                    if (Event.current.button == 0)
                    {
                        if (isSubscribed)
                        {
                            subscribedTags.Remove(tag);
                        }
                        else
                        {
                            subscribedTags.Add(tag);
                        }
                        isDirty = true;
                    }
                }

                if (Event.current.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent($"Only '{tag}'"), false, () =>
                    {
                        subscribedTags.Clear();
                        subscribedTags.Add(tag);
                        isDirty = true;
                    });
                    menu.AddItem(new GUIContent("Subscribe All"), false, () =>
                    {
                        subscribedTags.Clear();
                        isDirty = true;
                    });
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent($"Toggle Mute '{tag}'"), isDisabled, () =>
                    {
                        if (isDisabled) disabledTags.Remove(tag);
                        else disabledTags.Add(tag);
                        isDirty = true;
                    });
                    menu.ShowAsContext();
                    Event.current.Use();
                }

                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            var tagScrollRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.ScrollWheel && tagScrollRect.Contains(Event.current.mousePosition))
            {
                tagBarScroll.x += Event.current.delta.y * 25f;
                tagBarScroll.x = Mathf.Max(0f, tagBarScroll.x);
                Event.current.Use();
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Logs View Drawing

        private void DrawLogsView()
        {
            float listHeight = position.height - detailPanelHeight - 48f;

            // 스크롤 뷰가 이벤트를 소비하기 전에 사용자 입력(휠, 스크롤바 드래그)인지 기록
            var eventType = Event.current.type;
            bool isUserScrollEvent = eventType == EventType.ScrollWheel || eventType == EventType.MouseDrag ||
                                     eventType == EventType.MouseDown || eventType == EventType.MouseUp ||
                                     eventType == EventType.KeyDown;
            float prevScrollY = logScroll.y;

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(Mathf.Max(listHeight, 80f)));

            for (int i = 0; i < filteredLogs.Count; i++)
            {
                var entry = filteredLogs[i];
                bool isSelected = selectedEntries.Contains(entry);

                var rowStyle = new GUIStyle(EditorStyles.label)
                {
                    fixedHeight = 22,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };

                var bgRect = EditorGUILayout.GetControlRect(false, 22);

                // Alternating / Selected Background
                if (isSelected)
                {
                    EditorGUI.DrawRect(bgRect, new Color(0.24f, 0.38f, 0.58f, 0.5f));
                }
                else if (i % 2 == 1)
                {
                    EditorGUI.DrawRect(bgRect, new Color(0f, 0f, 0f, 0.08f));
                }

                // Click Selection & Double Click Open (컴파일 에러 및 일반 로그 모두 지원)
                if (Event.current.type == EventType.ContextClick && bgRect.Contains(Event.current.mousePosition))
                {
                    ShowRowContextMenu(entry);
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && bgRect.Contains(Event.current.mousePosition))
                {
                    OnRowClicked(entry, i, Event.current.shift, EditorGUI.actionKey);
                    if (Event.current.clickCount == 2)
                    {
                        OpenTopStackLocation(entry);
                        Event.current.Use();
                    }
                    else
                    {
                        Repaint();
                    }
                }

                // Row Content Layout
                float x = bgRect.x + 4;
                float y = bgRect.y + 2;

                // 1. Time Label
                GUI.color = new Color(0.55f, 0.7f, 0.9f);
                GUI.Label(new Rect(x, y, 55, 18), entry.TimeString, EditorStyles.miniLabel);
                x += 58;

                // 2. Type Badge
                DrawLogTypeBadge(new Rect(x, y + 1, 44, 16), entry.LogType);
                x += 48;

                // 3. Channel Tag Badge
                if (!string.IsNullOrEmpty(entry.Tag))
                {
                    GUI.color = new Color(0.45f, 0.65f, 0.85f);
                    GUI.Label(new Rect(x, y, 65, 18), $"[{entry.Tag}]", EditorStyles.boldLabel);
                    x += 70;
                }

                // 4. Message Text
                GUI.color = GetLogColor(entry.LogType);
                string singleLineMsg = entry.Message.Replace('\n', ' ');
                GUI.Label(new Rect(x, y, bgRect.width - x + bgRect.x, 18), singleLineMsg, rowStyle);
                GUI.color = Color.white;
            }

            if (eventType == EventType.Repaint)
            {
                logContentHeight = filteredLogs.Count > 0 ? GUILayoutUtility.GetLastRect().yMax : 0f;

                if (autoScroll && isScrollToBottomPending)
                {
                    logScroll.y = float.MaxValue;
                    isScrollToBottomPending = false;
                }
            }

            EditorGUILayout.EndScrollView();

            if (eventType == EventType.Repaint)
            {
                logViewHeight = GUILayoutUtility.GetLastRect().height;
            }

            // 사용자가 위로 올리면 따라가기를 멈추고, 다시 맨 아래로 내리면 재개
            if (isUserScrollEvent && !Mathf.Approximately(prevScrollY, logScroll.y))
            {
                isStickToBottom = IsLogScrollAtBottom();
            }
        }

        private bool IsLogScrollAtBottom()
        {
            float maxScroll = logContentHeight - logViewHeight;
            return maxScroll <= 0f || logScroll.y >= maxScroll - BottomTolerance;
        }

        private void DrawLogTypeBadge(Rect rect, LogType type)
        {
            Color badgeColor;
            string text;

            switch (type)
            {
                case LogType.Warning:
                    badgeColor = new Color(0.9f, 0.65f, 0.15f);
                    text = "WARN";
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    badgeColor = new Color(0.9f, 0.3f, 0.3f);
                    text = "ERR";
                    break;
                default:
                    badgeColor = new Color(0.35f, 0.65f, 0.95f);
                    text = "INFO";
                    break;
            }

            EditorGUI.DrawRect(rect, badgeColor * 0.25f);
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = badgeColor }
            };
            GUI.Label(rect, text, style);
        }

        private Color GetLogColor(LogType type)
        {
            switch (type)
            {
                case LogType.Warning: return new Color(1f, 0.85f, 0.45f);
                case LogType.Error:
                case LogType.Exception: return new Color(1f, 0.5f, 0.5f);
                default: return new Color(0.9f, 0.92f, 0.95f);
            }
        }

        #endregion

        #region Detail Panel & Splitter

        private void DrawDetailSplitter()
        {
            var splitterRect = EditorGUILayout.GetControlRect(false, 4);
            EditorGUI.DrawRect(splitterRect, new Color(0.18f, 0.2f, 0.24f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
            {
                isResizingDetail = true;
            }
            if (isResizingDetail)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    detailPanelHeight -= Event.current.delta.y;
                    detailPanelHeight = Mathf.Clamp(detailPanelHeight, 80f, position.height - 120f);
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    isResizingDetail = false;
                }
            }
        }

        private void DrawDetailPanel()
        {
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(detailPanelHeight));

            if (selectedEntry != null)
            {
                bool hasMessageLoc = TryExtractSourceLocation(selectedEntry.RawMessage, out string msgPath, out int msgLine)
                                  || TryExtractSourceLocation(selectedEntry.Message, out msgPath, out msgLine);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{selectedEntry.LogType}] {selectedEntry.TimeString}", EditorStyles.boldLabel);

                if (hasMessageLoc)
                {
                    string btnLabel = $"Open ({Path.GetFileName(msgPath)}:{msgLine})";
                    if (GUILayout.Button(btnLabel, EditorStyles.miniButton, GUILayout.Width(160)))
                    {
                        OpenSourceFileAtLine(msgPath, msgLine);
                    }
                }

                if (GUILayout.Button("Copy All", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    string fullText = $"{selectedEntry.Message}\n\n{selectedEntry.StackTrace}";
                    EditorGUIUtility.systemCopyBuffer = fullText;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.SelectableLabel(selectedEntry.Message, EditorStyles.wordWrappedLabel);

                GUILayout.Space(6);

                if (hasMessageLoc)
                {
                    EditorGUILayout.LabelField("--- Compiler / Message Source Location ---", EditorStyles.miniBoldLabel);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(45)))
                    {
                        OpenSourceFileAtLine(msgPath, msgLine);
                    }

                    var locRect = EditorGUILayout.GetControlRect(false, 18, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && locRect.Contains(Event.current.mousePosition))
                    {
                        OpenSourceFileAtLine(msgPath, msgLine);
                        Event.current.Use();
                    }
                    GUI.Label(locRect, $"{msgPath} : Line {msgLine}", EditorStyles.boldLabel);

                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }

                EditorGUILayout.LabelField("--- Stack Trace (Double-click or click Open to jump to IDE) ---", EditorStyles.miniBoldLabel);

                string stack = string.IsNullOrEmpty(selectedEntry.StackTrace) ? string.Empty : selectedEntry.StackTrace;
                string cleanedStack = HLogCore.FilterStackTrace(stack);
                if (string.IsNullOrEmpty(cleanedStack))
                {
                    cleanedStack = stack;
                }

                if (string.IsNullOrEmpty(cleanedStack))
                {
                    if (hasMessageLoc)
                    {
                        EditorGUILayout.LabelField("(Compiler error location detected above)", EditorStyles.miniLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("(No stack trace available for this entry)", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    var lines = cleanedStack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        bool hasLocation = TryExtractSourceLocation(line, out string filePath, out int lineNum);

                        EditorGUILayout.BeginHorizontal();

                        if (hasLocation)
                        {
                            if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(45)))
                            {
                                OpenSourceFileAtLine(filePath, lineNum);
                            }
                        }
                        else
                        {
                            GUILayout.Space(49);
                        }

                        var lineRect = EditorGUILayout.GetControlRect(false, 18, GUILayout.ExpandWidth(true));
                        if (hasLocation && Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && lineRect.Contains(Event.current.mousePosition))
                        {
                            OpenSourceFileAtLine(filePath, lineNum);
                            Event.current.Use();
                        }

                        GUI.Label(lineRect, line, EditorStyles.miniLabel);

                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a log entry to view details and stack trace.", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private static bool TryExtractSourceLocation(string text, out string filePath, out int lineNumber)
        {
            filePath = null;
            lineNumber = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var mCompile = PatternCompileError.Match(text);
            if (mCompile.Success)
            {
                filePath = mCompile.Groups[1].Value.Trim(' ', '(', ')', '"');
                if (int.TryParse(mCompile.Groups[2].Value, out lineNumber)) return true;
            }

            var mAt = PatternAt.Match(text);
            if (mAt.Success)
            {
                filePath = mAt.Groups[1].Value.Trim(' ', '(', ')', '"');
                if (int.TryParse(mAt.Groups[2].Value, out lineNumber)) return true;
            }

            var mInLine = PatternInLine.Match(text);
            if (mInLine.Success)
            {
                filePath = mInLine.Groups[1].Value.Trim(' ', '(', ')', '"');
                if (int.TryParse(mInLine.Groups[2].Value, out lineNumber)) return true;
            }

            var mColon = PatternFileColon.Match(text);
            if (mColon.Success)
            {
                filePath = mColon.Groups[1].Value.Trim(' ', '(', ')', '"');
                if (int.TryParse(mColon.Groups[2].Value, out lineNumber)) return true;
            }

            return false;
        }

        private static void OpenTopStackLocation(HLogCore.LogEntry entry)
        {
            if (entry == null) return;

            if (TryExtractSourceLocation(entry.RawMessage, out string msgPath, out int msgLine))
            {
                OpenSourceFileAtLine(msgPath, msgLine);
                return;
            }
            if (TryExtractSourceLocation(entry.Message, out msgPath, out msgLine))
            {
                OpenSourceFileAtLine(msgPath, msgLine);
                return;
            }

            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                string cleaned = HLogCore.FilterStackTrace(entry.StackTrace);
                if (string.IsNullOrEmpty(cleaned)) cleaned = entry.StackTrace;

                var lines = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (TryExtractSourceLocation(line, out string filePath, out int lineNum))
                    {
                        OpenSourceFileAtLine(filePath, lineNum);
                        return;
                    }
                }
            }
        }

        private static void OpenSourceFileAtLine(string path, int line)
        {
            if (string.IsNullOrEmpty(path)) return;

            path = path.Trim(' ', '(', ')', '"');
            string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            if (File.Exists(normalized))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(normalized, line);
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string fromRoot = Path.Combine(projectRoot, normalized);
                if (File.Exists(fromRoot))
                {
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fromRoot, line);
                    return;
                }
            }

            string fromAssets = Path.Combine(Application.dataPath, normalized);
            if (File.Exists(fromAssets))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fromAssets, line);
                return;
            }

            try
            {
                string full = Path.GetFullPath(normalized);
                if (File.Exists(full))
                {
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(full, line);
                    return;
                }
            }
            catch
            {
                // ignored
            }

            Debug.LogWarning($"[HLog] Unable to locate source file at: {path} (line {line})");
        }

        #endregion

        #region Watch View Drawing

        private void DrawWatchView()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool isValues = watchSubTab == 0;
            bool isTriggers = watchSubTab == 1;

            if (GUILayout.Toggle(isValues, "Watch Values", EditorStyles.toolbarButton, GUILayout.Width(100)) != isValues)
            {
                watchSubTab = 0;
            }

            int hitCount = 0;
            for (int i = 0; i < watchTriggers.Count; i++)
            {
                if (watchTriggers[i].hasTriggered) hitCount++;
            }
            string trLabel = hitCount > 0 ? $"Condition Triggers [{watchTriggers.Count}] ({hitCount} Hit)" : $"Condition Triggers [{watchTriggers.Count}]";
            if (GUILayout.Toggle(isTriggers, trLabel, EditorStyles.toolbarButton, GUILayout.Width(170)) != isTriggers)
            {
                watchSubTab = 1;
            }

            GUILayout.FlexibleSpace();

            if (watchSubTab == 1)
            {
                if (GUILayout.Button("+ Add Trigger", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    watchTriggers.Add(new WatchTrigger());
                    SaveWatchTriggers();
                }
                if (GUILayout.Button("Reset Hits", EditorStyles.toolbarButton, GUILayout.Width(75)))
                {
                    for (int i = 0; i < watchTriggers.Count; i++)
                    {
                        watchTriggers[i].hasTriggered = false;
                    }
                    SaveWatchTriggers();
                }
                if (GUILayout.Button("Open Reports Folder", EditorStyles.toolbarButton, GUILayout.Width(130)))
                {
                    OpenReportsFolder();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (watchSubTab == 0)
            {
                DrawWatchValuesSubView();
            }
            else
            {
                DrawWatchTriggersSubView();
            }
        }

        private static void OpenReportsFolder()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string reportsDir = Path.Combine(projectRoot, "Reports");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }
            EditorUtility.RevealInFinder(reportsDir);
        }

        private void DrawWatchValuesSubView()
        {
            if (isWatchesDirty || cachedWatches == null)
            {
                cachedWatches = HLogCore.Watches;
                isWatchesDirty = false;
            }
            var map = cachedWatches;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Watch Key", EditorStyles.boldLabel, GUILayout.Width(220));
            EditorGUILayout.LabelField("Current Value", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Updated Time", EditorStyles.boldLabel, GUILayout.Width(90));
            EditorGUILayout.LabelField("Action", EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            watchScroll = EditorGUILayout.BeginScrollView(watchScroll);

            if (map.Count == 0)
            {
                EditorGUILayout.HelpBox("No variables currently being watched.\nCall HLog.Common.Watch(\"Key\", value) at runtime.", MessageType.Info);
            }

            int index = 0;
            foreach (var kvp in map)
            {
                var entry = kvp.Value;
                var rowRect = EditorGUILayout.GetControlRect(false, 22);

                if (index % 2 == 1)
                {
                    EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.08f));
                }

                float x = rowRect.x + 6;
                float y = rowRect.y + 2;

                // 1. Key
                GUI.color = new Color(0.45f, 0.75f, 1f);
                GUI.Label(new Rect(x, y, 210, 18), entry.Key, EditorStyles.boldLabel);
                x += 220;

                // 2. Value (null 또는 발동한 조건은 붉은색 강조)
                bool isNull = string.Equals(entry.Value, "null", StringComparison.OrdinalIgnoreCase);
                bool isCondition = IsConditionValue(entry.Value);
                if (isCondition && entry.Value.IndexOf(" · Hit ", StringComparison.Ordinal) >= 0)
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f);
                    GUI.Label(new Rect(x, y, rowRect.width - x - 170 + rowRect.x, 18), entry.Value, EditorStyles.boldLabel);
                }
                else if (isNull)
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f);
                    GUI.Label(new Rect(x, y, rowRect.width - x - 170 + rowRect.x, 18), "null", EditorStyles.boldLabel);
                }
                else
                {
                    GUI.color = new Color(0.35f, 0.9f, 0.55f);
                    GUI.Label(new Rect(x, y, rowRect.width - x - 170 + rowRect.x, 18), entry.Value, EditorStyles.label);
                }

                // 3. Time
                GUI.color = new Color(0.6f, 0.65f, 0.75f);
                GUI.Label(new Rect(rowRect.width - 165 + rowRect.x, y, 80, 18), entry.Timestamp.ToString("HH:mm:ss"), EditorStyles.miniLabel);

                // 4. Trigger Quick Add (코드 조건은 이미 조건이 있으므로 대신 Reset: 발동한 조건을 다시 대기)
                GUI.color = Color.white;
                if (isCondition)
                {
                    if (GUI.Button(new Rect(rowRect.width - 75 + rowRect.x, y, 68, 18), "Reset", EditorStyles.miniButton))
                    {
                        HLogCore.ResetCondition(entry.Key);
                    }
                }
                else if (GUI.Button(new Rect(rowRect.width - 75 + rowRect.x, y, 68, 18), "+ Trigger", EditorStyles.miniButton))
                {
                    watchTriggers.Add(new WatchTrigger(entry.Key));
                    SaveWatchTriggers();
                    watchSubTab = 1;
                }

                index++;
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>코드에서 등록한 조건(BreakWhen / ReportWhen)의 Watch 값인지 ("[Break] …" / "[Report] …")</summary>
        private static bool IsConditionValue(string value)
        {
            return value != null && (value.StartsWith("[Break] ", StringComparison.Ordinal) || value.StartsWith("[Report] ", StringComparison.Ordinal));
        }

        private void DrawWatchTriggersSubView()
        {
            EditorGUILayout.HelpBox("특정 Watch 변수 값이 조건식에 도달하면 자동으로 에러 로그를 출력하고 유니티 에디터를 일시정지(Pause)합니다.\n'Report' 체크 시 프로젝트 루트의 'Reports/' 폴더에 전체 Watch 변수 스냅샷 텍스트 파일이 자동 생성됩니다.", MessageType.Info);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("On", EditorStyles.boldLabel, GUILayout.Width(25));
            EditorGUILayout.LabelField("Watch Key", EditorStyles.boldLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("Op", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Threshold Value", EditorStyles.boldLabel, GUILayout.Width(95));
            EditorGUILayout.LabelField("Pause", EditorStyles.boldLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Once", EditorStyles.boldLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(75));
            EditorGUILayout.LabelField("", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            watchScroll = EditorGUILayout.BeginScrollView(watchScroll);

            if (watchTriggers.Count == 0)
            {
                EditorGUILayout.HelpBox("등록된 조건 트리거가 없습니다. 상단 [+ Add Trigger] 또는 Watch Values 탭의 [+ Trigger]를 클릭하세요.", MessageType.None);
            }

            for (int i = 0; i < watchTriggers.Count; i++)
            {
                var tr = watchTriggers[i];
                var rowRect = EditorGUILayout.GetControlRect(false, 24);

                if (i % 2 == 1)
                {
                    EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.08f));
                }

                float x = rowRect.x + 4;
                float y = rowRect.y + 3;

                EditorGUI.BeginChangeCheck();

                // 1. Enable Toggle
                tr.enabled = EditorGUI.Toggle(new Rect(x, y, 20, 18), tr.enabled);
                x += 25;

                // 2. Watch Key
                tr.watchKey = EditorGUI.TextField(new Rect(x, y, 145, 18), tr.watchKey);
                x += 150;

                // 3. Operator
                tr.op = (TriggerOp)EditorGUI.EnumPopup(new Rect(x, y, 75, 18), tr.op);
                x += 80;

                // 4. Target Value (IsNull, IsNotNull은 자동 처리)
                bool isNullOp = tr.op == TriggerOp.IsNull || tr.op == TriggerOp.IsNotNull;
                if (isNullOp)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.TextField(new Rect(x, y, 90, 18), "(null check)");
                    }
                }
                else
                {
                    tr.targetValue = EditorGUI.TextField(new Rect(x, y, 90, 18), tr.targetValue);
                }
                x += 95;

                // 5. Pause Editor Toggle
                tr.pauseEditor = EditorGUI.Toggle(new Rect(x + 10, y, 20, 18), tr.pauseEditor);
                x += 45;

                // 6. Report Export Toggle
                tr.exportReport = EditorGUI.Toggle(new Rect(x + 12, y, 20, 18), tr.exportReport);
                x += 50;

                // 7. Trigger Once Toggle
                tr.triggerOnce = EditorGUI.Toggle(new Rect(x + 10, y, 20, 18), tr.triggerOnce);
                x += 45;

                // 8. Status Badge & Reset
                if (tr.hasTriggered)
                {
                    GUI.color = new Color(1f, 0.35f, 0.35f);
                    if (GUI.Button(new Rect(x, y, 70, 18), "Hit (Reset)", EditorStyles.miniButton))
                    {
                        tr.hasTriggered = false;
                    }
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = tr.enabled ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
                    GUI.Label(new Rect(x, y, 70, 18), tr.enabled ? "Waiting" : "Disabled", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
                x += 75;

                // 9. Delete Button
                if (GUI.Button(new Rect(x, y, 22, 18), "X", EditorStyles.miniButton))
                {
                    watchTriggers.RemoveAt(i);
                    SaveWatchTriggers();
                    GUIUtility.ExitGUI();
                }

                if (EditorGUI.EndChangeCheck())
                {
                    SaveWatchTriggers();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion
    }
}
#endif

