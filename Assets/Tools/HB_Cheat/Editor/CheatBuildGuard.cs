using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 릴리즈 빌드에 치트가 들어가지 않도록 막는다.
    /// 1) 빌드 전: 릴리즈인데 치트 심볼이 켜져 있거나, 두 심볼(HB_CHEAT / 프로젝트 심볼) 중 하나만 켜져 있으면 실패.
    /// 2) 스크립트 컴파일 직후: 릴리즈인데 컴파일 결과에 HB.Cheat 어셈블리나 그 참조가 있으면 실패 (심볼 설정과 무관하게 결과물 기준).
    /// 이 검사는 산출물을 만들기 전에 멈추는 장치이고, 배포 단계(CI)의 검사를 대신하지 않는다.
    /// </summary>
    internal sealed class CheatBuildGuard : IPreprocessBuildWithReport, IPostBuildPlayerScriptDLLs
    {
        /// <summary>CI에서 강제로 릴리즈 판정: 환경 변수 HB_CHEAT_RELEASE=1</summary>
        public const string ReleaseEnvironmentVariable = "HB_CHEAT_RELEASE";

        private static readonly byte[] s_CheatAssemblyMarker = Encoding.ASCII.GetBytes("HB.Cheat");

        // 다른 빌드 단계가 심볼을 바꾼 뒤에 검사하도록 늦게 실행한다
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = CheatSettings.Load();
            var defines = CollectDefines(report);
            bool hasCore = defines.Contains(CheatDefines.CoreDefine);
            bool hasUser = defines.Contains(settings.UserDefine);

            if (hasCore != hasUser)
            {
                throw new BuildFailedException(
                    $"[HB_Cheat] Define mismatch: {CheatDefines.CoreDefine}={(hasCore ? "ON" : "OFF")}, {settings.UserDefine}={(hasUser ? "ON" : "OFF")}. " +
                    "Turn both on or both off (Tools > HB > Cheat).");
            }

            if (IsRelease(report, defines, out string reason))
            {
                if (hasCore)
                {
                    throw new BuildFailedException(
                        $"[HB_Cheat] Release build ({reason}) with cheat defines ON ({CheatDefines.CoreDefine}, {settings.UserDefine}). " +
                        "Turn cheats off for this build target or change the release rule in Tools > HB > Cheat.");
                }
                Debug.Log($"[HB_Cheat] Release build ({reason}): cheat defines are OFF.");
            }
            else if (hasCore)
            {
                Debug.Log("[HB_Cheat] Non-release build: cheats are included.");
            }
        }

        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            var defines = CollectDefines(report);
            if (!IsRelease(report, defines, out string reason)) return;

            var files = report.GetFiles();
            int checkedCount = 0;
            foreach (var file in files)
            {
                if (!file.path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                checkedCount++;

                string fileName = Path.GetFileName(file.path);
                if (fileName.StartsWith("HB.Cheat", StringComparison.Ordinal))
                    throw new BuildFailedException($"[HB_Cheat] Release build ({reason}) contains cheat assembly '{fileName}'.");

                if (ContainsMarker(file.path))
                    throw new BuildFailedException($"[HB_Cheat] Release build ({reason}): '{fileName}' references HB.Cheat.");
            }

            if (checkedCount == 0)
            {
                const string message = "[HB_Cheat] Release build: no script assemblies were reported, compiled output could not be verified.";
                if (CheatSettings.Load().FailWhenUnverifiable)
                    throw new BuildFailedException(message + " (Disable 'Fail When Unverifiable' in Tools > HB > Cheat to allow.)");
                Debug.LogWarning(message);
            }
            else
                Debug.Log($"[HB_Cheat] Release build: verified {checkedCount} script assemblies contain no cheat code.");
        }

        /// <summary>
        /// 릴리즈 판정: 환경 변수 → 릴리즈 심볼 → Development Build 여부.
        /// BuildPlayerOptions.extraScriptingDefines는 BuildReport에서 읽을 수 없으므로 그 경로로 릴리즈 심볼을 넘기는 빌드는
        /// 환경 변수 HB_CHEAT_RELEASE=1을 함께 설정해야 한다.
        /// </summary>
        public static bool IsRelease(BuildReport report, ICollection<string> defines, out string reason)
        {
            string env = Environment.GetEnvironmentVariable(ReleaseEnvironmentVariable);
            if (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"env {ReleaseEnvironmentVariable}";
                return true;
            }

            var settings = CheatSettings.Load();
            foreach (var define in settings.ReleaseDefines)
            {
                if (!string.IsNullOrWhiteSpace(define) && defines.Contains(define.Trim()))
                {
                    reason = $"define {define.Trim()}";
                    return true;
                }
            }

            if (settings.TreatNonDevelopmentAsRelease && (report.summary.options & BuildOptions.Development) == 0)
            {
                reason = "non-development build";
                return true;
            }

            reason = null;
            return false;
        }

        private static HashSet<string> CollectDefines(BuildReport report)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            var defines = CheatDefines.GetDefines(target);
            foreach (var define in CheatDefines.GetActiveProfileDefines())
                defines.Add(define);
            return defines;
        }

        private static bool ContainsMarker(string path)
        {
            if (!File.Exists(path)) return false;

            byte[] bytes = File.ReadAllBytes(path);
            var marker = s_CheatAssemblyMarker;
            for (int i = 0; i <= bytes.Length - marker.Length; i++)
            {
                int j = 0;
                while (j < marker.Length && bytes[i + j] == marker[j]) j++;
                if (j == marker.Length) return true;
            }
            return false;
        }
    }
}
