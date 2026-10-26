using System;
using System.Collections;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>열거자를 직접 돌려 확인한다 (프레임 진행은 PlayMode 테스트에서)</summary>
    public class HBWaitTests
    {
        [Test]
        public void InvalidArguments_ThrowAtCall_BeforeEnumerating()
        {
            Assert.Throws<ArgumentNullException>(() => HBWait.Until(null, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBWait.Until(() => true, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBWait.Until(() => true, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBWait.Until(() => true, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBWait.Until(() => true, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBWait.Frames(-1));
        }

        [Test]
        public void Until_ImmediatelyTrue_DoesNotYield()
        {
            Assert.IsFalse(HBWait.Until(() => true, 1f).MoveNext());
        }

        [Test]
        public void Until_EvaluatesPredicateOncePerStep()
        {
            int calls = 0;
            IEnumerator e = HBWait.Until(() => ++calls >= 3, 5f);

            int steps = 0;
            while (e.MoveNext()) steps++;

            Assert.AreEqual(3, calls);
            Assert.AreEqual(2, steps);
        }

        [Test]
        public void Until_NeverTrue_FailsAfterTimeoutInRealTime()
        {
            IEnumerator e = HBWait.Until(() => false, 0.05f, "never");
            DateTime start = DateTime.UtcNow;

            var error = Assert.Throws<AssertionException>(() =>
            {
                while (e.MoveNext())
                {
                    // AssertionException이 아닌 예외라 Assert.Throws가 "다른 예외"로 실패시킨다
                    if ((DateTime.UtcNow - start).TotalSeconds > 5) throw new InvalidOperationException("시간 초과가 5초 안에 나지 않음");
                }
            });

            StringAssert.Contains("never", error.Message);
            Assert.GreaterOrEqual((DateTime.UtcNow - start).TotalSeconds, 0.04);
        }

        [Test]
        public void Until_PredicateException_Propagates()
        {
            IEnumerator e = HBWait.Until(() => throw new InvalidOperationException("predicate"), 1f);
            Assert.Throws<InvalidOperationException>(() => e.MoveNext());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void Frames_YieldsExactlyCountTimes(int count)
        {
            IEnumerator e = HBWait.Frames(count);
            int steps = 0;
            while (e.MoveNext()) steps++;
            Assert.AreEqual(count, steps);
        }
    }
}
