## 1. 상대 Phase 2 판정에 대한 응답

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| A2-01 (B1-01 관련) | 수용 | 확정 2(Watch 메인 스레드 전용, `PendingWatch` 미구현)로 전제가 소멸하여 Phase 2에서 이미 철회함. |
| A2-02 (B1-02 관련) | 수용 | 플레이 중 폴링 주체를 `PlayerLoop` 하나로 두고, 프레임 가드(`s_LastPolledFrame`)는 게터 폴링에만 적용하며 로그·태그 배출 예산과는 분리한다. |
| A2-03 (B1-03 관련) | 수용 | 확정 7에 따라 최초 열기 전에는 행을 만들지 않고, 한 번 연 뒤 닫을 때는 생성된 행을 유지하며 닫혀 있는 동안의 변경만 다음 열기 때 반영한다. |
| A2-04 (B1-04 관련) | 수용 | 닫힌 상태에서 Watch·태그 UI 갱신을 지연(`isWatchesDirty`, `isTagsDirty`)하고 열 때 반영하도록 설계안에 포함한다. |
| A2-05 (B1-05 관련) | 수용 | `finally`에서 단순 `null` 대입 대신 이전 값을 지역 변수에 백업 후 복원(`s_CurrentChannelTag = prevTag`)하고 `PendingLog`에 스냅샷을 저장한다. |
| A2-06 (B1-06 관련) | 수용 | `ResetStatics`와 플레이 모드 전환 시 정적 상태·대기 큐·프레임 가드를 초기화하고 에디터 창에 리셋을 알린다. |
| A2-07 (B1-07 관련) | 수용 | 확정 1에 따라 `#if USE_HLOG` 단일 심볼로 통일하고, 비활성 시 불변/읽기 전용 빈 컬렉션과 더미 채널을 반환한다. |
| A2-08 (B1-08 관련) | 부분 수용 | 상대 지적대로 소비자가 먼저 빼간 뒤의 `TryDequeue` 실패 자체는 과대 카운트를 만들지 않는다(내 Phase 1의 실패 경로 설명이 부정확했음을 인정하고 수정함). 다만 락 없는 `ConcurrentQueue` + 별도 원자 카운터 조합은 `Clear()`/`ResetStatics()`와 워커 생산자가 겹칠 때 카운터 불일치가 발생할 수 있으므로, A2-20에서 상대가 제안한 대안대로 **짧은 `lock(s_PendingLogLock)` 기반의 고정 용량 `Queue<PendingLog>`**로 단순화하여 카운터 드리프트 가능성을 원천 제거한다. |
| A2-09 (B1-09 관련) | 수용 | 확정 6에 따라 `ProjectSettings/HB_InGameConsole_Generated.txt` 비-YAML 마커를 사용하고 기존 파일 확인 또는 생성 성공 후에만 기록한다. |
| A2-10 ~ A2-16 | 수용 | PF1-01~07에 대한 판정 방향이 일치하며, 아래 섹션 2·5에 구체적인 반영 방식을 명시한다. |
| A2-17 ~ A2-24 | 수용 | A2-18(폴링 가드와 배출 분리), A2-19(이전 태그 복원), A2-20(짧은 잠금 큐), A2-21(`USE_HLOG` 및 읽기 전용 반환), A2-22(재오픈 시 상태 복원 및 `selectedRow` 정리), A2-23(게터 값 비교 시 가변 참조 객체 주의)을 모두 최종 설계에 반영한다. |

---

## 2. 최종 수정안 (설계 + 핵심 코드 스케치)

### B3-01. 조건부 컴파일 및 심볼 규칙 (확정 1)
- **단일 심볼**: 모든 런타임·생성 코드의 활성 조건은 `USE_HLOG` 하나로 통일한다(`UNITY_EDITOR`, `DEVELOPMENT_BUILD` 조건 제거).
- **메서드 어트리뷰트**:
  - `LogChannel` 및 `HLog` 정적 클래스(생성 템플릿 포함)의 `Log`, `Warning`, `Error`, `Exception`, `LogException`, `LogError`, `Watch` 계열, `BreakIf`, `Assert`, `ReportIf` 모두 `[Conditional("USE_HLOG")]` 단일 적용.
  - `BreakIf`, `Assert`, `ReportIf`는 `[Conditional("USE_HLOG")]`를 유지하되 메서드 본문은 `#if UNITY_EDITOR ... #endif`로 감싼다.
