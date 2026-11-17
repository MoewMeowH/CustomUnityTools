using System.Collections.Generic;
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
            m_ContainerStatus = null;
            Repaint();
        }

        private void RefreshConstrainedAsmdefs()
        {
            m_ConstrainedCheatAsmdefs = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !System.IO.Path.GetFileNameWithoutExtension(path).StartsWith("HB.Cheat", System.StringComparison.Ordinal))
                .Where(path => HasCheatDefineConstraint(System.IO.File.ReadAllText(path)))
                .ToArray();
        }

        [System.Serializable]
        private sealed class AsmdefConstraints
        {
            public string[] defineConstraints;
        }

        /// <summary>
        /// asmdef JSON의 defineConstraints 배열에 HB_CHEAT가 있는지 (문자열 포함 검사는 versionDefines · references 등에 같은 글자가 있어도 걸렸다).
        /// "!HB_CHEAT"(심볼이 없을 때만)는 치트 asmdef가 아니다.
        /// </summary>
        internal static bool HasCheatDefineConstraint(string asmdefJson)
        {
            try
            {
                var data = JsonUtility.FromJson<AsmdefConstraints>(asmdefJson);
                return data?.defineConstraints != null
                       && System.Array.Exists(data.defineConstraints, c => c != null && c.Trim() == CheatDefines.CoreDefine);
            }
            catch (System.ArgumentException)
            {
                return false;   // 깨진 asmdef는 판정하지 않는다
            }
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
            EditorGUILayout.LabelField("Cheat Define", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"{CheatDefines.CoreDefine}를 켜고 끕니다. 패키지와 프로젝트 치트 코드가 같은 심볼을 씁니다.\n" +
                "릴리즈 빌드에서 켜져 있으면 빌드가 실패합니다.", MessageType.None);

            // Standalone · Android · iOS + 현재 활성 플랫폼(그 밖이면, 예: WebGL · Dedicated Server)
            foreach (var target in CheatDefines.GetTargets())
            {
                bool enabled = CheatDefines.IsEnabled(target);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(target.TargetName, GUILayout.Width(110));
                    var color = GUI.color;
                    GUI.color = enabled ? new Color(0.5f, 1f, 0.6f) : color;
                    EditorGUILayout.LabelField(enabled ? "ON" : "OFF", EditorStyles.boldLabel, GUILayout.Width(90));
                    GUI.color = color;

                    if (GUILayout.Button(enabled ? "Turn Off" : "Turn On", GUILayout.Width(90)))
                    {
                        CheatDefines.SetEnabled(target, !enabled);
                        m_ContainerStatus = null;
                        // 상태가 바뀌어 이번 이벤트에서 그릴 컨트롤 수가 Layout 때와 달라질 수 있다
                        GUIUtility.ExitGUI();
                    }
                }
            }

            DrawIdeProjectWarning();

            var profileDefines = CheatDefines.GetActiveProfileDefines();
            if (profileDefines.Contains(CheatDefines.CoreDefine))
            {
                EditorGUILayout.HelpBox("활성 Build Profile의 Scripting Defines에도 치트 심볼이 있습니다. 위 토글은 Player Settings만 바꿉니다.", MessageType.Warning);
            }

            if (CheatDefines.NeedsManualConsoleDefine())
            {
                EditorGUILayout.HelpBox($"InGameConsole이 UPM 패키지가 아닌 Assets 폴더에 있어 {CheatDefines.ConsoleDefine}를 심볼과 함께 관리합니다.", MessageType.None);
            }

            DrawBuildDefineContainerSection();
        }

        // HB_Build DefineContainer 상태 캐시. null이면 다시 읽는다 (매 프레임 에셋을 찾지 않도록)
        private List<CheatBuildDefineSync.EntryStatus> m_ContainerStatus;

        private void OnFocus() => m_ContainerStatus = null;

        /// <summary>HB_Build가 있으면 DefineContainer 항목별 치트 심볼 상태와 맞추기 단추</summary>
        private void DrawBuildDefineContainerSection()
        {
            if (!CheatBuildDefineSync.IsAvailable) return;

            CheatBuildDefineSync.ReadPlayerSettingsState(out bool pcOn, out bool mobileOn);
            m_ContainerStatus ??= CheatBuildDefineSync.Inspect(pcOn, mobileOn);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("HB_Build DefineContainer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "토글하면 그 플랫폼 종류의 DefineContainer 항목에도 반영합니다: PC ← Standalone, MOBILE ← Android 또는 iOS.\n" +
                "켜면 DEV · QA 항목에 넣고(LIVE 제외), 끄면 DEV · QA · LIVE에서 뺍니다. 있는 항목만 고칩니다.", MessageType.None);

            if (mobileOn && CheatDefines.IsEnabled(NamedBuildTarget.Android) != CheatDefines.IsEnabled(NamedBuildTarget.iOS))
            {
                EditorGUILayout.HelpBox("Android · iOS 중 하나만 켜져 있습니다. DefineContainer의 MOBILE 항목은 둘이 같이 쓰므로 다른 쪽 DEV · QA 빌드에도 치트가 들어갑니다.", MessageType.Warning);
            }

            if (m_ContainerStatus.Count == 0)
            {
                EditorGUILayout.LabelField("DefineContainer 항목이 없습니다 (빌드가 Player Settings 심볼을 그대로 씁니다).", EditorStyles.miniLabel);
                return;
            }

            foreach (var group in m_ContainerStatus.GroupBy(e => e.AssetPath))
            {
                EditorGUILayout.LabelField(group.Key, EditorStyles.miniBoldLabel);
                foreach (var entry in group)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"  {entry.BuildType}/{entry.PlatformType}", GUILayout.Width(130));
                        var color = GUI.color;
                        bool warn = entry.IsOutOfSync || entry.IsLiveWithCheat;
                        if (warn) GUI.color = new Color(1f, 0.6f, 0.3f);
                        string state = entry.HasCheat ? "ON" : "OFF";
                        if (entry.IsOutOfSync) state += entry.WantCheat == true ? " (맞춰야 함)" : " (꺼야 함)";
                        else if (entry.IsLiveWithCheat) state += " (LIVE에 치트 심볼)";
                        EditorGUILayout.LabelField(state, EditorStyles.boldLabel);
                        GUI.color = color;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!m_ContainerStatus.Any(e => e.IsOutOfSync)))
            {
                if (GUILayout.Button("DefineContainer에 맞추기"))
                {
                    CheatBuildDefineSync.Sync(pcOn, mobileOn);
                    m_ContainerStatus = null;
                    GUIUtility.ExitGUI();
                }
            }

            if (m_ContainerStatus.Any(e => e.IsLiveWithCheat))
            {
                EditorGUILayout.HelpBox("LIVE 항목에 치트 심볼이 있습니다. 치트를 켜 둔 동안에는 맞추기가 LIVE를 건드리지 않으니 DefineContainer에서 직접 빼세요 (릴리즈 빌드는 빌드 가드가 막지만, 판정은 릴리즈 규칙을 따릅니다).", MessageType.Warning);
            }
        }

        /// <summary>
        /// 치트 asmdef는 심볼이 꺼지면 Unity가 파일 없는 csproj를 만들어, IDE(Rider 등)에서 치트 파일이 프로젝트 밖으로 잡히고 에러로 표시된다.
        /// </summary>
        private void DrawIdeProjectWarning()
        {
            if (m_ConstrainedCheatAsmdefs.Length == 0) return;

            // 에디터 컴파일이 쓰는 대상 (Dedicated Server면 Server 심볼)
            var activeTarget = CheatDefines.GetActiveTarget();
            if (CheatDefines.IsEnabled(activeTarget)) return;

            EditorGUILayout.HelpBox(
                $"현재 플랫폼({activeTarget.TargetName})에서 치트 심볼이 꺼져 있어, 다음 asmdef의 치트 코드가 IDE에서 프로젝트 밖 파일로 표시됩니다 (빨간 에러, 실제 Unity 컴파일과는 무관).\n" +
                string.Join("\n", m_ConstrainedCheatAsmdefs.Select(p => "  - " + p)) +
                "\n치트를 작성·수정하는 동안에는 켜 두세요. 릴리즈 빌드는 빌드 가드가 막습니다.",
                MessageType.Warning);

            if (GUILayout.Button($"{activeTarget.TargetName}에서 켜기"))
            {
                CheatDefines.SetEnabled(activeTarget, true);
                m_ContainerStatus = null;
                GUIUtility.ExitGUI();
            }
        }

        private void DrawSettingsAsset()
        {
            var settings = CheatSettings.Load();
            if (m_SerializedSettings == null || m_SerializedSettings.targetObject != settings)
                m_SerializedSettings = new SerializedObject(settings);

            m_SerializedSettings.Update();

            EditorGUI.BeginChangeCheck();
            var it = m_SerializedSettings.GetIterator();
            it.NextVisible(true); // m_Script
            while (it.NextVisible(false))
                EditorGUILayout.PropertyField(it, true);
            bool changed = EditorGUI.EndChangeCheck();

            m_SerializedSettings.ApplyModifiedProperties();

            // 레지스트리에 들어가는 값이 바뀌었을 때만 저장 · 재생성
            // (리스트 칸처럼 글자마다 바뀌는 것은 포커스를 잃거나 창을 닫을 때 저장)
            if (changed) CheatSettingsWatcher.NotifyChanged();

            if (!settings.AutoGenerate)
                EditorGUILayout.HelpBox("Auto Generate가 꺼져 있습니다. Runtime 설정을 바꾼 뒤 Generate Registry를 눌러야 게임에 반영됩니다.", MessageType.Info);
            else if (CheatSettingsWatcher.IsPending)
                EditorGUILayout.HelpBox("Play Mode 중에 바뀐 설정은 에디트 모드로 돌아오면 레지스트리에 반영합니다.", MessageType.Info);
        }

        private void DrawCodeSection()
        {
            var settings = CheatSettings.Load();
            EditorGUILayout.LabelField("Code", EditorStyles.boldLabel);

            string cheatFolder = settings.CheatFolder;
            bool inAssemblyCSharp = CheatFileTemplates.IsAssemblyCSharp(cheatFolder);
            EditorGUILayout.HelpBox(
                inAssemblyCSharp
                    ? $"'{cheatFolder}'는 Assembly-CSharp입니다. 치트 파일은 '#if {CheatDefines.CoreDefine}'로 감싸서 만듭니다."
                    : $"'{cheatFolder}'는 '{CheatFileTemplates.AssemblyOf(cheatFolder)}' 어셈블리입니다. asmdef의 defineConstraints로 제외됩니다.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                m_NewClassName = EditorGUILayout.TextField("Class Name", m_NewClassName);
                if (GUILayout.Button("Create Cheat File", GUILayout.Width(130)) && !string.IsNullOrWhiteSpace(m_NewClassName))
                {
                    // 클래스 이름이 C# 식별자가 아니면 만들지 않고 알린다 (같은 이름은 뒤에 번호를 붙여 만든다)
                    string error = CheatFileTemplates.ValidateClassName(m_NewClassName.Trim());
                    if (error != null)
                    {
                        EditorUtility.DisplayDialog("Create Cheat File", error, "OK");
                    }
                    else
                    {
                        string path = CheatFileTemplates.CreateCheatFile(cheatFolder, m_NewClassName.Trim());
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
                    }
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
