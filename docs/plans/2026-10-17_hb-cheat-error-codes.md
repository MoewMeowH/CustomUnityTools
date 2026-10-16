# HB_Cheat 에러 code 전수 부여 (7장 전 선행)

## 요약
`CheatProtocol`과 CLI가 돌려주는 `ok:false` 중 code가 없는 것을 전부 찾았다. 각각에 바꾸지 않을 code를 붙이고, code 문자열은 `CheatErrorCode` 상수로 한곳에 모은다.
README에는 "code / 언제 나오나" 표를 넣는다. 처리 방법은 "멈추고 사람에게 넘긴다"는 예시 하나만 둔다.
조사 중 확인한 것: 지시에 있던 "목록에 없는 값"은 지금 `ok:false`가 아니라 `ok:true` + `execution.status:"Failed"`로 나온다. 이것을 어떻게 다룰지 Q1로 묻는다.

## 배경
- 0.1.3~0.1.4에서 붙인 code: `evicted`, `unknown`, `raw_disabled`, `session_changed`, `not_initialized`.
- 0.1.3 이후 커밋 12개는 아직 origin에 푸시되지 않았다 (확정: `origin/main` = cbe5beb, 0.1.2). 즉 이 code들은 아직 배포된 계약이 아니라서, 이름을 바꿔도 깨지는 사용자가 없다.

### code 없는 `ok:false` 전수 (확정, `grep "Error("` + 호출 경로 추적)

| 위치 | 메시지 | 경로 |
|---|---|---|
| `CheatProtocol.cs:28` | `Empty command` | 브리지 `Handle` |
| `:36` `:42` `:45` `:48` | `Usage: options/run/raw/status ...` | 브리지 `Handle` |
| `:56` | `Unknown command '<verb>'` | 브리지 `Handle` |
| `:61` | `e.Message` (catch 전체) | 브리지 `Handle` — 대표 원인은 `ParsePairs`의 `Expected k=v` (FormatException) |
| `:106` `:138` | `Unknown cheat '<id>'` | options / run |
| `:108` | `'<id>' has no argument '<arg>'` | options의 `--arg` |
| `:109` | `'<arg>' has no option list` | options |
| `:111` `:139` → `:209` | `'<id>' has no argument '<key>'` | `--args`의 키 |
| `:111` `:139` → `CheatValueParser.cs:48` | `'<arg>': '<text>' is not a valid <Type>` | `--args` 값 형식 |
| `:139` → `:223` | `Missing argument '<arg>'` | run (기본값 없는 인자) |
| `:114` | `<Provider>: <예외 메시지>` | options (Provider 예외) |
| `:150` | `Empty raw command` | raw |
| `CheatCliCommands.cs:33` `:46` | `Expected k=v but got '...'` | CLI options / run |
| `CheatCliCommands.cs:93` | `e.Message` (Guard catch) | CLI 전체 |

### `ok:false`가 아닌 실패 (확정) — 지시의 "목록에 없는 값"이 여기 속한다
`CheatProtocol.Run`은 값 변환까지만 하고 `CheatSystem.Execute`를 부른다. `Execute` 안의 `Validate`(`CheatSystem.cs`)가 실패하면 실행이 만들어져 히스토리에 `Failed`로 남는다. 응답은 `ok:true`다.
- 범위 밖 숫자: `'<label>' must be in [min, max]`
- 목록에 없는 값: `'<label>': <v> is not in the option list`
- 선택 안 됨(null): `'<label>' is not selected`
- 검증 중 Provider 예외
- 그 밖에 인자 문제가 아닌 실행 결과 실패: 로컬 치트의 `CheatResult.Fail`·예외, 서버 명령 조합 예외, 전송 실패·전송 미등록, 서버 응답 실패, 시간 초과. 이것들은 요청이 아니라 **실행 결과**라 code 대상이 아니다.

## 계획

### 1. code 목록 (안) — 계약: 한 번 정하면 이름을 바꾸지 않는다

규칙: 소문자 snake_case, `<형용사/상태>_<대상>`. 메시지 문구는 바뀔 수 있고, code만 계약이다.

