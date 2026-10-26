using System;
using System.Collections.Generic;
using UnityEngine;

// m_Initialized는 인스펙터(SerializedProperty)로만 읽는다
#pragma warning disable CS0414

namespace HB.CustomBuild.Editor
{
    /// <summary> 행을 적용할 빌드 타입 (여럿 선택 가능). 아무것도 고르지 않으면 그 행은 적용되지 않는다 </summary>
    [Flags]
    public enum EXcodeBuildTypes
    {
        DEV = 1 << 0,
        QA = 1 << 1,
        LIVE = 1 << 2,
        All = DEV | QA | LIVE,
    }

    /// <summary>
    /// 논리 타깃. 적용할 때 실제 GUID로 해석한다 (Unity 7 Swift 프로젝트는 앱 타깃 이름이 제품 이름이라 이름을 저장하지 않음).
    /// All = 앱 + UnityFramework + GameAssembly(있을 때만). Tests 타깃은 다루지 않는다.
    /// </summary>
    public enum EXcodeTarget
    {
        App = 0,
        UnityFramework = 1,
        GameAssembly = 2,
        All = 3,
    }

    public enum EPlistValueType
    {
        String,
        Bool,
        Integer,
        Real,
        /// <summary> 문자열 배열. 같은 키는 합집합으로 합친다 </summary>
        StringArray,
        /// <summary> plist XML 값 하나 (배열 안의 딕셔너리 같은 복합 값) </summary>
        Raw,
    }

    public enum EBuildSettingOp
    {
        /// <summary> 값을 정한다. 같은 칸에 다른 값이 오면 충돌 </summary>
        Set,
        /// <summary> 목록에 더한다 (OTHER_LDFLAGS 등). 같은 칸의 Add끼리는 합집합 </summary>
        Add,
    }

    public enum EXcodeFileMode
    {
        File,
        /// <summary> 폴더 참조 (폴더 구조를 그대로 번들에 복사) — *.lproj 지역화 폴더용 </summary>
        FolderReference,
    }

    public enum EXcodeCapability
    {
        PushNotifications,
        SignInWithApple,
        AssociatedDomains,
        BackgroundModes,
        InAppPurchase,
        GameCenter,
        KeychainSharing,
        AppGroups,
    }

    /// <summary> 충돌 · 결정 행이 가리키는 값의 종류 </summary>
    public enum EXcodeChangeKind
    {
        Info,
        Entitlement,
        BuildSetting,
        Framework,
        File,
    }

