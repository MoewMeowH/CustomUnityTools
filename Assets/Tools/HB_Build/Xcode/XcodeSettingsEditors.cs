using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary> 공용 Xcode 설정 인스펙터 (Xcode 탭 모양) </summary>
    [CustomEditor(typeof(HBXcodeSettings))]
    public class HBXcodeSettingsEditor : UnityEditor.Editor
    {
        private static readonly string[] k_Tabs =
            { "Signing & Capabilities", "Info", "Build Settings", "Build Phases", "Files", "묶음 · 결정", "결과 미리보기" };

        private int m_Tab;
        private EBuildType m_PreviewBuildType = EBuildType.DEV;
        private XcodePlan m_Preview;
        private Vector2 m_PreviewScroll;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("iOS 빌드 뒤 HB_Build가 Xcode 프로젝트에 적용합니다. 버전 · 서명은 HB_Build 프로필, PlayerSettings에 칸이 있는 값은 PlayerSettings에서 관리합니다.\n" +
                                    "충돌(묶음끼리 · Xcode 프로젝트의 기존 값과)은 항상 빌드 실패이고, '묶음 · 결정' 탭의 결정 행으로만 해결합니다.", MessageType.None);

            m_Tab = GUILayout.Toolbar(m_Tab, k_Tabs.Take(4).ToArray());
            int second = GUILayout.Toolbar(m_Tab >= 4 ? m_Tab - 4 : -1, k_Tabs.Skip(4).ToArray());
            if (second >= 0) m_Tab = second + 4;
            EditorGUILayout.Space(4);

            var data = serializedObject.FindProperty(nameof(HBXcodeSettings.Data));
            if (m_Tab <= 4)
            {
                XcodeSettingsGUI.DrawDataTab(m_Tab, data, target);
            }
            else if (m_Tab == 5)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HBXcodeSettings.Fragments)), true);
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("결정 행: 묶음끼리 값이 다르면 ChosenSource의 값을 쓰고, OverwriteExisting이면 Unity · 다른 SDK 후처리기가 이미 넣은 다른 값을 덮어씁니다. 메모는 필수입니다.", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HBXcodeSettings.Decisions)), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HBXcodeSettings.FillLocalizationsFromLproj)));
            }
            else
            {
                DrawPreview();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginHorizontal();
            m_PreviewBuildType = (EBuildType)EditorGUILayout.EnumPopup("빌드 타입", m_PreviewBuildType);
            if (GUILayout.Button("계획 보기", GUILayout.Width(80)))
            {
                serializedObject.ApplyModifiedProperties();
                m_Preview = XcodeSettingsPlanner.Plan((HBXcodeSettings)target, new XcodePlanInput
                {
                    BuildType = m_PreviewBuildType,
                    ProjectRoot = System.IO.Path.GetDirectoryName(Application.dataPath),
                });
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("빌드 전 점검과 같은 계산입니다 (구성 · 타깃은 Unity 템플릿 기본값). Xcode 프로젝트의 기존 값과의 충돌은 빌드 뒤에만 알 수 있습니다.", EditorStyles.wordWrappedMiniLabel);

            if (m_Preview == null) return;
            foreach (var e in m_Preview.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            foreach (var w in m_Preview.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);

            var lines = m_Preview.Describe();
            if (lines.Count == 0)
            {
                EditorGUILayout.LabelField("적용할 항목 없음");
                return;
            }
            m_PreviewScroll = EditorGUILayout.BeginScrollView(m_PreviewScroll, GUILayout.MaxHeight(400));
            foreach (var line in lines)
                EditorGUILayout.SelectableLabel(line, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary> SDK 묶음 인스펙터 </summary>
    [CustomEditor(typeof(HBXcodeFragment))]
    public class HBXcodeFragmentEditor : UnityEditor.Editor
    {
        private static readonly string[] k_Tabs = { "Signing & Capabilities", "Info", "Build Settings", "Build Phases", "Files" };
        private int m_Tab;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("SDK 하나의 Xcode 설정 묶음입니다. 공용 XcodeSettings 에셋의 '묶음 · 결정' 탭 Fragments 목록에 넣어야 적용됩니다. 결과는 공용 에셋의 '결과 미리보기'에서 확인합니다.", MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HBXcodeFragment.Note)));
            m_Tab = GUILayout.Toolbar(m_Tab, k_Tabs);
            EditorGUILayout.Space(4);
            XcodeSettingsGUI.DrawDataTab(m_Tab, serializedObject.FindProperty(nameof(HBXcodeFragment.Data)), target);
            serializedObject.ApplyModifiedProperties();
        }
    }

    internal static class XcodeSettingsGUI
    {
        public static void DrawDataTab(int tab, SerializedProperty data, UnityEngine.Object asset)
        {
            switch (tab)
            {
                case 0:
                    DrawOwnedValues();
                    EditorGUILayout.Space(4);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.Capabilities)), true);
                    EditorGUILayout.Space(4);
                    DrawPasteButton(asset, "Entitlements 붙여넣기", d => d.Entitlements);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.Entitlements)), true);
                    break;
                case 1:
                    DrawPasteButton(asset, "Info.plist 조각 붙여넣기", d => d.Info);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.Info)), true);
                    break;
                case 2:
                    EditorGUILayout.HelpBox("링크 옵션 · 프레임워크 설정은 보통 UnityFramework, 앱 메타데이터는 App 타깃입니다. All은 App + UnityFramework + GameAssembly에 각각 씁니다.\n" +
                                            "OTHER_LDFLAGS 같은 목록 설정은 Add를 쓰세요 (Set으로 $(inherited)를 빼면 CocoaPods 값이 가려집니다).", MessageType.Info);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.BuildSettings)), true);
                    break;
                case 3:
                    EditorGUILayout.HelpBox("시스템 프레임워크 · 라이브러리 링크만 다룹니다. Assets/Plugins/iOS의 프레임워크 파일과 임베드는 Unity Plugin Inspector(Add to Embedded Binaries)가 관리합니다.", MessageType.Info);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.Frameworks)), true);
                    break;
                case 4:
                    EditorGUILayout.HelpBox("원본은 Unity 프로젝트 기준 상대 경로, 대상은 Xcode 프로젝트 폴더 기준 상대 경로입니다. 폴더 복사 때 .meta는 빠집니다.\n" +
                                            "*.lproj 폴더 참조로 앱 이름(InfoPlist.strings)을 지역화하는 것은 Mac에서 아직 확인하지 않은 실험 기능입니다. 홈 화면 아이콘의 언어별 지역화는 iOS가 지원하지 않습니다.", MessageType.Info);
                    EditorGUILayout.PropertyField(data.FindPropertyRelative(nameof(XcodeSettingsData.Files)), true);
                    break;
            }
        }

        /// <summary> 다른 곳이 주인인 값 (읽기 전용 표시) </summary>
        private static void DrawOwnedValues()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("다른 곳에서 관리하는 값 (읽기 전용)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("버전 · 빌드 번호 · 서명", "HB_Build 빌드 프로필 (Version · VersionCode · ProvisionUUID · ProvisionType)");
                EditorGUILayout.TextField("Bundle Identifier", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS));
                EditorGUILayout.TextField("Team ID", PlayerSettings.iOS.appleDeveloperTeamID);
                EditorGUILayout.Toggle("Automatic Signing (프로젝트 값)", PlayerSettings.iOS.appleEnableAutomaticSigning);
                EditorGUILayout.TextField("Minimum iOS", PlayerSettings.iOS.targetOSVersionString);
                EditorGUILayout.TextField("Camera Usage", PlayerSettings.iOS.cameraUsageDescription);
                EditorGUILayout.TextField("Location Usage", PlayerSettings.iOS.locationUsageDescription);
                EditorGUILayout.TextField("Microphone Usage", PlayerSettings.iOS.microphoneUsageDescription);
                EditorGUILayout.TextField("URL Schemes", string.Join(", ", PlayerSettings.iOS.iOSUrlSchemes ?? Array.Empty<string>()));
            }
            if (GUILayout.Button("Player Settings 열기"))
                SettingsService.OpenProjectSettings("Project/Player");
            EditorGUILayout.EndVertical();
        }

        private static void DrawPasteButton(UnityEngine.Object asset, string label, Func<XcodeSettingsData, List<XcodePlistEntry>> list)
        {
            if (GUILayout.Button(label))
                XcodePlistPasteWindow.Open(asset, label, list);
        }

        public static XcodeSettingsData GetData(UnityEngine.Object asset) => asset switch
        {
            HBXcodeSettings s => s.Data,
            HBXcodeFragment f => f.Data,
            _ => null,
        };
    }

    /// <summary> SDK 문서의 plist 조각을 붙여넣어 행으로 추가하는 창 </summary>
    internal class XcodePlistPasteWindow : EditorWindow
    {
        private UnityEngine.Object m_Asset;
        private Func<XcodeSettingsData, List<XcodePlistEntry>> m_List;
        private string m_Text = string.Empty;
        private string m_Error;
        private Vector2 m_Scroll;

        public static void Open(UnityEngine.Object asset, string title, Func<XcodeSettingsData, List<XcodePlistEntry>> list)
        {
            var w = CreateInstance<XcodePlistPasteWindow>();
            w.titleContent = new GUIContent(title);
            w.m_Asset = asset;
            w.m_List = list;
            w.minSize = new Vector2(480, 320);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("<key>…</key><string>…</string> 나열, <dict> 하나, 또는 전체 plist를 붙여넣으세요.", EditorStyles.wordWrappedMiniLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            m_Text = EditorGUILayout.TextArea(m_Text, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(m_Error))
                EditorGUILayout.HelpBox(m_Error, MessageType.Error);

            if (!GUILayout.Button("행으로 추가")) return;

            var data = XcodeSettingsGUI.GetData(m_Asset);
            if (data == null)
            {
                m_Error = "대상 에셋이 없습니다 (창을 다시 여세요).";
                return;
            }
            try
            {
                var entries = XcodePlistPaste.ToEntries(m_Text);
                Undo.RecordObject(m_Asset, "Paste plist entries");
                m_List(data).AddRange(entries);
                EditorUtility.SetDirty(m_Asset);
                Debug.Log($"[XcodeSettings] Added {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} to '{m_Asset.name}'.");
                Close();
            }
            catch (FormatException ex)
            {
                m_Error = ex.Message;
            }
        }
    }

    // ───────────── 행 Drawer (타입에 맞는 칸만 보인다) ─────────────

    internal abstract class XcodeRowDrawer : PropertyDrawer
    {
        protected abstract IEnumerable<string> VisibleFields(SerializedProperty property);

        protected virtual bool DrawSpecial(Rect rect, SerializedProperty property, SerializedProperty field) => false;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = 0;
            foreach (var name in VisibleFields(property))
                h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), true) + EditorGUIUtility.standardVerticalSpacing;
            return h + 4;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            XcodeRowDefaults.EnsureInitialized(property);
            EditorGUI.BeginProperty(position, label, property);
            var rect = new Rect(position.x, position.y + 2, position.width, 0);
            foreach (var name in VisibleFields(property))
            {
                var field = property.FindPropertyRelative(name);
                rect.height = EditorGUI.GetPropertyHeight(field, true);
                if (!DrawSpecial(rect, property, field))
                    EditorGUI.PropertyField(rect, field, true);
                rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.EndProperty();
        }

        /// <summary> 키 칸 오른쪽에 자주 쓰는 키 메뉴 버튼 </summary>
        protected static void DrawKeyWithPresets(Rect rect, SerializedProperty property, SerializedProperty keyField,
            IReadOnlyList<(string Key, Action<SerializedProperty> Apply)> presets)
        {
            var fieldRect = new Rect(rect.x, rect.y, rect.width - 24, rect.height);
            var buttonRect = new Rect(rect.xMax - 22, rect.y, 22, rect.height);
            EditorGUI.PropertyField(fieldRect, keyField);
            if (!GUI.Button(buttonRect, "▾", EditorStyles.miniButton)) return;

            var so = property.serializedObject;
            string path = property.propertyPath;
            var menu = new GenericMenu();
            foreach (var (key, apply) in presets)
            {
                menu.AddItem(new GUIContent(key), false, () =>
                {
                    so.Update();
                    apply(so.FindProperty(path));
                    so.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }

    [CustomPropertyDrawer(typeof(XcodePlistEntry))]
    internal class XcodePlistEntryDrawer : XcodeRowDrawer
    {
        private static (string, Action<SerializedProperty>) Preset(string key, EPlistValueType type, string value = "") =>
            (key, p =>
            {
                p.FindPropertyRelative(nameof(XcodePlistEntry.Key)).stringValue = key;
                p.FindPropertyRelative(nameof(XcodePlistEntry.Type)).enumValueIndex = (int)type;
                if (value.Length > 0) p.FindPropertyRelative(nameof(XcodePlistEntry.Value)).stringValue = value;
            });

        private static readonly (string, Action<SerializedProperty>)[] k_Presets =
        {
            Preset("ITSAppUsesNonExemptEncryption", EPlistValueType.Bool, "false"),
            Preset("NSUserTrackingUsageDescription", EPlistValueType.String),
            Preset("NSPhotoLibraryUsageDescription", EPlistValueType.String),
            Preset("NSPhotoLibraryAddUsageDescription", EPlistValueType.String),
            Preset("LSApplicationQueriesSchemes", EPlistValueType.StringArray),
            Preset("SKAdNetworkItems", EPlistValueType.Raw),
            Preset("GADApplicationIdentifier", EPlistValueType.String),
            Preset("FirebaseAppDelegateProxyEnabled", EPlistValueType.Bool, "false"),
            Preset("CFBundleDevelopmentRegion", EPlistValueType.String, "en"),
        };

        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodePlistEntry.Enabled);
            yield return nameof(XcodePlistEntry.Key);
            yield return nameof(XcodePlistEntry.Type);
            switch ((EPlistValueType)p.FindPropertyRelative(nameof(XcodePlistEntry.Type)).enumValueIndex)
            {
                case EPlistValueType.StringArray:
                    yield return nameof(XcodePlistEntry.Values);
                    break;
                case EPlistValueType.Raw:
                    yield return nameof(XcodePlistEntry.RawXml);
                    break;
                default:
                    yield return nameof(XcodePlistEntry.Value);
                    break;
            }
            yield return nameof(XcodePlistEntry.BuildTypes);
        }

        protected override bool DrawSpecial(Rect rect, SerializedProperty property, SerializedProperty field)
        {
            if (field.name == nameof(XcodePlistEntry.Key))
            {
                DrawKeyWithPresets(rect, property, field, k_Presets);
                return true;
            }
            if (field.name == nameof(XcodePlistEntry.Value) &&
                (EPlistValueType)property.FindPropertyRelative(nameof(XcodePlistEntry.Type)).enumValueIndex == EPlistValueType.Bool)
            {
                // 그리기만 해서는 값을 고치지 않는다 (사용자가 바꿀 때만 씀). 형식이 틀린 값은 계획 단계 오류로 드러난다
                bool valid = bool.TryParse(field.stringValue, out bool current);
                EditorGUI.BeginChangeCheck();
                bool next = EditorGUI.Toggle(rect, valid ? "Value" : $"Value (invalid: '{field.stringValue}')", current);
                if (EditorGUI.EndChangeCheck())
                    field.stringValue = next ? "true" : "false";
                return true;
            }
            return false;
        }
    }

    [CustomPropertyDrawer(typeof(XcodeCapabilityEntry))]
    internal class XcodeCapabilityEntryDrawer : XcodeRowDrawer
    {
        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodeCapabilityEntry.Enabled);
            yield return nameof(XcodeCapabilityEntry.Capability);
            switch ((EXcodeCapability)p.FindPropertyRelative(nameof(XcodeCapabilityEntry.Capability)).enumValueIndex)
            {
                case EXcodeCapability.PushNotifications:
                    yield return nameof(XcodeCapabilityEntry.ApsEnvironment);
                    break;
                case EXcodeCapability.AssociatedDomains:
                case EXcodeCapability.BackgroundModes:
                case EXcodeCapability.KeychainSharing:
                case EXcodeCapability.AppGroups:
                    yield return nameof(XcodeCapabilityEntry.Values);
                    break;
            }
            yield return nameof(XcodeCapabilityEntry.BuildTypes);
        }

        private static readonly string[] k_Aps = { "development", "production" };

        protected override bool DrawSpecial(Rect rect, SerializedProperty property, SerializedProperty field)
        {
            if (field.name != nameof(XcodeCapabilityEntry.ApsEnvironment)) return false;
            // 목록 밖 값(병합 실수 등)은 그대로 보여 주고 바꾸지 않는다 — 계획 단계가 오류로 알린다
            int index = Array.IndexOf(k_Aps, field.stringValue);
            var options = index >= 0 ? k_Aps : k_Aps.Append($"(invalid) {field.stringValue}").ToArray();
            if (index < 0) index = options.Length - 1;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUI.Popup(rect, "aps-environment", index, options);
            if (EditorGUI.EndChangeCheck() && next < k_Aps.Length)
                field.stringValue = k_Aps[next];
            return true;
        }
    }

    [CustomPropertyDrawer(typeof(XcodeBuildSettingEntry))]
    internal class XcodeBuildSettingEntryDrawer : XcodeRowDrawer
    {
        private static (string, Action<SerializedProperty>) Preset(string key, EXcodeTarget target, EBuildSettingOp op, string value) =>
            (key, p =>
            {
                p.FindPropertyRelative(nameof(XcodeBuildSettingEntry.Key)).stringValue = key;
                p.FindPropertyRelative(nameof(XcodeBuildSettingEntry.Target)).enumValueIndex = (int)target;
                p.FindPropertyRelative(nameof(XcodeBuildSettingEntry.Op)).enumValueIndex = (int)op;
                p.FindPropertyRelative(nameof(XcodeBuildSettingEntry.Value)).stringValue = value;
            });

        private static readonly (string, Action<SerializedProperty>)[] k_Presets =
        {
            Preset("OTHER_LDFLAGS", EXcodeTarget.UnityFramework, EBuildSettingOp.Add, "-ObjC"),
            Preset("ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", EXcodeTarget.UnityFramework, EBuildSettingOp.Set, "NO"),
            Preset("SWIFT_VERSION", EXcodeTarget.UnityFramework, EBuildSettingOp.Set, "5.0"),
            Preset("CLANG_ENABLE_MODULES", EXcodeTarget.UnityFramework, EBuildSettingOp.Set, "YES"),
            Preset("GCC_ENABLE_OBJC_EXCEPTIONS", EXcodeTarget.UnityFramework, EBuildSettingOp.Set, "YES"),
            Preset("LD_RUNPATH_SEARCH_PATHS", EXcodeTarget.UnityFramework, EBuildSettingOp.Add, "@executable_path/Frameworks"),
            Preset("FRAMEWORK_SEARCH_PATHS", EXcodeTarget.UnityFramework, EBuildSettingOp.Add, "$(PROJECT_DIR)/Frameworks"),
        };

        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodeBuildSettingEntry.Enabled);
            yield return nameof(XcodeBuildSettingEntry.Key);
            yield return nameof(XcodeBuildSettingEntry.Target);
            yield return nameof(XcodeBuildSettingEntry.Config);
            yield return nameof(XcodeBuildSettingEntry.Op);
            yield return nameof(XcodeBuildSettingEntry.Value);
            yield return nameof(XcodeBuildSettingEntry.BuildTypes);
        }

        protected override bool DrawSpecial(Rect rect, SerializedProperty property, SerializedProperty field)
        {
            if (field.name != nameof(XcodeBuildSettingEntry.Key)) return false;
            DrawKeyWithPresets(rect, property, field, k_Presets);
            return true;
        }
    }

    [CustomPropertyDrawer(typeof(XcodeFrameworkEntry))]
    internal class XcodeFrameworkEntryDrawer : XcodeRowDrawer
    {
        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodeFrameworkEntry.Enabled);
            yield return nameof(XcodeFrameworkEntry.Name);
            yield return nameof(XcodeFrameworkEntry.Target);
            yield return nameof(XcodeFrameworkEntry.Optional);
            yield return nameof(XcodeFrameworkEntry.BuildTypes);
        }
    }

    [CustomPropertyDrawer(typeof(XcodeFileEntry))]
    internal class XcodeFileEntryDrawer : XcodeRowDrawer
    {
        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodeFileEntry.Enabled);
            yield return nameof(XcodeFileEntry.SourcePath);
            yield return nameof(XcodeFileEntry.DestinationPath);
            yield return nameof(XcodeFileEntry.Mode);
            yield return nameof(XcodeFileEntry.Target);
            yield return nameof(XcodeFileEntry.AddToResources);
            yield return nameof(XcodeFileEntry.BuildTypes);
        }
    }

    [CustomPropertyDrawer(typeof(XcodeFragmentRef))]
    internal class XcodeFragmentRefDrawer : XcodeRowDrawer
    {
        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            yield return nameof(XcodeFragmentRef.Enabled);
            yield return nameof(XcodeFragmentRef.Fragment);
        }
    }

    /// <summary>
    /// 인스펙터 + 로 빈 목록에 만든 새 행은 C# 필드 초기값 없이 0으로 채워질 수 있다 (Enabled = false · 빌드 타입 없음 → 조용히 빠짐).
    /// 숨은 표시(m_Initialized)가 false인 행에만 기본값을 한 번 넣는다. 이미 쓰던 행은 건드리지 않는다.
    /// </summary>
    public static class XcodeRowDefaults
    {
        public const string MARKER = "m_Initialized";

        public static void EnsureInitialized(SerializedProperty row)
        {
            var marker = row.FindPropertyRelative(MARKER);
            if (marker == null || marker.boolValue) return;

            void SetBool(string name, bool v) { var p = row.FindPropertyRelative(name); if (p != null) p.boolValue = v; }
            void SetInt(string name, int v) { var p = row.FindPropertyRelative(name); if (p != null) p.intValue = v; }
            void SetString(string name, string v) { var p = row.FindPropertyRelative(name); if (p != null && string.IsNullOrEmpty(p.stringValue)) p.stringValue = v; }

            SetBool("Enabled", true);
            SetInt("BuildTypes", (int)EXcodeBuildTypes.All);
            SetBool("AddToResources", true);
            SetString("ApsEnvironment", "development");
            switch (row.type)
            {
                case nameof(XcodeBuildSettingEntry):
                case nameof(XcodeFrameworkEntry):
                    SetInt("Target", (int)EXcodeTarget.UnityFramework);
                    break;
                case nameof(XcodeDecisionEntry):
                    SetInt("Target", (int)EXcodeTarget.All);
                    break;
            }
            marker.boolValue = true;
        }
    }

    [CustomPropertyDrawer(typeof(XcodeDecisionEntry))]
    internal class XcodeDecisionEntryDrawer : XcodeRowDrawer
    {
        protected override IEnumerable<string> VisibleFields(SerializedProperty p)
        {
            var kind = (EXcodeChangeKind)p.FindPropertyRelative(nameof(XcodeDecisionEntry.Kind)).enumValueIndex;
            yield return nameof(XcodeDecisionEntry.Kind);
            if (kind is EXcodeChangeKind.BuildSetting or EXcodeChangeKind.Framework)
                yield return nameof(XcodeDecisionEntry.Target);
            if (kind == EXcodeChangeKind.BuildSetting)
                yield return nameof(XcodeDecisionEntry.Config);
            yield return nameof(XcodeDecisionEntry.Key);
            yield return nameof(XcodeDecisionEntry.ChosenSource);
            yield return nameof(XcodeDecisionEntry.OverwriteExisting);
            yield return nameof(XcodeDecisionEntry.Memo);
        }
    }
}