- **심볼 OFF(`!USE_HLOG`) 시 공개 API 계약**:
  - 모든 공개 타입·시그니처·이벤트는 컴파일 호환성을 위해 유지.
  - `HLogCore.History` → `Array.Empty<LogEntry>()` (읽기 전용 `IReadOnlyList<LogEntry>`).
  - `HLogCore.Watches` → `s_EmptyWatches` (`IReadOnlyDictionary<string, WatchEntry>` 또는 읽기 전용 래퍼, 호출마다 새 빈 딕셔너리를 반환하거나 `ReadOnlyDictionary` 사용).
  - `HLogCore.KnownTags` → `Array.Empty<string>()`.
  - `HLogCore.WatchCount` / `HistoryCount` → `0`.
  - `HLogCore.GetChannel(string tag)` → 태그만 보유한 싱글턴/캐시 더미 `LogChannel` 반환(내부 `UnityEngine.Logger` 생성 및 태그 등록 안 함).
  - `InGameConsole.Instance` / `EnsureUIInstance()` → `null`, `IsInitialized` / `IsExternalViewActive(...)` → `false`, 나머지 메서드는 no-op.
  - 씬에 배치된 `InGameConsole` / `HB_InGameConsoleUI` 컴포넌트는 `#if !USE_HLOG`일 때 `Awake`/`OnEnable`에서 아무것도 생성·초기화하지 않고 즉시 `enabled = false` 처리.

### B3-02. 스레드 계약 및 메인 스레드 검증 (확정 2)
- **허용 범위**:
  - 어느 스레드에서나 허용: `HLog.Get`, `HLog.Channel`, `HLogCore.GetChannel`, 각 채널의 로그 출력 메서드, `Application.logMessageReceivedThreaded` 콜백.
  - 메인 스레드 전용: `Watch` 계열, `Unwatch`, `Clear`, `IsLogEnabled` setter, `Initialize`, `Shutdown`, `PollWatchGetters`, `FlushPendingLogs`, `InGameConsole`의 모든 공개 UI/뷰 제어 API.
- **메인 스레드 ID 캡처 및 위반 진단 (`HLogCore`)**:
  ```csharp
  private static int s_MainThreadId = -1;
  private static int s_WarnedThreadViolationMask = 0; // API별 비트마스크
  [ThreadStatic] private static bool s_IsInternalLogging;

  private enum MainThreadApiId
  {
      Initialize = 1 << 0,
      Shutdown = 1 << 1,
      Clear = 1 << 2,
      SetLogEnabled = 1 << 3,
      Watch = 1 << 4,
      Unwatch = 1 << 5,
      PollWatchGetters = 1 << 6,
      FlushPendingLogs = 1 << 7,
      ConsoleUI = 1 << 8,
  }

  // SubsystemRegistration(ResetStatics), AutoInitialize, 에디터 InitializeOnLoad에서 반드시 먼저 호출
  internal static void CaptureMainThreadId()
  {
      s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
  }

  internal static bool EnsureMainThread(MainThreadApiId apiId, string apiName)
  {
      if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
          return true;

      // API별 최초 1회만 원자적으로 에러 출력
      int bit = (int)apiId;
      while (true)
      {
          int current = Volatile.Read(ref s_WarnedThreadViolationMask);
          if ((current & bit) != 0)
              return false;
          if (Interlocked.CompareExchange(ref s_WarnedThreadViolationMask, current | bit, current) == current)
          {
              Debug.LogError($"[HLog] '{apiName}' can only be called from the main thread.");
              return false;
          }
      }
  }
  ```
  - 주의: 워커가 `HLog.Get`을 메인 초기화보다 먼저 호출할 수 있으므로, `GetChannel`에서는 `s_MainThreadId`를 절대 초기화하지 않고 `SubsystemRegistration`(`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`)과 에디터 `[InitializeOnLoadMethod]`에서만 `CaptureMainThreadId()`를 호출한다.

### B3-03. 파일별 변경 사항 및 핵심 코드 스케치

#### 1) `Runtime/HLogCore.cs`
- **링 버퍼 (`LogRingBuffer`)**:
  - `List<LogEntry>` 및 `RemoveAt(0)` 제거. 고정 배열 `LogEntry[] _buffer = new LogEntry[MaxLogCount]`(1000), `_head`, `_count`로 구성.
  - 덮어쓰기 발생 시 밀려나는 항목의 `LogType`에 맞춰 에러/경고/일반 카운트 감소.
  - `History` 프로퍼티는 호출 시 `_count` 크기의 배열에 오래된 순서(`_head`부터)로 복사하여 반환. `HistoryCount`는 `_count`를 O(1)로 반환.
- **워커 대기 로그 및 신규 태그 큐 (B1-08, A1-03, PF1-01 반영)**:
  - `s_PendingLogs`: `lock (s_PendingLock)`으로 보호되는 고정 상한(1000) `Queue<PendingLog>` 사용. 큐가 가득 차면 `Dequeue()` 후 `Enqueue()`하여 카운트 드리프트를 원천 방지.
  - `PendingLog` 구조체: `(string Condition, string StackTrace, LogType Type, string ChannelTag, DateTime Timestamp)`.
  - `s_PendingTags`: `RegisterTag(string tag)`가 워커 스레드에서 호출되어 새 태그가 발견된 경우, `RaiseOnMainThread(Action)`(클로저 할당) 대신 `s_PendingTags` 큐(`lock (s_TagLock)`)에 태그 문자열만 넣는다.
  - 프레임당 배출 상한: `FlushPendingLogs()`에서 한 호출당 로그 최대 `MaxFlushLogsPerFrame = 500`개, 신규 태그 이벤트 최대 `MaxFlushTagsPerFrame = 64`개까지만 꺼내어 처리하고 나머지는 다음 프레임으로 넘긴다.
