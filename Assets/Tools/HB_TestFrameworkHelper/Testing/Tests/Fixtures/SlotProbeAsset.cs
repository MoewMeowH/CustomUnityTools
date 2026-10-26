using UnityEngine;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>에셋 건강 검사 테스트용 ScriptableObject: 슬롯 칸(Slot)과 어트리뷰트 없는 칸(Plain). 일반 Missing 순회가 건너뛰는 데이터(Numbers · Text)를 Plain 앞에 둔다.</summary>
    public class SlotProbeAsset : ScriptableObject
    {
        [NeedSlotNullCheck] public Texture2D Slot;
        public int[] Numbers = { 1, 2, 3 };
        public string Text = "probe";
        public Texture2D Plain;
    }
}
