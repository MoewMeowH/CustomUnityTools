using System.Text;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// Unity Test Framework (UTF) EditMode 테스트 러너 연동
    /// Window > General > Test Runner 또는 Jenkins CI/CD에서 자동 실행됩니다.
    /// </summary>
    public class SlotValidationTests
    {
        [Test]
        public void Validate_AllPrefabs_NoNullInspectorSlots_And_NoMissingScripts()
        {
            var issues = SlotValidatorEngine.ScanPrefabs(new[] { "Assets" });

            if (issues.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[NeedSlotNullCheck 검증 실패] 총 {issues.Count}개의 필수 슬롯/스크립트 누락이 발견되었습니다:\n");

                foreach (var issue in issues)
                {
                    sb.AppendLine($"  - [{issue.IssueType}] {issue.AssetPath} > {issue.ComponentName}.{issue.FieldName}");
                    if (!string.IsNullOrEmpty(issue.Description))
                        sb.AppendLine($"    ↳ 설명: {issue.Description}");
                }

                Assert.Fail(sb.ToString());
            }

            Assert.Pass("모든 프리팹의 [NeedSlotNullCheck] 슬롯 및 컴포넌트 유효성 검사를 통과했습니다.");
        }
    }
}
