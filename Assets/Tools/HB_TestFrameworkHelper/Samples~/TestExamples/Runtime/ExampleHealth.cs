using UnityEngine;

namespace HB.TestFrameworkHelper.Samples
{
    /// <summary>
    /// 체력 컴포넌트입니다. 체력이 0이 되면 <c>m_DestroyDelay</c>초(게임 시간) 뒤 GameObject를 지웁니다.
    /// <para>Awake · Start · Destroy는 Play 중에만 돕니다 — EditMode 테스트에서는 <see cref="Configure"/>로 값을 넣고, 죽이는 테스트는 PlayMode에서 합니다.</para>
    /// </summary>
    public sealed class ExampleHealth : MonoBehaviour
    {
        [SerializeField] private int m_MaxHp = 100;
        [SerializeField] private float m_DestroyDelay = 0.5f;

        public int Hp { get; private set; }
        public bool IsDead => Hp <= 0;

        /// <summary>Start가 불렸는지. PlayMode에서 AddComponent한 다음 프레임에 true가 됩니다.</summary>
        public bool IsReady { get; private set; }

        private void Awake() => Hp = m_MaxHp;

        private void Start() => IsReady = true;

        public void Configure(int maxHp, float destroyDelay)
        {
            m_MaxHp = maxHp;
            m_DestroyDelay = destroyDelay;
            Hp = maxHp;
        }

        public void TakeDamage(int amount)
        {
            if (IsDead) return;
            Hp = Mathf.Max(0, Hp - amount);
            if (IsDead) Destroy(gameObject, m_DestroyDelay);
        }
    }
}
