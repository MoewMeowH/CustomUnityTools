using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InGameConsoleSystem.Editor
{
    /// <summary>
    /// 패키지를 처음 설치한 프로젝트에서 한 번만 USE_HLOG를 Standalone · Android · iOS에 추가한다.
    /// 추가했다는 표시(ProjectSettings 마커, Git으로 공유)가 있으면 아무것도 하지 않는다 → 일부러 끈 프로젝트에 다시 켜지 않는다.
    /// 배치 모드(CI 빌드)에서는 하지 않는다 (빌드 중에 심볼이 바뀌면 안 된다).
    /// </summary>
    internal static class HLogDefineSetup
    {
        public const string MarkerPath = "ProjectSettings/HB_InGameConsole.UseHLog.added.txt";

        internal static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS
        };

        private static string Symbol => LogChannelGeneratorSettings.ConditionalSymbol;

        internal enum SetupAction
        {
            None,
            /// <summary>이 기능 전부터 쓰던 프로젝트 → 심볼은 건드리지 않고 표시만 남긴다 (일부러 끈 USE_HLOG를 다시 켜지 않도록)</summary>
            MarkOnly,
            Add
        }

        [InitializeOnLoadMethod]
        private static void Bootstrap()
        {
            // LogChannels 자동 생성 표시는 그쪽 delayCall에서 쓰이므로, 처음 설치하는 리로드의 이 시점에는 아직 없다
            bool existingInstall = File.Exists(LogChannelGeneratorWindow.BootstrapMarkerPath);
            switch (Decide(File.Exists(MarkerPath), Application.isBatchMode, existingInstall))
            {
                case SetupAction.Add:
                    EditorApplication.delayCall += AddOnce;
                    break;
                case SetupAction.MarkOnly:
                    TryWriteMarker();
                    break;
            }
        }

        internal static SetupAction Decide(bool markerExists, bool isBatchMode, bool existingInstall)
        {
            if (markerExists || isBatchMode) return SetupAction.None;
            return existingInstall ? SetupAction.MarkOnly : SetupAction.Add;
        }

        /// <summary>심볼 배열 끝에 붙인다. 이미 있으면 null (바꿀 것 없음).</summary>
        internal static string[] WithSymbol(string[] defines, string symbol)
        {
            defines = defines ?? Array.Empty<string>();
            if (Array.IndexOf(defines, symbol) >= 0) return null;
            var result = new string[defines.Length + 1];
            Array.Copy(defines, result, defines.Length);
            result[defines.Length] = symbol;
            return result;
        }

        private static void AddOnce()
        {
            if (File.Exists(MarkerPath)) return;
            try
            {
                var added = new List<string>();
                foreach (var target in Targets)
                {
                    PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
                    var updated = WithSymbol(defines, Symbol);
                    if (updated == null) continue;
                    PlayerSettings.SetScriptingDefineSymbols(target, updated);
                    added.Add(target.TargetName);
                }

                // 표시를 먼저 쓰고 에디터가 비정상 종료되면 심볼은 저장되지 않은 채 다시 시도하지 않는다 → 저장 뒤 표시
                if (added.Count > 0) AssetDatabase.SaveAssets();
                WriteMarker();
                if (added.Count > 0)
                    Debug.Log($"[HLog] {Symbol} added to {string.Join(", ", added)} (one-time setup). Remove it in Player Settings or the HLog console toolbar to turn HLog off.");

                WarnIfActiveTargetNotCovered();
            }
            catch (Exception ex)
            {
                // 마커를 쓰지 않았으므로 다음 리로드에서 다시 시도한다
                Debug.LogWarning($"[HLog] Failed to add {Symbol} to Scripting Define Symbols: {ex.Message}");
            }
        }

        // 에디터 컴파일은 현재 플랫폼 심볼을 쓴다. 세 플랫폼 밖(WebGL 등)이면 켜지지 않았다는 것을 알린다
        private static void WarnIfActiveTargetNotCovered()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var active = NamedBuildTarget.FromBuildTargetGroup(group);
            if (Array.IndexOf(Targets, active) >= 0) return;

            PlayerSettings.GetScriptingDefineSymbols(active, out string[] defines);
            if (Array.IndexOf(defines, Symbol) >= 0) return;
            Debug.Log($"[HLog] {Symbol} was not added to the current platform ({active.TargetName}). Turn it on with the HLog console toolbar button to use HLog in the editor.");
        }

        private static void TryWriteMarker()
        {
            try { WriteMarker(); }
            catch (Exception ex) { Debug.LogWarning($"[HLog] Failed to write {MarkerPath}: {ex.Message}"); }
        }

        private static void WriteMarker()
        {
            // 쓰다가 실패해도 반쯤 쓴 마커가 남지 않도록 임시 파일에 쓴 뒤 옮긴다
            string tempPath = MarkerPath + ".tmp";
            File.WriteAllText(tempPath,
                "HB_InGameConsole: USE_HLOG was added to Scripting Define Symbols once for this project.\n" +
                "It will not be added again while this file exists. Delete this file to allow it again.\n", new UTF8Encoding(false));
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            File.Move(tempPath, MarkerPath);
        }
    }

    /// <summary>
    /// 릴리즈 빌드(Development 빌드가 아님 또는 LIVE 심볼)에 USE_HLOG가 켜져 있으면 경고한다 (빌드는 막지 않는다).
    /// 빌드 도구와 무관하게 돈다. HB_Build를 쓰면 LIVE DefineContainer 항목에서 USE_HLOG를 빼면 꺼진다.
    /// </summary>
    internal sealed class HLogReleaseBuildCheck : IPreprocessBuildWithReport
    {
        public const string ReleaseDefine = "LIVE";

        // 다른 빌드 단계가 심볼을 바꾼 뒤에 검사하도록 늦게 실행한다
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Dedicated Server 빌드는 Standalone 그룹이지만 Server 심볼을 쓴다 (빌드 리포트로는 서브타깃을 읽지 않고 에디터의 현재 설정을 본다)
            var target = report.summary.platformGroup == BuildTargetGroup.Standalone
                         && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            var all = new HashSet<string>(defines, StringComparer.Ordinal);
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile != null && profile.scriptingDefines != null)
                all.UnionWith(profile.scriptingDefines);

            bool development = (report.summary.options & BuildOptions.Development) != 0;
            if (IsReleaseWithHLog(development, all, out string reason))
            {
                Debug.LogWarning($"[HLog] Release build ({reason}) with {LogChannelGeneratorSettings.ConditionalSymbol} ON: HLog logging and Watch are compiled into this build. " +
                                 $"Remove {LogChannelGeneratorSettings.ConditionalSymbol} for release (HB_Build: leave it out of the LIVE DefineContainer entry).");
            }
        }

        internal static bool IsReleaseWithHLog(bool development, ICollection<string> defines, out string reason)
        {
            reason = null;
            if (!defines.Contains(LogChannelGeneratorSettings.ConditionalSymbol)) return false;

            if (defines.Contains(ReleaseDefine)) reason = $"define {ReleaseDefine}";
            else if (!development) reason = "non-development build";
            return reason != null;
        }
    }
}
