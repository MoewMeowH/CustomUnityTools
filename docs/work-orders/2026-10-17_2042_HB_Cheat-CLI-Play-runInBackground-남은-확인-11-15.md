# 작업 지시서: HB_Cheat — CLI Play 동안 runInBackground + 남은 확인 항목 (11~15)

## 목적
- **A. 구현:** 7장 검증에서 지휘자가 `eval`로 손으로 하던 "Play 동안만 `runInBackground`를 켜고 끝나면 되돌리기"를 HB_Cheat 에디터 도구로 옮긴다. **자동화가 시작한 Play에만** 적용하고, 사람이 에디터에서 누른 Play와 디스크의 프로젝트 설정은 건드리지 않는다.
- **B. 확인:** A가 커밋된 뒤, 지금까지 코드 경로나 이벤트 재현으로만 확인한 todo 11~15를 실제 환경에서 관찰·기록한다. 결함이 나오면 고치지 않고 원인 추정과 함께 기록한 뒤 멈추고 보고한다. 고칠지는 사용자가 정한다.

대상: HB_Cheat (`Assets/Tools/HB_Cheat`, com.hb.cheat 0.1.5)
- CLI 명령: `Assets/Tools/HB_Cheat/Editor/Cli/CheatCliCommands.cs:17-107`
- CLI asmdef: `Assets/Tools/HB_Cheat/Editor/Cli/HB.Cheat.Editor.Cli.asmdef` (defineConstraints `HB_CHEAT`·`HB_CHEAT_PIPELINE`, versionDefines `com.unity.pipeline`)
- 심볼과 무관하게 컴파일되는 에디터 asmdef: `Assets/Tools/HB_Cheat/Editor/HB.Cheat.Editor.asmdef` (참조·제약 없음)
- 설정: `Assets/Tools/HB_Cheat/Editor/CheatSettings.cs:49-51` (Automation 헤더, `allowRawFromAutomation` 선례)
- code 계약: `Assets/Tools/HB_Cheat/Runtime/CheatErrorCode.cs` (19개), `Assets/Tools/HB_Cheat/Tests/Editor/CheatErrorCodeTests.cs:92`
- 7장 원본 기록 (확정): `F:/TTESTT/docs/verify/2026-10-17_boid/worklog.md` — 요청의 `docs/verify/2026-10-17_boid/`는 ToolCreate가 아니라 TTESTT에 있다. 같은 내용의 Notion 정리본: `F:/AI/Notion용/_notion_appendix7/C4-21.md`
- 관련 관찰 기록: `F:/AI/SampleCheck/SampleCheck/docs/worklog.md` (D절, "의심 지점 대조" 표의 10번)
- Pipeline 소스 (0.8.0-exp.1): `F:/TTESTT/Library/PackageCache/com.unity.pipeline@62c08c808737/`
  - `Editor/Commands/PlayModeCommands.cs:15-39` (editor_play / editor_stop)
  - `Editor/Commands/FocusEditorCommand.cs` (editor_focus)
  - `Runtime/PlayerSupport/RuntimePipelineDriver.cs:43-53, 469-477, 660-670, 717-722` (Pipeline 자체 runInBackground 덮기·복원)
- 참고 (외부, MIT): uLoop `Packages/src/Editor/FirstPartyTools/ControlPlayMode/CliPlayModeRunInBackgroundController.cs` — 구조만 참고, 코드 복사 금지

## 진행 방식
- **A와 B는 순서대로.** A 커밋 전에는 B를 시작하지 않는다.
- **A 1단계:** 코드와 7장 기록을 먼저 조사하고 계획서만 낸다: `docs/plans/2026-10-17_hb-cheat-cli-run-in-background.md`. 승인 전에는 코드를 고치지 않는다. 사용자 답은 계획서 끝 `## 결정`에 원문 그대로 붙인다.
- 아래 의심 지점은 확인 전 추정이다. 확정 / 기각 / 범위 조정을 근거와 함께 계획서에 적는다.
- **A 2단계:** 구현 → 컴파일 → EditMode 테스트(`--filter`로 HB.Cheat 관련만) → **`review`** → 실제 확인 → 커밋.
  - `review`는 구현 뒤, 커밋 전에 코드(diff + Play 상태 전환·도메인 리로드·수명주기 관련 주변 코드)를 대상으로 한다. Play 상태 전환·도메인 리로드·수명주기에 걸친 변경이라 필수다. `reason` 필수. `ownReview`는 결과를 보기 전에 직접 찾은 것으로 채운다.
  - 이 지시서의 의심 지점과 그외 이상사항은 에이전트 input에 넣지 않는다 (독립성 유지). 요구사항·제약·통과 기준은 넣는다.
  - TTESTT 기록·Pipeline 소스는 프로젝트 루트 밖이고 ExtraRoots에 `@orch`만 등록돼 있다(확정). 리뷰에 필요하면 CLAUDE.md "프로젝트 루트 밖의 코드를 리뷰할 때" 절차를 따른다.
