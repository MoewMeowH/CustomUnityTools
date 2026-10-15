using System;
using System.Collections.Generic;
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
/// 스텝이 변경한 PlayerSettings 등은 빌드 성공/실패와 관계없이 빌드 전 상태로 복원합니다.
/// </summary>
public static class AbsBuild
{
    public static BuildReport Execute(BuildContext context)
    {
        EnsureActiveBuildTarget(context.TargetGroup, context.Target);

        var snapshot = BuildSettingsSnapshot.Capture(context.TargetGroup);
        bool pipelineSucceeded = false;
        var sw = Stopwatch.StartNew();
        Log($"==========================================================================");
        Log($"🚀 [Build Pipeline Started] Target: {context.Target} | Mode: {context.BuildType} | Version: {context.Version}");
        Log($"==========================================================================");

        try
        {
            ValidateOutputName(context);
            WarnUnknownDisabledSteps(context);

            // 1. 사전 빌드 스텝들 순차 실행 (IPreBuildStep)
            BuildStepRegistry.ExecutePreBuildSteps(context);

            if (context.PlayerOptions.scenes == null || context.PlayerOptions.scenes.Length == 0)
            {
                throw ThrowError("No scenes to build. Enable scenes in Build Settings or set the scene list in the build profile.");
            }

            // Windows는 같은 이름의 빌드 폴더가 있으면 덮어쓰지 않고 "이름 (2)" 폴더로 분리한다 (이전 빌드 잔여 파일 섞임 방지)
            if (context.Target is BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64)
                context.PlayerOptions.locationPathName = GetUniqueBuildFolderLocation(context.PlayerOptions.locationPathName);

            string outputDir = Path.GetDirectoryName(context.PlayerOptions.locationPathName);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            // 2. 실제 Unity 빌드 실행
            Log($"[Pipeline] Invoking Unity BuildPipeline.BuildPlayer -> {context.PlayerOptions.locationPathName}");
            BuildReport report = BuildPipeline.BuildPlayer(context.PlayerOptions);
            context.Report = report;

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw ThrowError($"Build Failed: Result={report.summary.result}, Errors={report.summary.totalErrors}");
            }

            Log($"[Pipeline] Build Succeeded! Output Size: {report.summary.totalSize / (1024d * 1024d):F1} MB");

            // 3. 사후 빌드 스텝들 순차 실행 (IPostBuildStep)
            BuildStepRegistry.ExecutePostBuildSteps(context);

            sw.Stop();
            Log($"==========================================================================");
            Log($"🎉 [Build Pipeline Completed Successfully in {sw.Elapsed.TotalSeconds:F1}s]");
            Log($"==========================================================================");

            pipelineSucceeded = true;
            return report;
        }
        finally
        {
            try
            {
                snapshot.Restore();
            }
            catch (Exception restoreEx)
            {
                UnityEngine.Debug.LogException(restoreEx);

                // 빌드가 이미 실패했다면 원래 예외를 가리지 않도록 경고만 남기고,
                // 빌드는 성공했는데 복원만 실패했다면 설정이 바뀐 채 남으므로 실패로 처리한다
                if (pipelineSucceeded)
                    throw ThrowError($"Build succeeded but failed to restore build settings: {restoreEx.Message}");

                LogWarning($"[Pipeline] Failed to restore build settings: {restoreEx.Message}");
            }
        }
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
    /// 경로가 틀리면 다른 프로필로 대체하지 않고 실패합니다.
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
            string available = string.Join("\n  ", HBBuildProfile.FindAllProfiles().Select(AssetDatabase.GetAssetPath));
            throw ThrowError($"Failed to load HBBuildProfile at path: '{profilePath}'\nAvailable profiles:\n  {available}");
        }

        var context = profile.ToBuildContext();
        ApplyCommandLineOverrides(context);
        return Execute(context);
    }

    private static void ApplyCommandLineOverrides(BuildContext context)
    {
        if (TryGetBuildTypeArg(out var bType))
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

        if (TryGetBoolArg(DEV_BUILD, out bool dev))
        {
            Log($"[CLI Override] DevBuild: {context.IsDevBuild} -> {dev}");
            context.IsDevBuild = dev;
        }

        if (TryGetBoolArg(DEEP_PROF, out bool deep))
        {
            Log($"[CLI Override] DeepProfile: {context.IsDeepProfile} -> {deep}");
            context.IsDeepProfile = deep;
        }

        if (TryGetBoolArg(CLEAN_BUILD, out bool clean))
        {
            Log($"[CLI Override] CleanBuild: {context.IsCleanBuild} -> {clean}");
            context.IsCleanBuild = clean;
        }

        if (TryGetBoolArg(IS_AAB, out bool aab))
        {
            Log($"[CLI Override] IsAab: {context.IsAab} -> {aab}");
            context.IsAab = aab;
        }

        if (TryGetBoolArg(ZIP_PACKAGE, out bool zip))
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

        if (TryGetDisabledStepsArg(out var disabledSteps))
        {
            Log($"[CLI Override] DisabledSteps: {string.Join(";", disabledSteps)}");
            context.DisabledStepIds.Clear();
            context.DisabledStepIds.UnionWith(disabledSteps);
        }

        // 딥 프로파일은 개발 빌드에서만 유효하다 (-devBuild false만 넘겨도 스텝이 딥 프로파일을 켜지 않도록)
        if (!context.IsDevBuild && context.IsDeepProfile)
        {
            Log("[CLI Override] DeepProfile disabled because DevBuild is false.");
            context.IsDeepProfile = false;
        }

        // 덮어쓴 값(개발 빌드, 경로 등)을 실제 빌드 옵션에 반영
        context.RebuildPlayerOptions();
    }

    /// <summary>
    /// Jenkins 등 CLI 환경에서 커맨드라인 인자들을 읽어 BuildContext를 구성하고 파이프라인을 실행합니다.
    /// </summary>
    public static BuildReport ExecuteFromCommandLine(BuildTarget target)
    {
        var context = new BuildContext();
        context.Target = target;
        context.TargetGroup = GetTargetGroup(target);

        // 인자 파싱 (값이 있는데 잘못된 경우 기본값으로 대체하지 않고 실패)
        context.BuildType = TryGetBuildTypeArg(out var bType) ? bType : EBuildType.DEV;

        var ver = GetArg(BUILD_VERSION);
        context.Version = !string.IsNullOrEmpty(ver) ? ver : Application.version;

        var vCode = GetArg(VERSION_CODE);
        context.VersionCode = !string.IsNullOrEmpty(vCode) ? vCode : "1";

        context.IsDevBuild = TryGetBoolArg(DEV_BUILD, out bool dev) && dev;
        context.IsDeepProfile = context.IsDevBuild && TryGetBoolArg(DEEP_PROF, out bool deep) && deep;
        context.IsCleanBuild = TryGetBoolArg(CLEAN_BUILD, out bool clean) && clean;
        context.IsAab = TryGetBoolArg(IS_AAB, out bool isAab) && isAab;
        context.ZipPackage = TryGetBoolArg(ZIP_PACKAGE, out bool zip) && zip;

        if (TryGetDisabledStepsArg(out var disabledSteps))
            context.DisabledStepIds.UnionWith(disabledSteps);

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

        context.RebuildPlayerOptions();
        return Execute(context);
    }

    /// <summary>
    /// OutputPath/&lt;이름&gt;/&lt;이름&gt;.exe 에서 &lt;이름&gt; 폴더가 이미 있으면 "&lt;이름&gt; (2)", "&lt;이름&gt; (3)" ... 중 비어 있는 폴더로 바꿉니다.
    /// 실행 파일 이름은 그대로 둡니다.
    /// </summary>
    private static string GetUniqueBuildFolderLocation(string locationPathName)
    {
        string buildFolder = Path.GetDirectoryName(locationPathName);
        if (string.IsNullOrEmpty(buildFolder) || !Directory.Exists(buildFolder))
            return locationPathName;

        string parent = Path.GetDirectoryName(buildFolder) ?? string.Empty;
        string folderName = Path.GetFileName(buildFolder);
        string exeName = Path.GetFileName(locationPathName);

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(parent, $"{folderName} ({i})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate + ".zip"))
            {
                Log($"[Pipeline] Build folder '{folderName}' already exists. Using '{Path.GetFileName(candidate)}' instead.");
                return Path.Combine(candidate, exeName);
            }
        }
    }

    /// <summary>
    /// 출력 파일/폴더 이름에 쓰이는 제품명·버전에 파일명 금지 문자나 경로 구분자가 있으면 실패시킵니다.
    /// (CLI -buildVersion 에 "../" 등이 들어가 OutputPath 밖으로 벗어나는 것도 막음)
    /// </summary>
    private static void ValidateOutputName(BuildContext context)
    {
        CheckNamePart("productName", Application.productName);
        CheckNamePart("version", context.Version);

        static void CheckNamePart(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw ThrowError($"Build {label} is empty.");

            char invalid = value.FirstOrDefault(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\');
            if (invalid != default(char) || value.Contains(".."))
                throw ThrowError($"Build {label} '{value}' contains characters not allowed in file names.");
        }
    }

    /// <summary>
    /// 비활성 목록에 등록되지 않은 스텝 ID가 있으면 경고합니다. (오타로 끄려던 스텝이 실행되는 것을 알아차리도록)
    /// </summary>
    private static void WarnUnknownDisabledSteps(BuildContext context)
    {
        if (context.DisabledStepIds.Count == 0) return;

        var knownIds = new HashSet<string>(
            BuildStepRegistry.GetAllPreBuildSteps().Select(s => s.StepId)
                .Concat(BuildStepRegistry.GetAllPostBuildSteps().Select(s => s.StepId)));

        foreach (string id in context.DisabledStepIds)
        {
            if (!knownIds.Contains(id))
                LogWarning($"[Pipeline] Unknown disabled step id '{id}' (no registered step has this id).");
        }
    }

    private static bool TryGetBuildTypeArg(out EBuildType buildType)
    {
        buildType = default;
        string value = GetArg(BUILD_TYPE);
        if (string.IsNullOrEmpty(value))
        {
            if (HasArg(BUILD_TYPE))
                throw ThrowError($"-{BUILD_TYPE} requires a value (allowed: {string.Join(", ", Enum.GetNames(typeof(EBuildType)))})");
            return false;
        }

        if (!Enum.TryParse(value, true, out buildType) || !Enum.IsDefined(typeof(EBuildType), buildType))
        {
            throw ThrowError($"Invalid -{BUILD_TYPE} argument: '{value}' (allowed: {string.Join(", ", Enum.GetNames(typeof(EBuildType)))})");
        }
        return true;
    }

    private static bool TryGetDisabledStepsArg(out string[] stepIds)
    {
        string value = GetArg(DISABLED_STEPS);
        if (string.IsNullOrEmpty(value))
        {
            // -disabledSteps "" 는 "NONE"과 같이 비활성 목록을 비우는 것으로 처리
            stepIds = Array.Empty<string>();
            return HasArg(DISABLED_STEPS);
        }

        stepIds = value.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0 && x != NONE_SCENE_LIST).ToArray();
        return true;
    }

    private static void EnsureActiveBuildTarget(BuildTargetGroup group, BuildTarget target)
    {
        if (EditorUserBuildSettings.selectedBuildTargetGroup != group ||
            EditorUserBuildSettings.activeBuildTarget != target)
        {
            // executeMethod 도중 전환하면 스크립트가 재컴파일되지 않으므로 CLI에서는 -buildTarget 지정을 권장
            Log($"Switching Active Target -> Group: {group}, Target: {target}");
            EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        }
    }
}
