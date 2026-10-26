using System.Collections.Generic;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// Unity Test Framework (UTF) EditMode 테스트 러너 연동
    /// Window > General > Test Runner 또는 Jenkins CI/CD에서 자동 실행됩니다.
    /// 프리팹 [NeedSlotNullCheck] 슬롯 · Missing 스크립트(검사 ID PrefabSlots)를 맡습니다. 폴더 범위는 Project Settings를 따릅니다.
    /// 에러(빈 슬롯 · Missing 참조 · Missing 스크립트) 또는 검사를 끝까지 못 한 경우만 실패하고, 경고(배열 · List의 빈 원소)는 실패시키지 않습니다.
    /// 경고도 결과 메시지(실패 · 통과 모두)에 함께 적어 Test Runner 결과 화면에서 보이게 합니다.
    /// </summary>
    public class SlotValidationTests
    {
        [Test]
        public void Validate_AllPrefabs_NoNullInspectorSlots_And_NoMissingScripts()
        {
            if (!AssetHealthSettings.IsCheckEnabled(AssetCheckIds.PrefabSlots))
                Assert.Ignore("Project Settings > HB > Test Framework Helper에서 프리팹 슬롯 검사가 꺼져 있습니다.");

            var options = AssetHealthSettings.CreateScanOptions();
            options.EnabledChecks = new List<string> { AssetCheckIds.PrefabSlots };

            var result = AssetHealthEngine.Scan(options);
            AssetHealthTestReport.Verify(result, AssetCheckIds.PrefabSlots, "NeedSlotNullCheck", "필수 슬롯/스크립트 누락");
        }
    }
}