| code | 언제 나오나 | 지금 |
|---|---|---|
| `not_initialized` | Play Mode가 아니거나 초기화가 풀림 | 있음 |
| `session_changed` | `--session`이 현재 세션과 다름 | 있음 |
| `raw_disabled` | 자동화 경로 raw가 꺼짐 | 있음 |
| `evicted` → **`execution_evicted`** | 이번 세션 실행이 히스토리 용량으로 지워짐 | 이름 변경 제안 |
| `unknown` → **`unknown_execution`** | 발급된 적 없는 실행 id | 이름 변경 제안 |
| `empty_command` | 브리지에 빈 줄 | 신규 |
| `unknown_command` | 브리지의 모르는 동사 | 신규 |
| `invalid_usage` | 브리지 동사의 필수 인자 부족·형식 (`Usage: ...`) | 신규 |
| `malformed_args` | `k=v` 형식이 아닌 토큰 (CLI `--args`, 브리지) | 신규 |
| `unknown_cheat` | 등록되지 않은 치트 id | 신규 |
| `unknown_argument` | 치트에 없는 인자 이름 (`--arg`, `--args`의 키) | 신규 |
| `no_options` | 목록이 없는 인자에 `options` 요청 | 신규 |
| `invalid_value` | 값을 인자 타입으로 바꿀 수 없음 | 신규 |
| `missing_argument` | 기본값 없는 인자를 넣지 않음 | 신규 |
| `option_provider_failed` | 목록 Provider가 예외를 냄 | 신규 |
| `empty_raw_command` | raw 명령이 비어 있음 | 신규 |
| `internal_error` | 예상하지 못한 예외 (Handle·CLI 보호막) | 신규 |
| (Q1 B안이면) `out_of_range` | 숫자가 `[CheatRange]` 밖 | 신규 |
| (Q1 B안이면) `not_in_options` | 값이 목록에 없음 | 신규 |

### 2. 구현
- `public static class CheatErrorCode`에 위 code를 상수로 둔다. 주석에 "계약: 이름을 바꾸지 않는다"를 적는다.
- `CheatProtocol`과 CLI의 모든 `Error(...)` 호출에 code를 붙인다. `Error(string)`는 공개 API라 남기되, 패키지 안에서는 쓰지 않는다.
- `TryBuildValues`에 `out string code` 오버로드를 추가한다 (기존 시그니처 유지).
- `Handle`의 `ParsePairs`(예외) → `TryParsePairs`로 바꿔 `malformed_args`로 돌려준다. 지금은 catch에 걸려 code 없는 `e.Message`가 된다.
- (Q1 B안이면) `CheatSystem`에 `internal static bool Validate(def, values, out string error, out string code)`를 추가한다. 공개 `Validate`는 이를 감싼다. `CheatProtocol.Run`이 실행 전에 검증해서, 실패하면 실행을 만들지 않고 `ok:false`로 돌려준다.

### 3. README
- "code / 언제 나오나" 표 (위 목록, `internal_error` 포함).
- 처리 예시는 하나만 둔다: "예: 자동화는 `ok:false`면 code를 기록하고 멈춘 뒤 사람에게 넘긴다." code별 권장 처리는 쓰지 않는다 (쓰는 쪽이 정함).
- 기존 "자동화 폴링 규칙"의 code 언급은 표를 참조하도록 정리하고, `blocked` 규칙은 그대로 둔다.

### 4. 테스트 / 버전
- code마다 하나 이상 유발 테스트를 둔다 (`internal_error`는 예외를 내는 Provider·전송 없이 만들 수 있는 경로가 없으면 Handle 경로로).
- 전수 테스트: 실패 입력 묶음을 `Handle`과 `CheatProtocol` 공개 메서드에 넣고, 모든 `ok:false`에 code가 있으며 그 값이 `CheatErrorCode` 상수 중 하나인지 확인한다.
- CLI 래퍼는 실제 확인으로 본다: `--args` 형식 오류, 없는 치트, 없는 인자, 값 형식, 누락.
- 버전 0.1.4 → 0.1.5, 작업 로그.

## 버린 대안과 이유
- code를 메시지에서 뽑아 쓰기(접두어 규칙): 메시지는 사람용이라 바뀔 수 있다. 계약은 별도 필드여야 한다.
- 숫자 에러 코드: 사람이 로그에서 읽기 어렵고, 기존 문자열 code와 섞인다.
- 실행 결과 실패(`Failed`, `TimedOut`)에도 code 부여: 이번 요청 범위(`ok:false`) 밖이고, `status` 문자열이 이미 그 역할을 한다.

## 위험
- 이름 변경(`evicted`, `unknown`)은 로컬 커밋 안의 테스트·README·작업 로그 기록과 어긋난다. 테스트와 README는 고친다. 과거 작업 로그의 원문 JSON은 당시 기록이므로 고치지 않는다.
- Q1 B안을 고르면 자동화 경로에서 인자 검증 실패가 히스토리에 남지 않는다 (패널은 그대로 `Execute` → 히스토리에 남음). 패널 동작은 바뀌지 않는다.
- `Validate`는 Provider를 부른다. B안이면 run 한 번에 검증이 두 번 돈다 (사전 검증 + `Execute` 안). `CheatOptionCache`가 같은 의존 값이면 캐시를 쓰므로 Provider가 두 번 불리지는 않는다 (확정: `CheatOptionProvider.cs:85` 캐시 조건).

