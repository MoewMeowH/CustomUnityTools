using System.Collections.Generic;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// 프리팹 슬롯 밖의 에셋 건강 검사(씬 · ScriptableObject · 머티리얼 · 일반 Missing 참조)를 검사마다 테스트 한 줄로 보여 줍니다.
    /// 목록은 고정이고, Project Settings에서 꺼진 검사는 Ignore로 표시합니다 (목록을 만들 때 에셋을 열지 않음).
    /// </summary>
    public class AssetHealthTests
    {
        private static IEnumerable<string> AdditionalChecks() => AssetCheckIds.AdditionalChecks;

        [Test, TestCaseSource(nameof(AdditionalChecks))]
        public void Validate_AssetHealth(string checkId)
        {
            if (!AssetHealthSettings.IsCheckEnabled(checkId))
                Assert.Ignore($"Project Settings > HB > Test Framework Helper에서 '{checkId}' 검사가 꺼져 있습니다.");

            var options = AssetHealthSettings.CreateScanOptions();
            options.EnabledChecks = new List<string> { checkId };

            // 함께 돌아야 결과가 설정과 같아지는 검사: 씬 안 프리팹 인스턴스는 원본 프리팹 보고와 대조하고,
            // 일반 Missing 참조는 켜진 슬롯 검사가 맡는 칸을 빼므로 그 슬롯 검사도 같이 돌린다 (결과는 이 검사 것만 본다)
            foreach (string id in Companions(checkId))
            {
                if (AssetHealthSettings.IsCheckEnabled(id)) options.EnabledChecks.Add(id);
            }

            var result = AssetHealthEngine.Scan(options);
            AssetHealthTestReport.Verify(result, checkId, checkId, "문제");
        }

        private static IEnumerable<string> Companions(string checkId)
        {
            switch (checkId)
            {
                case AssetCheckIds.Scenes:
                    return new[] { AssetCheckIds.PrefabSlots };
                case AssetCheckIds.MissingReferences:
                    return new[] { AssetCheckIds.PrefabSlots, AssetCheckIds.Scenes, AssetCheckIds.ScriptableObjects };
                default:
                    return new string[0];
            }
        }
    }
}
