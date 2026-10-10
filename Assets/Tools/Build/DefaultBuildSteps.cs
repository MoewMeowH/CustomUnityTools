using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

/// <summary>
/// 1. 기본 PlayerSettings 및 품질, 로그 설정 스텝 (전 플랫폼 공통)
/// </summary>
public class PreBuildStep_PlayerSettings : IPreBuildStep
{
    public virtual int ExecutionOrder => 10;
    public virtual string StepName => "기본 Player & Log 설정";

    public virtual bool IsActive(BuildContext context) => true;

    public virtual void OnPreBuild(BuildContext context)
    {
        PlayerSettings.stripEngineCode = false;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        // 로그 및 스택트레이스 설정
        PlayerSettings.usePlayerLog = context.IsDevBuild;
        var enable = context.IsDevBuild || context.BuildType == EBuildType.DEV 
            ? StackTraceLogType.ScriptOnly 
            : StackTraceLogType.None;

        PlayerSettings.SetStackTraceLogType(LogType.Log, enable);
        PlayerSettings.SetStackTraceLogType(LogType.Warning, enable);
        PlayerSettings.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
        PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
        PlayerSettings.SetStackTraceLogType(LogType.Assert, enable);
    }
}

/// <summary>
/// 2. Scripting Define Symbols 구성 스텝 (DefineContainer 연동, 전 플랫폼 공통)
/// </summary>
public class PreBuildStep_ConfigDefines : IPreBuildStep
{
    public virtual int ExecutionOrder => 20;
    public virtual string StepName => "Scripting Define Symbols (전처리기 심볼) 설정";

    public virtual bool IsActive(BuildContext context) => true;

    public virtual void OnPreBuild(BuildContext context)
    {
        if (!File.Exists(BuildHelper.SYMBOL_DEFINE_PATH))
            return;

        var container = AssetDatabase.LoadAssetAtPath<ProjectDefine>(BuildHelper.SYMBOL_DEFINE_PATH);
        if (container == null) return;

        var defines = container.GetDefineArray(context.BuildType, context.Target);
        if (defines != null && defines.Length > 0)
        {
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(context.TargetGroup);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, defines);
            BuildHelper.Log($"[Defines] Applied {defines.Length} define(s): {string.Join(";", defines)}");
        }
    }
}

/// <summary>
/// 3-A. Android 전용 빌드 세부 설정 스텝 (Android 타깃일 때만 활성화)
/// </summary>
public class PreBuildStep_AndroidSettings : IPreBuildStep
{
    public virtual int ExecutionOrder => 30;
    public virtual string StepName => "Android 플랫폼 세부 설정 (ARM64, IL2CPP, AAB 등)";

    public virtual bool IsActive(BuildContext context) => context.Target == BuildTarget.Android;

    public virtual void OnPreBuild(BuildContext context)
    {
        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(context.TargetGroup);

        PlayerSettings.bundleVersion = context.Version;
        if (int.TryParse(context.VersionCode, out int vCode))
            PlayerSettings.Android.bundleVersionCode = vCode;

        EditorUserBuildSettings.development = context.IsDevBuild;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = context.IsDeepProfile;
        EditorUserBuildSettings.connectProfiler = BuildHelper.IsAutoProfile;

        EditorUserBuildSettings.buildAppBundle = context.IsAab;
        EditorUserBuildSettings.androidBuildType = context.IsDevBuild 
            ? AndroidBuildType.Debug 
            : AndroidBuildType.Release;

        PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
    }
}

/// <summary>
/// 3-B. Windows Standalone 전용 빌드 설정 스텝 (PC 타깃일 때만 활성화)
/// </summary>
public class PreBuildStep_WindowsSettings : IPreBuildStep
{
    public virtual int ExecutionOrder => 30;
    public virtual string StepName => "Windows Standalone 세부 설정";

    public virtual bool IsActive(BuildContext context) =>
        context.Target == BuildTarget.StandaloneWindows || context.Target == BuildTarget.StandaloneWindows64;

    public virtual void OnPreBuild(BuildContext context)
    {
        PlayerSettings.bundleVersion = context.Version;
        EditorUserBuildSettings.development = context.IsDevBuild;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = context.IsDeepProfile;
        EditorUserBuildSettings.connectProfiler = BuildHelper.IsAutoProfile;
    }
}

/// <summary>
/// 3-C. iOS 전용 빌드 설정 스텝 (iOS 타깃일 때만 활성화)
/// </summary>
public class PreBuildStep_IOSSettings : IPreBuildStep
{
    public virtual int ExecutionOrder => 30;
    public virtual string StepName => "iOS 플랫폼 세부 설정 (Provisioning Profile, 빌드 번호 등)";

    public virtual bool IsActive(BuildContext context) => context.Target == BuildTarget.iOS;

    public virtual void OnPreBuild(BuildContext context)
    {
        PlayerSettings.bundleVersion = context.Version;
        PlayerSettings.iOS.buildNumber = context.VersionCode;

        string provisionUuid = context.GetCustomData<string>("ProvisionUUID", null);
        if (!string.IsNullOrEmpty(provisionUuid))
        {
            PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Distribution;
            PlayerSettings.iOS.iOSManualProvisioningProfileID = provisionUuid;
        }

        EditorUserBuildSettings.development = context.IsDevBuild;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = context.IsDeepProfile;
        EditorUserBuildSettings.connectProfiler = BuildHelper.IsAutoProfile;
    }
}

/// <summary>
/// 4. 사후 산출물 ZIP 압축 스텝 (PC 타깃 및 ZipPackage 옵션 활성화 시에만 동작)
/// </summary>
public class PostBuildStep_ZipArchive : IPostBuildStep
{
    public virtual int ExecutionOrder => 100;
    public virtual string StepName => "PC 산출물 ZIP 자동 압축 (선택 옵션)";

    public virtual bool IsActive(BuildContext context)
    {
        return context.ZipPackage && 
               (context.Target == BuildTarget.StandaloneWindows || context.Target == BuildTarget.StandaloneWindows64);
    }

    public virtual void OnPostBuild(BuildContext context)
    {
        if (string.IsNullOrEmpty(context.PlayerOptions.locationPathName)) return;

        string exePath = context.PlayerOptions.locationPathName;
        if (!File.Exists(exePath)) return;

        string buildFolder = Path.GetDirectoryName(exePath);
        string zipPath = Path.Combine(buildFolder, $"{Path.GetFileNameWithoutExtension(exePath)}.zip");

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        BuildHelper.Log($"[PostBuild] Creating ZIP package -> {zipPath}");
        ZipFile.CreateFromDirectory(buildFolder, zipPath, CompressionLevel.Optimal, false);
        BuildHelper.Log("[PostBuild] ZIP package created successfully!");
    }
}
