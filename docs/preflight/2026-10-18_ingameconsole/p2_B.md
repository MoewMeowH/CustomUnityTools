### 1. 상대 주장 판정

| 상대 ID | 판정 | 근거 | 검증 방법 |
|---|---|---|---|
| A1-01 | REJECTED | [사실] `confirmed.md`(확정 2)에 따라 `Watch`/`Unwatch`는 메인 스레드 전용이 되었고 `PendingWatch` 대기 맵은 만들지 않기로 결정되어 발생 조건(워커 스레드 Watch 경합)이 소멸함. | 워커 스레드에서 `Watch`/`Unwatch` 호출 시 상태가 변하지 않고 최초 1회 에러 로그만 발생하는지 테스트. |
| A1-02 | SUPPORTED | [사실] 메인 스레드 전용으로 바뀌었더라도, `PollWatchGetters` 순회 중 게터 내부나 `OnWatchUpdated` 이벤트 구독자에서 동기적으로 `Unwatch(key)` 또는 `Watch(key, newGetter)`를 호출하는 재진입 시나리오는 여전히 유효함. 순회용 복사본만으로는 평가 직후 `Watch(key, val)`이 삭제된 키를 부활시키거나 새 등록을 덮어쓰는 것을 막지 못함. | 게터 콜백 안에서 `HLogCore.Unwatch(자기키)`를 호출한 뒤 값을 반환하게 하고, 폴링 직후 `Watches`에 해당 키가 부활하지 않는지 확인. |
| A1-03 | REVISED | [사실] `PendingWatch`는 제거되었으나, 워커 스레드의 `HLog.Get("동적태그")` 호출은 여전히 허용됨(`confirmed.md` 확정 2). 새 태그 발견 시 `RaiseOnMainThread`의 무제한 `ConcurrentQueue<Action>`에 쌓이는 문제와 프레임당 처리 예산이 없는 점은 유효함. | 워커 스레드에서 루프로 고유 채널 이름을 대량 생성(`HLog.Get("Tag_" + i)`)할 때 메인 스레드 큐 크기와 프레임 처리 시간을 측정. |
| A1-04 | REVISED | [사실] `confirmed.md`(추가 확정)로 활성 조건이 `#if USE_HLOG` 단일 심볼로 변경됨. `USE_HLOG` 미정의 시 `InGameConsole.EnsureUIInstance`, `Awake`/`OnEnable`(씬 배치), 외부 뷰 API 등 모든 우회 진입점도 비활성화되어야 한다는 지적은 타당함. `LogChannel.Error`/`Exception`은 `[Conditional("USE_HLOG")]`로 확정되어 사양 미확정 부분은 해소됨. | `USE_HLOG` 심볼을 빼고 프리팹을 씬에 배치하거나 `InGameConsole.EnsureUIInstance()`, `AddExternalView()` 등을 직접 호출했을 때 UI 생성 및 동작이 전혀 일어나지 않는지 확인. |
| A1-05 | REVISED | [사실] `confirmed.md`(확정 2)에서 `Clear`와 `IsLogEnabled` setter를 메인 스레드 전용(위반 시 무시 + API별 첫 1회 에러 로그)으로 확정함. 따라서 워커 스레드 이벤트 발생 문제는 메인 스레드 체크 게이트를 넣는 것으로 해결되며, 단 메인 스레드에서 `Clear()` 호출 시 아직 플러시되지 않은 워커 대기 로그(`s_PendingLogs`)도 함께 비워야 이전 로그가 부활하지 않음. | 워커 스레드에서 로그를 남긴 직후(플러시 전) 메인 스레드에서 `Clear()`를 호출하고, 다음 프레임 `FlushPendingLogs` 이후 기록이 0개인지 확인. |
| A1-06 | SUPPORTED | [사실] 내 B1-05와 동일한 지적이며, 추가로 중첩 로그 발생 시 이전 태그 복원(`string prev = s_CurrentChannelTag; try ... finally { s_CurrentChannelTag = prev; }`)까지 짚은 점이 타당함. | 커스텀 `ILogHandler` 또는 예외 메시지 평가 중 중첩 채널 로그를 발생시켜 바깥쪽 예외 태그가 유지·복원되는지 확인. |
| A1-07 | SUPPORTED | [사실] 내 B1-03과 일치함. 추가로 콘솔을 닫거나 로그를 재구성할 때 `selectedRow` 참조를 비우지 않으면 이미 제거된 VisualElement와 `LogEntry`가 메모리에 잡혀 있는 문제를 정확히 지적함. | 콘솔을 열어 행을 선택한 뒤 콘솔을 닫거나 `Clear()` 후 `selectedRow` 필드가 `null`인지 확인. |
| A1-08 | SUPPORTED | [사실] `HB_InGameConsoleUI.InitializeWithRoot`가 기존 UI 요소의 이벤트(`clicked`, `RegisterValueChangedCallback`)를 해제하지 않고 덮어쓰므로, 패널 재로드나 재초기화 시 콜백 중복 실행 및 누수가 발생할 수 있음. | `InitializeWithRoot`를 2회 연속 호출한 뒤 Clear나 탭 버튼을 1회 클릭했을 때 핸들러가 2번 호출되는지 확인. |
| A1-09 | NEEDS_TEST | [미확인] 도메인 리로드 비활성화 시 `HLogCore` 초기화가 에디터 창(`HLogConsoleWindow`)의 필터 캐시를 무효화하는지는 `HLogConsoleWindow`의 플레이 모드 전환 코드가 첨부되지 않아 확인 불가. **자료 부족: `HLogConsoleWindow.OnPlayModeChanged` 및 캐시 갱신 코드 전체.** | Enter Play Mode Options(Reload Domain 체크 해제)로 플레이 진입 시 에디터 콘솔 창의 이전 로그 목록이 즉시 비워지는지 확인. |
| A1-10 | SUPPORTED | [사실] 내 B1-09와 동일한 지적임. `EditorPrefs`는 로컬 머신/OS 계정 종속이므로 Git 등 저장소를 공유하는 팀·CI 환경에서는 '프로젝트당 1회'를 보장하지 못함. | `EditorPrefs` 키를 지우거나 다른 PC 환경에서 프로젝트를 열었을 때 삭제된 `LogChannels.cs`가 재생성되는지 확인. |
| A1-11 | SUPPORTED | [사실] `HLogCore.PollWatchGetters`의 `try-catch` 블록 안에 `Watch(key, val)` 호출과 `OnWatchUpdated` 이벤트 발생이 함께 들어 있어, 외부 이벤트 구독자가 던진 예외가 게터 평가 오류(`<Error: ...>`)로 둔갑해 Watch 값을 덮어쓰고 루프를 깨뜨릴 수 있음. | 정상 값을 반환하는 게터를 등록하고 `OnWatchUpdated` 구독자에서 예외를 던지게 한 뒤, Watch 값이 `<Error: ...>`로 오염되는지 확인. |
| A1-12 | SUPPORTED | [사실] `Shutdown()` 호출 시 Unity 로그 이벤트만 해제할 뿐, 새로 도입할 `PlayerLoop` 폴링 중단이나 대기 큐(`s_PendingLogs` 등) 정리가 정의되지 않음. | Watch 게터를 등록하고 `HLogCore.Shutdown()`을 호출한 뒤 다음 프레임에 게터가 여전히 호출되는지 확인. |
| PF1-01 | SUPPORTED | [사실] 워커 로그 배출 개수만 제한해도 메인 스레드 직접 로그나 태그 발견 이벤트, 다수의 UI 갱신이 겹치면 프레임 스파이크가 발생할 수 있으며, 여러 진입점(`PlayerLoop`, UI 폴백)이 같은 프레임에 중복 배출하지 않도록 프레임 가드를 공유해야 함(B1-02와 맥락 일치). | 한 프레임에 워커 로그 2000개와 태그 생성을 동시에 발생시키고 `PlayerLoop`와 UI `Update`가 모두 돌 때 프레임당 처리 건수가 상한(예: 1000개)을 넘지 않는지 확인. |
| PF1-02 | REVISED | [사실] `PendingWatch`는 제거되었으나(`confirmed.md` 확정 2), 고유 태그·채널 캐시 무제한 누적과 닫힌 UI에서의 태그 버튼 즉시 생성 문제는 유효함(B1-04와 일치). | 콘솔이 닫힌 상태에서 새 태그로 로그를 찍었을 때 UI 태그 버튼 요소가 생성되지 않는지 확인. |
| PF1-03 | SUPPORTED | [사실] `HandleWatchRemoved`가 단건 삭제 대신 `RefreshAllWatches()`(전체 클리어 후 재생성)를 호출하고 있어, owner 파괴로 다수의 Watch가 한 프레임에 해제될 때 O(W²) UI 재생성과 스냅샷 복사 할당이 발생함. | 콘솔이 열린 상태에서 동일 `GameObject`를 owner로 하는 Watch 50개를 등록하고 해당 오브젝트를 `Destroy`했을 때 `RefreshAllWatches` 대신 개별 행 제거만 수행되는지 확인. |
| PF1-04 | SUPPORTED | [사실] 순회용 리스트를 재사용해도 `Func<T>`를 `Func<object>`로 감싸는 래퍼에서 값 형식 박싱이 매 프레임 일어나고, 값 변경 여부와 무관하게 `ToString()`과 Unity 객체 이름 문자열 결합(`$"{uObj.name} ({type})"`)이 매 프레임 할당됨. | `USE_HLOG` 활성 상태에서 `int`를 반환하는 게터 100개를 등록하고 콘솔을 닫은 채 프로파일러로 매 프레임 GC.Alloc(박싱 및 문자열 할당)을 측정. |
| PF1-05 | SUPPORTED | [사실] A1-07 및 내 B1-03과 동일함. 초기화·패널 재로드 시 닫힘 상태면 행 생성을 건너뛰고, 닫을 때 기존 행·선택 참조 정리 여부를 명확히 해야 함. | 로그 1000개 기록 후 콘솔 열기 → 행 선택 → 콘솔 닫기 후 UI 행 개수와 `selectedRow` 참조 해제 여부 확인. |
| PF1-06 | SUPPORTED | [사실] 코어를 링 버퍼로 바꿔도 UI가 열린 상태에서는 `createdLogRows.RemoveAt(0)`이 여전히 O(H) 배열 복사를 유발하며, 콘솔을 여는 프레임에 1000개 행을 동기 생성할 때 모바일에서 프레임 드랍이 발생할 수 있음. | 로그 1000개가 찬 상태에서 콘솔을 열 때의 프레임 시간과, 열린 상태에서 추가 로그 발생 시 `RemoveAt(0)` 호출 여부를 확인. |
| PF1-07 | SUPPORTED | [추론] `HLogCore.Watches` 프로퍼티가 호출마다 딕셔너리와 `WatchEntry`를 깊은 복사하게 바뀌면, 에디터 창이나 외부 코드가 매 GUI 이벤트마다 `HLogCore.Watches`를 읽을 때 심각한 GC 할당이 발생함. | 에디터 콘솔 창의 Watch 탭을 열어둔 상태에서 프로파일러로 `OnGUI` 당 `HLogCore.Watches` 호출 횟수와 할당량을 확인. |

