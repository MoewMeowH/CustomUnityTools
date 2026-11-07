using System.Collections.Generic;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Samples.Tests
{
    public class AssertExamples
    {
        [Test]
        public void NoGCAlloc_InventoryAddDoesNotAllocate()
        {
            var inventory = new ExampleInventory(capacity: 8, maxStack: 99);

            // 람다(inventory를 캡처)는 여기서 한 번 만들어지므로 측정에 들어가지 않습니다.
            // warmup 1회(기본) 부른 뒤 1회 측정 — 첫 호출의 초기화 할당은 빠집니다.
            HBAssert.NoGCAlloc(() =>
            {
                inventory.Clear();
                inventory.Add(1001, 50);
                inventory.Add(1001, 80);
                inventory.Add(2002, 1);
            });

            // 할당하는 코드(Describe는 List · 문자열을 만듦)를 넣으면 실패합니다:
            //   HBAssert.NoGCAlloc(() => inventory.Describe());
        }

        [Test]
        public void AnyContains_NoneContains_OnStringList()
        {
            var inventory = new ExampleInventory(capacity: 3, maxStack: 99);
            inventory.Add(1001, 5);

            List<string> lines = inventory.Describe();

            HBAssert.AnyContains(lines, "item 1001 x5");
            HBAssert.NoneContains(lines, "item 2002");

            // 실패하면 메시지에 목록 전체가 찍혀 무엇이 들어 있었는지 바로 보입니다:
            //   'item 9999'를 포함하는 항목이 없습니다. 항목 3개:
            //     [0] slot 0: item 1001 x5
            //     [1] slot 1: empty
            //     [2] slot 2: empty
        }
    }
}
