using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>검사 결과를 테스트 판정 · 메시지로 바꾼다. 검사를 끝까지 못 했으면 문제가 0개여도 실패.</summary>
    internal static class AssetHealthTestReport
    {
        public static void Verify(AssetHealthScanResult result, string checkId, string tag, string noun)
        {
            var errors = new List<SlotValidationIssue>();
            var warnings = new List<SlotValidationIssue>();
            foreach (var issue in result.Issues)
            {
                if (issue.CheckId != checkId) continue;
                if (issue.Severity == EValidationSeverity.Error) errors.Add(issue);
                else warnings.Add(issue);
            }

            foreach (var warning in warnings)
            {
                Debug.LogWarning($"[{tag} 경고] {warning}");
            }

            var sb = new StringBuilder();
            if (!result.IsComplete)
            {
                sb.AppendLine($"[{tag} 검사 미완료] 검사를 끝까지 하지 못했습니다{(result.Canceled ? " (취소)" : string.Empty)} — 실패 {result.Failures.Count}개:\n");
                foreach (var failure in result.Failures)
                {
                    sb.AppendLine($"  - {failure}");
                }
                sb.AppendLine();
            }

            if (errors.Count > 0)
            {
                sb.AppendLine($"[{tag} 검증 실패] 총 {errors.Count}개의 {noun}가 발견되었습니다:\n");
                AppendIssues(sb, errors);
            }

            if (sb.Length > 0)
            {
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[경고] {warnings.Count}개 (테스트 실패 사유 아님):\n");
                    AppendIssues(sb, warnings);
                }
                Assert.Fail(sb.ToString());
            }

            sb.AppendLine($"[{tag}] 유효성 검사를 통과했습니다 (검사한 파일 · 씬 {result.ScannedTargetCount}개).");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"[경고] {warnings.Count}개:\n");
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