- **7장 규칙서:** 원본은 HB_Orchestration의 `project-files/CLAUDE.md`(`@orch`로 읽기 전용). 이 세션에서는 고치지 않는다. 바꿀 문안만 계획서에 적고, 원본 수정과 동기화 스크립트 실행은 오케스트레이션 저장소 쪽에서 따로 한다.
- **B:** A 커밋 뒤 별도 계획서 `docs/plans/2026-10-17_hb-cheat-remaining-checks.md`. 항목마다 통과 기준을 실행 전에 적고 바꾸지 않는다.
  - **결함 발견 시:** 그 자리에서 기록하고 멈춘 뒤 보고한다 (다음 항목으로 넘어가지 않는다).
  - **사람 조작 항목:** 지휘자는 준비(빌드, 씬, 설정, 확인 순서표)까지 하고 멈춘 뒤 사용자에게 넘긴다. 사용자가 조작한 결과를 받아 기록한다. 사람이 한 조작은 "사람 조작"으로 따로 표시한다.
  - 클릭 대신 `onClick.Invoke()` 같은 코드 호출로 처리한 것은 "대체 확인"으로 표시하고, 원래 항목은 미확인으로 둔다.
- 결정, 리뷰 판정(채택·기각과 근거), 예상과 달랐던 것은 `docs/worklog.md`에 남긴다.

## 원칙
- 옵션은 비워 두면 기존 동작과 같다. 설정이 꺼져 있으면 지금과 완전히 같아야 한다 (기본값은 사용자 결정).
- 실패는 예외가 아니라 `{"ok":false,"error":"...","code":"..."}` JSON으로 돌려준다. 새 code가 필요하면 `CheatErrorCode`에 추가하고 계약 테스트를 갱신한다 (6장 code 계약).
- 패널(사람용) 동작은 바꾸지 않는다. 바꿔야 하면 계획서에 이유를 적는다.
- **사람이 누른 Play에는 손대지 않는다.** "자동화가 시작한 Play"를 어떻게 구분하는지가 A의 핵심이다.
- **HB_Cheat는 설치된 프로젝트의 기존 설정을 존중하고 강제하지 않는다.** (사용자 결정 3)
  - A는 자동화가 시작한 Play 동안에만 메모리상 값을 바꾸고 반드시 원래 값으로 되돌린다.
  - `ProjectSettings.asset`이나 PlayerSettings를 디스크에 쓰지 않는다.
  - 원래 값이 이미 true인 프로젝트(ToolCreate처럼)에서는 아무것도 하지 않는다. 덮어쓴 것도 되돌린 것도 없어야 한다.
- **반드시 되돌린다.** 정상 종료, 예외, 도메인 리로드, Play 중 재컴파일, 에디터 재시작 어느 경로에서도 바뀐 값이 남지 않는다.
  - 코드가 바꾸는 값은 `Application.runInBackground`다. 7장에서 이 값을 바꾸자 `PlayerSettings.runInBackground`도 같이 바뀌었다(확정, `24_runInBackground_on.json`: `before app=False ps=False | after app=True ps=True`). 그래서 확인은 `Application.runInBackground`·`PlayerSettings.runInBackground`(메모리)·`ProjectSettings.asset`(디스크) 세 값을 따로 본다. 디스크 값은 어떤 경우에도 바뀌지 않아야 한다.
- 에디터 전용 코드. 릴리즈 빌드 제외 규칙(6장 3.4)을 그대로 지킨다.
- uLoop 코드는 복사하지 않고 구조만 참고한다. 참고했다는 사실과 라이선스(MIT)를 계획서에 적는다.
- B는 관찰과 기록이 목적이다. 코드 변경은 하지 않는다.

## A. 정할 것 (계획서에서 질문으로, 요청 원문)
1. **자동화가 시작한 Play를 어떻게 구분할까?** 장단점을 코드 근거로 정리한다.
   - (a) HB_Cheat에 Play 시작 명령을 새로 두고, 그 명령으로 시작한 Play만 대상으로 한다. 예: `[CliCommand] cheat_play` / `cheat_stop`.
   - (b) Unity Pipeline 내장 `editor_play`를 그대로 쓰고, 다른 신호로 구분한다.
   - (c) 그 밖에 찾은 방법.
2. 기본값을 켬으로 할까, 끔으로 할까? (4장 언어 검사, 6장 raw 차단처럼 "의도적 예외"로 둘지)
3. 응답 JSON에 상태를 넣을까? 예: `runInBackgroundOverride: {active, original}`.
4. 7장 규칙서(CLAUDE.md)의 Unity CLI 절차에 무엇을 바꿔 적을까? `editor_focus`는 OS 창을 앞으로 가져오지 않는다는 관찰도 함께 남길지 정한다.

## A. 실제 확인 (요청 원문 + 행 구성. 통과 기준은 실행 전에 칸마다 적고 바꾸지 않는다)
요청 원문:
- 다른 창을 맨 앞에 둔 상태로 자동화가 Play를 시작했을 때 프레임이 진행되는지. 7장 4.5와 같은 방법(`boid.status`의 frame 차이)으로 본다.
- Play를 끝낸 뒤 `Application.runInBackground`, `PlayerSettings.runInBackground`, `ProjectSettings.asset`의 값이 시작 전과 같은지.
- 사람이 직접 누른 Play에서는 값이 바뀌지 않는지.
- Play 중 재컴파일 뒤의 상태.
- 테스트 프로젝트(TTESTT 또는 SampleCheck)에서 확인하고, 끝나면 git status가 시작 전과 같은지 확인한다.

