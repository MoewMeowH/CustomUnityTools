using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper.Samples.Tests
{
    /// <summary>
    /// <see cref="HBTestFixture"/>를 상속하면 테스트마다 <c>Scope</c>가 새로 생기고,
    /// 테스트가 끝나면(실패해도) Scope에 등록한 것을 등록 역순으로 정리합니다.
    /// </summary>
    public class FixtureExamples : HBTestFixture
    {
        private ExampleItemConfig m_Config;

        // 파생 클래스의 [SetUp]에서도 Scope를 쓸 수 있습니다 (베이스의 [UnitySetUp]이 먼저 돌아 Scope를 만듦).
        // 여기서 만든 객체도 테스트가 끝나면 같이 정리됩니다 — [TearDown]을 따로 쓰지 않아도 됩니다.
        [SetUp]
        public void SetUp()
        {
            m_Config = Scope.NewInstance<ExampleItemConfig>();
            m_Config.MaxStack = 10;
        }

        [Test]
        public void NewInstance_FromSetUp_IsUsableInTest()
        {
            var inventory = new ExampleInventory(capacity: 2, maxStack: m_Config.MaxStack);

            int left = inventory.Add(1001, 25);

            Assert.AreEqual(5, left, "10 + 10을 넣고 5개가 남아야 함");
            Assert.AreEqual(20, inventory.CountOf(1001));
        }

        [Test]
        public void NewGameObject_WithComponents()
        {
            // 컴포넌트까지 붙여 만들고, 테스트가 끝나면 지웁니다 (DestroyImmediate를 직접 부르지 않아도 됨)
            GameObject player = Scope.NewGameObject("Player", typeof(ExampleHealth), typeof(BoxCollider));
            var health = player.GetComponent<ExampleHealth>();

            // EditMode에서는 Awake가 돌지 않으므로 값을 직접 넣습니다
            health.Configure(maxHp: 100, destroyDelay: 0f);
            health.TakeDamage(30);

            Assert.AreEqual(70, health.Hp);
            Assert.IsNotNull(player.GetComponent<BoxCollider>());
        }

        [Test]
        public void Track_ObjectsCreatedElsewhere()
        {
            // Scope 밖에서 만든 객체(게임 코드가 만든 Texture · Material · Instantiate 결과 등)도 Track으로 맡기면 같이 정리됩니다
            var texture = Scope.Track(new Texture2D(4, 4));
            GameObject clone = Scope.Track(Object.Instantiate(Scope.NewGameObject("Original")));

            Assert.AreEqual(4, texture.width);
            Assert.AreEqual("Original(Clone)", clone.name);
        }

        [Test]
        public void Preserve_RestoresValuesAfterTest()
        {
            // 지금 값을 기억했다가 테스트가 끝나면 되돌립니다 → 다른 테스트로 값이 새지 않음
            Scope.Preserve(() => ExampleGameSettings.GameSpeed, v => ExampleGameSettings.GameSpeed = v);
            Scope.Preserve(() => ExampleGameSettings.PlayerName, v => ExampleGameSettings.PlayerName = v);
            Scope.Preserve(() => Time.timeScale, v => Time.timeScale = v);   // Unity 값도 같은 방식

            ExampleGameSettings.GameSpeed = 2f;
            ExampleGameSettings.PlayerName = "Tester";
            Time.timeScale = 0.5f;

            Assert.AreEqual(2f, ExampleGameSettings.GameSpeed);

            // 주의: 얕은 복원입니다. 값 · 참조를 다시 넣을 뿐 같은 객체의 내부 변경(list.Add 등)은 되돌리지 않습니다.
        }

        [Test]
        public void Defer_UnsubscribesEvenIfTestFails()
        {
            var messages = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => messages.Add(condition);

            Application.logMessageReceived += handler;
            // 아래 Assert가 실패해도 테스트 정리 때 구독이 풀립니다 (try/finally를 쓰지 않아도 됨)
            Scope.Defer(() => Application.logMessageReceived -= handler);

            Debug.Log("[Inventory] item 1001 added");

            HBAssert.AnyContains(messages, "item 1001");
        }
    }
}
