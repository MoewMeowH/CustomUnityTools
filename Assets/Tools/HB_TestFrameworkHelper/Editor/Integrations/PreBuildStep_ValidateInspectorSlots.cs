#if HB_CUSTOM_BUILD_ENABLED
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
        public virtual int ExecutionOrder => 1; // 가장 먼저 실행
        public virtual string StepName => "Inspector 필수 슬롯([NeedSlotNullCheck]) 사전 검증";

        public virtual bool IsActive(BuildContext context) => true;

        public virtual void OnPreBuild(BuildContext context)
        {
            BuildHelper.Log("[Validator] Starting [NeedSlotNullCheck] validation before player build...");

            var issues = SlotValidatorEngine.ScanPrefabs(new[] { "Assets" });
            if (issues.Count > 0)
            {
                string msg = $"[Inspector Validation 경고] 총 {issues.Count}개의 필수 슬롯/스크립트 누락이 발견되었습니다!";
                BuildHelper.LogWarning(msg);

                foreach (var issue in issues)
                {
                    BuildHelper.LogWarning($"  ↳ [{issue.IssueType}] {issue.AssetPath} > {issue.ComponentName}.{issue.FieldName}");
                }

                if (context.BuildType == EBuildType.LIVE)
                {
                    throw BuildHelper.ThrowError($"LIVE 배포 빌드 차단: {issues.Count}개의 인스펙터 슬롯 누락이 존재합니다. 빌드 전 수정하세요.");
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
