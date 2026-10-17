# HB_InGameConsole 패키지 점검 로그 (2026-10-18)

## 0. 기준
- 패키지: `Assets/Tools/HB_InGameConsole` (1.0.0). 런타임 `HB.InGameConsole`(HLogCore 778 · UI 1631 · InGameConsole 222 · Settings 132 · LogChannel 250줄), 에디터 `HB.InGameConsole.Editor`(콘솔 창 1431 · 채널 생성기 530 등), `Resources/`(UXML · USS · TSS).
- ToolTest6.3 기준선 다시 백업: 사용자가 manifest를 바꿔 둔 상태(TestFrameworkHelper `#277d985…`) → scratchpad `tt63_backup2/` (manifest `0c70bb6c…`, lock `742f7303…`).

## 1. 실제 확인 (ToolTest6.3, 6000.3.23f1, `file:` 로컬 패키지, PlayMode 테스트 `Assets/IGCCheck/`)
| 항목 | 결과 |
|---|---|
| 백그라운드 스레드에서 새 채널 (`HLogCore.GetChannel("Worker…")`, 콘솔 UI 있음) | **Task Faulted: `UnityException: VisualElementCreation can only be called from the main thread.`** 호출한 사용자 코드로 예외가 던져짐 (확정) |
| 로그 1줄당 GC 할당 (에디터 배치, `GC Allocated In Frame` 카운터, 200줄/프레임) | `Debug.Log`만 ≈3.1KB · +HLogCore ≈2.9KB(오차 범위) · **+콘솔 UI(닫힘) ≈12.5KB** → 닫힌 콘솔이 줄마다 ≈9KB 추가. 에디터는 스택 추출 비용이 커서 플레이어와 절대값이 다름 (추정) |
| `GC.GetAllocatedBytesForCurrentThread` | Unity Mono에서 0 반환 → 측정 방법으로 쓰지 않음 |

## 2. 코드 검토
### 큰 것
- **A. 릴리즈 빌드에서도 동작 (확정, 코드).** `InGameConsole.AutoInitializeOnGameStart`(AfterSceneLoad)와 `HLogCore.AutoInitialize`(BeforeSceneLoad)에 빌드 조건이 없다. 기본 설정(`InitMode = AutoOnGameStart`, `AutoCreateUI = true`)이면 출시 빌드에도 우상단 `Logs` 버튼이 뜨고, 모든 로그를 스택 트레이스와 함께 1000개 보관하며, 줄마다 UI 행을 만든다. `Resources/`의 UXML·USS·TSS는 항상 빌드에 포함. (HB_Cheat는 릴리즈에서 엄격히 제외하는 것과 대조)
- **B. 스레드 (확정, 실측).** `HLogCore.RegisterTag`가 `OnTagDiscovered`를 호출 스레드에서 바로 발생시킨다(`HLogCore.cs:358-361`). 다른 이벤트(`OnWatchUpdated` 등)는 `RaiseOnMainThread`로 넘기는데 이것만 빠짐. 인게임 UI는 그 자리에서 버튼을 만들어 예외, 에디터 창은 `Repaint()`를 다른 스레드에서 호출.
- **C. 닫힌 콘솔의 로그 비용 (실측).** `HandleLogAdded` → `AddLogElement`가 콘솔이 닫혀 있어도 줄마다 VisualElement 5개 + 클릭 람다를 만들고 최대 1000줄 유지 (가상화 없는 ScrollView).
- **D. README 예제가 컴파일되지 않음 (확정).** 생성 채널 클래스·`LogChannel`에 `LogWarning`이 없고, 생성 채널 클래스에 `LogError`가 없다. README 빠른 시작은 `HLog.Common.LogWarning` · `HLog.Common.LogError` · `HLog.Channel("Quest").LogWarning`을 쓴다. README 주요 특징 2도 `LogWarning`에 Conditional이 있다고 적음.
- **E. 자동 생성(Auto-Bootstrap).** `[InitializeOnLoadMethod]`마다 `LogChannels` MonoScript가 없으면 `Assets/Scripts/LogChannels.cs`를 만든다. 지워도 다음 리로드에 다시 생김, 사용자에게 묻지 않음, 기본 위치는 Assembly-CSharp(README에 asmdef 안내 있음).

