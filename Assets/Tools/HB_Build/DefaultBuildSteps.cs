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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PreBuildStep_FilterScenes";
        public virtual int ExecutionOrder => 5;
        public virtual string StepName => "SceneDefine 기반 무시 씬(IgnoreScene) 필터링 (선택 사항)";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            // UnityEngine.Object는 ?? 로 판정하지 않는다 (파괴/누락된 참조가 null로 취급되지 않음)
            var sceneDefine = context.GetCustomData<SceneDefine>("SceneDefine", null);
            if (sceneDefine == null)
                sceneDefine = SceneDefine.LoadIfExists(context);

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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PreBuildStep_PlayerSettings";
        public virtual int ExecutionOrder => 10;
        public virtual string StepName => "기본 Player & Log 설정";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            PlayerSettings.stripEngineCode = false;

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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public const string FIXED_STEP_ID = "HB.CustomBuild.Editor.PreBuildStep_ConfigDefines";
        public virtual string StepId => FIXED_STEP_ID;
        public virtual int ExecutionOrder => 20;
        public virtual string StepName => "Scripting Define Symbols (전처리기 심볼) 설정 (LIVE는 DefineContainer 항목 필수)";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            var container = context.GetCustomData<ProjectDefine>("ProjectDefine", null);
            if (container == null)
                container = ProjectDefine.LoadIfExists(context);

            // LIVE에서 심볼 설정이 없으면 커밋된 ProjectSettings의 개발용 심볼(USE_HLOG 등)이 그대로 들어가므로 실패시킨다
            if (container == null)
            {
                if (context.BuildType == EBuildType.LIVE)
                    throw BuildHelper.ThrowError("LIVE build requires a DefineContainer (ProjectDefine) asset. Without it, the scripting define symbols committed in ProjectSettings are used as-is.");

                // DEV · QA는 기존 심볼 유지하고 건너뜀
                BuildHelper.Log("[Defines] No DefineContainer. Keeping existing symbols.");
                return;
            }

            var defines = container.GetDefineArray(context.BuildType, context.Target);
            if (defines == null)
            {
                if (context.BuildType == EBuildType.LIVE)
                    throw BuildHelper.ThrowError($"LIVE build requires a define entry for {context.BuildType}/{context.Target} in DefineContainer '{container.name}'. Add the entry (an empty list clears all symbols).");

                // DEV · QA는 해당 빌드 타입/플랫폼 설정이 없으면 기존 심볼 유지
                context.AddWarning($"[Defines] No define entry for {context.BuildType}/{context.Target} in DefineContainer '{container.name}'. Keeping existing symbols (LIVE builds fail on this).");
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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PreBuildStep_AndroidSettings";
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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PreBuildStep_WindowsSettings";
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
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PreBuildStep_IOSSettings";
        public virtual int ExecutionOrder => 30;
        public virtual string StepName => "iOS 플랫폼 세부 설정 (Provisioning Profile, 빌드 번호 등)";

        public virtual bool IsActive(BuildContext context) => context.Target == BuildTarget.iOS;

        public virtual void OnPreBuild(BuildContext context)
        {
            PlayerSettings.bundleVersion = context.Version;
            PlayerSettings.iOS.buildNumber = context.VersionCode?.Trim() ?? string.Empty;

            ApplySigning(context);

            EditorUserBuildSettings.development = context.IsDevBuild;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = context.IsDeepProfile;
            EditorUserBuildSettings.connectProfiler = BuildHelper.IsAutoProfile;
        }

        /// <summary> ProvisionType 값: 자동 서명 (Unity ProvisioningProfileType에는 없는 HB_Build 전용 값) </summary>
        public const string PROVISION_TYPE_AUTO_SIGN = "AutoSign";

        /// <summary>
        /// CustomData "ProvisionType"과 "ProvisionUUID"로 서명 방식을 정합니다.
        /// ProvisionType은 Unity ProvisioningProfileType 그대로(Development / Distribution / Automatic = 프로필 종류를 Xcode 빌드 때 자동 판정,
        /// 대소문자 무시, 없으면 Distribution)이고 UUID가 있으면 자동 서명을 끄고 그 프로필로 수동 서명합니다.
        /// "AutoSign"이면 UUID 없이 자동 서명을 켭니다.
        /// </summary>
        protected virtual void ApplySigning(BuildContext context)
        {
            string provisionUuid = context.GetCustomData<string>("ProvisionUUID", null)?.Trim();
            string typeText = context.GetCustomData<string>("ProvisionType", null)?.Trim();

            if (string.Equals(typeText, PROVISION_TYPE_AUTO_SIGN, StringComparison.OrdinalIgnoreCase))
            {
                PlayerSettings.iOS.appleEnableAutomaticSigning = true;
                if (!string.IsNullOrEmpty(provisionUuid))
                    context.AddWarning($"[iOS] ProvisionType is {PROVISION_TYPE_AUTO_SIGN}, so ProvisionUUID is ignored (Xcode picks the profile).");
                return;
            }

            var profileType = ProvisioningProfileType.Distribution;
            if (!string.IsNullOrEmpty(typeText) &&
                (!Enum.TryParse(typeText, true, out profileType) || !Enum.IsDefined(typeof(ProvisioningProfileType), profileType)))
            {
                throw BuildHelper.ThrowError($"Invalid ProvisionType '{typeText}' (allowed: Development, Distribution, Automatic, {PROVISION_TYPE_AUTO_SIGN})");
            }

            if (string.IsNullOrEmpty(provisionUuid))
            {
                // 프로필을 지정하지 않으면 프로젝트의 서명 설정을 그대로 쓴다 (기존 동작)
                if (!string.IsNullOrEmpty(typeText))
                    context.AddWarning($"[iOS] ProvisionType '{typeText}' is set without ProvisionUUID. Keeping the project's signing settings.");
                return;
            }

            // 수동 프로필은 자동 서명이 켜져 있으면 Xcode가 무시하므로 끈다 (빌드 뒤 스냅샷이 원래 값으로 되돌림)
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.iOSManualProvisioningProfileType = profileType;
            PlayerSettings.iOS.iOSManualProvisioningProfileID = provisionUuid;
            BuildHelper.Log($"[iOS] Manual signing: {profileType} profile {provisionUuid}");
        }
    }

    /// <summary>
    /// 4. 사후 산출물 ZIP 압축 스텝 (PC 타깃 및 ZipPackage 옵션 활성화 시에만 동작)
    /// </summary>
    public class PostBuildStep_ZipArchive : IPostBuildStep
    {
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.PostBuildStep_ZipArchive";
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
            string zipPath = GetUniqueZipPath(Path.GetDirectoryName(buildFolder), Path.GetFileName(buildFolder));

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
        /// "&lt;이름&gt;.zip"이 이미 있으면 지우지 않고 "&lt;이름&gt; (2).zip", "&lt;이름&gt; (3).zip" ... 중 비어 있는 이름을 씁니다.
        /// </summary>
        private static string GetUniqueZipPath(string parent, string name)
        {
            string zipPath = Path.Combine(parent, name + ".zip");
            for (int i = 2; File.Exists(zipPath); i++)
                zipPath = Path.Combine(parent, $"{name} ({i}).zip");
            return zipPath;
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