행 구성 (각 행에 초기 상태 / 조작 주체 / 관찰 값 / 통과 기준을 적는다):
| # | 프로젝트 | 시나리오 | 조작 주체 |
|---|---|---|---|
| 1 | TTESTT 또는 SampleCheck (원래 값 0, 리로드 설정은 확인 후 기록) | 다른 창을 맨 앞에 둔 채 자동화 Play → frame 두 번 읽기 → 종료 → 세 값 | 지휘자 (창 배치는 사람 조작) |
| 2 | 같음 | 사람이 에디터에서 Play → 세 값 (Play 중·종료 후) | 사람 조작 |
| 3 | 같음 | 자동화 Play 중 재컴파일 → 상태·종료 후 세 값 | 지휘자 |
| 4 | 같음 | 자동화 Play 중 예외(종료 경로) / 도메인 리로드 | 지휘자 |
| 5 | 같음 | 덮기 중 에디터 재시작(정상 종료, 가능하면 강제 종료) → 다시 열고 세 값 | 지휘자 (강제 종료 여부는 사용자 승인) |
| 6 | 같음, 시작 전 값이 true인 상태 | 자동화 Play → 종료. **아무것도 하지 않았는지**: 덮어쓰기·되돌리기 기록 없음, 값은 Play 내내 true, 디스크 0 그대로 | 지휘자 |
| 7 | 같음 | 설정 끔 상태에서 자동화 Play → 값이 바뀌지 않음 (기존 동작과 같음) | 지휘자 |
| 8 | 같음, Enter Play Mode Options로 도메인 리로드 끔 | 1·3·6행을 다시 (false·true 두 경우, 재컴파일 포함) | 지휘자 |

- 도메인 리로드 켬/끔 (사용자 결정 2-5): 같은 확인 장소에서 옵션을 잠시 바꿔 두 상태를 모두 본다. 바꾸기 전에 `ProjectSettings/EditorSettings.asset` SHA-256을 남기고, 끝나면 되돌려 해시가 같은지 확인한다.
- "원래 값"은 HB_Cheat가 Play를 켜기 직전, 에디트 모드에서 읽은 값으로 정의한다 (사용자 결정 2-6).

- **ToolCreate는 A 확인 장소로 쓰지 않는다** (사용자 결정 3). 원래 값 false 경우와 true 경우를 모두 0인 프로젝트에서 본다.
- 6행의 "시작 전 true"를 디스크를 건드리지 않고 만드는 방법은 계획서에서 정한다 (추정 후보: Play 전에 `eval`로 메모리 값만 true로 바꾸고, 시험 뒤 false로 되돌린 다음 디스크 값을 확인. 이 준비 조작 자체는 도구 동작이 아니라 "시험 준비"로 따로 기록한다).
- "아무것도 하지 않았다"를 판정할 관찰 수단(응답 JSON 상태, 로그, SessionState 기록 등)을 계획서에서 정하고, 6행 통과 기준에 쓴다.
- 확인에 쓰는 프로젝트의 시작 전 `git status --porcelain`과 `ProjectSettings.asset`의 runInBackground 줄을 파일로 저장하고 끝난 뒤 비교한다. SampleCheck는 시작 전부터 변경이 있다(확정: `HBCheatRegistry.g.cs` M, `BoidCellCheats.cs` 미추적 등).

## B. 항목 (요청 원문 그대로)
11. Unity 6.3에서 실제 컴파일 (ToolTest6.3 프로젝트에 UPM으로 설치)
    - 6.3 에디터 dll에 BuildProfile.scriptingDefines, textEdition.placeholder API가 있는지만 확인했고 실제 컴파일은 안 함
12. 서버 큐 멈춤 UI 확인 (Play 모드)
    - CheatSystem.Options.ServerTimeoutSeconds = 0.5f + CheatLogTransport로 서버 치트 2개 전송 -> 시간 초과
    - 패널 History 탭에 "Server queue paused" 안내와 Resume 버튼이 뜨는지, 대기 건 메시지가 "Paused: ..."로 바뀌는지
    - Resume 버튼 클릭 -> 다음 건이 전송되는지 (코드 경로는 테스트했고 화면·클릭만 미확인)
13. 실제 클릭·터치 확인: 버튼 클릭, CHEAT 버튼 드래그 이동 (지금까지는 submit 이벤트로만 재현)
14. 모바일 실기기 (Android/iOS): OS 키보드 인셋, 안전 영역(노치), 한글 폰트, 검색창·문자열 인자 입력
15. UPM git URL 설치 환경에서 확인: Samples Import, versionDefines 감지(InGameConsole 연동, pipeline CLI 명령)

누가 무엇을 하나 (계획서에서 확정):
- 지휘자 단독으로 가능 (추정): 11 컴파일, 15 설치·감지, 12의 상태 준비(타임아웃·전송·큐 정지까지)
- 사람이 해야 함: 12의 Resume 버튼 클릭과 화면 확인, 13 실제 클릭·드래그, 14 실기기
- 화면 확인이 필요한 항목은 스크린샷이나 패널 상태를 읽는 방법이 있으면 계획서에 제안한다.
- 12는 A가 끝난 뒤라 CLI로 시작한 Play에서 할 수 있다. A의 실제 확인과 겹치는 준비가 있으면 같이 묶는다.
- 11·15를 끝낸 뒤 그 프로젝트의 상태와 패키지 설정(`manifest.json`)을 시작 전과 비교해 보고한다.

