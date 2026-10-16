# HB_Cheat 자동화 정비 (6장 5절)

## 요약
7장에서 지휘자가 Unity CLI(`unity command cheat_*`)로 치트를 부를 때 깨지는 다섯 곳을 고친다: 예외 대신 JSON 에러, 끝나지 않은 실행의 조회 보장, 서버 큐 정지 상태의 JSON 노출, 실행 주체 기록, 자동화 경로의 raw 차단 옵션.
패널(사람용) 동작은 그대로 둔다. 바뀌는 곳은 히스토리 표시 두 군데이며 이유를 아래에 적었다.
EditMode 테스트 어셈블리를 패키지 안에 새로 만들고, 끝나면 0.1.2 → 0.1.3으로 올린다.

## 배경
- 대상: `Assets/Tools/HB_Cheat` (com.hb.cheat 0.1.2, CustomUnityTools 저장소 = 이 저장소).
- 자동화 진입점은 두 개다.
  - Unity CLI: `Editor/Cli/CheatCliCommands.cs` → `CheatProtocol.List/Options/Run/Raw/Status/History` 직접 호출.
  - 브리지(ADB·소켓): `CheatProtocol.Handle(line)` (전체가 try/catch로 감싸져 있음).
- 사람이 패널을 누를 때는 사람이 화면을 보고 판단하므로 드러나지 않던 문제가, 기계가 JSON만 보고 폴링할 때 드러난다.
- 환경 (확정): Unity 7000.0.0a8, `com.unity.pipeline` 0.8.0-exp.1, 에디터가 이 프로젝트로 열려 있음(`unity status`: ready). `unity command --query cheat`에 cheat_* 6개, `--query test`에 `run_tests`가 노출돼 있다. `com.unity.test-framework` 1.1.33은 간접 의존(packages-lock)으로만 있다.
- 패키지에 테스트가 없다 (확정: `Tests` 폴더·테스트 asmdef 없음).

## 조사 결과 — 의심 지점 판정

| # | 판정 | 근거 |
|---|---|---|
| 1 | **확정** | `CheatCliCommands.cs:28,37`이 `CheatProtocol.ParsePairs(string)`을 try 없이 부른다. `ParsePairs`는 `=`가 없는 토큰에 `FormatException`을 던진다(`CheatProtocol.cs:281`). 브리지 경로(`Handle`)는 바깥 try/catch가 있어 JSON 에러가 나간다 → CLI 경로만 문제. CLI 프레임워크가 예외를 어떤 모양으로 돌려주는지는 **미확인**(실제 호출로 확인, 아래 "실제 확인" 1). |
| 2 | **확정** | `AddHistory`(`CheatSystem.cs:627`)가 용량 초과 시 맨 앞을 상태와 무관하게 지운다. Pending 실행은 `s_ServerQueue`/`s_InFlight`에만 남고, `FindExecution`(`:454`)은 `s_History`만 찾는다 → `cheat_status`가 `Unknown execution`. 더 나쁜 점: 그 실행이 나중에 끝나도 어디서도 찾을 수 없어 **결과가 영영 사라진다**. 큐가 멈춰 있고 자동화가 계속 넣으면(기본 용량 50) 쉽게 일어난다. 패널 히스토리에서도 진행 중인 건이 사라진다. |
| 3 | **확정** | `WriteExecution`(`CheatProtocol.cs:216`)에 큐 정보가 없다. 정지 사실은 대기 건의 `message`(`"Paused: ... Waiting for the late response or ResumeServerQueue"`)와 시간 초과 건의 `message`(`"... Server queue paused"`)에만 있다. 자동화는 Pending을 계속 폴링하게 된다. 재개 수단도 자동화 경로에는 없다(`ResumeServerQueue`는 패널 버튼·코드만). |
| 4 | **확정** | `CheatExecution`에 주체 필드가 없다. `Execute`/`ExecuteRaw` 호출자: 패널(`CheatPanel.cs:575,751`), `CheatProtocol.Run/Raw`(CLI·브리지 공용), 프로젝트 코드. |
| 5 | **확정** | `CheatProtocol.Raw`는 빈 문자열만 막고 `ExecuteRaw`로 보낸다. CLI(`cheat_raw`)와 브리지(`Handle "raw ..."`) 모두 열려 있고 끌 방법이 없다. |
| 기각 ① | **기각 확인** | 초기화 전 `s_MainThreadId == -1`이라 `ReportServerResult`가 호출 스레드에서 바로 `HandleServerResult`로 간다. 하지만 초기화 전에는 `SendToServer`가 거절하므로(`:501`) `s_InFlight == null`, 큐 정지도 Shutdown에서만 걸리고 Shutdown 시점엔 `s_MainThreadId`가 이미 설정돼 있다 → 경고 로그(스레드 안전)만 남기고 false. 상태를 건드리지 않는다. |
| 기각 ② | **기록만** | `CheatBuildGuard.cs:71` `fileName.StartsWith("HB.Cheat")`는 `HB.CheatSheet.dll` 같은 무관한 어셈블리도 막는다. 막는 쪽 오탐이라 릴리즈에 치트가 섞이는 위험은 없음. 고치지 않는다. |

