# HB_InGameConsole 점검 결과 수정

## 요약
점검·교차 리뷰(`docs/10_18/worklog_InGameConsole.md`)에서 확인한 결함을 고친다. 핵심은 Watch를 메인 스레드에서 순서대로 처리하는 구조로 바꾸고, 출시 빌드 자동 초기화를 막고, 닫힌 콘솔이 로그마다 UI를 만들지 않게 하는 것.
수정은 런타임 `HB.InGameConsole`(HLogCore · LogChannel · InGameConsole · InGameConsoleSettings · HB_InGameConsoleUI), 에디터(콘솔 창 · 채널 생성기), README에 걸친다. `/preflight`로 점검한 뒤 적용한다.

## 배경
- 실측(ToolTest6.3 6000.3.23f1 PlayMode): 백그라운드 스레드의 새 채널 → `UnityException: VisualElementCreation can only be called from the main thread`가 호출자에게 던져짐. 닫힌 콘솔이 로그 1줄당 GC 할당 ≈9KB 추가(에디터 기준).
- 교차 리뷰 16건 모두 코드 재확인으로 타당.
- 외부 사용: HB_Cheat가 `InGameConsole.Initialize / IsInitialized / AddExternalView / RemoveExternalView / ShowExternalView / IsExternalViewActive / Close`를 쓴다 → 시그니처 유지.

## 계획

### A. 출시 빌드 (사용자 결정 1)
- `HLogCore.AutoInitialize`(BeforeSceneLoad)와 `InGameConsole.AutoInitializeOnGameStart`(AfterSceneLoad)는 `Application.isEditor || Debug.isDebugBuild`일 때만 동작. 릴리즈 빌드는 자동 초기화를 건너뛴다.
- 릴리즈에서도 필요하면 게임 코드가 `InGameConsole.Initialize()`를 직접 호출(Manual Init) → 그때부터 수집·UI 동작. 새 설정 항목은 만들지 않는다.
- `HLogCore.ResetStatics`(SubsystemRegistration)의 `DiscoverAllChannels()`(리플렉션)는 `Initialize`로 옮겨, 릴리즈에서 초기화하지 않으면 리플렉션도 돌지 않게 한다.
- 코드·Resources(UXML·USS·TSS)는 빌드에 그대로 포함 (README에 명시).

### B. 태그 이벤트 스레드
- `RegisterTag`의 `OnTagDiscovered`를 `RaiseOnMainThread`로 넘긴다. `LogChannel` 생성자 경로도 같은 함수라 함께 해결.

### Watch 구조 (사용자 결정 5)
- 원칙: Watch 상태(값 맵 · 게터 맵)는 **메인 스레드에서만** 바꾼다. 공개 API(`Watch` 값/게터, `Unwatch`)는 어느 스레드에서나 호출 가능.
  - 메인 스레드 호출: 그 키의 대기 명령을 버리고(더 나중 호출이므로) 즉시 적용.
  - 다른 스레드 호출: 키별 최신 명령 하나만 보관하는 대기 맵(`ConcurrentDictionary<string, PendingWatch>`)에 넣고, 메인 스레드 처리 때 적용. 같은 키 중간 값은 버려진다(최신 값만) → 큐 무한 증가 없음, 순서 역전·삭제 후 부활 없음.
  - 값 → 문자열: `UnityEngine.Object`는 메인 스레드에서 변환(`name` 접근), 그 밖의 값은 호출 스레드에서 즉시 `ToString()`(호출 시점 스냅샷).
  - 게터 등록·첫 평가·주기 평가·owner 파괴 해제 모두 메인 스레드.
