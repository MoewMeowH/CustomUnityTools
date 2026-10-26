using System.Collections.Generic;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 다른 곳이 주인인 키 (Xcode 설정 에셋에 넣으면 실패). 값마다 주인은 하나다.
    /// </summary>
    public static class XcodeOwnership
    {
        private const string OWNER_PROFILE = "HB_Build build profile (Version / VersionCode / ProvisionUUID / ProvisionType)";
        private const string OWNER_PLAYER = "PlayerSettings (iOS)";
        private const string OWNER_SELF = "HB_Build Xcode settings itself (managed automatically)";
        private const string OWNER_SIGNING = "code signing (set from the provisioning profile; HB_Build build profile chooses it)";
        private const string OWNER_INFO_TAB = "the Info tab (Info.plist generation settings would bypass owner checks)";

        /// <summary> Info.plist 키 → 주인 </summary>
        public static readonly IReadOnlyDictionary<string, string> InfoKeys = new Dictionary<string, string>
        {
            { "CFBundleShortVersionString", OWNER_PROFILE },
            { "CFBundleVersion", OWNER_PROFILE },
            { "CFBundleIdentifier", OWNER_PLAYER },
            { "CFBundleDisplayName", OWNER_PLAYER + " (localize it with *.lproj/InfoPlist.strings in Files)" },
            { "CFBundleName", OWNER_PLAYER },
            { "CFBundleURLTypes", OWNER_PLAYER + " (Supported URL schemes)" },
            { "MinimumOSVersion", OWNER_PLAYER + " (Target minimum iOS Version)" },
            { "NSCameraUsageDescription", OWNER_PLAYER + " (Camera Usage Description)" },
            { "NSLocationWhenInUseUsageDescription", OWNER_PLAYER + " (Location Usage Description)" },
            { "NSMicrophoneUsageDescription", OWNER_PLAYER + " (Microphone Usage Description)" },
            { "UIRequiresFullScreen", OWNER_PLAYER + " (Requires Fullscreen)" },
            { "UIStatusBarHidden", OWNER_PLAYER + " (Status Bar Hidden)" },
            { "UISupportedInterfaceOrientations", OWNER_PLAYER + " (Orientation)" },
            { "UISupportedInterfaceOrientations~ipad", OWNER_PLAYER + " (Orientation)" },
            { "UILaunchStoryboardName", OWNER_PLAYER + " (Launch Screen)" },
            { "UIRequiresPersistentWiFi", OWNER_PLAYER + " (Requires Persistent WiFi)" },
            { "UIApplicationExitsOnSuspend", OWNER_PLAYER + " (Behavior in Background)" },
            { "CADisableMinimumFrameDurationOnPhone", OWNER_PLAYER + " (ProMotion support)" },
        };

        /// <summary> entitlements 키 → 주인 (서명 때 프로파일에서 정해지는 값) </summary>
        public static readonly IReadOnlyDictionary<string, string> EntitlementKeys = new Dictionary<string, string>
        {
            { "application-identifier", OWNER_SIGNING },
            { "com.apple.developer.team-identifier", OWNER_SIGNING },
            { "get-task-allow", OWNER_SIGNING },
        };

        /// <summary> Build Settings 키 → 주인 ("KEY[sdk=...]" 같은 조건부 키도 앞부분으로 판정) </summary>
        public static readonly IReadOnlyDictionary<string, string> BuildSettingKeys = new Dictionary<string, string>
        {
            { "CODE_SIGN_STYLE", OWNER_PROFILE },
            { "CODE_SIGN_IDENTITY", OWNER_PROFILE },
            { "PROVISIONING_PROFILE", OWNER_PROFILE },
            { "PROVISIONING_PROFILE_SPECIFIER", OWNER_PROFILE },
            { "PROVISIONING_PROFILE_APP", OWNER_PROFILE },
            { "PROVISIONING_PROFILE_SPECIFIER_APP", OWNER_PROFILE },
            { "MARKETING_VERSION", OWNER_PROFILE },
            { "CURRENT_PROJECT_VERSION", OWNER_PROFILE },
            { "DEVELOPMENT_TEAM", OWNER_PLAYER + " (Signing Team ID)" },
            { "PRODUCT_BUNDLE_IDENTIFIER", OWNER_PLAYER + " (Bundle Identifier)" },
            { "IPHONEOS_DEPLOYMENT_TARGET", OWNER_PLAYER + " (Target minimum iOS Version)" },
            { "OTHER_CODE_SIGN_FLAGS", OWNER_PROFILE },
            { "INFOPLIST_FILE", OWNER_SELF },
            { "CODE_SIGN_ENTITLEMENTS", OWNER_SELF },
            { "GENERATE_INFOPLIST_FILE", OWNER_INFO_TAB },
        };

        /// <summary> 접두사로 막는 빌드 설정 (INFOPLIST_KEY_* · INFOPLIST_PREPROCESS* 등은 Info.plist 값을 우회해 바꾼다) </summary>
        private static readonly (string Prefix, string Owner)[] k_BuildSettingPrefixes =
        {
            ("INFOPLIST_", OWNER_INFO_TAB),
        };

        /// <summary> 목록형 빌드 설정. Set으로 쓰면서 $(inherited)를 빼면 CocoaPods xcconfig 값이 가려진다 </summary>
        public static readonly HashSet<string> ListBuildSettingKeys = new HashSet<string>
        {
            "OTHER_LDFLAGS",
            "OTHER_CFLAGS",
            "OTHER_CPLUSPLUSFLAGS",
            "OTHER_SWIFT_FLAGS",
            "FRAMEWORK_SEARCH_PATHS",
            "HEADER_SEARCH_PATHS",
            "LIBRARY_SEARCH_PATHS",
            "LD_RUNPATH_SEARCH_PATHS",
            "GCC_PREPROCESSOR_DEFINITIONS",
            "SWIFT_ACTIVE_COMPILATION_CONDITIONS",
        };

        /// <summary> "KEY[sdk=iphoneos*]" → "KEY" </summary>
        public static string BaseBuildSettingKey(string key)
        {
            int bracket = key.IndexOf('[');
            return bracket > 0 ? key.Substring(0, bracket) : key;
        }

        public static bool TryGetInfoOwner(string key, out string owner) => InfoKeys.TryGetValue(key, out owner);

        public static bool TryGetBuildSettingOwner(string key, out string owner)
        {
            string baseKey = BaseBuildSettingKey(key);
            if (BuildSettingKeys.TryGetValue(baseKey, out owner)) return true;
            foreach (var (prefix, prefixOwner) in k_BuildSettingPrefixes)
            {
                if (baseKey.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    owner = prefixOwner;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetEntitlementOwner(string key, out string owner) => EntitlementKeys.TryGetValue(key, out owner);
    }
}