### 조사 중 추가로 찾은 것 (의심 목록에 없던 것)
- A. `TryBuildValues`가 `query`/`limit` 키를 건너뛴다(`CheatProtocol.cs:180`). 인자 이름이 `query`·`limit`인 치트는 프로토콜로 값을 넣을 수 없다. **이번 범위 밖, 기록만** (README에 예약 이름으로 적는다).
- B. 큐가 멈춘 동안 자동화가 계속 넣으면 대기열이 무한정 늘어난다. 3번에서 `run` 응답에 정지 상태를 실어 자동화가 멈추게 하는 것으로 대응하고, 대기열 상한은 만들지 않는다(사람 패널 동작 변경이 되므로).
- C. `CheatCliCommands`의 다른 명령은 현재 경로상 예외가 나갈 곳이 없어 보이지만(`GetOptions`·`RunLocal`·Provider는 내부에서 catch), 확인은 코드 읽기 수준 **추정**이다 → 1번에서 모든 CLI 명령을 같은 보호막으로 감싼다.

## 계획

순서는 의존 관계 기준: 0 → 1 → 4 → 2 → 3 → 5 → 문서·버전. 항목마다 구현 → 컴파일 확인 → 관련 테스트 → 커밋.

### 0. 테스트 어셈블리 (먼저)
- 위치: `Assets/Tools/HB_Cheat/Tests/Editor/HB.Cheat.Tests.Editor.asmdef`
  - references: `HB.Cheat`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` / precompiled `nunit.framework.dll`
  - includePlatforms: Editor, defineConstraints: `HB_CHEAT`, `UNITY_INCLUDE_TESTS`
  - `autoReferenced: false` → 패키지 사용자 프로젝트에서는 `testables`에 넣지 않는 한 돌지 않는다.
- `Runtime/AssemblyInfo.cs`에 `[assembly: InternalsVisibleTo("HB.Cheat.Tests.Editor")]`.
- 서버 치트 경로 테스트에는 초기화가 필요한데, `Initialize`는 GameObject·UI 패널을 만든다. EditMode에서 쓰기 위해 **에디터 전용 내부 시험 진입점**을 둔다 (→ 질문 Q3):
  `#if UNITY_EDITOR internal static void InitializeHeadlessForTests() / ResetForTests()` — 러너 없이 `s_IsInitialized`와 메인 스레드 id만 세팅, 시간 초과는 `Tick()`을 테스트가 직접 부른다.
- 테스트용 `CheatDefinition`은 코드로 만들어 `CheatRegistry`에 등록·해제한다 (등록 API가 내부면 InternalsVisibleTo로).

### 1. CLI가 항상 JSON을 돌려주게
- `CheatProtocol`에 `public static bool TryParsePairs(string text, out Dictionary<string,string> pairs, out string error)` 추가. 기존 `ParsePairs(string)`는 공개 API이므로 남긴다(던지는 동작 그대로, 주석에 명시).
- `CheatCliCommands`의 모든 명령을 `Guard(Func<string>)`로 감싼다: 예외 → `CheatProtocol.Error(e.Message)` + `Debug.LogException`.
- Options/Run은 `TryParsePairs` 사용 → 실패 시 `{"ok":false,"error":"Expected k=v but got 'level'"}`.
- 패널 영향: 없음.

