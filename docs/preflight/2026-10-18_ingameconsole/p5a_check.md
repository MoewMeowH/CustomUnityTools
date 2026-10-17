1. 새로 찾은 문제

- **C-01**
  - **위치**: `HLogCore.cs:456-489` (`ResetStatics`), `LogChannelGeneratorWindow.cs:460-464` (`AppendChannelClass`)
  - **문제**: 도메인 리로드 비활성화(Enter Play Mode Without Domain Reload) 시 정적 채널(`HLog.Common` 등)과 동적 채널의 인스턴스 분리 및 태그 누락 회귀
  - **발생 조건**: 에디터 Project Settings에서 Enter Play Mode Options를 켜고 Reload Domain을 끈 상태로 두 번째 이상 플레이 모드에 진입할 때
  - **영향**:
    1. `HLog` 및 중첩 채널 클래스들에 `[NoAutoStaticsCleanup]`이 적용되어 있어 `HLog.Common.Channel` 등의 `readonly` 정적 필드는 최초 생성된 `LogChannel` 인스턴스를 계속 유지한다.
    2. 반면 `HLogCore.ResetStatics()`는 매 플레이 진입 시 `s_KnownTags.Clear()`와 `s_ChannelCache.Clear()`를 수행한다.
    3. 이후 `Initialize() -> DiscoverAllChannels()`는 `Common`, `System` 및 리플렉션/설정 에셋의 태그 문자열만 `s_KnownTags`에 다시 등록할 뿐, `s_ChannelCache`는 채우지 않는다.
    4. 결과적으로 인게임 콘솔 Settings 탭(`HB_InGameConsoleUI.cs:1079`)이나 `HLog.Get("Common")`이 호출하는 `HLogCore.GetChannel("Common")`은 **새로운 `LogChannel` 인스턴스**를 생성해 캐시에 넣는다. 사용자가 콘솔 UI에서 채널 토글을 꺼도 `HLog.Common.Log(...)`가 참조하는 기존 `Channel` 인스턴스의 `Enabled` 값은 바뀌지 않아 채널 음소거가 작동하지 않는다. 또한 이전 플레이에서 `HLog.Common.Channel.Enabled = false`로 바꿨다면 다음 플레이에서도 복구되지 않는다.
    5. 코드에서 직접 생성해 정적 필드에 보관한 커스텀 `new LogChannel("Custom")`의 경우 `s_KnownTags`에서도 지워지고 재발견되지 않아 태그 목록에서 사라진다.
  - **심각도**: major
  - **확신도**: 높음 (사실 — 코드 경로로 직접 확인됨)
  - **검증 방법**: Enter Play Mode Options(Reload Domain 해제) 설정 후 플레이 진입 → 인게임 콘솔 Settings 탭에서 `Common` 채널 OFF → `HLog.Common.Log("test")` 호출 시 로그가 여전히 출력되는지 확인(첫 플레이에서도 `HLog.Common.Channel`과 `HLogCore.GetChannel("Common")`은 서로 다른 인스턴스임 — C-02 참조).

- **C-02**
  - **위치**: `LogChannelGeneratorWindow.cs:464` (`AppendChannelClass`), `LogChannel.cs:23-37`, `HB_InGameConsoleUI.cs:1079` (`RefreshChannelToggles`)
  - **문제**: 생성 코드의 정적 채널(`new LogChannel("tag")`)과 `HLogCore.GetChannel("tag")`가 서로 다른 `LogChannel` 인스턴스를 생성함 (도메인 리로드 여부와 무관하게 첫 실행부터 발생)
  - **발생 조건**: `HLog.Common.Log(...)` 등 정적 생성 채널을 사용하면서, 인게임 콘솔 Settings 탭(또는 `HLog.Get("Common")`)으로 채널 On/Off를 제어할 때
  - **영향**: `AppendChannelClass`가 생성하는 코드는 `public static readonly LogChannel Channel = new LogChannel("Common");`처럼 생성자를 직접 호출하며 `s_ChannelCache`에 등록하지 않는다. 반면 `HB_InGameConsoleUI.RefreshChannelToggles()`는 `HLogCore.GetChannel(tagName)`을 호출해 `s_ChannelCache`에서 별도의 `LogChannel` 인스턴스를 가져와 `Enabled`를 토글한다. 따라서 인게임 콘솔 Settings 탭의 채널별 On/Off 버튼이 정적 채널(`HLog.Common.*`, `HLog.UI.*` 등)에는 전혀 적용되지 않는다.
  - **심각도**: major
  - **확신도**: 높음 (사실 — 코드 경로로 직접 확인됨)
  - **검증 방법**: 플레이 모드에서 인게임 콘솔을 열고 Settings 탭에서 `Common: ON` 버튼을 눌러 `OFF`로 바꾼 뒤 `HLog.Common.Log("test")`를 호출해 로그가 차단되는지 확인.

