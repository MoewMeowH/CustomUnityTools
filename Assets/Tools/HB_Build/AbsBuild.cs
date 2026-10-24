using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using static HB.CustomBuild.Editor.BuildHelper;

namespace HB.CustomBuild.Editor
{
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
            // 플랫폼 확인에서 실패해도 이전 빌드의 경고 파일이 남지 않도록 가장 먼저 지운다
            DeleteWarningsFile();
            // 지원하지 않는 대상(StandaloneOSX 등)은 Standalone으로 취급돼 PC용 스텝이 돌므로 플랫폼 전환 전에 막는다
            if (!IsSupportedTarget(context.Target))
                throw ThrowError($"Unsupported build target: {context.Target} (supported: StandaloneWindows, StandaloneWindows64, Android, iOS)");
            EnsureActiveBuildTarget(context.TargetGroup, context.Target);

            // 딥 프로파일은 개발 빌드에서만 유효하다. 프로필 · 창 · CLI 모든 경로를 여기서 한 번에 맞춘다
            // (프로필 값은 그대로 두어야 CLI가 -devBuild만 덮어써도 딥 프로파일 설정이 살아 있다)
            if (!context.IsDevBuild && context.IsDeepProfile)
            {
                Log("[Pipeline] DeepProfile disabled because DevBuild is false.");
                context.IsDeepProfile = false;
            }

            var changeReport = BuildChangeReport.Capture();
            var snapshot = BuildSettingsSnapshot.Capture(context.TargetGroup);
            bool pipelineSucceeded = false;
            var sw = Stopwatch.StartNew();
            Log($"==========================================================================");
            Log($"🚀 [Build Pipeline Started] Target: {context.Target} | Mode: {context.BuildType} | Version: {context.Version} ({context.VersionCode})");
            Log($"==========================================================================");

            if (context.IsLiveDevBuild)
                WarnLiveDevBuild(context, "started");

