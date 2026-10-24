using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 빌드 파이프라인 전반에서 공유되는 데이터 컨텍스트입니다.
    /// </summary>
    public class BuildContext
    {
        public BuildTarget Target { get; set; }
        public BuildTargetGroup TargetGroup { get; set; }
        public EBuildType BuildType { get; set; }
        public string Version { get; set; } = "1.0.0";
        /// <summary> Android versionCode / iOS buildNumber. 모바일 빌드에서는 비어 있으면 실패 (기본값으로 채우지 않음) </summary>
        public string VersionCode { get; set; } = string.Empty;
        public bool IsDevBuild { get; set; }
        public bool IsDeepProfile { get; set; }
        public bool IsCleanBuild { get; set; }
        public bool IsAab { get; set; }
        public bool ZipPackage { get; set; }
        public string OutputPath { get; set; }
        public List<string> Scenes { get; set; } = new List<string>();
        public BuildPlayerOptions PlayerOptions;
        public BuildReport Report { get; set; }

        /// <summary>
        /// 이번 빌드에서 제외할 스텝 ID 목록 (프로필 에셋 또는 -disabledSteps CLI 인자로 지정)
        /// </summary>
        public HashSet<string> DisabledStepIds { get; } = new HashSet<string>();

        /// <summary>
        /// LIVE인데 Development Build가 켜진 빌드 (라이브 환경 프로파일링 · 로그 확인용으로 허용하되 눈에 띄게 표시)
        /// </summary>
        public bool IsLiveDevBuild => BuildType == EBuildType.LIVE && IsDevBuild;

        /// <summary>
        /// 빌드를 막지는 않지만 사람이 확인해야 할 경고. 빌드가 끝나면 BuildOutput/HB_BUILD_WARNINGS.txt로 남는다 (Jenkins는 UNSTABLE 처리)
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

        public void AddWarning(string message)
        {
            Warnings.Add(message);
            BuildHelper.LogWarning(message);
        }

        /// <summary>
        /// 빌드 결과물 경로를 계산합니다.
        /// Windows는 버전별 하위 폴더에 실행 파일과 데이터 폴더를 모으고, Android는 단일 파일, iOS는 Xcode 프로젝트 폴더입니다.
        /// LIVE + Development 빌드는 이름에 _DEVBUILD가 붙습니다.
        /// </summary>
        public string GetLocationPathName()
        {
            string outDir = BuildHelper.ResolveProjectRelativePath(OutputPath);
            string baseName = $"{Application.productName}_v{Version}_{BuildType}" + (IsLiveDevBuild ? "_DEVBUILD" : string.Empty);
            return Target switch
            {
                BuildTarget.Android => Path.Combine(outDir, baseName + (IsAab ? ".aab" : ".apk")),
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => Path.Combine(outDir, baseName, baseName + ".exe"),
                _ => Path.Combine(outDir, baseName)
            };
        }

        /// <summary>
        /// 현재 컨텍스트 값(씬, 경로, 개발 빌드 옵션 등)으로 PlayerOptions를 다시 구성합니다.
        /// 컨텍스트 값을 바꾼 뒤에는 반드시 호출해야 실제 빌드 옵션에 반영됩니다.
        /// </summary>
        public void RebuildPlayerOptions()
        {
            PlayerOptions = new BuildPlayerOptions
            {
                scenes = Scenes.ToArray(),
                target = Target,
                targetGroup = TargetGroup,
                locationPathName = GetLocationPathName(),
                options = BuildOptions.CompressWithLz4HC
            };

            if (IsDevBuild)
            {
                PlayerOptions.options |= BuildOptions.Development;
                if (IsDeepProfile)
                    PlayerOptions.options |= BuildOptions.EnableDeepProfilingSupport;
                // -autoProfile: BuildPipeline.BuildPlayer는 EditorUserBuildSettings.connectProfiler가 아니라 이 옵션을 따른다
                if (BuildHelper.IsAutoProfile)
                    PlayerOptions.options |= BuildOptions.ConnectWithProfiler;
            }

            if (IsCleanBuild)
                PlayerOptions.options |= BuildOptions.CleanBuildCache;
        }

        /// <summary>
        /// 프로젝트 전용 커스텀 스텝들이 자유롭게 데이터를 주고받을 수 있는 확장 딕셔너리
        /// </summary>
        public Dictionary<string, object> CustomData { get; } = new Dictionary<string, object>();

        public void SetCustomData<T>(string key, T value) => CustomData[key] = value;

        public T GetCustomData<T>(string key, T defaultValue = default)
        {
            if (CustomData.TryGetValue(key, out var val) && val is T typedVal)
                return typedVal;
            return defaultValue;
        }
    }

    /// <summary>
    /// Unity BuildPipeline.BuildPlayer 실행 직전에 호출되는 사전 빌드 스텝 인터페이스입니다.
    /// </summary>
    public interface IPreBuildStep
    {
        /// <summary> 스텝 고유 식별자 (체크박스 On/Off 저장용) </summary>
        string StepId => GetType().FullName;

        /// <summary> 실행 우선순위 (낮을수록 먼저 실행) </summary>
        int ExecutionOrder { get; }

        /// <summary> 에디터 UI에 표시될 설명 </summary>
        string StepName { get; }

        /// <summary> 현재 빌드 대상(플랫폼, 옵션 등)에 활성화할지 여부 </summary>
        bool IsActive(BuildContext context) => true;

        /// <summary> 사전 빌드 설정 로직 실행 </summary>
        void OnPreBuild(BuildContext context);
    }

    /// <summary>
    /// Unity BuildPipeline.BuildPlayer 실행 완료 후 호출되는 사후 빌드 스텝 인터페이스입니다.
    /// </summary>
    public interface IPostBuildStep
    {
        /// <summary> 스텝 고유 식별자 (체크박스 On/Off 저장용) </summary>
        string StepId => GetType().FullName;

        /// <summary> 실행 우선순위 (낮을수록 먼저 실행) </summary>
        int ExecutionOrder { get; }

        /// <summary> 에디터 UI에 표시될 설명 </summary>
        string StepName { get; }

        /// <summary> 현재 빌드 대상(플랫폼, 옵션 등)에 활성화할지 여부 </summary>
        bool IsActive(BuildContext context) => true;

        /// <summary> 사후 빌드 로직 실행 </summary>
        void OnPostBuild(BuildContext context);
    }
}
