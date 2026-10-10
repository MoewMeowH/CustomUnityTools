using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UITK 기반 인게임 콘솔 로그 UI 컨트롤러입니다.
/// 화면 우상단 버튼을 통해 콘솔 창을 열고 닫을 수 있으며,
/// AutoScroll 및 로그 필터링, 상세 스택 트레이스 확인 기능을 제공합니다.
/// 대량 로그 발생 시 메모리 보호를 위해 최대 1,000개의 UI 엘리먼트를 유지합니다.
/// 빌드 후 런타임(Windows, Android, iOS)에서도 완벽하게 동작합니다.
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

    private const int MaxUiLogCount = 1000;

    private UIDocument uiDocument;
    private VisualElement rootElement;
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

    private LogFilter currentFilter = LogFilter.All;
    private bool autoScrollEnabled = true;
    private readonly List<VisualElement> createdLogRows = new List<VisualElement>();
    private VisualElement selectedRow;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();

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
    }

    private void OnDisable()
    {
        HB_Log.OnLogAdded -= HandleLogAdded;
        HB_Log.OnLogsCleared -= HandleLogsCleared;
        UnregisterCallbacks();
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
    }

    private void UnregisterCallbacks()
    {
        if (openButton != null) openButton.clicked -= OpenConsole;
        if (closeButton != null) closeButton.clicked -= CloseConsole;
        if (clearButton != null) clearButton.clicked -= ClearConsole;
        if (autoScrollButton != null) autoScrollButton.clicked -= ToggleAutoScroll;
    }

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
}