11·15 절차 (사용자 결정 1·2):
- **비교 방법:** 파일 해시. git init은 하지 않고 6.3 프로젝트도 바꾸지 않는다.
  - 시작 전 스냅샷: `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/` 아래 파일별 SHA-256. `Library/`, `Temp/`, `Logs/` 같은 생성 폴더는 뺀다.
  - 끝난 뒤 같은 방식으로 다시 떠서 차이를 보고한다. 줄바꿈만 다른 파일은 "내용 diff 0"으로 따로 표시한다.
- **순서:**
  1. com.hb.cheat만 git URL(`#84016a8`)로 설치 → 컴파일(11) → pipeline 연동 코드가 꺼진 상태로 컴파일되는지 확인. 짧은 해시가 해석되지 않으면 전체 해시 `84016a8b1cc913b698d4ef73a7f37d71d27408e7`를 쓰고 그 사실을 기록한다.
  2. com.unity.pipeline 추가 → 연동이 켜지고 `cheat_*` CLI 명령이 보이는지 확인
  3. InGameConsole: 이미 설치돼 있으므로 manifest에서 잠시 빼서 "꺼짐"을 보고, 다시 넣어 "켜짐"을 본다
  4. manifest.json, packages-lock.json을 시작 전 스냅샷으로 되돌리고 해시가 시작 전과 같은지 확인한다. com.hb.cheat도 같이 빠진다. 남겨 둘 것은 그때 사용자가 정한다.
     - 시험 때문에 켠 scripting define(`HB_CHEAT`, `DEV_CHEAT` 등)도 시작 전 값으로 되돌린다. 되돌린 뒤 `ProjectSettings/` 파일 해시를 시작 전과 비교한다. 디파인 말고 남은 차이(Unity 자동 변경 등)는 되돌리지 않고 보고만 한다.
     - 되돌린 뒤 InGameConsole이 원래대로 컴파일·동작하는지 확인한다.
     - `Assets/` 아래 생긴 것(샘플 Import, 생성 레지스트리, 설정 에셋)은 기록하지 않고 지우지 않는다. 대신 되돌린 뒤 이 파일들 때문에 컴파일 오류(Safe Mode)가 나지 않는지 본다. 오류가 나면 지우지 말고 멈추고 보고한다.
     - 샘플 Import 파일에도 치트 파일과 같은 보호(`#if DEV_CHEAT` 또는 심볼 조건 asmdef)가 있는지 본다. 사전 조사(확정): `Samples~/CheatExamples`의 치트 파일 6개는 `#if DEV_CHEAT`로 시작하고, `ExampleGameData.cs`만 이 보호가 없다(HB.Cheat를 참조하지 않는 가짜 게임 데이터·네트워크 코드). 생성 레지스트리는 `#if HB_CHEAT && <UserDefine>`으로 감싼다(`CheatCodeGenerator.cs:339`). 설정 에셋은 패키지가 빠지면 스크립트 없는 에셋이 된다(추정: 컴파일 오류가 아니라 경고).

## B. 정할 것 (계획서에서 질문으로, 요청 원문)
1. 11~15의 순서. 제안: 11 → 15 → 12 → 13 → 14
2. 14를 이번 범위에 넣을지 (장비·빌드 시간이 크다. 이번에 할지, 순서표만 만들고 미룰지)
3. 15의 git URL은 어느 브랜치나 태그를 쓸지 (현재 0.1.5 커밋 기준인지)

