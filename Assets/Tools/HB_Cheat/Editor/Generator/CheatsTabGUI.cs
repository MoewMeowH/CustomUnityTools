#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 에디터 창의 Cheats 탭: 스캔 결과·검증 메시지, 플레이 모드에서는 패널로 열기 / 실행 히스토리.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatsTabGUI
    {
        private static string s_Filter = string.Empty;

        public static void Draw()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rescan", GUILayout.Width(80))) CheatCodeGenerator.Scan(out _, out _);
                if (GUILayout.Button("Generate", GUILayout.Width(80))) CheatCodeGenerator.Generate(false);
                s_Filter = EditorGUILayout.TextField(s_Filter, EditorStyles.toolbarSearchField);
            }

            var issues = CheatCodeGenerator.LastIssues;
            if (issues.Count > 0)
            {
                EditorGUILayout.Space(4);
                foreach (var issue in issues)
                {
                    EditorGUILayout.HelpBox($"{CheatCodeGenerator.Describe(issue.Method)}\n{issue.Message}",
                        issue.IsError ? MessageType.Error : MessageType.Warning);
                }
            }

            EditorGUILayout.Space(4);
            bool playing = EditorApplication.isPlaying && CheatSystem.IsInitialized;
            if (!playing)
                EditorGUILayout.HelpBox("Play Mode에서 패널 열기와 실행 히스토리를 쓸 수 있습니다.", MessageType.None);

            var cheats = CheatCodeGenerator.LastCheats;
            EditorGUILayout.LabelField($"Cheats ({cheats.Count})", EditorStyles.boldLabel);

            foreach (var group in cheats.GroupBy(c => c.AssemblyName))
            {
                EditorGUILayout.LabelField(group.Key, EditorStyles.miniBoldLabel);
                foreach (var cheat in group.OrderBy(c => c.Attribute.Path))
                {
                    var a = cheat.Attribute;
                    if (s_Filter.Length > 0
                        && a.Path.IndexOf(s_Filter, System.StringComparison.OrdinalIgnoreCase) < 0
                        && a.Id.IndexOf(s_Filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        string args = string.Join(", ", cheat.Args.Select(arg => $"{arg.Parameter.ParameterType.Name} {arg.Parameter.Name} [{arg.Editor}]"));
                        EditorGUILayout.LabelField($"{a.Path}   ({a.Id}, {a.Target})\n{args}", EditorStyles.wordWrappedMiniLabel);

                        if (GUILayout.Button("Script", GUILayout.Width(52)))
                            OpenScript(cheat);

                        using (new EditorGUI.DisabledScope(!playing))
                        {
                            if (GUILayout.Button("Show", GUILayout.Width(48)))
                                CheatSystem.ShowCheat(a.Id);
                        }
                    }
                }
            }

            if (playing) DrawHistory();
        }

        private static void DrawHistory()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
            var history = CheatSystem.History;
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - 20; i--)
            {
                var e = history[i];
                EditorGUILayout.LabelField($"#{e.Id} {e.Status}  {e.Describe()}" + (string.IsNullOrEmpty(e.Message) ? string.Empty : $"  — {e.Message}"),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void OpenScript(ScannedCheat cheat)
        {
            var type = cheat.Method.DeclaringType;
            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.text.Contains(cheat.Method.Name))
                {
                    AssetDatabase.OpenAsset(script);
                    return;
                }
            }
            Debug.LogWarning($"[HB_Cheat] Script for {type.FullName} not found (file name should match the class name).");
        }
    }
}
