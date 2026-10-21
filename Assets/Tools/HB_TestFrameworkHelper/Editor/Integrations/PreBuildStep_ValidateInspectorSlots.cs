#if HB_CUSTOM_BUILD_ENABLED
using HB.CustomBuild.Editor;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// HB Custom Build Manager 연동: 빌드 실행 전 인스펙터 슬롯([NeedSlotNullCheck]) 및 Missing Script 사전 검증
    /// com.hb.custombuild 패키지가 프로젝트에 설치되어 있을 때만 활성화됩니다.
    /// </summary>
    public class PreBuildStep_ValidateInspectorSlots : IPreBuildStep
    {
        // 클래스 이름이 바뀌어도 프로필의 비활성 목록이 깨지지 않도록 ID를 고정한다
        public virtual string StepId => "HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots";
        public virtual int ExecutionOrder => 1; // 가장 먼저 실행
        public virtual string StepName => "Inspector 필수 슬롯([NeedSlotNullCheck]) 사전 검증";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            BuildHelper.Log("[Validator] Starting [NeedSlotNullCheck] validation before player build...");

            var issues = SlotValidatorEngine.ScanPrefabs(new[] { "Assets" });
            if (issues.Count > 0)
            {
                // 경고(배열 · List의 빈 원소)는 로그만, 에러(빈 슬롯 · Missing 스크립트)만 LIVE 빌드를 막는다
                int errorCount = 0;
                foreach (var issue in issues)
                {
                    if (issue.Severity == EValidationSeverity.Error) errorCount++;
                }

                string msg = $"[Inspector Validation 경고] 총 {issues.Count}개(에러 {errorCount}개)의 필수 슬롯/스크립트 문제가 발견되었습니다!";
                BuildHelper.LogWarning(msg);

                foreach (var issue in issues)
                {
                    BuildHelper.LogWarning($"  ↳ [{issue.Severity}][{issue.IssueType}] {issue.AssetPath} > {issue.ComponentName}.{issue.FieldName}");
                }

                if (errorCount > 0 && context.BuildType == EBuildType.LIVE)
                {
                    throw BuildHelper.ThrowError($"LIVE 배포 빌드 차단: {errorCount}개의 인스펙터 슬롯 누락이 존재합니다. 빌드 전 수정하세요.");
                }
            }
            else
            {
                BuildHelper.Log("[Validator] All Inspector slots passed validation successfully!");
            }
        }
    }
}
#endif
