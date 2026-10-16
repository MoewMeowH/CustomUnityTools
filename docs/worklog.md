# 작업 로그

## 2026-10-17 HB_Cheat 자동화 정비 (6장 5절) — com.hb.cheat 0.1.2 → 0.1.3

계획서: [docs/plans/2026-10-17_hb-cheat-automation.md](plans/2026-10-17_hb-cheat-automation.md)

### 커밋

| 순서 | 커밋 | 내용 |
|---|---|---|
| 0 | 4d00408 | EditMode 테스트 어셈블리, 테스트용 진입점, 계획서 |
| 1 | ca81f96 | CLI가 실패를 항상 JSON으로 |
| 4 | 1677d87 | 실행 주체 기록 |
| 2 | 9edf1e4 | 끝나지 않은 실행은 히스토리에서 지우지 않음 |
| 3 | 10cc676 | 서버 큐 상태를 JSON에 (`serverQueue`, `blocked`) |
| 2·3 | ef776c2 | 교차 리뷰 반영 |
| 5 | 13655ab | 자동화 경로 raw 차단 옵션 (기본 꺼짐) |
| 6 | (이 커밋) | README · 버전 0.1.3 · 작업 로그 |

전체 EditMode 테스트(마지막 1회): 34개 중 33 통과, 1 실패 — `HB.TestFrameworkHelper.Editor.SlotValidationTests` (`Assets/Resources/Player.prefab > NewMonoBehaviourScript.m_Image` 빈 슬롯). HB_Cheat와 무관, 작업 전부터 사용자가 수정 중이던 파일 (확정: 세션 시작 시 git status에 `NewMonoBehaviourScript.cs` 수정 상태).

### 계획과 달라진 것 (확정)

- 4번 패널 표시: `· CLI` 대신 `[CLI]`. 이유: `CheatStyles.StatusText`에 "내장 폰트에 없는 기호를 피한다"는 기존 규칙이 있고 `·`(U+00B7) 포함 여부를 확인하지 않았음.
- 2번 보존 범위: 계획은 "추가 순서 기준 최근 N건". 리뷰(R1) 반영으로 **발급 id 기준**으로 바꿈.
- 3번: 계획에 없던 변경 2가지 — 시간 초과·종료 시 상태 확정 후 알림 순서(리뷰 R2), `blocked`를 서버 실행에만(R1을 확인하다 로컬 실행이 실행 중 Pending인 것을 보고 추가).
- 공개 API 추가: `CheatSource`, `CheatExecution.Source`, `Execute/ExecuteRaw(…, source)`, `CheatProtocol.TryParsePairs`, `Error(message, code)`, `Handle(line, source)`, `CheatSystem.InFlightServerExecution`, `CheatRuntimeOptions.AllowRawFromAutomation`.
- README에 `Handle`은 메인 스레드에서 부른다는 요구를 적음 (기존부터 스레드 안전하지 않았음, 자체 리뷰 O4).

### 테스트 (확정)

- `HB.Cheat.Tests.Editor` 33개, 에디터의 `unity command run_tests --mode EditMode --filter HB.Cheat.Tests`로 실행, **33/33 통과**. (에디터가 열려 있어 배치 `unity test`는 쓰지 않음)
- 간접 의존 test-framework 1.1.33이 Unity 7000.0.0a8에서 동작함을 확인.
- 리뷰 반영 테스트 2개(중첩 로컬 실행, 큐 변경 구독자)는 수정 전 코드에서 실패하는 조건임을 코드로 추적해 확인 (수정 전 코드로 실제로 돌리지는 않음 — 추정).
- raw 테스트 3개는 옵션 확인 코드가 빠진 상태에서 실제로 실패하는 것을 봄 (편집 스크립트가 중간에 멈춘 덕에 우연히 확인).
- CLI 래퍼(`CheatCliCommands`)는 pipeline 어셈블리에 묶여 단위 테스트 없음 → 실제 확인으로 대신.

### 실제 확인 — 지휘자 → Unity CLI (Play Mode, 확정)

준비: 프로젝트에 등록된 치트가 없어 샘플(`Samples~/CheatExamples`)을 `Assets/Samples/HB_Cheat/`로 복사해 사용, 끝난 뒤 생성 레지스트리(`Assets/Cheats/Generated`)와 함께 삭제. 커밋하지 않음.

