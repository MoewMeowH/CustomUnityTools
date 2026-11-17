using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>
    /// HB_Build(com.hb.custombuild)가 있으면 치트 켜짐 상태를 DefineContainer(ProjectDefine) 에셋에도 반영한다.
    /// HB_Build는 빌드 때 항목이 있으면 그 목록으로 심볼을 통째로 바꾸므로, PlayerSettings만 켜면 그 빌드에서 치트가 빠진다.
    /// HB_Build 어셈블리를 참조하지 않는다 (Assets 설치면 versionDefines가 동작하지 않아서) → 타입 전체 이름으로 찾고(어셈블리 이름 무관) SerializedObject로 고친다.
    /// 규칙: PC 항목 ← Standalone, MOBILE 항목 ← Android 또는 iOS. 켜짐이면 DEV · QA에 넣고(LIVE는 그대로), 꺼짐이면 DEV · QA · LIVE에서 뺀다.
    /// 이미 있는 항목만 고친다 (항목이 없으면 HB_Build가 PlayerSettings를 그대로 쓴다. 만들면 그 플랫폼의 다른 심볼이 다 지워진다).
    /// 토글은 그 플랫폼 종류(PC · MOBILE) 항목만 고치고, 전체 맞추기는 창의 단추로만 한다 (손으로 고친 다른 항목을 건드리지 않도록).
    /// (static은 타입 캐시 · 경고 한 번 표시용이라 도메인 리로드에서만 초기화)
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatBuildDefineSync
    {
        internal const string ProjectDefineTypeFullName = "HB.CustomBuild.Editor.ProjectDefine";
        internal const string PlatformPC = "PC";
        internal const string PlatformMobile = "MOBILE";
        private const string ContainerListField = "m_DefineContainer";
        private const string BuildTypeField = "BuildType";
        private const string PlatformTypeField = "PlatformType";
        private const string DefinesField = "DefineArr";

        private static bool s_WarnedLayout;
        private static bool s_TypeResolved;
        private static Type s_ProjectDefineType;

        internal sealed class EntryStatus
        {
            public string AssetPath;
            public string BuildType;
            public string PlatformType;
            public bool HasCheat;
            /// <summary>true = 있어야 함, false = 없어야 함, null = 규칙 밖 (그대로 둠)</summary>
            public bool? WantCheat;
            /// <summary>맞추기를 하면 바뀌는 항목 (콘솔 심볼 포함)</summary>
            public bool IsOutOfSync;
            /// <summary>LIVE 항목에 치트 심볼이 있음 (켜짐 상태에서는 규칙 밖이라 맞추기가 빼지 않는다)</summary>
            public bool IsLiveWithCheat => BuildType == "LIVE" && HasCheat;
        }

        /// <summary>ProjectDefine 타입 (어셈블리 이름과 무관하게 전체 이름으로 찾는다. HB_Build가 없으면 null)</summary>
        public static Type ProjectDefineType
        {
            get
            {
                if (!s_TypeResolved)
                {
                    s_ProjectDefineType = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                        .FirstOrDefault(t => t.FullName == ProjectDefineTypeFullName);
                    s_TypeResolved = true;
                }
                return s_ProjectDefineType;
            }
        }

        public static bool IsAvailable => ProjectDefineType != null;

        /// <summary>
        /// 항목 하나가 치트 심볼을 가져야 하는지. true = 넣기, false = 빼기, null = 건드리지 않음.
        /// </summary>
        internal static bool? WantCheat(string buildType, string platformType, bool pcOn, bool mobileOn)
        {
            bool on;
            switch (platformType)
            {
                case PlatformPC: on = pcOn; break;
                case PlatformMobile: on = mobileOn; break;
                default: return null;
            }

            switch (buildType)
            {
                case "DEV":
                case "QA":
                    return on;
                case "LIVE":
                    // 켜져 있어도 LIVE에는 넣지 않는다. 손으로 넣은 것은 빌드 가드가 막는다
                    return on ? (bool?)null : false;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 항목의 심볼 목록에 규칙을 적용한 새 목록. 바꿀 것이 없으면 null.
        /// 넣을 때는 addDefines 중 없는 것을 끝에 붙이고, 뺄 때는 removeDefines를 모두 뺀다.
        /// </summary>
        internal static string[] Apply(string[] defines, bool want, string[] addDefines, string[] removeDefines)
        {
            defines = defines ?? Array.Empty<string>();
            if (want)
            {
                var missing = addDefines.Where(d => Array.IndexOf(defines, d) < 0).ToArray();
                return missing.Length == 0 ? null : defines.Concat(missing).ToArray();
            }

            if (!defines.Any(d => Array.IndexOf(removeDefines, d) >= 0)) return null;
            return defines.Where(d => Array.IndexOf(removeDefines, d) < 0).ToArray();
        }

        /// <summary>PlayerSettings의 지금 켜짐 상태 (PC = Standalone, MOBILE = Android 또는 iOS)</summary>
        public static void ReadPlayerSettingsState(out bool pcOn, out bool mobileOn)
        {
            pcOn = CheatDefines.IsEnabled(NamedBuildTarget.Standalone);
            mobileOn = CheatDefines.IsEnabled(NamedBuildTarget.Android) || CheatDefines.IsEnabled(NamedBuildTarget.iOS);
        }

        /// <summary>플랫폼 → DefineContainer 플랫폼 종류. Standalone → PC, Android · iOS → MOBILE, 그 밖(WebGL · Dedicated Server 등)은 null</summary>
        internal static string PlatformTypeOf(NamedBuildTarget target)
        {
            if (target == NamedBuildTarget.Standalone) return PlatformPC;
            if (target == NamedBuildTarget.Android || target == NamedBuildTarget.iOS) return PlatformMobile;
            return null;
        }

        /// <summary>토글한 플랫폼 종류(PC · MOBILE)의 항목만 PlayerSettings 상태에 맞춘다. HB_Build가 없거나 해당 종류가 없으면 아무것도 하지 않는다.</summary>
        public static int SyncPlatformOf(NamedBuildTarget target)
        {
            string platformType = PlatformTypeOf(target);
            if (platformType == null || !IsAvailable) return 0;
            ReadPlayerSettingsState(out bool pcOn, out bool mobileOn);
            return Sync(pcOn, mobileOn, platformType);
        }

        /// <summary>DefineContainer 항목을 맞춘다 (전체는 창의 맞추기 단추). onlyPlatformType을 주면 그 종류 항목만. 고친 항목 수.</summary>
        public static int Sync(bool pcOn, bool mobileOn, string onlyPlatformType = null)
        {
            var add = CheatDefines.GetManagedDefines(CheatDefines.NeedsManualConsoleDefine());
            var remove = CheatDefines.GetManagedDefines(true);
            var changed = new List<string>();

            int count = RewriteEntries((path, buildType, platformType, defines) =>
            {
                if (onlyPlatformType != null && platformType != onlyPlatformType) return null;
                var want = WantCheat(buildType, platformType, pcOn, mobileOn);
                if (!want.HasValue) return null;
                var updated = Apply(defines, want.Value, add, remove);
                if (updated != null)
                {
                    var diff = want.Value ? updated.Except(defines) : defines.Except(updated);
                    changed.Add($"{System.IO.Path.GetFileName(path)} {buildType}/{platformType} {(want.Value ? "+" : "-")}{string.Join(",", diff)}");
                }
                return updated;
            });

            if (count > 0)
                Debug.Log($"[HB_Cheat] DefineContainer updated: {string.Join("; ", changed)}");
            return count;
        }

        /// <summary>모든 DefineContainer 항목의 상태 (창 표시용)</summary>
        public static List<EntryStatus> Inspect(bool pcOn, bool mobileOn)
        {
            var add = CheatDefines.GetManagedDefines(CheatDefines.NeedsManualConsoleDefine());
            var remove = CheatDefines.GetManagedDefines(true);
            var result = new List<EntryStatus>();
            ForEachEntry((path, entry) =>
            {
                string buildType = EnumName(entry.FindPropertyRelative(BuildTypeField));
                string platformType = EnumName(entry.FindPropertyRelative(PlatformTypeField));
                var defines = ReadDefines(entry.FindPropertyRelative(DefinesField));
                var want = WantCheat(buildType, platformType, pcOn, mobileOn);
                result.Add(new EntryStatus
                {
                    AssetPath = path,
                    BuildType = buildType,
                    PlatformType = platformType,
                    HasCheat = Array.IndexOf(defines, CheatDefines.CoreDefine) >= 0,
                    WantCheat = want,
                    // 맞추기(Sync)와 같은 판정 — HB_CHEAT는 있는데 콘솔 심볼이 빠진 경우도 어긋남
                    IsOutOfSync = want.HasValue && Apply(defines, want.Value, add, remove) != null
                });
                return false;
            });
            return result;
        }

        /// <summary>
        /// 모든 DefineContainer 항목을 돌며 rewrite가 돌려준 목록으로 바꾼다 (null이면 그대로). 바꾼 항목 수.
        /// rewrite(에셋 경로, BuildType, PlatformType, 지금 목록).
        /// </summary>
        internal static int RewriteEntries(Func<string, string, string, string[], string[]> rewrite)
        {
            int count = 0;
            ForEachEntry((path, entry) =>
            {
                var definesProp = entry.FindPropertyRelative(DefinesField);
                var updated = rewrite(path,
                    EnumName(entry.FindPropertyRelative(BuildTypeField)),
                    EnumName(entry.FindPropertyRelative(PlatformTypeField)),
                    ReadDefines(definesProp));
                if (updated == null) return false;

                WriteDefines(definesProp, updated);
                count++;
                return true;
            });
            return count;
        }

        /// <summary>visit(경로, 항목)이 true를 돌려주면 그 에셋을 고친 것으로 보고 적용 · 저장한다.</summary>
        private static void ForEachEntry(Func<string, SerializedProperty, bool> visit)
        {
            var type = ProjectDefineType;
            if (type == null) return;

            // Packages 아래(읽기 전용일 수 있음)는 고치지 않는다
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null) continue;

                using var so = new SerializedObject(asset);
                var list = so.FindProperty(ContainerListField);
                if (list == null || !list.isArray || !HasExpectedLayout(list))
                {
                    WarnLayoutOnce(path);
                    continue;
                }

                bool modified = false;
                for (int i = 0; i < list.arraySize; i++)
                    modified |= visit(path, list.GetArrayElementAtIndex(i));

                if (!modified) continue;
                so.ApplyModifiedProperties();   // Undo 기록 · dirty 표시
                AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        private static bool HasExpectedLayout(SerializedProperty list)
        {
            if (list.arraySize == 0) return true;
            var entry = list.GetArrayElementAtIndex(0);
            return entry.FindPropertyRelative(BuildTypeField)?.propertyType == SerializedPropertyType.Enum
                   && entry.FindPropertyRelative(PlatformTypeField)?.propertyType == SerializedPropertyType.Enum
                   && entry.FindPropertyRelative(DefinesField)?.isArray == true;
        }

        private static void WarnLayoutOnce(string path)
        {
            if (s_WarnedLayout) return;
            s_WarnedLayout = true;
            Debug.LogWarning($"[HB_Cheat] DefineContainer '{path}' does not have the expected fields ({ContainerListField}[].{BuildTypeField}/{PlatformTypeField}/{DefinesField}). " +
                             "HB_Build version mismatch? Cheat defines are not synced to DefineContainer.");
        }

        private static string EnumName(SerializedProperty prop)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Enum) return null;
            int index = prop.enumValueIndex;
            return index >= 0 && index < prop.enumNames.Length ? prop.enumNames[index] : null;
        }

        private static string[] ReadDefines(SerializedProperty array)
        {
            var result = new string[array.arraySize];
            for (int i = 0; i < result.Length; i++)
                result[i] = array.GetArrayElementAtIndex(i).stringValue;
            return result;
        }

        private static void WriteDefines(SerializedProperty array, string[] defines)
        {
            array.arraySize = defines.Length;
            for (int i = 0; i < defines.Length; i++)
                array.GetArrayElementAtIndex(i).stringValue = defines[i];
        }
    }
}