- **예외 채널 태그 보존 (A1-06, B1-05 반영)**:
  ```csharp
  [ThreadStatic] private static string s_CurrentChannelTag;

  internal static string PushChannelTag(string tag)
  {
      string prev = s_CurrentChannelTag;
      s_CurrentChannelTag = tag;
      return prev;
  }

  internal static void PopChannelTag(string prev)
  {
      s_CurrentChannelTag = prev;
  }
  ```
  - `HandleUnityLogThreaded` 진입 즉시 `string overrideTag = s_CurrentChannelTag;`를 읽는다. 메인 스레드면 즉시 `LogEntry` 생성 시 주입하고, 워커 스레드면 `PendingLog.ChannelTag`에 담아 보존한다.
  - 태그 정규식 `s_TagRegex`에 `RegexOptions.Compiled | RegexOptions.Singleline` 적용(여러 줄 로그 파싱).
- **Watch 관리 및 재진입/교체 규칙 (확정 2, 확정 5, A1-02, A1-11, PF1-04 반영)**:
  - `PendingWatch` 대기 맵은 만들지 않는다. 모든 `Watch`/`Unwatch` 진입 시 `if (!EnsureMainThread(...)) return;` 수행.
  - **값 Watch가 게터 Watch를 덮어쓰기 (확정 5)**:
    - `Watch(string key, object value, ...)`(게터 내부 평가 호출이 아닌 공개 값 Watch 호출) 시 `s_WatchGetters.Remove(key)`를 먼저 수행하여 기존 게터를 해제한다.
  - **게터 세대(Generation) 검증으로 재진입 부활 방지 (A1-02)**:
    ```csharp
    private struct WatchGetterEntry
    {
        public string Key;
        public Func<object> Getter;
        public string Category;
        public UnityEngine.Object Owner;
        public bool HasOwner;
        public uint Generation; // 등록·교체 시마다 ++s_NextWatchGen
        public object LastRawValue; // PF1-04: 값 형식/불변 타입 비교 캐시
    }
    ```
    - `Unwatch(key)` 또는 같은 키에 새 `Watch`(값/게터)가 호출되면 `s_WatchGetters`에서 제거되거나 새 `Generation`을 부여받는다.
    - `PollWatchGetters()`는 재사용 리스트 `s_GetterSnapshotBuffer`에 현재 엔트리들을 복사해 순회한다.
    - 각 항목 평가 전후에 `s_WatchGetters.TryGetValue(entry.Key, out var current) && current.Generation == entry.Generation`인지 확인한다. 평가 도중 게터 내부나 앞선 이벤트 구독자가 해당 키를 `Unwatch`했거나 다른 값/게터로 교체했다면 평가 결과를 버린다.
  - **게터 예외와 구독자 예외 분리 및 재귀 방지 (A1-11, B2-04)**:
    - 게터 호출(`entry.Getter()`)과 값 포맷팅만 `try-catch`로 감싸 실패 시 `"<Error: ...>"`로 값을 만든다.
    - `OnWatchUpdated` / `OnLogAdded` / `OnTagDiscovered` 등 공개 이벤트 호출은 별도 `InvokeEventSafely` 헬퍼에서 수행하며, 구독자 예외 발생 시 `s_IsInternalLogging = true` 상태에서 `Debug.LogException`을 호출해 무한 재귀 수집을 막고 다음 구독자 실행을 보장한다.
  - **포맷팅 할당 완화 (PF1-04, A2-23)**:
    - 반환된 `val`이 `string`이거나 ` ValueType`(기본 값 형식)이고 `Equals(val, current.LastRawValue)`가 참이면 `ToString()` 및 `OnWatchUpdated` 호출을 건너뛴다(내부 상태가 변할 수 있는 일반 참조 타입이나 `UnityEngine.Object`는 이름 변경 가능성이 있으므로 매번 평가하되, 문자열이 이전 `WatchEntry.Value`와 같으면 이벤트 발생 생략).
  - **`WatchCount` 및 `Watches` 복사 (PF1-07)**:
    - `public static int WatchCount => s_Watches.Count;` 추가.
    - `public static int WatchVersion { get; private set; }` 추가(Watch 추가/값 변경/삭제/Clear 시 증가).
    - `Watches` 프로퍼티는 각 `WatchEntry`를 복제(`entry.Clone()`)한 새 `Dictionary`를 반환.
  - **PlayerLoop 등록 및 수명주기 (확정 4, B1-02, A1-12)**:
    - `Initialize()` 시 커스텀 `PlayerLoopSystem`(`HLogWatchUpdateSystem`)을 `Update` 단계에 주입(이미 있으면 중복 주입 안 함).
    - `s_LastPolledFrame` 가드는 `PollWatchGetters()`에만 적용하여, PlayerLoop와 UI 폴백(`HB_InGameConsoleUI.Update`)이 같은 프레임에 겹쳐도 게터 평가는 정확히 1회만 돌게 한다.
    - **`Shutdown()` (확정 4)**: `s_IsInitialized = false`, Unity 로그 콜백 해제, `s_PendingLogs` 폐기, `s_PendingTags` 폐기. `PollWatchGetters()`와 `FlushPendingLogs()`는 진입 시 `if (!s_IsInitialized) return;`으로 즉시 빠져나와 모든 수집·평가·대기 로그 처리를 정지한다.
    - **`Clear()` (확정 4)**: 메인 스레드 검사 후 링 버퍼 초기화, 카운트 0 초기화, `s_PendingLogs` 전체 폐기(`lock` 안에서 `Clear()`), `OnLogsCleared` 이벤트 발생(`DiscoverAllChannels()` 재호출 제거).
    - **`ResetStatics()`**: `CaptureMainThreadId()`, `s_WarnedThreadViolationMask = 0`, `s_IsLogEnabled = true`, `s_LastPolledFrame = -1`, 링 버퍼·Watch·게터·대기 로그·대기 태그 초기화, `OnCoreReset` 내부 알림 발생(공개 이벤트 구독자 목록은 에디터 창 유지를 위해 지우지 않음).