### 4. 실행 주체 기록 (5번이 주체를 쓰므로 2·3보다 먼저)
- `public enum CheatSource { Code, Panel, Cli, Bridge }`, `CheatExecution.Source { get; }`.
- `CheatSystem.Execute(def, values)` / `ExecuteRaw(command)` → 기존 시그니처 유지(= `Code`), 주체를 받는 오버로드 추가.
- 패널은 `Panel`, `CheatCliCommands`는 `Cli`, `CheatProtocol.Handle(line)`은 `Bridge`(브리지 전용 진입점이라는 기존 주석과 일치).
  `CheatProtocol.Run/Raw`에는 `CheatSource source = CheatSource.Code` 선택 인자를 붙인다 (소스 호환, UPM 소스 패키지라 바이너리 호환 문제 없음).
- JSON: 실행 객체에 `"source":"Cli"`.
- **패널 변경(표시만)**: 히스토리 카드 머리줄 `#3  Succeeded` → `#3  Succeeded · CLI` (주체가 Panel이면 생략). 이유: 같은 기기에서 QA와 자동화가 동시에 쓰면 사람이 "내가 누르지 않은 실행"을 구분할 수 있어야 한다. 버튼·동작은 그대로. (→ 질문 Q4)

### 2. 끝나지 않은 실행은 항상 조회 가능
- `AddHistory` 정리 규칙: 용량을 넘으면 **가장 오래된 끝난(IsDone) 항목**부터 지운다. 끝나지 않은 항목은 지우지 않는다 → 히스토리가 일시적으로 `용량 + 대기 건수`까지 늘 수 있다.
- 실행이 끝날 때(`Finish`) 용량을 넘어 있으면 그때 다시 정리한다 (`TrimHistory()`를 AddHistory·Finish 공용으로).
- `CheatProtocol.Status`: 못 찾았는데 `id < 다음 id`면 `{"ok":false,"error":"Execution 3 was removed from history (capacity 50)","code":"evicted"}`, 처음 보는 id면 `"code":"unknown"`. 자동화가 "지워진 것"과 "잘못된 id"를 구분해 폴링을 끝낼 수 있다. (`ok:false` 형식은 유지, 필드만 추가)
- **패널 변경**: 진행 중인 서버 치트가 히스토리에서 사라지지 않는다. 지금 동작(진행 중인 건이 목록에서 사라졌다가 결과도 못 봄)이 버그에 가깝기 때문에 고친다.

### 3. 서버 큐 상태를 JSON에
- `run` / `raw` / `status` / `history` 응답 최상위에 같은 객체를 붙인다:
  ```json
  "serverQueue": {"paused": true, "reason": "#7 timed out", "queued": 3, "inFlight": null}
  ```
- 실행 객체에 `"blocked": true` — Pending이면서 큐가 멈춰 있어 **스스로는 진행되지 않는** 경우(대기열 안에 있고 큐 정지). 자동화 규칙을 README에 적는다: `done == true` 또는 `blocked == true`면 폴링을 멈춘다.
- 시간 초과로 끝난 실행은 이미 `done:true, status:"TimedOut"`이므로 따로 손대지 않는다.
- 재개 명령(`cheat_resume` / `resume` 동사)은 넣을지 질문으로 올린다 (→ Q2). 기본안은 **넣지 않음**: 재개는 재접속 뒤에 해야 안전한데 자동화는 재접속 여부를 모른다.
- 패널 영향: 없음 (패널은 이미 `IsServerQueuePaused`를 직접 읽는다).

### 5. 자동화 경로의 raw 차단 옵션
- `CheatRuntimeOptions.AllowRawFromAutomation` (bool), `CheatSettings`에 같은 필드 + 에디터 창 토글, `CheatCodeGenerator`가 생성 레지스트리에 값 기록(기존 `ServerTimeoutSeconds`와 같은 방식).
- `CheatProtocol.Raw`에서 주체가 `Cli`/`Bridge`이고 옵션이 꺼져 있으면 `{"ok":false,"error":"Raw commands from automation are disabled (CheatRuntimeOptions.AllowRawFromAutomation)","code":"raw_disabled"}`. 실행 객체를 만들지 않는다(히스토리에 남기지 않음).
- 패널 raw·코드 `ExecuteRaw`는 영향 없음.
- 기본값은 질문 Q1.

