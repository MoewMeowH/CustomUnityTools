using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

/// <summary>
/// 프로젝트 내 모든 IPreBuildStep 및 IPostBuildStep을 TypeCache를 통해 자동 감지하고 관리하는 레지스트리입니다.
/// 상속 오버라이드(자식 클래스 우선) 및 에디터 윈도우 체크박스(On/Off) 저장을 지원합니다.
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public static class BuildStepRegistry
{
    private const string DisabledStepsPrefKey = "HB_Build_DisabledSteps";

    private static List<IPreBuildStep> s_AllPreBuildSteps;
    private static List<IPostBuildStep> s_AllPostBuildSteps;
    private static HashSet<string> s_DisabledStepIds;

    static BuildStepRegistry()
    {
        LoadDisabledStepIds();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void ResetStaticsOnDomainReload()
    {
        s_AllPreBuildSteps = null;
        s_AllPostBuildSteps = null;
        s_DisabledStepIds = null;
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
    /// 현재 context의 조건(플랫폼, 옵션 등)을 만족하고, 체크박스로 활성화된 Pre-Build 스텝만 반환
    /// </summary>
    public static List<IPreBuildStep> GetActivePreBuildSteps(BuildContext context)
    {
        return GetAllPreBuildSteps()
            .Where(s => IsStepEnabled(s.StepId) && (context == null || s.IsActive(context)))
            .ToList();
    }

    /// <summary>
    /// 현재 context의 조건(플랫폼, 옵션 등)을 만족하고, 체크박스로 활성화된 Post-Build 스텝만 반환
    /// </summary>
    public static List<IPostBuildStep> GetActivePostBuildSteps(BuildContext context)
    {
        return GetAllPostBuildSteps()
            .Where(s => IsStepEnabled(s.StepId) && (context == null || s.IsActive(context)))
            .ToList();
    }

    public static bool IsStepEnabled(string stepId)
    {
        if (s_DisabledStepIds == null)
            LoadDisabledStepIds();
        return !s_DisabledStepIds.Contains(stepId);
    }

    public static void SetStepEnabled(string stepId, bool enabled)
    {
        if (s_DisabledStepIds == null)
            LoadDisabledStepIds();

        if (enabled)
            s_DisabledStepIds.Remove(stepId);
        else
            s_DisabledStepIds.Add(stepId);

        SaveDisabledStepIds();
    }

    private static void LoadDisabledStepIds()
    {
        s_DisabledStepIds = new HashSet<string>();
        string saved = EditorPrefs.GetString(DisabledStepsPrefKey, string.Empty);
        if (!string.IsNullOrEmpty(saved))
        {
            var split = saved.Split(';');
            foreach (var s in split)
            {
                if (!string.IsNullOrEmpty(s))
                    s_DisabledStepIds.Add(s);
            }
        }
    }

    private static void SaveDisabledStepIds()
    {
        if (s_DisabledStepIds == null) return;
        string combined = string.Join(";", s_DisabledStepIds);
        EditorPrefs.SetString(DisabledStepsPrefKey, combined);
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
                throw BuildHelper.ThrowError($"PreBuild Step Failed at '{step.StepName}': {ex.Message}");
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
                throw BuildHelper.ThrowError($"PostBuild Step Failed at '{step.StepName}': {ex.Message}");
            }
        }
    }
}
