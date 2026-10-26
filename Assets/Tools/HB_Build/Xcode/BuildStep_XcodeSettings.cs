using System;
using System.IO;
using UnityEditor;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// iOS Xcode 설정 적용 스텝 (iOS 타깃 + XcodeSettings 에셋이 있을 때만).
    /// 빌드 전: 계획을 세워 충돌 · 주인 위반을 미리 실패시키고, 적용 코드가 컴파일돼 있는지 확인한다 (BuildPlayer 전에 멈춤).
    /// 빌드 뒤: 생성된 Xcode 프로젝트의 실제 구성 · 타깃으로 다시 계획하고 적용한다.
    /// </summary>
    public class BuildStep_XcodeSettings : IPreBuildStep, IPostBuildStep
    {
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다 (파생 클래스도 같은 ID를 이어받음)
        public virtual string StepId => "HB.CustomBuild.Editor.BuildStep_XcodeSettings";
        // 빌드 전에는 iOS 설정(30) 뒤, 빌드 뒤에는 ZIP(100) 앞
        public virtual int ExecutionOrder => 40;
        public virtual string StepName => "iOS Xcode 설정 적용 (XcodeSettings 에셋, 선택 사항)";

        public const string CUSTOM_DATA_KEY = "XcodeSettings";

        /// <summary> Xcode API(iOS Build Support 모듈)를 쓰는 적용 코드가 이 컴파일본에 들어 있는지 </summary>
        public static bool ApplierCompiled
        {
            get
            {
#if UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        public virtual bool IsActive(BuildContext context) => context.Target == BuildTarget.iOS;

        public virtual void OnPreBuild(BuildContext context)
        {
            var settings = LoadSettings(context);
            if (settings == null)
            {
                BuildHelper.Log("[Xcode] No XcodeSettings asset. Skipping.");
                return;
            }

            var snapshot = XcodeSettingsSnapshot.Capture(settings);
            var plan = XcodeSettingsPlanner.Plan(snapshot, CreateInput(context));
            Report(plan, context, settings);
            if (!plan.HasChanges)
            {
                BuildHelper.Log($"[Xcode] '{settings.name}' has nothing to apply for {context.BuildType}.");
                return;
            }

            // 조용히 건너뛰지 않는다: 모듈이 없거나, 같은 호출 안에서 iOS로 막 전환해 이전 플랫폼 컴파일본이 돌고 있는 경우
            if (!ApplierCompiled)
            {
                throw BuildHelper.ThrowError(
                    $"XcodeSettings '{settings.name}' has changes to apply, but the Xcode apply code is not compiled (UNITY_IOS is not defined). " +
                    "Check that iOS Build Support is installed. If the active platform was just switched to iOS, wait for script compilation to finish and build again.");
            }
            // BuildPlayer가 쓰지 않는 에셋을 정리하면 넘겨받은 Unity 객체 참조가 null이 될 수 있다 (디스크 에셋 · 메모리 객체 모두 확인함).
            // 그래서 점검한 내용을 순수 C# 스냅샷으로 넘기고, 빌드 뒤에는 이것만 적용한다 (다른 에셋을 다시 찾지 않음)
            context.SetCustomData(SNAPSHOT_KEY, snapshot);
            BuildHelper.Log($"[Xcode] '{settings.name}' plan checked: {plan.Describe().Count} change(s) to apply after the build.");
        }

        private const string SNAPSHOT_KEY = "XcodeSettings.Snapshot";

        public virtual void OnPostBuild(BuildContext context)
        {
            // 빌드 전 점검을 통과한(적용할 것이 있는) 설정만 적용한다
            var snapshot = context.GetCustomData<XcodeSettingsSnapshot>(SNAPSHOT_KEY, null);
            if (snapshot == null) return;

            string xcodeRoot = context.PlayerOptions.locationPathName;
            if (string.IsNullOrEmpty(xcodeRoot) || !Directory.Exists(xcodeRoot))
                throw BuildHelper.ThrowError($"[Xcode] Xcode project folder not found: '{xcodeRoot}'.");

#if UNITY_IOS
            var input = CreateInput(context);
            XcodeSettingsApplier.ReadFacts(xcodeRoot, input);
            var plan = XcodeSettingsPlanner.Plan(snapshot, input);
            // 빌드 전 점검과 같은 경고는 다시 쌓지 않는다 (오류만 확인)
            if (plan.Errors.Count > 0)
                throw BuildHelper.ThrowError($"[Xcode] '{snapshot.Name}' cannot be applied to this Xcode project:\n  - {string.Join("\n  - ", plan.Errors)}");
            if (!plan.HasChanges) return;

            var warnings = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<string> log;
            try
            {
                log = XcodeSettingsApplier.Apply(plan, xcodeRoot, warnings);
            }
            catch (InvalidOperationException ex)
            {
                throw BuildHelper.ThrowError($"[Xcode] {ex.Message}");
            }
            foreach (var w in warnings) context.AddWarning(w);
            BuildHelper.Log($"[Xcode] Applied '{snapshot.Name}' to {xcodeRoot} ({log.Count} change(s)):\n  {string.Join("\n  ", log)}");
#else
            throw BuildHelper.ThrowError("[Xcode] The Xcode apply code is not compiled (UNITY_IOS is not defined). This should have failed before the build.");
#endif
        }

        protected virtual HBXcodeSettings LoadSettings(BuildContext context)
        {
            // 프로필 · 창이 넘긴 값이 있으면 그것만 쓴다. 깨진 참조(Missing)나 다른 타입(CustomEntries의 같은 키)을 조용히 건너뛰지 않는다
            if (context.CustomData.TryGetValue(CUSTOM_DATA_KEY, out object value))
            {
                if (value is not HBXcodeSettings settings)
                    throw BuildHelper.ThrowError($"[Xcode] CustomData '{CUSTOM_DATA_KEY}' is a {value?.GetType().Name ?? "null"}, not an HBXcodeSettings asset (CustomEntries must not use this key).");
                // UnityEngine.Object는 == null로 판정한다 (참조는 있는데 null이면 삭제 · Missing)
                if (settings == null)
                    throw BuildHelper.ThrowError("[Xcode] The build profile references an XcodeSettings asset that is missing (deleted or broken reference). Fix the reference or clear the slot.");
                return settings;
            }
            return HBXcodeSettings.LoadIfExists(context);
        }

        private static XcodePlanInput CreateInput(BuildContext context) => new XcodePlanInput
        {
            BuildType = context.BuildType,
            ProjectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath),
            ProvisionType = context.GetCustomData<string>("ProvisionType", null),
        };

        private static void Report(XcodePlan plan, BuildContext context, HBXcodeSettings settings)
        {
            foreach (var w in plan.Warnings)
                context.AddWarning($"[Xcode] {w}");
            if (plan.Errors.Count > 0)
                throw BuildHelper.ThrowError($"[Xcode] XcodeSettings '{settings.name}' has {plan.Errors.Count} error(s):\n  - {string.Join("\n  - ", plan.Errors)}");
        }
    }
}
