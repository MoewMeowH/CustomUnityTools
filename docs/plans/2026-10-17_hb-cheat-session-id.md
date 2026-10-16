# HB_Cheat 실행 id 세션 구분 (7장 전 선행)

## 요약
Play Mode 재시작이나 Play Mode 중 재컴파일로 정적 상태가 초기화되면 실행 id가 1부터 다시 발급된다. 그러면 자동화가 `run`과 `status` 사이에 끼인 초기화를 모른 채, 다른 실행의 결과를 정답으로 읽는다.
id 공간마다 세션 값을 두고, 응답에 싣고, `status`가 세션을 받아 다르면 `code:"session_changed"`로 거절한다.
초기화 해제 상태의 에러에도 `code:"not_initialized"`를 붙여, 자동화가 문구가 아니라 code로 멈출 수 있게 한다.

## 배경
- 직전 작업의 자체 리뷰 O1. 그때는 범위 밖으로 기록만 했다 → `docs/worklog.md`.
- 실제 사례(확정): 실제 확인 중 Play Mode 재컴파일로 CheatSystem이 초기화 해제됐다. 그때는 `Enter Play Mode first`로 끝났지만, 다시 초기화된 뒤였다면 `status 2`가 새 세션의 #2를 돌려줬을 것이다.
- id 카운터는 `s_NextExecutionId`(`CheatSystem.cs:24`) 하나다. 초기화되는 곳은 정적 리셋 두 군데뿐이다.
  - Unity 6.5+/7: `[AutoStaticsCleanup]`
  - 그 이전: `ResetStaticsOnDomainReload`
  - 테스트 진입점 `ResetExecutionState`
- `Shutdown` → `Initialize`(씬 전환 등)는 카운터와 히스토리를 유지한다. 즉 같은 id 공간이다 (확정: `Shutdown`이 `s_NextExecutionId`·`s_History`를 건드리지 않음).

## 계획
1. **세션 값**: `CheatSystem.SessionId`(string, 8자리 16진). 정적 필드 초기값을 null로 두고 첫 실행 id를 발급할 때 만든다.
   - 정적 리셋(위 세 곳)이 null로 되돌리므로, "id 공간이 바뀔 때만" 바뀐다. `Shutdown`/`Initialize`로는 바뀌지 않는다.
   - `CheatExecution.Session`에도 기록한다.
2. **JSON**: `run` / `raw` / `status` / `history` 응답 최상위에 `"session":"…"`를 넣는다.
   - history의 항목은 모두 현재 세션 것이다 (리셋 때 히스토리도 같이 비워지기 때문).
3. **status 세션 확인**:
   - CLI: `cheat_status --execution N --session S`
   - 프로토콜: `status N [session=S]`
   - S가 현재 세션과 다르면 `{"ok":false,"error":"Session changed (S → T). Execution N belongs to a previous session","code":"session_changed"}`를 돌려준다.
   - 같으면 기존 동작(`evicted` / `unknown` 포함).
   - `--session`을 생략하면 지금처럼 동작한다 ("옵션을 비워 두면 기존 동작").
4. **not_initialized**: CLI의 `Enter Play Mode first`, `CheatProtocol.Run/Raw`의 not initialized 에러에 `code:"not_initialized"`를 붙인다.
5. **README 폴링 규칙**:
   - `run` 응답의 `session`을 저장하고 `status`에 항상 넘긴다.
   - `session_changed` / `not_initialized`가 오면 멈추고 사람에게 넘긴다.
6. 버전 0.1.3 → 0.1.4. `docs/worklog.md`에 절을 추가한다.

