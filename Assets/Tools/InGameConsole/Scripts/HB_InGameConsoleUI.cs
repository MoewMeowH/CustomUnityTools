using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UITK 기반 인게임 콘솔 로그 UI 및 실시간 성능(FPS & FrameTime Graph) 모니터 컨트롤러입니다.
/// 화면 좌상단에 실시간 FPS/FrameTime 꺾은선 그래프를 표시하여 미세 끊김(Micro-stutter)을 즉시 포착하며,
/// 우상단 버튼을 통해 콘솔 창을 열고 닫을 수 있습니다.
/// 런타임 빌드(Windows, Android, iOS) 및 에디터에서 가비지 컬렉션(GC) 할당 없이 고성능으로 동작합니다.
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

    [Header("=== UI Toolkit 설정 ===")]
    [Tooltip("콘솔 UI Document에 연결할 VisualTreeAsset")]
    [SerializeField] private VisualTreeAsset consoleVisualTree;

    [Tooltip("씬 전환 시에도 콘솔을 유지할지 여부")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("=== 성능 모니터 설정 ===")]
    [Tooltip("프레임 그래프 표시 여부")]
    [SerializeField] private bool showFpsGraph = true;

    private const int MaxUiLogCount = 1000;
    private const int GraphSampleCount = 80;

    private UIDocument uiDocument;
    private VisualElement rootElement;

    // 콘솔 UI 요소
    private Button openButton;
    private VisualElement consoleWindow;
    private Button closeButton;
    private Button clearButton;
    private Button filterAllButton;
    private Button filterInfoButton;
    private Button filterWarnButton;
    private Button filterErrorButton;
    private Button autoScrollButton;
    private ScrollView logScrollView;
    private Label detailLabel;
    private Label consoleStatsLabel;

    // FPS & 그래프 UI 요소
    private VisualElement fpsWidget;
    private Label fpsLabel;
    private Label msLabel;
    private VisualElement fpsGraphCanvas;

    // 로깅 상태
    private LogFilter currentFilter = LogFilter.All;
    private bool autoScrollEnabled = true;
    private readonly List<VisualElement> createdLogRows = new List<VisualElement>();
    private VisualElement selectedRow;

    // 성능 & 그래프 데이터 (Zero GC Ring Buffer)
    private readonly float[] frameTimes = new float[GraphSampleCount];
    private int bufferIndex = 0;
    private int recordedCount = 0;
    private float textUpdateTimer = 0f;
    private const float TextUpdateInterval = 0.12f;
    private float accumulatedDelta = 0f;
    private int accumulatedFrames = 0;
    private bool isGraphCollapsed = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();

        Application.targetFrameRate = 60;
        if (dontDestroyOnLoad)
        {
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (consoleVisualTree != null && uiDocument.visualTreeAsset == null)
        {
            uiDocument.visualTreeAsset = consoleVisualTree;
        }

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        BindUIElements();
        RegisterCallbacks();

        // 기존 히스토리 로드
        RefreshAllLogs();

        // 실시간 로그 이벤트 구독
        HB_Log.OnLogAdded += HandleLogAdded;
        HB_Log.OnLogsCleared += HandleLogsCleared;

        // 그래프 렌더링 콜백 등록
        if (fpsGraphCanvas != null)
        {
            fpsGraphCanvas.generateVisualContent += DrawFrameTimeGraph;
        }
    }

    private void OnDisable()
    {
        HB_Log.OnLogAdded -= HandleLogAdded;
        HB_Log.OnLogsCleared -= HandleLogsCleared;

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

        filterAllButton = rootElement.Q<Button>("filterAllBtn");
        filterInfoButton = rootElement.Q<Button>("filterInfoBtn");
        filterWarnButton = rootElement.Q<Button>("filterWarnBtn");
        filterErrorButton = rootElement.Q<Button>("filterErrorBtn");
        autoScrollButton = rootElement.Q<Button>("autoScrollBtn");

        logScrollView = rootElement.Q<ScrollView>("logScrollView");
        detailLabel = rootElement.Q<Label>("detailLabel");
        consoleStatsLabel = rootElement.Q<Label>("consoleStatsLabel");

        // FPS & Graph 위젯 바인딩
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

        if (filterAllButton != null) filterAllButton.clicked += () => SetFilter(LogFilter.All);
        if (filterInfoButton != null) filterInfoButton.clicked += () => SetFilter(LogFilter.Info);
        if (filterWarnButton != null) filterWarnButton.clicked += () => SetFilter(LogFilter.Warning);
        if (filterErrorButton != null) filterErrorButton.clicked += () => SetFilter(LogFilter.Error);
        if (autoScrollButton != null) autoScrollButton.clicked += ToggleAutoScroll;

        // FPS 위젯 클릭 시 미니 그래프 접기/펼치기
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

    /// <summary>
    /// Application.targetFrameRate를 기준으로 목표 FPS를 반환합니다.
    /// 미설정(-1) 시 모니터 주사율 또는 60을 기본값으로 반환합니다.
    /// </summary>
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

        // 원형 버퍼(Ring Buffer)에 기록 (Zero GC)
        frameTimes[bufferIndex] = frameMs;
        bufferIndex = (bufferIndex + 1) % GraphSampleCount;
        if (recordedCount < GraphSampleCount) recordedCount++;

        // 그래프 리페인트 요청
        if (fpsGraphCanvas != null && !isGraphCollapsed)
        {
            fpsGraphCanvas.MarkDirtyRepaint();
        }

        // 텍스트 업데이트 주기 (매 프레임 string 생성 GC 방지)
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

                // TargetFrameRate 기준 인디케이터 색상:
                // 90% 이상: 녹색 (Good)
                // 70% 이상 90% 미만: 주황색 (Warn)
                // 그 외: 빨간색 (Bad)
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

        // 스케일 계산: 기본 warnMs * 1.2f, 스파이크가 크면 최대 100ms까지 자동 조절
        float maxMs = Mathf.Max(warnMs * 1.25f, 40f);
        for (int i = 0; i < recordedCount; i++)
        {
            if (frameTimes[i] > maxMs)
            {
                maxMs = Mathf.Min(frameTimes[i], 100f);
            }
        }

        // 1. Target FrameRate (100% 목표) 기준선 - 은은한 녹색
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

        // 2. 70% 위험 경계선 (Warn Threshold) - 은은한 주황색
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

        // 3. 최근 프레임 꺾은선 및 하단 반투명 영역 칠하기
        int samples = recordedCount;
        float stepX = width / Mathf.Max(samples - 1, 1);

        // 영역 채우기 (Fill)
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

        // 라인 그리기 (Stroke)
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

    #region Console UI

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

    public void ClearConsole()
    {
        HB_Log.Clear();
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

        var history = HB_Log.History;
        for (int i = 0; i < history.Count; i++)
        {
            AddLogElement(history[i]);
        }

        UpdateOpenButtonText(history.Count);
    }

    private void HandleLogAdded(HB_Log.HB_LogEntry entry)
    {
        if (logScrollView == null) return;

        var row = AddLogElement(entry);
        UpdateOpenButtonText(HB_Log.History.Count);

        // AutoScroll 활성화 시 안전하게 해당 row로 스크롤
        if (autoScrollEnabled && row != null)
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
        UpdateOpenButtonText(0);
    }

    private VisualElement AddLogElement(HB_Log.HB_LogEntry entry)
    {
        // 1000개 초과 시 가장 오래된 UI 엘리먼트 제거 (메모리 보호)
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

        var tagLabel = new Label();
        tagLabel.AddToClassList("hb-log-type-tag");

        var msgLabel = new Label(entry.Message);
        msgLabel.AddToClassList("hb-log-message");

        switch (entry.LogType)
        {
            case LogType.Warning:
                tagLabel.text = "[WARN]";
                tagLabel.AddToClassList("hb-type-warn");
                msgLabel.AddToClassList("hb-text-warn");
                row.userData = LogFilter.Warning;
                break;
            case LogType.Error:
            case LogType.Assert:
            case LogType.Exception:
                tagLabel.text = "[ERR]";
                tagLabel.AddToClassList("hb-type-error");
                msgLabel.AddToClassList("hb-text-error");
                row.userData = LogFilter.Error;
                break;
            default:
                tagLabel.text = "[INFO]";
                tagLabel.AddToClassList("hb-type-info");
                msgLabel.AddToClassList("hb-text-info");
                row.userData = LogFilter.Info;
                break;
        }

        row.Add(timeLabel);
        row.Add(tagLabel);
        row.Add(msgLabel);

        // 캡처 변수로 안전한 참조 보장
        HB_Log.HB_LogEntry capturedEntry = entry;
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
                string details = $"[{capturedEntry.LogType}] {capturedEntry.TimeString}\n{capturedEntry.Message}";
                if (!string.IsNullOrEmpty(capturedEntry.StackTrace))
                {
                    details += $"\n\n--- Stack Trace ---\n{capturedEntry.StackTrace}";
                }
                detailLabel.text = details;
            }
        });

        // 필터링 적용 여부
        bool isVisible = IsLogVisible(entry.LogType, currentFilter);
        row.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

        createdLogRows.Add(row);
        logScrollView.Add(row);

        return row;
    }

    private void ApplyFilterToVisibleLogs()
    {
        foreach (var row in createdLogRows)
        {
            if (row.userData is LogFilter logCategory)
            {
                bool visible = (currentFilter == LogFilter.All) || (currentFilter == logCategory);
                row.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }

    private bool IsLogVisible(LogType type, LogFilter filter)
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
