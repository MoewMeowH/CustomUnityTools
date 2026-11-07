using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper.Samples.Tests
{
    /// <summary>
    /// PlayMode에서는 <see cref="HBTestFixture"/>가 정리(Destroy 예약) 뒤 한 프레임 기다려,
    /// 다음 테스트가 시작될 때 이전 테스트의 객체가 남아 있지 않습니다.
    /// </summary>
    public class WaitExamples : HBTestFixture
    {
        [UnityTest]
        public IEnumerator Frames_StartRunsOnNextFrame()
        {
            var health = Scope.NewGameObject("Player", typeof(ExampleHealth)).GetComponent<ExampleHealth>();

            // Awake는 AddComponent 때 바로 돌지만 Start는 아직입니다
            Assert.AreEqual(100, health.Hp);
            Assert.IsFalse(health.IsReady);

            yield return HBWait.Frames(1);

            Assert.IsTrue(health.IsReady);
        }

        [UnityTest]
        public IEnumerator Until_WaitsForCondition()
        {
            GameObject enemy = Scope.NewGameObject("Enemy", typeof(ExampleHealth));
            var health = enemy.GetComponent<ExampleHealth>();
            health.Configure(maxHp: 10, destroyDelay: 0.2f);

            health.TakeDamage(10);
            Assert.IsTrue(health.IsDead);

            // 조건이 참이 될 때까지 프레임마다 확인합니다. 시간은 실제 시간(timeScale 무관)이고,
            // 2초가 지나면 "[HBWait.Until] 2초 안에 조건이 참이 되지 않았습니다: 죽은 적이 지워짐"으로 실패합니다.
            yield return HBWait.Until(() => enemy == null, timeoutSec: 2f, what: "죽은 적이 지워짐");

            // 이미 지워진 객체는 Scope 정리 때 건너뜁니다
        }
    }
}