### 6. 문서·버전
- README 자동화 절: 실패 JSON 형식과 `code`, `source`, `serverQueue`/`blocked` 폴링 규칙, raw 옵션, `query`/`limit` 예약 이름, 테스트 실행법.
- `package.json` 0.1.2 → 0.1.3.
- `docs/worklog.md`에 결과 절(첫 줄 계획서 링크). 리뷰 표에 "누가 찾았나(지휘자만 / 에이전트만 / 둘 다)" 열.

### 외부 리뷰
- 2·3번 구현 후 각각(또는 묶어서 한 번) `review` 모드. `reason` 필수, `ownReview`는 결과를 보기 전에 완성한 내 리뷰 원문.
- input에는 목적·제약·변경 의도만. 위 의심 지점 표와 내 판정은 넣지 않는다. 코드는 `context`로 (`CheatSystem.cs` 히스토리·큐·Finish·Shutdown 구간, `CheatProtocol.cs` 실행 JSON 구간, `CheatPanel.cs` 히스토리 구간).

## 버린 대안과 이유
- 2번: `FindExecution`이 `s_InFlight`·`s_ServerQueue`까지 찾게 하기 — 진행 중에는 보이지만 끝나는 순간 다시 어디에도 없어 결과를 잃는다.
- 2번: id → 실행 사전을 따로 두고 별도 상한 — 상태 저장소가 둘이 되고 상한도 둘이 된다. 이득이 "히스토리 표시 개수 고정" 하나뿐.
- 2번: 기본 용량을 늘리기 — 확률만 줄일 뿐 같은 문제.
- 1번: `ParsePairs(string)`를 던지지 않게 바꾸기 — 공개 API 동작 변경. 새 `TryParsePairs`를 추가하는 쪽이 안전.
- 3번: 정지 상태를 `message` 문자열 규칙으로 유지 — 자동화가 문자열을 파싱하게 되고 문구를 바꾸면 깨진다.
- 4번: `Execute`에 주체를 필수 인자로 — 프로젝트 코드의 기존 호출이 깨진다.
- 테스트: PlayMode 테스트로 `Initialize`를 그대로 쓰기 — 느리고 UI 생성까지 엮인다. 단, Q3에서 시험 진입점이 싫다면 이쪽으로 간다.

## 위험
- 2번 정리 규칙 변경으로 히스토리가 용량을 넘을 수 있다. 큐가 멈춘 채 자동화가 계속 넣으면 커진다 → 3번 `blocked`로 자동화가 멈추게 하는 것이 전제. 상한 없음은 의도된 선택(패널 동작 유지).
- `Finish`에서 정리하면 `ExecutionChanged` 알림 시점에 히스토리가 바뀐다. 패널 `RebuildNav`가 알림 안에서 히스토리를 순회하므로 순서(정리 → 알림)를 지켜야 한다.
- 4번 선택 인자 추가는 `CheatProtocol.Run`을 메서드 그룹으로 넘기는 외부 코드가 있다면 깨진다 (패키지 안에는 없음, 확정).
- 시험 진입점(`#if UNITY_EDITOR internal`)은 런타임 어셈블리에 테스트용 코드가 들어가는 것. 빌드에는 포함되지 않는다.
- 간접 의존 test-framework 1.1.33이 Unity 7 알파에서 동작하는지 **미확인**. 에디터의 `run_tests`가 노출돼 있어 가능성은 높음.
- 에디터가 열려 있어 배치 `unity test`는 못 쓴다 → `unity command run_tests` 사용.

## 테스트 (EditMode, `HB.Cheat.Tests.Editor`)
- 1: `TryParsePairs` — 정상, `=` 없음, 빈 키(`=v`), 따옴표 값. 실패 시 예외 없음.
- 1: `CheatProtocol.Error` 출력이 JSON으로 파싱되고 `ok:false`.
- 4: Execute 오버로드별 `Source`, JSON에 `source` 포함. 기존 오버로드는 `Code`.
- 2: 용량 2, 서버 치트 3건을 큐 정지 상태로 넣기 → 셋 다 `FindExecution` 가능. 하나 끝내면 용량으로 돌아오고 끝난 것부터 빠짐. 지워진 id → `code:"evicted"`, 미래 id → `code:"unknown"`.
- 3: 정지 상태에서 `run`·`status`·`history`에 `serverQueue.paused:true`, 대기 건 `blocked:true`. 재개 후 `blocked:false`.
- 5: 옵션 off + 주체 Cli/Bridge → `raw_disabled`, 히스토리 늘지 않음. 주체 Code/Panel은 그대로 전송.
- 실행: `unity command run_tests --mode EditMode --filter HB.Cheat.Tests` (관련 항목만), 마지막에 전체 한 번.

