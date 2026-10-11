#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[AutoStaticsCleanup]
#endif
public static partial class InGameConsole
{
    // 신버전: [AutoStaticsCleanup]에 의해 도메인 리로드 / 플레이 모드 진입 시 자동 초기화됨
    // (만약 자동 초기화가 불필요한 필드는 필드 단위로 [NoAutoStaticsCleanup] 지정 가능)
    private static HB_InGameConsoleUI s_UIInstance;
    private static bool s_IsInitialized = false;

    public static bool IsInitialized => s_IsInitialized;

    public static HB_InGameConsoleUI Instance
    {
        get
        {
            if (s_UIInstance == null)
            {
#if UNITY_2023_1_OR_NEWER
                s_UIInstance = Object.FindAnyObjectByType<HB_InGameConsoleUI>();
#else
                s_UIInstance = Object.FindObjectOfType<HB_InGameConsoleUI>();
#endif
            }
            return s_UIInstance;
        }
    }

#if !(UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER)
    // 구버전 Unity: AutoStaticsCleanup 기능이 없으므로 Domain Reload 비활성화 환경을 위해 SubsystemRegistration으로 수동 초기화
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnDomainReload()
    {
        s_UIInstance = null;
        s_IsInitialized = false;
    }
#endif
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitializeOnGameStart()
    {
        var settings = InGameConsoleSettings.Instance;
        if (settings != null && settings.InitMode == ConsoleInitMode.AutoOnGameStart)
        {
            Initialize();
        }
    }

    public static void Initialize()
    {
        if (s_IsInitialized) return;

        HLogCore.Initialize();

        var settings = InGameConsoleSettings.Instance;
        if (settings != null && settings.AutoCreateUI)
        {
            EnsureUIInstance();
        }

        s_IsInitialized = true;
    }

    public static HB_InGameConsoleUI EnsureUIInstance()
    {
        if (Instance != null)
        {
            return s_UIInstance;
        }

        var settings = InGameConsoleSettings.Instance;
        var go = new GameObject("[InGameConsole]");
        
        var uiDocument = go.AddComponent<UIDocument>();
        if (settings != null)
        {
            uiDocument.panelSettings = settings.PanelSettings != null 
                ? settings.PanelSettings 
                : Resources.Load<PanelSettings>("HB_PanelSettings");

            uiDocument.visualTreeAsset = settings.ConsoleVisualTree != null 
                ? settings.ConsoleVisualTree 
                : Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

        s_UIInstance = go.AddComponent<HB_InGameConsoleUI>();

        bool shouldDontDestroy = settings == null || settings.DontDestroyOnLoad;
        if (shouldDontDestroy)
        {
            Object.DontDestroyOnLoad(go);
        }

        return s_UIInstance;
    }

    public static void Open() => Instance?.OpenConsole();
    public static void Close() => Instance?.CloseConsole();
    public static void Toggle() => Instance?.ToggleConsole();
    public static void ClearLogs() => HLogCore.Clear();
}