**고치기 전 (0.1.2)**

```
$ unity command cheat_run --id item.enchant --args "itemId=1001 level"
{"success":false,...,"errors":[{"code":"COMMAND_FAILED","message":"Pipeline server returned 400 Bad Request: Command Execution Failed. Command 'cheat_run' failed: Expected k=v but got 'level'"}]}
exit=6

# HistoryCapacity=2, 응답 없는 전송으로 서버 치트 3건 → 전송 중인 #2 조회
$ unity command cheat_status --execution 2
{"ok":false,"error":"Unknown execution 2"}
```

**고친 뒤 (0.1.3)**

```
$ unity command cheat_list            → 17개
$ unity command cheat_run --id item.enchant --args "itemId=1001 level"
{"ok":false,"error":"Expected k=v but got 'level'"}          exit=0
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,...,"source":"Cli","status":"Pending","done":false,"blocked":false,...}}
$ unity command cheat_status --execution 1
{"ok":true,...,"execution":{"id":1,...,"status":"Succeeded","done":true,"blocked":false,"message":"Success",...}}
$ unity command cheat_raw --command "item_add 1001 10"
{"ok":false,"error":"Raw commands from automation are disabled (...)","code":"raw_disabled"}
$ unity command cheat_status --execution 999
{"ok":false,"error":"Unknown execution 999","code":"unknown"}
```

**러너 → Tick 연결 (사용자 결정 3번 요청)** — 시간 초과 0.5초 + 응답 없는 전송(`CheatLogTransport`)

```
$ unity command cheat_run --id item.clear                     → #1 Pending, inFlight 1
(2초 대기)
$ unity command cheat_status --execution 1
{"ok":true,"serverQueue":{"paused":true,"reason":"#1 timed out","queued":0,"inFlight":null},"execution":{"id":1,...,"status":"TimedOut","done":true,"blocked":false,...}}
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":true,"reason":"#1 timed out","queued":1,"inFlight":null},"execution":{"id":2,...,"status":"Pending","done":false,"blocked":true,"message":"Paused: #1 timed out. ..."}}
# 백그라운드 스레드에서 ReportServerResult → true 반환, 직후 아직 paused (큐에 쌓임)
# 다음 Tick: 늦은 응답으로 버리고 재개 → #2 전송 → 0.5초 뒤 #2 TimedOut ("reason":"#2 timed out")
# 시간 초과 30초로 바꾸고 #3 실행 → 백그라운드 스레드 응답 → Tick에서 처리
$ unity command cheat_status --execution 3
{"ok":true,...,"execution":{"id":3,...,"status":"Succeeded","done":true,"blocked":false,"message":"ok from thread",...}}
```

→ 러너가 매 프레임 `Tick`을 불러 ① 시간 초과 ② 스레드 밖 응답 처리 ③ 늦은 응답 폐기·재개가 모두 동작함을 확인.

**패널 히스토리 표시 (결정 4)** — 패널 Label 텍스트를 읽어 확인 (게임 뷰 스크린샷은 카메라 렌더라 UI Toolkit 오버레이가 찍히지 않음)

