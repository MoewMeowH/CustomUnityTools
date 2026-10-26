using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// iOS 빌드 뒤 Xcode 프로젝트에 적용할 설정 (공용). HB_Build 사후 스텝이 빌드마다 적용합니다.
    /// 버전 · 서명은 HB_Build 프로필, PlayerSettings에 칸이 있는 값은 PlayerSettings가 주인이고, 이 에셋은 그 밖의 것만 씁니다.
    /// SDK별 값은 HBXcodeFragment(묶음)로 나눠 Fragments 목록에서 켜고 끕니다.
    /// </summary>
    [CreateAssetMenu(fileName = "XcodeSettings", menuName = "HB_Build/Xcode Settings", order = 60)]
    public class HBXcodeSettings : ScriptableObject
    {
        [SerializeField] public XcodeSettingsData Data = new XcodeSettingsData();

        [Tooltip("SDK별 묶음. 끄면 그 묶음의 행이 모두 빠진다")]
        [SerializeField] public List<XcodeFragmentRef> Fragments = new List<XcodeFragmentRef>();

        [Tooltip("충돌 해결 · 기존 값 덮어쓰기 (메모 필수)")]
        [SerializeField] public List<XcodeDecisionEntry> Decisions = new List<XcodeDecisionEntry>();

        [Tooltip("Files의 *.lproj 폴더 이름으로 Info.plist CFBundleLocalizations를 채운다")]
        [SerializeField] public bool FillLocalizationsFromLproj = true;

        /// <summary> 기본 경로에 있으면 그것, 없으면 프로젝트에 하나뿐일 때 그것 (창의 자동 연결용) </summary>
        public static HBXcodeSettings LoadIfExists() =>
            BuildHelper.LoadSettingsAsset<HBXcodeSettings>(BuildHelper.USER_XCODE_SETTINGS_PATH);

        /// <summary> 빌드용: 기본 경로 우선(읽지 못하면 실패). 기본 경로에 없고 여러 개면 LIVE는 실패, DEV · QA는 경고 후 경로 순 첫 번째 </summary>
        public static HBXcodeSettings LoadIfExists(BuildContext context) =>
            BuildHelper.LoadSettingsAssetForBuild<HBXcodeSettings>(BuildHelper.USER_XCODE_SETTINGS_PATH, context);

        /// <summary>
        /// 기본 경로에 새 에셋을 만듭니다. 이미 있으면 새로 만들지 않고 기존 에셋을 반환합니다 (CreateAsset은 같은 경로의 기존 에셋을 지우고 만든다).
        /// </summary>
        public static HBXcodeSettings CreateAssetInUserFolder()
        {
            string path = BuildHelper.USER_XCODE_SETTINGS_PATH;
            var existing = AssetDatabase.LoadAssetAtPath<HBXcodeSettings>(path);
            if (existing == null && File.Exists(path))
            {
                // 파일은 있는데 아직 임포트 전일 수 있다 (자동 Refresh 꺼짐, git pull 직후 등)
                AssetDatabase.ImportAsset(path);
                existing = AssetDatabase.LoadAssetAtPath<HBXcodeSettings>(path);
            }

            if (existing != null)
            {
                Debug.Log($"[XcodeSettings] Asset already exists. Using: {path}");
                return existing;
            }

            if (File.Exists(path))
                throw new InvalidOperationException($"[XcodeSettings] A file exists at {path} but cannot be loaded as HBXcodeSettings (different type, import error, or missing script). Check the file before creating a new one; it was not overwritten.");

            BuildHelper.EnsureUserBuildDataDirExists();

            var instance = CreateInstance<HBXcodeSettings>();
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[XcodeSettings] Created user asset at: {path}");
            return instance;
        }
    }
}
