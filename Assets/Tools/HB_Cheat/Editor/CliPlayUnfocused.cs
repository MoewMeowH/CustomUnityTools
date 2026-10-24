using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>cheat_play가 Play 직전에 판정한 결과 (응답 JSON의 playUnfocusedOverride)</summary>
    internal readonly struct PlayUnfocusedDecision
    {
        public const string Disabled = "disabled";
        public const string ApiUnavailable = "api_unavailable";
        public const string NoPlayModeView = "no_play_mode_view";
        public const string NotFocused = "not_focused";

        public readonly bool Active;
        /// <summary>메인 Play 뷰의 원래 값 (PlayFocused · PlayMaximized · PlayUnfocused). 읽지 않았으면 null</summary>
        public readonly string Original;
        /// <summary>Active가 false인 이유. Active면 null</summary>
        public readonly string Reason;

        public PlayUnfocusedDecision(bool active, string original, string reason)
        {
            Active = active;
            Original = original;
            Reason = reason;
        }
    }

    /// <summary>테스트에서 Game 뷰 · SessionState · Play 상태를 바꿔 끼우기 위한 경계</summary>
    internal interface ICliPlayViewHost
    {
        bool IsPlayingOrWillChangePlaymode { get; }
        /// <summary>내부 API(PlayModeView.enterPlayModeBehavior)를 찾았는지</summary>
        bool ApiAvailable { get; }
        /// <summary>메인 Play 뷰의 값 이름. 뷰가 없으면 null</summary>
        string GetBehavior();
        bool SetBehavior(string behavior);
        bool GetBool(string key);
        void SetBool(string key, bool value);
        void EraseBool(string key);
        string GetString(string key);
        void SetString(string key, string value);
        void EraseString(string key);
        int GetInt(string key);
        void SetInt(string key, int value);
        void EraseInt(string key);
        void Log(string message);
        /// <summary>에디터 재시작을 넘기는 복구 기록 (원래 값). 없으면 null</summary>
        string ReadRecovery();
        void WriteRecovery(string original);
        void DeleteRecovery();
    }

    /// <summary>
    /// cheat_play로 시작한 Play 동안만 메인 Game 뷰의 "Enter Play Mode" 동작을 Play Unfocused로 바꿔,
    /// Play에 들어갈 때 Unity가 맨 앞으로 나와 사람의 포커스를 뺏지 않게 한다 (설정 기본 꺼짐).
    /// 원래 값이 Play Focused일 때만 바꾸고, 에디트 모드 · 리로드 · Play 진입 실패 · 에디터 종료(레이아웃 저장 전)에 되돌린다.
    /// 되돌릴 때 값이 아직 Play Unfocused일 때만 덮는다 (Play 중 사람이 드롭다운을 바꿨으면 그 값을 둔다).
    /// 공개 API(PlayModeWindow.SetPlayModeFocused)는 Focused ↔ Maximized만 고를 수 있어 내부 API를 리플렉션으로 쓴다. 못 찾으면 아무것도 하지 않는다.
    /// </summary>
    internal sealed class CliPlayUnfocused
    {
        public const string Focused = "PlayFocused";
        public const string Unfocused = "PlayUnfocused";

        public const string OwnedKey = "HB.Cheat.CliPlay.Unfocused.Owned";
        public const string OriginalKey = "HB.Cheat.CliPlay.Unfocused.Original";
        /// <summary>0보다 크면 EnteredPlayMode를 기다리는 중 (CliPlayRunInBackground.PendingKey와 같은 역할)</summary>
        public const string PendingKey = "HB.Cheat.CliPlay.Unfocused.PendingTicks";
        /// <summary>이번 에디터 실행에서 복구 기록을 처리했는지 (기록은 시작 때 한 번만 쓴다 — 나중에 사람이 고른 Unfocused를 덮지 않도록)</summary>
        public const string RecoveryCheckedKey = "HB.Cheat.CliPlay.Unfocused.RecoveryChecked";

        private readonly ICliPlayViewHost m_Host;

        public CliPlayUnfocused(ICliPlayViewHost host)
        {
            m_Host = host;
        }

        public bool Owned => m_Host.GetBool(OwnedKey);
        public bool Pending => m_Host.GetInt(PendingKey) > 0;

        /// <summary>cheat_play가 isPlaying = true 직전(에디트 모드)에 부른다</summary>
        public PlayUnfocusedDecision Begin(bool enabled)
        {
            if (Owned) Restore("stale state before cheat_play");
            else TryApplyRecovery();

            if (!enabled) return new PlayUnfocusedDecision(false, null, PlayUnfocusedDecision.Disabled);
            if (!m_Host.ApiAvailable) return new PlayUnfocusedDecision(false, null, PlayUnfocusedDecision.ApiUnavailable);

            string original = m_Host.GetBehavior();
            if (original == null) return new PlayUnfocusedDecision(false, null, PlayUnfocusedDecision.NoPlayModeView);
            // Maximized는 사람이 고른 보기 방식이고, Unfocused는 이미 원하는 상태다
            if (original != Focused) return new PlayUnfocusedDecision(false, original, PlayUnfocusedDecision.NotFocused);

            if (!m_Host.SetBehavior(Unfocused)) return new PlayUnfocusedDecision(false, original, PlayUnfocusedDecision.ApiUnavailable);

            m_Host.SetString(OriginalKey, original);
            m_Host.SetBool(OwnedKey, true);
            // SessionState는 에디터를 닫으면 사라진다 → Play 중에 닫혀 레이아웃에 Unfocused가 저장돼도 다음 실행에서 되돌리도록
            m_Host.WriteRecovery(original);
            m_Host.SetInt(PendingKey, CliPlayRunInBackground.PendingGraceTicks);
            m_Host.Log($"Game view set to {Unfocused} for this CLI Play (original: {original})");
            return new PlayUnfocusedDecision(true, original, null);
        }

        public void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    // Unity는 Play에서 나올 때도 이 값으로 포커스를 정한다(WindowLayout.TryFocusAppropriateWindow) → 에디트 모드까지 유지
                    m_Host.EraseInt(PendingKey);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (Owned) Restore("CLI Play ended");
                    else m_Host.EraseInt(PendingKey);
                    break;
            }
        }

        /// <summary>
        /// 도메인 리로드 · 에디터 시작. Play 밖인데 소유권이 남아 있으면 되돌린다.
        /// 소유권(SessionState)이 없는데 복구 기록이 있으면 지난 에디터가 Play 중에 닫힌 것 → 아직 Unfocused면 원래 값으로.
        /// 반환: Game 뷰가 아직 없어 다시 확인해야 하면 true (호출부가 잠시 뒤 다시 부르고, 끝내 없으면 GiveUpRecovery).
        /// </summary>
        public bool OnLoad()
        {
            if (m_Host.IsPlayingOrWillChangePlaymode) return false;
            if (Owned)
            {
                Restore("recovered after reload");
                return false;
            }
            return TryApplyRecovery();
        }

        /// <summary>시작 뒤 기다려도 Game 뷰가 생기지 않았다 → 기록을 버린다 (나중에 사람이 고른 값을 덮지 않도록)</summary>
        public void GiveUpRecovery()
        {
            if (m_Host.ReadRecovery() == null) return;
            m_Host.DeleteRecovery();
            m_Host.SetBool(RecoveryCheckedKey, true);
            m_Host.Log("Recovery record from a previous session discarded (no Game view)");
        }

        /// <returns>Game 뷰가 아직 없어 다시 확인해야 하면 true</returns>
        private bool TryApplyRecovery()
        {
            if (m_Host.GetBool(RecoveryCheckedKey)) return false;
            string original = m_Host.ReadRecovery();
            if (original == null)
            {
                m_Host.SetBool(RecoveryCheckedKey, true);
                return false;
            }

            string current = m_Host.GetBehavior();
            if (current == null) return true;

            // 기록에는 항상 PlayFocused만 쓴다 → 그 밖의 내용은 쓰지 않는다
            bool restored = original == Focused && current == Unfocused && m_Host.SetBehavior(original);
            m_Host.DeleteRecovery();
            m_Host.SetBool(RecoveryCheckedKey, true);
            m_Host.Log(restored
                ? $"Game view restored to {original} (the editor was closed during a CLI Play)"
                : $"Game view left as {current} (recovery record from a previous session)");
            return false;
        }

        /// <summary>EditorApplication.update. 계속 기다려야 하면 true</summary>
        public bool Tick()
        {
            int ticks = m_Host.GetInt(PendingKey);
            if (ticks <= 0) return false;
            if (m_Host.IsPlayingOrWillChangePlaymode) return true;

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

        /// <summary>
        /// 바꾼 채 에디터가 닫히는 경우. Play 중에 닫으면 Unity가 되돌린 값이 아니라 Play 진입 때의 창 상태로 레이아웃을 저장해 Unfocused가 남는다(6.3 실제 확인)
        /// → 되돌리되 복구 기록은 남겨 다음 실행(OnLoad)에서 한 번 더 확인한다.
        /// </summary>
        public void OnQuitting()
        {
            if (Owned) Restore("editor quitting", keepRecovery: true);
        }

        private void Restore(string why, bool keepRecovery = false)
        {
            string original = m_Host.GetString(OriginalKey);
            string current = m_Host.GetBehavior();
            bool restored = false;
            if (!string.IsNullOrEmpty(original) && current == Unfocused)
                restored = m_Host.SetBehavior(original);

            m_Host.EraseBool(OwnedKey);
            m_Host.EraseString(OriginalKey);
            m_Host.EraseInt(PendingKey);
            if (!keepRecovery) m_Host.DeleteRecovery();
            m_Host.Log(restored
                ? $"Game view restored to {original} ({why})"
                : $"Game view left as {current ?? "(no view)"} ({why}; it was changed by someone else or the view is gone)");
        }
    }

    /// <summary>CliPlayUnfocused를 에디터 이벤트와 내부 API(리플렉션)에 붙인다</summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CliPlayUnfocusedHook
    {
        private sealed class UnityHost : ICliPlayViewHost
        {
            private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // UnityEditor.PlayModeView (internal, GameView의 부모) — 6000.3 · 7000.0 alpha에서 이름 확인
            private static readonly Type s_ViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            private static readonly MethodInfo s_GetMain = s_ViewType?.GetMethod("GetMainPlayModeView", AnyStatic, null, Type.EmptyTypes, null);
            private static readonly PropertyInfo s_Behavior = s_ViewType?.GetProperty("enterPlayModeBehavior", AnyInstance);

            public bool ApiAvailable =>
                s_GetMain != null && s_Behavior != null && s_Behavior.CanRead && s_Behavior.CanWrite
                && s_Behavior.PropertyType.IsEnum
                && Enum.IsDefined(s_Behavior.PropertyType, Focused) && Enum.IsDefined(s_Behavior.PropertyType, CliPlayUnfocused.Unfocused);

            private static string Focused => CliPlayUnfocused.Focused;

            public bool IsPlayingOrWillChangePlaymode => EditorApplication.isPlayingOrWillChangePlaymode;

            public string GetBehavior()
            {
                if (!ApiAvailable) return null;
                try
                {
                    var view = s_GetMain.Invoke(null, null) as EditorWindow;
                    return view == null ? null : s_Behavior.GetValue(view)?.ToString();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HB_Cheat] Could not read the Game view play mode behavior: {e.GetBaseException().Message}");
                    return null;
                }
            }

            public bool SetBehavior(string behavior)
            {
                if (!ApiAvailable) return false;
                try
                {
                    var view = s_GetMain.Invoke(null, null) as EditorWindow;
                    if (view == null) return false;
                    s_Behavior.SetValue(view, Enum.Parse(s_Behavior.PropertyType, behavior));
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HB_Cheat] Could not change the Game view play mode behavior: {e.GetBaseException().Message}");
                    return false;
                }
            }

            public bool GetBool(string key) => SessionState.GetBool(key, false);
            public void SetBool(string key, bool value) => SessionState.SetBool(key, value);
            public void EraseBool(string key) => SessionState.EraseBool(key);
            public string GetString(string key) => SessionState.GetString(key, null);
            public void SetString(string key, string value) => SessionState.SetString(key, value);
            public void EraseString(string key) => SessionState.EraseString(key);
            public int GetInt(string key) => SessionState.GetInt(key, 0);
            public void SetInt(string key, int value) => SessionState.SetInt(key, value);
            public void EraseInt(string key) => SessionState.EraseInt(key);
            public void Log(string message) => Debug.Log("[HB_Cheat] " + message);

            // 프로젝트 Library(버전 관리 밖, 이 PC 전용). 정상 종료 경로에서는 지워진다
            private const string RecoveryPath = "Library/HB_Cheat/PlayUnfocused.restore";

            public string ReadRecovery()
            {
                try { return File.Exists(RecoveryPath) ? File.ReadAllText(RecoveryPath).Trim() : null; }
                catch (Exception) { return null; }
            }

            public void WriteRecovery(string original)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
                    File.WriteAllText(RecoveryPath, original);
                }
                catch (Exception e) { Debug.LogWarning($"[HB_Cheat] Could not write {RecoveryPath}: {e.Message}"); }
            }

            public void DeleteRecovery()
            {
                try
                {
                    if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath);
                    // 비었으면 폴더도 지운다 (흔적을 남기지 않는다)
                    string dir = Path.GetDirectoryName(RecoveryPath);
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
                catch (Exception e) { Debug.LogWarning($"[HB_Cheat] Could not delete {RecoveryPath}: {e.Message}"); }
            }
        }

        private static readonly CliPlayUnfocused s_Controller = new CliPlayUnfocused(new UnityHost());
        private static bool s_Ticking;

        static CliPlayUnfocusedHook()
        {
            EditorApplication.playModeStateChanged += s_Controller.OnPlayModeStateChanged;
            EditorApplication.quitting += s_Controller.OnQuitting;
            // 에디터 시작 직후에는 Game 뷰가 아직 없다 → 창이 뜬 뒤 확인하고, 그래도 없으면 잠시 더 기다린다
            EditorApplication.delayCall += () =>
            {
                if (s_Controller.OnLoad()) StartRecoveryRetry();
            };
            if (s_Controller.Pending) StartTicking();
        }

        public static PlayUnfocusedDecision Begin(bool enabled)
        {
            var decision = s_Controller.Begin(enabled);
            if (s_Controller.Pending) StartTicking();
            return decision;
        }

        // 복구 기록 확인을 다시 시도하는 update 횟수 (끝내 Game 뷰가 없으면 기록을 버린다)
        private const int RecoveryRetryTicks = 300;
        private static int s_RecoveryRetriesLeft;

        private static void StartRecoveryRetry()
        {
            s_RecoveryRetriesLeft = RecoveryRetryTicks;
            EditorApplication.update -= RetryRecovery;
            EditorApplication.update += RetryRecovery;
        }

        private static void RetryRecovery()
        {
            if (s_Controller.OnLoad() && --s_RecoveryRetriesLeft > 0) return;
            if (s_RecoveryRetriesLeft <= 0) s_Controller.GiveUpRecovery();
            EditorApplication.update -= RetryRecovery;
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