- 게터 주기 평가를 UI `Update`에서 코어 PlayerLoop 시스템으로 옮긴다 (UI가 없어도 동작, owner 해제도 동작). 에디터 비플레이 중에는 `EditorApplication.update`가 대기 로그·명령만 처리. UI `Update`의 중복 호출은 제거하고, PlayerLoop 시스템이 다른 코드의 `SetPlayerLoop`로 빠졌을 때만 UI가 대신 처리(프레임당 한 번 보장).
- 평가 중 해제를 위해 게터 목록을 재사용 버퍼에 복사해 순회.
- `WatchCount` 속성 추가(잠금 안에서 개수만). UI·에디터 창의 개수 표시는 이것을 쓴다.
- `Watches` 스냅샷은 `WatchEntry`를 복사해 반환(내부 상태 공유 안 함).

### C. 로그 저장·표시 (사용자 결정 2)
- 코어 기록을 고정 크기(1000) 링 버퍼로. `History`는 오래된 순서로 복사해 반환(API 유지). 개수 집계는 지금처럼 추가·제거 때 갱신.
- 인게임 UI: 콘솔이 닫혀 있으면 로그 행을 만들지 않고 버튼 개수만 갱신. 열 때 `History`로 한 번에 다시 만든다(`RefreshAllLogs`). 열려 있는 동안만 줄 단위 추가.
- 다른 스레드 로그 처리에 프레임당 상한(예: 1000개). 남은 것은 다음 프레임.
- 다른 스레드 로그는 수신 시각을 `PendingLog`에 담아 `LogEntry` 시각으로 쓴다.

### D. README (사용자 결정 3)
- 빠른 시작·주요 특징을 실제 API로: 채널 메서드는 `Log` · `Warning` · `Error` · `Exception` · `LogException`(+ `LogChannel.LogError`). Conditional 적용 대상 설명도 실제(`Log` · `Warning` · `Watch` 계열)로.
- 메뉴 경로를 실제(`Tools > HB > In-Game Console > …`)로. 출시 빌드 정책(A), Watch 스레드 규칙 반영.

### E. 자동 생성 1회 (사용자 결정 4)
- `LogChannels.cs` 자동 생성은 프로젝트당 한 번. `EditorPrefs` 키에 `PlayerSettings.productGUID`를 붙여 기록(파일이 이미 있어도 기록). 지운 뒤에는 다시 만들지 않는다. Generator 창에서 직접 만들 수 있음.

### 리뷰 minor
- `ResetStatics`에서 `IsLogEnabled`를 true로 복원 (도메인 리로드 꺼도 같은 동작). 공개 이벤트 구독은 지우지 않는다(에디터 창이 플레이 사이에 구독 유지) — 문서화.
- 태그 정규식에 `RegexOptions.Singleline` (여러 줄 로그 태그 파싱).
- 채널 `Exception()`/`LogException()`의 채널 태그 보존: 호출 동안 `[ThreadStatic]` 현재 채널 태그를 두고, 로그 콜백(같은 스레드에서 동기 호출)이 그 태그를 `LogEntry`에 쓴다. 원본 예외·스택 출력은 유지.
- 설정 로딩: 에디터도 플레이어와 같은 `Resources/HB_InGameConsoleUserSettings`만 쓴다. 다른 위치의 `InGameConsoleSettings` 에셋이 있으면 에디터에서 한 번 경고(옮기라고).
- `Clear()`에서 `DiscoverAllChannels()` 호출 제거(태그는 지우지 않으므로 불필요).
- 채널 탐색의 `catch { }`는 에디터에서만 경고 한 번 출력.

## 버린 대안과 이유
- 릴리즈 허용 설정 체크박스: 사용자 결정으로 Manual Init(직접 `Initialize()`)으로 대신.
- ListView 가상화: 사용자 결정으로 "열 때 만들기"(구조 유지, 메모리 이득).
- `LogWarning`/`LogError` 별칭 추가: 사용자 결정으로 README만 최신화.
- Watch를 전부 메인 스레드 전용으로 제한(다른 스레드 호출 금지): 기존 사용 코드(Task 등)가 깨짐 → 키별 최신 명령 큐로.
- 모든 Watch 값을 메인 스레드에서 `ToString`: 호출 시점 값이 아닌 나중 값이 보일 수 있어 Unity 객체만 지연.

