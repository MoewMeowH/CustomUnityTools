#if UNITY_EDITOR
using InGameConsoleSystem;
using UnityEditor;
using UnityEngine;

namespace InGameConsoleSystem.Editor
{
    public static class InGameConsoleSettingsProvider
    {
        [MenuItem("Tools/HB/In-Game Console/Settings", priority = 11)]
        public static void OpenSettings()
        {
            // 패키지(UPM) 안의 기본 설정은 읽기 전용이므로, 편집은 항상 프로젝트의 사용자 설정 에셋에서 한다
            var settings = AssetDatabase.LoadAssetAtPath<InGameConsoleSettings>(InGameConsoleSettings.UserAssetPath);
            if (settings == null)
            {
                settings = CreateUserSettings();
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private static InGameConsoleSettings CreateUserSettings()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var settings = ScriptableObject.CreateInstance<InGameConsoleSettings>();

            // VisualTree/PanelSettings는 비워 두면 패키지 기본값을 사용한다 (필요할 때만 프로젝트 에셋을 지정)

            AssetDatabase.CreateAsset(settings, InGameConsoleSettings.UserAssetPath);
            AssetDatabase.SaveAssets();

            InGameConsoleSettings.ReloadInstance();
            Debug.Log($"[InGameConsole] Created user settings: {InGameConsoleSettings.UserAssetPath}");
            return settings;
        }
    }
}
#endif
