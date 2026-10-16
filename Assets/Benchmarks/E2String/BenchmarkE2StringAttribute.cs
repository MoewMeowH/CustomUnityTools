using System;

namespace HB.E2String.Benchmarks
{
    // 벤치마크 asmdef는 Assembly-CSharp의 어트리뷰트를 참조할 수 없어 같은 형태를 따로 둔다.
    // 생성기는 이름(E2StringAttribute)과 옵션 값만 보므로 네임스페이스가 달라도 동작한다.

    [Flags]
    public enum E2StringOptions
    {
        ParseExact = 0,
        IntConversion = 1 << 1,
        Collections = 1 << 2,
        All = ~0,
    }

    [AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
    public sealed class E2StringAttribute : Attribute
    {
        public E2StringAttribute(E2StringOptions options = E2StringOptions.ParseExact) { Options = options; }
        public E2StringOptions Options { get; }
    }
}