#### 2) `Runtime/LogChannel.cs`
- 생성자: `#if USE_HLOG`일 때만 `_logger = new Logger(Debug.unityLogger.logHandler)` 및 `HLogCore.RegisterTag(tag)` 수행.
- `Log`, `Warning`, `Error`, `LogError`, `Exception`, `LogException`, `Watch` 모두 `[Conditional("USE_HLOG")]` 부착.
- `BreakIf`, `Assert`, `ReportIf`는 `[Conditional("USE_HLOG")]` 부착 + 메서드 본문 전체를 `#if UNITY_EDITOR ... #endif`로 감싼다.
- `Exception` / `LogException` 구현:
  ```csharp
  [Conditional("USE_HLOG")]
  public void Exception(Exception exception, UnityEngine.Object context = null)
  {
  #if USE_HLOG
      if (!IsEnabled || !HLogCore.IsLogEnabled || exception == null) return;
      string prev = HLogCore.PushChannelTag(Tag);
      try
      {
          if (context != null) _logger.LogException(exception, context);
          else _logger.LogException(exception);
      }
      finally
      {
          HLogCore.PopChannelTag(prev);
      }
  #endif
  }
  ```

#### 3) `Runtime/InGameConsole.cs` & `Runtime/InGameConsoleSettings.cs`
- `AutoInitializeOnGameStart` 및 `Initialize`: `#if USE_HLOG`로 감싼다. `Application.isEditor || Debug.isDebugBuild` 체크는 **사용하지 않는다**(확정 1: `USE_HLOG` 심볼 하나로만 제어).
- 모든 공개 메서드(`Initialize`, `EnsureUIInstance`, `AddExternalView`, `RemoveExternalView`, `ShowExternalView`, `IsExternalViewActive`, `Close`) 진입부에 `if (!HLogCore.EnsureMainThread(MainThreadApiId.ConsoleUI, nameof(...))) return;` 추가.
- `InGameConsoleSettings`: 에디터와 플레이어 모두 `Resources.Load<InGameConsoleSettings>("HB_InGameConsoleUserSettings")`만 사용한다. `#if UNITY_EDITOR`에서 `AssetDatabase.FindAssets("t:InGameConsoleSettings")`로 확인해 `Resources` 폴더 밖에 에셋이 존재하면 최초 1회 경고 로그를 출력한다.

