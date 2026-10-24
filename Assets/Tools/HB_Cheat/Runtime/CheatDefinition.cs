using System;

namespace HB.Cheat
{
    /// <summary>
    /// 치트 인자 정의. 생성 코드(레지스트리)가 만든다.
    /// </summary>
    public sealed class CheatArgDefinition
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public CheatValueType ValueType { get; set; }
        public CheatEditorKind Editor { get; set; }
        public Type EnumType { get; set; }

        public bool HasRange { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Step { get; set; } = 1;

        public object DefaultValue { get; set; }

        /// <summary>목록 Provider (없으면 null)</summary>
        public ICheatOptionProvider Provider { get; set; }

        /// <summary>고정 목록 (enum). Provider보다 우선하지 않는다: Provider가 있으면 Provider를 쓴다.</summary>
        public CheatOption[] FixedOptions { get; set; }

        /// <summary>이 인자가 의존하는 앞 인자의 인덱스</summary>
        public int[] DependsOn { get; set; } = Array.Empty<int>();

        public string DisplayLabel => string.IsNullOrEmpty(Label) ? Name : Label;
        public bool HasOptions => Provider != null || FixedOptions != null;
        public bool IsInteger => ValueType == CheatValueType.Int || ValueType == CheatValueType.Long;
    }

    /// <summary>
    /// 치트 정의. 생성 코드(레지스트리)가 만들어 CheatRegistry에 등록한다.
    /// </summary>
    public sealed class CheatDefinition
    {
        private string[] m_PathSegments;

        public string Id { get; set; }
        public string Path { get; set; }
        public CheatTarget Target { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
        public CheatArgDefinition[] Args { get; set; } = Array.Empty<CheatArgDefinition>();

        /// <summary>Local 치트 실행</summary>
        public Func<CheatArgs, CheatResult> LocalInvoker { get; set; }

        /// <summary>
        /// Server 치트의 명령 문자열 생성. 패널 미리보기가 값이 바뀔 때마다 부르므로 문자열만 만들고 부작용이 없어야 한다.
        /// </summary>
        public Func<CheatArgs, string> ServerCommandBuilder { get; set; }

        /// <summary>"Item/Enchant" → ["Item", "Enchant"]</summary>
        public string[] PathSegments => m_PathSegments ??= SplitPath(Path);

        public string Name => PathSegments.Length > 0 ? PathSegments[PathSegments.Length - 1] : Id;

        public int IndexOfArg(string name)
        {
            for (int i = 0; i < Args.Length; i++)
            {
                if (string.Equals(Args[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        public object[] CreateDefaultValues()
        {
            var values = new object[Args.Length];
            for (int i = 0; i < Args.Length; i++)
                values[i] = CheatValueParser.DefaultOf(Args[i]);
            return values;
        }

        private static string[] SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }
    }

    /// <summary>
    /// 실행 시 메서드에 넘길 인자 값. 값은 이미 인자 타입으로 변환돼 있다.
    /// </summary>
    public readonly struct CheatArgs
    {
        private readonly object[] m_Values;

        public CheatArgs(object[] values)
        {
            m_Values = values ?? Array.Empty<object>();
        }

        public int Count => m_Values.Length;

        public T Get<T>(int index) => (T)m_Values[index];
    }
}
