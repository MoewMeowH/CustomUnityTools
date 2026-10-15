#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace InGameConsoleSystem.Editor
{
    public class LogChannelGeneratorWindow : EditorWindow
    {
        private LogChannelGeneratorSettings settings;
        private Vector2 scrollPosition;
        private Vector2 codePreviewScroll;

        [MenuItem("Tools/HB/In-Game Console/Log Channel Generator", priority = 12)]
        public static void OpenWindow()
        {
            var window = GetWindow<LogChannelGeneratorWindow>("Log Channel Generator");
            window.minSize = new Vector2(480, 580);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void CheckAndBootstrapChannels()
        {
            // UPM 패키지 임포트 직후 신규 프로젝트일 때, 프로젝트 폴더에 LogChannels.cs 가 없으면 기본 채널 파일 1회 자동 생성
            EditorApplication.delayCall += () =>
            {
                EnsureLogChannelsExists();
            };
        }

        /// <summary>
        /// 프로젝트 폴더에 LogChannels.cs가 없을 경우(예: UPM 패키지 신규 설치 시) 기본 HLog 확장 클래스 파일을 자동 생성합니다.
        /// </summary>
        public static void EnsureLogChannelsExists()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("LogChannels t:MonoScript");
                if (guids != null && guids.Length > 0)
                {
                    return; // 이미 프로젝트 내에 존재함
                }

                var currentSettings = LogChannelGeneratorSettings.FindSettings();
                string targetDir = currentSettings != null && !string.IsNullOrWhiteSpace(currentSettings.OutputDirectory)
                    ? currentSettings.OutputDirectory.Trim()
                    : "Assets/Scripts";
                string targetFileName = currentSettings != null && !string.IsNullOrWhiteSpace(currentSettings.OutputFileName)
                    ? currentSettings.OutputFileName.Trim()
                    : "LogChannels.cs";
                string filePath = Path.Combine(targetDir, targetFileName);

                if (File.Exists(filePath))
                {
                    return;
                }

                Debug.Log($"[HLog] First-time setup detected. Automatically generating '{filePath}' for UPM integration...");
                SaveCodeToFile(currentSettings, silent: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HLog] Failed to auto-bootstrap LogChannels.cs: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            if (settings == null)
            {
                settings = LogChannelGeneratorSettings.FindSettings();
            }
        }

        private void OnDisable()
        {
            if (settings != null)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                if (settings.AutoGenerateOnSave)
                {
                    SaveCodeToFile(settings, silent: true);
                }
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawSettingsSection();
            EditorGUILayout.Space(10);

            if (settings != null)
            {
                DrawChannelListSection();
                EditorGUILayout.Space(12);

                DrawCodePreviewSection();
                EditorGUILayout.Space(10);

                DrawActionButtons();
            }
            else
            {
                DrawNoSettingsHelpBox();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("HLog Channel Generator (Class Extension)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("정적 partial 클래스 HLog를 확장하여 HLog.[Channel].Log 형태의 인텔리센스 호출을 지원합니다.\n네임스페이스 없이 전역 클래스이므로 using HLog; 없이도 UnityEngine.UI 처럼 즉시 사용 가능합니다.\nUPM 패키지 환경에서도 생성 코드는 Assets 폴더에 안전하게 생성/관리됩니다.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.LabelField("환경 설정 (Settings)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newSettings = (LogChannelGeneratorSettings)EditorGUILayout.ObjectField("설정 에셋", settings, typeof(LogChannelGeneratorSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                settings = newSettings;
            }

            if (settings != null)
            {
                if (GUILayout.Button("Find", GUILayout.Width(45)))
                {
                    EditorGUIUtility.PingObject(settings);
                }
            }
            else
            {
                if (GUILayout.Button("Find", GUILayout.Width(45)))
                {
                    settings = LogChannelGeneratorSettings.FindSettings();
                    if (settings != null) EditorGUIUtility.PingObject(settings);
                }
            }

            if (GUILayout.Button("New", GUILayout.Width(45)))
            {
                CreateNewSettingsAsset();
            }
            EditorGUILayout.EndHorizontal();

            if (settings != null)
            {
                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                string newDir = EditorGUILayout.TextField("생성 경로", settings.OutputDirectory);
                string newFile = EditorGUILayout.TextField("파일명", settings.OutputFileName);
                string newCond = EditorGUILayout.TextField("Conditional 심볼", settings.ConditionalSymbol);
                bool newUseSame = EditorGUILayout.Toggle("변수명과 태그명을 동일하게 사용", settings.UseSameName);
                bool newAutoSave = EditorGUILayout.Toggle("에디터 변경 시 자동 코드 갱신", settings.AutoGenerateOnSave);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(settings, "Modify Log Channel Generator Settings");
                    settings.OutputDirectory = newDir;
                    settings.OutputFileName = newFile;
                    settings.ConditionalSymbol = newCond;
                    settings.UseSameName = newUseSame;
                    settings.AutoGenerateOnSave = newAutoSave;

                    if (newUseSame)
                    {
                        foreach (var item in settings.Channels)
                        {
                            item.tagName = item.varName;
                        }
                    }
                    EditorUtility.SetDirty(settings);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawNoSettingsHelpBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox("프로젝트 설정 에셋(LogChannelGeneratorSettings.asset)이 필요합니다.\n아래 [설정 에셋 생성] 버튼을 클릭해 폴더에 설정 파일을 생성하면 채널 목록을 편집할 수 있습니다.", MessageType.Warning);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("설정 에셋 생성 (Create Settings Asset)", GUILayout.Height(30)))
            {
                settings = LogChannelGeneratorSettings.CreateSettingsAsset();
                if (settings != null)
                {
                    EditorGUIUtility.PingObject(settings);
                    Selection.activeObject = settings;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void CreateNewSettingsAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("새 설정 에셋 저장", "LogChannelGeneratorSettings", "asset", "생성할 설정 파일 이름을 지정하세요.");
            if (!string.IsNullOrEmpty(path))
            {
                settings = LogChannelGeneratorSettings.CreateSettingsAsset(path);
                if (settings != null)
                {
                    EditorGUIUtility.PingObject(settings);
                    Selection.activeObject = settings;
                }
            }
        }

        private void DrawChannelListSection()
        {
            var channels = settings.Channels;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"등록된 채널 목록 ({channels.Count})", EditorStyles.boldLabel);

            if (GUILayout.Button("+ 채널 추가", GUILayout.Width(90)))
            {
                Undo.RecordObject(settings, "Add Channel");
                string newName = $"Channel{channels.Count + 1}";
                channels.Add(new LogChannelGeneratorSettings.ChannelItem(newName));
                EditorUtility.SetDirty(settings);
                if (settings.AutoGenerateOnSave)
                {
                    SaveCodeToFile(settings, silent: true);
                }
            }
            if (GUILayout.Button("전체 비우기", GUILayout.Width(75)))
            {
                if (EditorUtility.DisplayDialog("채널 전체 비우기", "등록된 모든 채널을 비우시겠습니까?", "예", "아니오"))
                {
                    Undo.RecordObject(settings, "Clear Channels");
                    channels.Clear();
                    EditorUtility.SetDirty(settings);
                    if (settings.AutoGenerateOnSave)
                    {
                        SaveCodeToFile(settings, silent: true);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (channels.Count == 0)
            {
                EditorGUILayout.HelpBox("등록된 채널이 없습니다. [+ 채널 추가] 버튼을 눌러 추가하세요.", MessageType.Info);
            }

            for (int i = 0; i < channels.Count; i++)
            {
                var item = channels[i];
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField($"[{i + 1}]", GUILayout.Width(24));

                EditorGUI.BeginChangeCheck();
                string newVarName = EditorGUILayout.TextField("채널 클래스명", item.varName);
                string newTagName = item.tagName;

                if (settings.UseSameName)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("태그명 (자동동기화)", item.varName);
                    }
                }
                else
                {
                    newTagName = EditorGUILayout.TextField("태그명", item.tagName);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(settings, "Edit Channel");
                    item.varName = SanitizeIdentifier(newVarName);
                    item.tagName = settings.UseSameName ? item.varName : newTagName;
                    EditorUtility.SetDirty(settings);
                }

                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    Undo.RecordObject(settings, "Remove Channel");
                    channels.RemoveAt(i);
                    EditorUtility.SetDirty(settings);
                    if (settings.AutoGenerateOnSave)
                    {
                        SaveCodeToFile(settings, silent: true);
                    }
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCodePreviewSection()
        {
            EditorGUILayout.LabelField("생성 코드 미리보기", EditorStyles.boldLabel);
            string code = GenerateCode(settings);

            codePreviewScroll = EditorGUILayout.BeginScrollView(codePreviewScroll, GUILayout.Height(180));
            EditorGUILayout.TextArea(code, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("클립보드 복사", GUILayout.Height(32)))
            {
                string code = GenerateCode(settings);
                GUIUtility.systemCopyBuffer = code;
                EditorUtility.DisplayDialog("복사 완료", "생성된 코드가 클립보드에 복사되었습니다.", "확인");
            }

            if (GUILayout.Button("스크립트 파일 갱신 (Save File)", GUILayout.Height(32)))
            {
                SaveCodeToFile(settings, silent: false);
            }

            EditorGUILayout.EndHorizontal();
        }

        public static string GenerateCode(LogChannelGeneratorSettings settings)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated by HLog Channel Generator />");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER");
            sb.AppendLine("using Unity.Scripting.LifecycleManagement;");
            sb.AppendLine("#endif");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER");
            sb.AppendLine("[NoAutoStaticsCleanup]");
            sb.AppendLine("#endif");
            sb.AppendLine("public static partial class HLog");
            sb.AppendLine("{");
            sb.AppendLine("    public static void Watch(string key, object value) => HLogCore.Watch(key, value);");
            sb.AppendLine("    public static void Watch(string key, Func<object> getter, UnityEngine.Object owner = null) => HLogCore.Watch(key, getter, owner);");
            sb.AppendLine("    public static void Watch<T>(string key, Func<T> getter, UnityEngine.Object owner = null) => HLogCore.Watch<T>(key, getter, owner);");
            sb.AppendLine("    public static void Unwatch(string key) => HLogCore.Unwatch(key);");
            sb.AppendLine("    public static void Clear() => HLogCore.Clear();");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 전역 로그 활성화/비활성화 (에디터 및 런타임)");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static bool IsLogEnabled");
            sb.AppendLine("    {");
            sb.AppendLine("        get => HLogCore.IsLogEnabled;");
            sb.AppendLine("        set => HLogCore.IsLogEnabled = value;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 동적 채널 호출 (코드 생성 없이도 원하는 태그 채널을 즉시 사용합니다.)");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static LogChannel Get(string channelName) => HLogCore.GetChannel(channelName);");
            sb.AppendLine("    public static LogChannel Channel(string channelName) => HLogCore.GetChannel(channelName);");
            sb.AppendLine();
            sb.AppendLine("    public static class Core");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void Initialize() => HLogCore.Initialize();");
            sb.AppendLine("        public static void Shutdown() => HLogCore.Shutdown();");
            sb.AppendLine("        public static void Clear() => HLogCore.Clear();");
            sb.AppendLine("        public static void Watch(string key, object value) => HLogCore.Watch(key, value);");
            sb.AppendLine("        public static void Watch(string key, Func<object> getter, UnityEngine.Object owner = null) => HLogCore.Watch(key, getter, owner);");
            sb.AppendLine("        public static void Watch<T>(string key, Func<T> getter, UnityEngine.Object owner = null) => HLogCore.Watch<T>(key, getter, owner);");
            sb.AppendLine("        public static void Unwatch(string key) => HLogCore.Unwatch(key);");
            sb.AppendLine("    }");
            sb.AppendLine();

            string cond = settings != null && !string.IsNullOrWhiteSpace(settings.ConditionalSymbol) 
                ? settings.ConditionalSymbol.Trim() 
                : "USE_HLOG";

            AppendChannelClass(sb, "Common", "Common", cond);
            sb.AppendLine();
            AppendChannelClass(sb, "System", "System", cond);

            var channels = settings != null ? settings.Channels : new List<LogChannelGeneratorSettings.ChannelItem>();
            for (int i = 0; i < channels.Count; i++)
            {
                var ch = channels[i];
                if (string.IsNullOrWhiteSpace(ch.varName)) continue;
                string vName = SanitizeIdentifier(ch.varName.Trim());
                string tName = string.IsNullOrWhiteSpace(ch.tagName) ? vName : ch.tagName.Trim();

                if (vName == "Common" || vName == "System" || vName == "Core") continue;

                sb.AppendLine();
                AppendChannelClass(sb, vName, tName, cond);
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendChannelClass(StringBuilder sb, string className, string tagName, string cond)
        {
            sb.AppendLine("    #if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER");
            sb.AppendLine("    [NoAutoStaticsCleanup]");
            sb.AppendLine("    #endif");
            sb.AppendLine($"    public static class {className}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public static readonly LogChannel Channel = new LogChannel(\"{tagName}\");");
            sb.AppendLine();
            sb.AppendLine($"        [Conditional(\"UNITY_EDITOR\"), Conditional(\"{cond}\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Log(object message, UnityEngine.Object context = null) => Channel.Log(message, context);");
            sb.AppendLine();
            sb.AppendLine($"        [Conditional(\"UNITY_EDITOR\"), Conditional(\"{cond}\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Warning(object message, UnityEngine.Object context = null) => Channel.Warning(message, context);");
            sb.AppendLine();
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Error(object message, UnityEngine.Object context = null) => Channel.Error(message, context);");
            sb.AppendLine();
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void LogException(Exception exception, UnityEngine.Object context = null) => Channel.LogException(exception, context);");
            sb.AppendLine();
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Exception(Exception exception, UnityEngine.Object context = null) => Channel.Exception(exception, context);");
            sb.AppendLine();
            sb.AppendLine($"        [Conditional(\"UNITY_EDITOR\"), Conditional(\"{cond}\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Watch(string key, object value) => Channel.Watch(key, value);");
            sb.AppendLine();
            sb.AppendLine($"        [Conditional(\"UNITY_EDITOR\"), Conditional(\"{cond}\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Watch(string key, Func<object> getter, UnityEngine.Object owner = null) => Channel.Watch(key, getter, owner);");
            sb.AppendLine();
            sb.AppendLine($"        [Conditional(\"UNITY_EDITOR\"), Conditional(\"{cond}\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Watch<T>(string key, Func<T> getter, UnityEngine.Object owner = null) => Channel.Watch<T>(key, getter, owner);");
            sb.AppendLine("        public static void Unwatch(string key) => Channel.Unwatch(key);");
            sb.AppendLine();
            sb.AppendLine("        [Conditional(\"UNITY_EDITOR\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void BreakIf(bool condition, object message, UnityEngine.Object context = null) => Channel.BreakIf(condition, message, context);");
            sb.AppendLine();
            sb.AppendLine("        [Conditional(\"UNITY_EDITOR\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void Assert(bool condition, object message, UnityEngine.Object context = null) => Channel.Assert(condition, message, context);");
            sb.AppendLine();
            sb.AppendLine("        [Conditional(\"UNITY_EDITOR\")]");
            sb.AppendLine("        [HideInCallstack]");
            sb.AppendLine("        public static void ReportIf(bool condition, string reason, params (string Key, object Value)[] extraVariables) => Channel.ReportIf(condition, reason, extraVariables);");
            sb.AppendLine("    }");
        }

        public static void SaveCodeToFile(LogChannelGeneratorSettings settings, bool silent = false)
        {
            string fileName = settings != null && !string.IsNullOrWhiteSpace(settings.OutputFileName)
                ? settings.OutputFileName.Trim()
                : "LogChannels.cs";
            string dir = settings != null && !string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? settings.OutputDirectory.Trim()
                : "Assets/Scripts";

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string filePath = Path.Combine(dir, fileName);
            string code = GenerateCode(settings);

            File.WriteAllText(filePath, code, Encoding.UTF8);

            if (settings != null)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            AssetDatabase.Refresh();

            if (!silent)
            {
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }

                EditorUtility.DisplayDialog("저장 완료", $"스크립트 파일이 갱신되었습니다:\n{filePath}", "확인");
            }
        }

        private static string SanitizeIdentifier(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string cleaned = Regex.Replace(input, @"[^\w]", "");
            if (cleaned.Length > 0 && char.IsDigit(cleaned[0]))
            {
                cleaned = "_" + cleaned;
            }
            return cleaned;
        }
    }
}
#endif