### 마이너
- README 메뉴 경로 불일치: README `Tools > InGameConsole > Console Window` · `… > Log Channel Generator` / 실제 `Tools/HB/In-Game Console/…`.
- 런타임 Watch 게터는 인게임 UI의 `Update`에서만 평가 → `AutoCreateUI = false`면 런타임에 게터가 갱신되지 않음 (에디터 플레이 중은 EditorApplication.update로 평가).
- `MaxLogCount`(1000) 도달 후 `List.RemoveAt(0)`(코어·UI 둘 다) — 줄마다 1000칸 이동.
- `LogEntry` 생성마다 정규식 2회.
- `HLogCore.Clear()`마다 `DiscoverAllChannels()`(6.3에선 전체 어셈블리 `GetType` 순회).
- `catch { }`로 예외를 삼키는 곳 여러 군데 (채널 탐색, PanelSettings 리플렉션).

## 3. 테스트 프로젝트 상태
- 수정 후 재확인을 위해 `Assets/IGCCheck/`와 manifest(`com.hb.ingameconsole` → `file:`)를 남겨 둠.

## 4. 외부 리뷰 (review, runId 6cb62f0c10, gpt_main_sol) — 사용자 요청
16건(major 8 / minor 8). 지적마다 코드 재확인 후 종합.

**공통 지적 (신뢰도 높음)**
- B 스레드(`OnTagDiscovered`) — 리뷰 major, LogChannel 생성자 경로도 함께.
- A 출시 빌드 — 리뷰 major. Resources 포함 정책도 함께 정하라는 의견.
- C 닫힌 콘솔 행 생성 + `RemoveAt(0)` — 리뷰 minor(코어 링 버퍼, UI 가상화 제안).
- 런타임 Watch 폴링이 UI `Update`에만 있음 — 리뷰는 major로 봄: owner 파괴 자동 해제도 멈추고 캡처 객체가 계속 살아 있음. **심각도 상향 타당.**

**리뷰만 지적 → 코드 재확인 결과 모두 타당**
- 워커 스레드 `Watch(key, UnityObject)`가 `uObj.name`(메인 스레드 전용)을 호출 스레드에서 접근 (`HLogCore.cs:471-480`). major.
- Watch 이벤트 순서 역전: 워커 갱신은 큐, 메인 갱신은 즉시 발생 → UI에 B 다음 A, 대기 중 갱신 뒤 `Unwatch`면 삭제된 행 부활 (`HLogCore.cs:507-509, 556-558, 612-617`). major.
- Watch 이벤트 큐(`s_PendingEvents`) 상한·키별 병합 없음. major.
- `ResetStatics`가 `s_IsLogEnabled`를 복원하지 않음 → 도메인 리로드 끈 상태에서 로그 OFF가 다음 플레이로 이어짐 (리로드 켠 경우와 동작이 다름). major.
- `FlushPendingLogs`가 큐가 빌 때까지 돌아 프레임당 상한 없음. major(빈도 낮음).
- 폴링과 `Unwatch` 경합으로 값 부활 / 같은 키 새 게터까지 제거. minor.
- Watch 값 변경마다 개수 표시용으로 `HLogCore.Watches`(Dictionary 전체 복사) 호출 (`HB_InGameConsoleUI.cs:1076-1078, 1136-1141`). minor.
- 태그 정규식에 Singleline이 없어 여러 줄 로그는 태그 파싱 실패. minor.
- 워커 로그 시각을 처리 시점으로 기록. minor.
- 채널 `Exception()`/`LogException()`이 채널 태그를 잃음. minor.
- 설정 로딩: 에디터는 프로젝트 어디의 설정 에셋이든 찾지만 플레이어는 `Resources/HB_InGameConsoleUserSettings`만 → 에디터에서 Manual로 확인한 설정이 빌드에선 자동 UI로 바뀔 수 있음. minor(영향은 큼).
- `Watches`가 가변 `WatchEntry`를 공유. minor.

**Claude만 지적 (유지)**: D README API 불일치, E Auto-Bootstrap, README 메뉴 경로, Clear마다 채널 탐색, `catch { }`.

## 5. 결정 대기
(사용자 답 후 계획서 작성)
- 사용자 결정: A·C·D·E·Watch 구조 → 계획서 `docs/plans/2026-10-18_ingameconsole-package-fix.md` (`결정`, `결정 2`). A는 최종적으로 "에디터 + Development Build만, 릴리즈는 Manual Init으로도 안 켜짐".

## 6. preflight (docs/preflight/2026-10-18_ingameconsole/)
- 시작 전 토큰(사용자 확인): 입력 270,728 / 출력 86,000.

