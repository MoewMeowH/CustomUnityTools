using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>
/// 빌드 파이프라인 전반에서 공유되는 데이터 컨텍스트입니다.
/// </summary>
public class BuildContext
{
    public BuildTarget Target { get; set; }
    public BuildTargetGroup TargetGroup { get; set; }
    public EBuildType BuildType { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string VersionCode { get; set; } = "1";
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