## 테스트
위 4번.

## 실제 확인 (Play Mode, Unity CLI)
각 code를 CLI로 한 번씩 유발해 응답 원문을 작업 로그에 남긴다: `malformed_args`, `unknown_cheat`, `unknown_argument`, `no_options`, `invalid_value`, `missing_argument`, `empty_raw_command`(raw 옵션을 켠 상태에서), `unknown_execution`, (B안이면) `out_of_range`, `not_in_options`. 브리지 전용 code(`empty_command`, `unknown_command`, `invalid_usage`)는 테스트로만 본다.

## 사용자에게 물을 것
- **Q1. "목록에 없는 값"·"범위 밖"을 어떻게 할까요?**
  - (A) 지금처럼 `ok:true` + `execution.status:"Failed"`로 둔다. code 없음, 히스토리에 남음.
  - (B) **(추천)** 자동화 경로(`CheatProtocol.Run`)에서 실행 전에 검증해 `ok:false` + `out_of_range` / `not_in_options`로 돌려준다. 값 형식·누락이 이미 실행 전 `ok:false`이므로 "인자 문제는 모두 `ok:false`"로 일관된다. 패널은 그대로.
- **Q2. 미배포 code 이름 변경** (`evicted` → `execution_evicted`, `unknown` → `unknown_execution`): 푸시 전이라 지금이 마지막 기회입니다. 바꿀까요? (critique 결과를 보고 최종 확정)
- Q3. 버전은 0.1.5로 올릴까요? 0.1.3·0.1.4가 배포되지 않았으니 합쳐도 됩니다.

## 외부 검토 (critique)

`critique` 모드를 두 모델에 **따로** 실행했다. 서로의 결과는 input에 넣지 않았다. input에는 목적·제약·code 후보와 상황만 넣었고, 내 추천(Q1~Q3)과 이름 변경 선호는 넣지 않았다. 코드는 `context`로 넘겼다 (`CheatProtocol.cs` 20–231·270–362, `CheatValueParser.cs` 11–50, `CheatCliCommands.cs` 전체).
- gpt_mini_luna: runId 1dba482e30 → [원문](../reviews/2026-10-17_hb-cheat-error-codes_critique_gpt_mini_luna.md)
- gemini_mini_lite: runId 2204bcf1c2 → [원문](../reviews/2026-10-17_hb-cheat-error-codes_critique_gemini_mini_lite.md)

### 지적 판정

| # | 지적 | 누가 | 판정 | 근거 |
|---|---|---|---|---|
| C1 | `Handle`의 포괄 catch에 `k=v` 형식 오류가 섞여 `internal_error`와 구분 안 됨 | 둘 다 | 채택 (계획 2에 이미 있음) | `Handle`이 `ParsePairs`(예외)를 씀 → `TryParsePairs`로 바꿔 `malformed_*`로 |
| C2 | `unknown` / `evicted`가 대상을 드러내지 않음, `unknown_command`와 혼동 | luna (나도 계획에서 제안) | 채택 | 이름은 luna 안 `evicted_execution`을 따른다. 내 안 `execution_evicted`보다 `<형용사>_<대상>` 패턴에 맞음 |
| C3 | `invalid_value`가 범위·목록 오류와 의미가 겹침. `no_options` · `out_of_range` · `not_in_options`의 대상이 모호 | luna | 채택 (이름은 내가 패턴에 맞춰 정함) | 값 오류 셋을 `<형용사>_value`로 통일: `unparsable_value` / `out_of_range_value` / `unlisted_value`. `no_options` → `argument_without_options` |
| C4 | `unknown`의 정의: "어느 세션에서도 발급 안 됨"과 "이전 세션 id인데 세션 생략"을 구분 못 함 | luna | 채택 (문서) | 맞다 (`WasIssued`는 현재 카운터만 봄). 구분 수단은 `--session` → 정의를 "현재 세션에서 발급된 적 없는 id"로 명시하고, 표에 "세션을 넘기면 이전 세션 id는 `session_changed`"를 적는다 |
| C5 | CLI 프레임워크가 필수 인자 누락·정수 변환 실패를 메서드 전에 거절하는지 | luna (질문) | 채택 (문서) | **확인함**: `--execution abc` → CLI 봉투 `{"success":false,"errors":[{"code":"INVALID_COMMAND_ARGS",...}]}`, `--id` 누락 → `COMMAND_FAILED` "Required parameter 'id' is missing". 패키지 JSON이 아니라 code를 붙일 수 없다 → README에 "CLI 봉투 단계 오류"로 따로 적는다 |
| C6 | `optionError`가 Provider 예외만 뜻하는지 | luna (질문) | 확인 | `CheatOptionCache.Get`의 `error`는 Provider `Collect` 예외(`entry.Error`)뿐 (`CheatOptionProvider.cs:101-106`). `option_provider_failed` 유지 |
| C7 | `malformed_args`와 `invalid_usage`의 경계가 겹침 | gemini | 채택 (정의 명확화) | `invalid_usage` = 동사의 위치 인자(치트 id, 인자 이름, 실행 id) 부족·형식, `malformed_argument` = `k=v` 토큰 형식. 단수형으로 통일 (`missing_argument`·`unknown_argument`와 맞춤) |
| C8 | `Handle` 안의 `Run`/`Raw`가 `NotInitialized()`를 일관되게 쓰지 않음 | gemini | 기각 | 0.1.4에서 `Run`·`Raw` 모두 `NotInitialized()` 사용 (`CheatProtocol.cs` Run·Raw 첫 줄). 다른 동사는 초기화 없이도 동작하는 조회이고, 브리지는 초기화 뒤에만 시작된다 |
| C9 | `option_provider_failed`에 예외 메시지가 노출됨, 메인 스레드 안전성 | gemini | 기각 | 개발·QA 전용 패키지라 메시지 노출은 의도(원인 파악용). 스레드는 이번 범위(code 이름) 밖이며 `Handle`의 메인 스레드 요구는 README에 이미 있음 |

