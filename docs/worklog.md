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

**고치기 전 (0.1.2)** — 응답 원문. `--result-only` 출력의 바깥 문자열 감싸기만 풀었다.

*cheat_list → 17개*

*잘못된 --args (`--format json`, CLI 전체 출력)*

```
$ unity command cheat_run --id item.enchant --args "itemId=1001 level" --format json
{
  "success": false,
  "command": "unity command cheat_run",
  "data": null,
  "errors": [
    {
      "code": "COMMAND_FAILED",
      "message": "Pipeline server returned 400 Bad Request: Command Execution Failed. Command 'cheat_run' failed: Expected k=v but got 'level'"
    }
  ],
  "warnings": []
}
exit=6
```

*정상 서버 치트 / 없는 id*

```
$ unity command cheat_run --id item.clear
{"ok":true,"execution":{"id":1,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 1
{"ok":true,"execution":{"id":1,"cheatId":"item.clear","target":"Server","status":"Succeeded","done":true,"message":"Success","command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 999
{"ok":false,"error":"Unknown execution 999"}
```

*2번 재현: HistoryCapacity=2 + 응답 없는 전송(CheatLogTransport)으로 서버 치트 3건*

```
# eval → set  (HistoryCapacity = 2, ServerTransport = new CheatLogTransport())
$ unity command cheat_run --id item.clear   (1/3)
{"ok":true,"execution":{"id":2,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_run --id item.clear   (2/3)
{"ok":true,"execution":{"id":3,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":"Queued (waiting for the previous server cheat)","command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_run --id item.clear   (3/3)
{"ok":true,"execution":{"id":4,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":"Queued (waiting for the previous server cheat)","command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 2
{"ok":false,"error":"Unknown execution 2"}
$ unity command cheat_history
{"ok":true,"history":[{"id":3,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":"Queued (waiting for the previous server cheat)","command":"item_clear","describe":"Item/Clear Inventory"},{"id":4,"cheatId":"item.clear","target":"Server","status":"Pending","done":false,"message":"Queued (waiting for the previous server cheat)","command":"item_clear","describe":"Item/Clear Inventory"}]}
```

**고친 뒤 (0.1.3)** — 응답 원문

*1. cheat_list → 17개*

*2. 잘못된 --args*

```
$ unity command cheat_run --id item.enchant --args "itemId=1001 level"
{"ok":false,"error":"Expected k=v but got 'level'"}
```

(종료 코드는 아래 2b에서 따로 확인. 기록 스크립트의 echo가 따옴표를 지워 명령 줄의 따옴표는 원래 호출대로 되돌려 적음)

*3. 정상 서버 치트 (가짜 네트워크 0.3초 응답)*

```
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 1
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Succeeded","done":true,"blocked":false,"message":"Success","command":"item_clear","describe":"Item/Clear Inventory"}}
```

*4. raw (기본 꺼짐)*

```
$ unity command cheat_raw --command "item_add 1001 10"
{"ok":false,"error":"Raw commands from automation are disabled (CheatRuntimeOptions.AllowRawFromAutomation, Tools > HB > Cheat)","code":"raw_disabled"}
```

*5. 없는 id*

```
$ unity command cheat_status --execution 999
{"ok":false,"error":"Unknown execution 999","code":"unknown"}
```

**러너 → Tick 연결 (사용자 결정 3번 요청)** — 시간 초과 0.5초 + 응답 없는 전송(`CheatLogTransport`). 아래는 재컴파일 사건 뒤 다시 시작한 Play Mode의 기록 (첫 시도 기록은 무효라 뺐다)

*2b. 잘못된 --args 종료 코드*

```
$ unity command cheat_run --id item.enchant --args "itemId=1001 level" --result-only
exit=0
```

*6. 러너 → Tick: 시간 초과 0.5초 + 응답 없는 전송*

```
# eval → timeout=0.5, CheatLogTransport
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 1
{"ok":true,"serverQueue":{"paused":true,"reason":"#1 timed out","queued":0,"inFlight":null},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"TimedOut","done":true,"blocked":false,"message":"No server response in 0.5s. Server queue paused","command":"item_clear","describe":"Item/Clear Inventory"}}
```

*7. 정지 중 새 실행 → blocked*

```
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":true,"reason":"#1 timed out","queued":1,"inFlight":null},"execution":{"id":2,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":true,"message":"Paused: #1 timed out. Waiting for the late response or ResumeServerQueue","command":"item_clear","describe":"Item/Clear Inventory"}}
```

