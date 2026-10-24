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

        // 치트 심볼을 defineConstraints로 요구하는 프로젝트 asmdef (패키지 자체 asmdef 제외)
        private string[] m_ConstrainedCheatAsmdefs = System.Array.Empty<string>();

        [MenuItem("Tools/HB/Cheat", priority = 30)]
        public static void Open()
        {
            var window = GetWindow<CheatEditorWindow>("HB Cheat");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnEnable() => RefreshConstrainedAsmdefs();

        private void OnLostFocus() => SaveSettingsAsset();

        private void OnDisable() => SaveSettingsAsset();

        private static void SaveSettingsAsset()
        {
            var settings = CheatSettings.Load();
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                AssetDatabase.SaveAssetIfDirty(settings);
        }

        private void OnProjectChange()
        {
            RefreshConstrainedAsmdefs();
            Repaint();
        }

        private void RefreshConstrainedAsmdefs()
        {
            m_ConstrainedCheatAsmdefs = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !System.IO.Path.GetFileNameWithoutExtension(path).StartsWith("HB.Cheat", System.StringComparison.Ordinal))
                .Where(path =>
                {
                    string text = System.IO.File.ReadAllText(path);
                    return text.Contains("defineConstraints") && text.Contains($"\"{CheatDefines.CoreDefine}\"");
                })
                .ToArray();
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

            DrawIdeProjectWarning(settings);

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

        /// <summary>
        /// 치트 asmdef는 심볼이 꺼지면 Unity가 파일 없는 csproj를 만들어, IDE(Rider 등)에서 치트 파일이 프로젝트 밖으로 잡히고 에러로 표시된다.
        /// </summary>
        private void DrawIdeProjectWarning(CheatSettings settings)
        {
            if (m_ConstrainedCheatAsmdefs.Length == 0) return;

            var activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var activeTarget = NamedBuildTarget.FromBuildTargetGroup(activeGroup);
            var defines = CheatDefines.GetDefines(activeTarget);
            if (defines.Contains(CheatDefines.CoreDefine) && defines.Contains(settings.UserDefine)) return;

            EditorGUILayout.HelpBox(
                $"현재 플랫폼({activeTarget.TargetName})에서 치트 심볼이 꺼져 있어, 다음 asmdef의 치트 코드가 IDE에서 프로젝트 밖 파일로 표시됩니다 (빨간 에러, 실제 Unity 컴파일과는 무관).\n" +
                string.Join("\n", m_ConstrainedCheatAsmdefs.Select(p => "  - " + p)) +
                "\n치트를 작성·수정하는 동안에는 켜 두세요. 릴리즈 빌드는 빌드 가드가 막습니다.",
                MessageType.Warning);

            if (GUILayout.Button($"{activeTarget.TargetName}에서 켜기"))
                CheatDefines.SetEnabled(activeTarget, true);
        }

        private void DrawSettingsAsset()
        {
            var settings = CheatSettings.Load();
            if (m_SerializedSettings == null || m_SerializedSettings.targetObject != settings)
                m_SerializedSettings = new SerializedObject(settings);

            m_SerializedSettings.Update();
            string oldUserDefine = settings.UserDefine;
            string oldRegistryInputs = settings.RegistryInputs;

            EditorGUI.BeginChangeCheck();
            var it = m_SerializedSettings.GetIterator();
            it.NextVisible(true); // m_Script
            while (it.NextVisible(false))
                EditorGUILayout.PropertyField(it, true);
            bool changed = EditorGUI.EndChangeCheck();

            m_SerializedSettings.ApplyModifiedProperties();

            if (changed)
            {
                if (oldUserDefine != settings.UserDefine)
                {
                    CheatDefines.RenameUserDefine(oldUserDefine, settings.UserDefine);
                    Debug.LogWarning($"[HB_Cheat] Project define renamed {oldUserDefine} → {settings.UserDefine}. " +
                                     "Update '#if' guards in cheat files and defineConstraints in cheat asmdefs.");
                }

                // 리스트 칸(Delayed 아님)은 글자마다 바뀌므로 그때마다 디스크에 쓰지 않는다 → 포커스를 잃거나 창을 닫을 때 저장
                if (oldRegistryInputs != settings.RegistryInputs)
                {
                    AssetDatabase.SaveAssetIfDirty(settings);
                    RequestRegenerate();
                }
            }

            if (!settings.AutoGenerate)
                EditorGUILayout.HelpBox("Auto Generate가 꺼져 있습니다. Runtime 설정을 바꾼 뒤 Generate Registry를 눌러야 게임에 반영됩니다.", MessageType.Info);
            else if (SessionState.GetBool(RegeneratePendingKey, false))
                EditorGUILayout.HelpBox("Play Mode 중에 바뀐 설정은 에디트 모드로 돌아오면 레지스트리에 반영합니다.", MessageType.Info);
        }

        #region Regenerate

        // Play 중에 바뀐 설정을 에디트 모드로 돌아온 뒤 반영하기 위한 표시 (도메인 리로드를 넘긴다)
        private const string RegeneratePendingKey = "HB.Cheat.RegeneratePending";

        /// <summary>런타임 옵션은 생성 레지스트리 코드에 들어가므로 설정이 바뀌면 다시 생성한다 (내용이 같으면 쓰지 않는다).</summary>
        private static void RequestRegenerate()
        {
            if (!CheatSettings.Load().AutoGenerate) return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(RegeneratePendingKey, true);
                return;
            }

            // OnGUI 안에서 파일 쓰기 · Refresh를 하지 않도록 미룬다
            EditorApplication.delayCall -= RegenerateNow;
            EditorApplication.delayCall += RegenerateNow;
        }

        private static void RegenerateNow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(RegeneratePendingKey, true);
                return;
            }
            SessionState.EraseBool(RegeneratePendingKey);
            // Play 중에 예약한 뒤 Auto Generate를 껐을 수 있다
            if (!CheatSettings.Load().AutoGenerate) return;
            // 현재 플랫폼에서 심볼이 꺼져 있으면 생성기가 없다 → 켜질 때 생성기가 로드되며 생성한다
            CheatEditorHooks.Generate?.Invoke(true);
        }

        [InitializeOnLoadMethod]
        private static void HookPlayModeRegenerate()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RegeneratePendingKey, false))
                    RegenerateNow();
            };
        }

        #endregion

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
