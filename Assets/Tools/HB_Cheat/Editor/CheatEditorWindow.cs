using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// Tools > HB > Cheat. 심볼 ON/OFF, 설정, 치트 파일·asmdef 생성, 레지스트리 생성, 등록 치트 확인.
    /// </summary>
    public class CheatEditorWindow : EditorWindow
    {
        private static readonly string[] s_Tabs = { "Settings", "Cheats" };

        private int m_Tab;
        private Vector2 m_Scroll;
        private SerializedObject m_SerializedSettings;
        private string m_NewClassName = "ItemCheats";

        [MenuItem("Tools/HB/Cheat", priority = 30)]
        public static void Open()
        {
            var window = GetWindow<CheatEditorWindow>("HB Cheat");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnGUI()
        {
            m_Tab = GUILayout.Toolbar(m_Tab, s_Tabs);
            EditorGUILayout.Space(4);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Tab == 0) DrawSettingsTab();
            else DrawCheatsTab();
            EditorGUILayout.EndScrollView();
        }

        #region Settings

        private void DrawSettingsTab()
        {
            DrawDefineSection();
            EditorGUILayout.Space(8);

            if (!CheatSettings.AssetExists)
            {
                EditorGUILayout.HelpBox($"설정 에셋이 없어 기본값을 사용합니다. ({CheatSettings.DefaultAssetPath})", MessageType.Info);
                if (GUILayout.Button("Create Settings Asset"))
                {
                    CheatSettings.CreateAsset();
                    m_SerializedSettings = null;
                }
            }
            else
            {
                DrawSettingsAsset();
            }

            EditorGUILayout.Space(8);
            DrawCodeSection();
        }

        private void DrawDefineSection()
        {
            var settings = CheatSettings.Load();
            EditorGUILayout.LabelField("Cheat Defines", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"{CheatDefines.CoreDefine} (패키지) + {settings.UserDefine} (프로젝트)를 함께 켜고 끕니다.\n" +
                "릴리즈 빌드에서 켜져 있거나 한쪽만 켜져 있으면 빌드가 실패합니다.", MessageType.None);

            foreach (var target in CheatDefines.Targets)
            {
                var defines = CheatDefines.GetDefines(target);
                bool enabled = defines.Contains(CheatDefines.CoreDefine) && defines.Contains(settings.UserDefine);
                bool mismatch = CheatDefines.IsMismatched(defines, settings.UserDefine);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(target.TargetName, GUILayout.Width(110));
                    var color = GUI.color;
                    GUI.color = mismatch ? new Color(1f, 0.6f, 0.3f) : enabled ? new Color(0.5f, 1f, 0.6f) : color;
                    EditorGUILayout.LabelField(mismatch ? "MISMATCH" : enabled ? "ON" : "OFF", EditorStyles.boldLabel, GUILayout.Width(90));
                    GUI.color = color;

                    if (GUILayout.Button(enabled ? "Turn Off" : "Turn On", GUILayout.Width(90)))
                        CheatDefines.SetEnabled(target, !enabled);
                }
            }

            var profileDefines = CheatDefines.GetActiveProfileDefines();
            if (profileDefines.Contains(CheatDefines.CoreDefine) || profileDefines.Contains(settings.UserDefine))
            {
                EditorGUILayout.HelpBox("활성 Build Profile의 Scripting Defines에도 치트 심볼이 있습니다. 위 토글은 Player Settings만 바꿉니다.", MessageType.Warning);
            }

            if (CheatDefines.NeedsManualConsoleDefine())
            {
                EditorGUILayout.HelpBox($"InGameConsole이 UPM 패키지가 아닌 Assets 폴더에 있어 {CheatDefines.ConsoleDefine}를 심볼과 함께 관리합니다.", MessageType.None);
            }
        }

        private void DrawSettingsAsset()
        {
            var settings = CheatSettings.Load();
            if (m_SerializedSettings == null || m_SerializedSettings.targetObject != settings)
                m_SerializedSettings = new SerializedObject(settings);

            m_SerializedSettings.Update();
            string oldUserDefine = settings.UserDefine;

            EditorGUI.BeginChangeCheck();
            var it = m_SerializedSettings.GetIterator();
            it.NextVisible(true); // m_Script
            while (it.NextVisible(false))
                EditorGUILayout.PropertyField(it, true);
            bool changed = EditorGUI.EndChangeCheck();

            m_SerializedSettings.ApplyModifiedProperties();

            if (changed && oldUserDefine != settings.UserDefine)
            {
                CheatDefines.RenameUserDefine(oldUserDefine, settings.UserDefine);
                Debug.LogWarning($"[HB_Cheat] Project define renamed {oldUserDefine} → {settings.UserDefine}. " +
                                 "Update '#if' guards in cheat files and defineConstraints in cheat asmdefs.");
            }
        }

        private void DrawCodeSection()
        {
            var settings = CheatSettings.Load();
            EditorGUILayout.LabelField("Code", EditorStyles.boldLabel);

            string cheatFolder = settings.CheatFolder;
            bool inAssemblyCSharp = CheatFileTemplates.IsAssemblyCSharp(cheatFolder);
            EditorGUILayout.HelpBox(
                inAssemblyCSharp
                    ? $"'{cheatFolder}'는 Assembly-CSharp입니다. 치트 파일은 '#if {settings.UserDefine}'로 감싸서 만듭니다."
                    : $"'{cheatFolder}'는 '{CheatFileTemplates.AssemblyOf(cheatFolder)}' 어셈블리입니다. asmdef의 defineConstraints로 제외됩니다.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                m_NewClassName = EditorGUILayout.TextField("Class Name", m_NewClassName);
                if (GUILayout.Button("Create Cheat File", GUILayout.Width(130)) && !string.IsNullOrWhiteSpace(m_NewClassName))
                {
                    string path = CheatFileTemplates.CreateCheatFile(cheatFolder, m_NewClassName.Trim());
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
                }
            }

            if (inAssemblyCSharp && GUILayout.Button($"Create Cheat asmdef in '{cheatFolder}'"))
            {
                string path = CheatFileTemplates.CreateCheatAsmdef(cheatFolder);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CheatEditorHooks.IsGeneratorAvailable))
                {
                    if (GUILayout.Button("Generate Registry"))
                        CheatEditorHooks.Generate(false);
                }
                if (GUILayout.Button("Delete Generated"))
                {
                    int count = CheatFileTemplates.DeleteGeneratedRegistries();
                    Debug.Log($"[HB_Cheat] Deleted {count} generated registry file(s).");
                }
            }

            if (!CheatEditorHooks.IsGeneratorAvailable)
                EditorGUILayout.HelpBox("현재 플랫폼에서 치트 심볼이 꺼져 있어 레지스트리를 생성할 수 없습니다.", MessageType.Info);

            var generated = CheatFileTemplates.FindGeneratedRegistries();
            if (generated.Length > 0)
                EditorGUILayout.LabelField("Generated: " + string.Join(", ", generated), EditorStyles.wordWrappedMiniLabel);
        }

        #endregion

        private void DrawCheatsTab()
        {
            if (CheatEditorHooks.DrawCheatsTab == null)
            {
                EditorGUILayout.HelpBox("현재 플랫폼에서 치트 심볼이 꺼져 있습니다. Settings 탭에서 켜세요.", MessageType.Info);
                return;
            }
            CheatEditorHooks.DrawCheatsTab();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint();
        }
    }
}
