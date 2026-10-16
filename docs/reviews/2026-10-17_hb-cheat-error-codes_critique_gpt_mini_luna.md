## 1. 핵심 약점

1. **`unknown`과 `evicted`의 대상이 이름에 드러나지 않습니다.** `unknown_command`도 생기므로 `unknown`은 모르는 실행 ID인지 명령인지 혼동될 수 있습니다. 미배포 코드이므로 `unknown_execution`, `evicted_execution`처럼 실행 ID 대상임을 이름에 넣는 편이 안전합니다.
2. **`no_options`, `out_of_range`, `not_in_options`는 대상이 모호하고 표현 패턴도 덜 구체적입니다.** 각각 `no_options_for_argument`, `value_out_of_range`, `value_not_in_options`를 권합니다. `invalid_value`도 이 후보들과 범위가 겹쳐 보일 수 있으므로, 현재 의미가 타입 변환 실패라면 `value_parse_failed`가 더 명확합니다.
3. **`unknown`의 제안된 의미가 코드 동작과 맞지 않을 수 있습니다.** `Status`는 현재 상태에서 ID를 찾지 못하고 `WasIssued`도 false면 `unknown`을 반환합니다. 세션이 초기화된 뒤 이전 세션 ID를 전달하면서 `session`을 생략한 경우도 이 경로로 갈 수 있으므로, “한 번도 발급된 적 없음”과 “이전 세션에서 발급됨”을 구별하지 못합니다. 이 구분이 필요하다면 현재 입력·상태만으로는 분류할 수 없습니다.
4. **현재 오류 경로 중 일부는 새 코드 적용 전까지 일반 예외 경로로 뭉칩니다.** `ParsePairs`의 형식 오류는 `Handle`의 포괄 `catch`에 들어가며, CLI의 `Guard` 예외도 코드 없는 오류를 반환합니다. 분류상 전자는 `malformed_args`, 예상하지 못한 예외는 `internal_error`에 해당합니다. 나머지 첨부된 `ok:false` 경로는 제안 목록으로 분류 가능합니다.

## 2. 대안

- **원안의 대부분을 유지하고 모호한 이름만 구체화:** `unknown_execution`, `evicted_execution`, `no_options_for_argument`, `value_out_of_range`, `value_not_in_options`, `value_parse_failed`.
  - 장점: 각 코드만 보고 대상과 발생 상황을 추론하기 쉬워 자동화 분기에서 혼동이 줄어듭니다.
  - 단점: 코드가 길어지고, 미배포 코드 이름을 지금 확정해야 합니다.
- 현재 실행 전 검증이 없는 `out_of_range`, `not_in_options`는 **검증이 추가되는 경우에만** 코드 계약에 포함하면 됩니다. 첨부 코드의 값 파싱은 타입 변환까지만 확인하므로, 현재 실패 경로의 누락 코드는 아닙니다.

## 3. 결정 전 확인할 질문

- `unknown`을 “현재 세션에서 찾을 수 없음”으로 정의할지, “어느 세션에서도 발급된 적 없음”으로 정의할지? 이전 세션 발급 여부까지 자동화가 구분해야 하는지?
- Unity CLI 프레임워크가 메서드 호출 전에 필수 인자 누락이나 `--execution`의 정수 변환 실패를 거부하는지? **자료 부족: `Unity.Pipeline.Commands`의 인자 검증·실패 응답 동작 또는 해당 입력의 실제 응답**
- `CheatSystem.GetOptions`가 반환하는 `optionError`가 선택 목록 Provider 예외만 뜻하는지? **자료 부족: `GetOptions` 구현 및 `optionError` 발생 조건**

## 4. 결론: 수정 후 진행 — 미배포 단계에서 대상이 불명확한 코드를 구체화하고 `unknown`의 세션 경계를 확정해야 합니다.