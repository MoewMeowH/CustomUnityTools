### 1. 지적 목록

| ID | 위치 | 문제 | 발생 조건 | 영향 | 심각도 | 확신도 | 검증 방법 |
|---|---|---|---|---|---|---|---|
| B1-01 | `HLogCore.Watch(key, getter)` 및 계획서 'Watch 구조' | 다른 스레드에서 게터 등록 시 첫 평가를 메인 스레드에서 하기로 계획했으나, 같은 프레임 안에서 워커가 `Watch(getter)` 직후 `Watch(value)`나 `Unwatch()`를 호출하면 명령이 덮어써짐. | 워커 스레드에서 `Watch("k", getter)` 호출 후 메인 스레드가 큐를 처리하기 전에 `Watch("k", val)` 호출 시 `ConcurrentDictionary<string, PendingWatch>`에서 기존 명령이 교체됨. | 게터 등록 자체가 유실되거나, 반대로 게터 첫 평가가 나중에 호출된 값을 덮어써서 상태 역전이 발생함. | 높음 | 사실 (설계 추론) | 워커 스레드에서 `Watch("k", () => 1)` 직후 `Watch("k", 2)`를 연속 호출한 뒤 다음 프레임에 게터가 도는지 및 최종 값이 무엇인지 확인. |
| B1-02 | `HLogCore.cs:204` (`PollWatchGettersInEditor`) 및 계획서 'Watch 구조' | 에디터 플레이 모드에서 `EditorApplication.update`와 `PlayerLoop` 양쪽에서 Watch 게터 폴링 및 플러시가 중복 실행되거나, 프레임당 1회 보장 플래그가 `EditorApplication.update`(PlayerLoop 외부)에서 먼저 소비될 위험이 있음. | 계획서에 "에디터 비플레이 중에는 `EditorApplication.update`가 대기 로그·명령만 처리"라고 했으나, 플레이 중 `EditorApplication.update` 차단이 누락되거나 `Time.frameCount` 기반 중복 방지 시 에디터 업데이트 주기와 PlayerLoop 주기가 어긋날 때 발생. | 프레임당 2회 평가되거나, 반대로 에디터 업데이트가 플래그를 먼저 올려 정작 게임 루프(`Update`) 시점에는 평가가 건너뛰어짐. | 중간 | 추론 | 에디터 PlayMode에서 게터 호출 횟수 카운터를 두고 `Update` 1프레임당 정확히 1회만 증가하는지 확인. |
| B1-03 | `HB_InGameConsoleUI.cs:248, 1225, 1378` 및 계획서 'C. 로그 저장·표시' | 콘솔이 닫힌 상태에서 로그만 쌓이다가 `OpenConsole()`을 호출할 때 `RefreshAllLogs()`가 누락되거나, 초기화(`InitializeWithRoot`) 시점에 콘솔이 닫혀 있는데도 `RefreshAllLogs()`를 호출해 최대 1000개의 UI 요소를 미리 만드는 문제. | 콘솔이 닫힌 채로 시작해 로그 1000개가 찍힌 뒤 콘솔을 열 때, 또는 씬 시작 시 이미 로그가 쌓여 있을 때. | 초기화 시점에 닫혀 있는데도 행을 생성하면 메모리 절감 목표가 깨지고, `OpenConsole()`에서 재구성을 안 하면 빈 창이 뜸. 또한 열려 있는 동안 이미 그려진 상태에서 다시 `OpenConsole()`이 불리면 1000개를 중복 재구성함. | 높음 | 사실 | 시작 직후 콘솔을 열지 않고 로그 500개 발생 시 `createdLogRows.Count == 0`인지 확인 후, `OpenConsole()` 호출 시 500개가 보이는지 확인. |
| B1-04 | `HB_InGameConsoleUI.cs:1076~1134` (`HandleWatchUpdated`, `RefreshAllWatches`) | 로그 행은 콘솔이 닫혀 있을 때 생성을 건너뛰기로 했으나, Watch 행(`watchRowMap`, `VisualElement`)과 태그 버튼은 콘솔이 닫혀 있어도 이벤트마다 즉시 UI 요소를 생성·갱신함. | 콘솔이 닫힌 상태에서 새로운 Watch 키가 다수 등록되거나 `Unwatch`로 인해 `RefreshAllWatches()`가 호출될 때(`Clear()` 포함). | Watch 항목이 추가·삭제될 때마다 닫힌 콘솔 뒤에서 UI Toolkit 요소 생성 및 `Clear()`가 반복되어 GC 할당 발생. | 중간 | 사실 | 콘솔이 닫힌 상태에서 서로 다른 키로 `Watch` 100회 및 `Unwatch` 호출 시 `watchScrollView` 자식 요소가 생성되는지 프로파일러로 확인. |
| B1-05 | `LogChannel.cs:70~81` 및 계획서 '리뷰 minor' | `Exception` / `LogException` 호출 시 `[ThreadStatic]`으로 채널 태그를 보존하기로 했으나, 커스텀 `ILogHandler`가 예외를 던지거나 로그가억제될 때 태그가 초기화되지 않고 남아 다음 일반 `Debug.Log`에 오염될 수 있음. 또한 `LogEntry` 생성자(`ParseTagAndMessage`)는 메시지 문자열만 파싱하므로 `[ThreadStatic]` 태그를 주입할 경로가 현재 구조에 없음. | `LogChannel.Exception()` 내부에서 `try/finally` 없이 `[ThreadStatic]`을 쓰거나, 워커 스레드에서 `Exception()` 호출 시(`HandleUnityLogThreaded`에서 `PendingLog`에 태그를 담지 않으면 메인 스레드 플러시 시점엔 `[ThreadStatic]` 값을 잃음). | 워커 스레드에서 찍은 `channel.Exception(ex)`의 채널 태그가 유실되거나, 같은 스레드의 다음 무태그 로그에 이전 예외의 채널 태그가 잘못 붙음. | 높음 | 사실 | 백그라운드 스레드(`Task.Run`)에서 `HLog.Get("Net").Exception(new Exception("err"))` 호출 후 메인 스레드에 기록된 `LogEntry.Tag`가 `"Net"`인지 확인. |
| B1-06 | `HLogCore.cs:174` (`ResetStatics`), `HLogConsoleWindow.cs:169` | 도메인 리로드 비활성화(Enter Play Mode Options) 상태에서 플레이 모드 진입 후 종료 시, `HLogCore.Shutdown()`이 호출되지 않으면 에디터 비플레이 모드에서도 `s_IsListeningUnityLogs`가 계속 켜져 있거나, 반대로 플레이 진입 시 `ResetStatics`(SubsystemRegistration)가 에디터 창(`OnEnable`)에서 켜둔 상태를 초기화한 뒤 `BeforeSceneLoad` 전까지 로그가 유실됨. | 에디터 콘솔 창을 열어둔 채로 도메인 리로드 없이 플레이 모드 진입/종료 반복 시. | `ResetStatics`에서 `s_WatchGetters`와 `PendingWatch` 대기 맵을 모두 비우지 않으면 이전 플레이의 대기 명령이 다음 플레이 첫 프레임에 실행됨. | 중간 | 사실 | Enter Play Mode Settings에서 Reload Domain을 끄고 플레이 중 워커 스레드 Watch/Log를 발생시키며 플레이 종료 후 재진입하여 잔여 데이터가 남는지 확인. |
| B1-07 | `InGameConsole.cs`, `HLogCore.cs`, `LogChannel.cs` 및 계획서 '결정 2 (A 변경)' | 릴리즈 빌드에서 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 동작 코드를 비울 때, `LogChannel`의 `[Conditional("UNITY_EDITOR"), Conditional("USE_HLOG")]`와 충돌하거나 `Error`/`Exception`/`GetChannel`/`Watches`/`History` 등 반환값이 있는 공개 API의 더미(stub) 반환값 처리가 누락될 위험. | 비개발(Release) 플레이어 빌드 컴파일 시, 또는 릴리즈 빌드에 `USE_HLOG` 심볼이 정의된 경우. | 1) `USE_HLOG`가 정의된 릴리즈 빌드에서 `LogChannel.Log`가 `_logger.Log`를 호출해 Unity 기본 로그가 그대로 출력됨(콘솔만 안 뜨고 로그는 나감). 2) `HLogCore.History`, `Watches`, `KnownTags`가 `null`을 반환하면 외부 호출자에서 `NullReferenceException` 발생. | 높음 | 추론 | `DEVELOPMENT_BUILD` 없이 플레이어 빌드 스크립트 컴파일 시 `HLogCore.History` 등이 빈 컬렉션(`Array.Empty<LogEntry>()`)을 반환하는지, `LogChannel` 메서드가 동작하지 않는지 확인. |
| B1-08 | `HLogCore.cs:624` (`FlushPendingLogs`) 및 계획서 'C. 로그 저장·표시' | 다른 스레드 로그 처리에 프레임당 상한(예: 1000개)을 둘 때, 생산 속도가 프레임당 1000개를 넘고 `s_PendingLogs`의 최대 크기(`MaxLogCount = 1000`) 제한이 그대로면, `HandleUnityLogThreaded`에서 `s_PendingLogs.TryDequeue`로 버릴 때 `s_PendingLogCount` 증감 경합으로 카운트가 음수나 실제보다 큰 값으로 틀어질 수 있음. | 다수의 워커 스레드가 동시에 로그를 쏟아내는 중 메인 스레드의 `FlushPendingLogs`가 동시에 `TryDequeue` 및 `Interlocked.Decrement`를 수행할 때. | `HandleUnityLogThreaded`에서 `Interlocked.Increment` 후 `MaxLogCount` 초과 시 `TryDequeue`를 하는데, 그 사이 메인 스레드가 큐를 비워버리면 `TryDequeue`가 실패하여 `Decrement`가 호출되지 않음 → `s_PendingLogCount`가 영구적으로 실제 큐 크기보다 커져서 이후 정상 로그도 들어오자마자 즉시 버려짐(치명적 버그, 현재 코드 603~606줄에 이미 존재). | 높음 | 사실 | 워커 스레드에서 2000개 로그를 찍고 메인 스레드가 비운 뒤, 다시 워커 스레드에서 로그 1개를 찍었을 때 `s_PendingLogCount`가 1인지(1000 이상으로 고정되어 즉시 버려지지 않는지) 확인. |
| B1-09 | `LogChannelGeneratorWindow.cs:40` 및 계획서 'E. 자동 생성 1회' | `EditorPrefs`는 프로젝트가 아니라 **사용자 OS 계정(로컬 머신)** 단위 저장소임. | 팀원 A가 프로젝트에서 `LogChannels.cs`를 생성한 후 불필요하다고 판단해 삭제하고 커밋했을 때, 다른 PC의 팀원 B가 프로젝트를 열거나 CI 머신에서 열면 `EditorPrefs`에 기록이 없어 `LogChannels.cs`가 다시 자동 생성되어 버전 관리(Git)가 더럽혀짐. | 다인 개발 환경이나 다른 PC에서 클론 시 삭제된 `LogChannels.cs`가 다시 살아남. | 낮음 | 사실 | `EditorPrefs` 키 삭제 후 에디터 재시작(다른 팀원 PC 시뮬레이션) 시 파일이 재생성되는지 확인. |

