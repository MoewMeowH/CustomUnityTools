using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Compilation;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 치트 심볼 관리. 패키지 심볼(HB_CHEAT)과 프로젝트 심볼(설정의 UserDefine)을 항상 함께 켜고 끈다.
    /// 콘솔이 패키지가 아닌 Assets 폴더로 들어와 있으면 versionDefines가 동작하지 않으므로 HB_CHEAT_HAS_CONSOLE도 함께 관리한다.
    /// </summary>
    public static class CheatDefines
    {
        public const string CoreDefine = "HB_CHEAT";
        public const string ConsoleDefine = "HB_CHEAT_HAS_CONSOLE";
        private const string ConsoleAssemblyName = "HB.InGameConsole";
        private const string ConsolePackageName = "com.hb.ingameconsole";

        public static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS
        };

        public static HashSet<string> GetDefines(NamedBuildTarget target)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            return new HashSet<string>(defines, StringComparer.Ordinal);
        }

        /// <summary>현재 활성 빌드 프로파일의 추가 심볼 (없으면 빈 배열)</summary>
        public static string[] GetActiveProfileDefines()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            return profile != null && profile.scriptingDefines != null ? profile.scriptingDefines : Array.Empty<string>();
        }

        public static bool IsEnabled(NamedBuildTarget target)
        {
            var defines = GetDefines(target);
            return defines.Contains(CoreDefine) && defines.Contains(CheatSettings.Load().UserDefine);
        }

        /// <summary>한쪽만 켜진 상태 (빌드 가드가 실패시킨다)</summary>
        public static bool IsMismatched(ICollection<string> defines, string userDefine)
        {
            return defines.Contains(CoreDefine) != defines.Contains(userDefine);
        }

        public static void SetEnabled(NamedBuildTarget target, bool enabled)
        {
            string userDefine = CheatSettings.Load().UserDefine;
            var defines = GetDefines(target);

            if (enabled)
            {
                defines.Add(CoreDefine);
                defines.Add(userDefine);
                if (NeedsManualConsoleDefine()) defines.Add(ConsoleDefine);
            }
            else
            {
                defines.Remove(CoreDefine);
                defines.Remove(userDefine);
                defines.Remove(ConsoleDefine);
            }

            PlayerSettings.SetScriptingDefineSymbols(target, defines.OrderBy(d => d, StringComparer.Ordinal).ToArray());
            Debug.Log($"[HB_Cheat] {target.TargetName}: cheats {(enabled ? "ON" : "OFF")} ({CoreDefine}, {userDefine})");
        }

        /// <summary>프로젝트 심볼 이름을 바꿀 때 기존 이름을 새 이름으로 교체한다.</summary>
        public static void RenameUserDefine(string oldDefine, string newDefine)
        {
            if (string.IsNullOrWhiteSpace(oldDefine) || string.IsNullOrWhiteSpace(newDefine) || oldDefine == newDefine) return;

            foreach (var target in Targets)
            {
                var defines = GetDefines(target);
                if (!defines.Remove(oldDefine)) continue;
                defines.Add(newDefine);
                PlayerSettings.SetScriptingDefineSymbols(target, defines.OrderBy(d => d, StringComparer.Ordinal).ToArray());
            }
        }

        /// <summary>콘솔 어셈블리가 있지만 UPM 패키지가 아니라서 versionDefines로 감지되지 않는 경우</summary>
        public static bool NeedsManualConsoleDefine()
        {
            bool hasConsoleAssembly = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(ConsoleAssemblyName) != null;
            if (!hasConsoleAssembly) return false;

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + ConsolePackageName);
            return package == null;
        }
    }
}
