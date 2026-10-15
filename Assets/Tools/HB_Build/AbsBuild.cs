using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using static BuildHelper;

/// <summary>
/// Jenkins 및 로컬 빌드 진입을 중계하는 프로세스 클래스입니다.
/// </summary>
public sealed class BuildProcess
{
    public static void Build(BuildTarget target)
    {
        Log($"[BuildProcess] Starting automated build pipeline for {target}");
        AbsBuild.ExecuteFromCommandLine(target);
    }
}

/// <summary>
/// 빌드 파이프라인 오케스트레이터입니다.
/// IPreBuildStep 설정 실행 -> Unity BuildPipeline.BuildPlayer -> IPostBuildStep 사후 처리만 순수하게 수행합니다.
/// </summary>
public static class AbsBuild
{
    public static BuildReport Execute(BuildContext context)
    {
        EnsureActiveBuildTarget(context.TargetGroup, context.Target);

        var sw = Stopwatch.StartNew();
        Log($"==========================================================================");
        Log($"🚀 [Build Pipeline Started] Target: {context.Target} | Mode: {context.BuildType} | Version: {context.Version}");
        Log($"==========================================================================");

        // 1. 사전 빌드 스텝들 순차 실행 (IPreBuildStep)
        BuildStepRegistry.ExecutePreBuildSteps(context);

        // 2. 실제 Unity 빌드 실행
        Log($"[Pipeline] Invoking Unity BuildPipeline.BuildPlayer -> {context.PlayerOptions.locationPathName}");
        BuildReport report = BuildPipeline.BuildPlayer(context.PlayerOptions);
        context.Report = report;

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw ThrowError($"Build Failed: Result={report.summary.result}, Errors={report.summary.totalErrors}");
        }

        Log($"[Pipeline] Build Succeeded! Output Size: {report.summary.totalSize / (1024 * 1024):F1} MB");

        // 3. 사후 빌드 스텝들 순차 실행 (IPostBuildStep)
        BuildStepRegistry.ExecutePostBuildSteps(context);

        sw.Stop();
        Log($"==========================================================================");
        Log($"🎉 [Build Pipeline Completed Successfully in {sw.Elapsed.TotalSeconds:F1}s]");
        Log($"==========================================================================");

