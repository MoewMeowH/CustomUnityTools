using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper
{
    /// <summary>[UnityTest]에서 <c>yield return HBWait.Until(...)</c>로 쓰는 대기 도우미입니다. 잘못된 인자는 부르는 즉시 예외를 던집니다.</summary>
    public static class HBWait
    {
        /// <summary>
        /// predicate가 참이 될 때까지 프레임마다 한 번 확인합니다. 처음에 참이면 기다리지 않습니다.
        /// <para>timeoutSec는 실제 시간(Time.timeScale과 무관)입니다. 러너가 다음 프레임에 재개할 때 확인하므로 "최소 대기 시간"이며 그 시각에 정확히 끊기지 않습니다.
        /// 시간이 지나면 Assert.Fail. predicate의 예외는 그대로 전달됩니다.</para>
        /// </summary>
        public static IEnumerator Until(Func<bool> predicate, float timeoutSec = 5f, string what = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (float.IsNaN(timeoutSec) || float.IsInfinity(timeoutSec) || timeoutSec <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeoutSec), timeoutSec, "0보다 큰 유한한 값이어야 합니다.");

            return UntilCore(predicate, timeoutSec, what);
        }

        /// <summary>정확히 count번 <c>yield return null</c> 합니다 (0이면 기다리지 않음).</summary>
        public static IEnumerator Frames(int count = 1)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "0 이상이어야 합니다.");
            return FramesCore(count);
        }

        private static IEnumerator UntilCore(Func<bool> predicate, float timeoutSec, string what)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            while (!predicate())
            {
                if (Time.realtimeSinceStartupAsDouble - start >= timeoutSec)
                    Assert.Fail($"[HBWait.Until] {timeoutSec:0.###}초 안에 조건이 참이 되지 않았습니다: {what ?? "조건"}");
                yield return null;
            }
        }

        private static IEnumerator FramesCore(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }
    }
}
