#if UNITY_EDITOR
using InGameConsoleSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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

            // 현재 쓰이는 설정(패키지 기본값)을 복사해 시작값으로 사용
            var current = InGameConsoleSettings.Instance;
            var settings = current != null
                ? Object.Instantiate(current)
                : ScriptableObject.CreateInstance<InGameConsoleSettings>();

            if (settings.ConsoleVisualTree == null)
                settings.ConsoleVisualTree = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
            if (settings.PanelSettings == null)
                settings.PanelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");

            AssetDatabase.CreateAsset(settings, InGameConsoleSettings.UserAssetPath);
            AssetDatabase.SaveAssets();

            InGameConsoleSettings.ReloadInstance();
            Debug.Log($"[InGameConsole] Created user settings: {InGameConsoleSettings.UserAssetPath}");
            return settings;
        }
    }
}
#endif