        return report;
    }

    /// <summary>
    /// 지정된 HBBuildProfile 에셋을 바탕으로 빌드 파이프라인을 실행합니다.
    /// </summary>
    public static BuildReport ExecuteFromProfile(HBBuildProfile profile)
    {
        if (profile == null)
        {
            throw ThrowError("HBBuildProfile instance is null!");
        }

        Log($"[Pipeline] Executing build from Profile: {profile.name} (Platform: {profile.TargetPlatform}, Mode: {profile.BuildType})");
        var context = profile.ToBuildContext();
        return Execute(context);
    }

    /// <summary>
    /// 에셋 경로를 통해 HBBuildProfile을 로드하여 빌드 파이프라인을 실행합니다. (Jenkins -buildProfile CLI 지원)
    /// </summary>
    public static BuildReport ExecuteFromProfilePath(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            throw ThrowError("Build profile path argument is empty!");
        }

        profilePath = profilePath.Trim().Replace('\\', '/');
        var profile = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(profilePath);
        if (profile == null)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(profilePath);
            string[] guids = AssetDatabase.FindAssets($"t:HBBuildProfile {fileNameWithoutExt}");
            if (guids != null && guids.Length > 0)
            {
                string resolvedPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                profile = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(resolvedPath);
            }
        }

        if (profile == null)
        {
            throw ThrowError($"Failed to load HBBuildProfile at path: '{profilePath}'");
        }

        var context = profile.ToBuildContext();
        ApplyCommandLineOverrides(context);
        return Execute(context);
    }

    private static void ApplyCommandLineOverrides(BuildContext context)
    {
        var buildTypeStr = GetArg(BUILD_TYPE);
        if (!string.IsNullOrEmpty(buildTypeStr) && Enum.TryParse<EBuildType>(buildTypeStr, true, out var bType))
        {
            Log($"[CLI Override] BuildType: {context.BuildType} -> {bType}");
            context.BuildType = bType;
        }

        var ver = GetArg(BUILD_VERSION);
        if (!string.IsNullOrEmpty(ver))
        {
            Log($"[CLI Override] Version: {context.Version} -> {ver}");
            context.Version = ver;
        }

        var vCode = GetArg(VERSION_CODE);
        if (!string.IsNullOrEmpty(vCode))
        {
            Log($"[CLI Override] VersionCode: {context.VersionCode} -> {vCode}");
            context.VersionCode = vCode;
        }

        var devBuildStr = GetArg(DEV_BUILD);
        if (!string.IsNullOrEmpty(devBuildStr) && bool.TryParse(devBuildStr, out bool dev))
        {
            Log($"[CLI Override] DevBuild: {context.IsDevBuild} -> {dev}");
            context.IsDevBuild = dev;
        }

        var isAabStr = GetArg(IS_AAB);
        if (!string.IsNullOrEmpty(isAabStr) && bool.TryParse(isAabStr, out bool aab))
        {
            Log($"[CLI Override] IsAab: {context.IsAab} -> {aab}");
            context.IsAab = aab;
        }

        var zipStr = GetArg(ZIP_PACKAGE);
        if (!string.IsNullOrEmpty(zipStr) && bool.TryParse(zipStr, out bool zip))
        {
            Log($"[CLI Override] ZipPackage: {context.ZipPackage} -> {zip}");
            context.ZipPackage = zip;
        }

        var customOut = GetArg(OUTPUT_PATH);
        if (!string.IsNullOrEmpty(customOut))
        {
            Log($"[CLI Override] OutputPath: {context.OutputPath} -> {customOut}");
            context.OutputPath = customOut;
        }

        string ext = context.Target switch
        {
            BuildTarget.Android => context.IsAab ? ".aab" : ".apk",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
            _ => ""
        };
        string appName = $"{Application.productName}_v{context.Version}_{context.BuildType}{ext}";
        context.PlayerOptions.locationPathName = Path.Combine(context.OutputPath, appName);
    }

    /// <summary>
    /// Jenkins 등 CLI 환경에서 커맨드라인 인자들을 읽어 BuildContext를 구성하고 파이프라인을 실행합니다.
    /// </summary>
    public static BuildReport ExecuteFromCommandLine(BuildTarget target)
    {
        var context = new BuildContext();
        context.Target = target;
        context.TargetGroup = GetTargetGroup(target);

        // 인자 파싱
        var buildTypeStr = GetArg(BUILD_TYPE);
        EBuildType bType = EBuildType.DEV;
        if (!string.IsNullOrEmpty(buildTypeStr))
            Enum.TryParse(buildTypeStr, true, out bType);
        context.BuildType = bType;

        var ver = GetArg(BUILD_VERSION);
        context.Version = !string.IsNullOrEmpty(ver) ? ver : Application.version;

        var vCode = GetArg(VERSION_CODE);
        context.VersionCode = !string.IsNullOrEmpty(vCode) ? vCode : "1";

        context.IsDevBuild = IsDevBuild;
        context.IsDeepProfile = IsDeepProfile;
        context.IsCleanBuild = IsCleanBuild;

        var isAabStr = GetArg(IS_AAB);
        if (bool.TryParse(isAabStr, out bool isAab))
            context.IsAab = isAab;

        var zipStr = GetArg(ZIP_PACKAGE);
        if (bool.TryParse(zipStr, out bool zip))
            context.ZipPackage = zip;

        var customOut = GetArg(OUTPUT_PATH);
        context.OutputPath = !string.IsNullOrEmpty(customOut) ? customOut : GetDefaultOutputPath(target);

        // 씬 목록
        var sceneList = GetArg(SCENE_LIST);
        if (!string.IsNullOrEmpty(sceneList) && !sceneList.Equals(NONE_SCENE_LIST, StringComparison.OrdinalIgnoreCase))
        {
            context.Scenes = sceneList.Split(';', '$', ',').Where(x => !string.IsNullOrEmpty(x)).ToList();
        }
        else
        {
            context.Scenes = GetDefaultBuildScenes().ToList();
        }

        // 출력 파일 경로 생성
        string ext = target switch
        {
            BuildTarget.Android => context.IsAab ? ".aab" : ".apk",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
            _ => ""
        };

        if (!Directory.Exists(context.OutputPath))
            Directory.CreateDirectory(context.OutputPath);

        string appName = $"{Application.productName}_v{context.Version}_{context.BuildType}{ext}";
        string locationPath = Path.Combine(context.OutputPath, appName);

        // BuildPlayerOptions 구성
        context.PlayerOptions = new BuildPlayerOptions
        {
            scenes = context.Scenes.ToArray(),
            target = target,
            targetGroup = context.TargetGroup,
            locationPathName = locationPath,
            options = BuildOptions.CompressWithLz4HC
        };

        if (context.IsDevBuild)
        {
            context.PlayerOptions.options |= BuildOptions.Development;
            if (context.IsDeepProfile) context.PlayerOptions.options |= BuildOptions.EnableDeepProfilingSupport;
        }
        if (context.IsCleanBuild)
        {
            context.PlayerOptions.options |= BuildOptions.CleanBuildCache;
        }

        return Execute(context);
    }

    private static void EnsureActiveBuildTarget(BuildTargetGroup group, BuildTarget target)
    {
        if (EditorUserBuildSettings.selectedBuildTargetGroup != group ||
            EditorUserBuildSettings.activeBuildTarget != target)
        {
            Log($"Switching Active Target -> Group: {group}, Target: {target}");
            EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        }
    }
}