---

### 2. 사양 확인이 필요한 항목

1. **릴리즈 빌드에서 `LogChannel.Error` / `Exception`의 Unity 기본 로그 출력 여부 (계획 A / 결정 2 관련)**
   - **질문**: 결정 2에 따라 릴리즈(비개발) 빌드에서 인게임 콘솔과 `HLogCore` 수집을 완전히 끄기로 했습니다. 이때 `HLog.Common.Error(...)`나 `Exception(...)`을 호출하면 `HLogCore` 수집만 안 할 뿐 Unity 기본 `Debug.LogError` / `Debug.LogException`(플레이어 로그 파일 기록용)은 나가야 합니까, 아니면 `LogChannel`의 모든 출력 자체를 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 막아야 합니까?
   - **조건부 결론**: 만약 크래시 분석을 위해 플레이어 로그 파일에는 에러/예외가 남아야 한다면 `LogChannel.Error`/`Exception`의 `_logger` 호출은 유지하고 `HLogCore`만 비워야 하며, 완전히 무음이어야 한다면 `LogChannel` 메서드 본문 전체를 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 감싸야 합니다.

2. **Watch 키별 대기 명령(`PendingWatch`)에서 값과 게터가 충돌할 때의 동작 (B1-01 관련)**
   - **질문**: 다른 스레드에서 같은 키에 대해 `Watch(key, getter)`와 `Watch(key, value)`를 섞어서 호출하는 사용 사례가 있습니까?
   - **조건부 결론**: 없다면 `PendingWatch` 구조체에 `Getter`와 `Value`를 별도 필드로 두어 같은 프레임 내에서는 게터 등록과 최신 값 갱신이 서로 덮어쓰지 않고 둘 다 반영되도록 하거나, `Watch(getter)`는 `UnityEngine.Object owner`를 다루므로 메인 스레드 전용으로 두는 것이 안전합니다.

