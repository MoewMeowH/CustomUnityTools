using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.CustomBuild.Editor
{
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
        private static List<string> s_InstantiationErrors = new List<string>();
        private static List<string> s_DuplicateStepIdErrors = new List<string>();

        /// <summary> 마지막 ReloadSteps에서 인스턴스를 만들지 못한 스텝 (빌드 경로에서는 실패 처리) </summary>
        public static IReadOnlyList<string> InstantiationErrors => s_InstantiationErrors;

        /// <summary>
        /// 마지막 ReloadSteps에서 같은 StepId로 둘 이상 등록된 스텝 (예: 같은 기본 스텝을 덮어쓴 형제 클래스 둘 — 둘 다 돌고 하나만 끌 수 없음).
        /// 빌드 경로에서는 실패 처리
        /// </summary>
        public static IReadOnlyList<string> DuplicateStepIdErrors => s_DuplicateStepIdErrors;

    #if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ResetStaticsOnDomainReload()
        {
            s_AllPreBuildSteps = null;
            s_AllPostBuildSteps = null;
            s_InstantiationErrors = new List<string>();
            s_DuplicateStepIdErrors = new List<string>();
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

        /// <summary>
        /// 주어진 ID 중 등록된 Pre/Post 스텝 어디에도 없는 ID를 반환합니다 (스텝 이름 변경 · 삭제, 오타).
        /// </summary>
        public static List<string> GetUnknownStepIds(IEnumerable<string> stepIds)
        {
            var knownIds = new HashSet<string>(
                GetAllPreBuildSteps().Select(s => s.StepId)
                    .Concat(GetAllPostBuildSteps().Select(s => s.StepId)));

            return stepIds.Where(id => !string.IsNullOrWhiteSpace(id) && !knownIds.Contains(id)).Distinct().ToList();
        }

        public static void ReloadSteps()
        {
            s_AllPreBuildSteps = new List<IPreBuildStep>();
            s_AllPostBuildSteps = new List<IPostBuildStep>();
            s_InstantiationErrors = new List<string>();
            s_DuplicateStepIdErrors = new List<string>();

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
                    // Activator는 생성자 예외를 TargetInvocationException으로 감싸므로 원래 예외를 꺼낸다
                    var cause = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    Debug.LogError($"[BuildStepRegistry] Failed to instantiate {type.Name}: {cause.GetType().Name}: {cause.Message}");
                    s_InstantiationErrors.Add($"{type.FullName}: {cause.GetType().Name}: {cause.Message}");
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
                    // Activator는 생성자 예외를 TargetInvocationException으로 감싸므로 원래 예외를 꺼낸다
                    var cause = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    Debug.LogError($"[BuildStepRegistry] Failed to instantiate {type.Name}: {cause.GetType().Name}: {cause.Message}");
                    s_InstantiationErrors.Add($"{type.FullName}: {cause.GetType().Name}: {cause.Message}");
                }
            }

            // 비활성 목록은 Pre/Post 구분 없이 ID로 끄므로 둘을 합쳐 검사한다
            // (한 클래스가 Pre · Post를 함께 구현하면 두 목록에 같은 타입으로 들어가므로 서로 다른 타입만 센다)
            s_DuplicateStepIdErrors = s_AllPreBuildSteps.Select(s => (s.StepId, Type: s.GetType()))
                .Concat(s_AllPostBuildSteps.Select(s => (s.StepId, Type: s.GetType())))
                .GroupBy(x => x.StepId)
                .Select(g => (Id: g.Key, Types: g.Select(x => x.Type).Distinct().ToList()))
                .Where(g => g.Types.Count > 1)
                .Select(g => $"'{g.Id}' ({string.Join(", ", g.Types.Select(t => t.FullName))})")
                .ToList();
            foreach (var error in s_DuplicateStepIdErrors)
                Debug.LogError($"[BuildStepRegistry] Duplicate step id {error}");

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
}
