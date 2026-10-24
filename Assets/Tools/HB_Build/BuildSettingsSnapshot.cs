using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 기본 빌드 스텝이 변경하는 PlayerSettings / QualitySettings / EditorUserBuildSettings 값을 빌드 전에 저장하고 빌드 후 복원합니다.
    /// 커스텀 스텝이 이 목록 밖의 설정을 바꾸면 복원되지 않으므로, 해당 스텝에서 직접 복원해야 합니다.
    /// (Active Build Target 전환은 비용이 커서 복원하지 않습니다.)
    /// </summary>
    public sealed class BuildSettingsSnapshot
    {
        private static readonly LogType[] s_LogTypes =
        {
            LogType.Log, LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert
        };

        private BuildTargetGroup m_Group;
        private NamedBuildTarget m_NamedTarget;

        // 공통
        private bool m_StripEngineCode;
        private bool m_UsePlayerLog;
        private readonly StackTraceLogType[] m_StackTraceTypes = new StackTraceLogType[s_LogTypes.Length];
        private string m_Defines;
        private string m_BundleVersion;
        private ScriptingImplementation m_ScriptingBackend;
        private bool m_Development;
        private bool m_DeepProfiling;
        private bool m_ConnectProfiler;

        // Android
        private int m_AndroidVersionCode;
        private bool m_BuildAppBundle;
        private AndroidBuildType m_AndroidBuildType;
        private AndroidArchitecture m_AndroidArchitectures;

        // iOS
        private string m_IOSBuildNumber;
        private ProvisioningProfileType m_IOSProfileType;
        private string m_IOSProfileId;
        private bool m_IOSAutomaticSigning;

        private BuildSettingsSnapshot() { }

        public static BuildSettingsSnapshot Capture(BuildTargetGroup group)
        {
            var s = new BuildSettingsSnapshot
            {
                m_Group = group,
                m_NamedTarget = NamedBuildTarget.FromBuildTargetGroup(group),
                m_StripEngineCode = PlayerSettings.stripEngineCode,
                m_UsePlayerLog = PlayerSettings.usePlayerLog,
                m_BundleVersion = PlayerSettings.bundleVersion,
                m_Development = EditorUserBuildSettings.development,
                m_DeepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport,
                m_ConnectProfiler = EditorUserBuildSettings.connectProfiler
            };

            s.m_Defines = PlayerSettings.GetScriptingDefineSymbols(s.m_NamedTarget);
            s.m_ScriptingBackend = PlayerSettings.GetScriptingBackend(s.m_NamedTarget);

            for (int i = 0; i < s_LogTypes.Length; i++)
                s.m_StackTraceTypes[i] = PlayerSettings.GetStackTraceLogType(s_LogTypes[i]);

            if (group == BuildTargetGroup.Android)
            {
                s.m_AndroidVersionCode = PlayerSettings.Android.bundleVersionCode;
                s.m_BuildAppBundle = EditorUserBuildSettings.buildAppBundle;
                s.m_AndroidBuildType = EditorUserBuildSettings.androidBuildType;
                s.m_AndroidArchitectures = PlayerSettings.Android.targetArchitectures;
            }
            else if (group == BuildTargetGroup.iOS)
            {
                s.m_IOSBuildNumber = PlayerSettings.iOS.buildNumber;
                s.m_IOSProfileType = PlayerSettings.iOS.iOSManualProvisioningProfileType;
                s.m_IOSProfileId = PlayerSettings.iOS.iOSManualProvisioningProfileID;
                s.m_IOSAutomaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning;
            }

            return s;
        }

        /// <summary>
        /// 저장한 값을 되돌립니다. 한 항목이 실패해도 나머지는 계속 복원하고, 실패 항목을 모아 마지막에 예외로 알립니다.
        /// </summary>
        public void Restore()
        {
            var errors = new List<string>();

            TryRestore(errors, "stripEngineCode", () => PlayerSettings.stripEngineCode = m_StripEngineCode);
            TryRestore(errors, "usePlayerLog", () => PlayerSettings.usePlayerLog = m_UsePlayerLog);
            TryRestore(errors, "bundleVersion", () => PlayerSettings.bundleVersion = m_BundleVersion);
            TryRestore(errors, "development", () => EditorUserBuildSettings.development = m_Development);
            TryRestore(errors, "buildWithDeepProfilingSupport", () => EditorUserBuildSettings.buildWithDeepProfilingSupport = m_DeepProfiling);
            TryRestore(errors, "connectProfiler", () => EditorUserBuildSettings.connectProfiler = m_ConnectProfiler);

            for (int i = 0; i < s_LogTypes.Length; i++)
            {
                int index = i;
                TryRestore(errors, $"StackTraceLogType.{s_LogTypes[index]}", () => PlayerSettings.SetStackTraceLogType(s_LogTypes[index], m_StackTraceTypes[index]));
            }

            // 바뀐 경우에만 되돌린다 (같은 값을 다시 설정해도 스크립트 재컴파일이 발생할 수 있음)
            TryRestore(errors, "ScriptingDefineSymbols", () =>
            {
                if (PlayerSettings.GetScriptingDefineSymbols(m_NamedTarget) != m_Defines)
                    PlayerSettings.SetScriptingDefineSymbols(m_NamedTarget, m_Defines);
            });

            TryRestore(errors, "ScriptingBackend", () =>
            {
                if (PlayerSettings.GetScriptingBackend(m_NamedTarget) != m_ScriptingBackend)
                    PlayerSettings.SetScriptingBackend(m_NamedTarget, m_ScriptingBackend);
            });

            if (m_Group == BuildTargetGroup.Android)
            {
                TryRestore(errors, "Android.bundleVersionCode", () => PlayerSettings.Android.bundleVersionCode = m_AndroidVersionCode);
                TryRestore(errors, "buildAppBundle", () => EditorUserBuildSettings.buildAppBundle = m_BuildAppBundle);
                TryRestore(errors, "androidBuildType", () => EditorUserBuildSettings.androidBuildType = m_AndroidBuildType);
                TryRestore(errors, "Android.targetArchitectures", () => PlayerSettings.Android.targetArchitectures = m_AndroidArchitectures);
            }
            else if (m_Group == BuildTargetGroup.iOS)
            {
                TryRestore(errors, "iOS.buildNumber", () => PlayerSettings.iOS.buildNumber = m_IOSBuildNumber);
                TryRestore(errors, "iOS.ProvisioningProfileType", () => PlayerSettings.iOS.iOSManualProvisioningProfileType = m_IOSProfileType);
                TryRestore(errors, "iOS.ProvisioningProfileID", () => PlayerSettings.iOS.iOSManualProvisioningProfileID = m_IOSProfileId);
                TryRestore(errors, "iOS.appleEnableAutomaticSigning", () => PlayerSettings.iOS.appleEnableAutomaticSigning = m_IOSAutomaticSigning);
            }

            // 전역 SaveAssets는 사용자의 미저장 에셋까지 저장하므로, 복원한 설정 에셋만 저장한다
            TryRestore(errors, "Save settings assets", SaveSettingsAssets);

            if (errors.Count > 0)
                throw new InvalidOperationException($"Failed to restore {errors.Count} setting(s):\n  {string.Join("\n  ", errors)}");

            BuildHelper.Log("[Pipeline] Restored PlayerSettings / EditorUserBuildSettings to pre-build state.");
        }

        private static void TryRestore(List<string> errors, string name, Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void SaveSettingsAssets()
        {
            // EditorUserBuildSettings는 Library에 저장되는 값이라 에셋 저장 대상이 아니다
            foreach (var settings in Resources.FindObjectsOfTypeAll<PlayerSettings>())
                AssetDatabase.SaveAssetIfDirty(settings);
            foreach (var settings in Resources.FindObjectsOfTypeAll<QualitySettings>())
                AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