---

### 3. 수정 방향

- **B1-08 (`s_PendingLogCount` 영구 오염 수정 — 최우선)**:
  `FlushPendingLogs`에서 큐를 비울 때 `TryDequeue`가 성공한 만큼만 감소시키고, `HandleUnityLogThreaded`에서 오버플로로 버릴 때는 카운트 증가/감소 순서를 맞춰야 합니다.
  ```csharp
  // HandleUnityLogThreaded 내부
  s_PendingLogs.Enqueue(new PendingLog(condition, stackTrace, type, tag, DateTime.Now));
  if (Interlocked.Increment(ref s_PendingLogCount) > MaxLogCount)
  {
      if (s_PendingLogs.TryDequeue(out _))
          Interlocked.Decrement(ref s_PendingLogCount);
  }
  // FlushPendingLogs 내부: 루프가 끝난 뒤 큐가 비었으면 카운트 드리프트를 막기 위해 보정하지 말고,
  // 정확히 TryDequeue 성공 시에만 Interlocked.Decrement(ref s_PendingLogCount) 수행 (현재는 Increment만 하고 TryDequeue 실패 시 Decrement를 안 해서 카운트가 무한 증가함).
  ```
  *수정 핵심*: `HandleUnityLogThreaded`에서 `Increment`를 했으면 `TryDequeue` 성공 시에만 `Decrement`해야 실제 큐 원소 수와 일치합니다. 현재 코드(603줄)는 `Increment`로 1001이 된 상태에서 `TryDequeue`가 성공하면 `Decrement`로 1000을 맞추지만, 만약 `FlushPendingLogs`가 먼저 다 빼가서 `TryDequeue`가 실패하면 `Decrement`가 안 불려 실제 큐는 0개인데 `s_PendingLogCount`는 1001로 남는 버그입니다. 오버플로 체크 시 `while (Volatile.Read(ref s_PendingLogCount) > MaxLogCount && s_PendingLogs.TryDequeue(out _)) { Interlocked.Decrement(ref s_PendingLogCount); }` 형태로 고쳐야 드리프트가 발생하지 않습니다.