*8. 백그라운드 스레드에서 늦은 응답 → 다음 Tick에서 버리고 재개*

```
# eval → off-thread returned True, still paused right after=True
$ unity command cheat_status --execution 2
{"ok":true,"serverQueue":{"paused":true,"reason":"#2 timed out","queued":0,"inFlight":null},"execution":{"id":2,"cheatId":"item.clear","target":"Server","source":"Cli","status":"TimedOut","done":true,"blocked":false,"message":"No server response in 0.5s. Server queue paused","command":"item_clear","describe":"Item/Clear Inventory"}}
```

*9. 백그라운드 스레드 응답 → #2 성공*

```
# eval → reported
$ unity command cheat_history --limit 5
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"history":[{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"TimedOut","done":true,"blocked":false,"message":"No server response in 0.5s. Server queue paused","command":"item_clear","describe":"Item/Clear Inventory"},{"id":2,"cheatId":"item.clear","target":"Server","source":"Cli","status":"TimedOut","done":true,"blocked":false,"message":"No server response in 0.5s. Server queue paused","command":"item_clear","describe":"Item/Clear Inventory"}]}
```

*9b. 시간 초과 30초로 → 백그라운드 스레드 응답 → Tick에서 성공 처리*

```
# eval → timeout=30
$ unity command cheat_run --id item.clear
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":3},"execution":{"id":3,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
# eval → off-thread returned True
$ unity command cheat_status --execution 3
{"ok":true,"serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"execution":{"id":3,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Succeeded","done":true,"blocked":false,"message":"ok from thread","command":"item_clear","describe":"Item/Clear Inventory"}}
```

해석 (확정):
- 6·7: 러너의 `Tick`이 시간 초과를 처리해 큐를 멈추고, 그 뒤 실행은 `blocked:true`.
- 8: 백그라운드 스레드의 `ReportServerResult`는 큐에 쌓이고(반환 true, 직후 아직 paused), 다음 `Tick`이 늦은 응답으로 버린 뒤 재개 → #2 전송 → 0.5초 뒤 #2 시간 초과 (`reason`이 `#2 timed out`으로 바뀜).
- 9: 멈춘 상태에서 온 백그라운드 응답도 늦은 응답으로 버리고 재개 (`paused:false`). 제목의 "#2 성공"은 내 시나리오 실수(0.5초 시간 초과가 먼저 남)였고, 동작은 설계대로다.
- 9b: 시간 초과를 30초로 늘려 백그라운드 스레드 응답이 `Tick`에서 `Succeeded`로 처리되는 것을 확인.

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

## 2026-10-17 HB_Cheat 실행 id 세션 구분 — com.hb.cheat 0.1.3 → 0.1.4

계획서: [docs/plans/2026-10-17_hb-cheat-session-id.md](plans/2026-10-17_hb-cheat-session-id.md)

### 커밋

| 커밋 | 내용 |
|---|---|
| 38ddf7c | `SessionId` · `CheatExecution.Session`, 응답 `session`, `--session` → `session_changed`, `not_initialized`, 테스트 |
| (이 커밋) | README · 버전 0.1.4 · 작업 로그 |

### 계획과 달라진 것 (확정)

- 세션 생성 시점: 계획은 첫 id 발급 때 → 사용자 결정으로 `SessionId` getter에서 null이면 생성. 재시작 직후(실행 0건)에도 이전 세션 값과 비교된다.
- `ExecutionJson`의 `session`은 실행의 `Session`을 쓴다 (히스토리가 세션과 함께 비워지므로 현재 세션과 같다).
- README `⚠️ 제한`에 "Play Mode 중 재컴파일 뒤에는 Play Mode를 다시 시작" 추가 (실제 확인 4에서 발견).
- 외부 review 생략 (사용자 결정).

### 테스트 (확정)

- `CheatSessionTests` 7개 추가. 그중 `Status_AfterRestart_NoExecutionsYet_IsSessionChanged`가 사용자 요청 케이스(재시작 직후 실행 0건 + 이전 세션 값). `HB.Cheat.Tests` **40/40 통과**.
- 기존 `HistoryJson_HasServerQueue`는 키 순서(`session`이 맨 앞)로 실패 → 기대값을 `StartsWith` → `Contains`로 고침. 동작 변경이 아니라 테스트가 키 순서에 묶여 있던 것.