            try
            {
                // 입력값(CLI · 프로필 · 창)의 이름은 스텝이 설정을 바꾸기 전에 먼저 확인한다
                ValidateOutputName(context);
                ValidateStepRegistry();
                ValidateDisabledSteps(context);
                ValidateLiveDefines(context);

                // 1. 사전 빌드 스텝들 순차 실행 (IPreBuildStep)
                BuildStepRegistry.ExecutePreBuildSteps(context);

                // 버전 · 버전 코드는 사전 스텝이 채우거나 바꿀 수 있으므로 스텝 뒤, 실제 빌드 전에 다시 확인한다
                ValidateOutputName(context);
                ValidateVersionCode(context);

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
                BuildReport report;
                using (BuildGuard.HBBuildScope())
                    report = BuildPipeline.BuildPlayer(context.PlayerOptions);
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

                if (context.IsLiveDevBuild)
                    WarnLiveDevBuild(context, "finished");

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
                finally
                {
                    // 빌드 산출물(기본 BuildOutput/ 과 이번 출력 경로)과 빌드 로그 파일은 목록에서 뺀다
                    var outputDirs = new List<string> { ResolveProjectRelativePath("BuildOutput") };
                    if (!string.IsNullOrWhiteSpace(context.OutputPath))
                        outputDirs.Add(ResolveProjectRelativePath(context.OutputPath));
                    changeReport?.LogChangesDuringBuild(outputDirs, GetLogFilePaths());
                    WriteWarningsFile(context);
                }
            }
        }

        /// <summary> Jenkinsfile이 읽는 경고 파일 (있으면 빌드를 UNSTABLE로 표시) </summary>
        public const string WARNINGS_FILE = "BuildOutput/HB_BUILD_WARNINGS.txt";

        private static void DeleteWarningsFile()
        {
            try
            {
                string path = ResolveProjectRelativePath(WARNINGS_FILE);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                LogWarning($"[Pipeline] Failed to delete previous {WARNINGS_FILE}: {ex.Message}");
            }
        }

        private static void WriteWarningsFile(BuildContext context)
        {
            if (context.Warnings.Count == 0) return;
            try
            {
                string path = ResolveProjectRelativePath(WARNINGS_FILE);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, context.Warnings);
            }
            catch (Exception ex)
            {
                LogWarning($"[Pipeline] Failed to write {WARNINGS_FILE}: {ex.Message}");
            }
        }

        /// <summary>
        /// LIVE + Development 빌드는 막지 않되(라이브 환경 프로파일링 · 로그 확인용), 로그 · 산출물 이름 · 경고 파일로 눈에 띄게 남깁니다.
        /// </summary>
        private static void WarnLiveDevBuild(BuildContext context, string phase)
        {
            string message = $"LIVE build with Development Build ON{(context.IsDeepProfile ? " + Deep Profiling" : string.Empty)} ({phase}). Output is marked _DEVBUILD. Do NOT ship this build to store users.";
            LogWarning("##########################################################################");
            if (phase == "started")
                context.AddWarning($"[Pipeline] {message}");
            else
                LogWarning($"[Pipeline] {message}");
            LogWarning("##########################################################################");
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
            // 버전 코드는 기본값으로 채우지 않는다 (모바일 빌드는 비어 있으면 ValidateVersionCode에서 실패)
            context.VersionCode = vCode ?? string.Empty;

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
            // 폴더는 없어도 예전 빌드의 "<이름>.zip"만 남아 있으면 같이 피한다 (폴더와 zip 이름을 짝으로 유지)
            if (string.IsNullOrEmpty(buildFolder) || (!Directory.Exists(buildFolder) && !File.Exists(buildFolder + ".zip")))
                return locationPathName;

            string parent = Path.GetDirectoryName(buildFolder) ?? string.Empty;
            string folderName = Path.GetFileName(buildFolder);
            string exeName = Path.GetFileName(locationPathName);

            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(parent, $"{folderName} ({i})");
                if (!Directory.Exists(candidate) && !File.Exists(candidate + ".zip"))
                {
                    Log($"[Pipeline] Build folder or zip '{folderName}' already exists. Using '{Path.GetFileName(candidate)}' instead.");
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

        /// <summary> 이번 Unity 실행이 쓰는 로그 파일 (-logFile 인자, Application.consoleLogPath) </summary>
        private static IEnumerable<string> GetLogFilePaths()
        {
            var paths = new List<string>();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    paths.Add(ResolveProjectRelativePath(args[i + 1].Trim('"')));
            }
            if (!string.IsNullOrEmpty(Application.consoleLogPath))
                paths.Add(Application.consoleLogPath);
            return paths;
        }

        /// <summary>
        /// 스텝 인스턴스 생성에 실패한 타입이 있으면 실패시킵니다. (빠진 스텝의 검증 · 설정 없이 빌드되는 것 방지)
        /// </summary>
        private static void ValidateStepRegistry()
        {
            BuildStepRegistry.ReloadSteps();
            var errors = BuildStepRegistry.InstantiationErrors;
            if (errors.Count > 0)
                throw ThrowError($"Failed to create build step(s): {string.Join("; ", errors)}. Fix the step class (public parameterless constructor, no exception).");

            var duplicates = BuildStepRegistry.DuplicateStepIdErrors;
            if (duplicates.Count > 0)
                throw ThrowError($"Duplicate build step id(s): {string.Join("; ", duplicates)}. Each step id must be unique — derive one class from the other or give each its own StepId.");
        }

        /// <summary>
        /// LIVE 빌드는 DefineContainer와 해당 빌드 타입 · 플랫폼 항목이 있어야 하고, 심볼 설정 스텝을 끌 수 없습니다.
        /// (스텝 안의 검사만으로는 스텝을 끈 프로필에서 건너뛰어지므로 파이프라인 단계에서 먼저 확인)
        /// </summary>
        private static void ValidateLiveDefines(BuildContext context)
        {
            if (context.BuildType != EBuildType.LIVE) return;

            if (context.DisabledStepIds.Contains(PreBuildStep_ConfigDefines.FIXED_STEP_ID))
                throw ThrowError($"LIVE build cannot disable the define step '{PreBuildStep_ConfigDefines.FIXED_STEP_ID}'. Re-enable it in the profile.");

            var container = context.GetCustomData<ProjectDefine>("ProjectDefine", null);
            if (container == null)
                container = ProjectDefine.LoadIfExists(context);
            if (container == null)
                throw ThrowError("LIVE build requires a DefineContainer (ProjectDefine) asset. Without it, the scripting define symbols committed in ProjectSettings are used as-is.");

            if (container.GetDefineArray(context.BuildType, context.Target) == null)
                throw ThrowError($"LIVE build requires a define entry for {context.BuildType}/{context.Target} in DefineContainer '{container.name}'. Add the entry (an empty list clears all symbols).");
        }

        /// <summary>
        /// 비활성 목록에 등록되지 않은 스텝 ID가 있으면 (스텝 이름 변경 · 삭제, 오타) 끄려던 의도가 깨진 것이므로
        /// LIVE 또는 배치 모드(Jenkins)에서는 실패, 로컬 DEV · QA에서는 경고합니다.
        /// </summary>
        private static void ValidateDisabledSteps(BuildContext context)
        {
            var unknown = BuildStepRegistry.GetUnknownStepIds(context.DisabledStepIds);
            if (unknown.Count == 0) return;

            string list = string.Join(", ", unknown.Select(id => $"'{id}'"));
            if (context.BuildType == EBuildType.LIVE || Application.isBatchMode)
                throw ThrowError($"Disabled step id(s) not registered: {list}. Remove them from the profile (Build Manager window) or -disabledSteps.");

            context.AddWarning($"[Pipeline] Disabled step id(s) not registered: {list} (the step may have been renamed or removed). LIVE / batch builds fail on this.");
        }

        private static readonly System.Text.RegularExpressions.Regex s_IosBuildNumber =
            new System.Text.RegularExpressions.Regex(@"^\d+(\.\d+){0,2}$");

        /// <summary>
        /// 모바일 빌드의 버전 코드는 기본값으로 채우지 않고, 비어 있거나 형식이 틀리면 실패시킵니다.
        /// Android versionCode는 모든 빌드에서 증가하는 양의 정수, iOS buildNumber는 점으로 구분한 정수 1~3개입니다.
        /// </summary>
        private static void ValidateVersionCode(BuildContext context)
        {
            string code = context.VersionCode?.Trim() ?? string.Empty;
            switch (context.Target)
            {
                case BuildTarget.Android:
                    if (!int.TryParse(code, out int versionCode) || versionCode <= 0)
                        throw ThrowError($"Android versionCode is required and must be a positive integer (increase it for every store build): '{code}'");
                    break;
                case BuildTarget.iOS:
                    if (!s_IosBuildNumber.IsMatch(code))
                        throw ThrowError($"iOS build number is required (e.g. 1, 12, 1.0.3): '{code}'");
                    break;
            }

            // 앞뒤 공백을 지운 값으로 맞춘다 (iOS CFBundleVersion은 공백을 허용하지 않음)
            context.VersionCode = code;
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
            // 배치 모드(-executeMethod)에서 플랫폼을 전환하면 스크립트가 재컴파일되지 않아 #if UNITY_ANDROID 등이 이전 플랫폼 기준으로 남는다.
            // 그래서 배치 모드는 전환하지 않고 실패시킨다 (Unity 실행 시 -buildTarget으로 맞춰야 함)
            if (Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != target)
            {
                throw ThrowError($"Active build target is {EditorUserBuildSettings.activeBuildTarget}, but this build targets {target}. " +
                                 $"Launch Unity with -buildTarget {target} (regenerate the Jenkinsfile if it was created before HB_Build added -buildTarget to profile builds).");
            }

            if (EditorUserBuildSettings.selectedBuildTargetGroup != group ||
                EditorUserBuildSettings.activeBuildTarget != target)
            {
                Log($"Switching Active Target -> Group: {group}, Target: {target}");
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
        }
    }
}