    /// <summary> Info.plist · entitlements 행 </summary>
    [Serializable]
    public class XcodePlistEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        public string Key = string.Empty;
        public EPlistValueType Type = EPlistValueType.String;
        [Tooltip("String · Bool(true/false) · Integer · Real 값")]
        public string Value = string.Empty;
        [Tooltip("StringArray 값")]
        public List<string> Values = new List<string>();
        [Tooltip("Raw 값: plist XML 값 하나 (예: <array><dict>...</dict></array>)")]
        [TextArea(2, 8)]
        public string RawXml = string.Empty;
        public EXcodeBuildTypes BuildTypes = EXcodeBuildTypes.All;
    }

    /// <summary> Signing &amp; Capabilities 행. 적용할 때 entitlements · Info.plist 값으로 바꿔 다른 행과 함께 충돌 검사한다 </summary>
    [Serializable]
    public class XcodeCapabilityEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        public EXcodeCapability Capability = EXcodeCapability.PushNotifications;
        [Tooltip("AssociatedDomains: applinks:example.com / BackgroundModes: remote-notification · audio · fetch · processing / KeychainSharing · AppGroups: 그룹 ID")]
        public List<string> Values = new List<string>();
        [Tooltip("PushNotifications 전용: development 또는 production (서명 프로파일 종류와 맞춰야 함)")]
        public string ApsEnvironment = "development";
        public EXcodeBuildTypes BuildTypes = EXcodeBuildTypes.All;
    }

    [Serializable]
    public class XcodeBuildSettingEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        public EXcodeTarget Target = EXcodeTarget.UnityFramework;
        [Tooltip("구성 이름 (Debug · Release · ReleaseForProfiling · ReleaseForRunning). 비우면 모든 구성")]
        public string Config = string.Empty;
        public string Key = string.Empty;
        public EBuildSettingOp Op = EBuildSettingOp.Set;
        public string Value = string.Empty;
        public EXcodeBuildTypes BuildTypes = EXcodeBuildTypes.All;
    }

    /// <summary> Build Phases: 시스템 프레임워크 · 라이브러리 링크 (Assets/Plugins/iOS의 프레임워크 파일은 PluginImporter가 주인) </summary>
    [Serializable]
    public class XcodeFrameworkEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        public EXcodeTarget Target = EXcodeTarget.UnityFramework;
        [Tooltip("예: AdSupport.framework, libz.tbd")]
        public string Name = string.Empty;
        [Tooltip("Optional(weak) 링크")]
        public bool Optional;
        public EXcodeBuildTypes BuildTypes = EXcodeBuildTypes.All;
    }

    [Serializable]
    public class XcodeFileEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        [Tooltip("프로젝트 기준 상대 경로 (예: iOSResources/Live/GoogleService-Info.plist, iOSResources/Localization/ko.lproj)")]
        public string SourcePath = string.Empty;
        [Tooltip("Xcode 프로젝트 폴더 기준 상대 경로 (예: GoogleService-Info.plist, ko.lproj). '..' · 절대 경로 불가")]
        public string DestinationPath = string.Empty;
        public EXcodeFileMode Mode = EXcodeFileMode.File;
        [Tooltip("App 또는 UnityFramework")]
        public EXcodeTarget Target = EXcodeTarget.App;
        [Tooltip("타깃의 Copy Bundle Resources에 넣는다 (끄면 복사 · 프로젝트 참조만)")]
        public bool AddToResources = true;
        public EXcodeBuildTypes BuildTypes = EXcodeBuildTypes.All;
    }

    /// <summary> 공용 에셋과 SDK 묶음이 같이 쓰는 Xcode 탭 구성 </summary>
    [Serializable]
    public class XcodeSettingsData
    {
        public List<XcodeCapabilityEntry> Capabilities = new List<XcodeCapabilityEntry>();
        public List<XcodePlistEntry> Info = new List<XcodePlistEntry>();
        public List<XcodePlistEntry> Entitlements = new List<XcodePlistEntry>();
        public List<XcodeBuildSettingEntry> BuildSettings = new List<XcodeBuildSettingEntry>();
        public List<XcodeFrameworkEntry> Frameworks = new List<XcodeFrameworkEntry>();
        public List<XcodeFileEntry> Files = new List<XcodeFileEntry>();
    }

    [Serializable]
    public class XcodeFragmentRef
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public bool Enabled = true;
        public HBXcodeFragment Fragment;
    }

    /// <summary>
    /// 충돌 해결 행. 묶음끼리 값이 다르면 ChosenSource의 값을 쓰고, OverwriteExisting이면 Xcode 프로젝트에 이미 있는 다른 값을 덮어쓴다.
    /// 메모는 필수 (왜 이 값인지).
    /// </summary>
    [Serializable]
    public class XcodeDecisionEntry
    {
        /// <summary> 인스펙터 + 로 만든 새 행은 필드 초기값 없이 0으로 채워질 수 있어, 그 경우를 알아보고 기본값을 넣는 표시 (XcodeRowDefaults) </summary>
        [SerializeField, HideInInspector] private bool m_Initialized = true;
        public EXcodeChangeKind Kind = EXcodeChangeKind.Info;
        [Tooltip("BuildSetting · Framework만. All이면 모든 타깃")]
        public EXcodeTarget Target = EXcodeTarget.All;
        [Tooltip("BuildSetting만. 비우면 모든 구성")]
        public string Config = string.Empty;
        [Tooltip("Info · Entitlement 키, 빌드 설정 이름, 프레임워크 이름, File 대상 경로")]
        public string Key = string.Empty;
        [Tooltip("묶음끼리 부딪힐 때 이길 에셋 (공용 HBXcodeSettings 또는 HBXcodeFragment). 기존 값 덮어쓰기만 할 때는 비워도 됨")]
        public UnityEngine.Object ChosenSource;
        [Tooltip("Xcode 프로젝트에 이미 다른 값이 있어도(Unity 기본값 · 다른 SDK 후처리기) 덮어쓴다")]
        public bool OverwriteExisting;
        [TextArea(1, 4)]
        public string Memo = string.Empty;
    }
}
