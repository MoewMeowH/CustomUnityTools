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

        /// <summary>
        /// 켜고 끄기 · 이름 변경 대상: Standalone · Android · iOS(<see cref="Targets"/>) + 현재 활성 플랫폼이 그 밖이면 그 플랫폼
        /// (WebGL · Dedicated Server 등. 에디터 컴파일은 활성 플랫폼 심볼을 쓴다)
        /// </summary>
        public static List<NamedBuildTarget> GetTargets()
        {
            var targets = new List<NamedBuildTarget>(Targets);
            var active = GetActiveTarget();
            if (!targets.Contains(active)) targets.Add(active);
            return targets;
        }

        /// <summary> 현재 활성 플랫폼의 심볼 대상 (Dedicated Server는 Standalone 그룹이지만 Server 심볼을 쓴다) </summary>
        public static NamedBuildTarget GetActiveTarget()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            return group == BuildTargetGroup.Standalone && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(group);
        }

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

        /// <summary>
        /// 프로젝트 심볼 이름을 바꿀 때 기존 이름을 새 이름으로 교체한다.
        /// 지금 창에 보이는 대상만이 아니라 옛 이름이 남아 있는 모든 플랫폼을 바꾼다
        /// (예전에 WebGL에서 켜 둔 뒤 다른 플랫폼으로 바꿨어도 WebGL에 옛 이름이 남아 MISMATCH가 되지 않게).
        /// </summary>
        public static void RenameUserDefine(string oldDefine, string newDefine)
        {
            if (string.IsNullOrWhiteSpace(oldDefine) || string.IsNullOrWhiteSpace(newDefine) || oldDefine == newDefine) return;

            foreach (var target in GetAllNamedTargets())
            {
                var defines = GetDefines(target);
                if (!defines.Remove(oldDefine)) continue;
                defines.Add(newDefine);
                PlayerSettings.SetScriptingDefineSymbols(target, defines.OrderBy(d => d, StringComparer.Ordinal).ToArray());
            }
        }

        /// <summary> 심볼을 가질 수 있는 모든 플랫폼 (BuildTargetGroup 값 + Dedicated Server). 쓰지 않는 · 없어진 그룹은 건너뛴다 </summary>
        internal static List<NamedBuildTarget> GetAllNamedTargets()
        {
            var result = new List<NamedBuildTarget>();
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown) continue;
                try
                {
                    var target = NamedBuildTarget.FromBuildTargetGroup(group);
                    if (!result.Contains(target)) result.Add(target);
                }
                catch (Exception)
                {
                    // 없어진(Obsolete) 그룹은 NamedBuildTarget으로 바꿀 수 없다
                }
            }
            if (!result.Contains(NamedBuildTarget.Server)) result.Add(NamedBuildTarget.Server);
            return result;
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
