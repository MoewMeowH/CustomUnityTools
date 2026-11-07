using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper.Samples.Tests
{
    /// <summary>프로젝트에 이미 공통 테스트 베이스가 있다고 가정한 클래스입니다.</summary>
    public abstract class ExampleProjectTestBase
    {
        protected const int DefaultCapacity = 4;
    }

    /// <summary>
    /// 다른 베이스를 이미 상속해 <see cref="HBTestFixture"/>를 쓸 수 없을 때는 <see cref="TestScope"/>를 직접 만듭니다.
    /// 짧은 구간만 정리하고 싶을 때는 <c>using</c> 블록으로 씁니다.
    /// </summary>
    public class TestScopeExamples : ExampleProjectTestBase
    {
        private TestScope m_Scope;

        [SetUp]
        public void SetUp() => m_Scope = new TestScope();

        [TearDown]
        public void TearDown() => m_Scope.Dispose();

        [Test]
        public void FieldScope_WorksLikeFixture()
        {
            GameObject enemy = m_Scope.NewGameObject("Enemy", typeof(ExampleHealth));
            enemy.GetComponent<ExampleHealth>().Configure(maxHp: 50, destroyDelay: 0f);

            var inventory = new ExampleInventory(DefaultCapacity, maxStack: 99);
            inventory.Add(1001, 1);

            Assert.AreEqual(50, enemy.GetComponent<ExampleHealth>().Hp);
            Assert.AreEqual(1, inventory.CountOf(1001));
        }

        [Test]
        public void UsingBlock_CleansUpAtEndOfBlock()
        {
            GameObject go;
            ExampleItemConfig config;
            using (var scope = new TestScope())
            {
                go = scope.NewGameObject("Temp");
                config = scope.NewInstance<ExampleItemConfig>();
                Assert.IsTrue(go != null);
            }

            // EditMode는 DestroyImmediate라 블록이 끝나면 바로 지워져 있습니다 (PlayMode는 다음 프레임에 지워짐)
            Assert.IsTrue(go == null);
            Assert.IsTrue(config == null);
        }

        [Test]
        public void Cleanup_RunsInReverseOrder()
        {
            var order = new List<string>();
            using (var scope = new TestScope())
            {
                scope.Defer(() => order.Add("first registered"));
                scope.Defer(() => order.Add("second registered"));
            }

            // 나중에 등록한 것이 먼저 정리됩니다 (스택)
            CollectionAssert.AreEqual(new[] { "second registered", "first registered" }, order);
        }

        [Test]
        public void Track_Component_RemovesOnlyThatComponent()
        {
            GameObject owner = m_Scope.NewGameObject("Owner");
            using (var inner = new TestScope())
            {
                inner.Track(owner.AddComponent<ExampleHealth>());
            }

            // Component를 Track하면 그 Component만 지워지고 GameObject는 남습니다
            Assert.IsTrue(owner != null);
            Assert.IsTrue(owner.GetComponent<ExampleHealth>() == null);
        }
    }
}