---

### 2. 상대 수정안 검토

- **B2-01 (A1-01 수정안 검토)**: A1-01의 버전 비교·명시적 접수 순서 직렬화 수정안은 `confirmed.md`(확정 2: Watch는 메인 스레드 전용, `PendingWatch` 미구현)로 인해 불필요해졌으므로 채택하지 않는다. 대신 워커 스레드에서 메인 전용 API 호출 시 `IsMainThread` 검사 후 처리 거부 + API별 최초 1회 에러 로그(`Interlocked.CompareExchange` 비트마스크 또는 플래그)만 남기면 된다.
- **B2-02 (A1-03 / PF1-01 수정안 검토)**: 시간(ms) 기반 처리 예산은 `Stopwatch` 호출 오버헤드와 프레임 레이트 변동에 따른 비결정적 동작을 유발할 수 있으므로, **프레임당 고정 개수 상한(예: 로그 256~1000개, 신규 태그 이벤트 64개)**으로 단순화하는 것이 실용적이고 안전하다. 또한 `RaiseOnMainThread(Action)`의 범용 `Action` 큐 대신 신규 태그 문자열을 담는 `ConcurrentQueue<string> s_PendingTags`로 바꾸면 람다 클로저 할당까지 제거할 수 있다.
- **B2-03 (A1-07 / PF1-05 수정안 중 '닫을 때 행 파괴'의 부작용)**: 콘솔을 닫을 때마다 생성된 1000개의 `VisualElement`를 즉시 `Clear()`로 파괴하면, 사용자가 콘솔을 자주 여닫을 때 열 때마다 1000개 재생성 비용(PF1-06의 프레임 스파이크)을 반복해서 치르게 된다. 따라서 **최초 열기 전까지는 아예 생성하지 않되(지연 생성), 일단 한 번이라도 열어서 생성된 행은 닫을 때 파괴하지 않고 풀(또는 고정 링 버퍼 리스트)로 재사용**하거나, 파괴 여부는 설정/사양으로 결정해야 한다. 단, `selectedRow` 참조 해제와 닫혀 있는 동안 새 로그의 UI 반영 중지(`isLogsDirty = true` 마킹 후 다시 열 때 동기화)는 반드시 적용해야 한다.
- **B2-04 (A1-11 수정안의 재귀 위험)**: 이벤트 구독자(`OnLogAdded`, `OnWatchUpdated` 등) 예외를 잡아서 `Debug.LogError`나 `Debug.LogException`으로 보고할 때, 현재 `HLogCore`가 `Application.logMessageReceivedThreaded`를 구독 중이므로 **에러 로그 출력이 다시 `HandleUnityLogThreaded` → `OnLogAdded`를 호출해 무한 재귀(StackOverflow)**에 빠질 위험이 있다. 구독자 예외 로깅 시에는 재진입 방지 플래그(`[ThreadStatic] bool s_IsHandlingInternalError`)를 두어 `HLogCore` 수집을 건너뛰게 해야 한다.
- **B2-05 (PF1-04 수정안의 구현 난이도 및 범위)**: `Watch<T>(string key, Func<T> getter)` 제네릭 오버로드가 없는 상태에서 값 형식 박싱을 완전히 없애려면 공개 API 시그니처 추가가 필요해 **요청 범위(API 유지)를 넘을 수 있다**. 현재 시그니처(`Func<object>`) 안에서는 게터가 반환한 `object`와 이전 `object`를 `Equals`로 먼저 비교하고, 같으면 `ToString()` 및 `UnityEngine.Object` 이름 포맷팅 문자열 생성을 건너뛰는 수준으로 타협하는 것이 안전하다(문자열 할당은 제거되나 값 형식 반환 시 박싱 1회는 남음).
- **B2-06 (PF1-07 수정안 보완)**: `HLogCore.Watches`의 깊은 복사로 인한 반복 할당을 막으려면, `HLogCore`에 변경 버전 번호(`int WatchVersion`)를 노출하거나 `CopyWatchesTo(Dictionary<string, WatchEntry> buffer)` 형태를 내부/에디터 창에 제공해, 변경이 없을 때는 에디터 창이 스냅샷을 재사용하도록 해야 한다.

