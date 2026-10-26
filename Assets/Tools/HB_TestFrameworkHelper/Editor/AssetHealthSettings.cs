using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// ProjectSettings/HBTestFrameworkHelperSettings.asset (git으로 팀이 같이 씀). 파일이 없으면 기본값 = 프리팹 슬롯 검사만, 검사 폴더 Assets, 씬 대상 Build Settings.
    /// </summary>
    [FilePath("ProjectSettings/HBTestFrameworkHelperSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AssetHealthSettingsAsset : ScriptableSingleton<AssetHealthSettingsAsset>
    {
        [SerializeField] internal List<string> m_SearchFolders = new List<string> { "Assets" };
        [SerializeField] internal List<string> m_ExcludeFolders = new List<string>();
        [SerializeField] internal List<string> m_EnabledChecks = new List<string> { AssetCheckIds.PrefabSlots };
        [SerializeField] internal ESceneTargetSources m_SceneSources = ESceneTargetSources.BuildSettings;
        [SerializeField] internal List<string> m_SceneFolders = new List<string>();
        [SerializeField] internal List<SceneAsset> m_ExplicitScenes = new List<SceneAsset>();

        internal void SaveNow() => Save(true);
    }

    /// <summary>프로젝트 설정(Project Settings &gt; HB &gt; Test Framework Helper)을 읽는다.</summary>
    public static class AssetHealthSettings
    {
        public const string SettingsPath = "Project/HB/Test Framework Helper";

        /// <summary>지금 프로젝트 설정으로 검사 옵션을 만든다 (복사본 — 고쳐도 설정은 바뀌지 않음).</summary>
        public static AssetHealthScanOptions CreateScanOptions()
        {
            var settings = AssetHealthSettingsAsset.instance;
            var options = new AssetHealthScanOptions
            {
                SearchFolders = new List<string>(settings.m_SearchFolders),
                ExcludeFolders = new List<string>(settings.m_ExcludeFolders),
                EnabledChecks = new List<string>(settings.m_EnabledChecks),
                SceneSources = settings.m_SceneSources,
                SceneFolders = new List<string>(settings.m_SceneFolders),
            };

            // 삭제된 씬을 가리키는 칸은 "없는 씬"으로 넘겨 검사 실패로 남긴다 (빈 칸은 무시)
            using (var so = new SerializedObject(settings))
            {
                var list = so.FindProperty(nameof(AssetHealthSettingsAsset.m_ExplicitScenes));
                for (int i = 0; i < list.arraySize; i++)
                {
                    var element = list.GetArrayElementAtIndex(i);
                    if (element.objectReferenceValue != null) options.ExplicitScenePaths.Add(AssetDatabase.GetAssetPath(element.objectReferenceValue));
                    else if (SlotValidatorEngine.IsMissingReference(element)) options.ExplicitScenePaths.Add($"(삭제된 씬, 설정 목록 {i}번)");
                }
            }
            return options;
        }

        public static bool IsCheckEnabled(string checkId) => AssetHealthSettingsAsset.instance.m_EnabledChecks.Contains(checkId);
    }

    internal static class AssetHealthSettingsProvider
    {
        private static SerializedObject s_Serialized;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(AssetHealthSettings.SettingsPath, SettingsScope.Project)
            {
                label = "Test Framework Helper",
                guiHandler = _ => Draw(),
                keywords = new HashSet<string> { "HB", "NeedSlotNullCheck", "Asset Health", "Missing" },
            };
        }

        private static void Draw()
        {
            var settings = AssetHealthSettingsAsset.instance;
            settings.hideFlags &= ~HideFlags.NotEditable;
            if (s_Serialized == null || s_Serialized.targetObject != settings) s_Serialized = new SerializedObject(settings);

            bool changed = false;
            EditorGUILayout.LabelField("켜진 검사", EditorStyles.boldLabel);
            foreach (string id in AssetCheckIds.All)
            {
                bool on = settings.m_EnabledChecks.Contains(id);
                bool next = EditorGUILayout.ToggleLeft(Describe(id), on);
                if (next == on) continue;
                if (next) settings.m_EnabledChecks.Add(id);
                else settings.m_EnabledChecks.Remove(id);
                changed = true;
            }

            s_Serialized.Update();
            EditorGUILayout.Space(8);
            EditorGUILayout.PropertyField(s_Serialized.FindProperty(nameof(AssetHealthSettingsAsset.m_SearchFolders)), new GUIContent("검사 폴더", "프리팹 · ScriptableObject · 머티리얼을 찾는 폴더 (Assets 안)"));
            EditorGUILayout.PropertyField(s_Serialized.FindProperty(nameof(AssetHealthSettingsAsset.m_ExcludeFolders)), new GUIContent("제외 폴더", "직접 고른 씬과 빌드 스텝의 실제 빌드 씬에는 적용되지 않음"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("씬 대상 (여럿 함께 켤 수 있음)", EditorStyles.boldLabel);
            var sources = s_Serialized.FindProperty(nameof(AssetHealthSettingsAsset.m_SceneSources));
            var value = (ESceneTargetSources)sources.intValue;
            value = Flag(value, ESceneTargetSources.BuildSettings, "Build Settings에서 켜진 씬");
            value = Flag(value, ESceneTargetSources.Folders, "지정 폴더의 씬");
            value = Flag(value, ESceneTargetSources.ExplicitScenes, "직접 고른 씬");
            sources.intValue = (int)value;
            EditorGUILayout.PropertyField(s_Serialized.FindProperty(nameof(AssetHealthSettingsAsset.m_SceneFolders)), new GUIContent("씬 폴더", "비우면 검사 폴더"));
            EditorGUILayout.PropertyField(s_Serialized.FindProperty(nameof(AssetHealthSettingsAsset.m_ExplicitScenes)), new GUIContent("직접 고른 씬"));
            EditorGUILayout.HelpBox("HB_Build 빌드 전 검사는 씬 대상 대신 실제 빌드 씬 목록(명령줄 · 프로필 · SceneDefine 필터 뒤)을 검사합니다.", MessageType.None);

            if (s_Serialized.ApplyModifiedPropertiesWithoutUndo()) changed = true;
            if (changed) settings.SaveNow();
        }

        private static ESceneTargetSources Flag(ESceneTargetSources value, ESceneTargetSources flag, string label)
        {
            bool on = EditorGUILayout.ToggleLeft(label, (value & flag) != 0);
            return on ? value | flag : value & ~flag;
        }

        internal static string Describe(string checkId)
        {
            switch (checkId)
            {
                case AssetCheckIds.PrefabSlots: return "프리팹: [NeedSlotNullCheck] 빈 슬롯 · Missing 참조 · Missing 스크립트 (에러)";
                case AssetCheckIds.Scenes: return "씬: [NeedSlotNullCheck] 빈 슬롯 · Missing 참조 · Missing 스크립트 (에러)";
                case AssetCheckIds.ScriptableObjects: return "ScriptableObject: [NeedSlotNullCheck] 빈 슬롯 · Missing 참조 (에러)";
                case AssetCheckIds.Materials: return "머티리얼: 셰이더 없음 · 에러 셰이더 (에러), 텍스처 Missing (경고)";
                case AssetCheckIds.MissingReferences: return "일반 Missing 참조: 어트리뷰트 없는 칸 · 내장 컴포넌트 포함 (경고)";
                default: return checkId;
            }
        }
    }
}