### P1 ownReview (결과 보기 전, 계획서 기준 지휘자 자체 검토)
1. Watch 키별 최신 명령 큐: 다른 스레드의 중간 값은 의도적으로 버려진다. 같은 키에 메인·워커가 섞이면 "메인 호출 시 그 키 대기 명령 폐기"로 순서를 맞추지만, 워커 호출이 메인 호출보다 실제로 나중인데 메인 적용 직후 큐에 들어오면 정상(나중 값 적용). 반대로 워커가 먼저 큐에 넣고 메인이 나중에 호출하면 폐기 → 정상. 경계: 워커가 대기 맵에 쓰는 중과 메인의 폐기가 겹칠 때 — ConcurrentDictionary 연산 단위라 마지막 연산 기준으로 정해짐. 수용.
2. Exception 채널 태그 `[ThreadStatic]`: `logMessageReceivedThreaded`가 로그 호출과 같은 스레드에서 동기로 불린다는 가정에 의존. Unity 문서 확인 필요(확인 못 하면 추정으로 표기).
3. 릴리즈 판정을 런타임 `Debug.isDebugBuild`가 아니라 컴파일 심볼로 바꿈(결정 2). `DEVELOPMENT_BUILD`는 플레이어 스크립트 컴파일에서 정의됨 — 에디터는 `UNITY_EDITOR`로 통과.
4. 열 때 행 생성: 1000줄이면 열 때 VisualElement 약 5000개를 한 프레임에 생성 → 열 때 순간 끊김. 수용 또는 열 때 분할 생성 필요한지 검토 대상.
5. PlayerLoop에서 게터 매 프레임 평가: 게터 수 × ToString 할당이 매 프레임. 기존(UI Update)과 비용 동일, 위치만 이동.
6. 링 버퍼 `History` 복사는 열 때·에디터 창 갱신 때 1000칸 배열 할당 — 기존과 같음.
7. 에디터 콘솔 창 `OnGUI`의 `Watches.Count`(1회 복사/리페인트) → `WatchCount`로.
8. `ResetStatics`에서 이벤트 구독을 지우지 않는 정책: 도메인 리로드 끈 상태에서 파괴된 UI의 구독이 남을 수 있으나 UI는 OnDisable에서 해제 → 수용.

### P1 (2026-10-18 02:19, runId 91059f0ba4)
- 첫 두 번 호출은 서버 첨부 상한(100,000자)에 걸려 거절됨(모델 호출 전). UI 파일을 수정 대상 범위(40-410, 485-600, 1060-1200, 1223-1508, 1572-1631)로, LogChannel을 1-112로, 에디터 창을 165-200 + 입력 요약으로 줄여 세 번째에 실행.
- 저장: p1_A.md(gpt_main_sol, 12건), p1_B.md(gemini_high_argon, 9건), p1_Perf.md(gpt_main_sol, 7건). 토큰: A 28,876/4,550, B 35,745/4,014, Perf 29,049/3,002.
- 설계 방향: A·B 모두 "수정 후 진행" (재설계 의견 없음).
- P1 이후 사용자 사전 확정 → `docs/preflight/2026-10-18_ingameconsole/confirmed.md` (활성 조건 `UNITY_EDITOR || USE_HLOG`, 커스텀 심볼 제거, 로그·Get만 스레드 허용 나머지 메인 전용 + 첫 위반 시 에러 1회).
- 추가 확정: 활성 조건을 `USE_HLOG`만으로(에디터 포함). 후속 작업: 설치 시 `USE_HLOG` 자동 추가.
- P2 진행 방식: 스킬 절차상 확정은 P2 뒤지만, 사용자가 P1 사양 질문에 먼저 답했으므로 `confirmed.md`(사전 확정)를 P2 입력에 함께 넘긴다.

### P2 (2026-10-18 02:32, runId 02f5f9388c)
- 입력: 이름표 + 사전 확정(confirmed.md) 요약. context: request, p1_A/B/Perf, confirmed.
- 저장: p2_A.md(gpt_main_sol, 16,170/4,849), p2_B.md(gemini_high_argon, 15,392/4,166).
- 후속 작업(사용자, P2 중 메시지): 이 작업 완료 후 "설치 시 USE_HLOG 자동 추가", "HB_Build 연동" 검토.
- P2 뒤 사용자 확정 4~7 → confirmed.md (Shutdown·Clear 범위, 값 Watch가 게터 해제, 자동 생성 마커는 ProjectSettings 텍스트 파일, 닫을 때 행 유지).
- 판정 원문 대조: P2 두 결과가 상대 P1 지적을 바꿔 인용한 곳 없음 (B1-08 요약, A1-04/05/11 요약 확인).