#### 4) `Runtime/UI/HB_InGameConsoleUI.cs`
- **콘솔 열기/닫기 및 로그 행 수명주기 (확정 3, 확정 7, B1-03, A1-07, PF1-05, PF1-06)**:
  - 상태 플래그: `private bool _hasEverOpened = false;`, `private bool _isLogsDirty = false;`, `private bool _isWatchesDirty = false;`, `private bool _isTagsDirty = false;`.
  - **초기화(`InitializeWithRoot`)**:
    - 재바인딩 전 반드시 `UnbindUIEvents()`를 먼저 호출해 **이전 UI 요소와 이전 델리게이트**의 이벤트 구독을 해제한다(A1-08).
    - 시작 시 콘솔이 닫혀 있으면(`!IsConsoleOpen`) `RefreshAllLogs()` / `RefreshAllWatches()`를 호출하지 않고, `_isLogsDirty = true; _isWatchesDirty = true;`로 표시한 뒤 `UpdateOpenButtonText(HLogCore.HistoryCount)`와 `UpdateWatchButtonCount(HLogCore.WatchCount)`만 갱신한다.
  - **닫혀 있는 동안 이벤트 수신**:
    - `HandleLogAdded`: `UpdateOpenButtonText(HLogCore.HistoryCount)`만 갱신. `!IsConsoleOpen`이면 UI 행을 만들지 않고 `_isLogsDirty = true`만 설정 후 반환.
    - `HandleLogsCleared`: `ClearLogRows()`(이미 생성된 행과 `selectedRow = null` 정리) 수행 후 `!IsConsoleOpen`이면 `_isLogsDirty = false`로 두고 반환.
    - `HandleWatchUpdated` / `HandleWatchRemoved`: `UpdateWatchButtonCount(HLogCore.WatchCount)` 갱신. `!IsConsoleOpen`이면 `_isWatchesDirty = true`만 설정하고 반환(B1-04).
    - `HandleTagDiscovered`: `!IsConsoleOpen`이면 `_isTagsDirty = true`만 설정하고 반환(PF1-02).
  - **콘솔 열기 (`OpenConsole`) (확정 7)**:
    - 이미 열려 있으면(`if (IsConsoleOpen) return;`) 중복 실행 방지.
    - 열린 상태로 전환 후:
      - `if (!_hasEverOpened || _isLogsDirty)`: `_hasEverOpened = true; _isLogsDirty = false; RefreshAllLogs();` 수행.
      - `if (_isWatchesDirty)`: `_isWatchesDirty = false; RefreshAllWatches();` 수행.
      - `if (_isTagsDirty)`: `_isTagsDirty = false; RefreshTagButtons();` 수행.
  - **콘솔 닫기 (`CloseConsole`) (확정 7)**:
    - 이미 생성된 로그 행(`createdLogRows`)은 **파괴하지 않고 그대로 유지**한다(자주 여닫을 때 재생성 스파이크 방지).
  - **`selectedRow` 정리 및 선두 삭제 개선 (확정 7, PF1-06)**:
    - `createdLogRows`를 `List<VisualElement>` 대신 `Queue<VisualElement>`(또는 링 버퍼 인덱스)로 관리해 상한(1000) 도달 시 `RemoveAt(0)`의 O(H) 배열 이동을 없앤다.
    - 상한 초과로 가장 오래된 행이 제거될 때 `if (selectedRow == removedRow) ClearSelectedRow();`를 수행하고, `RefreshAllLogs()`나 `Clear()`로 전체 행을 재구성·제거할 때도 `ClearSelectedRow()`(`selectedRow = null` 및 상세 패널 텍스트 비우기)를 호출한다.
  - **Watch 단건 제거 최적화 (PF1-03)**:
    - `HandleWatchRemoved(string key)`: 열린 상태일 때 `RefreshAllWatches()` 전체 재생성 대신 `watchRowMap.TryGetValue(key, out var row)`로 해당 행만 `watchScrollView.Remove(row)` 및 맵에서 제거한다.

#### 5) `Editor/LogChannelGeneratorWindow.cs`
- **커스텀 Conditional 심볼 UI 및 로직 제거 (확정 1)**:
  - 생성기 창의 커스텀 심볼 입력 필드 제거.
  - 코드 생성 템플릿에서 모든 로그/Watch/Assert/BreakIf/ReportIf 메서드에 `[Conditional("USE_HLOG")]`만 출력하도록 수정.
  - `BreakIf`, `Assert`, `ReportIf` 생성 코드는 메서드 내부를 `#if UNITY_EDITOR ... #endif`로 감싸도록 템플릿 수정.
- **프로젝트 전체 1회 자동 생성 마커 (확정 6, A1-10, B1-09)**:
  - 마커 경로: `ProjectSettings/HB_InGameConsole_Generated.txt` (내용: `"1"`, 비-YAML 순수 텍스트 파일, Git 공유 대상).
  - `[InitializeOnLoadMethod]` 자동 생성 체크 순서:
    1. 마커 파일(`File.Exists("ProjectSettings/HB_InGameConsole_Generated.txt")`)이 존재하면 즉시 종료(재생성 안 함).
    2. 마커가 없는데 프로젝트 내에 기존 `LogChannels.cs` 파일이 이미 존재하면, 파일을 덮어쓰지 않고 마커 파일만 기록한 뒤 종료.
    3. 둘 다 없으면 기본 `LogChannels.cs` 생성을 시도하고, **파일 쓰기가 예외 없이 성공한 직후에만** 마커 파일을 기록한다.

#### 6) `Editor/HLogConsoleWindow.cs`
- **심볼 OFF 표시 (확정 1)**:
  - 에디터 툴바의 `USE_HLOG` 토글 버튼은 항상 유지.
  - `#if !USE_HLOG`일 때 창 본문 상단 또는 중앙에 `"USE_HLOG 심볼이 꺼져 있습니다. 로그 수집 및 Watch를 사용하려면 상단 툴바에서 USE_HLOG를 켜주세요."` 안내 배너를 표시.
- **Watch 탭 스냅샷 캐시 및 리셋 동기화 (A1-09, PF1-07)**:
  - `HLogCore.WatchVersion`을 확인하여 이전 렌더링 시점의 버전과 같으면 `HLogCore.Watches`를 다시 호출하지 않고 캐시된 스냅샷을 그린다.
  - `EditorApplication.playModeStateChanged` 및 `HLogCore.OnCoreReset`에서 창의 로그 필터 캐시와 Watch 캐시를 즉시 무효화한다.

