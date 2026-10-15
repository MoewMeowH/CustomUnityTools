using System;

namespace HB.Cheat
{
    /// <summary>
    /// 치트 메서드. static 메서드(public 또는 internal)에만 붙인다.
    /// Local: 반환형 void / bool / string(메시지) / CheatResult.
    /// Server: 반환형 string = 서버에 보낼 명령 문자열 (CheatCommand.Join 사용 권장).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class CheatAttribute : Attribute
    {
        /// <summary>고정 식별자. 즐겨찾기·히스토리·자동 QA가 이 값을 쓰므로 경로를 옮겨도 바꾸지 않는다.</summary>
        public string Id { get; }

        /// <summary>표시 경로. "Item/Enchant" → Item 탭의 Enchant. 마지막 마디가 치트 이름.</summary>
        public string Path { get; }

        public CheatTarget Target { get; set; } = CheatTarget.Local;
        public string Description { get; set; }

        /// <summary>같은 탭 안에서의 정렬 순서 (작을수록 앞).</summary>
        public int Order { get; set; }

        public CheatAttribute(string id, string path)
        {
            Id = id;
            Path = path;
        }
    }

    /// <summary>인자 표시 이름. 없으면 파라미터 이름을 쓴다.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class CheatArgAttribute : Attribute
    {
        public string Label { get; }

        public CheatArgAttribute(string label)
        {
            Label = label;
        }
    }

    /// <summary>숫자 인자의 범위와 스테퍼 간격.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class CheatRangeAttribute : Attribute
    {
        public double Min { get; }
        public double Max { get; }
        public double Step { get; set; } = 1;

        public CheatRangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 인자 값을 목록에서 고르게 한다. Provider는 ICheatOptionProvider 구현이며 매개변수 없는 생성자가 필요하다.
    /// DependsOn: 이 목록이 의존하는 앞 인자 이름. 앞 인자가 바뀌면 이 인자는 초기화된다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class CheatOptionsAttribute : Attribute
    {
        public Type Provider { get; }
        public string[] DependsOn { get; set; }

        public CheatOptionsAttribute(Type provider)
        {
            Provider = provider;
        }
    }
}