### 수정된 code 목록 (계획 1 대체안)

패턴: 요청 문제는 `<형용사>_<대상>`(대상은 단수), 상태·사건은 `<주체>_<과거분사>` 또는 `not_<상태>`.

| code | 언제 나오나 |
|---|---|
| `not_initialized` | Play Mode가 아니거나 재컴파일 등으로 초기화가 풀림 |
| `session_changed` | `--session`이 현재 세션과 다름 |
| `raw_disabled` | 자동화 경로의 raw가 꺼짐 |
| `evicted_execution` | 현재 세션에서 실행됐지만 히스토리 용량으로 지워진 id |
| `unknown_execution` | 현재 세션에서 발급된 적 없는 id (세션을 넘기면 이전 세션 id는 `session_changed`) |
| `empty_command` | 브리지에 빈 줄 |
| `unknown_command` | 브리지의 모르는 동사 |
| `invalid_usage` | 브리지 동사의 위치 인자(치트 id · 인자 이름 · 실행 id) 부족·형식 |
| `malformed_argument` | `k=v` 형식이 아닌 토큰 |
| `unknown_cheat` | 등록되지 않은 치트 id |
| `unknown_argument` | 치트에 없는 인자 이름 |
| `argument_without_options` | 선택 목록이 없는 인자에 `options` 요청 |
| `unparsable_value` | 값을 인자 타입으로 바꿀 수 없음 |
| `missing_argument` | 기본값 없는 인자를 넣지 않음 |
| `option_provider_failed` | 선택 목록 Provider가 예외를 냄 |
| `empty_raw_command` | raw 명령이 비어 있음 |
| `internal_error` | 예상하지 못한 예외 |
| `out_of_range_value` | (Q1 B안) 숫자가 `[CheatRange]` 밖 |
| `unlisted_value` | (Q1 B안) 값이 선택 목록에 없음 |

README에는 이 표 밖의 층도 적는다: CLI 프레임워크가 먼저 거절하는 오류(`INVALID_COMMAND_ARGS` 등)는 CLI 봉투(`success:false`)로 오며 패키지 code가 없다.

## 결정

사용자 답 (2026-10-17, 원문):

> B로 하자. 인자 문제는 전부 ok:false, 실행 결과 문제는 ok:true + Failed로 나누는 게 맞아.
> 이름 변경 확정. 수정된 19개 목록대로 가자.
> 0.1.5로. 문서 기록이 0.1.3, 0.1.4로 되어 있어서 맞추고 싶어.
> 진행해줘!

바뀐 계획 항목:
- 계획 1: "수정된 code 목록"(19개)을 확정 목록으로 쓴다. `evicted` → `evicted_execution`, `unknown` → `unknown_execution`.
- 계획 2: Q1 B안. `CheatProtocol.Run`이 실행 전에 검증해서 `out_of_range_value` / `unlisted_value` / `missing_argument` / `option_provider_failed`를 `ok:false`로 돌려준다. 원칙: 인자 문제는 전부 `ok:false`, 실행 결과 문제는 `ok:true` + `Failed`.
- 계획 4: 버전 0.1.4 → **0.1.5** (0.1.3·0.1.4로 남은 문서 기록과 맞춘다. 합치지 않는다).