#### 7) `README.md`
- 실제 API 이름(`Log`, `Warning`, `Error`, `Exception`, `LogException`, `LogChannel.LogError`)으로 예제 최신화(확정 3).
- `USE_HLOG` 단일 심볼 정책 명시: 에디터와 빌드(개발/비개발 무관) 모두 `USE_HLOG`가 켜져 있으면 동작하고 꺼져 있으면 모든 로그/Watch 호출과 코어가 제거됨. 출시 빌드 전 `USE_HLOG`를 끄는 것은 빌드 담당자 책임임을 명시(확정 1).
- 기존 프로젝트는 생성기 창에서 `LogChannels.cs`를 재생성해야 함을 안내(확정 1).
- 스레드 규칙 명시: `HLog.Get`/`Channel` 및 로그 출력만 멀티스레드 허용, `Watch`/`Unwatch`/`Clear` 등 나머지 API는 메인 스레드 전용(확정 2).

---

## 3. 자기 보고

- **바뀐 파일 목록 (총 7개)**:
  1. `Runtime/HLogCore.cs`
  2. `Runtime/LogChannel.cs`
  3. `Runtime/InGameConsole.cs`
  4. `Runtime/InGameConsoleSettings.cs`
  5. `Runtime/UI/HB_InGameConsoleUI.cs`
  6. `Editor/LogChannelGeneratorWindow.cs`
  7. `Editor/HLogConsoleWindow.cs`
  8. `README.md` (문서)
- **파일별 줄 수**: 산출물이 설계 및 핵심 코드 스케치이므로 전체 줄 수는 산출 대상 아님.
- **바뀐 공개 API·필드 및 설계 결정 목록**:
  - `HLogCore.WatchCount` (`public static int`) 추가.
  - `HLogCore.WatchVersion` (`internal/public static int`) 추가.
  - `LogChannel` 및 생성 `HLog`의 모든 출력·Watch·진단 메서드에 `[Conditional("USE_HLOG")]` 단일 적용 (`Error`, `Exception`, `LogException`, `LogError`에 신규 추가, 기존 `UNITY_EDITOR` 조건 제거).
  - 코어·UI 활성 조건을 `Application.isEditor || Debug.isDebugBuild`에서 `#if USE_HLOG` 단일 컴파일 조건으로 변경.
  - Watch·Unwatch·Clear·Initialize·Shutdown·IsLogEnabled setter·UI API를 메인 스레드 전용으로 변경(`PendingWatch` 대기 맵 제거).
  - 같은 키에 게터 Watch가 있을 때 값 Watch 호출 시 게터 해제 후 값으로 교체.
  - 콘솔 최초 열기 전 로그 행 미생성, 한 번 연 뒤 닫을 때는 생성된 행 유지, 재오픈 시 닫힌 동안의 변경 반영.
  - `LogChannels.cs` 1회 자동 생성 마커를 `EditorPrefs`에서 `ProjectSettings/HB_InGameConsole_Generated.txt`로 변경.

---

## 4. 확정 사항 반영 내역

| 확정 항목 | 반영 위치 및 방식 |
|---|---|
| **1. `USE_HLOG` 단일 심볼** | - `LogChannel.cs` 및 `LogChannelGeneratorWindow.cs` 템플릿: 모든 로그(`Log`, `Warning`, `Error`, `Exception`, `LogException`, `LogError`)와 `Watch` 계열에 `[Conditional("USE_HLOG")]` 적용. `BreakIf`, `Assert`, `ReportIf`는 `[Conditional("USE_HLOG")]` + 본문 `#if UNITY_EDITOR`. 생성기의 커스텀 심볼 설정 제거.<br>- `HLogCore.cs`, `InGameConsole.cs`, `HB_InGameConsoleUI.cs`: `#if USE_HLOG`로 동작 코드 분리, `#if !USE_HLOG`에서는 빈 동작 및 읽기 전용 빈 컬렉션/`null` 반환.<br>- `HLogConsoleWindow.cs`: `#if !USE_HLOG`일 때 `"USE_HLOG 꺼짐"` 표시.<br>- `README.md`: 비개발 빌드 포함 `USE_HLOG` 동작 및 출시 전 해제 책임, 기존 생성 파일 재생성 안내 명시. |
| **2. 스레드 계약** | - `HLogCore.GetChannel`(`HLog.Get`/`Channel`)과 로그 출력만 멀티스레드 허용.<br>- `Watch`, `Unwatch`, `Clear`, `IsLogEnabled` setter, `Initialize`, `Shutdown`, `PollWatchGetters`, `FlushPendingLogs`, `InGameConsole` UI API 진입부에 `EnsureMainThread` 배치. 위반 시 즉시 반환 + `Interlocked.CompareExchange` 비트마스크로 API별 첫 1회 `Debug.LogError` 출력.<br>- `PendingWatch` 대기 맵 제거. |
| **3. 기존 결정 유지** | - `HLogCore` 고정 크기(1000) 링 버퍼 적용.<br>- 닫힌 콘솔 로그 행 지연 생성.<br>- `README.md`만 실제 코드로 수정(별칭 API 추가 없음).<br>- Watch 상태 변경·평가는 메인 스레드 전용. |
| **4. Shutdown / Clear** | - `HLogCore.Shutdown()`: 로그 콜백 해제 + `s_IsInitialized = false`로 `PollWatchGetters`(Watch 평가) 및 `FlushPendingLogs`(대기 로그 처리) 모두 정지 + 대기 큐 폐기.<br>- `HLogCore.Clear()`: 링 버퍼 초기화와 함께 `s_PendingLogs`의 미처리 타 스레드 대기 로그까지 모두 폐기. |
| **5. 게터 → 값 Watch 교체** | - `HLogCore.Watch(key, value)` 호출 시 `s_WatchGetters.Remove(key)`를 수행해 기존 게터를 해제하고 세대(`Generation`)를 갱신하여 나중 호출인 값 Watch가 이기도록 처리. |
| **6. 자동 생성 1회 마커** | - `LogChannelGeneratorWindow.cs`: `ProjectSettings/HB_InGameConsole_Generated.txt`(비-YAML 텍스트)를 확인하여 없을 때만 실행하고, 기존 `LogChannels.cs` 존재 확인 또는 파일 생성 성공 직후에만 마커 파일을 생성. |
| **7. 콘솔 닫기 시 로그 행 유지** | - `HB_InGameConsoleUI.cs`: 최초 `OpenConsole()` 전에는 로그 행을 만들지 않음(`_hasEverOpened == false`). 한 번 열린 뒤 `CloseConsole()`을 호출할 때는 생성된 행을 파괴하지 않고 유지하며, 닫혀 있는 동안 온 로그는 `_isLogsDirty = true`로 표시했다가 다음 `OpenConsole()` 때 반영. `selectedRow`는 상한 초과 행 제거·`Clear`·전체 재구성 시 정리. |

