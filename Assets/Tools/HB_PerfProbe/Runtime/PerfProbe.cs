using System;
using UnityEngine;

namespace HB.PerfProbe
{
    /// <summary>
    /// 개발 빌드용 성능 Probe. 매 프레임 지표를 모아 1초마다 요약 한 줄을 logcat(태그 HBProbe)에 쓰고,
    /// 127.0.0.1 명령 소켓으로 캡처 명령을 받는다. 명령에는 시작 때 만든 토큰이 있어야 한다 (logcat 요약 줄에만 실림).
    /// 어셈블리는 HB_PERFPROBE 심볼이 있을 때만 컴파일된다. Android 개발 빌드(Debug.isDebugBuild)에서는 첫 씬 로드 뒤 저절로 켜지고,
    /// 다른 플랫폼 · 에디터에서는 저절로 켜지지 않는다 (Play 중에 <see cref="Start"/>로 켠다).
    /// 이 API는 메인 스레드에서만 부른다.
    /// </summary>
    public static class PerfProbe
    {
        public const string LogcatTag = "HBProbe";

        public static bool IsRunning => PerfProbeRunner.s_Instance != null;

        /// <summary>명령 소켓 포트. 꺼져 있으면 0.</summary>
        public static int Port => PerfProbeRunner.s_Instance != null ? PerfProbeRunner.s_Instance.Port : 0;

        /// <summary>캡처 폴더 위치. 꺼져 있으면 null.</summary>
        public static string CaptureRoot => PerfProbeRunner.s_Instance != null ? PerfProbeRunner.s_Instance.Root : null;

        /// <summary>가장 최근 요약 JSON (토큰 없음). 아직 없으면 null.</summary>
        public static string LatestSummaryJson => PerfProbeRunner.s_Instance != null ? PerfProbeRunner.s_Instance.LatestSummaryJson : null;

        /// <summary>
        /// Probe를 켠다. 이미 켜져 있으면 그대로 두고 true.
        /// 개발 빌드가 아니거나, Play 중이 아니거나, 소켓을 열지 못하면(포트 사용 중 등) 켜지 않고 false.
        /// </summary>
        public static bool Start(PerfProbeOptions options = null)
        {
            if (PerfProbeRunner.s_Instance != null) return true;
            if (!Debug.isDebugBuild)
            {
                Debug.LogWarning("[HBProbe] Not a development build. Probe not started.");
                return false;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HBProbe] Start only works in Play mode. Probe not started.");
                return false;
            }

            var go = new GameObject("HBPerfProbe") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<PerfProbeRunner>();
            try
            {
                runner.Initialize(options ?? new PerfProbeOptions());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HBProbe] Failed to start: {e.GetType().Name}: {e.Message}");
                UnityEngine.Object.DestroyImmediate(go);
                return false;
            }
        }

        /// <summary>Probe를 바로 끈다 (소켓 · 수집 정리). 돌아온 뒤에는 같은 프레임에서 다시 Start할 수 있다.</summary>
        public static void Stop()
        {
            PerfProbeRunner runner = PerfProbeRunner.s_Instance;
            if (runner == null) return;
            runner.Shutdown();
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        /// <summary>
        /// 구간 표시. 다음 요약 줄의 marks에 들어간다. 이름은 영문 · 숫자 · _ - . 만(처음 · 끝은 . 아님), 64자까지.
        /// 한 구간의 marks가 상한을 넘으면 뒤의 것은 버리고 끝에 "..."를 붙인다.
        /// </summary>
        public static void Mark(string name)
        {
            if (PerfProbeRunner.s_Instance == null) return;
            if (name == null || !ProbeCommand.IsSafeValue(name)) throw new ArgumentException("Mark name: only A-Z a-z 0-9 _ - . (not first or last '.') up to 64 chars", nameof(name));
            PerfProbeRunner.s_Instance.AddMark(name);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PerfProbeRunner.s_Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Application.platform != RuntimePlatform.Android || !Debug.isDebugBuild) return;
            Start();
        }
    }
}
