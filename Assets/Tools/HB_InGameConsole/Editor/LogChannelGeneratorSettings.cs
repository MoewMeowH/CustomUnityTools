#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InGameConsoleSystem.Editor
{
    /// <summary>
    /// LogChannelGeneratorWindow의 채널 목록 및 생성 설정을 저장하고 관리하는 ScriptableObject입니다.
    /// </summary>
    public class LogChannelGeneratorSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/LogChannelGeneratorSettings.asset";
        public const string FallbackLegacyPath = "Assets/Tools/HB_InGameConsole/Editor/LogChannelGeneratorSettings.asset";

        [Serializable]
        public class ChannelItem
        {
            public string varName;
            public string tagName;

            public ChannelItem(string name)
            {
                varName = name;
                tagName = name;
            }

            public ChannelItem(string vName, string tName)
            {
                varName = vName;
                tagName = tName;
            }
        }

        [SerializeField] private string outputDirectory = "Assets/Scripts";
        [SerializeField] private string outputFileName = "LogChannels.cs";
        [SerializeField] private bool useSameName = true;
        [SerializeField] private bool autoGenerateOnSave = true;
        [SerializeField] private List<ChannelItem> channels = new List<ChannelItem>();

        public string OutputDirectory
        {
            get => outputDirectory;
            set => outputDirectory = value;
        }

        public string OutputFileName
        {
            get => outputFileName;
            set => outputFileName = value;
        }

        /// <summary>
        /// HLog 동작 심볼. 고정값 (커스텀 심볼은 지원하지 않음: Conditional은 OR 조건이라 심볼을 늘리면 끄는 수단이 사라진다)
        /// </summary>
        public const string ConditionalSymbol = "USE_HLOG";

        public bool UseSameName
        {
            get => useSameName;
            set => useSameName = value;
        }

        public bool AutoGenerateOnSave
        {
            get => autoGenerateOnSave;
            set => autoGenerateOnSave = value;
        }

        public List<ChannelItem> Channels => channels;

        /// <summary>
        /// 프로젝트 내 존재하는 설정 파일을 찾아 반환합니다. (자동 생성하지 않음)
        /// </summary>
        public static LogChannelGeneratorSettings FindSettings()
        {
            // 1. 프로젝트 내 기존 에셋 검색
            string[] guids = AssetDatabase.FindAssets("t:LogChannelGeneratorSettings");
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var existing = AssetDatabase.LoadAssetAtPath<LogChannelGeneratorSettings>(path);
                if (existing != null)
                {
                    return existing;
                }
            }

            // 2. 기본 프로젝트 루트 경로 로드 시도
            var asset = AssetDatabase.LoadAssetAtPath<LogChannelGeneratorSettings>(DefaultAssetPath);
            if (asset != null) return asset;

            // 3. 기존 레거시 경로 로드 시도
            return AssetDatabase.LoadAssetAtPath<LogChannelGeneratorSettings>(FallbackLegacyPath);
        }

        /// <summary>
        /// 설정 에셋을 생성하여 지정된 경로(또는 기본 경로)에 저장합니다.
        /// </summary>
        public static LogChannelGeneratorSettings CreateSettingsAsset(string assetPath = DefaultAssetPath)
        {
            var asset = CreateInstance<LogChannelGeneratorSettings>();
            asset.channels = new List<ChannelItem>(); // 빈 목록으로 시작

            string dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return asset;
        }
    }
}
#endif