---

## 5. 성능 지적 반영 내역

| PF1 ID | 반영 · 부분 · 미반영 | 이유 |
|---|---|---|
| **PF1-01** | **부분 반영** | `FlushPendingLogs`에 프레임당 로그 처리 상한(500개)과 신규 태그 이벤트 처리 상한(64개)을 두고, 닫힌 콘솔에서는 버튼 숫자만 갱신하도록 분리했다. 단, 메인 스레드 직접 로그의 동기 `OnLogAdded` 이벤트 발생 의미는 기존 계약을 유지하기 위해 바꾸지 않았다. |
| **PF1-02** | **부분 반영** | `PendingWatch` 제거로 대기 Watch 맵 누적은 소멸했다. 닫힌 콘솔에서 신규 태그 버튼 생성을 열기 시점까지 지연(`_isTagsDirty`)하고, 태그 전달을 `Action` 클로저 큐 대신 `Queue<string>`으로 바꿔 할당을 제거했다. 고유 태그·채널 캐시 자체의 임의 퇴출은 API 동작을 바꾸므로 적용하지 않는다. |
| **PF1-03** | **반영** | `HandleWatchRemoved`에서 `RefreshAllWatches()` 전체 재생성 대신 `watchRowMap`에서 삭제된 키의 UI 행만 단건 제거하고, 닫힌 동안에는 `_isWatchesDirty`로 병합한다. |
| **PF1-04** | **부분 반영** | 공개 API(`Func<object>`) 시그니처 유지 제약 내에서, 반환된 값 형식/`string`이 이전 원시 값(`LastRawValue`)과 같으면 `ToString()` 및 문자열 결합을 건너뛰도록 반영했다(제네릭 오버로드 추가를 통한 박싱 완전 제거는 API 추가 금지 제약으로 미반영). |
| **PF1-05** | **반영** | 확정 7에 맞춰 초기화·패널 재로드 시 닫혀 있으면 행을 생성하지 않고, 한 번 연 뒤 닫을 때는 행을 유지하되 상한 퇴출·Clear·재구성 시 `selectedRow` 참조를 해제하여 퇴출된 `LogEntry` 누수를 막는다. |
| **PF1-06** | **부분 반영** | 열린 상태에서 상한 도달 시 `List.RemoveAt(0)`의 O(H) 배열 이동을 없애기 위해 `Queue<VisualElement>` 구조로 변경했다. 열기 프레임의 분할 생성은 구현 복잡도 대비 확정 사양 외이므로 제외하고 재오픈 시 행 유지(확정 7)로 반복 생성 비용을 없앴다. |
| **PF1-07** | **반영** | 개수 조회는 `HLogCore.WatchCount`를 사용하고, 에디터 창(`HLogConsoleWindow`)은 `HLogCore.WatchVersion`이 변했을 때만 `HLogCore.Watches` 스냅샷을 다시 가져오도록 캐싱한다. |

---

## 6. 미해결 쟁점