### P3 (2026-10-18 02:39, runId 1275234dd0)
- 절차 변경: 대상 코드가 약 5,000줄이라 첨부 상한 때문에 원본 전체를 넘길 수 없어, P3 산출물을 전체 코드가 아닌 "최종 수정안(설계 + 핵심 코드 스케치)"으로 요청. 실제 코드는 P4에서 지휘자가 작성. context: request, 계획서(1-66), p1 셋, p2 둘. confirmed.md 내용은 input에 요약.
- 저장: p3_A.md(gpt_main_sol, 26,980/9,659, 211초 — 백그라운드 전환), p3_B.md(gemini_high_argon, 25,363/8,972). 출력 잘림 [경고] 없음.
- 자기 보고 수치: 둘 다 줄 수는 "설계라 해당 없음"으로 보고 — 대조할 수치 없음.

### P4 진행 중 (지휘자 코드 작성)
- 원본 백업: scratchpad `igc_orig/` (런타임 5 + 에디터 4 파일).
- 작성 완료(컴파일 전): HLogCore.cs(재작성), LogChannel.cs, InGameConsole.cs, InGameConsoleSettings.cs, HB_InGameConsoleUI.cs(패치), LogChannelGeneratorSettings.cs, LogChannelGeneratorWindow.cs, HLogConsoleWindow.cs.
- 남은 것: 컴파일(ON/OFF), PlayMode 테스트, README, final_code/ 복사, p4_report.md.
- P4 완료: 컴파일 ON/OFF(Unity 7 · 6.3) 통과, PlayMode ON 8/8 · OFF 1/1 통과, README 갱신, final_code/·p4_report.md 작성. 상세는 `docs/preflight/2026-10-18_ingameconsole/p4_report.md`.
- ToolTest6.3 임시 상태(정리 전): ProjectSettings 원본·LogChannels.cs·manifest·lock 백업 `tt63_backup3/`, 현재 USE_HLOG 꺼진 상태(원본과 같음), `Assets/IGCCheck/`·`Assets/IGCGenEditor/` 남음, `Assets/Scripts/LogChannels.cs`는 새 템플릿으로 재생성됨.
- 추가 확인 (사용자 질문: 에디터 콘솔도 확인했나 → 컴파일만 했었음): ToolTest6.3 EditMode 테스트 `Assets/IGCCheckEditor/` 6/6 통과 (USE_HLOG ON)
  - E1 비플레이 메인 로그 즉시 기록 / E2 워커 로그·새 태그가 EditorApplication.update에서 메인 스레드 처리 / E3 비플레이 중 게터 주기 평가 없음(등록 때 1회) / E4 비플레이 Clear가 대기 로그 폐기 / E5 에디터 콘솔 창을 연 채 로그·Watch·태그·Clear 이벤트 — 에러·예외 없음 / E6 워커 Watch 거절
  - 처음 두 번은 USE_HLOG가 실제로 정의되지 않은 채 실행됨(OFF 결과): ToolTest6.3의 Define 줄을 AI Inference(Sentis) 패키지가 로드 때 바꿔서(`SENTIS_ANALYTICS_ENABLED` 추가·제거) 고정 문자열 치환이 빗나감 → 줄 내용과 무관하게 덧붙이는 방식으로 해결. 원래 기준선은 `APP_UI_EDITOR_ONLY`(백업과 같음).
  - 창 그리기(OnGUI) 결과 화면은 배치로 확인 불가 → 사용자 에디터 확인.
- ToolTest6.3 현재 상태(사용자 수동 확인용으로 유지): USE_HLOG ON, InGameConsole `file:` 로컬 패키지, LogChannels.cs 새 템플릿, `Assets/IGCCheck/`·`Assets/IGCCheckEditor/`·`Assets/IGCGenEditor/` 있음. 정리 시 manifest·lock·LogChannels.cs는 `tt63_backup3/`, ProjectSettings는 Define 줄에서 USE_HLOG만 제거.