- **B1-05 (`Exception` 채널 태그 스레드 안전 전달)**:
  `[ThreadStatic] private static string s_CurrentChannelTag;`를 설정할 때 반드시 `try { s_CurrentChannelTag = Tag; _logger.LogException(...); } finally { s_CurrentChannelTag = null; }`로 감싸고, `HandleUnityLogThreaded`가 호출되는 즉시 현재 스레드의 `s_CurrentChannelTag`를 읽어 `PendingLog` 구조체에 함께 저장해야 워커 스레드 예외도 태그가 보존됩니다.

- **B1-01 (`PendingWatch` 명령 덮어쓰기 방지)**:
  `PendingWatch` 항목을 갱신할 때 `AddOrUpdate`를 사용하여, 기존 대기 명령에 `Getter` 등록이 있었고 새 명령이 단순 `Value` 갱신이면 `Getter`를 유지하거나, 명령 종류(`SetValue`, `SetGetter`, `Unwatch`) 중 `Unwatch`는 이전 모든 상태를 지우고 `SetGetter`는 즉시 평가 대신 메인 스레드에서 게터 등록 후 1회 평가하도록 상태를 합칩니다.

- **B1-03 & B1-04 (닫힌 콘솔 UI 갱신 억제)**:
  - `InitializeWithRoot`에서는 `IsConsoleOpen`일 때만 `RefreshAllLogs()` / `RefreshAllWatches()`를 호출하고, 닫혀 있으면 `UpdateOpenButtonText(HLogCore.HistoryCount)`와 `UpdateWatchButtonCount()`만 호출합니다.
  - `OpenConsole()`에서 열리기 직전 상태가 닫혀 있었을 때만(`if (IsConsoleOpen) return;`) 클래스를 제거하고 `RefreshAllLogs()`, `RefreshAllWatches()`, `ScrollToBottom()`을 수행합니다.
  - `HandleWatchUpdated` / `HandleWatchRemoved`에서도 `!IsConsoleOpen`이면 `UpdateWatchButtonCount()`만 갱신하고 UI 행 조작은 건너뜁니다.

- **B1-02 (PlayerLoop와 에디터 업데이트 중복 방지)**:
  `HLogCore`에 `private static int s_LastPolledFrame = -1;`을 두어 `PollWatchGetters()` 진입 시 `if (Application.isPlaying) { int frame = Time.frameCount; if (s_LastPolledFrame == frame) return; s_LastPolledFrame = frame; }`로 가드하고, `PollWatchGettersInEditor`에서는 `if (! UnityEditor.EditorApplication.isPlaying) FlushPendingLogs();`만 수행하도록 분리합니다.

- **B1-07 (릴리즈 빌드 빈 껍데기 API 안전성)**:
  `#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)` 구간에서 `History`, `Watches`, `KnownTags`는 `null` 대신 정적 빈 인스턴스(`Array.Empty<LogEntry>()`, 빈 정적 Dictionary/List)를 반환하게 하고, `GetChannel`은 아무 동작도 하지 않는 싱글턴 더미 `LogChannel`을 반환하도록 하여 릴리즈 빌드에서 호출자 코드가 깨지거나 힙 할당이 일어나지 않게 합니다.

---

### 4. 설계 방향 판단

**수정 후 진행** — Watch의 메인 스레드 단일화, 링 버퍼 도입, 릴리즈 빌드 코드 제외 등 핵심 설계 방향은 타당하나, 기존 코드에 있던 `s_PendingLogCount` 드리프트 버그(B1-08)와 워커 스레드 `Exception` 태그 전달(B1-05), 콘솔 열기 시점의 UI 동기화 조건(B1-03/04)을 계획에 보완한 뒤 구현해야 합니다.