### 실제 확인 — Unity CLI, Play Mode (응답 원문)

샘플을 `Assets/Samples/HB_Cheat/`로 다시 복사해 쓰고, 끝난 뒤 생성 레지스트리와 함께 지움 (커밋 안 함). `--result-only` 출력의 바깥 문자열 감싸기만 풀었다.

*1. 첫 Play Mode*

```
# Entered play mode
$ unity command cheat_run --id item.clear
{"ok":true,"session":"66409b58","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
# S1=66409b58
$ unity command cheat_status --execution 1 --session 66409b58
{"ok":true,"session":"66409b58","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Succeeded","done":true,"blocked":false,"message":"Success","command":"item_clear","describe":"Item/Clear Inventory"}}
```

*2. Play Mode 재시작 (AutoStaticsCleanup)*

```
# Exited play mode
# Entered play mode
# 2a. 재시작 직후 실행 0건, 이전 세션으로 조회
$ unity command cheat_history
{"ok":true,"session":"0f02c881","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"history":[]}
$ unity command cheat_status --execution 1 --session 66409b58
{"ok":false,"error":"Session changed (66409b58 -> 0f02c881). Execution 1 belongs to a previous session","code":"session_changed"}
# 2b. 새 #1 발급 후 이전 세션으로 조회 / 세션 생략
$ unity command cheat_run --id item.clear
{"ok":true,"session":"0f02c881","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"item_clear","describe":"Item/Clear Inventory"}}
$ unity command cheat_status --execution 1 --session 66409b58
{"ok":false,"error":"Session changed (66409b58 -> 0f02c881). Execution 1 belongs to a previous session","code":"session_changed"}
$ unity command cheat_status --execution 1
{"ok":true,"session":"0f02c881","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"execution":{"id":1,"cheatId":"item.clear","target":"Server","source":"Cli","status":"Succeeded","done":true,"blocked":false,"message":"Success","command":"item_clear","describe":"Item/Clear Inventory"}}
# S2=0f02c881
```

*3. Play Mode 중 재컴파일 (샘플 사본에 주석 한 줄 추가 → Refresh)*

```
# eval → refreshed, isPlaying=True
# eval → isPlaying=True initialized=False
$ unity command cheat_status --execution 1 --session 0f02c881
{"ok":false,"error":"CheatSystem is not initialized (enter Play Mode)","code":"not_initialized"}
$ unity command cheat_run --id item.clear
{"ok":false,"error":"CheatSystem is not initialized (enter Play Mode)","code":"not_initialized"}
```

*4. 재컴파일 뒤 다시 Initialize (run과 status 사이에 재초기화가 끼는 경우)*

```
# eval → initialized=True
$ unity command cheat_run --id item.clear
{"ok":false,"error":"Unknown cheat 'item.clear'"}
$ unity command cheat_status --execution 1 --session 0f02c881
{"ok":false,"error":"Session changed (0f02c881 -> 771cf2d9). Execution 1 belongs to a previous session","code":"session_changed"}
$ unity command cheat_status --execution 1
{"ok":false,"error":"Unknown execution 1","code":"unknown"}
```

해석 (확정):
- **AutoStaticsCleanup이 세션을 되돌림 (사용자 요청 근거)**: 같은 에디터에서 Play Mode를 정지 → 재시작하자 세션이 `66409b58` → `0f02c881`로 바뀌고 히스토리가 비었다. 실행 0건 상태에서 이전 세션 값으로 조회하면 `session_changed`.
  - 도메인 리로드가 아니라 AutoStaticsCleanup이 되돌렸다는 근거: 이 프로젝트는 Play Mode 진입 시 도메인 리로드를 끈다 (`eval → enabled=True options=DisableDomainReload, DisableSceneReload`, `ProjectSettings/EditorSettings.asset` `m_EnterPlayModeOptions: 3`). Unity 7에서는 `ResetStaticsOnDomainReload`가 `#if !(… || UNITY_6000_5_OR_NEWER)`로 컴파일에서 빠지므로 남은 리셋 경로는 `[AutoStaticsCleanup]`뿐이다.
