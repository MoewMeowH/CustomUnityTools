using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Tests
{
    public class HBAssertTests
    {
        private static object s_Sink;

        [Test]
        public void AnyContains_PassesWhenSomeItemContains()
        {
            HBAssert.AnyContains(new[] { "alpha", null, "beta" }, "et");
        }

        [Test]
        public void AnyContains_Fails_ListsEveryItem()
        {
            var e = Assert.Throws<AssertionException>(() => HBAssert.AnyContains(new[] { "alpha", null, "beta" }, "zeta"));

            StringAssert.Contains("[0] alpha", e.Message);
            StringAssert.Contains("[1] <null>", e.Message);
            StringAssert.Contains("[2] beta", e.Message);
        }

        [Test]
        public void AnyContains_ComparisonIsOrdinalByDefault()
        {
            Assert.Throws<AssertionException>(() => HBAssert.AnyContains(new[] { "Alpha" }, "alpha"));
            HBAssert.AnyContains(new[] { "Alpha" }, "alpha", StringComparison.OrdinalIgnoreCase);
        }

        [Test]
        public void NoneContains_FailsWithTheMatchingIndex()
        {
            HBAssert.NoneContains(new[] { "alpha", null }, "zeta");

            var e = Assert.Throws<AssertionException>(() => HBAssert.NoneContains(new[] { "alpha", "beta" }, "bet"));
            StringAssert.Contains("[1]", e.Message);
        }

        [Test]
        public void Contains_EnumeratesInputOnlyOnce()
        {
            HBAssert.AnyContains(new OnceEnumerable("a", "b"), "b");
            Assert.Throws<AssertionException>(() => HBAssert.AnyContains(new OnceEnumerable("a", "b"), "z"));
            HBAssert.NoneContains(new OnceEnumerable("a", "b"), "z");
        }

        [Test]
        public void Contains_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => HBAssert.AnyContains(null, "a"));
            Assert.Throws<ArgumentNullException>(() => HBAssert.AnyContains(new[] { "a" }, null));
            Assert.Throws<ArgumentNullException>(() => HBAssert.NoneContains(null, "a"));
        }

        [Test]
        public void NoGCAlloc_CallsActionWarmupPlusOneTimes()
        {
            int count = 0;
            Action action = () => count++;

            HBAssert.NoGCAlloc(action, 2);

            Assert.AreEqual(3, count);
        }

        [Test]
        public void NoGCAlloc_FailsWhenTheActionAllocates()
        {
            Assert.Throws<AssertionException>(() => HBAssert.NoGCAlloc(() => s_Sink = new byte[64]));
        }

        [Test]
        public void NoGCAlloc_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => HBAssert.NoGCAlloc(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => HBAssert.NoGCAlloc(() => { }, -1));
        }

        /// <summary>두 번째 열거에서 예외를 던지는 입력</summary>
        private sealed class OnceEnumerable : IEnumerable<string>
        {
            private readonly string[] m_Items;
            private bool m_Used;

            public OnceEnumerable(params string[] items) => m_Items = items;

            public IEnumerator<string> GetEnumerator()
            {
                if (m_Used) throw new InvalidOperationException("두 번 열거함");
                m_Used = true;
                return ((IEnumerable<string>)m_Items).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