```
hostedInConsole=True; #4  Failed | #3  Success  [CLI] | #2  Timeout  [CLI] | #1  Timeout  [CLI]
```
(#4는 주체 Panel로 실행 → 표시 없음)

**확인 중 사건**: 첫 번째 러너 → Tick 확인 도중 CheatSystem이 초기화 해제됨. 원인은 컴파일 직후 내가 `CheatSettings.cs` 줄바꿈을 정리한 것 → Play Mode 중 재컴파일 → 도메인 리로드. 패키지 문제 아님(확정, 콘솔에 Play Mode 중 컴파일 경고 기록). 컴파일 완료 후 Play Mode를 다시 시작해 처음부터 재확인함.

### 외부 리뷰 (review, runId cfe210dc11, gpt_main_sol) — 2·3번

원문: [docs/reviews/2026-10-17_hb-cheat-automation_review.md](reviews/2026-10-17_hb-cheat-automation_review.md). 에이전트 input에는 목적·의도·제약만 넣고 지시서의 의심 지점 표와 내 판정은 넣지 않음.

| # | 지적 | 누가 찾았나 | 판정 | 근거 / 조치 |
|---|---|---|---|---|
| R1 | 로컬 치트 안에서 다른 치트를 실행하면 안쪽이 먼저 히스토리에 들어가, 용량 경계에서 더 최근(#2) 실행이 지워지고 `evicted`가 됨 (major) | 에이전트만 | 채택 | 코드로 확인. 순서 문제 자체는 0.1.2에도 있었음(`RemoveAt(0)`). 보존 범위를 발급 id 기준으로 바꿈 + 테스트 |
| R2 | 시간 초과 처리에서 `PauseServerQueue`가 `Finish`보다 먼저 `ServerQueueChanged`를 불러 구독자가 끝난 실행을 `Pending`·`blocked:true`로 봄. Shutdown도 같음 (minor) | 에이전트만 | 채택 | 코드로 확인. CLI 폴링은 프레임 사이라 보지 못하지만 같은 프로세스 구독자는 봄. 상태 확정 → 정지 → 알림 순으로 + 테스트 |
| O1 | Play Mode 재시작 시 id가 1부터 다시 발급 → 이전 세션 id로 조회하면 다른 실행을 받음 | 지휘자만 | 범위 밖 (기록) | 기존 문제. 세션 식별자가 필요. README `unknown` 설명에 재발급 사실만 적음 |
| O2 | 큐가 멈춘 채 자동화가 계속 넣으면 히스토리·대기열이 무한정 커짐 | 지휘자만 | 의도 (유지) | 패널 동작을 바꾸지 않기 위해 상한 없음. `blocked`에서 자동화가 멈추는 것이 전제 |
| O3 | `blocked` 판정 정확성 (정지 중 Pending은 모두 대기열에 있음) | 지휘자만 | 확인, 보강 | R1 확인 중 로컬 실행도 실행 중엔 Pending인 것을 보고 서버 실행으로 제한 |
| O4 | 브리지가 `Handle`을 백그라운드 스레드에서 부르면 히스토리 압축과 경합 | 지휘자만 | 채택 (문서) | 기존부터 스레드 안전하지 않음. README에 메인 스레드 요구 명시 |
| O5~O7 | Local 응답에도 `serverQueue`, 키 순서, `TrimHistory` O(n), 패널 히스토리 길이 | 지휘자만 | 무해 / 의도 | — |

판정 짝짓기 집계 (5장 두 번째 사례): **둘 다 0건 / 에이전트만 2건(모두 채택) / 지휘자만 7건(채택 2, 의도·범위 밖 5)**. 에이전트만 찾은 2건은 모두 "호출 순서·재진입" 계열이고, 지휘자만 찾은 것은 세션·스레드·운영 규칙 계열이었다.

### 의심 지점 판정 결과 요약

| # | 판정 | 결과 |
|---|---|---|
| 1 | 확정 | 수정 (ca81f96) |
| 2 | 확정 (추정보다 심각: 끝난 뒤에도 결과 소실) | 수정 (9edf1e4, ef776c2) |
| 3 | 확정 | 수정 (10cc676, ef776c2). 재개 명령은 결정 2에 따라 없음 |
| 4 | 확정 | 수정 (1677d87) |
| 5 | 확정 | 수정 (13655ab), 기본 false (결정 1) |
| 기각 ① | 기각 확인 | 변경 없음 |
| 기각 ② | 기록만 | 변경 없음 (`HB.CheatSheet` 같은 이름도 막는 쪽 오탐) |

### 관찰 (이번 범위 밖, 추정 포함)

- `unity command recompile`은 새 파일을 가져오지 않아, 샘플 복사 직후 레지스트리 자동 생성이 빈 결과였던 것으로 보임 (추정). eval로 `Generate` 호출 후 정상.
- 에디터 어셈블리에 Unity 7 경고: `UAL0010/UAL0013`(`CheatEditorHooks`, `CheatSettings`에 AutoStaticsCleanup 특성 없음), `CS0108`(`CheatSettings.DontDestroyOnLoad`가 `Object.DontDestroyOnLoad`를 가림). 기존 경고, 손대지 않음.
- 테스트 러너가 한 번 "Test execution timed out, cancelling..." 후 자동 재실행해 통과한 기록이 콘솔에 있음. 원인 미확인.
- 자동화 실행도 `GetLastValues`(패널의 마지막 인자 값)를 갱신함. 기존 동작 그대로 둠.
