using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

/// <summary>
/// 프로젝트 내 모든 IPreBuildStep 및 IPostBuildStep을 TypeCache를 통해 자동 감지하고 관리하는 레지스트리입니다.
/// 상속 오버라이드(자식 클래스 우선)를 지원하며, 스텝 On/Off는 빌드 프로필(BuildContext.DisabledStepIds)로 결정됩니다.
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public static class BuildStepRegistry
{
    private static List<IPreBuildStep> s_AllPreBuildSteps;
    private static List<IPostBuildStep> s_AllPostBuildSteps;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void ResetStaticsOnDomainReload()
    {
        s_AllPreBuildSteps = null;
        s_AllPostBuildSteps = null;
    }
#endif

    public static IReadOnlyList<IPreBuildStep> GetAllPreBuildSteps()
    {
        if (s_AllPreBuildSteps == null)
            ReloadSteps();
        return s_AllPreBuildSteps;
    }

    public static IReadOnlyList<IPostBuildStep> GetAllPostBuildSteps()
    {
        if (s_AllPostBuildSteps == null)
            ReloadSteps();
        return s_AllPostBuildSteps;
    }

    /// <summary>
    /// 현재 context의 조건(플랫폼, 옵션 등)을 만족하고, context.DisabledStepIds에 없는 Pre-Build 스텝만 반환
    /// </summary>
    public static List<IPreBuildStep> GetActivePreBuildSteps(BuildContext context)
    {
        return GetAllPreBuildSteps()
            .Where(s => !context.DisabledStepIds.Contains(s.StepId) && s.IsActive(context))
            .ToList();
    }

    /// <summary>
    /// 현재 context의 조건(플랫폼, 옵션 등)을 만족하고, context.DisabledStepIds에 없는 Post-Build 스텝만 반환
    /// </summary>
    public static List<IPostBuildStep> GetActivePostBuildSteps(BuildContext context)
    {
        return GetAllPostBuildSteps()
            .Where(s => !context.DisabledStepIds.Contains(s.StepId) && s.IsActive(context))
            .ToList();
    }

    public static void ReloadSteps()
    {
        s_AllPreBuildSteps = new List<IPreBuildStep>();
        s_AllPostBuildSteps = new List<IPostBuildStep>();

        // 1. IPreBuildStep 타입 수집 및 상속 오버라이드 필터링
        var preTypes = TypeCache.GetTypesDerivedFrom<IPreBuildStep>()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        // 만약 자식 클래스가 존재하면 부모 클래스는 목록에서 제외 (상속 오버라이드)
        var filteredPreTypes = preTypes.Where(candidate =>
            !preTypes.Any(other => other != candidate && candidate.IsAssignableFrom(other))
        ).ToList();

        foreach (var type in filteredPreTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IPreBuildStep step)
                    s_AllPreBuildSteps.Add(step);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildStepRegistry] Failed to instantiate {type.Name}: {ex.Message}");
            }
        }

        // 2. IPostBuildStep 타입 수집 및 상속 오버라이드 필터링
        var postTypes = TypeCache.GetTypesDerivedFrom<IPostBuildStep>()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .ToList();

        var filteredPostTypes = postTypes.Where(candidate =>
            !postTypes.Any(other => other != candidate && candidate.IsAssignableFrom(other))
        ).ToList();

        foreach (var type in filteredPostTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IPostBuildStep step)
                    s_AllPostBuildSteps.Add(step);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildStepRegistry] Failed to instantiate {type.Name}: {ex.Message}");
            }
        }

        // 우선순위 정렬
        s_AllPreBuildSteps = s_AllPreBuildSteps.OrderBy(x => x.ExecutionOrder).ToList();
        s_AllPostBuildSteps = s_AllPostBuildSteps.OrderBy(x => x.ExecutionOrder).ToList();
    }

    public static void ExecutePreBuildSteps(BuildContext context)
    {
        var steps = GetActivePreBuildSteps(context);
        BuildHelper.Log($"[Pipeline] Executing {steps.Count} Active Pre-Build Step(s)...");

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            BuildHelper.Log($"  ↳ [Step {i + 1}/{steps.Count}] [{step.ExecutionOrder}] {step.StepName}");
            try
            {
                step.OnPreBuild(context);
            }
            catch (Exception ex)
            {
                // 원래 예외의 타입과 스택트레이스를 로그에 남긴다
                Debug.LogException(ex);
                throw BuildHelper.ThrowError($"PreBuild Step Failed at '{step.StepName}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public static void ExecutePostBuildSteps(BuildContext context)
    {
        var steps = GetActivePostBuildSteps(context);
        BuildHelper.Log($"[Pipeline] Executing {steps.Count} Active Post-Build Step(s)...");

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            BuildHelper.Log($"  ↳ [Step {i + 1}/{steps.Count}] [{step.ExecutionOrder}] {step.StepName}");
            try
            {
                step.OnPostBuild(context);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw BuildHelper.ThrowError($"PostBuild Step Failed at '{step.StepName}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