### P5 준비
- 사용자 결정: "1번은 지금 하지말구 2번으로 p5 까지 완료후에 진행하자 p5a 는 아르곤으로 가자"
  - 후속 작업(별도): 등록형 조건 감시(`BreakWhen(() => 조건, …, owner)` 류) — P5 완료 후. README의 "Watch + 조건 트리거로 이상값 감시" 예시는 이번에 넣지 않음.
  - P5a: `preflight_check`의 에이전트가 `gpt_high_astra(사용불가)` → 사용자 요청으로 이번 실행만 `agents: gpt_high_astra=gemini_high_argon`.
- 사용자 수동 확인(에디터 콘솔·인게임 콘솔 화면): 아직 결과 없음.
- 첨부 목록
  - P5a: request.md, confirmed.md, final_code/ 런타임 5 + 에디터 3 + README (IGCCheckTests.cs.txt는 검증용이라 제외). P1~P3 원문·p4_report·지휘자 의심 지점 첨부 안 함.
  - P5b: 위와 같음 + p1_Perf.md 1~13줄(PF1 지적 표만).

### P5 ownReview (결과 보기 전)
- 정확성
  1. 메인 스레드 식별: SubsystemRegistration·InitializeOnLoadMethod에서 확정, 그 전 호출은 `UnitySynchronizationContext` 타입 이름으로만 인정 — 이름 문자열 의존(Unity 내부 타입명 변경 시 초기 호출이 거절될 뿐 오등록은 없음).
  2. Shutdown 뒤 PlayerLoop 시스템은 남고 Tick이 실행 여부로 빠져나감 — 의도. 워커 태그는 Shutdown 중 큐에 남았다가 재시작 때 처리.
  3. Exception 태그는 ThreadStatic + 동기 콜백 가정 — 에디터 Mono에서 메인·워커 확인, IL2CPP 미확인.
  4. 설정 로딩 동작 변경: 에디터가 더 이상 Resources 밖 설정 에셋을 쓰지 않음(경고만) — 기존 사용자는 에디터 동작이 바뀔 수 있음.
  5. OFF에서 씬 배치 UI 컴포넌트는 Awake에서 enabled=false, 공개 API는 빈 동작.
  6. 생성 템플릿 변경 — 기존 생성 파일은 재생성 전까지 예전 조건(UNITY_EDITOR 포함) 유지.
- 성능
  1. 로그마다 LogEntry 생성 시 정규식 2회(메인 스레드) 그대로.
  2. 워커 로그마다 s_IngressGate 잠금 1회.
  3. 게터 평가: Func<T> 래퍼의 박싱, 값이 바뀔 때만 문자열 변환(값 형식·string). 폴링 버퍼는 재사용(매 프레임 할당 없음).
  4. 처음 열 때 최대 1000줄 동기 생성(순간 비용), 열린 동안 상한 후 행 하나 제거·추가.
  5. History는 열 때·에디터 창 갱신 때 1000칸 배열 복사.
- P5 첫 호출(P5a·P5b 동시)은 서버 첨부 상한(250,000자, 줄 번호 포함)에 걸려 모델 호출 전 거절. 에디터 두 창을 바뀐 범위로 축소: HLogConsoleWindow 1-330 · 470-640 · 1260-1340, LogChannelGeneratorWindow 1-125 · 161-232 · 382-540 (빠진 곳은 변경 없는 GUI 그리기·채널 목록 편집 코드). 나머지 파일은 전체.

### P5 (2026-10-18 03:39)
- P5a runId d9ae0d51bd (preflight_check, gpt_high_astra→gemini_high_argon, 77,393/2,195) → p5a_check.md. 결론: 수정 후 진행.
- P5b runId cd66da2044 (preflight_perf, gpt_main_sol, 64,007/3,861) → p5b_perf.md. 결론: 수정 후 진행.

