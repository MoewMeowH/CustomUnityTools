using System;
using System.Collections.Generic;
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit 기반 인게임 콘솔 UI 및 성능 모니터링 컨트롤러입니다.
/// Unity 6+ 권장 사양인 PanelRenderer(또는 하위 호환 UIDocument) 컴포넌트와 연동되어 동작하며,
/// 실시간 Watch, 동적 태그 필터, 검색/제외 필터, 스택트레이스 정제 기능을 지원합니다.
/// </summary>
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
        Watch,
        Settings,
        External
    }

    [Header("UI Toolkit")]
    [Tooltip("비워둘 경우 PanelRenderer/UIDocument의 VisualTreeAsset 또는 InGameConsoleSettings 설정을 자동으로 사용합니다.")]
    [SerializeField] private VisualTreeAsset consoleVisualTree;
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Performance Monitor")]
    [SerializeField] private bool showFpsGraph = true;

    [Header("Log List")]
    [Tooltip("맨 아래를 보고 있을 때 새 로그를 따라 자동으로 스크롤합니다.")]
    [SerializeField] private bool autoScroll = true;

    // 사용자가 맨 아래를 보고 있는지 여부. 위로 스크롤하면 false가 되어 새 로그를 따라가지 않는다
    private bool isStickToBottom = true;
    // 코드에서 스크롤 값을 바꾸는 중에는 사용자 스크롤로 판단하지 않는다
    private bool isProgrammaticScroll;
    private const float BottomTolerance = 2f;

    private const int MaxUiLogCount = 1000;
    private const int GraphSampleCount = 80;

#if UNITY_6000_5_OR_NEWER
    private PanelRenderer panelRenderer;
#endif
    private UIDocument uiDocument;
    private VisualElement rootElement;
    private int handledUIVersion = -1;

    // View Mode elements
    private Button viewLogsBtn;
    private Button viewWatchBtn;
    private Button viewSettingsBtn;
    private ConsoleViewMode currentViewMode = ConsoleViewMode.Logs;

    // 다른 패키지가 붙이는 탭 (예: HB_Cheat). 패널 재로드 시 다시 붙인다.
    private sealed class ExternalView
    {
        public string Name;
        public VisualElement Content;
        public Button Button;
        public Action OnClicked;
    }
    private readonly List<ExternalView> externalViews = new List<ExternalView>();
    private ExternalView currentExternalView;

    // Header Toolbar elements
    private Button openButton;
    private VisualElement consoleWindow;
    private Button closeButton;
    private Button clearButton;
    private Button filterAllButton;
    private Button filterInfoButton;
    private Button filterWarnButton;
    private Button filterErrorButton;
    private Button bottomBtn;
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
    private ScrollView settingsScrollView;
    private VisualElement detailPanel;
    private Label detailLabel;
    private Button copyDetailBtn;
    private Button toggleDetailSizeBtn;
    private Button collapseDetailBtn;
    private bool isDetailExpanded = false;
    private bool isDetailCollapsed = false;

    // Settings elements
    private Button toggleLogEnableBtn;
    private Button toggleFpsWidgetBtn;
    private Button toggleAutoScrollBtn;
    private VisualElement channelTogglesContainer;
    private Button clearAllLogsSettingBtn;

    // FPS & Graph elements
    private VisualElement fpsWidget;
    private Label fpsLabel;
    private Label msLabel;
    private VisualElement fpsGraphCanvas;

    // State & Caches
    private LogFilter currentFilter = LogFilter.All;
    private string currentTagFilter = string.Empty;
    private string currentSearchQuery = string.Empty;

    private readonly List<VisualElement> createdLogRows = new List<VisualElement>();
    private readonly Dictionary<string, (VisualElement row, Label valLabel, Label timeLabel)> watchRowMap =
        new Dictionary<string, (VisualElement row, Label valLabel, Label timeLabel)>(StringComparer.OrdinalIgnoreCase);
    private VisualElement selectedRow;

    // Delegate caches for clean event unregistration
    private Action onLogsModeClicked;
    private Action onWatchModeClicked;
    private Action onSettingsModeClicked;
    private Action onFilterAllClicked;
    private Action onFilterInfoClicked;
    private Action onFilterWarnClicked;
    private Action onFilterErrorClicked;
    private Action onClearSearchClicked;
    private EventCallback<ChangeEvent<string>> onSearchChangedCallback;

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
#if UNITY_6000_5_OR_NEWER
    public PanelRenderer PanelRendererComponent => panelRenderer != null ? panelRenderer : (panelRenderer = GetComponent<PanelRenderer>());
