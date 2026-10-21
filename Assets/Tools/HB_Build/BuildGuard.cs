using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// HB_Build 파이프라인(AbsBuild.Execute) 안에서 시작한 플레이어 빌드인지 추적합니다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class BuildGuard
    {
        private static int s_ActiveScopes;

        public static bool IsHBBuildRunning => s_ActiveScopes > 0;

        public static IDisposable HBBuildScope() => new Scope();

        private sealed class Scope : IDisposable
        {
            private bool m_Disposed;

            public Scope() => s_ActiveScopes++;

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                s_ActiveScopes--;
            }
        }
    }

    /// <summary>
    /// Unity 기본 빌드(File > Build 등)처럼 HB_Build를 거치지 않은 플레이어 빌드에 경고를 남깁니다. 빌드를 막지는 않습니다.
    /// (HB_Build 스텝 — 심볼 설정, 검증, 설정 복원 — 이 실행되지 않으므로 Jenkins 빌드와 결과가 달라질 수 있음)
    /// </summary>
    internal sealed class ExternalBuildWarning : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildGuard.IsHBBuildRunning) return;

            BuildHelper.LogWarning("==========================================================================");
            BuildHelper.LogWarning("[Build] This player build was not started by HB_Build. HB_Build steps (defines, validation, settings restore) will NOT run.");
            BuildHelper.LogWarning("        Use Tools > HB > Custom Build Manager (or -executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild) for team builds.");
            BuildHelper.LogWarning("==========================================================================");
        }
    }
}