---

### 3. 내 Phase 1 수정

- **B1-01 (철회)**: `confirmed.md`(확정 2)에서 `Watch` 계열과 `Unwatch`를 모두 메인 스레드 전용으로 확정하고 `PendingWatch` 대기 맵을 제거하기로 했으므로, 워커 스레드의 `Watch(getter)`와 `Watch(value)` 간 대기 명령 덮어쓰기 시나리오는 전제가 사라져 철회한다.
- **B1-07 (수정)**: `confirmed.md`(확정 1 및 추가 확정)에 따라 조건부 컴파일 기준을 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`에서 **`#if USE_HLOG` 단일 심볼**로 수정한다. 또한 사양 질문이었던 `LogChannel.Error` / `Exception`의 릴리즈 동작도 모두 `[Conditional("USE_HLOG")]`를 붙여 호출 자체를 제거하는 것으로 확정되었으므로 해당 조건부 결론을 확정 사양으로 대체한다. (`#if !USE_HLOG`일 때 `History`, `Watches`, `KnownTags`가 `null` 대신 빈 컬렉션을 반환해야 한다는 지적은 그대로 유지한다.)

---

### 4. 미해결 쟁점

1. **메인 스레드 전용 API를 워커 스레드에서 호출했을 때의 '최초 1회 에러 로그' 출력 방식 (재귀 및 스레드 안전성)**:
   - 워커 스레드에서 `HLog.Watch(...)` 등을 잘못 호출했을 때 에러 로그를 찍기로 확정(`confirmed.md` 확정 2)했으나, 워커 스레드에서 `Debug.LogError`를 호출하면 다시 `HLogCore.HandleUnityLogThreaded`로 수집된다. 이 에러 로그를 인게임 콘솔에도 표시할 것인지(자연스럽게 `Debug.LogError` 호출), 그리고 API별 1회 제한 플래그(`s_WarnedApis` 비트마스크)를 `ResetStatics`(플레이 모드 재진입) 시 초기화할 것인지 구현 시 누락하지 말아야 한다.