- 같은 id(#1)가 다시 발급된 뒤에도 이전 세션 값이면 `session_changed`. `--session`을 빼면 새 세션의 #1(`Succeeded`)을 돌려준다 → 자동화가 반드시 넘겨야 하는 이유.
- Play Mode 중 재컴파일: `isPlaying=True initialized=False`, `status` · `run` 모두 `not_initialized`.
- 재컴파일 뒤 `Initialize`를 다시 부르면(= run과 status 사이에 재초기화가 끼는 경우) 세션이 `0f02c881` → `771cf2d9`로 바뀌고, 이전 세션 값 조회는 `session_changed`.
- 새로 발견 (범위 밖, 기록·README): 재컴파일 뒤 재초기화해도 치트 목록이 비어 있다(`Unknown cheat 'item.clear'`). 도메인 리로드로 레지스트리 정적 상태가 사라지고 `SubsystemRegistration`은 Play Mode 중에 다시 돌지 않기 때문으로 보임 (원인은 추정, 현상은 확정).

## 2026-10-17 HB_Cheat 에러 code 전수 부여 — com.hb.cheat 0.1.4 → 0.1.5

계획서: [docs/plans/2026-10-17_hb-cheat-error-codes.md](plans/2026-10-17_hb-cheat-error-codes.md)

### 커밋

| 커밋 | 내용 |
|---|---|
| 712642b | `CheatErrorCode` 19개, 모든 `ok:false`에 code, 실행 전 검증(B안), 미배포 code 이름 변경, 테스트, 계획서·critique 원문 |
| (이 커밋) | README code 표 · 버전 0.1.5 · 작업 로그 |

### 계획과 달라진 것 (확정)

- code 목록은 critique 반영 후의 "수정된 19개"로 확정 (사용자 결정). 판정은 계획서 `## 외부 검토 (critique)` 절.
- README "자동화 폴링 규칙"(번호 붙은 처리 지시)을 "폴링" 절로 바꿈: 사실(`session`·`done`·`blocked`·히스토리 보존)은 남기고, 처리 방법은 "예: … 멈추고 사람에게 넘긴다" 한 줄만 둠 (사용자 지시).
- `CheatProtocol.Error(string)`(code 없음)는 공개 API라 남기고 패키지 안에서는 쓰지 않음.
- `Validate` 안의 도달 불가 분기(정의 null, 인자 수 불일치)는 `internal_error`.

### critique 판정 집계 (계획 단계, 두 모델 따로 실행)

| 누가 찾았나 | 건수 | 채택 | 기각 |
|---|---|---|---|
| 둘 다 | 1 (C1 포괄 catch에 형식 오류 혼입) | 1 | 0 |
| gpt_mini_luna만 | 5 (C2 이름 대상, C3 값 오류 이름, C4 unknown 정의, C5 CLI 프레임워크 단계, C6 optionError 의미) | 5 (C5·C6은 직접 확인) | 0 |
| gemini_mini_lite만 | 3 (C7 malformed/usage 경계, C8 not_initialized 일관성, C9 Provider 메시지 노출) | 1 | 2 (C8 사실과 다름, C9 의도) |
| 지휘자만 | 1 ("목록에 없는 값"이 `ok:false`가 아니라 실행 실패라는 점 → Q1) | 1 | — |

### 테스트 (확정)

- `CheatErrorCodeTests` 4개 추가 → `HB.Cheat.Tests` **44/44 통과**.
  - code별 유발 입력 28개, 모든 계약 code(19개)가 테스트로 나오고 그 밖의 code는 없음(리플렉션으로 `CheatErrorCode` 상수와 대조).
  - 인자 문제는 실행 없음 / 실행 결과 문제는 `ok:true` + `Failed` / 패널 경로(`Execute` 직접)는 기존대로 `Failed` 기록.
- 테스트 작성 중 내 설계 실수 2건: ① `FixedOptions` 인자는 기본값(0)이 생겨 `missing_argument`를 만들 수 없음 → Provider 목록 인자로 변경. ② 의도된 Provider 예외 로그를 테스트 프레임워크가 실패로 봄 → 해당 테스트에서 `LogAssert.ignoreFailingMessages`.
- 기존 테스트 3개 기대값을 새 code 이름(`evicted_execution`, `unknown_execution`, `malformed_argument`)으로 갱신.

### 실제 확인 — Unity CLI, Play Mode (응답 원문)

샘플을 `Assets/Samples/HB_Cheat/`로 다시 복사해 쓰고, 끝난 뒤 생성 레지스트리와 함께 지움 (커밋 안 함). `--result-only` 출력의 바깥 문자열 감싸기만 풀었다. `option_provider_failed`·`internal_error`·브리지 전용 code(`empty_command`·`unknown_command`·`invalid_usage`)·`not_initialized`·`evicted_execution`은 샘플로 만들 수 없거나 앞선 작업에서 원문을 남겨 테스트로만 확인.

*malformed_argument*

```
$ unity command cheat_run --id item.add --args "itemId=1001 count"
{"ok":false,"error":"Expected k=v but got 'count'","code":"malformed_argument"}
```

*unknown_cheat*

```
$ unity command cheat_run --id no.such
{"ok":false,"error":"Unknown cheat 'no.such'","code":"unknown_cheat"}
```

*unknown_argument (--args 키)*

```
$ unity command cheat_run --id item.add --args "itemId=1001 amount=3"
{"ok":false,"error":"'item.add' has no argument 'amount'","code":"unknown_argument"}
```

*unknown_argument (--arg)*

```
$ unity command cheat_options --id item.add --arg amount
{"ok":false,"error":"'item.add' has no argument 'amount'","code":"unknown_argument"}
```

*argument_without_options*

```
$ unity command cheat_options --id item.add --arg count
{"ok":false,"error":"'count' has no option list","code":"argument_without_options"}
```

*unparsable_value*

```
$ unity command cheat_run --id item.add --args itemId=abc
{"ok":false,"error":"'itemId': 'abc' is not a valid Int","code":"unparsable_value"}
```

*missing_argument*

```
$ unity command cheat_run --id item.add --args count=3
{"ok":false,"error":"Missing argument 'itemId'","code":"missing_argument"}
```

*out_of_range_value*

```
$ unity command cheat_run --id item.add --args "itemId=1001 count=0"
{"ok":false,"error":"'개수' must be in [1, 9999]","code":"out_of_range_value"}
```

*unlisted_value*

```
$ unity command cheat_run --id item.add --args "itemId=99999 count=1"
{"ok":false,"error":"'아이템': 99999 is not in the option list","code":"unlisted_value"}
```

*인자 문제는 실행을 만들지 않음 (히스토리 0건)*

```
$ unity command cheat_history
{"ok":true,"session":"b30bec36","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"history":[]}
```

*raw_disabled*

```
$ unity command cheat_raw --command "item_add 1001 1"
{"ok":false,"error":"Raw commands from automation are disabled (CheatRuntimeOptions.AllowRawFromAutomation, Tools > HB > Cheat)","code":"raw_disabled"}
```

*empty_raw_command (AllowRawFromAutomation=true)*

```
# eval → AllowRawFromAutomation=true
$ unity command cheat_raw --command "   "
{"ok":false,"error":"Empty raw command","code":"empty_raw_command"}
```

*unknown_execution*

```
$ unity command cheat_status --execution 999
{"ok":false,"error":"Unknown execution 999","code":"unknown_execution"}
```

*session_changed*

```
$ unity command cheat_status --execution 1 --session deadbeef
{"ok":false,"error":"Session changed (deadbeef -> b30bec36). Execution 1 belongs to a previous session","code":"session_changed"}
```

*실행 결과 문제는 ok:true + Failed (가짜 네트워크가 모르는 명령으로 응답)*

```
$ unity command cheat_raw --command bogus_cmd
{"ok":true,"session":"b30bec36","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":1},"execution":{"id":1,"cheatId":null,"target":"Server","source":"Cli","status":"Pending","done":false,"blocked":false,"message":null,"command":"bogus_cmd","describe":"bogus_cmd"}}
$ unity command cheat_status --execution 1
{"ok":true,"session":"b30bec36","serverQueue":{"paused":false,"reason":null,"queued":0,"inFlight":null},"execution":{"id":1,"cheatId":null,"target":"Server","source":"Cli","status":"Failed","done":true,"blocked":false,"message":"InvalidCommand","command":"bogus_cmd","describe":"bogus_cmd"}}
```

*CLI 봉투 단계 (패키지 code 없음)*

```
$ unity command cheat_status --execution abc --format json
{
  "success": false,
  "command": "unity command cheat_status",
  "data": null,
  "errors": [
    {
      "code": "INVALID_COMMAND_ARGS",
      "message": "cheat_status의 인수가 잘못되었습니다:\n  --execution에는 Int32이(가) 필요하지만 abc이(가) 지정되었습니다."
    }
  ],
  "warnings": []
}
```

### 관찰 (범위 밖, 추정 포함)

- 컴파일 직후 첫 `unity command run_tests`가 2번 300초 시간 초과("Main thread operation timed out")로 끝났고, 바로 다시 돌리면 3~4초에 통과. 원인 미확인 (에디터 쪽 추정). 앞 작업에서도 "Test execution timed out, cancelling" 1회 기록.
- `CheatProtocol.Options`에서 Provider가 `Label`이 null인 `CheatOption`을 주고 `query`가 있으면 `option.Label.IndexOf`에서 NullReferenceException → `internal_error`로 나감 (코드 읽기로 확인, 실행 확인 안 함). 고치지 않음.

## 2026-10-17 HB_Cheat CLI Play 동안만 runInBackground — com.hb.cheat 0.1.5 → 0.1.6
계획서: [docs/plans/2026-10-17_hb-cheat-cli-run-in-background.md](plans/2026-10-17_hb-cheat-cli-run-in-background.md) · 지시서: [docs/work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md](work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md) (A 부분) · 실제 확인 기록: [docs/verify/2026-10-17_cli-run-in-background/README.md](verify/2026-10-17_cli-run-in-background/README.md)

### 변경 (확정)
- `cheat_play` / `cheat_stop` CLI 명령. `cheat_play`로 시작한 Play 동안만 `Application.runInBackground`를 메모리에서 켜고, 에디트 모드로 돌아오면 원래 값으로 되돌린다. 원래 true면 아무것도 하지 않는다. 디스크에 쓰지 않는다.
- `Editor/CliPlayRunInBackground.cs` (HB.Cheat.Editor, 심볼 조건 없음): SessionState에 소유·원래 값·대기. 복원 경로: EnteredEditMode(ExitingPlayMode에서 한 번 재적용), 정적 생성자(리로드 뒤 에디트 모드에서 남은 상태), Play 진입 실패(update 10회 유예), quitting, 다음 cheat_play 전 잔여 상태.
- `Editor/PipelineRuntimeConfigProbe.cs`: `RuntimePipelineConfig.json`만 읽어 Pipeline 런타임 드라이버 활성 판정 (없음 / disabled / enabled → `pipeline_driver` / 읽기 실패 → `pipeline_config_unreadable`). Pipeline 0.8.0-exp.1 기준 형식.
- `CheatSettings.runInBackgroundDuringCliPlay` (기본 켬). code 계약 3개 추가 (`already_playing`, `not_playing`, `play_mode_unavailable`, 19 → 22). README, package.json 0.1.6.
- `docs/todo.md` 신설: Play 동안만 Game 뷰 Play Unfocused 옵션 (A 범위 밖).

### 계획과 달라진 것 (확정)
- `CheatProtocol`(HB.Cheat 런타임, HB_CHEAT 조건)에 `PlayModeRequestError`·`PlayEntering`·`PlayExiting`을 두었다. HB.Cheat.Editor는 런타임을 참조할 수 없어(심볼 조건) code·JSON은 런타임 쪽, 판정은 에디터 쪽으로 나눴다.
- `HB.Cheat.Editor`에 `AssemblyInfo.cs`(InternalsVisibleTo: Cli, Tests) 추가. CLI·테스트 asmdef에 `HB.Cheat.Editor` 참조 추가.
- Pipeline 판정: `enableInBuilds`가 true면 `autoStart`와 무관하게 `pipeline_driver` (드라이버가 코드 리로드 푸시에 runInBackground를 켜기 때문).
- 실제 확인: frame은 `eval`로 `Time.frameCount` (TTESTT에 `boid.status` 없음), 0행은 `editor_play` (설정 에셋이 없어 끌 수 없음, 같은 경로), 4행 예외는 `eval`, 3행 재컴파일은 `RequestScriptCompilation` — 모두 사용자 승인.
- 0행 재현 조건: 회차 1~6에서 정지가 재현되지 않거나 1 → 2로 기준 불통과. 회차 7(Play Unfocused + 다른 모니터 메모장에서 계속 타이핑, ready 뒤 3초 대기)에서 통과. 정지는 "Play 동안 Unity가 OS 맨 앞에 오지 않을 때" 생긴다(관측). 조건에 따라 생기며 조건은 완전히 확정되지 않았다.

### 테스트 (확정)
- ToolCreate EditMode `--filter HB.Cheat`: 66개 통과 (새 22개: 컨트롤러 수명주기, Pipeline 판정 세 갈래, 응답 JSON, code 계약 22).
- 첫 실행에서 컴파일 오류 1건(public 테스트 인자에 internal enum, CS0051) → 문자열로 받도록 수정.

### 실제 확인 (TTESTT, Unity 6000.3.23f1, com.hb.cheat를 `file:` 경로로 잠시 설치)
| 행 | 판정 |
|---|---|
| 0 기준선 (editor_play, 창 뒤) | 회차 7 통과 (1 → 1) |
| 1 cheat_play (창 뒤) | 통과 (358 → 1566, Play 중 True/True/0, 종료 후 False/False/0, 로그 2줄). 응답 항목은 기록 실수로 미확인 → 3·4행에서 대체 확인 |
| 2 사람 툴바 Play | 통과 (값 변화 없음, 로그 0줄) |
| 3 Play 중 재컴파일 | 통과 |
| 4 Play 중 예외 | 통과 |
| 5 켠 채 정상 종료 → 다시 열기 | 통과 (`editor quitting` 경로, ProjectSettings 해시 같음) |
| 6 원래 true | 통과 (`already_true`, 쓰기 없음) |
| 7 설정 끔 | 미실시 / 대체 확인(EditMode `Disabled_DoesNothing`). 결정 수정 A로 0행으로 옮겼고, 0행은 설정 에셋이 없어 `editor_play`로 함(같은 경로). 설정 끔 상태의 cheat_play 자체는 실제 에디터에서 실행 안 함 |
| 8 도메인 리로드 끔 (1·3·6) | 통과 |
| 9 켠 채 SaveAssets | 통과 (디스크 0) |
| 10 정리 | TTESTT git status·27개 해시 = 시작 전, ToolCreate HB_Cheat 116개 해시 같음 |

- 관측 (추정 포함): 창 뒤 + cheat_play는 초당 약 129프레임으로 창 앞(600 안팎)보다 낮다 — runInBackground는 멈춤을 막지만 백그라운드 속도 제한은 남는다(추정). `editor_focus`는 이번에는 Unity를 실제로 맨 앞으로 가져왔지만, 사람이 다른 창에 입력 중이면 곧 포커스를 잃었다. 드롭다운 값은 메모리에만 있다가 에디터 종료 때 레이아웃에 저장됐다.
- 사건: 8행에서 Enter Play Mode Options를 바꾸기 전에 메모리 값을 읽지 않았다(지휘자 누락). Unity 6.3은 API로 `enabled 1 / options 0` 표현을 되살릴 수 없다. 결과적으로 디스크는 바뀌지 않았고(M5에서 git restore), 다시 연 뒤 메모리 값은 같은 `enabled=False options=None`이었다.
- 마무리에서 에디터가 바꾼 파일(lock·ProjectSettings·EditorBuildSettings 줄바꿈, Mobile_RPAsset URP 버전)은 사용자가 미리 고정한 규칙대로 git restore.

### 외부 리뷰 (review, runId 0aa25a710f, gpt_main_sol)
- 기각: 빈 설정 파일이 Disabled로 판정된다 (major) — Pipeline `RuntimePipelineConfig.Load`도 같은 `FromJsonOverwrite`로 기본값 → 드라이버 없음 (확정). 판정기의 목적이 "드라이버가 생기는가"라 Pipeline과 같게 둔다. 빈 파일 테스트 추가, 주석으로 근거.
- 기각: 접근 권한 없으면 `File.Exists` false → NoFile (major) — Pipeline도 같은 `File.Exists` → config null → 드라이버 없음 (확정).

### 규칙서 문안 후보 (반영은 사용자 결정, 계획서 결정·결정 2 참조)
- Play 모드 자동화 절 (계획서 "결정"의 최종 문안) + "AI 실행 전 Game 뷰 Play 모드 드롭다운 값을 읽어 기록한다. Unfocused가 아니면 사람에게 바꿔 달라고 요청한다. 지휘자가 직접 바꾸지 않는다." + "창이 뒤에 있는 동안의 성능 수치는 앞에 있을 때와 다를 수 있다" + "에디터 설정을 시험 때문에 바꿀 때는 바꾸기 전에 디스크 값과 메모리 값을 둘 다 기록한다. API로 원래 표현을 되살릴 수 없는 설정도 있으니, 되돌리기는 git 기준으로 한다."
- 0행 결과로 문안의 "`editor_play`로 시작하면 … 멈출 수 있다 (7장 관찰)"은 "조건에 따라 멈출 수 있다"로 맞는다.

## 2026-10-17 HB_Cheat 남은 확인 11~15 (B) — com.hb.cheat 0.1.6, ToolTest6.3 (6000.3.23f1)
계획서: [docs/plans/2026-10-17_hb-cheat-remaining-checks.md](plans/2026-10-17_hb-cheat-remaining-checks.md) · 실제 확인 기록: [docs/verify/2026-10-17_hb-cheat-b/README.md](verify/2026-10-17_hb-cheat-b/README.md) · 14 순서표: [14_mobile_checklist.md](verify/2026-10-17_hb-cheat-b/14_mobile_checklist.md)

코드 변경 없음 (관찰·기록). 결함은 고치지 않고 todo.

### 결과
| 항목 | 판정 |
|---|---|
| 11 6.3 컴파일 | 0.1.6 회차: 11 본체 컴파일 통과, InGameConsole 연동 컴파일 실패(결함, 원인 확정), 15-1 판정 보류 → InGameConsole HEAD로 올린 뒤 통과 |
| 15 Samples Import / pipeline 감지 / InGameConsole 감지 | 통과 / 통과 (꺼짐·켜짐) / 통과 (꺼짐·켜짐) |
| 12 서버 큐 멈춤 UI | 통과 |
| 13 마우스 클릭·드래그 | 통과 (터치 미확인 → 14) |
| 14 모바일 | 미실시, 순서표만 |

### 결함 (todo, 고치지 않음)
- InGameConsole 외부 탭 API를 추가하면서(`c451c74`) 버전을 1.0.0에서 올리지 않았고, HB_Cheat 감지 하한도 1.0.0 → 오래된 InGameConsole(`bfd6415a`)에서 연동 컴파일 에러 (`CS0117 AddExternalView` 등 4종).
- 런타임 설정(Prefer Console Tab)을 바꿔도 레지스트리가 다시 생성되지 않아 반영되지 않음. 설정 에셋도 디스크에 저장되지 않음.
- README에 "콘솔 탭 모드에서는 CHEAT 버튼이 없음" 안내 없음.

### 계획과 달라진 것 (확정)
- 설치 기준 `#84016a8`(0.1.5) → A 커밋 `869e2614…`(0.1.6), 전체 해시 (짧은 해시는 UPM이 거부).
- InGameConsole을 HEAD(`43f1f105…`)로 올려 진행 (기준선과 다름, 정리에서 `bfd6415a`로 되돌림).
- pipeline이 없는 동안 CLI 대신 Editor.log와 `Library/ScriptAssemblies`로 관측.
- 순서: 샘플 Import를 pipeline 뒤로.
- 15-3을 위해 InGameConsole을 쓰는 프로젝트 파일 3개를 `F:/AI/ToolTest6.3/_B_moved/`로 옮김 (되돌리지 않음).
- 13을 13-A(콘솔 탭)·13-B(독립 패널, Prefer Console Tab 끔 + Generate Registry)로 나눔.
- 지휘자 누락: 기준선에서 원본 사본을 남기지 않아, 정리 때 manifest·lock·디파인 줄을 재구성해 해시로 원본과 같음을 확인.

### 관측 (추정 포함)
- UPM git URL은 짧은 해시를 받지 않는다 (에러 원문 확정) → README 설치 안내 보강 후보.
- Unity Hub가 꺼져 있으면 패키지 추가 resolve가 멈춘다 (`hubIPCService` IPC 시간 초과, Hub를 켜자 진행).
- 창이 뒤에 있으면 manifest 변경 뒤 resolve는 되지만 스크립트 컴파일이 미뤄졌다 (사람 클릭 뒤 진행).
- Pipeline `screenshot`은 카메라 렌더만 담고 UI Toolkit 오버레이를 담지 않는다 (추정).
- 에디터 종료 때 Unity가 디파인 순서를 바꿔 저장했다.
- 정리 뒤 기준선 대비 차이: 레이아웃 파일 2개뿐 (`CurrentMaximizeLayout.dwlt`의 드롭다운 값 0 → 2).

### 규칙서 문안 후보 (추가)
- "패키지에 공개 API를 추가하면 버전을 올리고, 쓰는 쪽 감지 하한도 맞춘다".
- "확인 0단계에서 해시와 함께 원본 파일 사본도 남긴다 (git이 없는 프로젝트는 특히). 정리 때 해시가 다르면 사본으로 되돌린다." (사용자, B 기준선 누락 뒤)
