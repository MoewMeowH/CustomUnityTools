using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HB.E2String.Editor
{
    /// <summary>
    /// E2String 적용 Enum 스캔 결과 데이터
    /// </summary>
    [Serializable]
    public class E2StringEnumInfo
    {
        public string EnumName;
        public string Namespace;
        public string ContainingTypes; // 중첩 enum의 바깥 타입 경로 (예: "Outer.Inner"), 없으면 빈 문자열
        public string AssetPath;
        public int LineNumber;
        public string AttributeName;
        public List<string> Members = new List<string>();

        /// <summary>네임스페이스를 제외한 타입 이름 (예: Outer.MyEnum)</summary>
        public string TypeName => string.IsNullOrEmpty(ContainingTypes) ? EnumName : $"{ContainingTypes}.{EnumName}";

        public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

        /// <summary>Source Generator가 생성하는 확장 클래스 이름 (중첩 경로의 '.'을 '_'로 치환)</summary>
        public string ExtensionClassName => $"{TypeName.Replace('.', '_')}Extensions";
    }

    /// <summary>
    /// E2String Source Generator 전용 에디터 매니저 윈도우입니다.
    /// - 프로젝트 내 E2String 어트리뷰트가 적용된 Enum 목록 시각화
    /// - 어트리뷰트 스크립트 존재 여부 검사 및 원하는 경로(서버 공유 폴더 등)로 원클릭 생성
    /// - 빠른 소스 코드 이동 및 코드 치트시트 제공
    /// </summary>
    public class E2StringManagerWindow : EditorWindow
    {
        // EditorPrefs는 PC의 모든 프로젝트가 공유하므로 프로젝트별로 키를 분리한다
        private static string PREFS_TARGET_FOLDER_KEY => $"HB_E2String_TargetFolder_{PlayerSettings.productGUID}";
        // 패키지(UPM) 폴더는 읽기 전용이므로 기본 생성 위치는 프로젝트의 Assets 아래로 둔다
        private const string DEFAULT_TARGET_FOLDER = "Assets/Scripts/E2String";
        private const string COMPAT_NAMESPACE = "EString.Script";
        private const string ATTRIBUTE_FILE_NAME = "E2StringAttribute.cs";

        // 스캔용 정규식 (주석·문자열·전처리 지시문이 제거된 코드에 적용)
        private static readonly Regex s_ScopeTokenRegex = new Regex(
            @"\{|\}|\bnamespace\s+(?<ns>[\w.]+)|\b(?:class|struct)\s+(?<type>\w+)|\benum\s+(?<enum>\w+)",
            RegexOptions.Compiled);
        private static readonly Regex s_E2StringAttrRegex = new Regex(
            @"\[[^\]]*?(?<![\w.])(?:\w+\.)*E2String(?:Attribute)?(?![\w.])[^\]]*\]",
            RegexOptions.Compiled);
        private static readonly Regex s_AttributeSectionRegex = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex s_IdentifierRegex = new Regex(@"^[\p{L}_][\p{L}\p{Nd}_]*$", RegexOptions.Compiled);
        private static readonly Regex s_AttributeClassRegex = new Regex(@"\bclass\s+E2StringAttribute\b", RegexOptions.Compiled);
        private static readonly Regex s_NamespaceNameRegex = new Regex(
            @"^[\p{L}_][\p{L}\p{Nd}_]*(?:\.[\p{L}_][\p{L}\p{Nd}_]*)*$", RegexOptions.Compiled);

        // 상태 변수 (도메인 리로드 후에도 유지되어 재스캔을 피한다)
        [SerializeField] private bool m_HasScanned = false;
        [SerializeField] private bool m_IsAttributeInstalled = false;
        [SerializeField] private string m_InstalledAttributePath = string.Empty;
        private string m_TargetFolderPath = DEFAULT_TARGET_FOLDER;
        private bool m_UseGlobalNamespace = true;
        private string m_CustomNamespace = COMPAT_NAMESPACE;

        // Enum 목록 검색 변수
        [SerializeField] private List<E2StringEnumInfo> m_EnumList = new List<E2StringEnumInfo>();
        private string m_SearchFilter = string.Empty;
        private Vector2 m_ScrollPosition = Vector2.zero;
        private bool m_ShowGuide = false;

        // GUI 스타일 캐시
        private GUIStyle m_HeaderStyle;
        private GUIStyle m_ItemBoxStyle;
        private GUIStyle m_BoldLabelStyle;
        private GUIStyle m_BadgeStyle;

        [MenuItem("Tools/HB/E2String Manager", priority = 40)]
        public static void OpenWindow()
        {
            var window = GetWindow<E2StringManagerWindow>("E2String Manager");
            window.minSize = new Vector2(560f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            m_TargetFolderPath = EditorPrefs.GetString(PREFS_TARGET_FOLDER_KEY, DEFAULT_TARGET_FOLDER);

            // 전체 스캔은 처음 열 때만 수행하고, 이후에는 Scan / Refresh 버튼으로 갱신한다.
            if (!m_HasScanned)
            {
                RefreshAll();
            }
        }

        public void RefreshAll()
        {
            CheckAttributeInstallation();
            ScanTargetEnums();
            m_HasScanned = true;
            Repaint();
        }

        #region 파일 유틸리티

        /// <summary>
        /// Assets 하위의 .cs 파일 경로를 '/' 구분자로 열거합니다. Unity가 무시하는 폴더('.' 시작, '~' 끝)는 제외합니다.
        /// </summary>
        private static IEnumerable<string> EnumerateProjectScripts()
        {
            foreach (string file in Directory.EnumerateFiles("Assets", "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (IsIgnoredByUnity(path)) continue;
                yield return path;
            }
        }

        private static bool IsIgnoredByUnity(string path)
        {
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (segment.StartsWith(".", StringComparison.Ordinal) || segment.EndsWith("~", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadText(string path, out string text)
        {
            try
            {
                text = File.ReadAllText(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                text = null;
                return false;
            }
        }

        /// <summary>
        /// 절대 경로 또는 프로젝트 상대 경로를 "Assets" 또는 "Assets/..." 형식으로 변환합니다. Assets 밖이면 false.
        /// </summary>
        private static bool TryToAssetsRelativePath(string path, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim()).Replace('\\', '/').TrimEnd('/');
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }

            string assetsRoot = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');

            if (string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets";
                return true;
            }

            if (fullPath.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets" + fullPath.Substring(assetsRoot.Length);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 주석, 문자열/문자 리터럴, 전처리 지시문을 공백으로 치환합니다. 줄바꿈은 유지하므로 인덱스와 줄 번호가 원본과 같습니다.
        /// </summary>
        private static string StripNonCode(string src)
        {
            var sb = new StringBuilder(src.Length);
            int n = src.Length;
            int i = 0;
            bool atLineStart = true;

            while (i < n)
            {
                char c = src[i];
                char next = i + 1 < n ? src[i + 1] : '\0';

                // 전처리 지시문 (#if, #region 등)
                if (atLineStart && c == '#')
                {
                    while (i < n && src[i] != '\n') AppendBlank(sb, src[i++]);
                    continue;
                }

                // 한 줄 주석
                if (c == '/' && next == '/')
                {
                    while (i < n && src[i] != '\n') AppendBlank(sb, src[i++]);
                    continue;
                }

                // 블록 주석
                if (c == '/' && next == '*')
                {
                    int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? n : end + 2;
                    while (i < end) AppendBlank(sb, src[i++]);
                    atLineStart = false;
                    continue;
                }

                // 축자 문자열 (@"", $@"", @$"")
                bool isVerbatim = (c == '@' && next == '"')
                                  || (((c == '$' && next == '@') || (c == '@' && next == '$')) && i + 2 < n && src[i + 2] == '"');
                if (isVerbatim)
                {
                    while (src[i] != '"') AppendBlank(sb, src[i++]);
                    AppendBlank(sb, src[i++]); // 여는 따옴표
                    while (i < n)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < n && src[i + 1] == '"')
                            {
                                AppendBlank(sb, src[i++]);
                                AppendBlank(sb, src[i++]);
                                continue;
                            }
                            AppendBlank(sb, src[i++]);
                            break;
                        }
                        AppendBlank(sb, src[i++]);
                    }
                    atLineStart = false;
                    continue;
                }

                // 일반 문자열 / 문자 리터럴
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    AppendBlank(sb, src[i++]);
                    while (i < n && src[i] != '\n')
                    {
                        char s = src[i];
                        if (s == '\\' && i + 1 < n)
                        {
                            AppendBlank(sb, src[i++]);
                            AppendBlank(sb, src[i++]);
                            continue;
                        }
                        AppendBlank(sb, src[i++]);
                        if (s == quote) break;
                    }
                    atLineStart = false;
                    continue;
                }

                sb.Append(c);
                if (c == '\n') atLineStart = true;
                else if (!char.IsWhiteSpace(c)) atLineStart = false;
                i++;
            }

            return sb.ToString();
        }

        private static void AppendBlank(StringBuilder sb, char original)
        {
            sb.Append(original == '\n' || original == '\r' ? original : ' ');
        }

        #endregion

        #region 어트리뷰트 스크립트 검사 및 생성 로직

        /// <summary>
        /// 프로젝트 내에 E2StringAttribute 정의가 있는지 검사합니다.
        /// </summary>
        private void CheckAttributeInstallation()
        {
            m_IsAttributeInstalled = false;
            m_InstalledAttributePath = string.Empty;

            // 1. 파일명으로 후보를 찾고 실제 클래스 선언이 있는지 확인 (패키지 포함)
            foreach (string guid in AssetDatabase.FindAssets("E2StringAttribute t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (monoScript != null && DeclaresAttributeClass(monoScript.text))
                {
                    m_InstalledAttributePath = path;
                    m_IsAttributeInstalled = true;
                    return;
                }
            }

            // 2. 파일명이 다른 경우 대비: Assets 하위 전체 .cs 검색 (에디터 전용 제외)
            foreach (string path in EnumerateProjectScripts())
            {
                if (path.Contains("/Editor/")) continue;
                if (!TryReadText(path, out string text)) continue;

                if (DeclaresAttributeClass(text))
                {
                    m_InstalledAttributePath = path;
                    m_IsAttributeInstalled = true;
                    return;
                }
            }
        }

        private static bool DeclaresAttributeClass(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("E2StringAttribute", StringComparison.Ordinal) < 0) return false;
            return s_AttributeClassRegex.IsMatch(StripNonCode(text));
        }

        /// <summary>
        /// 지정한 경로에 E2StringAttribute 스크립트를 생성합니다.
        /// </summary>
        private void CreateAttributeScript()
        {
            string requestedFolder = string.IsNullOrWhiteSpace(m_TargetFolderPath) ? DEFAULT_TARGET_FOLDER : m_TargetFolderPath;
            if (!TryToAssetsRelativePath(requestedFolder, out string folderPath))
            {
                EditorUtility.DisplayDialog("E2String Manager",
                    $"생성 폴더는 프로젝트의 Assets 폴더 안이어야 합니다.\n\n입력값: {requestedFolder}", "확인");
                return;
            }

            string ns = m_UseGlobalNamespace ? COMPAT_NAMESPACE : (m_CustomNamespace ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(ns) && !s_NamespaceNameRegex.IsMatch(ns))
            {
                EditorUtility.DisplayDialog("E2String Manager", $"올바르지 않은 네임스페이스 이름입니다: {ns}", "확인");
                return;
            }

            string filePath = $"{folderPath}/{ATTRIBUTE_FILE_NAME}";
            if (File.Exists(filePath) &&
                !EditorUtility.DisplayDialog("E2String Manager", $"이미 파일이 존재합니다. 덮어쓸까요?\n\n경로: {filePath}", "덮어쓰기", "취소"))
            {
                return;
            }

            // 글로벌 옵션: 글로벌 + EString.Script 둘 다 생성 / 커스텀: 지정 네임스페이스만 (비우면 글로벌만)
            bool includeGlobal = m_UseGlobalNamespace || string.IsNullOrEmpty(ns);
            string source = BuildAttributeSource(includeGlobal, ns);

            try
            {
                Directory.CreateDirectory(folderPath);
                File.WriteAllText(filePath, source, new UTF8Encoding(true));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("E2String Manager", $"스크립트 생성에 실패했습니다.\n\n{e.Message}", "확인");
                return;
            }

            AssetDatabase.Refresh();

            m_TargetFolderPath = folderPath;
            EditorPrefs.SetString(PREFS_TARGET_FOLDER_KEY, folderPath);
            EditorUtility.DisplayDialog("E2String Manager", $"E2StringAttribute 스크립트가 성공적으로 생성되었습니다!\n\n경로: {filePath}", "확인");

            RefreshAll();
        }

        private static string BuildAttributeSource(bool includeGlobal, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by HB E2String Manager />");
            sb.AppendLine("using System;");

            if (includeGlobal)
            {
                sb.AppendLine();
                sb.AppendLine("[AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]");
                sb.AppendLine("public sealed class E2StringAttribute : Attribute");
                sb.AppendLine("{");
                sb.AppendLine("}");
            }

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine();
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                sb.AppendLine("    [AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]");
                sb.AppendLine("    public sealed class E2StringAttribute : Attribute");
                sb.AppendLine("    {");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        #endregion

        #region Enum 검색 로직

        private struct Scope
        {
            public string Namespace;
            public string TypeName;
        }

        /// <summary>
        /// 프로젝트 내 모든 C# 파일에서 [E2String]이 적용된 Enum을 검색합니다.
        /// </summary>
        private void ScanTargetEnums()
        {
            m_EnumList.Clear();

            foreach (string path in EnumerateProjectScripts())
            {
                if (path.EndsWith(ATTRIBUTE_FILE_NAME, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryReadText(path, out string text)) continue;

                // 빠른 문자열 사전 필터
                if (text.IndexOf("E2String", StringComparison.Ordinal) < 0) continue;

                ScanFile(path, StripNonCode(text));
            }
        }

        private void ScanFile(string path, string code)
        {
            var scopes = new List<Scope>();
            string pendingNamespace = null;
            string pendingType = null;

            int lineNumber = 1;
            int lineCountedUntil = 0;

            for (Match m = s_ScopeTokenRegex.Match(code); m.Success; m = m.NextMatch())
            {
                string token = m.Value;
                if (token == "{")
                {
                    scopes.Add(new Scope { Namespace = pendingNamespace, TypeName = pendingType });
                    pendingNamespace = null;
                    pendingType = null;
                    continue;
                }

                if (token == "}")
                {
                    if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
                    continue;
                }

                if (m.Groups["ns"].Success)
                {
                    pendingNamespace = m.Groups["ns"].Value;
                    continue;
                }

                if (m.Groups["type"].Success)
                {
                    pendingType = m.Groups["type"].Value;
                    continue;
                }

                // enum 선언: 직전 선언 경계(; { }) 이후의 어트리뷰트 영역에 E2String이 있는지 확인
                int prefixStart = code.LastIndexOfAny(new[] { ';', '{', '}' }, Math.Max(0, m.Index - 1)) + 1;
                Match attrMatch = s_E2StringAttrRegex.Match(code, prefixStart, m.Index - prefixStart);
                if (!attrMatch.Success) continue;

                lineNumber += CountNewLines(code, lineCountedUntil, m.Index);
                lineCountedUntil = m.Index;

                m_EnumList.Add(new E2StringEnumInfo
                {
                    EnumName = m.Groups["enum"].Value,
                    Namespace = JoinScopes(scopes, true),
                    ContainingTypes = JoinScopes(scopes, false),
                    AssetPath = path,
                    LineNumber = lineNumber,
                    AttributeName = attrMatch.Value,
                    Members = ExtractEnumMembers(code, m.Index + m.Length)
                });
            }
        }

        private static int CountNewLines(string text, int start, int end)
        {
            int count = 0;
            for (int i = start; i < end; i++)
            {
                if (text[i] == '\n') count++;
            }
            return count;
        }

        private static string JoinScopes(List<Scope> scopes, bool namespaces)
        {
            var sb = new StringBuilder();
            foreach (var scope in scopes)
            {
                string part = namespaces ? scope.Namespace : scope.TypeName;
                if (string.IsNullOrEmpty(part)) continue;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(part);
            }
            return sb.ToString();
        }

        private static List<string> ExtractEnumMembers(string code, int searchStart)
        {
            var members = new List<string>();

            int open = code.IndexOf('{', searchStart);
            if (open < 0) return members;
            int close = code.IndexOf('}', open + 1);
            if (close < 0) return members;

            string body = code.Substring(open + 1, close - open - 1);
            foreach (string raw in body.Split(','))
            {
                // 멤버 어트리뷰트 [Tag] 및 = 값 대입 부분 제거
                string t = s_AttributeSectionRegex.Replace(raw, string.Empty);
                int eqIdx = t.IndexOf('=');
                if (eqIdx >= 0) t = t.Substring(0, eqIdx);
                t = t.Trim();
                if (t.StartsWith("@", StringComparison.Ordinal)) t = t.Substring(1);

                if (s_IdentifierRegex.IsMatch(t))
                {
                    members.Add(t);
                }
            }

            return members;
        }

        #endregion

        #region GUI 렌더링

        private void InitStyles()
        {
            if (m_HeaderStyle == null)
            {
                m_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (m_ItemBoxStyle == null)
            {
                m_ItemBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 8, 8),
                    margin = new RectOffset(4, 4, 4, 4)
                };
            }

            if (m_BoldLabelStyle == null)
            {
                m_BoldLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
                };
            }

            if (m_BadgeStyle == null)
            {
                m_BadgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.2f, 0.7f, 1f) },
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();

            DrawHeader();

            EditorGUILayout.Space(6);
            DrawAttributeStatusSection();

            EditorGUILayout.Space(8);
            DrawEnumListSection();

            EditorGUILayout.Space(4);
            DrawGuideSection();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("HB E2String Manager", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Scan / Refresh", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    RefreshAll();
                    // 데이터가 바뀌었으므로 현재 이벤트의 남은 레이아웃을 중단한다
                    GUIUtility.ExitGUI();
                }

                m_ShowGuide = GUILayout.Toggle(m_ShowGuide, "Guide & Cheatsheet", EditorStyles.toolbarButton, GUILayout.Width(140));
            }
        }

        private void DrawAttributeStatusSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1. 어트리뷰트 스크립트 상태 (Attribute Script Status)", m_BoldLabelStyle);
                EditorGUILayout.Space(2);

                if (m_IsAttributeInstalled)
                {
                    EditorGUILayout.HelpBox(
                        $"E2StringAttribute 스크립트가 프로젝트에 존재합니다.\n위치: {m_InstalledAttributePath}",
                        MessageType.Info);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("스크립트 파일 열기", GUILayout.Width(130)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(m_InstalledAttributePath);
                            if (asset != null) AssetDatabase.OpenAsset(asset);
                        }
                        if (GUILayout.Button("프로젝트 위치 표시", GUILayout.Width(130)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(m_InstalledAttributePath);
                            if (asset != null) EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "프로젝트 내에 E2StringAttribute 스크립트가 없습니다.\n" +
                        "Source Generator가 인식할 수 있도록 어트리뷰트 스크립트를 생성해주세요.\n" +
                        "(서버와 공유하는 Enum 디렉토리 등 Assets 하위의 원하는 위치를 지정하여 생성할 수 있습니다.)",
                        MessageType.Warning);

                    EditorGUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("생성 폴더 경로", GUILayout.Width(90));
                        m_TargetFolderPath = EditorGUILayout.TextField(m_TargetFolderPath);

                        if (GUILayout.Button("폴더 선택...", GUILayout.Width(80)))
                        {
                            SelectTargetFolder();
                            // 모달 패널 이후 레이아웃 불일치를 막는다
                            GUIUtility.ExitGUI();
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        m_UseGlobalNamespace = EditorGUILayout.ToggleLeft(
                            "글로벌 + EString.Script 호환 (using 없이 즉시 사용 및 서버/이전 코드 호환)",
                            m_UseGlobalNamespace);
                    }

                    if (!m_UseGlobalNamespace)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("커스텀 네임스페이스", GUILayout.Width(120));
                            m_CustomNamespace = EditorGUILayout.TextField(m_CustomNamespace);
                        }
                        EditorGUILayout.LabelField("비워두면 글로벌 네임스페이스에만 생성됩니다.", EditorStyles.miniLabel);
                    }

                    EditorGUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        Color prevBackground = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                        bool create = GUILayout.Button("E2StringAttribute 스크립트 생성", GUILayout.Height(28), GUILayout.Width(230));
                        GUI.backgroundColor = prevBackground;

                        if (create)
                        {
                            CreateAttributeScript();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void SelectTargetFolder()
        {
            string selected = EditorUtility.OpenFolderPanel("E2StringAttribute 저장 폴더 선택", Application.dataPath, "");
            if (string.IsNullOrEmpty(selected)) return;

            if (TryToAssetsRelativePath(selected, out string assetPath))
            {
                m_TargetFolderPath = assetPath;
                EditorPrefs.SetString(PREFS_TARGET_FOLDER_KEY, m_TargetFolderPath);
            }
            else
            {
                EditorUtility.DisplayDialog("E2String Manager",
                    $"프로젝트의 Assets 폴더 안의 경로만 선택할 수 있습니다.\n\n선택한 경로: {selected}", "확인");
            }
        }

        private void DrawEnumListSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"2. E2String 적용 Enum 목록 ({m_EnumList.Count}개 감지됨)", m_BoldLabelStyle);
                    GUILayout.FlexibleSpace();

                    // 검색창
                    EditorGUILayout.LabelField("검색:", GUILayout.Width(35));
                    m_SearchFilter = EditorGUILayout.TextField(m_SearchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(180));
                }

                EditorGUILayout.Space(4);

                if (m_EnumList.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "현재 프로젝트에서 [E2String]이 적용된 Enum이 없습니다.\n" +
                        "Enum 선언 위에 [E2String] 어트리뷰트를 붙이면 Source Generator가 확장 메서드를 자동 생성합니다.",
                        MessageType.Info);
                    return;
                }

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition, GUILayout.MinHeight(200));

                int displayedCount = 0;
                foreach (var item in m_EnumList)
                {
                    if (!string.IsNullOrEmpty(m_SearchFilter))
                    {
                        bool matchName = item.TypeName.IndexOf(m_SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchNs = !string.IsNullOrEmpty(item.Namespace) && item.Namespace.IndexOf(m_SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchPath = item.AssetPath.IndexOf(m_SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!matchName && !matchNs && !matchPath) continue;
                    }

                    displayedCount++;
                    DrawEnumItem(item);
                }

                if (displayedCount == 0 && !string.IsNullOrEmpty(m_SearchFilter))
                {
                    EditorGUILayout.HelpBox($"'{m_SearchFilter}' 검색 조건과 일치하는 Enum이 없습니다.", MessageType.None);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawEnumItem(E2StringEnumInfo item)
        {
            using (new EditorGUILayout.VerticalScope(m_ItemBoxStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("-", GUILayout.Width(16));
                    GUILayout.Label(item.TypeName, m_BoldLabelStyle);

                    if (!string.IsNullOrEmpty(item.Namespace))
                    {
                        GUILayout.Label($"({item.Namespace})", m_BadgeStyle);
                    }
                    else
                    {
                        GUILayout.Label("(Global)", EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("스크립트 열기", EditorStyles.miniButton, GUILayout.Width(85)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(item.AssetPath);
                        if (asset != null)
                        {
                            AssetDatabase.OpenAsset(asset, item.LineNumber);
                        }
                    }

                    if (GUILayout.Button("위치 표시", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(item.AssetPath);
                        if (asset != null)
                        {
                            EditorGUIUtility.PingObject(asset);
                        }
                    }

                    if (GUILayout.Button("호출 복사", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        string typeName = item.TypeName;
                        string extClass = item.ExtensionClassName;
                        string sampleValue = item.Members.Count > 0 ? $"{typeName}.{item.Members[0]}" : $"default({typeName})";
                        string copyText =
                            $"// [E2String] {item.FullTypeName} 사용 예시\n" +
                            $"string str = {sampleValue}.ToStringFast();\n" +
                            $"int num = {sampleValue}.ToIntFast();\n" +
                            $"{typeName} parsed = str.To{item.EnumName}();\n" +
                            $"bool ok = {extClass}.TryParseFast(str, out var result);\n" +
                            $"var allValues = {extClass}.AllValues;";
                        EditorGUIUtility.systemCopyBuffer = copyText;
                        ShowNotification(new GUIContent($"'{item.TypeName}' 호출 코드 복사됨"));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"파일: {item.AssetPath}:{item.LineNumber}", EditorStyles.miniLabel);
                }

                if (item.Members.Count > 0)
                {
                    string membersPreview = string.Join(", ", item.Members);
                    if (membersPreview.Length > 80)
                    {
                        membersPreview = membersPreview.Substring(0, 77) + "...";
                    }
                    EditorGUILayout.LabelField($"멤버 ({item.Members.Count}개): {membersPreview}", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawGuideSection()
        {
            if (!m_ShowGuide) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("E2String 자동 생성 확장 메서드 요약 ({EnumName}Extensions)", m_BoldLabelStyle);
                EditorGUILayout.Space(2);

                EditorGUILayout.HelpBox(
                    "- myEnum.ToStringFast(fallback) : Zero-Allocation 문자열 변환 (switch 식)\n" +
                    "- str.To{EnumName}(fallback) / {EnumName}Extensions.TryParseFast(str, out res) : 문자열 -> Enum 고속 파싱\n" +
                    "- myEnum.ToInt() / myEnum.ToIntFast() : Enum -> int 변환 (Unity 2021.3+ UnsafeUtility.As 지원)\n" +
                    "- intVal.To{EnumName}(fallback) / intVal.To{EnumName}Fast() : int -> Enum 변환\n" +
                    "- {EnumName}Extensions.IsDefined(val) : Enum 또는 int 값 유효성 검사\n" +
                    "- {EnumName}Extensions.Count / Names / AllNames / Values / AllValues : 캐싱된 배열 및 읽기 전용 리스트\n" +
                    "- 중첩 enum은 바깥 타입 경로의 '.'이 '_'로 치환됩니다 (예: Outer_MyEnumExtensions)",
                    MessageType.None);
            }
        }

        #endregion
    }
}
