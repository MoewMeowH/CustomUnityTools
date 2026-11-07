using UnityEngine;

namespace HB.TestFrameworkHelper.Samples
{
    /// <summary>아이템 설정 ScriptableObject입니다. 테스트에서는 <c>Scope.NewInstance</c>로 만들거나 임시 에셋 폴더에 에셋으로 저장합니다.</summary>
    public sealed class ExampleItemConfig : ScriptableObject
    {
        [SerializeField] private int m_MaxStack = 99;

        public int MaxStack
        {
            get => m_MaxStack;
            set => m_MaxStack = value;
        }
    }
}
