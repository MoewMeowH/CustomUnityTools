using UnityEngine;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>에셋 건강 검사 테스트용: 슬롯 칸(Slot)과 어트리뷰트 없는 칸(Plain). GameObject에 붙여야 해서 에디터 전용이 아닌 어셈블리에 둔다.</summary>
    public class SlotProbeComponent : MonoBehaviour
    {
        [NeedSlotNullCheck] public Texture2D Slot;
        public Texture2D Plain;
    }
}
