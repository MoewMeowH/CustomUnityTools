1. 핵심 약점
- `internal_error`와 그 외의 명시적 에러(`unknown_cheat`, `invalid_value` 등)의 경계가 모호하여, 예외 메시지에 따라 클라이언트가 어떤 상황인지 예측할 수 없음 (`CheatProtocol.Handle`의 `catch (Exception e)`에서 일괄 처리됨).
- `CheatCliCommands`는 `TryRequirePlayMode` 실패 시 `not_initialized` 코드를 반환하지만, `CheatProtocol.Handle` 내부(예: `Run`, `Raw`)에서는 `CheatSystem.IsInitialized` 체크 시 구체적인 에러 코드 부재 또는 `NotInitialized()` 호출 여부가 일관되지 않음.
- `malformed_args`와 `invalid_usage`의 대상 범위가 겹침 (`ParsePairs` 실패 시 `malformed_args`가 필요한지, 인자 부족을 뜻하는 `invalid_usage`와 분리되어야 하는지 모호함).
- `option_provider_failed`가 발생했을 때 예외 내용이 클라이언트에 그대로 노출되거나 런타임 안정성을 해칠 수 있음 (메인 스레드 안전성 보장 여부 불명).

2. 대안
- 대안 1: `CheatProtocol.Handle` 및 CLI 레이어의 예외 처리 단계를 세분화하여 `internal_error`와 파싱 에러를 명확히 분리
  - 장점: 자동화 스크립트가 시스템 오류와 입력 오류를 명확히 구분 가능.
  - 단점: 예외 핸들링 보일러플레이트 코드가 증가함.

3. 결정 전 확인할 질문
- `CheatProtocol.Handle`로 들어오는 파싱 에러(예: `k=v` 포맷 위반)는 `malformed_args`와 `invalid_usage` 중 정확히 어떤 코드로 매핑되어야 하는가?

4. 결론: 수정 후 진행 + 이유 한 줄
입력 유효성 검증과 예외 처리 경로에서 누락된 에러 코드를 보완해야 자동화 클라이언트가 오작동하지 않기 때문.