## 의심 지점 (추정)
1. `PlayModeCommands.cs:15-26` (Pipeline) — `editor_play`는 `EditorApplication.isPlaying = true` 한 줄이고, 명령 실행 전후로 쓸 수 있는 공개 이벤트를 찾지 못했다(`PipelineTransactionLog`는 internal) / (b)는 "CLI에서 왔다"는 신호를 직접 얻기 어렵다 / Pipeline에 명령 디스패치 공개 훅이 있는지 다시 찾고, 다른 신호 후보(예: 사전 무장 명령을 부른 뒤 다음 Play 진입에서 소비, Play 진입 시 `InternalEditorUtility.isApplicationActive`)를 비교표에 넣는다. 창 활성 상태는 "CLI가 시작했다"는 신호가 아니므로 쓸 경우 오판 사례(포커스가 있는 채로 자동화 Play, 포커스를 잃은 채로 사람 Play가 가능한지)를 검증 필요로 표시한다.
2. `RuntimePipelineDriver.cs:43-53, 469-477, 660-670, 717-722` (Pipeline) — Pipeline 런타임 드라이버가 자체로 `Application.runInBackground`를 덮고 복원한다 / 에디터 Play에서도 이 드라이버가 돌면 소유자가 둘이 되어 원래 값 기록·복원 순서가 엇갈릴 수 있다 / 에디터 Play에서 드라이버가 활성인지, Console에 `Pipeline: forced Application.runInBackground = true` 로그가 나오는지 확인.
3. `FocusEditorCommand.cs` (Pipeline) — 소스가 있다(`GUIView.Focus`를 리플렉션으로 호출, 주석상 "activates the OS window"). 7장 기록의 "소스 미확인"은 갱신 대상이다 / 호출은 성공해도 창이 앞에 오지 않는 원인은 Windows의 전경 창 전환 제한일 수 있다 / 규칙서 문안은 원인을 단정하지 않고 "응답이 성공이어도 OS 창이 앞에 오지 않을 수 있다"는 관찰로 적는다.
4. 관찰 엇갈림 — SampleCheck 기록(D절, 의심 10 판정)은 "editor_focus 뒤에는 정지 없음", TTESTT 기록은 "editor_focus 뒤에도 정지, 사람 클릭 뒤 진행" / 그때 다른 창이 맨 앞에 있었는지 등 조건 차이로 보인다 / 규칙서 문안에 두 기록을 함께 반영하고 "항상 실패"로 쓰지 않는다.
5. `HB.Cheat.Editor.Cli.asmdef` defineConstraints — 복원 코드를 이 어셈블리에 두면, 덮기 중 `HB_CHEAT`를 끄거나 HB_Cheat·Pipeline 패키지를 제거했을 때 복원 코드가 컴파일에서 빠져 값이 켜진 채 남을 수 있다 / 복원 코드를 둘 어셈블리를 계획서에서 정한다 (후보: 제약 없는 `HB.Cheat.Editor`).
6. 덮기 중 저장 경로 — 7장에서는 켜 둔 동안에도 디스크에 1이 쓰이지 않았다(확정, TTESTT worklog 219줄). 그러나 덮기 중 `AssetDatabase.SaveAssets`(예: `CheatSettings.cs:117`)나 에디터 종료 시 저장이 끼면 `ProjectSettings.asset`에 1이 쓰일 수 있다 / 덮기 중 SaveAssets 호출 뒤, 그리고 덮기 중 에디터를 닫은 뒤 디스크 값을 확인. `EditorApplication.quitting`에서 복원하는지도 계획서에 적는다.
7. 프로젝트 차이 (확정 값) — ToolCreate: `ProjectSettings.asset:104 runInBackground: 1`(ee389db, 테스트용으로 켠 것 — 사용자 결정 3, 그대로 둔다), `EditorSettings m_EnterPlayModeOptions: 3`(도메인 리로드 끔). TTESTT·SampleCheck·ToolTest6.3: `runInBackground: 0`, `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 0` / 리로드를 끈 프로젝트에서는 static 필드 기반 상태가 Play 종료 뒤에도 남을 수 있다 / ToolCreate는 A 확인 장소에서 빠졌으므로, 리로드 끔 경로를 어디서 볼지는 "사용자 결정" 절의 새 확인 사항 5로 넘긴다. 옵션 값 0의 실제 리로드 동작은 에디터에서 확인 후 기록.
8. Play 중 재컴파일·비정상 종료 — `SessionState`는 에디터 프로세스가 살아 있는 동안만 남는다 / 강제 종료 뒤 재시작하면 기록이 없어지므로, 디스크에 1이 쓰이지 않았다는 전제(6번)가 깨지면 복구 근거가 없다 / 재시작 후 메모리 값이 디스크에서 다시 읽히는지, 남은 상태를 어떻게 처리하는지 확인.
9. B-11 — TTESTT·SampleCheck가 이미 6000.3.23f1 + com.hb.cheat git URL(packages-lock hash d22291e)에서 cheat_run까지 돌았다(확정) / 6.3 컴파일은 사실상 한 번 지나갔을 수 있다 / 11은 요청대로 ToolTest6.3에서 하고, 이 선행 결과는 참고로만 계획서에 적는다.
10. B-11·15 — ToolTest6.3(`F:/AI/ToolTest6.3/ToolTest6.3`, 6000.3.23f1)은 git 저장소가 아니고(확정: `fatal: not a git repository`), manifest에 com.hb.cheat·com.unity.pipeline이 없다(확정) / 요청의 "git status 비교"를 그대로 할 수 없고, 15의 pipeline CLI 감지에는 manifest에 pipeline 추가가 필요하다 / 비교 방법을 사용자에게 묻는다.
11. B-15 — 저장소에 태그가 없다(확정). HB_Cheat 폴더는 0.1.5 커밋 84016a8과 HEAD d22291e 사이 차이가 없다(확정) / A 커밋 뒤에는 HEAD가 0.1.5와 달라진다 / 브랜치·태그·커밋 해시(`#<hash>`) 중 무엇을 쓸지 사용자 결정으로 올린다.

## 함께 확인할 것
- 이 지시서는 work_order(gpt_mini_luna · gemini_main_flash)로 검토받았다 (runId 2d90e09f3f).
- A 계획서에서 답할 것: 위 "A. 정할 것" 1~4. 특히 1번은 (a)/(b)/(c)를 의심 지점 1·2·5의 근거로 비교한다.
- B 계획서에서 답할 것: 위 "B. 정할 것" 1~3과 아래 "사용자 판단 필요".
- 실행 세션이 사용자에게 먼저 확인할 것: A 실제 확인 5행의 강제 종료 시험을 할지 (에디터 강제 종료는 Library·씬에 영향이 있을 수 있다).

