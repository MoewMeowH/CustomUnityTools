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
                if (IsCheatAssemblyFileName(fileName))
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
            // Dedicated Server 빌드는 Standalone 그룹이지만 Server 심볼을 쓴다 (에디터 설정이 아니라 이 빌드의 서브타깃으로 판정)
            var target = report.summary.platformGroup == BuildTargetGroup.Standalone
                         && report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            var defines = CheatDefines.GetDefines(target);
            foreach (var define in CheatDefines.GetActiveProfileDefines())
                defines.Add(define);
            return defines;
        }

        private static bool ContainsMarker(string path)
        {
            if (!File.Exists(path)) return false;
            return ContainsMarker(File.ReadAllBytes(path));
        }

        /// <summary> HB_Cheat 패키지 어셈블리 파일인지 (HB.Cheat.dll · HB.Cheat.*.dll). HB.CheatSheet.dll 같은 다른 이름은 아니다 </summary>
        internal static bool IsCheatAssemblyFileName(string fileName) =>
            string.Equals(fileName, "HB.Cheat.dll", StringComparison.Ordinal)
            || fileName.StartsWith("HB.Cheat.", StringComparison.Ordinal);

        internal static bool ContainsMarker(byte[] bytes)
        {
            var marker = s_CheatAssemblyMarker;
            for (int i = 0; i <= bytes.Length - marker.Length; i++)
            {
                int j = 0;
                while (j < marker.Length && bytes[i + j] == marker[j]) j++;
                if (j == marker.Length && IsWholeName(bytes, i, marker.Length)) return true;
            }
            return false;
        }

        /// <summary>
        /// 메타데이터 문자열(어셈블리 참조 · 네임스페이스 이름)은 UTF-8 + '\0' 끝으로 저장된다.
        /// 뒤가 '\0'(어셈블리 HB.Cheat) 또는 '.'(HB.Cheat.* 네임스페이스)일 때만 HB_Cheat 참조로 본다 → "HB.CheatSheet" 같은 다른 이름은 걸리지 않는다.
        /// 앞쪽은 보지 않는다: 컴파일러가 끝이 같은 문자열을 접어 저장하면(예: "Game.HB.Cheat"의 꼬리를 "HB.Cheat" 참조가 공유) 앞이 '\0'이 아니어서
        /// 진짜 참조를 놓치기 때문이다 (그래서 "MyHB.Cheat" 같은 이름도 걸린다 — 안전한 쪽). 문자열 상수는 UTF-16이라 처음부터 걸리지 않는다.
        /// </summary>
        internal static bool IsWholeName(byte[] bytes, int start, int length)
        {
            int end = start + length;
            return end >= bytes.Length || bytes[end] == 0 || bytes[end] == (byte)'.';
        }
    }
}