## 실제 확인 (7장 경로: 지휘자 → Unity CLI)
`unity command editor_play`로 Play Mode 진입 후 (이 프로젝트에 등록된 치트 사용, 없으면 샘플 import 여부를 먼저 알림):
1. **고치기 전 기준선**: `cheat_list`, `cheat_run --id <치트> --args "level"`(잘못된 형식), `cheat_status --execution <id>` 결과를 그대로 로그에 남긴다.
2. 고친 뒤 같은 세 호출 + 큐 정지 상태의 `cheat_status`·`cheat_history`, raw 옵션 off의 `cheat_raw`.
3. 결과 JSON 원문을 `docs/worklog.md`에 남긴다. 끝나면 `editor_stop`.

## 사용자에게 물을 것
- **Q1. `AllowRawFromAutomation` 기본값** — (a) `true`: 기존 동작 유지, "옵션을 비워 두면 기존 동작" 원칙과 일치 **(추천)** / (b) `false`: 자동화에서 raw는 명시적으로 켜야만. 서버에 임의 문자열이 가는 통로라 안전 쪽을 원하면 (b). 브리지는 실기기 개발 빌드까지 닿는다는 점이 (b)의 근거.
- **Q2. 자동화용 재개 명령(`cheat_resume`)을 넣을까** — 기본안은 넣지 않음. 넣으면 자동화가 막힘을 스스로 풀 수 있지만, 재접속 전에 풀면 늦은 응답이 다음 결과로 붙는다.
- **Q3. 테스트용 내부 진입점(`#if UNITY_EDITOR`)을 런타임 어셈블리에 둬도 되는가** — 아니면 PlayMode 테스트로 실제 `Initialize`를 쓴다(느림).
- **Q4. 패널 히스토리에 주체 표시(`· CLI`)를 넣을까** — 표시만 바뀌고 동작은 같다. 싫으면 JSON에만 노출.

## 결정

사용자 답 (2026-10-17, 원문):

> 1. false 로 할게. 4장 언어검사 처럼 의도적 예외로 두고 7장 자동화에는 raw 가 필요 없어, 서버로 임의 문자열이 가는 통로는 켜야만 열리게
> 2. ㅇㅋ 널지말자 blocked 면 멈추고 나에게 넘겨줭
> 3. 괜찮앙 대신 Runner -> Tick 연결은 실제확인에서 꼭 봐줘 (Play Mode 호출)
> 4. 넣자 7장에서 자동화 돌 때 내가 화면에서 구분할 수 있게.
>
> 진행해줘!

바뀐 계획 항목:
- 5번: `AllowRawFromAutomation` 기본값 **false**. "옵션을 비워 두면 기존 동작" 원칙의 **의도적 예외**로 README·주석에 명시한다 (자동화 경로의 `cheat_raw`/`raw`는 0.1.3부터 기본 거절).
- 3번: `cheat_resume`/`resume` 명령을 넣지 않는다. README 자동화 규칙: `blocked == true`면 폴링을 멈추고 사람에게 넘긴다.
- 0번: 시험 진입점(`#if UNITY_EDITOR internal`)으로 간다. 대신 테스트가 우회하는 **러너 → `Tick` 연결**(실제 러너가 매 프레임 `Tick`을 불러 시간 초과·스레드 밖 응답이 처리되는지)은 실제 확인에서 Play Mode 호출로 반드시 본다 → 실제 확인 2에 "짧은 시간 초과로 서버 치트를 보내 TimedOut + `serverQueue.paused` + 대기 건 `blocked`가 되는지" 추가.
- 4번: 패널 히스토리에 주체 표시(`· CLI`)를 넣는다.
