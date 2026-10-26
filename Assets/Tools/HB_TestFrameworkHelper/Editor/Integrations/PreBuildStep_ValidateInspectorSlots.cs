#if HB_CUSTOM_BUILD_ENABLED
using System.Collections.Generic;
using HB.CustomBuild.Editor;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// HB Custom Build Manager 연동: 빌드 실행 전 에셋 건강 검사 (Project Settings에서 켜진 검사 · [NeedSlotNullCheck] 슬롯 · Missing 스크립트 등)
    /// com.hb.custombuild 패키지가 프로젝트에 설치되어 있을 때만 활성화됩니다.
    /// 씬은 Project Settings의 씬 대상 대신 실제 빌드 씬 목록(BuildContext.Scenes, SceneDefine 필터 뒤)을 검사합니다.
    /// </summary>
    public class PreBuildStep_ValidateInspectorSlots : IPreBuildStep
    {
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다
        public virtual string StepId => "HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots";
        public virtual int ExecutionOrder => 6; // SceneDefine 씬 필터(5) 뒤 — 실제 빌드 씬으로 검사
        public virtual string StepName => "Inspector 필수 슬롯([NeedSlotNullCheck]) 사전 검증";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            BuildHelper.Log("[Validator] Starting asset health validation before player build...");

            var options = AssetHealthSettings.CreateScanOptions();
            options.OverrideScenePaths = context.Scenes != null ? new List<string>(context.Scenes) : new List<string>();
            var result = AssetHealthEngine.Scan(options);

            int errorCount = result.ErrorCount;
            bool live = context.BuildType == EBuildType.LIVE;

            // 검사를 끝까지 못 했으면(로드 실패 · 씬을 못 엶 · 설정 오류) 문제가 0개여도 통과로 보지 않는다
            if (!result.IsComplete)
            {
                string failSummary = $"[Inspector Validation] 검사를 끝까지 하지 못했습니다 (실패 {result.Failures.Count}개{(result.Canceled ? " · 취소" : string.Empty)})";
                context.AddWarning(failSummary);
                foreach (var failure in result.Failures)
                {
                    BuildHelper.LogWarning($"  ↳ {failure}");
                }
            }

            var issues = result.Issues;
            if (issues.Count > 0)
            {
                // 경고(배열 · List의 빈 원소, 텍스처 · 일반 Missing 참조)는 로그만, 에러만 LIVE 빌드를 막는다
                string summary = $"[Inspector Validation] 문제 {issues.Count}개 (에러 {errorCount} · 경고 {issues.Count - errorCount})";

                // 에러가 있으면 HB_Build 경고 파일(HB_BUILD_WARNINGS.txt → Jenkins UNSTABLE)에 요약만 남긴다. 경고만 있으면 로그만.
                // 항목별 상세는 경고 파일이 에셋 수만큼 길어지지 않게 로그로만 남긴다
                if (errorCount > 0)
                {
                    context.AddWarning(summary);
                }
                else
                {
                    BuildHelper.LogWarning(summary);
                }

                foreach (var issue in issues)
                {
                    BuildHelper.LogWarning($"  ↳ {issue}");
                }
            }
            else if (result.IsComplete)
            {
                BuildHelper.Log("[Validator] All asset health checks passed validation successfully!");
            }

            if (live && !result.IsComplete)
            {
                throw BuildHelper.ThrowError($"LIVE 배포 빌드 차단: 에셋 건강 검사를 끝까지 하지 못했습니다 (실패 {result.Failures.Count}개). 로그를 확인하세요.");
            }
            if (live && errorCount > 0)
            {
                throw BuildHelper.ThrowError($"LIVE 배포 빌드 차단: 에러 {errorCount}개(빈 슬롯 · Missing 참조 · Missing 스크립트 · 셰이더)가 있습니다. 빌드 전 수정하세요.");
            }
        }
    }
}
#endif