#endif
    public UIDocument Document => uiDocument != null ? uiDocument : (uiDocument = GetComponent<UIDocument>());
    public VisualElement RootElement => rootElement;

    private void Awake()
    {
#if UNITY_6000_5_OR_NEWER
        panelRenderer = GetComponent<PanelRenderer>();
#endif
        uiDocument = GetComponent<UIDocument>();

        var settings = InGameConsoleSettings.Instance;
        if (settings != null)
        {
            dontDestroyOnLoad = settings.DontDestroyOnLoad;
            showFpsGraph = settings.ShowFpsGraph;
            autoScroll = settings.AutoScroll;
        }

        if (dontDestroyOnLoad && transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnEnable()
    {
#if UNITY_6000_5_OR_NEWER
        if (panelRenderer == null)
            panelRenderer = GetComponent<PanelRenderer>();
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (panelRenderer == null && uiDocument == null)
        {
            panelRenderer = gameObject.AddComponent<PanelRenderer>();
        }

        InitializePanelHost();

        if (panelRenderer != null)
        {
            panelRenderer.RegisterUIReloadCallback(OnPanelReloaded);
        }
        else if (uiDocument != null)
        {
            InitializeWithRoot(uiDocument.rootVisualElement);
        }
#else
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            uiDocument = gameObject.AddComponent<UIDocument>();
        }

        InitializePanelHost();

        if (uiDocument != null)
        {
            InitializeWithRoot(uiDocument.rootVisualElement);
        }
#endif

        HLogCore.OnLogAdded += HandleLogAdded;
        HLogCore.OnLogsCleared += HandleLogsCleared;
        HLogCore.OnWatchUpdated += HandleWatchUpdated;
        HLogCore.OnWatchRemoved += HandleWatchRemoved;
        HLogCore.OnTagDiscovered += HandleTagDiscovered;
    }

#if UNITY_6000_5_OR_NEWER
    private void OnPanelReloaded(PanelRenderer renderer, VisualElement root, int version)
    {
        if (version == handledUIVersion) return;
        handledUIVersion = version;

        InitializeWithRoot(root);
    }
#endif

    private void InitializeWithRoot(VisualElement root)
    {
        rootElement = root;
        if (rootElement == null) return;

        EnsureVisualTreeCloned();
        EnsureStyleSheetAttached();

        BindUIElements();
        RegisterCallbacks();
        AttachAllExternalViews();

        RefreshAllLogs();
        RefreshAllWatches();
        RefreshAllTags();

        if (fpsGraphCanvas != null)
        {
            fpsGraphCanvas.generateVisualContent -= DrawFrameTimeGraph;
            fpsGraphCanvas.generateVisualContent += DrawFrameTimeGraph;
        }
    }

    private void InitializePanelHost()
    {
        var settings = InGameConsoleSettings.Instance;

#if UNITY_6000_5_OR_NEWER
        if (panelRenderer != null)
        {
            if (panelRenderer.visualTreeAsset == null)
            {
                if (consoleVisualTree != null)
                    panelRenderer.visualTreeAsset = consoleVisualTree;
                else if (settings != null && settings.ConsoleVisualTree != null)
                    panelRenderer.visualTreeAsset = settings.ConsoleVisualTree;
                else
                    panelRenderer.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");

#if UNITY_EDITOR
                if (panelRenderer.visualTreeAsset == null)
                {
                    string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
                    if (vGuids != null && vGuids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                        panelRenderer.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                    }
                }
#endif
            }

            if (panelRenderer.panelSettings == null)
                panelRenderer.panelSettings = InGameConsole.ResolvePanelSettings(settings);
            else
                InGameConsole.EnsureThemeStyleSheet(panelRenderer.panelSettings);

            if (panelRenderer.sortingOrder < 1000)
            {
                panelRenderer.sortingOrder = 9999;
            }
        }
        else
#endif
        if (uiDocument != null)
        {
            if (uiDocument.visualTreeAsset == null)
            {
                if (consoleVisualTree != null)
                    uiDocument.visualTreeAsset = consoleVisualTree;
                else if (settings != null && settings.ConsoleVisualTree != null)
                    uiDocument.visualTreeAsset = settings.ConsoleVisualTree;
                else
                    uiDocument.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");

#if UNITY_EDITOR
                if (uiDocument.visualTreeAsset == null)
                {
                    string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
                    if (vGuids != null && vGuids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                        uiDocument.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                    }
                }
#endif
            }

            if (uiDocument.panelSettings == null)
                uiDocument.panelSettings = InGameConsole.ResolvePanelSettings(settings);
            else
                InGameConsole.EnsureThemeStyleSheet(uiDocument.panelSettings);

            if (uiDocument.sortingOrder < 1000)
            {
                uiDocument.sortingOrder = 9999;
            }
        }
    }

    private void EnsureVisualTreeCloned()
    {
        if (rootElement == null) return;

        VisualTreeAsset vta = null;
#if UNITY_6000_5_OR_NEWER
        if (panelRenderer != null) vta = panelRenderer.visualTreeAsset;
        else
#endif
        if (uiDocument != null) vta = uiDocument.visualTreeAsset;
        if (vta == null) vta = consoleVisualTree;

        // VisualTree가 자동 클론되지 않은 경우 직접 복제
        if (rootElement.Q<VisualElement>("rootContainer") == null && vta != null)
        {
            vta.CloneTree(rootElement);
        }
    }

    private void EnsureStyleSheetAttached()
    {
        if (rootElement == null) return;

        StyleSheet uss = Resources.Load<StyleSheet>("HB_InGameConsole");
#if UNITY_EDITOR
        if (uss == null)
        {
            string[] ussGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:StyleSheet");
            if (ussGuids != null && ussGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(ussGuids[0]);
                uss = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
        }
#endif
        if (uss != null && !rootElement.styleSheets.Contains(uss))
        {
            rootElement.styleSheets.Add(uss);
        }
    }

    private void OnDisable()
    {
        HLogCore.OnLogAdded -= HandleLogAdded;
        HLogCore.OnLogsCleared -= HandleLogsCleared;
        HLogCore.OnWatchUpdated -= HandleWatchUpdated;
        HLogCore.OnWatchRemoved -= HandleWatchRemoved;
        HLogCore.OnTagDiscovered -= HandleTagDiscovered;

        if (fpsGraphCanvas != null)
        {
            fpsGraphCanvas.generateVisualContent -= DrawFrameTimeGraph;
        }

#if UNITY_6000_5_OR_NEWER
        if (panelRenderer != null)
        {
            panelRenderer.UnregisterUIReloadCallback(OnPanelReloaded);
        }
#endif

        UnregisterCallbacks();
    }

    private void Update()
    {
        // PlayerLoop에 넣은 처리 시스템이 다른 코드의 SetPlayerLoop로 빠져도 콘솔이 떠 있는 동안은 처리되도록
        HLogCore.FlushPendingLogs();
        HLogCore.PollWatchGetters();
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
        viewSettingsBtn = rootElement.Q<Button>("viewSettingsBtn");

        filterAllButton = rootElement.Q<Button>("filterAllBtn");
        filterInfoButton = rootElement.Q<Button>("filterInfoBtn");
        filterWarnButton = rootElement.Q<Button>("filterWarnBtn");
        filterErrorButton = rootElement.Q<Button>("filterErrorBtn");
        bottomBtn = rootElement.Q<Button>("bottomBtn");

        subToolBar = rootElement.Q<VisualElement>("subToolBar");
        searchField = rootElement.Q<TextField>("searchField");
        clearSearchBtn = rootElement.Q<Button>("clearSearchBtn");
        tagScrollView = rootElement.Q<ScrollView>("tagScrollView");

        logScrollView = rootElement.Q<ScrollView>("logScrollView");
        watchScrollView = rootElement.Q<ScrollView>("watchScrollView");
        settingsScrollView = rootElement.Q<ScrollView>("settingsScrollView");
        detailPanel = rootElement.Q<VisualElement>("detailPanel");

        toggleLogEnableBtn = rootElement.Q<Button>("toggleLogEnableBtn");
        toggleFpsWidgetBtn = rootElement.Q<Button>("toggleFpsWidgetBtn");
        toggleAutoScrollBtn = rootElement.Q<Button>("toggleAutoScrollBtn");
        channelTogglesContainer = rootElement.Q<VisualElement>("channelTogglesContainer");
        clearAllLogsSettingBtn = rootElement.Q<Button>("clearAllLogsSettingBtn");

        detailLabel = rootElement.Q<Label>("detailLabel");
        copyDetailBtn = rootElement.Q<Button>("copyDetailBtn");
        toggleDetailSizeBtn = rootElement.Q<Button>("toggleDetailSizeBtn");
        collapseDetailBtn = rootElement.Q<Button>("collapseDetailBtn");
        consoleStatsLabel = rootElement.Q<Label>("consoleStatsLabel");

        fpsWidget = rootElement.Q<VisualElement>("fpsWidget");
        fpsLabel = rootElement.Q<Label>("fpsLabel");
        msLabel = rootElement.Q<Label>("msLabel");
        fpsGraphCanvas = rootElement.Q<VisualElement>("fpsGraphCanvas");

        // 레이아웃 고정 제약 설정 (로그 증가 시 디테일 패널 축소/소멸 완전 방지)
        if (detailPanel != null)
        {
            detailPanel.style.flexShrink = 0;
        }
        if (logScrollView != null)
        {
            logScrollView.style.flexGrow = 1;
            logScrollView.style.flexShrink = 1;
            logScrollView.style.flexBasis = 0;
            logScrollView.style.minHeight = 0;
        }
        if (watchScrollView != null)
        {
            watchScrollView.style.flexGrow = 1;
            watchScrollView.style.flexShrink = 1;
            watchScrollView.style.flexBasis = 0;
            watchScrollView.style.minHeight = 0;
        }
        if (settingsScrollView != null)
        {
            settingsScrollView.style.flexGrow = 1;
            settingsScrollView.style.flexShrink = 1;
            settingsScrollView.style.flexBasis = 0;
            settingsScrollView.style.minHeight = 0;
        }

        if (fpsWidget != null && !showFpsGraph)
        {
            fpsWidget.style.display = DisplayStyle.None;
        }
    }

    private void RegisterCallbacks()
    {
        onLogsModeClicked = () => SwitchViewMode(ConsoleViewMode.Logs);
        onWatchModeClicked = () => SwitchViewMode(ConsoleViewMode.Watch);
        onSettingsModeClicked = () => SwitchViewMode(ConsoleViewMode.Settings);

        onFilterAllClicked = () => SetFilter(LogFilter.All);
        onFilterInfoClicked = () => SetFilter(LogFilter.Info);
        onFilterWarnClicked = () => SetFilter(LogFilter.Warning);
        onFilterErrorClicked = () => SetFilter(LogFilter.Error);

        onClearSearchClicked = () => { if (searchField != null) searchField.value = string.Empty; };
        onSearchChangedCallback = OnSearchValueChanged;

        if (openButton != null) openButton.clicked += OpenConsole;
        if (closeButton != null) closeButton.clicked += CloseConsole;
        if (clearButton != null) clearButton.clicked += ClearConsole;

        if (viewLogsBtn != null) viewLogsBtn.clicked += onLogsModeClicked;
        if (viewWatchBtn != null) viewWatchBtn.clicked += onWatchModeClicked;
        if (viewSettingsBtn != null) viewSettingsBtn.clicked += onSettingsModeClicked;

        if (toggleLogEnableBtn != null) toggleLogEnableBtn.clicked += ToggleMasterLogEnable;
        if (toggleFpsWidgetBtn != null) toggleFpsWidgetBtn.clicked += ToggleFpsWidget;
        if (toggleAutoScrollBtn != null) toggleAutoScrollBtn.clicked += ToggleAutoScroll;
        if (clearAllLogsSettingBtn != null) clearAllLogsSettingBtn.clicked += ClearConsole;

        if (filterAllButton != null) filterAllButton.clicked += onFilterAllClicked;
        if (filterInfoButton != null) filterInfoButton.clicked += onFilterInfoClicked;
        if (filterWarnButton != null) filterWarnButton.clicked += onFilterWarnClicked;
        if (filterErrorButton != null) filterErrorButton.clicked += onFilterErrorClicked;
        if (bottomBtn != null) bottomBtn.clicked += ScrollToBottom;

        if (logScrollView != null)
        {
            logScrollView.verticalScroller.valueChanged += OnLogScrollValueChanged;
            logScrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(OnLogContentGeometryChanged);
        }

        if (searchField != null)
        {
            searchField.RegisterValueChangedCallback(onSearchChangedCallback);
        }
        if (clearSearchBtn != null)
        {
            clearSearchBtn.clicked += onClearSearchClicked;
        }

        if (copyDetailBtn != null)
        {
            copyDetailBtn.clicked += CopySelectedDetail;
        }
        if (toggleDetailSizeBtn != null)
        {
            toggleDetailSizeBtn.clicked += ToggleDetailSize;
        }
        if (collapseDetailBtn != null)
        {
            collapseDetailBtn.clicked += ToggleDetailCollapsed;
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

        if (viewLogsBtn != null && onLogsModeClicked != null) viewLogsBtn.clicked -= onLogsModeClicked;
        if (viewWatchBtn != null && onWatchModeClicked != null) viewWatchBtn.clicked -= onWatchModeClicked;
        if (viewSettingsBtn != null && onSettingsModeClicked != null) viewSettingsBtn.clicked -= onSettingsModeClicked;

        if (toggleLogEnableBtn != null) toggleLogEnableBtn.clicked -= ToggleMasterLogEnable;
        if (toggleFpsWidgetBtn != null) toggleFpsWidgetBtn.clicked -= ToggleFpsWidget;
        if (toggleAutoScrollBtn != null) toggleAutoScrollBtn.clicked -= ToggleAutoScroll;
        if (clearAllLogsSettingBtn != null) clearAllLogsSettingBtn.clicked -= ClearConsole;

        if (filterAllButton != null && onFilterAllClicked != null) filterAllButton.clicked -= onFilterAllClicked;
        if (filterInfoButton != null && onFilterInfoClicked != null) filterInfoButton.clicked -= onFilterInfoClicked;
        if (filterWarnButton != null && onFilterWarnClicked != null) filterWarnButton.clicked -= onFilterWarnClicked;
        if (filterErrorButton != null && onFilterErrorClicked != null) filterErrorButton.clicked -= onFilterErrorClicked;
        if (bottomBtn != null) bottomBtn.clicked -= ScrollToBottom;

        if (logScrollView != null)
        {
            logScrollView.verticalScroller.valueChanged -= OnLogScrollValueChanged;
            logScrollView.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnLogContentGeometryChanged);
        }

        if (searchField != null && onSearchChangedCallback != null)
        {
            searchField.UnregisterValueChangedCallback(onSearchChangedCallback);
        }
        if (clearSearchBtn != null && onClearSearchClicked != null)
        {
            clearSearchBtn.clicked -= onClearSearchClicked;
        }

        if (copyDetailBtn != null) copyDetailBtn.clicked -= CopySelectedDetail;
        if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.clicked -= ToggleDetailSize;
        if (collapseDetailBtn != null) collapseDetailBtn.clicked -= ToggleDetailCollapsed;

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

        viewLogsBtn?.RemoveFromClassList("hb-filter-active");
        viewWatchBtn?.RemoveFromClassList("hb-filter-active");
        viewSettingsBtn?.RemoveFromClassList("hb-filter-active");

        logScrollView?.AddToClassList("hb-hidden");
        watchScrollView?.AddToClassList("hb-hidden");
        settingsScrollView?.AddToClassList("hb-hidden");
        subToolBar?.AddToClassList("hb-hidden");
        detailPanel?.AddToClassList("hb-hidden");
        foreach (var view in externalViews)
        {
            view.Button?.RemoveFromClassList("hb-filter-active");
            view.Content?.AddToClassList("hb-hidden");
        }

        if (mode == ConsoleViewMode.Logs)
        {
            viewLogsBtn?.AddToClassList("hb-filter-active");
            logScrollView?.RemoveFromClassList("hb-hidden");
            subToolBar?.RemoveFromClassList("hb-hidden");
            detailPanel?.RemoveFromClassList("hb-hidden");
        }
        else if (mode == ConsoleViewMode.Watch)
        {
            viewWatchBtn?.AddToClassList("hb-filter-active");
            watchScrollView?.RemoveFromClassList("hb-hidden");
            detailPanel?.RemoveFromClassList("hb-hidden");
            RefreshAllWatches();
        }
        else if (mode == ConsoleViewMode.Settings)
        {
            viewSettingsBtn?.AddToClassList("hb-filter-active");
            settingsScrollView?.RemoveFromClassList("hb-hidden");
            RefreshSettingsView();
        }
        else if (mode == ConsoleViewMode.External && currentExternalView != null)
        {
            currentExternalView.Button?.AddToClassList("hb-filter-active");
            currentExternalView.Content?.RemoveFromClassList("hb-hidden");
        }
    }

    #region External Views

    /// <summary>
    /// 콘솔 상단 탭(Logs / Watch / Settings 옆)에 외부 뷰를 추가합니다. 같은 이름이면 내용을 교체합니다.
    /// </summary>
    public void AddExternalView(string viewName, VisualElement content)
    {
        if (string.IsNullOrEmpty(viewName) || content == null) return;

        var view = externalViews.Find(v => v.Name == viewName);
        if (view == null)
        {
            view = new ExternalView { Name = viewName };
            externalViews.Add(view);
        }
        else
        {
            DetachExternalView(view);
        }

        view.Content = content;
        if (rootElement != null) AttachExternalView(view);
    }

    public void RemoveExternalView(string viewName)
    {
        var view = externalViews.Find(v => v.Name == viewName);
        if (view == null) return;

        DetachExternalView(view);
        externalViews.Remove(view);
        if (currentExternalView == view)
        {
            currentExternalView = null;
            if (currentViewMode == ConsoleViewMode.External) SwitchViewMode(ConsoleViewMode.Logs);
        }
    }

    /// <summary>콘솔을 열고 해당 외부 뷰로 전환합니다.</summary>
    public void ShowExternalView(string viewName)
    {
        var view = externalViews.Find(v => v.Name == viewName);
        if (view == null) return;

        OpenConsole();
        currentExternalView = view;
        SwitchViewMode(ConsoleViewMode.External);
    }

    public bool IsExternalViewActive(string viewName)
    {
        return IsConsoleOpen && currentViewMode == ConsoleViewMode.External
            && currentExternalView != null && currentExternalView.Name == viewName;
    }

    private void AttachAllExternalViews()
    {
        foreach (var view in externalViews)
        {
            DetachExternalView(view);
            AttachExternalView(view);
        }
    }

    private void AttachExternalView(ExternalView view)
    {
        if (consoleWindow == null) return;

        var button = new Button { text = view.Name };
        button.AddToClassList("hb-filter-btn");
        view.OnClicked = () =>
        {
            currentExternalView = view;
            SwitchViewMode(ConsoleViewMode.External);
        };
        button.clicked += view.OnClicked;
        view.Button = button;

        // 마지막 뷰 탭(Settings 또는 앞서 붙은 외부 탭) 뒤에 넣는다
        var anchor = (VisualElement)viewSettingsBtn;
        int index = externalViews.IndexOf(view);
        for (int i = index - 1; i >= 0; i--)
        {
            if (externalViews[i].Button != null && externalViews[i].Button.parent != null)
            {
                anchor = externalViews[i].Button;
                break;
            }
        }
        if (anchor != null && anchor.parent != null)
            anchor.parent.Insert(anchor.parent.IndexOf(anchor) + 1, button);

        var content = view.Content;
        content.style.flexGrow = 1;
        content.style.flexShrink = 1;
        content.style.flexBasis = 0;
        content.style.minHeight = 0;
        int contentIndex = settingsScrollView != null && settingsScrollView.parent == consoleWindow
            ? consoleWindow.IndexOf(settingsScrollView) + 1
            : consoleWindow.childCount;
        consoleWindow.Insert(Mathf.Min(contentIndex + index, consoleWindow.childCount), content);

        bool active = currentViewMode == ConsoleViewMode.External && currentExternalView == view;
        if (active)
        {
            button.AddToClassList("hb-filter-active");
            content.RemoveFromClassList("hb-hidden");
        }
        else
        {
            content.AddToClassList("hb-hidden");
        }
    }

    private static void DetachExternalView(ExternalView view)
    {
        if (view.Button != null)
        {
            if (view.OnClicked != null) view.Button.clicked -= view.OnClicked;
            view.Button.RemoveFromHierarchy();
            view.Button = null;
        }
        view.Content?.RemoveFromHierarchy();
    }

    #endregion

    #region Settings View Handlers

    private void RefreshSettingsView()
    {
        UpdateLogEnableButtonUI();
        UpdateFpsWidgetButtonUI();
        UpdateAutoScrollButtonUI();
        RefreshChannelToggles();
    }

    private void UpdateLogEnableButtonUI()
    {
        if (toggleLogEnableBtn == null) return;
        bool isEnabled = HLogCore.IsLogEnabled;
        toggleLogEnableBtn.text = isEnabled ? "Log: ON" : "Log: OFF";
        if (isEnabled)
        {
            toggleLogEnableBtn.AddToClassList("hb-toggle-on");
            toggleLogEnableBtn.RemoveFromClassList("hb-toggle-off");
        }
        else
        {
            toggleLogEnableBtn.AddToClassList("hb-toggle-off");
            toggleLogEnableBtn.RemoveFromClassList("hb-toggle-on");
        }
    }

    private void ToggleMasterLogEnable()
    {
        HLogCore.IsLogEnabled = !HLogCore.IsLogEnabled;
        UpdateLogEnableButtonUI();
    }

    private void UpdateFpsWidgetButtonUI()
    {
        if (toggleFpsWidgetBtn == null) return;
        bool isVisible = fpsWidget != null && fpsWidget.style.display != DisplayStyle.None;
        toggleFpsWidgetBtn.text = isVisible ? "FPS: ON" : "FPS: OFF";
        if (isVisible)
        {
            toggleFpsWidgetBtn.AddToClassList("hb-toggle-on");
            toggleFpsWidgetBtn.RemoveFromClassList("hb-toggle-off");
        }
        else
        {
            toggleFpsWidgetBtn.AddToClassList("hb-toggle-off");
            toggleFpsWidgetBtn.RemoveFromClassList("hb-toggle-on");
        }
    }

    private void UpdateAutoScrollButtonUI()
    {
        if (toggleAutoScrollBtn == null) return;
        toggleAutoScrollBtn.text = autoScroll ? "Scroll: ON" : "Scroll: OFF";
        if (autoScroll)
        {
            toggleAutoScrollBtn.AddToClassList("hb-toggle-on");
            toggleAutoScrollBtn.RemoveFromClassList("hb-toggle-off");
        }
        else
        {
            toggleAutoScrollBtn.AddToClassList("hb-toggle-off");
            toggleAutoScrollBtn.RemoveFromClassList("hb-toggle-on");
        }
    }

    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
    }

    private void ToggleFpsWidget()
    {
        if (fpsWidget == null) return;
        bool isVisible = fpsWidget.style.display != DisplayStyle.None;
        fpsWidget.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
        UpdateFpsWidgetButtonUI();
    }

    private void RefreshChannelToggles()
    {
        if (channelTogglesContainer == null) return;
        channelTogglesContainer.Clear();

        var knownTags = HLogCore.KnownTags;
        if (knownTags.Count == 0)
        {
            HLogCore.DiscoverAllChannels();
            knownTags = HLogCore.KnownTags;
        }

        foreach (var tag in knownTags)
        {
            string tagName = tag;
            var channel = HLogCore.GetChannel(tagName);
            bool isChEnabled = channel.Enabled;

            var chipBtn = new Button();
            chipBtn.text = isChEnabled ? $"{tagName}: ON" : $"{tagName}: OFF";
            chipBtn.AddToClassList("hb-channel-chip");
            chipBtn.AddToClassList(isChEnabled ? "hb-toggle-on" : "hb-toggle-off");

            chipBtn.clicked += () =>
            {
                channel.Enabled = !channel.Enabled;
                bool nowEnabled = channel.Enabled;
                chipBtn.text = nowEnabled ? $"{tagName}: ON" : $"{tagName}: OFF";
                if (nowEnabled)
                {
                    chipBtn.AddToClassList("hb-toggle-on");
                    chipBtn.RemoveFromClassList("hb-toggle-off");
                }
                else
                {
                    chipBtn.AddToClassList("hb-toggle-off");
                    chipBtn.RemoveFromClassList("hb-toggle-on");
                }
            };

            channelTogglesContainer.Add(chipBtn);
        }
    }

    #endregion

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

    private void HandleWatchRemoved(string key)
    {
        RefreshAllWatches();
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

    /// <summary>
    /// 새 로그 자동 스크롤 사용 여부. 켜져 있어도 사용자가 위로 스크롤한 동안은 따라가지 않습니다.
    /// </summary>
    public bool AutoScroll
    {
        get => autoScroll;
        set
        {
            autoScroll = value;
            UpdateAutoScrollButtonUI();
            if (value) ScrollToBottom();
        }
    }

    /// <summary>
    /// 로그 목록의 가장 최신(맨 아래) 위치로 스크롤하고, 다시 새 로그를 따라가도록 합니다.
    /// </summary>
    public void ScrollToBottom()
    {
        isStickToBottom = true;
        if (logScrollView == null) return;

        // 레이아웃이 끝난 뒤에야 highValue가 갱신되므로 다음 틱에 이동
        logScrollView.schedule.Execute(SetLogScrollToBottom);
    }

    private void SetLogScrollToBottom()
    {
        if (logScrollView == null || logScrollView.verticalScroller == null) return;

        // 예약된 뒤 실행되기 전에 사용자가 위로 스크롤했으면 끌어내리지 않는다
        if (!isStickToBottom) return;

        isProgrammaticScroll = true;
        logScrollView.verticalScroller.value = logScrollView.verticalScroller.highValue;
        isProgrammaticScroll = false;
    }

    private bool IsLogScrollAtBottom()
    {
        var scroller = logScrollView.verticalScroller;
        return scroller.highValue <= scroller.lowValue || scroller.value >= scroller.highValue - BottomTolerance;
    }

    private void OnLogScrollValueChanged(float value)
    {
        if (isProgrammaticScroll || logScrollView == null) return;

        // 사용자가 위로 올리면 따라가기를 멈추고, 다시 맨 아래로 내리면 재개
        isStickToBottom = IsLogScrollAtBottom();
    }

    private void OnLogContentGeometryChanged(GeometryChangedEvent evt)
    {
        // 로그가 추가되어 내용 높이가 바뀌었을 때, 맨 아래를 보고 있었다면 따라 내려간다
        if (autoScroll && isStickToBottom && evt.oldRect.height != evt.newRect.height)
        {
            // ScrollView가 같은 이벤트에서 스크롤 범위를 먼저 갱신하므로 바로 이동하고,
            // 범위 갱신이 늦어지는 경우를 대비해 다음 틱에 한 번 더 맞춘다
            SetLogScrollToBottom();
            logScrollView.schedule.Execute(SetLogScrollToBottom);
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

        AddLogElement(entry);
        UpdateOpenButtonText(HLogCore.HistoryCount);
    }

    private void HandleLogsCleared()
    {
        if (logScrollView != null)
        {
            logScrollView.Clear();
        }
        createdLogRows.Clear();
        selectedRow = null;
        isStickToBottom = true;
        RefreshAllWatches();
        UpdateOpenButtonText(0);
    }

    private VisualElement AddLogElement(HLogCore.LogEntry entry)
    {
        if (createdLogRows.Count >= MaxUiLogCount)
        {
            var oldest = createdLogRows[0];
            if (oldest == selectedRow) selectedRow = null;

            float removedHeight = oldest.resolvedStyle.display == DisplayStyle.None
                ? 0f
                : oldest.layout.height + oldest.resolvedStyle.marginTop + oldest.resolvedStyle.marginBottom;

            logScrollView.Remove(oldest);
            createdLogRows.RemoveAt(0);

            if (!isStickToBottom && removedHeight > 0f && !float.IsNaN(removedHeight))
            {
                isProgrammaticScroll = true;
                logScrollView.verticalScroller.value = Mathf.Max(0f, logScrollView.verticalScroller.value - removedHeight);
                isProgrammaticScroll = false;
            }
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

            // 디테일 패널이 숨겨져 있었다면 자동으로 표시
            if (isDetailCollapsed)
            {
                SetDetailCollapsed(false);
            }

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

    private void ToggleDetailSize()
    {
        if (detailPanel == null) return;

        if (isDetailCollapsed)
        {
            SetDetailCollapsed(false);
        }

        isDetailExpanded = !isDetailExpanded;
        if (isDetailExpanded)
        {
            detailPanel.AddToClassList("hb-detail-panel-expanded");
            if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.text = "Normal";
        }
        else
        {
            detailPanel.RemoveFromClassList("hb-detail-panel-expanded");
            if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.text = "Expand";
        }
    }

    private void ToggleDetailCollapsed()
    {
        SetDetailCollapsed(!isDetailCollapsed);
    }

    private void SetDetailCollapsed(bool collapsed)
    {
        if (detailPanel == null) return;
        isDetailCollapsed = collapsed;
        if (isDetailCollapsed)
        {
            detailPanel.RemoveFromClassList("hb-detail-panel-expanded");
            detailPanel.AddToClassList("hb-detail-panel-collapsed");
            if (collapseDetailBtn != null) collapseDetailBtn.text = "Show";
            if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.style.display = DisplayStyle.None;
        }
        else
        {
            detailPanel.RemoveFromClassList("hb-detail-panel-collapsed");
            if (isDetailExpanded)
            {
                detailPanel.AddToClassList("hb-detail-panel-expanded");
                if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.text = "Normal";
            }
            else
            {
                if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.text = "Expand";
            }
            if (collapseDetailBtn != null) collapseDetailBtn.text = "Hide";
            if (toggleDetailSizeBtn != null) toggleDetailSizeBtn.style.display = DisplayStyle.Flex;
        }
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
