namespace HB.Cheat
{
    /// <summary>
    /// 자동화 응답 {"ok":false,"error":"...","code":"..."}의 code. <b>계약: 이름을 바꾸지 않는다</b> (error 문구는 바뀔 수 있다).
    /// 인자·요청 문제는 모두 ok:false + code, 실행 결과 문제(로컬 치트 실패, 서버 실패·시간 초과)는 ok:true + execution.status.
    /// 패턴: 요청 문제는 &lt;형용사&gt;_&lt;대상&gt;(대상은 단수), 상태·사건은 &lt;주체&gt;_&lt;과거분사&gt; 또는 not_&lt;상태&gt;.
    /// </summary>
    public static class CheatErrorCode
    {
        /// <summary>Play Mode가 아니거나 재컴파일 등으로 초기화가 풀림</summary>
        public const string NotInitialized = "not_initialized";

        /// <summary>요청의 세션 값이 현재 세션과 다름 (Play Mode 재시작·재컴파일로 id가 다시 발급됨)</summary>
        public const string SessionChanged = "session_changed";

        /// <summary>자동화 경로(CLI·브리지)의 raw 전송이 꺼짐 (CheatRuntimeOptions.AllowRawFromAutomation)</summary>
        public const string RawDisabled = "raw_disabled";

        /// <summary>현재 세션에서 실행됐지만 히스토리 용량으로 지워진 실행 id</summary>
        public const string EvictedExecution = "evicted_execution";

        /// <summary>현재 세션에서 발급된 적 없는 실행 id (세션을 넘기면 이전 세션 id는 session_changed)</summary>
        public const string UnknownExecution = "unknown_execution";

        /// <summary>브리지에 빈 줄</summary>
        public const string EmptyCommand = "empty_command";

        /// <summary>브리지의 모르는 동사</summary>
        public const string UnknownCommand = "unknown_command";

        /// <summary>브리지 동사의 위치 인자(치트 id · 인자 이름 · 실행 id) 부족·형식</summary>
        public const string InvalidUsage = "invalid_usage";

        /// <summary>k=v 형식이 아닌 토큰</summary>
        public const string MalformedArgument = "malformed_argument";

        /// <summary>등록되지 않은 치트 id</summary>
        public const string UnknownCheat = "unknown_cheat";

        /// <summary>치트에 없는 인자 이름</summary>
        public const string UnknownArgument = "unknown_argument";

        /// <summary>선택 목록이 없는 인자에 options 요청</summary>
        public const string ArgumentWithoutOptions = "argument_without_options";

        /// <summary>값을 인자 타입으로 바꿀 수 없음</summary>
        public const string UnparsableValue = "unparsable_value";

        /// <summary>기본값 없는 인자를 넣지 않음</summary>
        public const string MissingArgument = "missing_argument";

        /// <summary>선택 목록 Provider가 예외를 냄</summary>
        public const string OptionProviderFailed = "option_provider_failed";

        /// <summary>raw 명령이 비어 있음</summary>
        public const string EmptyRawCommand = "empty_raw_command";

        /// <summary>예상하지 못한 예외</summary>
        public const string InternalError = "internal_error";

        /// <summary>숫자가 [CheatRange] 밖</summary>
        public const string OutOfRangeValue = "out_of_range_value";

        /// <summary>값이 선택 목록에 없음</summary>
        public const string UnlistedValue = "unlisted_value";
    }
}
