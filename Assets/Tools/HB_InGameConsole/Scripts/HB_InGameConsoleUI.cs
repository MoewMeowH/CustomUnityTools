using System;
using System.Collections.Generic;
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit 기반 인게임 콘솔 UI 및 성능 모니터링 컨트롤러입니다.
/// 실시간 Watch, 동적 태그 필터, 검색/제외 필터, 스택트레이스 정제 기능을 지원합니다.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HB_InGameConsoleUI : MonoBehaviour
{
    private enum LogFilter
    {
        All,
        Info,
        Warning,
        Error
    }

    private enum ConsoleViewMode
    {
        Logs,
        Watch
    }

    [Header("UI Toolkit")]
    [SerializeField] private VisualTreeAsset consoleVisualTree;
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Performance Monitor")]
    [SerializeField] private bool showFpsGraph = true;

    private const int MaxUiLogCount = 1000;
    private const int GraphSampleCount = 80;

    private UIDocument uiDocument;
    private VisualElement rootElement;

    // View Mode elements
    private Button viewLogsBtn;
    private Button viewWatchBtn;
    private ConsoleViewMode currentViewMode = ConsoleViewMode.Logs;

    // Header Toolbar elements
    private Button openButton;
    private VisualElement consoleWindow;
    private Button closeButton;
    private Button clearButton;
    private Button filterAllButton;
    private Button filterInfoButton;
    private Button filterWarnButton;
    private Button filterErrorButton;
    private Button autoScrollButton;
    private Label consoleStatsLabel;

    // Sub Toolbar elements (Search & Tags)
    private VisualElement subToolBar;
    private TextField searchField;
    private Button clearSearchBtn;
    private ScrollView tagScrollView;
    private Button tagAllButton;
    private readonly Dictionary<string, Button> createdTagButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

    // ScrollViews & Detail Panel
    private ScrollView logScrollView;
    private ScrollView watchScrollView;
    private Label detailLabel;
    private Button copyDetailBtn;

    // FPS & Graph elements
    private VisualElement fpsWidget;
    private Label fpsLabel;
    private Label msLabel;
    private VisualElement fpsGraphCanvas;

    // State & Caches
    private LogFilter currentFilter = LogFilter.All;
    private string currentTagFilter = string.Empty;
    private string currentSearchQuery = string.Empty;
    private bool autoScrollEnabled = true;

    private readonly List<VisualElement> createdLogRows = new List<VisualElement>();
    private readonly Dictionary<string, (VisualElement row, Label valLabel, Label timeLabel)> watchRowMap =
        new Dictionary<string, (VisualElement row, Label valLabel, Label timeLabel)>(StringComparer.OrdinalIgnoreCase);
    private VisualElement selectedRow;

    // Frame metrics
    private readonly float[] frameTimes = new float[GraphSampleCount];
    private int bufferIndex = 0;
    private int recordedCount = 0;
    private float textUpdateTimer = 0f;
    private const float TextUpdateInterval = 0.12f;
    private float accumulatedDelta = 0f;
    private int accumulatedFrames = 0;
    private bool isGraphCollapsed = false;

    public bool IsConsoleOpen => consoleWindow != null && !consoleWindow.ClassListContains("hb-hidden");

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();

        var settings = InGameConsoleSettings.Instance;
        if (settings != null)
        {
            dontDestroyOnLoad = settings.DontDestroyOnLoad;
            showFpsGraph = settings.ShowFpsGraph;
        }

        Application.targetFrameRate = 60;
        if (dontDestroyOnLoad && transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var settings = InGameConsoleSettings.Instance;

        if (uiDocument.visualTreeAsset == null)
        {
            if (consoleVisualTree != null)
                uiDocument.visualTreeAsset = consoleVisualTree;
            else if (settings != null && settings.ConsoleVisualTree != null)
                uiDocument.visualTreeAsset = settings.ConsoleVisualTree;
            else
                uiDocument.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

        if (uiDocument.panelSettings == null)
        {
            if (settings != null && settings.PanelSettings != null)
                uiDocument.panelSettings = settings.PanelSettings;
            else
                uiDocument.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");
        }

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        BindUIElements();
        RegisterCallbacks();

        RefreshAllLogs();
        RefreshAllWatches();
        RefreshAllTags();

        HLogCore.OnLogAdded += HandleLogAdded;
        HLogCore.OnLogsCleared += HandleLogsCleared;
        HLogCore.OnWatchUpdated += HandleWatchUpdated;
        HLogCore.OnTagDiscovered += HandleTagDiscovered;

        if (fpsGraphCanvas != null)
        {
            fpsGraphCanvas.generateVisualContent += DrawFrameTimeGraph;
        }
    }

    private void OnDisable()
    {
        HLogCore.OnLogAdded -= HandleLogAdded;
        HLogCore.OnLogsCleared -= HandleLogsCleared;
        HLogCore.OnWatchUpdated -= HandleWatchUpdated;
        HLogCore.OnTagDiscovered -= HandleTagDiscovered;

        if (fpsGraphCanvas != null)
        {
            fpsGraphCanvas.generateVisualContent -= DrawFrameTimeGraph;
        }

        UnregisterCallbacks();
    }

    private void Update()
    {
        UpdatePerformanceMetrics();
    }

    private void BindUIElements()
    {
        openButton = rootElement.Q<Button>("openButton");
        consoleWindow = rootElement.Q<VisualElement>("consoleWindow");
        closeButton = rootElement.Q<Button>("closeButton");
        clearButton = rootElement.Q<Button>("clearBtn");

        viewLogsBtn = rootElement.Q<Button>("viewLogsBtn");
        viewWatchBtn = rootElement.Q<Button>("viewWatchBtn");

        filterAllButton = rootElement.Q<Button>("filterAllBtn");
        filterInfoButton = rootElement.Q<Button>("filterInfoBtn");
        filterWarnButton = rootElement.Q<Button>("filterWarnBtn");
        filterErrorButton = rootElement.Q<Button>("filterErrorBtn");
        autoScrollButton = rootElement.Q<Button>("autoScrollBtn");

        subToolBar = rootElement.Q<VisualElement>("subToolBar");
        searchField = rootElement.Q<TextField>("searchField");
        clearSearchBtn = rootElement.Q<Button>("clearSearchBtn");
        tagScrollView = rootElement.Q<ScrollView>("tagScrollView");

        logScrollView = rootElement.Q<ScrollView>("logScrollView");
        watchScrollView = rootElement.Q<ScrollView>("watchScrollView");

        detailLabel = rootElement.Q<Label>("detailLabel");
        copyDetailBtn = rootElement.Q<Button>("copyDetailBtn");
        consoleStatsLabel = rootElement.Q<Label>("consoleStatsLabel");

        fpsWidget = rootElement.Q<VisualElement>("fpsWidget");
        fpsLabel = rootElement.Q<Label>("fpsLabel");
        msLabel = rootElement.Q<Label>("msLabel");
        fpsGraphCanvas = rootElement.Q<VisualElement>("fpsGraphCanvas");

        if (fpsWidget != null && !showFpsGraph)
        {
            fpsWidget.style.display = DisplayStyle.None;
        }
    }

    private void RegisterCallbacks()
    {
        if (openButton != null) openButton.clicked += OpenConsole;
        if (closeButton != null) closeButton.clicked += CloseConsole;
        if (clearButton != null) clearButton.clicked += ClearConsole;

        if (viewLogsBtn != null) viewLogsBtn.clicked += () => SwitchViewMode(ConsoleViewMode.Logs);
        if (viewWatchBtn != null) viewWatchBtn.clicked += () => SwitchViewMode(ConsoleViewMode.Watch);

        if (filterAllButton != null) filterAllButton.clicked += () => SetFilter(LogFilter.All);
        if (filterInfoButton != null) filterInfoButton.clicked += () => SetFilter(LogFilter.Info);
        if (filterWarnButton != null) filterWarnButton.clicked += () => SetFilter(LogFilter.Warning);
        if (filterErrorButton != null) filterErrorButton.clicked += () => SetFilter(LogFilter.Error);
        if (autoScrollButton != null) autoScrollButton.clicked += ToggleAutoScroll;

        if (searchField != null)
        {
            searchField.RegisterValueChangedCallback(OnSearchValueChanged);
        }
        if (clearSearchBtn != null)
        {
            clearSearchBtn.clicked += () => { if (searchField != null) searchField.value = string.Empty; };
        }

        if (copyDetailBtn != null)
        {
            copyDetailBtn.clicked += CopySelectedDetail;
        }

        if (fpsWidget != null)
        {
            fpsWidget.RegisterCallback<ClickEvent>(OnFpsWidgetClicked);
        }
    }

    private void UnregisterCallbacks()
    {
        if (openButton != null) openButton.clicked -= OpenConsole;
        if (closeButton != null) closeButton.clicked -= CloseConsole;
        if (clearButton != null) clearButton.clicked -= ClearConsole;
        if (autoScrollButton != null) autoScrollButton.clicked -= ToggleAutoScroll;

        if (copyDetailBtn != null) copyDetailBtn.clicked -= CopySelectedDetail;

        if (fpsWidget != null)
        {
            fpsWidget.UnregisterCallback<ClickEvent>(OnFpsWidgetClicked);
        }
    }

    private void OnFpsWidgetClicked(ClickEvent evt)
    {
        if (fpsGraphCanvas == null) return;

        isGraphCollapsed = !isGraphCollapsed;
        if (isGraphCollapsed)
        {
            fpsGraphCanvas.AddToClassList("hb-graph-collapsed");
        }
        else
        {
            fpsGraphCanvas.RemoveFromClassList("hb-graph-collapsed");
        }
    }

    #region Performance & Frame Graph

    private int GetTargetFps()
    {
        int target = Application.targetFrameRate;
        if (target > 0) return target;

#if UNITY_2022_2_OR_NEWER
        double refresh = Screen.currentResolution.refreshRateRatio.value;
        if (refresh > 10.0) return Mathf.RoundToInt((float)refresh);
#else
        int refresh = Screen.currentResolution.refreshRate;
        if (refresh > 10) return refresh;
#endif

        return 60;
    }

    private void UpdatePerformanceMetrics()
    {
        float dt = Time.unscaledDeltaTime;
        float frameMs = dt * 1000f;

        frameTimes[bufferIndex] = frameMs;
        bufferIndex = (bufferIndex + 1) % GraphSampleCount;
        if (recordedCount < GraphSampleCount) recordedCount++;

        if (fpsGraphCanvas != null && !isGraphCollapsed)
        {
            fpsGraphCanvas.MarkDirtyRepaint();
        }

        accumulatedDelta += dt;
        accumulatedFrames++;
        textUpdateTimer += dt;

        if (textUpdateTimer >= TextUpdateInterval)
        {
            float avgDt = accumulatedDelta / Mathf.Max(accumulatedFrames, 1);
            float currentFps = avgDt > 0.0001f ? 1.0f / avgDt : 0f;
            float avgMs = avgDt * 1000f;

            int intFps = Mathf.RoundToInt(currentFps);
            int targetFps = GetTargetFps();
            float ratio = targetFps > 0 ? (currentFps / targetFps) : 1f;

            if (fpsLabel != null)
            {
                fpsLabel.text = $"{intFps} FPS";
                fpsLabel.RemoveFromClassList("hb-fps-good");
                fpsLabel.RemoveFromClassList("hb-fps-warn");
                fpsLabel.RemoveFromClassList("hb-fps-bad");

                if (ratio >= 0.9f)
                    fpsLabel.AddToClassList("hb-fps-good");
                else if (ratio >= 0.7f)
                    fpsLabel.AddToClassList("hb-fps-warn");
                else
                    fpsLabel.AddToClassList("hb-fps-bad");
            }

            if (msLabel != null)
            {
                msLabel.text = $"{avgMs:F1} ms";
            }

            if (consoleStatsLabel != null)
            {
                consoleStatsLabel.text = $"[{intFps}/{targetFps} FPS | {avgMs:F1}ms]";
            }

            textUpdateTimer = 0f;
            accumulatedDelta = 0f;
            accumulatedFrames = 0;
        }
    }

    private void DrawFrameTimeGraph(MeshGenerationContext mgc)
    {
        if (fpsGraphCanvas == null || recordedCount < 2) return;

        float width = fpsGraphCanvas.contentRect.width;
        float height = fpsGraphCanvas.contentRect.height;
        if (width <= 10f || height <= 10f) return;

        var painter = mgc.painter2D;

        int targetFps = GetTargetFps();
        float targetMs = 1000f / Mathf.Max(targetFps, 1);
        float warnMs = 1000f / Mathf.Max(targetFps * 0.7f, 1);

        float maxMs = Mathf.Max(warnMs * 1.25f, 40f);
        for (int i = 0; i < recordedCount; i++)
        {
            if (frameTimes[i] > maxMs)
            {
                maxMs = Mathf.Min(frameTimes[i], 100f);
            }
        }

        float yTarget = height - (targetMs / maxMs * height);
        if (yTarget >= 0f && yTarget <= height)
        {
            painter.strokeColor = new Color(0.29f, 0.87f, 0.5f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, yTarget));
            painter.LineTo(new Vector2(width, yTarget));
            painter.Stroke();
        }

        float yWarn = height - (warnMs / maxMs * height);
        if (yWarn >= 0f && yWarn <= height)
        {
            painter.strokeColor = new Color(0.97f, 0.5f, 0.2f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, yWarn));
            painter.LineTo(new Vector2(width, yWarn));
            painter.Stroke();
        }

        int samples = recordedCount;
        float stepX = width / Mathf.Max(samples - 1, 1);

        painter.fillColor = new Color(0.25f, 0.7f, 1f, 0.16f);
        painter.BeginPath();
        painter.MoveTo(new Vector2(0f, height));

        for (int i = 0; i < samples; i++)
        {
            int ringIdx = (bufferIndex - samples + i + GraphSampleCount) % GraphSampleCount;
            float ms = frameTimes[ringIdx];
            float y = Mathf.Clamp(height - (ms / maxMs * height), 0f, height);
            float x = i * stepX;
            painter.LineTo(new Vector2(x, y));
        }

        painter.LineTo(new Vector2(width, height));
        painter.ClosePath();
        painter.Fill();

        painter.lineWidth = 1.4f;
        painter.strokeColor = new Color(0.35f, 0.75f, 1f, 0.95f);
        painter.BeginPath();

        for (int i = 0; i < samples; i++)
        {
            int ringIdx = (bufferIndex - samples + i + GraphSampleCount) % GraphSampleCount;
            float ms = frameTimes[ringIdx];
            float y = Mathf.Clamp(height - (ms / maxMs * height), 0f, height);
            float x = i * stepX;

            if (i == 0)
                painter.MoveTo(new Vector2(x, y));
            else
                painter.LineTo(new Vector2(x, y));
        }
        painter.Stroke();
    }

    #endregion

    #region View Mode & Watch System

    private void SwitchViewMode(ConsoleViewMode mode)
    {
        currentViewMode = mode;

        if (mode == ConsoleViewMode.Logs)
        {
            viewLogsBtn?.AddToClassList("hb-filter-active");
            viewWatchBtn?.RemoveFromClassList("hb-filter-active");

            logScrollView?.RemoveFromClassList("hb-hidden");
            watchScrollView?.AddToClassList("hb-hidden");
            subToolBar?.RemoveFromClassList("hb-hidden");
        }
        else
        {
            viewWatchBtn?.AddToClassList("hb-filter-active");
            viewLogsBtn?.RemoveFromClassList("hb-filter-active");

            watchScrollView?.RemoveFromClassList("hb-hidden");
            logScrollView?.AddToClassList("hb-hidden");
            subToolBar?.AddToClassList("hb-hidden");

            RefreshAllWatches();
        }
    }

    private void HandleWatchUpdated(string key, string value)
    {
        UpdateWatchButtonCount();

        if (watchScrollView == null) return;

        if (watchRowMap.TryGetValue(key, out var rowInfo))
        {
            rowInfo.valLabel.text = value;
            rowInfo.timeLabel.text = DateTime.Now.ToString("HH:mm:ss");
        }
        else
        {
            AddWatchElement(key, value, DateTime.Now);
        }
    }

    private void RefreshAllWatches()
    {
        if (watchScrollView == null) return;

        watchScrollView.Clear();
        watchRowMap.Clear();

        var map = HLogCore.Watches;
        foreach (var kvp in map)
        {
            AddWatchElement(kvp.Key, kvp.Value.Value, kvp.Value.Timestamp);
        }

        UpdateWatchButtonCount();
    }

    private void AddWatchElement(string key, string value, DateTime time)
    {
        var row = new VisualElement();
        row.AddToClassList("hb-watch-row");

        var keyLabel = new Label(key);
        keyLabel.AddToClassList("hb-watch-key");

        var valLabel = new Label(value);
        valLabel.AddToClassList("hb-watch-val");

        var timeLabel = new Label(time.ToString("HH:mm:ss"));
        timeLabel.AddToClassList("hb-watch-time");

        row.Add(keyLabel);
        row.Add(valLabel);
        row.Add(timeLabel);

        watchScrollView.Add(row);
        watchRowMap[key] = (row, valLabel, timeLabel);
    }

    private void UpdateWatchButtonCount()
    {
        if (viewWatchBtn != null)
        {
            viewWatchBtn.text = $"Watch [{HLogCore.Watches.Count}]";
        }
    }

    #endregion

    #region Tag & Channel Filtering

    private void RefreshAllTags()
    {
        if (tagScrollView == null) return;

        tagScrollView.Clear();
        createdTagButtons.Clear();

        tagAllButton = new Button(() => SetTagFilter(string.Empty)) { text = "All Tags" };
        tagAllButton.AddToClassList("hb-tag-btn");
        if (string.IsNullOrEmpty(currentTagFilter))
        {
            tagAllButton.AddToClassList("hb-tag-btn-active");
        }
        tagScrollView.Add(tagAllButton);

        foreach (var tag in HLogCore.KnownTags)
        {
            AddTagButton(tag);
        }
    }

    private void HandleTagDiscovered(string tag)
    {
        if (tagScrollView == null || createdTagButtons.ContainsKey(tag)) return;
        AddTagButton(tag);
    }

    private void AddTagButton(string tag)
    {
        var btn = new Button(() => SetTagFilter(tag)) { text = $"#{tag}#" };
        btn.AddToClassList("hb-tag-btn");
        if (string.Equals(currentTagFilter, tag, StringComparison.OrdinalIgnoreCase))
        {
            btn.AddToClassList("hb-tag-btn-active");
        }

        createdTagButtons[tag] = btn;
        tagScrollView.Add(btn);
    }

    private void SetTagFilter(string tag)
    {
        currentTagFilter = tag;

        if (tagAllButton != null)
        {
            if (string.IsNullOrEmpty(tag))
                tagAllButton.AddToClassList("hb-tag-btn-active");
            else
                tagAllButton.RemoveFromClassList("hb-tag-btn-active");
        }

        foreach (var kvp in createdTagButtons)
        {
            if (!string.IsNullOrEmpty(tag) && string.Equals(kvp.Key, tag, StringComparison.OrdinalIgnoreCase))
                kvp.Value.AddToClassList("hb-tag-btn-active");
            else
                kvp.Value.RemoveFromClassList("hb-tag-btn-active");
        }

        ApplyFilterToVisibleLogs();
    }

    #endregion

    #region Search Filtering

    private void OnSearchValueChanged(ChangeEvent<string> evt)
    {
        currentSearchQuery = evt.newValue != null ? evt.newValue.Trim() : string.Empty;
        ApplyFilterToVisibleLogs();
    }

    #endregion

    #region Console UI & Logs

    public void OpenConsole()
    {
        if (consoleWindow != null)
        {
            consoleWindow.RemoveFromClassList("hb-hidden");
        }
    }

    public void CloseConsole()
    {
        if (consoleWindow != null)
        {
            consoleWindow.AddToClassList("hb-hidden");
        }
    }

    public void ToggleConsole()
    {
        if (IsConsoleOpen)
        {
            CloseConsole();
        }
        else
        {
            OpenConsole();
        }
    }

    public void ClearConsole()
    {
        HLogCore.Clear();
        if (detailLabel != null)
        {
            detailLabel.text = "Logs cleared.";
        }
    }

    private void ToggleAutoScroll()
    {
        autoScrollEnabled = !autoScrollEnabled;
        if (autoScrollButton != null)
        {
            autoScrollButton.text = autoScrollEnabled ? "AutoScroll: ON" : "AutoScroll: OFF";
            if (autoScrollEnabled)
                autoScrollButton.AddToClassList("hb-filter-active");
            else
                autoScrollButton.RemoveFromClassList("hb-filter-active");
        }
    }

    public void SetFilter(int filterIndex)
    {
        SetFilter((LogFilter)filterIndex);
    }

    private void SetFilter(LogFilter filter)
    {
        currentFilter = filter;
        UpdateFilterButtonStyles();
        ApplyFilterToVisibleLogs();
    }

    private void UpdateFilterButtonStyles()
    {
        filterAllButton?.RemoveFromClassList("hb-filter-active");
        filterInfoButton?.RemoveFromClassList("hb-filter-active");
        filterWarnButton?.RemoveFromClassList("hb-filter-active");
        filterErrorButton?.RemoveFromClassList("hb-filter-active");

        switch (currentFilter)
        {
            case LogFilter.All:
                filterAllButton?.AddToClassList("hb-filter-active");
                break;
            case LogFilter.Info:
                filterInfoButton?.AddToClassList("hb-filter-active");
                break;
            case LogFilter.Warning:
                filterWarnButton?.AddToClassList("hb-filter-active");
                break;
            case LogFilter.Error:
                filterErrorButton?.AddToClassList("hb-filter-active");
                break;
        }
    }

    private void RefreshAllLogs()
    {
        if (logScrollView == null) return;

        logScrollView.Clear();
        createdLogRows.Clear();

        var history = HLogCore.History;
        for (int i = 0; i < history.Count; i++)
        {
            AddLogElement(history[i]);
        }

        UpdateOpenButtonText(history.Count);
    }

    private void HandleLogAdded(HLogCore.LogEntry entry)
    {
        if (logScrollView == null) return;

        var row = AddLogElement(entry);
        UpdateOpenButtonText(HLogCore.History.Count);

        if (autoScrollEnabled && row != null && currentViewMode == ConsoleViewMode.Logs)
        {
            logScrollView.schedule.Execute(() =>
            {
                if (logScrollView != null && row != null)
                {
                    logScrollView.ScrollTo(row);
                }
            });
        }
    }

    private void HandleLogsCleared()
    {
        if (logScrollView != null)
        {
            logScrollView.Clear();
        }
        createdLogRows.Clear();
        selectedRow = null;
        RefreshAllWatches();
        UpdateOpenButtonText(0);
    }

    private VisualElement AddLogElement(HLogCore.LogEntry entry)
    {
        if (createdLogRows.Count >= MaxUiLogCount)
        {
            var oldest = createdLogRows[0];
            if (oldest == selectedRow) selectedRow = null;
            logScrollView.Remove(oldest);
            createdLogRows.RemoveAt(0);
        }

        var row = new VisualElement();
        row.AddToClassList("hb-log-row");

        var timeLabel = new Label(entry.TimeString);
        timeLabel.AddToClassList("hb-log-time");

        var typeTagLabel = new Label();
        typeTagLabel.AddToClassList("hb-log-type-tag");

        switch (entry.LogType)
        {
            case LogType.Warning:
                typeTagLabel.text = "[WARN]";
                typeTagLabel.AddToClassList("hb-type-warn");
                break;
            case LogType.Error:
            case LogType.Assert:
            case LogType.Exception:
                typeTagLabel.text = "[ERR]";
                typeTagLabel.AddToClassList("hb-type-error");
                break;
            default:
                typeTagLabel.text = "[INFO]";
                typeTagLabel.AddToClassList("hb-type-info");
                break;
        }

        row.Add(timeLabel);
        row.Add(typeTagLabel);

        if (!string.IsNullOrEmpty(entry.Tag))
        {
            var channelTag = new Label($"[{entry.Tag}]");
            channelTag.AddToClassList("hb-log-channel-tag");
            row.Add(channelTag);
        }

        var msgLabel = new Label(entry.Message);
        msgLabel.AddToClassList("hb-log-message");
        if (entry.LogType == LogType.Warning)
            msgLabel.AddToClassList("hb-text-warn");
        else if (entry.LogType == LogType.Error || entry.LogType == LogType.Exception)
            msgLabel.AddToClassList("hb-text-error");
        else
            msgLabel.AddToClassList("hb-text-info");

        row.Add(msgLabel);
        row.userData = entry;

        HLogCore.LogEntry capturedEntry = entry;
        row.RegisterCallback<ClickEvent>(evt =>
        {
            if (selectedRow != null)
            {
                selectedRow.RemoveFromClassList("hb-log-row-selected");
            }
            selectedRow = row;
            selectedRow.AddToClassList("hb-log-row-selected");

            if (detailLabel != null)
            {
                string tagInfo = !string.IsNullOrEmpty(capturedEntry.Tag) ? $" [{capturedEntry.Tag}]" : "";
                string details = $"[{capturedEntry.LogType}]{tagInfo} {capturedEntry.TimeString}\n{capturedEntry.Message}";

                string cleanedStack = HLogCore.FilterStackTrace(capturedEntry.StackTrace);
                if (!string.IsNullOrEmpty(cleanedStack))
                {
                    details += $"\n\n--- Stack Trace ---\n{cleanedStack}";
                }
                detailLabel.text = details;
            }
        });

        bool isVisible = EvaluateLogVisibility(entry);
        row.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

        createdLogRows.Add(row);
        logScrollView.Add(row);

        return row;
    }

    private void CopySelectedDetail()
    {
        if (detailLabel != null && !string.IsNullOrEmpty(detailLabel.text))
        {
            GUIUtility.systemCopyBuffer = detailLabel.text;
        }
    }

    private void ApplyFilterToVisibleLogs()
    {
        foreach (var row in createdLogRows)
        {
            if (row.userData is HLogCore.LogEntry entry)
            {
                bool visible = EvaluateLogVisibility(entry);
                row.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }

    private bool EvaluateLogVisibility(HLogCore.LogEntry entry)
    {
        if (!IsLogTypeVisible(entry.LogType, currentFilter))
            return false;

        if (!string.IsNullOrEmpty(currentTagFilter))
        {
            if (!string.Equals(entry.Tag, currentTagFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(currentSearchQuery))
        {
            if (currentSearchQuery.StartsWith("!") && currentSearchQuery.Length > 1)
            {
                string excludeWord = currentSearchQuery.Substring(1);
                if (entry.RawMessage.IndexOf(excludeWord, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            else
            {
                if (entry.RawMessage.IndexOf(currentSearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
        }

        return true;
    }

    private bool IsLogTypeVisible(LogType type, LogFilter filter)
    {
        if (filter == LogFilter.All) return true;
        if (filter == LogFilter.Info && type == LogType.Log) return true;
        if (filter == LogFilter.Warning && type == LogType.Warning) return true;
        if (filter == LogFilter.Error && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) return true;
        return false;
    }

    private void UpdateOpenButtonText(int count)
    {
        if (openButton != null)
        {
            openButton.text = $"Logs [{count}]";
        }
    }

    #endregion
}
