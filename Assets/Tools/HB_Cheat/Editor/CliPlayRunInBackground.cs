using UnityEditor;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>cheat_play가 Play 직전에 판정한 결과 (응답 JSON의 runInBackgroundOverride)</summary>
    internal readonly struct RunInBackgroundDecision
    {
        public const string Disabled = "disabled";
        public const string AlreadyTrue = "already_true";
        public const string PipelineDriver = "pipeline_driver";
        public const string PipelineConfigUnreadable = "pipeline_config_unreadable";

        public readonly bool Active;
        /// <summary>읽지 않았으면 null (설정 꺼짐·Pipeline 판정으로 멈춘 경우)</summary>
        public readonly bool? Original;
        /// <summary>Active가 false인 이유. Active면 null</summary>
        public readonly string Reason;
        public readonly PipelineRuntimeConfigProbe.Result Pipeline;

        public RunInBackgroundDecision(bool active, bool? original, string reason, PipelineRuntimeConfigProbe.Result pipeline)
        {
            Active = active;
            Original = original;
            Reason = reason;
            Pipeline = pipeline;
        }
    }

    /// <summary>테스트에서 Application·SessionState·Play 상태를 바꿔 끼우기 위한 경계</summary>
    internal interface ICliPlayHost
    {
        bool RunInBackground { get; set; }
        bool IsPlayingOrWillChangePlaymode { get; }
        bool GetBool(string key);
        void SetBool(string key, bool value);
        void EraseBool(string key);
        int GetInt(string key);
        void SetInt(string key, int value);
        void EraseInt(string key);
        void Log(string message);
    }

    /// <summary>
    /// cheat_play로 시작한 Play 동안만 Application.runInBackground를 켜고, 에디트 모드로 돌아오면 원래 값으로 되돌린다.
    /// 에디터는 창이 OS 맨 앞에 없으면 Play 프레임을 멈출 수 있다 (7장 관찰). 사람이 누른 Play는 cheat_play를 거치지 않으므로 손대지 않는다.
    /// 디스크(ProjectSettings.asset)에는 쓰지 않는다. 원래 값이 이미 true면 아무것도 하지 않는다.
    /// 상태는 SessionState에 둔다 → 도메인 리로드를 넘기고, 리로드를 끈 프로젝트에서도 static에 남지 않는다.
    /// 심볼 조건이 없는 이 어셈블리에 두어, HB_CHEAT를 꺼도 복원은 돈다.
    /// </summary>
    internal sealed class CliPlayRunInBackground
    {
        public const string OwnedKey = "HB.Cheat.CliPlay.Owned";
        public const string OriginalKey = "HB.Cheat.CliPlay.Original";
        /// <summary>0보다 크면 EnteredPlayMode를 기다리는 중. Play 진입이 실패하면(컴파일 에러 등) 이 횟수만큼 update를 본 뒤 되돌린다</summary>
        public const string PendingKey = "HB.Cheat.CliPlay.PendingTicks";
        public const int PendingGraceTicks = 10;

        private readonly ICliPlayHost m_Host;

        public CliPlayRunInBackground(ICliPlayHost host)
        {
            m_Host = host;
        }

        public bool Owned => m_Host.GetBool(OwnedKey);
        public bool Pending => m_Host.GetInt(PendingKey) > 0;

        /// <summary>cheat_play가 isPlaying = true 직전(에디트 모드)에 부른다. "원래 값"은 이 시점의 값이다.</summary>
        public RunInBackgroundDecision Begin(bool enabled, PipelineRuntimeConfigProbe.Result pipeline)
        {
            // 에디트 모드에서 소유권이 남아 있으면 이전 회차의 종료 경로를 놓친 것 → 먼저 되돌린다
            if (Owned) Restore("stale state before cheat_play");

            if (!enabled) return new RunInBackgroundDecision(false, null, RunInBackgroundDecision.Disabled, pipeline);

            // Pipeline 드라이버도 runInBackground를 켜고 되돌린다 → 소유자를 하나로 두기 위해 이쪽은 손대지 않는다
            if (pipeline.State == PipelineRuntimeConfigProbe.State.DriverEnabled)
                return new RunInBackgroundDecision(false, null, RunInBackgroundDecision.PipelineDriver, pipeline);
            if (pipeline.State == PipelineRuntimeConfigProbe.State.Unreadable)
                return new RunInBackgroundDecision(false, null, RunInBackgroundDecision.PipelineConfigUnreadable, pipeline);

            bool original = m_Host.RunInBackground;
            if (original) return new RunInBackgroundDecision(false, true, RunInBackgroundDecision.AlreadyTrue, pipeline);

            m_Host.SetBool(OriginalKey, original);
            m_Host.SetBool(OwnedKey, true);
            m_Host.SetInt(PendingKey, PendingGraceTicks);
            m_Host.RunInBackground = true;
            m_Host.Log("runInBackground on for this CLI Play (original: false)");
            return new RunInBackgroundDecision(true, original, null, pipeline);
        }

        public void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    m_Host.EraseInt(PendingKey);
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    // 종료 전환 중에 다시 적용하되 소유권은 유지한다. 이 뒤에 다른 쓰기가 끼어도 EnteredEditMode에서 마지막으로 쓴다.
                    if (Owned) m_Host.RunInBackground = m_Host.GetBool(OriginalKey);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (Owned) Restore("CLI Play ended");
                    else m_Host.EraseInt(PendingKey);
                    break;
            }
        }

        /// <summary>도메인 리로드·에디터 시작. Play 밖인데 소유권이 남아 있으면 (Play 중 재컴파일로 종료 이벤트를 놓친 경우 등) 되돌린다.</summary>
        public void OnLoad()
        {
            if (Owned && !m_Host.IsPlayingOrWillChangePlaymode) Restore("recovered after reload");
        }

        /// <summary>EditorApplication.update. 계속 기다려야 하면 true</summary>
        public bool Tick()
        {
            int ticks = m_Host.GetInt(PendingKey);
            if (ticks <= 0) return false;
            if (m_Host.IsPlayingOrWillChangePlaymode) return true;

            // Play로 들어가지 않았다 (컴파일 에러로 진입 취소 등)
            ticks--;
            if (ticks > 0)
            {
                m_Host.SetInt(PendingKey, ticks);
                return true;
            }

            if (Owned) Restore("Play Mode was not entered");
            else m_Host.EraseInt(PendingKey);
            return false;
        }

        /// <summary>켜진 채로 에디터가 닫히며 설정이 저장되지 않도록 되돌린다</summary>
        public void OnQuitting()
        {
            if (Owned) Restore("editor quitting");
        }

        private void Restore(string why)
        {
            bool original = m_Host.GetBool(OriginalKey);
            m_Host.RunInBackground = original;
            m_Host.EraseBool(OwnedKey);
            m_Host.EraseBool(OriginalKey);
            m_Host.EraseInt(PendingKey);
            m_Host.Log($"runInBackground restored to {(original ? "true" : "false")} ({why})");
        }
    }

    /// <summary>CliPlayRunInBackground를 에디터 이벤트에 붙인다</summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CliPlayRunInBackgroundHook
    {
        private sealed class UnityHost : ICliPlayHost
        {
            public bool RunInBackground
            {
                get => Application.runInBackground;
                set => Application.runInBackground = value;
            }

            public bool IsPlayingOrWillChangePlaymode => EditorApplication.isPlayingOrWillChangePlaymode;
            public bool GetBool(string key) => SessionState.GetBool(key, false);
            public void SetBool(string key, bool value) => SessionState.SetBool(key, value);
            public void EraseBool(string key) => SessionState.EraseBool(key);
            public int GetInt(string key) => SessionState.GetInt(key, 0);
            public void SetInt(string key, int value) => SessionState.SetInt(key, value);
            public void EraseInt(string key) => SessionState.EraseInt(key);
            public void Log(string message) => Debug.Log("[HB_Cheat] " + message);
        }

        private static readonly CliPlayRunInBackground s_Controller = new CliPlayRunInBackground(new UnityHost());
        private static bool s_Ticking;

        static CliPlayRunInBackgroundHook()
        {
            EditorApplication.playModeStateChanged += s_Controller.OnPlayModeStateChanged;
            EditorApplication.quitting += s_Controller.OnQuitting;
            s_Controller.OnLoad();
            if (s_Controller.Pending) StartTicking();
        }

        public static RunInBackgroundDecision Begin(bool enabled)
        {
            var decision = s_Controller.Begin(enabled, PipelineRuntimeConfigProbe.ProbeProject());
            if (s_Controller.Pending) StartTicking();
            return decision;
        }

        private static void StartTicking()
        {
            if (s_Ticking) return;
            s_Ticking = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (s_Controller.Tick()) return;
            EditorApplication.update -= Tick;
            s_Ticking = false;
        }
    }
}
