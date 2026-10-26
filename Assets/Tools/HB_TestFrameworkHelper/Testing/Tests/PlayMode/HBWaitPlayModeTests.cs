using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper.Tests
{
    public class HBWaitPlayModeTests
    {
        [UnityTest]
        public IEnumerator Until_FinishesWhenConditionBecomesTrue()
        {
            int start = Time.frameCount;

            yield return HBWait.Until(() => Time.frameCount >= start + 3, 5f, "3프레임");

            Assert.GreaterOrEqual(Time.frameCount, start + 3);
        }

        [UnityTest]
        public IEnumerator Frames_AdvancesFrames()
        {
            int start = Time.frameCount;

            yield return HBWait.Frames(3);

            // 중첩 열거자가 끝난 뒤 같은 프레임에 재개되는지는 러너에 달려 있어 1프레임 여유를 둔다
            Assert.That(Time.frameCount - start, Is.InRange(3, 4));
        }

        [UnityTest]
        public IEnumerator Until_TimesOutInRealTime_WhenTimeScaleIsZero()
        {
            var scope = new TestScope();
            try
            {
                scope.Preserve(() => Time.timeScale, v => Time.timeScale = v);
                Time.timeScale = 0f;

                IEnumerator wait = HBWait.Until(() => false, 0.1f, "never");
                double start = Time.realtimeSinceStartupAsDouble;
                bool failed = false;
                while (true)
                {
                    bool more;
                    try
                    {
                        more = wait.MoveNext();
                    }
                    catch (AssertionException)
                    {
                        failed = true;
                        break;
                    }
                    if (!more) break;
                    if (Time.realtimeSinceStartupAsDouble - start > 5) Assert.Fail("5초 안에 시간 초과가 나지 않음");
                    yield return null;
                }

                Assert.IsTrue(failed, "timeScale이 0이어도 실제 시간으로 시간 초과가 나야 함");
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