### P5 지적 수정 (confirmed 8)
- C-03: 생성 템플릿에 `LogError` 포워딩 추가.
- C-01·C-02: `LogChannel` 생성자가 `HLogCore.RegisterChannelInstance`로 자신을 캐시에 등록(같은 태그는 먼저 것 유지). 생성 템플릿은 `HLogCore.GetChannel("태그")`로 채널을 가져옴. `ResetStatics`는 채널 캐시를 비우지 않고 각 채널을 생성자 인자 상태로 되돌린 뒤 태그 재등록.
- C-04: 인게임 UI가 `OnLogEnableChanged`를 구독(OnEnable/OnDisable).
- PF5-01: 로그·Watch 개수 문자열은 개수가 바뀔 때만 생성(재바인딩 때 캐시 무효화).
- PF5-02: Watch 탭 전환 때 재생성 안 함, 닫혀 있던 동안 바뀐 경우만 `SyncWatches`(없어진 키 제거·바뀐 값만 수정·새 키 추가).
- PF5-04: 검색어(제외 검색 포함)는 바뀔 때 한 번만 해석 — 인게임 UI·에디터 창.
- PF5-05: 생성기는 내용이 같으면 파일을 쓰지 않음, 창 닫을 때 SetDirty 제거(SaveAssets만).
- 보류(todo): PF5-03·06·07·08, PF1 잔여, IL2CPP Exception 태그 → `docs/todo.md`.
- 확인 (ToolTest6.3): 템플릿 재생성 컴파일 통과, PlayMode ON 9/9(T9 신규: 직접 생성 채널 = GetChannel 인스턴스, 끄면 로그 차단·켜면 표시), EditMode ON 6/6, PlayMode OFF 1/1. T8 로그당 할당 +HLogCore ≈2.9KB, +닫힌 콘솔 ≈2.9KB.
- 사용자 수동 확인(에디터 콘솔 창·인게임 콘솔): "문제없었어" (P5 수정 전 상태 기준).

### 등록형 조건 감시 추가 (confirmed 10·11)
- API: `LogChannel.BreakWhen(key, Func<bool>, message, owner, once=true)` · `ReportWhen(key, Func<bool>, reason, owner, once=true)` → `HLogCore.WatchCondition(...)`, `HLogCore.ResetCondition(key)`, 공개 enum `HLogCore.WatchConditionKind`. 생성 템플릿에도 포워딩. 모두 `[Conditional("USE_HLOG")]`, 메인 스레드 전용.
- 구현: Watch 게터 레지스트리를 재사용(키 공간 공유 → Unwatch·owner 파괴 해제, 값 Watch가 덮으면 교체). 평가는 게터와 같은 루프(플레이 중 프레임당 1회), 거짓→참 순간만 발동, once면 Disarmed. 표시 문자열은 상태가 바뀔 때만 생성(매 프레임 할당 없음). 조건 예외는 에러 1회 후 해제.
- 발동: 에디터 Break = 채널 Error + `Debug.Break`, Report = 채널 `ReportIf`(리포트 파일+에러+일시정지). 빌드 = 채널 Error만.
- 표시: Watch 값 `[Break] False` / `[Break] True · Hit N회 (마지막 HH:mm:ss) · 완료`. 에디터 Watch Values에서 발동 시 빨간 강조, `+ Trigger` 대신 Reset. 인게임 Watch 탭은 같은 문자열.
- README: 빠른 시작 4절, Conditional 대상 목록, 채널 인스턴스 동일성.
- 확인 (ToolTest6.3): PlayMode ON 13/13 (T10 거짓→참 1회·once 정지·Reset 재발동 1/1/2, T11 once:false 2회·완료 표시 없음, T12 ReportWhen 리포트 1회, T13 조건 예외 에러 1회 후 해제·owner 파괴 해제), EditMode ON 6/6, PlayMode OFF 1/1, ToolCreate(7000.0.0a8) 컴파일 exit 0. 테스트의 `Debug.Break`는 `pauseStateChanged`에서 바로 해제.
- 자동 생성 마커 실제 생성 확인: 사용자 에디터 세션(03:05)에서 `ProjectSettings/HB_InGameConsole.LogChannels.generated.txt` 생성됨(내용 정상). 배치에서는 delayCall이 돌지 않아 미확인이던 항목.
- ToolTest6.3 정리: 테스트 폴더 3개·Reports·마커 삭제, manifest·lock은 `tt63_backup2`(해시 `0c70bb6c…`/`742f7303…`), LogChannels.cs·ProjectSettings는 `tt63_backup3`와 같음, 다시 열어 exit 0. `Packages/com.hb.testframeworkhelper`(embedded)는 사용자 것이라 그대로 둠.
- final_code/ 갱신(P5 수정·조건 감시 반영본).
- 커밋 전 정리 (사용자: "1번은 ifnore 해줘 2번은 지워주고 커밋 푸시 부탁해"): ToolCreate의 자동 생성 마커를 `.gitignore`에 추가(작업 프로젝트 한정), `docs/todo.md`의 "(릴리즈 전) InGameConsole 1.1.0 + HB_Cheat 감지 하한 1.1.0" 항목 삭제(도구 버전을 모두 1.0.0으로 맞춤).