## 그외 이상사항
- 반영: B 항목(12의 설정값·기대 문구, 15의 Samples Import·versionDefines 등)과 A 실제 확인(boid.status frame 차이 등)이 초안에서 축약됐다 — "A. 실제 확인", "B. 항목", "B. 정할 것" 절에 요청 원문을 그대로 넣었다 (공통)
- 반영: 실제 확인 행렬의 재컴파일이 어떤 Play 유형인지, 예외·리로드·재시작·디스크 값 검증이 빠졌다 — 행마다 시나리오·조작 주체를 적은 표로 바꾸고 4·5·7행(예외/리로드, 재시작, 설정 끔)을 더했다 (luna)
- 반영: ExtraRoots 등록을 사용자 질문으로 둔 것은 요청 범위를 넘는다 — 질문에서 빼고 "진행 방식"에 CLAUDE.md 절차를 따른다고 적었다 (공통)
- 반영: review가 계획서 검토인지 코드 검토인지, 언제 하는지 불명확 — 구현 뒤·커밋 전 코드 리뷰로 명시하고, 에이전트 input에서 빼는 것은 의심 지점·그외 이상사항뿐이며 요구사항·통과 기준은 넣는다고 적었다 (luna)
- 반영: 규칙서 파일을 수정하지 말라는 제한이 없다 — "진행 방식"에 읽기 전용, 문안만 계획서에 쓴다고 명시했다 (luna)
- 반영: 창 활성 상태(`isApplicationActive`)는 CLI 시작 신호가 아니다 — 의심 지점 1에서 "검증 필요 후보"로 낮추고 오판 사례를 적게 했다 (luna)
- 반영: B-11을 할지 묻는 것은 요청 항목을 선택 사항으로 낮춘다 — 의심 지점 9를 "요청대로 ToolTest6.3에서 하고 선행 결과는 참고"로 고쳤다 (luna)
- 반영: 결함 발견 시 멈춤과 사람 조작 대기의 경계가 모호 — "진행 방식" B 항목에 두 경우를 나눠 적었다 (flash)
- 반영: ToolCreate는 원래 값이 1이라 git status 비교 기준을 따로 둬야 한다 — "A. 실제 확인"에 ToolCreate 포함 모든 프로젝트의 시작 전 상태 저장을 적었다 (flash)
- 반영: 에디터 종료(`EditorApplication.quitting`) 시 복원 누락, 리로드 끔 프로젝트의 static 상태 잔류 — 의심 지점 6·7에 넣었다 (flash)
- 반영: 옵션 꺼짐이 기존 동작과 같은지, 값 세 개를 따로 관찰 — 실제 확인 7행, 원칙의 "반드시 되돌린다" 하위 항목에 넣었다 (luna)
- 반영: 복원 코드를 둘 어셈블리 — 의심 지점 5에 후보(`HB.Cheat.Editor`)로만 적었다. 어느 쪽에 둘지는 계획서에서 정한다 (flash는 확정 지시를 제안했으나 구현 방법은 지시서에서 정하지 않는다)
- 기각: "PlayerSettings까지 복원하는 것은 요청 범위 초과" (luna) — 요청 원문 "함께 확인할 것"이 `PlayerSettings.runInBackground`를 명시하고, 7장에서 Application 값을 바꾸면 PlayerSettings도 같이 바뀌었다(확정). 대신 원칙에 "코드가 바꾸는 값은 Application, 확인은 세 값"으로 관계를 분리해 적었다.
- 기각: "사람 조작 항목에서 멈추는 것은 관찰을 하지 않고 넘기는 것" (luna) — 요청 원문이 "지휘자는 순서표를 만든 뒤 멈추고 넘긴다"고 정했다.
- 기각: "ToolTest6.3이 git 저장소가 아니라는 근거가 없다" (luna) — 조사에서 `git status`가 `fatal: not a git repository`로 끝났다(확정). 의심 지점 10에 확정으로 표시했다.
- 기각: "의심 지점 1~4·6·7·8은 자료가 없어 확인 필요" (luna) — 검토 input에 Pipeline 소스·7장 기록을 붙이지 않아서 생긴 판정이다. 원문 위치(파일:줄)는 대상 절에 적었고, 실행 세션이 직접 확인한다.
- 기각: 백그라운드 프레임레이트 제한으로 boid.status 갱신이 늦을 수 있다 (flash) — 7장에서 runInBackground를 켠 회차는 모두 프레임이 진행됐다(확정). frame 차이로 판정하는 방법은 그대로 쓴다.
- 기각: 0.5초 경계에서 타이머 만료와 UI 갱신이 경쟁할 수 있다 (flash) — 근거가 없고, 12는 관찰 항목이라 그대로 관찰해 기록하면 된다. 의심 지점 12(0.5는 하한 경계값)도 결론에 영향이 없어 뺐다.
- 결정됨 (사용자 결정 1): ToolTest6.3은 git 저장소가 아니라 요청의 "git status 비교"를 할 수 없다 — (가) 시작 전 `Packages/manifest.json`·`packages-lock.json`·`ProjectSettings/*`·`Assets` 목록의 SHA-256을 저장해 비교 (git status와 같지 않다: 새 파일·삭제를 따로 봐야 함) / (나) 시작 전에 `git init` + 첫 커밋을 만들어 비교 (테스트 프로젝트에 저장소가 생긴다) / (다) SampleCheck 등 git 저장소가 있는 6.3 프로젝트로 바꿈 (요청은 ToolTest6.3)
- 결정됨 (사용자 결정 2): 15에서 pipeline CLI 감지를 보려면 ToolTest6.3 manifest에 com.unity.pipeline을 추가해야 한다 — 끝난 뒤 되돌릴지, 그대로 둘지

