#if UNITY_EDITOR
using System.IO;
using InGameConsoleSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace InGameConsoleSystem.Editor
{
    public static class InGameConsoleSettingsProvider
    {
        private const string SettingsAssetPath = "Assets/Tools/HB_InGameConsole/Resources/InGameConsoleSettings.asset";

        [MenuItem("Tools/InGameConsole/Settings", priority = 100)]
        public static void OpenSettings()
        {
            var settings = Resources.Load<InGameConsoleSettings>("InGameConsoleSettings");
            if (settings == null)
            {
                // Resources 폴더 경로 확인 및 생성
                string dir = Path.GetDirectoryName(SettingsAssetPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                settings = ScriptableObject.CreateInstance<InGameConsoleSettings>();
                settings.ConsoleVisualTree = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
                settings.PanelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");

                AssetDatabase.CreateAsset(settings, SettingsAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
#endif
