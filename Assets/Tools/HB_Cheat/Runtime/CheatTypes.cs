namespace HB.Cheat
{
    public enum CheatTarget
    {
        /// <summary>클라에서 메서드를 직접 실행</summary>
        Local = 0,
        /// <summary>메서드가 만든 문자열을 ICheatServerTransport로 전송</summary>
        Server = 1
    }

    /// <summary>인자의 저장 타입</summary>
    public enum CheatValueType
    {
        Int,
        Long,
        Float,
        Double,
        Bool,
        String,
        Enum
    }

    /// <summary>인자의 입력 방식 (저장 타입과 별개)</summary>
    public enum CheatEditorKind
    {
        /// <summary>스테퍼 + 패널 내 키패드 (OS 키보드 안 씀)</summary>
        Number,
        Toggle,
        /// <summary>목록 선택 (Provider 또는 enum)</summary>
        Options,
        /// <summary>문자열 입력 (OS 키보드)</summary>
        Text
    }

    public enum CheatStatus
    {
        /// <summary>서버로 보냈고 응답 대기 중</summary>
        Pending,
        Succeeded,
        Failed,
        TimedOut
    }

    /// <summary>실행 주체. 패널 히스토리와 자동화 JSON에 표시된다.</summary>
    public enum CheatSource
    {
        /// <summary>프로젝트 코드 (CheatSystem.Execute를 직접 호출)</summary>
        Code,
        /// <summary>치트 패널 (사람)</summary>
        Panel,
        /// <summary>Unity CLI (unity command cheat_*)</summary>
        Cli,
        /// <summary>ICheatBridge (CheatProtocol.Handle)</summary>
        Bridge
    }

    public enum CheatInitMode
    {
        AutoOnGameStart = 0,
        Manual = 1
    }

    public readonly struct CheatOption
    {
        public readonly object Value;
        public readonly string Label;

        public CheatOption(object value, string label)
        {
            Value = value;
            Label = label ?? (value != null ? value.ToString() : string.Empty);
        }
    }

    /// <summary>Local 치트의 실행 결과</summary>
    public readonly struct CheatResult
    {
        public readonly bool Success;
        public readonly string Message;

        public CheatResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static CheatResult Ok(string message = null) => new CheatResult(true, message);
        public static CheatResult Fail(string message) => new CheatResult(false, message);
    }
}