## 사용자 결정 (지시서 작성 후)

사용자 답 원문:

> 1. ToolTest6.3 비교 방법: 파일 해시로 한다.
> - git init은 하지 않고, 6.3 프로젝트도 바꾸지 않는다.
> - 시작 전 스냅샷: Packages/manifest.json, Packages/packages-lock.json, ProjectSettings/ 아래 파일별 SHA-256.
> - Library/, Temp/, Logs/ 같은 생성 폴더는 비교에서 뺀다.
> - 끝난 뒤 같은 방식으로 다시 떠서 차이를 보고한다. 줄바꿈만 다른 파일은 "내용 diff 0"으로 따로 표시한다(7장 정리 때처럼).
>
> 2. pipeline 추가 후 처리: 넣고 확인한 뒤 되돌린다.
> - 15의 versionDefines는 양쪽을 다 봐야 한다. 그래서 순서를 이렇게 한다.
>   (1) com.hb.cheat만 git URL(#84016a8)로 설치 → 컴파일(11) → pipeline 연동 코드가 꺼진 상태로 컴파일되는지 확인
>   (2) com.unity.pipeline 추가 → 연동이 켜지고 cheat_* CLI 명령이 보이는지 확인
>   (3) InGameConsole 연동도 같은 방식으로 꺼짐/켜짐을 본다(설치 가능하면)
> - 끝나면 manifest.json, packages-lock.json을 시작 전 스냅샷으로 되돌리고, 해시가 시작 전과 같은지 확인한다. com.hb.cheat도 같이 빠진다.
> - 남겨 둘 게 있으면 그때 내가 정한다.
>
> 3. ToolCreate의 runInBackground: 1 (ee389db)
> - 테스트용으로 켠 것이다. 그대로 둔다.
> - 이번 A 확인 장소로는 쓰지 않는다(0인 프로젝트에서 확인).
> - 설계 원칙으로 하나 못 박아 줘. HB_Cheat는 설치된 프로젝트의 기존 설정을 존중하고 강제하지 않는다.
>   - A는 자동화가 시작한 Play 동안에만 메모리상 값을 바꾸고 반드시 원래 값으로 되돌린다.
>   - ProjectSettings.asset이나 PlayerSettings를 디스크에 쓰지 않는다.
>   - 원래 값이 이미 true인 프로젝트(ToolCreate처럼)에서는 아무것도 하지 않는다. 덮어쓴 것도 되돌린 것도 없어야 한다.
> - 이 원칙이 지켜지는지 실제 확인에 넣어 줘: 시작 전 true인 경우와 false인 경우를 둘 다 본다.

이 답으로 바뀐 항목:
- 원칙: "기존 설정을 존중하고 강제하지 않는다" 원칙과 하위 세 줄을 추가했다.
- A. 실제 확인: ToolCreate 행(구 6행)을 빼고 "시작 전 true" 행을 0인 프로젝트로 넣었다. ToolCreate는 확인 장소에서 뺐다.
- B. 항목: 11·15 절차(해시 스냅샷, 설치 순서 1~4, 되돌리기)를 추가했다.
- 의심 지점 7: ToolCreate의 runInBackground 1은 테스트용으로 켠 것으로 확정. 그대로 둔다. 리로드 끔 프로젝트 경로는 아래 "결정 뒤 새로 생긴 확인 사항"으로 넘긴다.
- 의심 지점 10·11, 그외 이상사항의 "사용자 판단 필요" 두 건: 결정됐다.

결정 뒤 새로 생긴 확인 사항 (추정, 실행 세션이 계획서에서 확정 / 기각):
1. **`#84016a8` 짧은 해시** — UPM git URL이 짧은 커밋 해시를 받는지 확인하지 않았다. 해석에 실패하면 전체 해시 `84016a8b1cc913b698d4ef73a7f37d71d27408e7`를 쓰고 그 사실을 기록한다. 이 커밋은 0.1.5이고 A가 들어 있지 않다 (B-11·15는 A와 무관한 항목이라 문제는 없어 보인다).
2. **HB_CHEAT 심볼** — CLI asmdef는 `HB_CHEAT`·`HB_CHEAT_PIPELINE`가 둘 다 있어야 컴파일된다. 1단계에서 "pipeline 연동이 꺼짐"을 보려면 `HB_CHEAT`가 켜진 상태여야 한다 (아니면 다른 이유로 꺼진다). 심볼을 켜면 `ProjectSettings/ProjectSettings.asset`의 scripting define이 바뀌는데, 사용자 결정 2의 되돌리기 대상은 manifest·lock뿐이다 → ProjectSettings 변경은 스냅샷 비교에 차이로 보고하고 되돌릴지 사용자에게 묻는다.
3. **InGameConsole 이미 설치됨** — ToolTest6.3 manifest에 `com.hb.ingameconsole`(git URL)이 이미 있다(확정). 3단계의 "꺼짐"을 보려면 일시적으로 manifest에서 빼야 한다. 4단계 되돌리기로 원상 복구된다.
4. **스냅샷 범위 밖의 변경** — Samples Import(`Assets/Samples/`), 레지스트리 생성(`Assets/Cheats/Generated` 등), 설정 에셋 생성(`Assets/Editor/HBCheat/HBCheatSettings.asset`)은 `Assets/` 아래라 사용자 결정 1의 스냅샷 범위 밖이다. 이 파일들의 목록을 따로 기록하고, 지울지는 사용자에게 묻는다.
5. **도메인 리로드 끔 경로** — ToolCreate를 쓰지 않으면, 리로드를 끈 프로젝트에서의 확인(의심 지점 7)이 빈다. TTESTT·SampleCheck는 `m_EnterPlayModeOptions: 0`이다. 이 경로를 어디서 볼지(시험 중 Enter Play Mode 옵션을 바꿨다가 되돌림 / EditMode 테스트로만 확인 / 범위 밖) 계획서에서 사용자에게 묻는다.
6. **"아무것도 하지 않음" 판정과 Pipeline 드라이버** — 원래 값이 true인 경우 HB_Cheat는 아무것도 하지 않아야 한다. 의심 지점 2의 Pipeline 런타임 드라이버가 에디터 Play에서 따로 덮기·복원을 하면, HB_Cheat가 Play를 시작할 때 본 "원래 값"이 Pipeline이 바꾼 값일 수 있다. 원래 값을 언제 읽는지를 계획서에 적는다.

## 사용자 결정 2 (새 확인 사항 1~6에 대한 답)

사용자 답 원문:

> 1. 짧은 해시: 그렇게 해 줘. 실패하면 전체 해시를 쓰고 기록한다.
>
> 2. ProjectSettings: 디파인도 함께 되돌린다.
> - 시험 때문에 켠 scripting define(HB_CHEAT, DEV_CHEAT 등)은 끝날 때 시작 전 값으로 되돌린다.
> - 되돌린 뒤 ProjectSettings 파일 해시를 시작 전과 비교한다. 디파인 말고 남은 차이(Unity가 자동으로 바꾼 것 등)는 되돌리지 말고 보고만 한다.
>
> 3. InGameConsole: 잠시 빼서 "꺼짐"을 보고, 4단계에서 manifest를 되돌려 원래대로 돌아오게 한다. 돌아온 뒤 InGameConsole이 원래대로 컴파일·동작하는지까지 확인한다.
>
> 4. Assets/ 아래 생긴 것(샘플 Import, 생성 레지스트리, 설정 에셋): 기록하지 않아도 되고 지우지 않고 그대로 둔다.
> - 대신 하나만 확인해 줘. manifest·디파인을 되돌린 뒤에도 남은 파일 때문에 컴파일 오류(Safe Mode)가 나지 않는지 본다.
> - 설계상 치트 파일은 #if DEV_CHEAT나 심볼 조건 asmdef로 빠져야 한다. 샘플 Import 파일에도 같은 보호가 있는지 본다.
> - 오류가 나면 지우지 말고 멈추고 보고한다.
>
> 5. 도메인 리로드 끔 경로: 시험 중 옵션을 잠시 바꾸는 쪽으로 한다.
> - 확인 장소(TTESTT 또는 SampleCheck)에서 Enter Play Mode Options로 도메인 리로드를 끈 상태와 켠 상태를 둘 다 본다.
> - 바꾸기 전에 EditorSettings.asset 해시를 남기고, 끝나면 되돌려 해시가 같은지 확인한다.
>
> 6. "원래 값"을 읽는 시점: HB_Cheat가 Play를 켜기 직전, 에디트 모드에서 읽은 값으로 정의한다.
> - 종료 때 누가 마지막으로 쓰는지는 3번 조사(RuntimePipelineDriver가 에디터 Play에서 도는지) 결과로 정한다.
>   - 플레이어 빌드에서만 돈다 → 충돌 없음, 그대로 진행
>   - 에디터 Play에서도 Play 내내 true로 유지한다 → 이 기능이 필요 없거나 서로 덮는다. 멈추고 보고
>   - 일부만 한다(켜기만, 되돌리기 시점이 다름 등) → 순서와 소유권 규칙을 계획서에 적고 질문으로 올린다

이 답으로 바뀐 항목:
- B. 11·15 절차: 1단계에 전체 해시 대체를, 3단계에 InGameConsole을 잠시 빼고 다시 넣는 방법을 적었다. 4단계에는 디파인 되돌리기와 ProjectSettings 해시 비교(남은 차이는 보고만), InGameConsole 복귀 확인, `Assets/` 잔여 파일로 인한 Safe Mode 확인(오류가 나면 멈춤), 샘플 보호 확인을 넣었다.
- A. 실제 확인: 8행(도메인 리로드 끔)과 EditorSettings.asset 해시 저장·복원을 넣었다. "원래 값"의 정의를 적었다.
- 사용자 결정 1의 새 확인 사항 4(Assets 목록 기록)는 기록하지 않는 것으로 바뀌었다.
- 사용자 답의 "3번 조사"는 이 지시서의 **의심 지점 2**(RuntimePipelineDriver가 에디터 Play에서 도는지)다. 계획서 1단계에서 먼저 조사하고, 결과에 따라 아래와 같이 나눈다.
  - 플레이어 빌드에서만 돈다 → 충돌 없음, 계획대로 진행
  - 에디터 Play에서도 Play 내내 true로 유지한다 → **계획서를 더 쓰지 않고 멈추고 보고**
  - 일부만 한다(켜기만, 되돌리는 시점이 다름 등) → 순서와 소유권 규칙을 계획서에 적고 질문으로 올린다
