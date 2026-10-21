using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 0. SceneDefine 에셋이 존재할 경우 빌드 타입(DEV/QA/LIVE)별 무시할 씬(IgnoreScene) 자동 필터링
    /// (에셋 미생성 시 자동으로 건너뜀)
    /// </summary>
    public class PreBuildStep_FilterScenes : IPreBuildStep
    {
        public virtual int ExecutionOrder => 5;
        public virtual string StepName => "SceneDefine 기반 무시 씬(IgnoreScene) 필터링 (선택 사항)";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            // UnityEngine.Object는 ?? 로 판정하지 않는다 (파괴/누락된 참조가 null로 취급되지 않음)
            var sceneDefine = context.GetCustomData<SceneDefine>("SceneDefine", null);
            if (sceneDefine == null)
                sceneDefine = SceneDefine.LoadIfExists();

            if (sceneDefine == null) return;

            int originalCount = context.Scenes.Count;
            context.Scenes = sceneDefine.FilterScenes(context.BuildType, context.Scenes);
            int filteredCount = context.Scenes.Count;

            if (originalCount != filteredCount)
            {
                BuildHelper.Log($"[SceneDefine] Filtered {originalCount - filteredCount} ignored scene(s) for {context.BuildType}. Active scenes: {filteredCount}");
                context.PlayerOptions.scenes = context.Scenes.ToArray();
            }
        }
    }

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
    /// 2. Scripting Define Symbols 구성 스텝 (DefineContainer 연동)
    /// (에셋 미생성 시 기존 PlayerSettings 설정을 유지하고 자동으로 건너뜀)
    /// </summary>
    public class PreBuildStep_ConfigDefines : IPreBuildStep
    {
        public virtual int ExecutionOrder => 20;
        public virtual string StepName => "Scripting Define Symbols (전처리기 심볼) 설정 (선택 사항)";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            var container = context.GetCustomData<ProjectDefine>("ProjectDefine", null);
            if (container == null)
                container = ProjectDefine.LoadIfExists();

            if (container == null)
            {
                // 사용자가 정의하지 않았으면 기존 심볼 유지하고 건너뜀
                return;
            }

            var defines = container.GetDefineArray(context.BuildType, context.Target);
            if (defines == null)
            {
                // 해당 빌드 타입/플랫폼 설정이 없으면 기존 심볼 유지
                BuildHelper.Log($"[Defines] No define entry for {context.BuildType}/{context.Target}. Keeping existing symbols.");
                return;
            }

            // 설정은 있으나 리스트가 비어 있으면 심볼을 모두 제거한다
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(context.TargetGroup);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, defines);
            BuildHelper.Log(defines.Length > 0
                ? $"[Defines] Applied {defines.Length} define(s) from DefineContainer: {string.Join(";", defines)}"
                : "[Defines] Define entry is empty. Cleared all scripting define symbols.");
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
            if (!int.TryParse(context.VersionCode, out int vCode) || vCode <= 0)
                throw BuildHelper.ThrowError($"Android versionCode must be a positive integer: '{context.VersionCode}'");
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

            // 실행 파일은 버전별 하위 폴더(OutputPath/<이름>/<이름>.exe)에 있으므로 그 폴더만 압축하고,
            // zip은 압축 대상 밖(상위 OutputPath)에 폴더 이름으로 생성한다 (중복 빌드 시 "<이름> (2).zip")
            string buildFolder = Path.GetDirectoryName(exePath);
            string zipPath = Path.Combine(Path.GetDirectoryName(buildFolder), $"{Path.GetFileName(buildFolder)}.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            BuildHelper.Log($"[PostBuild] Creating ZIP package -> {zipPath}");
            int skipped = 0;
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.EnumerateFiles(buildFolder, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(buildFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (IsExcludedFromPackage(relative))
                    {
                        skipped++;
                        continue;
                    }
                    archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
            BuildHelper.Log($"[PostBuild] ZIP package created successfully! (excluded {skipped} debug/backup file(s))");
        }

        /// <summary>
        /// Unity가 빌드 폴더에 함께 만드는 배포 금지 폴더(Burst 디버그 정보, IL2CPP 백업)는 압축에서 제외합니다.
        /// </summary>
        protected virtual bool IsExcludedFromPackage(string relativePath)
        {
            string topFolder = relativePath.Split('\\', '/')[0];
            return topFolder.EndsWith("_DoNotShip", StringComparison.OrdinalIgnoreCase)
                || topFolder.EndsWith("_ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase);
        }
    }
}