## 위험
- Watch 구조 변경이 에디터 콘솔 창(트리거·리포트)과 HB_Cheat 연동에 영향 → 에디터 창이 쓰는 API(`Watches`, `OnWatchUpdated`, `History`) 유지.
- PlayerLoop 시스템 이중 처리/누락.
- 링 버퍼 인덱스 오류(순서 뒤집힘).
- 릴리즈 판정(`Debug.isDebugBuild`)이 에디터 플레이에서 true인지 — 에디터는 `Application.isEditor`로 먼저 통과.

## 테스트
- ToolTest6.3 PlayMode(`Assets/IGCCheck`): 백그라운드 새 채널(예외 없음, 이벤트 메인 스레드), 백그라운드 Watch(Unity 객체 포함) 순서·최신값·Unwatch 후 부활 없음, AutoCreateUI 없이 게터 갱신·owner 해제, 링 버퍼 1000 초과 순서, 닫힌 콘솔 로그당 할당(재측정), 다른 스레드 로그 시각·프레임 상한, 여러 줄 태그, Exception 채널 태그.
- 릴리즈 판정: 개발 빌드가 아닌 플레이어 빌드는 배치 빌드 비용이 커서 우선 코드 확인. 가능하면 Windows 비개발 빌드 1회로 자동 초기화가 없는지 확인.
- 에디터 컴파일: ToolCreate(7000.0.0a8), ToolTest6.3(6000.3.23f1).

## 실제 확인
- 인게임 콘솔 열기/닫기·필터·Watch 탭은 에디터 플레이에서 사용자 확인.

## 사용자에게 물을 것
- preflight 진행 중 P2 확정 단계에서 묻는다.

## 결정
> 1. 응 에디터와 Development Build 에서 허용하고 설정체크는 Manual Init 으로 처리 가능할까? 2. 응 메모리 생각하면 그게 좋겠다. 3. 아마 저거 Log 만 빠졌을거야 README 만 지금 코드로 최신화 해줘 4. 한번만 하자 5. 지금 구조에 문제가 많으니 수정하자 preflight 로 한번 돌려보자 세션 한도는 초기화했어

- 반영: A(Manual Init으로 릴리즈 허용 — 가능하다고 답함), C(열 때 만들기 + 링 버퍼), D(README만), E(1회), Watch 구조 변경, `/preflight` 진행.

> 알겠어. 어차피 adb 나 우회로 로그는 확인 가능하니까 InGameConsole 을 키는것만으로는 로그만 확인 가능하니 위험성이 있긴하지만 크리티컬하다고 생각하지 않아서 괜찮을거같아

- 반영: 릴리즈에서 Manual Init으로 켤 수 있게 두는 위험(로그 노출)은 수용. 추가 차단(심볼·설정)은 하지 않는다.

## 결정 2 (A 변경)
> 입력 270,728 에 출력 86000 이야. 아 그리고 다시 수정할게 DevelopmentBuild 에서만 활성화 하자 위험성 그냥 빼는게 좋다 P5에서 아스트라는 내가 비활성화 했어

- 바뀐 계획 항목 A: 에디터와 Development Build에서만 동작. 릴리즈(비개발) 빌드는 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 자동 초기화·`Initialize()`·수집·UI 생성 코드를 비운다(Manual Init으로도 켜지지 않음). 공개 API 시그니처는 남겨 사용 코드가 릴리즈에서도 컴파일되게 한다. Resources의 UXML·USS·TSS는 패키지 구조상 빌드에 포함되지만 쓰이지 않는다.
- 결정 1의 "Manual Init으로 릴리즈 허용", 직전 결정의 "위험 수용"은 이 결정으로 대체.
- 토큰: preflight 시작 전 오늘 사용량 입력 270,728 / 출력 86,000 (사용자 확인).
