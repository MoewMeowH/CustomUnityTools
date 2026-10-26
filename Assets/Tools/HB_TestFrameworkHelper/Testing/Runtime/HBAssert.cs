using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.TestTools.Constraints;
using Assert = NUnit.Framework.Assert;
// NUnit.Framework.Is와 이름이 겹치므로 Unity 쪽 Is만 쓴다
using Is = UnityEngine.TestTools.Constraints.Is;

namespace HB.TestFrameworkHelper
{
    public static class HBAssert
    {
        /// <summary>
        /// action이 GC 할당을 하지 않는지 확인합니다. warmup회 먼저 부른 뒤(첫 호출의 초기화 할당 제외) 1회 측정합니다 — 총 warmup + 1회 호출됩니다.
        /// <para>Unity Test Framework의 AllocatingGCMemory를 씁니다 (메인 스레드 · 프로파일러 기록 기반, 에디터에서는 에디터 기준 값).</para>
        /// </summary>
        public static void NoGCAlloc(Action action, int warmup = 1)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (warmup < 0) throw new ArgumentOutOfRangeException(nameof(warmup), warmup, "0 이상이어야 합니다.");

            WarmUpMeasurementOnce();

            // 워밍업도 측정과 같은 델리게이트로 부른다 — 감싼 델리게이트의 첫 호출 비용이 측정에 섞이지 않게
            var measured = new NUnit.Framework.TestDelegate(action);
            for (int i = 0; i < warmup; i++) measured();
            Assert.That(measured, Is.Not.AllocatingGCMemory());
        }

        private static bool s_MeasurementWarmedUp;

        // Test Framework의 측정 코드는 받은 델리게이트를 다시 람다로 감싸 측정 구간 안에서 처음 부른다.
        // 도메인에서 처음 쓸 때 그 첫 호출 비용이 할당으로 잡히는 일이 있어(6000.3 · 7000.0 실측, 가끔), 빈 동작으로 한 번 먼저 돌려 둔다 (결과는 버림).
        private static void WarmUpMeasurementOnce()
        {
            if (s_MeasurementWarmedUp) return;
            s_MeasurementWarmedUp = true;
            Is.AllocatingGCMemory().ApplyTo(new NUnit.Framework.TestDelegate(NoOp));
        }

        private static void NoOp() { }

        /// <summary>values 중 하나라도 expected를 포함하는지 확인합니다. values는 한 번만 열거하며, 실패하면 전체 목록을 메시지에 적습니다.</summary>
        public static void AnyContains(IEnumerable<string> values, string expected, StringComparison comparison = StringComparison.Ordinal)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (expected == null) throw new ArgumentNullException(nameof(expected));

            // 열거하면서 모아 두고, 찾으면 바로 끝낸다 (실패 메시지는 모은 전체 목록으로)
            var items = new List<string>();
            foreach (string item in values)
            {
                if (item != null && item.IndexOf(expected, comparison) >= 0) return;
                items.Add(item);
            }
            Assert.Fail(Describe($"'{expected}'를 포함하는 항목이 없습니다.", items));
        }

        /// <summary>values 중 어느 것도 unexpected를 포함하지 않는지 확인합니다. values는 한 번만 열거하며, 실패하면 전체 목록을 메시지에 적습니다.</summary>
        public static void NoneContains(IEnumerable<string> values, string unexpected, StringComparison comparison = StringComparison.Ordinal)
        {
            List<string> items = Snapshot(values, unexpected, nameof(unexpected));
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].IndexOf(unexpected, comparison) >= 0)
                    Assert.Fail(Describe($"'{unexpected}'를 포함하는 항목이 있습니다: [{i}]", items));
            }
        }

        private static List<string> Snapshot(IEnumerable<string> values, string text, string textName)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (text == null) throw new ArgumentNullException(textName);
            return new List<string>(values);
        }

        private static string Describe(string headline, List<string> items)
        {
            var sb = new StringBuilder(headline);
            sb.Append($" 항목 {items.Count}개:");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append($"\n  [{i}] {items[i] ?? "<null>"}");
            }
            return sb.ToString();
        }
    }
}