1. **설치 시 `USE_HLOG` 심볼 자동 추가 및 `HB_Build` 연동**: 사용자 확정 사항에 따라 **범위 밖(후속 작업)**으로 남긴다.
2. **`Debug.Log` 직접 호출의 빌드 포함**: `USE_HLOG`가 켜진 빌드에서는 Unity 기본 `Debug.Log`도 수집·표시되며, 게임 코드의 `Debug.Log` 자체를 막는 것은 **범위 밖**이다(확정 1).
3. **고유 태그·채널 이름의 무한 동적 생성**: 호출자가 루프에서 매번 다른 이름으로 `HLog.Get("Tag_" + i)`를 끝없이 호출하는 비정상 사용에 대한 강제 상한은 두지 않는다(README에 채널은 정적/고정 태그로 사용할 것을 권장).

---

## 7. 테스트 계획

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | **`USE_HLOG` ON/OFF 컴파일 및 호출 제거 (확정 1)** | `LogChannels.cs` 재생성 후 `USE_HLOG`를 껐을 때, `HLog.Common.Log/Warning/Error/Exception/Watch/BreakIf/Assert/ReportIf`의 인자 평가 부작용(예: `Func` 카운터)이 0회이고, `HLogCore.History`/`Watches`/`KnownTags`가 `null`이 아닌 읽기 전용 빈 컬렉션을 반환하며, 씬 배치된 `InGameConsole` 및 `HB_Cheat` 연동 API 호출 시 UI가 생성되지 않아야 한다. 에디터 창에는 `"USE_HLOG 꺼짐"`이 표시되어야 한다. |
| 2 | **비개발 빌드 + `USE_HLOG` ON 동작 (확정 1)** | `Development Build`를 끄고 `USE_HLOG`를 켠 상태에서 빌드 시, 로그 수집·Watch 평가·인게임 콘솔 UI가 정상 동작해야 한다. |
| 3 | **스레드 계약 및 위반 1회 에러 (확정 2)** | 백그라운드 스레드(`Task.Run`)에서 `HLog.Get("WorkerTag")` 및 로그 출력 시 메인 스레드에서 예외 없이 태그 버튼과 로그(수신 시각 보존)가 반영되어야 한다. 반면 워커 스레드에서 `Watch`, `Unwatch`, `Clear`, `IsLogEnabled = false`, `Initialize`, `Shutdown`을 각각 10회씩 호출했을 때 상태가 변하지 않고 API별로 에러 로그가 정확히 1회씩만 출력되어야 한다. |
| 4 | **Watch 게터→값 교체 및 재진입 방지 (확정 5, A1-02)** | 1) 같은 키에 게터 Watch 등록 후 값 Watch를 호출하면 다음 프레임 `PollWatchGetters` 이후에도 게터가 돌지 않고 값 Watch의 값이 유지되어야 한다.<br>2) 게터 콜백 내부에서 자기 자신을 `Unwatch`하거나 다른 값으로 `Watch`했을 때, 게터 반환값이 삭제된 키를 부활시키거나 새 값을 덮어쓰지 않아야 한다. |
| 5 | **Shutdown 및 Clear 수명주기 (확정 4)** | 1) 워커 스레드가 로그를 큐에 넣은 직후(플러시 전) 메인에서 `Clear()`를 호출하면, 다음 프레임 `FlushPendingLogs()` 이후에도 기록이 0개여야 한다.<br>2) `Shutdown()` 호출 후에는 로그 출력, Watch 게터 폴링, 대기 로그 배출이 모두 멈춰야 한다. |
| 6 | **콘솔 지연 생성 및 여닫기 행 유지 (확정 3, 확정 7)** | 1) 시작 후 콘솔을 한 번도 열지 않고 로그 1200개를 찍었을 때 코어 `History`는 최신 1000개(오래된 순서)이고 UI 로그 행(`createdLogRows`) 개수는 0개여야 한다.<br>2) 최초 `OpenConsole()` 시 1000개 행이 생성되고, `CloseConsole()` 시 행이 파괴되지 않으며(1000개 유지), 닫힌 동안 로그 10개가 추가된 뒤 다시 열면 최신 상태(1000개)로 반영되어야 한다. 행 선택 후 상한 퇴출·`Clear()` 시 `selectedRow`가 `null`이어야 한다. |
| 7 | **예외 채널 태그 보존 및 중첩 복원 (A1-06, B1-05)** | 메인 스레드와 워커 스레드에서 각각 `HLog.Get("Net").Exception(new Exception("test"))` 호출 시 기록된 `LogEntry.Tag`가 `"Net"`이고, 직후 찍은 일반 `Debug.Log("plain")`에는 `"Net"` 태그가 오염되지 않아야 한다. |
| 8 | **`LogChannels.cs` 1회 자동 생성 마커 (확정 6)** | `ProjectSettings/HB_InGameConsole_Generated.txt`와 `LogChannels.cs`를 모두 지운 상태에서 도메인 리로드 시 `LogChannels.cs`와 마커 파일이 생성되고, 이후 `LogChannels.cs`만 삭제하고 에디터를 재시작하거나 `EditorPrefs`를 초기화해도 다시 자동 생성되지 않아야 한다. |