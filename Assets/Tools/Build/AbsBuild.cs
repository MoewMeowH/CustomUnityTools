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
            target = context.Target,
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