## 버린 대안과 이유
- id를 문자열 `"<세션>-<번호>"`로 바꾸기: `--execution`이 int라 CLI·프로토콜·공개 API(`FindExecution(int)`)가 모두 깨진다.
- 세션마다 id 시작값을 무작위·시각 기반으로 띄우기: 확률로만 피할 뿐이고, 자동화가 "세션이 바뀌었다"는 사실을 알 수 없다(그냥 `unknown`).
- `Initialize` 때마다 세션 갱신: 같은 id 공간인데 세션만 바뀌어, 씬 전환 뒤에도 정상 조회가 거절된다.
- `--session` 필수화: 기존 호출이 깨진다. 자동화 쪽 규칙(README)으로 강제한다.

## 위험
- 자동화가 `--session`을 빼먹으면 지금과 같은 위험이 남는다. 7장 자동화 절차에 넣는 것으로 대응한다.
- `[AutoStaticsCleanup]`이 null 초기값 필드를 실제로 되돌리는지는 **추정**이다. 6.5+ 문서 동작에 기댄다 → 실제 확인 2로 검증한다.
- 세션 값은 `Guid` 기반이라 테스트에서 값을 고정할 수 없다. 테스트는 같음·다름만 본다.

## 테스트 (EditMode)
- 같은 세션이면 `status`가 정상, 다른 세션 문자열이면 `session_changed`, 생략하면 기존 동작.
- `ResetForTests` 뒤 첫 실행에서 세션이 바뀌고, 그 전 세션 값으로 조회하면 `session_changed`.
- `Shutdown`을 흉내 낸 뒤 `Initialize` 상당 경로에서는 세션이 유지된다 (테스트 진입점이 허용하는 범위에서. 안 되면 실제 확인으로).
- `Run`/`Raw` 초기화 전 에러에 `not_initialized`.
- 응답 JSON에 `session`이 있는지.

## 실제 확인 (Play Mode, Unity CLI)
1. `cheat_run`으로 `session=S1`, id 1을 받는다 → `cheat_status --execution 1 --session S1` 정상.
2. Play Mode 정지 → 재시작 → `cheat_run`(새 #1, `session=S2`) → `cheat_status --execution 1 --session S1` → `session_changed`.
3. Play Mode 중 재컴파일(스크립트 수정) → `cheat_status --execution 1 --session S2` → `not_initialized`.
4. 응답 원문을 작업 로그에 남긴다.

## 사용자에게 물을 것
- Q1. 4번 `not_initialized` code를 이번에 같이 넣을까요? 같은 사고(재컴파일)의 앞단이라 같이 넣는 것을 추천합니다.
- Q2. 외부 `review`는 부르지 않으려 합니다. 변경이 50줄 안팎이고 비동기 경로를 건드리지 않습니다. 필요하면 말씀해 주세요.

## 결정

사용자 답 (2026-10-17, 원문):

> not_initialized 같이 넣자.
> review는 생략해도 돼.
> 추가로, 세션 값이 첫 발급 때 생기면 Play Mode 재시작 직후(실행 0건) 이전 세션 값으로 status를 부를 때 현재 세션이 null이야. 이것도 session_changed로 처리되게 해줘. getter에서 null이면 만드는 식이면 될 것 같아. 이 경우 테스트도 하나 넣어줘.
> AutoStaticsCleanup이 세션을 되돌리는지는 실제 확인 2에서 꼭 원문으로 남겨줘. 진행해줘!

바뀐 계획 항목:
- 계획 1: 세션 값을 "첫 id 발급 때"가 아니라 **`SessionId` getter에서 null이면 만든다**. 그래서 실행이 0건이어도 `status`가 세션을 비교할 수 있다. 재시작 직후 이전 세션 값으로 부르면 `session_changed`가 된다.
- 계획 4(`not_initialized`) 포함. 외부 review 생략.
- 테스트 추가: 리셋 직후(실행 0건) 이전 세션 값으로 `status` → `session_changed`.
- 실제 확인 2: Play Mode 재시작 전후의 `session` 값과 `session_changed` 응답을 원문으로 작업 로그에 남긴다 (AutoStaticsCleanup이 세션을 되돌리는지에 대한 근거).
