using UnityEngine;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>OnDisable · OnDestroy 호출 수를 센다 (PlayMode 파괴 시점 확인용)</summary>
    public class DestroyProbe : MonoBehaviour
    {
        public static int DisabledCount;
        public static int DestroyedCount;

        public static void ResetCounts()
        {
            DisabledCount = 0;
            DestroyedCount = 0;
        }

        private void OnDisable() => DisabledCount++;

        private void OnDestroy() => DestroyedCount++;
    }
}