- **C-03**
  - **위치**: `LogChannelGeneratorWindow.cs:465-469` (`AppendChannelClass`), `confirmed.md:16`
  - **문제**: 생성되는 정적 채널 클래스에 `LogError` 메서드가 누락됨
  - **발생 조건**: `HLog.Common.LogError(...)` 형태로 호출할 때
  - **영향**: `LogChannel` 클래스(`LogChannel.cs:93`)에는 `LogError`가 있으나, `LogChannelGeneratorWindow.AppendChannelClass`에서 생성하는 정적 래퍼 클래스에는 `Error`만 생성하고 `LogError` 포워딩 메서드를 생성하지 않는다. 확정 사항 1번("HLog 로그 메서드 전부(`Log`·`Warning`·`Error`·`Exception`·`LogException`·`LogError`, Watch 계열)를 ... `LogChannel`과 생성 템플릿 모두")과 불일치한다.
  - **심각도**: minor
  - **확신도**: 높음 (사실)
  - **검증 방법**: Generator로 `LogChannels.cs` 생성 후 `HLog.Common.LogError("msg")` 컴파일 여부 확인.

- **C-04**
  - **위치**: `HB_InGameConsoleUI.cs:192-238` (`OnEnable`), `HB_InGameConsoleUI.cs:434-440` (`Update`)
  - **문제**: `HB_InGameConsoleUI`의 `OnEnable`에서 `HLogCore.OnLogEnableChanged` 이벤트를 구독하지 않아, 외부(코드 또는 에디터 창)에서 `HLogCore.IsLogEnabled`를 변경할 때 인게임 콘솔 Settings 탭이 열려 있으면 버튼 표시(`Log: ON/OFF`)가 갱신되지 않음
  - **발생 조건**: 인게임 콘솔의 Settings 탭을 열어 둔 상태에서 코드(`HLog.IsLogEnabled = false`)나 에디터 콘솔 창 툴바로 마스터 로그 상태를 바꿀 때
  - **영향**: UI 표시 불일치 (탭을 다시 누르면 갱신됨).
  - **심각도**: minor
  - **확신도**: 높음 (사실)
  - **검증 방법**: 인게임 콘솔 Settings 탭을 연 상태에서 에디터 콘솔 창의 `Log: ON` 버튼을 클릭해 인게임 UI의 `toggleLogEnableBtn` 텍스트가 즉시 바뀌는지 확인.

2. 확정 사항 위반
- **확정 사항 1 (`confirmed.md:16`)**: "`Log`·`Warning`·`Error`·`Exception`·`LogException`·`LogError`, Watch 계열을 `[Conditional("USE_HLOG")]`로 — `LogChannel`과 생성 템플릿 모두."
  - **위반 위치**: `LogChannelGeneratorWindow.cs:465-469` (`AppendChannelClass`에서 `LogError` 미생성 — C-03).

3. 수정 방향
- **C-01 / C-02 수정**:
  1. `LogChannel` 생성자(`LogChannel.cs:23`) 또는 `HLogCore.GetChannel`에서 채널 인스턴스와 `s_ChannelCache`를 단일화한다.
     - 생성 템플릿(`LogChannelGeneratorWindow.cs:464`)에서 `new LogChannel(...)` 대신 `HLogCore.GetChannel("...")`을 호출하도록 바꾸고, `LogChannel` 생성자에서도 `HLogCore.RegisterChannel(this)`(내부에서 `s_ChannelCache.TryAdd(tag, this)` 및 `RegisterTag(tag)` 수행)를 호출해 직접 생성된 `LogChannel`도 캐시에 등록되게 한다.
  2. `HLogCore.ResetStatics()`(`HLogCore.cs:482`)에서 `s_ChannelCache.Clear()`를 호출하지 말고, 캐시에 남아 있는 기존 `LogChannel` 인스턴스들을 순회하며 `ch.Enabled = true; ch.FilterLevel = LogType.Log;`로 상태를 초기화하고 `s_KnownTags.Add(ch.Tag)`로 태그를 복원한다. 이렇게 하면 `[NoAutoStaticsCleanup]`으로 유지된 정적 `LogChannel` 참조와 `s_ChannelCache` 참조가 도메인 리로드 없이도 항상 동일한 인스턴스를 가리킨다.
- **C-03 수정**:
  - `LogChannelGeneratorWindow.cs:467` 아래에 `AppendForward(sb, "public static void LogError(object message, UnityEngine.Object context = null) => Channel.LogError(message, context);");`를 추가한다.
- **C-04 수정**:
  - `HB_InGameConsoleUI`의 `OnEnable`/`OnDisable`에서 `HLogCore.OnLogEnableChanged`에 `_ => UpdateLogEnableButtonUI()` 핸들러를 구독/해제한다.

4. 결론
- **수정 후 진행** — 정적 채널(`HLog.Common` 등)과 `HLogCore.GetChannel`의 인스턴스가 분리되어 인게임 콘솔의 채널별 On/Off 토글이 동작하지 않고 도메인 리로드 비활성화 시 채널 상태가 어긋나는 결함(C-01, C-02) 및 생성 템플릿의 `LogError` 누락(C-03)을 수정해야 한다.