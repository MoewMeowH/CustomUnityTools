using System;
using UnityEditor;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 런타임 옵션은 생성 레지스트리 코드에 들어가므로, 설정 에셋이 어떤 경로로 바뀌든(설정 창 · Inspector · Undo · VCS 풀)
    /// 레지스트리에 들어가는 값(<see cref="CheatSettings.RegistryInputs"/>)이 바뀌었으면 다시 생성한다.
    /// Play 중이면 에디트 모드로 돌아온 뒤, Auto Generate가 꺼져 있으면 생성하지 않는다. 생성 결과가 같으면 파일을 쓰지 않는다.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatSettingsWatcher
    {
        // Play 중에 바뀐 설정을 에디트 모드로 돌아온 뒤 반영하기 위한 표시 (도메인 리로드를 넘긴다)
        internal const string RegeneratePendingKey = "HB.Cheat.RegeneratePending";

        // 마지막으로 반영한 레지스트리 입력값. null이면 아직 모름 (변경 알림이 오면 생성을 시도하고, 같으면 생성기가 쓰지 않는다)
        private static string s_LastInputs;
        // 마지막으로 본 프로젝트 심볼 이름 (어느 경로로 바뀌든 PlayerSettings 심볼을 같이 바꾸기 위해)
        private static string s_LastUserDefine;
        // 설정 에셋 guid 캐시. null이면 다시 찾는다, ""이면 에셋 없음. 에셋 속성 변경 이벤트마다 FindAssets를 하지 않도록 임포트 · 삭제 때만 비운다
        private static string s_SettingsGuid;

        static CheatSettingsWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode && IsPending)
                    RegenerateNow();
            };
            // 도메인 리로드 뒤 생성기가 레지스트리를 맞추므로 그 시점의 값을 기준으로 둔다
            EditorApplication.delayCall += () =>
            {
                var settings = CheatSettings.Load();
                s_LastInputs ??= settings.RegistryInputs;
                s_LastUserDefine ??= settings.UserDefine;
            };
        }

        public static bool IsPending => SessionState.GetBool(RegeneratePendingKey, false);

        /// <summary>
        /// 설정 에셋이 바뀌었을 수 있을 때 부른다. 레지스트리 입력값이 그대로면 아무것도 하지 않는다.
        /// save: 임포트 콜백에서는 false (디스크에서 막 읽은 값을 다시 쓰지 않는다).
        /// </summary>
        public static void NotifyChanged(bool save = true)
        {
            var settings = CheatSettings.Load();
            string inputs = settings.RegistryInputs;
            if (inputs == s_LastInputs) return;
            s_LastInputs = inputs;

            SyncUserDefine(settings.UserDefine);

            if (save && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                AssetDatabase.SaveAssetIfDirty(settings);
            RequestRegenerate();
        }

        /// <summary>설정 에셋이 생기거나 지워졌을 수 있다 → guid를 다시 찾게 한다</summary>
        internal static void InvalidateSettingsGuid() => s_SettingsGuid = null;

        // 레지스트리는 #if {UserDefine}으로 감싸므로, 에셋 값만 바뀌고 PlayerSettings 심볼이 그대로면 치트가 통째로 컴파일에서 빠진다.
        // 설정 창 · Inspector · Undo · VCS 어느 경로든 같이 바꾼다 (옛 이름이 심볼에 없으면 RenameUserDefine은 아무것도 하지 않는다)
        private static void SyncUserDefine(string userDefine)
        {
            string previous = s_LastUserDefine;
            s_LastUserDefine = userDefine;
            if (previous == null || previous == userDefine) return;

            CheatDefines.RenameUserDefine(previous, userDefine);
            UnityEngine.Debug.LogWarning($"[HB_Cheat] Project define renamed {previous} → {userDefine}. " +
                                         "Update '#if' guards in cheat files and defineConstraints in cheat asmdefs.");
        }

        private static void RequestRegenerate()
        {
            if (!CheatSettings.Load().AutoGenerate) return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(RegeneratePendingKey, true);
                return;
            }

            // OnGUI · 임포트 콜백 안에서 파일 쓰기 · Refresh를 하지 않도록 미룬다
            EditorApplication.delayCall -= RegenerateNow;
            EditorApplication.delayCall += RegenerateNow;
        }

        private static void RegenerateNow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(RegeneratePendingKey, true);
                return;
            }
            SessionState.EraseBool(RegeneratePendingKey);
            // Play 중에 예약한 뒤 Auto Generate를 껐을 수 있다
            if (!CheatSettings.Load().AutoGenerate) return;
            // 현재 플랫폼에서 심볼이 꺼져 있으면 생성기가 없다 → 켜질 때 생성기가 로드되며 생성한다
            CheatEditorHooks.Generate?.Invoke(true);
        }

        // Inspector 편집 · Undo/Redo · SerializedObject 적용
        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            string settingsGuid = null;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;

                settingsGuid ??= SettingsGuid();
                if (settingsGuid.Length == 0) return;

                stream.GetChangeAssetObjectPropertiesEvent(i, out var change);
                if (change.guid.ToString() == settingsGuid)
                {
                    NotifyChanged();
                    return;
                }
            }
        }

        private static string SettingsGuid()
        {
            if (s_SettingsGuid != null) return s_SettingsGuid;
            string path = AssetDatabase.GetAssetPath(CheatSettings.Load());
            s_SettingsGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            return s_SettingsGuid;
        }
    }

    /// <summary>VCS 풀 · 외부 편집으로 설정 에셋이 다시 임포트되거나 지워진 경우</summary>
    internal sealed class CheatSettingsImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool changed = false;
            foreach (string path in importedAssets)
            {
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(CheatSettings)) continue;
                changed = true;
                break;
            }

            // 지운 뒤에는 타입을 알 수 없으므로 .asset 삭제면 다시 확인한다 (설정이 기본값으로 돌아갔을 수 있음)
            if (!changed)
            {
                foreach (string path in deletedAssets)
                {
                    if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                    changed = true;
                    break;
                }
            }

            if (!changed) return;
            CheatSettingsWatcher.InvalidateSettingsGuid();
            CheatSettingsWatcher.NotifyChanged(save: false);
        }
    }
}