2. **에디터 콘솔 창(`HLogConsoleWindow`)의 `USE_HLOG` 미정의 시 컴파일 및 참조 처리**:
   - `confirmed.md`(추가 확정)에서 `USE_HLOG`가 꺼져 있으면 런타임 코어는 빈 껍데기가 되고 에디터 창은 `"USE_HLOG 꺼짐"` 상태를 표시하기로 했다. 이때 런타임 어셈블리의 공개 API 시그니처(`HLogCore.Watches`, `History` 등)는 `#if !USE_HLOG`에서도 그대로 유지되어야 에디터 어셈블리가 컴파일 에러 없이 빌드된다.
3. **`LogChannels.cs` 자동 생성 1회 기록 위치 (A1-10 / B1-09)**:
   - `EditorPrefs`는 로컬 PC 단위라 팀원/CI 환경에서 삭제된 파일이 재생성될 수 있다. 패키지 제약상 YAML 에셋을 쓰지 않기로 했으므로(`request.md:29`), `ProjectSettings/` 아래에 간단한 텍스트/JSON 마커 파일을 두어 Git으로 공유할지, 로컬 `EditorPrefs`로 충분한지 결정이 필요하다.

---

### 5. 사용자에게 필요한 질문

1. **콘솔을 닫을 때 이미 생성된 UI 로그 행의 처리 방식 (A1-07, PF1-05, PF1-06 관련)**
   - 콘솔을 한 번 열면 최대 1000개의 UI 행이 생성됩니다. 콘솔을 **다시 닫을 때** 메모리를 즉시 회수하기 위해 생성된 UI 행들을 파괴(`Clear`)할까요(다시 열 때 재생성 비용 발생), 아니면 한 번 생성된 UI 행들은 풀(Pool)로 유지하고 화면만 숨길까요?
2. **`LogChannels.cs` 1회 자동 생성 마커의 팀 공유 여부 (A1-10, B1-09 관련)**
   - 계획서의 `EditorPrefs`는 개인 PC(OS 계정)에만 저장되어, 한 사람이 `LogChannels.cs`를 지우고 커밋해도 다른 팀원 PC에서 프로젝트를 열면 다시 자동 생성됩니다. 개인 PC당 1회(`EditorPrefs`)면 충분합니까, 아니면 Git에 커밋될 수 있는 `ProjectSettings/` 내 비-YAML 파일(예: JSON/텍스트)로 프로젝트 전체에서 1회만 생성되게 할까요?