using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// Unity Test Framework (UTF) EditMode 테스트 러너 연동
    /// Window > General > Test Runner 또는 Jenkins CI/CD에서 자동 실행됩니다.
    /// 에러(빈 슬롯 · Missing 참조 · Missing 스크립트)만 실패로 보고하고, 경고(배열 · List의 빈 원소)는 실패시키지 않습니다.
    /// 경고도 결과 메시지(실패 · 통과 모두)에 함께 적어 Test Runner 결과 화면에서 보이게 합니다.
    /// </summary>
    public class SlotValidationTests
    {
        [Test]
        public void Validate_AllPrefabs_NoNullInspectorSlots_And_NoMissingScripts()
        {
            var issues = SlotValidatorEngine.ScanPrefabs(new[] { "Assets" });
            var errors = issues.Where(i => i.Severity == EValidationSeverity.Error).ToList();
            var warnings = issues.Where(i => i.Severity == EValidationSeverity.Warning).ToList();

            foreach (var warning in warnings)
            {
                Debug.LogWarning($"[NeedSlotNullCheck 경고] {warning}");
            }

            var sb = new StringBuilder();
            if (errors.Count > 0)
            {
                sb.AppendLine($"[NeedSlotNullCheck 검증 실패] 총 {errors.Count}개의 필수 슬롯/스크립트 누락이 발견되었습니다:\n");
                AppendIssues(sb, errors);
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[경고] 배열 · List의 빈 원소 {warnings.Count}개 (테스트 실패 사유 아님):\n");
                    AppendIssues(sb, warnings);
                }

                Assert.Fail(sb.ToString());
            }

            sb.AppendLine("모든 프리팹의 [NeedSlotNullCheck] 슬롯 및 컴포넌트 유효성 검사를 통과했습니다.");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"[경고] 배열 · List의 빈 원소 {warnings.Count}개:\n");
                AppendIssues(sb, warnings);
            }

            Assert.Pass(sb.ToString());
        }

        private static void AppendIssues(StringBuilder sb, List<SlotValidationIssue> issues)
        {
            // [심각도][종류] 에셋 > GameObject 경로 > 컴포넌트.필드 (설명)
            foreach (var issue in issues)
            {
                sb.AppendLine($"  - {issue}");
            }
        }
    }
}
