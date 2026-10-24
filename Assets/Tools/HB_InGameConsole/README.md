# HB_InGameConsole & HLog

Unity 6.3 이상(6 / 7)을 지원하는 고성능 인게임 콘솔 및 에디터 로깅 / 디버깅 툴킷입니다.

---

## 📦 UPM 패키지 설치 (Installation)

Unity Package Manager에서 아래 Git URL을 입력하여 설치합니다:
```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole
```

설치하면 프로젝트에 `Assets/Scripts/LogChannels.cs`가 **한 번만** 자동 생성(Auto-Bootstrap)되어 바로 `HLog`를 호출할 수 있습니다.

- **자동 생성은 프로젝트당 1회**: 생성했다는 표시를 `ProjectSettings/HB_InGameConsole.LogChannels.generated.txt`에 남깁니다(Git으로 공유). 이 파일이 있으면 `LogChannels.cs`를 지워도 다시 만들지 않습니다. 다시 만들려면 Generator 창의 **스크립트 파일 갱신**을 누르거나 표시 파일을 지우세요.
  - 생성 위치는 Generator 설정 에셋이 있으면 그 출력 폴더 · 파일 이름, 없으면 `Assets/Scripts/LogChannels.cs`입니다.
  - 이름이 `LogChannels`인 스크립트가 이미 있거나 생성 위치에 파일이 있으면 만들지 않고 표시만 남깁니다.
- **콘솔 설정 (InitMode, AutoCreateUI 등)**: 패키지에는 설정 에셋이 없고, 에셋이 없으면 코드에 있는 기본값을 씁니다. `Tools > HB > In-Game Console > Settings` 메뉴를 실행하면 기본값으로 채운 `Assets/Resources/HB_InGameConsoleUserSettings.asset`이 생성되며, 이 에셋이 있으면 그 값을 씁니다. 에디터와 빌드 모두 이 경로의 에셋만 읽습니다(다른 위치에 있으면 에디터에서 경고).
- **asmdef를 쓰는 프로젝트**: 기본 생성 위치(`Assets/Scripts`)는 Assembly-CSharp에 포함되어 asmdef 코드에서 `HLog`를 참조할 수 없습니다. LogChannel Generator 설정에서 출력 폴더를 해당 asmdef 폴더 안으로 지정하세요.

### 콘솔 설정 항목 (`InGameConsoleSettings`)

| 항목 | 기본값 | 설명 |
|---|---|---|
| `Init Mode` | `AutoOnGameStart` | `AutoOnGameStart`면 첫 씬 로드 뒤(`AfterSceneLoad`) 콘솔을 자동 초기화합니다. `Manual`이면 `InGameConsole.Initialize()`를 직접 호출해야 콘솔이 만들어집니다. 로그 수집 자체는 설정과 무관하게 씬 로드 전에 시작됩니다. |
| `Dont Destroy On Load` | 켬 | 콘솔 GameObject(`[InGameConsole]`)를 씬 전환 때 유지합니다. |
| `Auto Create UI` | 켬 | 초기화할 때 콘솔 UI를 만듭니다. 끄면 UI 없이 로그 수집 · Watch 평가만 합니다. |
| `Show Fps Graph` | 켬 | 좌상단 FPS · 프레임타임 그래프 위젯의 처음 표시 여부입니다. |
| `Auto Scroll` | 켬 | 로그 목록 자동 스크롤의 처음 값입니다. |
| `Console Visual Tree` | 비움 | 콘솔 UXML을 바꿀 때 지정합니다. 비우면 패키지의 `Resources/HB_InGameConsole.uxml`을 씁니다. |
| `Panel Settings` | 비움 | 콘솔 패널 설정을 바꿀 때 지정합니다. 비우면 코드로 만든 기본값(ScaleWithScreenSize · 기준 해상도 1200×800)을 씁니다. 테마가 비어 있으면 패키지 기본 테마를 채웁니다. |

- 콘솔 UI는 Unity 6.5 이상에서 `PanelRenderer`, 그 아래에서는 `UIDocument`로 그리며 컴포넌트 정렬 순서(`sortingOrder`)는 9999입니다.

---

## ⚙️ 동작 조건: `USE_HLOG` 심볼

HLog와 인게임 콘솔은 Scripting Define Symbol **`USE_HLOG`가 있을 때만** 동작합니다. **에디터도 같은 규칙**입니다.

| `USE_HLOG` | 동작 |
|---|---|
| 있음 | 로그 수집, 인게임 콘솔, Watch가 동작. 개발 빌드가 아니어도(라이브와 같은 빌드) 동작합니다. |
| 없음 | `HLog` 로그·Watch 호출이 **호출과 인자 계산까지 컴파일에서 제거**됩니다(`[Conditional("USE_HLOG")]`). 콘솔은 만들어지지 않고, 코어 API는 아무것도 하지 않으며 빈 값을 돌려줍니다. 씬 · 프리팹에 놓인 `HB_InGameConsoleUI`도 바로 꺼집니다. 에디터에서 프로파일링할 때 HLog 비용을 없앨 수 있습니다. |

- **처음 설치하면 자동으로 켭니다**: 패키지를 처음 넣은 프로젝트에서 한 번만 Standalone · Android · iOS의 Scripting Define Symbols에 `USE_HLOG`를 추가하고, 표시 파일 `ProjectSettings/HB_InGameConsole.UseHLog.added.txt`를 남깁니다(Git으로 공유). 이 파일이 있으면 다시 추가하지 않으므로 일부러 끈 프로젝트에 다시 켜지지 않습니다. 배치 모드(CI)에서는 추가하지 않습니다.
  - 이 기능 전부터 쓰던 프로젝트(`LogChannels` 자동 생성 표시 파일이 이미 있음)에서는 심볼을 건드리지 않고 표시 파일만 남깁니다.
  - 처음 추가할 때 현재 플랫폼이 세 플랫폼 밖(WebGL 등)이면 켜지지 않았다고 콘솔에 알립니다. 툴바 버튼으로 켜세요.
- 켜고 끄기: 에디터 콘솔 창 툴바의 `USE_HLOG: ON / OFF` 버튼 (Build Settings에서 선택된 빌드 타깃의 Scripting Define Symbols를 바꿉니다).
- ⚠️ **출시 빌드 전에 `USE_HLOG`를 끄는 것은 빌드하는 사람의 몫입니다.** Scripting Define Symbol은 플랫폼 단위라 Development/Release를 구분하지 않습니다. 대신 **릴리즈 빌드(Development 빌드가 아님 또는 `LIVE` 심볼)에 `USE_HLOG`가 켜져 있으면 빌드 시작 때 경고**합니다(빌드는 막지 않음). HB_Build를 쓰면 DefineContainer의 LIVE 항목에서 `USE_HLOG`를 빼면 LIVE 빌드에서 꺼집니다.
  - 검사는 다른 빌드 단계가 심볼을 바꾼 뒤에 돌도록 늦게(`callbackOrder` 10000) 실행하고, 플랫폼 심볼과 활성 Build Profile의 심볼을 함께 봅니다. Dedicated Server 빌드는 Server 심볼을 봅니다.
- 대상은 `HLog`를 통한 호출뿐입니다. `Debug.Log`를 직접 호출한 로그는 제거되지 않으며, `USE_HLOG`가 켜진 빌드에서는 콘솔에 수집됩니다.
- 심볼 이름은 `USE_HLOG`로 고정입니다(바꿀 수 없음).
- **이전 버전으로 생성한 `LogChannels.cs`는 Generator 창에서 한 번 다시 생성하세요.** 이전 파일은 `UNITY_EDITOR`에서도 호출이 남는 예전 조건을 쓰고 있습니다.

---

## 🧵 스레드 규칙

| 어느 스레드에서나 | 메인 스레드 전용 |
|---|---|
| 채널 가져오기 `HLog.Get` · `HLog.Channel` · `HLogCore.GetChannel`<br>로그 출력 `Log` · `Warning` · `Error` · `LogError` · `Exception` · `LogException`<br>태그 등록 `HLogCore.RegisterTag` (이벤트는 메인 스레드에서 발생) | `Watch` 계열, `Unwatch`, `BreakWhen` · `ReportWhen`(`WatchCondition`), `ResetCondition`, `Clear`, `IsLogEnabled` 설정, `Initialize`, `Shutdown`, `PollWatchGetters`, `FlushPendingLogs`, `InGameConsole`의 UI API |

- 메인 스레드 전용 API를 다른 스레드에서 호출하면 **처리하지 않고**, API별로 처음 한 번만 에러 로그를 남깁니다.
- 다른 스레드에서 찍은 로그는 수신 시각을 그대로 기록하고, 메인 스레드에서 다음 프레임에 콘솔에 추가됩니다(프레임당 최대 1000개, 새 태그 이벤트는 프레임당 최대 64개). 그래서 메인 스레드 로그와 표시 순서가 약간 어긋날 수 있습니다.
- 처리되기 전에 쌓인 다른 스레드 로그도 최대 1000개까지만 두고, 넘치면 가장 오래된 것부터 버립니다.
- 채널 이름을 매번 다르게 만드는 사용(`HLog.Get("Tag_" + i)`)은 채널·태그가 계속 늘어납니다. 채널은 고정된 이름으로 쓰세요.

---

## 📌 주요 특징 (Key Features)

1. **클래스 기반 HLog 채널 아키텍처 (Namespace Conflict Free)**
   - `HLog`는 네임스페이스가 아닌 **전역 정적 partial 클래스(`public static partial class HLog`)**로 구현되어 있어, `using UnityEngine.UI;` 등 기존 엔진/프로젝트 네임스페이스와의 명칭 충돌이 전혀 발생하지 않습니다.
   - `HLog.UI.Log(...)`, `HLog.Common.Log(...)`, `HLog.System.Log(...)` 형태로 직관적이고 일관된 채널 접근을 제공합니다.
   - 생성 코드의 채널(`HLog.Common`)과 `HLog.Get("Common")`은 같은 인스턴스라, 콘솔 Settings 탭의 채널 On/Off가 양쪽에 똑같이 적용됩니다. 채널 이름은 대소문자를 구분하지 않습니다.
   - 채널마다 `Enabled`(On/Off)와 `FilterLevel`(Unity `Logger.filterLogType`)이 있습니다. `new LogChannel(tag, enabled, filterLevel)`로 직접 만든 채널도 같은 태그의 `HLog.Get`과 같은 인스턴스가 됩니다(먼저 만든 쪽이 남음).

2. **`USE_HLOG` Conditional 스트리핑**
   - 채널의 모든 로그·Watch·진단 메서드(`Log`, `Warning`, `Error`, `LogError`, `Exception`, `LogException`, `Watch`, `Unwatch`, `BreakIf`, `Assert`, `ReportIf`, `BreakWhen`, `ReportWhen`)에 `[Conditional("USE_HLOG")]`가 적용되어, 심볼이 없으면 IL 호출 자체가 제로 오버헤드로 스트리핑됩니다. 위 **동작 조건** 참고.

3. **에디터 & 인게임 실시간 LogEnable 제어**
   - **에디터 툴바**: `Log: ON/OFF` 런타임 토글 및 `USE_HLOG: ON/OFF` Scripting Define Symbol 원클릭 스위치 제공.
   - **인게임 콘솔 Settings 탭**: 콘솔 헤더의 `Settings` 탭을 통해 게임 플레이 도중에도 마스터 로그 On/Off 및 **채널별(Common, UI, Network 등) 개별 On/Off**를 손쉽게 조작할 수 있습니다.
   - 마스터 로그(`HLog.IsLogEnabled`)를 끄면 채널 로그는 출력되지 않고, `Debug.Log`로 직접 찍은 로그도 콘솔에 수집하지 않습니다(Unity 콘솔에는 그대로 나옴). 채널을 끄면 그 채널 로그만 출력되지 않습니다.

4. **사전 채널 감지 & 가로 스크롤 필터 바 (Editor & In-Game)**
   - 로그를 한 번도 찍지 않아도 프로젝트에 정의된 모든 채널(`Common` · `System`, `HLog`의 중첩 클래스, 에디터에서는 Generator 설정 에셋의 채널)이 필터 탭에 즉시 표시됩니다.
   - 채널이 많아져 창 너비를 초과하면 필터 바를 가로로 스크롤합니다. 에디터 창은 마우스 휠을 가로 스크롤로 바꿔 주고, 인게임 콘솔은 가로 ScrollView입니다.
   - **다중 채널 구독 (에디터 창)**: 보고 싶은 여러 개의 채널을 동시에 체크/구독하여 복합 필터링할 수 있습니다. 인게임 콘솔의 태그 바는 `All Tags` 또는 한 태그만 고릅니다.
   - 채널의 `Exception()` / `LogException()`으로 남긴 예외도 그 채널로 분류됩니다.
   - **태그 해석**: `Debug.Log`로 직접 찍은 로그도 메시지가 `#태그# 내용` 또는 `태그: 내용`(영문 · 숫자 · `_-./`, 32자 미만) 형태면 그 태그로 분류됩니다. 채널 로그도 Unity `Logger`가 `태그: 내용`으로 출력하므로 같은 규칙으로 분류됩니다. `.cs`나 `Exception`으로 끝나는 이름은 태그로 보지 않고, 예외 로그는 콜론 태그를 해석하지 않습니다.

5. **실시간 람다 Watch (Lambda Getter Monitoring)**
   - `HLog.Common.Watch("Player/ImageSlot", () => m_Image);` 형태로 **람다(게터)**를 등록할 수 있습니다.
   - `Start()`에서 1회만 등록해 두면 매 프레임 값을 평가해 바뀌면 즉시 반영합니다. 인게임 콘솔 UI가 없어도(`AutoCreateUI` 꺼짐) 평가됩니다.
   - 같은 키에 값 Watch(`Watch(key, value)`)를 호출하면 그 키의 게터는 해제되고 값으로 바뀝니다(나중 호출이 이김).
   - 채널을 통한 Watch 키는 `채널태그/키`입니다(예: `HLog.Common.Watch("Player/Hp", …)` → `Common/Player/Hp`). 키는 대소문자를 구분하지 않습니다.
   - 게터 Watch는 값 형식 · 문자열이 이전 값과 같으면 문자열 변환을 건너뜁니다. `UnityEngine.Object`는 `이름 (타입)`으로, 파괴된 오브젝트는 `null`로 표시합니다. 게터가 예외를 던지면 값이 `<Error: 메시지>`로 표시됩니다.

6. **조건부 에디터 Pause & 스냅샷 리포트 (Condition Triggers & Reports)**
   - **조건 트리거 (Condition Triggers)**: 특정 Watch 변수가 조건(예: `IsNotNull`, `== 0`, `> 100`)을 만족하면 에러 로그를 남기고, `Pause`를 켠 트리거는 에디터를 즉시 일시정지(`Debug.Break()`)시킵니다. 트리거는 HLog Console 창이 열려 있는 동안 Watch 값이 바뀔 때 검사합니다.
   - **스냅샷 리포트 (`ReportIf` 및 트리거 자동 덤프)**: 문제 발생 시 전체 Watch 변수 목록과 콜스택, 프레임 정보를 프로젝트 루트의 `Reports/` 폴더에 텍스트 파일로 자동 덤프합니다. (`BreakIf` · `Assert` · `ReportIf`는 에디터에서만 동작)
     - `ReportIf` · `ReportWhen`: `Report_{채널}_{시각}_{사유}.txt` — 추가 변수, 전체 Watch, 콜스택.
     - 조건 트리거: `WatchReport_{키}_{시각}.txt` — 전체 Watch(발동한 키 표시), 최근 로그 20줄.

7. **Unity 6 / 7 수명 주기 관리 호환 (Lifecycle Safe)**
   - 채널 클래스와 `HLogCore`에 `[NoAutoStaticsCleanup]`을 적용하여(Unity 6.5 이상 또는 분석 기능이 켜진 경우), 플레이 모드 진입 때 정적 필드를 자동으로 되돌리는 기능(AutoStaticsCleanup)이 채널 인스턴스를 `null`로 만드는 문제를 막았습니다.
   - 플레이 모드에 들어갈 때마다 기록·Watch·로그 On 상태와 채널별 On/Off · FilterLevel이 처음 값으로 초기화되어, 도메인 리로드를 꺼도 같은 상태로 시작합니다. 플레이 모드를 나갈 때는 게터 · 조건 등록을 해제합니다.
   - 에디터에서 수집을 시작할 때 Log · Warning의 스택트레이스 설정이 `None`이면 `ScriptOnly`로 바꿉니다(상세 패널에 스택을 보이기 위해).

---

## 🚀 빠른 시작 (Quick Start)

### 1. 기본 로그 출력 (Basic Logging)
```csharp
// 1) 정적 채널 (코드 자동 생성)
HLog.Common.Log("[Player] 플레이어가 스폰되었습니다.");
HLog.Common.Warning("[Player] 주의: 체력이 20% 이하입니다.");
HLog.Common.Error("[Network] 네트워크 연결 실패!");
HLog.Common.Exception(exception);
HLog.UI.Log("[UI] 인벤토리 창 열림");

// 2) 동적 채널 (코드 생성 없이 즉시 사용)
HLog.Get("CustomChannel").Log("동적 채널 메시지");
HLog.Channel("Quest").Warning("퀘스트 진행 지연");
```

### 2. 실시간 변수 감시 (Watch) — 메인 스레드에서
```csharp
private void Start()
{
    // 람다 등록 (권장): 값이 바뀔 때마다 자동 반영. owner(this)를 넘기면 파괴될 때 자동 해제
    HLog.Common.Watch("Player/ImageSlot", () => m_Image, this);
    HLog.Common.Watch("Player/Health", () => player.CurrentHp, this);
}

private void Update()
{
    // 값 직접 전달 (매 프레임 갱신 시)
    HLog.Common.Watch("Player/Position", transform.position);
    HLog.Common.Watch("Player/MoveSpeed", moveSpeed);
}
```
- owner 없이 등록한 게터는 `Unwatch(key)`로 직접 해제해야 합니다.
- 채널 없이 `HLog.Watch(key, …)` · `HLog.Unwatch(key)`도 쓸 수 있습니다(키에 채널 태그가 붙지 않음).

### 3. 조건부 일시정지 및 리포트 덤프 (에디터)
```csharp
// 1) BreakIf: 특정 조건 시 에디터 즉시 Pause
HLog.Common.BreakIf(moveSpeed > 100f, $"비정상 이동 속도 감지: {moveSpeed}");

// 2) ReportIf: 조건 만족 시 현재 모든 Watch 변수를 텍스트 파일로 덤프 후 에디터 Pause
HLog.Common.ReportIf(currentHp <= 0 && isInvincible, "무적 상태 사망 버그 발생",
    ("PlayerState", currentState),
    ("LastAttacker", attackerName),
    ("FrameCount", Time.frameCount)
);
```
- `Assert(condition, message)`: 조건이 거짓이면 Assert 로그(`[Assertion Failed]`)를 남기고 에디터를 일시정지합니다.

### 4. 등록형 조건 감시 (BreakWhen / ReportWhen) — 메인 스레드에서
```csharp
private void Start()
{
    // 한 번 등록하면 매 프레임 조건을 확인하다가, 거짓 → 참으로 바뀌는 순간 발동합니다
    HLog.Common.BreakWhen("HpNegative", () => hp < 0 && !isDead, "체력 이상", this);
    HLog.Common.ReportWhen("StuckAttack", () => state == State.Attack && attackTimer > 5f, "공격 상태 고착", this, once: false);
}
// 해제: HLog.Common.Unwatch("HpNegative") (owner가 파괴되면 자동 해제)
```
- **발동**: `BreakWhen`은 채널 에러 로그 + 에디터 일시정지, `ReportWhen`은 리포트 파일(`Reports/`)까지 남깁니다. 빌드(USE_HLOG 켬)에서는 에러 로그만 남습니다(인게임 콘솔에 표시).
- **once** (기본 true): 한 번 발동하면 멈춥니다. false면 다시 거짓이 됐다가 참이 될 때마다 발동합니다. 계속 참인 동안 매 프레임 발동하지는 않습니다.
- **Watch 목록에 표시**: `Common/HpNegative   [Break] False`처럼 보이고, 발동하면 `[Break] True · Hit 1회 (마지막 03:41:12) · 완료`로 바뀝니다. 에디터 콘솔 창 Watch Values에서는 빨간색으로 강조되고 **Reset** 버튼으로 다시 대기 상태로 돌릴 수 있습니다(`HLogCore.ResetCondition(key)`).
- 조건을 평가하다 예외가 나면 에러 로그를 한 번 남기고 해제합니다.
- 조건은 플레이 중에만 평가합니다. 지역 변수를 캡처하는 람다는 등록할 때 한 번만 만들어지므로 매 프레임 할당은 없습니다.
- 간단한 값 비교(`hp < 0` 하나)라면 코드 없이 Watch + 에디터 창의 **Condition Triggers**로도 할 수 있습니다(트리거는 PC별 저장).

### 5. 수집 제어 (메인 스레드에서)
- `HLog.Core.Shutdown()`: 로그 수집 · Watch 평가 · 대기 로그 처리를 모두 멈추고, 아직 처리되지 않은 다른 스레드 로그는 버립니다. 기록과 Watch 값은 남습니다. 다시 시작하려면 `HLog.Core.Initialize()`.
- `HLog.Clear()`: 기록을 비웁니다. 아직 처리되지 않은 다른 스레드 로그도 버립니다. 태그와 Watch는 남습니다.
- 로그 수집은 첫 씬 로드 전(`BeforeSceneLoad`)에 자동으로 시작합니다. 에디터의 플레이 모드 밖에서는 HLog Console 창이 열릴 때 시작합니다.
- 기록은 최근 1000개까지만 두고, 넘치면 가장 오래된 것부터 지웁니다.

### 6. 공개 API 요약

**`HLog` (생성 코드 `LogChannels.cs`)**

| API | 설명 |
|---|---|
| `HLog.{채널}.Log/Warning/Error/LogError/Exception/LogException` | 채널 로그. `Error`와 `LogError`는 같습니다. |
| `HLog.{채널}.Watch/Unwatch/BreakIf/Assert/ReportIf/BreakWhen/ReportWhen` | 채널 Watch · 진단 (위 예제). |
| `HLog.{채널}.Channel` | 그 채널의 `LogChannel` 인스턴스 (`Enabled` · `FilterLevel` 조작). |
| `HLog.Get(name)` · `HLog.Channel(name)` | 이름으로 채널 가져오기 (없으면 만듦, 빈 이름은 `Common`). |
| `HLog.Watch` · `HLog.Unwatch` · `HLog.Clear()` · `HLog.IsLogEnabled` | 채널 없는 Watch, 기록 비우기, 마스터 로그 On/Off. |
| `HLog.Core.Initialize/Shutdown/Clear/Watch/Unwatch` | 수집 시작 · 정지와 코어 Watch. |

**`HLogCore` (패키지 코어)**

| API | 설명 |
|---|---|
| `IsLogEnabled` · `IsInitialized` | 마스터 로그 On/Off, 수집 중인지. |
| `History` · `HistoryCount` | 기록 복사본(오래된 순), 개수만(복사 없음). |
| `GetLogTypeCounts(out info, out warning, out error)` | 종류별 개수. error에는 Error · Assert · Exception이 들어갑니다. |
| `HasUntaggedLogs` · `KnownTags` | 태그 없는 로그가 있는지, 알려진 태그 목록(복사본). |
| `Watches` · `WatchCount` | Watch 항목 복사본(바꿔도 내부는 그대로), 개수만. |
| `Initialize` · `Shutdown` · `Clear` | 수집 시작 · 정지, 기록 비우기. |
| `GetChannel(tag)` · `RegisterTag(tag)` · `DiscoverAllChannels()` | 채널 가져오기, 태그 등록, 프로젝트의 채널을 찾아 태그로 미리 등록. |
| `Watch` · `Unwatch` · `WatchCondition` · `ResetCondition` | Watch 등록 · 해제, 등록형 조건(`WatchConditionKind.Break/Report`), 발동한 조건 다시 대기. |
| `PollWatchGetters()` · `FlushPendingLogs()` | 게터 평가(프레임당 1회), 다른 스레드 대기 로그 처리. 보통 자동으로 불립니다. |
| `FilterStackTrace(stack)` | 스택에서 `UnityEngine.Debug` · `Logger` · HLog 내부 줄을 뺍니다. |
| `OnLogAdded` · `OnLogsCleared` · `OnWatchUpdated` · `OnWatchRemoved` · `OnTagDiscovered` · `OnLogEnableChanged` | 이벤트 (메인 스레드에서 발생). 구독자가 예외를 던져도 다른 구독자는 계속 불립니다. |

**`InGameConsole` (인게임 UI)**

| API | 설명 |
|---|---|
| `Initialize()` · `IsInitialized` | 콘솔 초기화(수집 시작 + `AutoCreateUI`면 UI 생성). `InitMode`가 `Manual`일 때 직접 부릅니다. |
| `EnsureUIInstance()` · `Instance` | UI가 없으면 만들고 돌려줌, 현재 UI(`HB_InGameConsoleUI`). |
| `Open()` · `Close()` · `Toggle()` · `ClearLogs()` | 콘솔 열기 · 닫기 · 전환, 기록 비우기. |
| `AddExternalView(name, element)` · `RemoveExternalView` · `ShowExternalView` · `IsExternalViewActive` | 상단 탭에 외부 뷰 붙이기 · 떼기 · 열기 · 확인 (예: HB_Cheat의 `Cheat` 탭). UI가 없으면 `AddExternalView`는 false. |

- `HB_InGameConsoleUI`에는 `AutoScroll`, `ScrollToBottom()`, `SetFilter(int)`(0 All · 1 Info · 2 Warning · 3 Error), `IsConsoleOpen`, `RootElement`도 있습니다.

---

## 🖥️ 에디터 창 사용법 (Editor Window)

상단 메뉴: **Tools > HB > In-Game Console > Console Window** (또는 **Window > General > HLog Console**)

1. **상단 툴바**:
   - `Clear`: 로그 히스토리 비우기.
   - `Logs` · `Watch [개수]` 탭: 조건 트리거가 있으면 `Watch [개수 / Triggers: 개수]`로 표시합니다.
   - `Auto Scroll`: 맨 아래를 보고 있을 때 새 로그를 따라 내려갑니다. 위로 스크롤하면 따라가기를 멈추고, 다시 맨 아래로 내리면 이어서 따라갑니다. 끄면 새 로그가 와도 스크롤 위치가 바뀌지 않습니다.
   - `Pause on Error`: Error · Exception 로그가 들어오면 자동 일시정지.
   - `Log: ON / OFF`: 에디터 및 런타임 마스터 로깅 On/Off 즉시 제어.
   - `USE_HLOG: ON / OFF`: Scripting Define Symbol 원클릭 추가/제거 스위치. 꺼져 있으면 창에 안내가 표시됩니다.
   - `Info: n` · `Warn: n` · `Error: n`: 종류별 개수를 보이고, 누르면 그 종류를 목록에서 숨기거나 보입니다.
2. **Logs 탭**:
   - 검색창: 메시지에 검색어가 들어 있는 줄만 보입니다(대소문자 무시). `!단어`로 쓰면 그 단어가 들어 있는 줄을 뺍니다.
   - 상단 채널 바: 다중 채널 구독/해제 (`✓ Common`, `✓ UI` 등), 마우스 휠 가로 스크롤. `All`은 구독을 모두 풀어 전체를 보입니다. 태그 없는 로그가 있으면 `[Untagged]` 버튼이 생깁니다.
   - 우클릭 메뉴: `Only '[채널명]'` (단독 표시), `Subscribe All` (전체 구독), `Toggle Mute` (이 창에서만 그 채널을 숨김, `[Off]`로 표시. 채널 로그 자체는 끄지 않음).
   - 줄 더블클릭: 메시지의 파일 위치(컴파일 에러 형식 등) 또는 스택 첫 위치를 IDE로 엽니다.
   - 로그 상세 창: 스택트레이스 줄의 `Open` 버튼이나 더블클릭으로 소스 코드로 점프. 메시지에 파일 위치가 있으면 `Open (파일:줄)` 버튼을 보입니다. `Copy All`은 메시지와 스택 전체를 복사합니다. 목록과 상세 창 사이 경계를 끌어 높이를 바꿉니다.
   - **여러 줄 선택 · 복사**: 클릭(하나) · `Ctrl`(맥 `Cmd`)+클릭(추가/빼기) · `Shift`+클릭(범위) · `Ctrl/Cmd+A`(보이는 줄 전체). `Ctrl/Cmd+C`로 선택한 줄을 화면 순서대로 `HH:mm:ss [Type] [Tag] 메시지` 한 줄씩 복사합니다. 줄을 우클릭하면 `선택 복사` · `선택 복사 - 스택 포함` · `선택 해제`. 필터로 가려진 줄은 선택에서 빠집니다. 검색창 · 상세 창 텍스트에 포커스가 있으면 그쪽 복사가 먼저입니다.
3. **Watch 탭**:
   - **Watch Values**: Watch 키 · 현재 값 · 바뀐 시각 목록. `null` 값과 발동한 조건(`BreakWhen` · `ReportWhen`)은 빨간색으로 강조합니다. 일반 Watch 줄의 `+ Trigger` 버튼으로 그 키의 감시 조건을 바로 등록하고, 조건 줄에는 `Reset` 버튼이 있습니다.
   - **Condition Triggers**: 변수 조건식(`>`, `<`, `==`, `!=`, `Contains`, `IsNull`, `IsNotNull`), `Pause`(자동 일시정지), `Report`(스냅샷 덤프), `Once`(단일 발동) 설정.
     - 숫자로 읽히면 숫자로, 아니면 문자열(대소문자 무시)로 비교합니다. 값이 비었거나 `null` · `none`이면 null로 봅니다.
     - 트리거 키는 Watch 키와 같거나 `/`로 끝부분이 맞으면 적용됩니다(예: `Hp` → `Common/Player/Hp`).
     - `+ Add Trigger`(추가) · `Reset Hits`(발동 상태 모두 초기화) · `Open Reports Folder`(`Reports/` 열기) 버튼, 줄마다 `Hit (Reset)` · `X`(삭제).
     - 트리거 목록은 EditorPrefs에 저장되어 PC별로 남습니다.
   - 플레이 모드 진입 시 `Once` 발동 상태가 자동 초기화되어 매번 안정적으로 테스트 가능.

---

## 📱 인게임 콘솔 UI (In-Game Console UI)

런타임 환경에서 우상단 `Logs [개수]` 플로팅 버튼을 터치/클릭하여 열 수 있습니다.

- **헤더**: `Logs` · `Watch [개수]` · `Settings` 탭(외부 뷰가 있으면 그 뒤에 탭 추가), 로그 레벨 필터, `▼ Bottom`(맨 아래로 이동하고 다시 따라가기), `Clear`, `✕`(닫기). 제목 옆에 `[FPS/목표 FPS | ms]`를 표시합니다.
- **`Logs` 탭**: 게임 런타임 로그 목록, 로그 레벨 필터(All/Info/Warn/Error), 검색창, 가로 스크롤 채널 필터 바.
  - 검색창: 메시지 검색(대소문자 무시). `!단어`는 그 단어가 들어 있는 줄을 뺍니다.
  - 태그 바: `All Tags` 또는 `#태그#` 버튼 하나를 골라 그 태그 로그만 봅니다.
  - 줄을 누르면 하단 상세 패널에 종류 · 태그 · 시각 · 메시지 · 정리한 스택을 보입니다. `Copy`(클립보드 복사) · `Expand`/`Normal`(패널 높이) · `Hide`/`Show`(패널 접기) 버튼이 있습니다.
- **`Watch` 탭**: 게임 내 실시간 등록된 Watch 변수 목록 및 실시간 값 모니터링(키 · 값 · 바뀐 시각).
- **`Settings` 탭 (분리된 전용 설정 뷰)**:
  - **Master Log Enable** (`Log: ON/OFF`): 런타임 전체 로깅 On/Off 토글 버튼.
  - **Show FPS Widget** (`FPS: ON/OFF`): 좌상단 FPS 및 프레임타임 그래프 위젯 표시 On/Off.
  - **Auto Scroll** (`Scroll: ON/OFF`): 맨 아래를 보고 있을 때 새 로그를 따라 내려갑니다. 위로 스크롤하면 멈추고, 다시 맨 아래로 내리거나 `▼ Bottom`을 누르면 이어서 따라갑니다. 기본값은 `InGameConsoleSettings`의 `Auto Scroll`, 코드에서는 `InGameConsole.Instance.AutoScroll`로 바꿀 수 있습니다.
  - **Channel Log Enable (Per Channel)**: 프로젝트의 모든 채널(`Common`, `UI`, `System` 등)을 칩(Chip) 버튼으로 나열하여 **각 채널별로 로그 On/Off**를 독립 제어.
  - **Clear All Log History**: 런타임 로그 히스토리 전체 비우기.
- **FPS 위젯**: 0.12초마다 평균 FPS · ms를 갱신하고, 최근 80프레임의 프레임타임 그래프와 목표 · 경고선을 그립니다. 목표 FPS는 `Application.targetFrameRate`, 없으면 화면 주사율, 그것도 없으면 60입니다. 목표의 90% 이상은 초록, 70% 이상은 노랑, 그 아래는 빨강입니다. 위젯을 누르면 그래프를 접고 펼칩니다.
- **메모리와 비용**: 콘솔을 처음 열기 전에는 로그 행을 만들지 않고 개수만 갱신합니다. 처음 열 때 최근 로그(최대 1000줄)를 한 번에 만들므로 그 순간 비용이 있습니다. 한 번 만든 행은 닫아도 유지되어(자주 여닫는 경우를 위해), 다시 열 때는 닫혀 있던 동안의 새 로그만 덧붙입니다.
- 콘솔 UI는 매 프레임 대기 로그 처리와 Watch 평가도 부릅니다. 다른 코드가 PlayerLoop를 바꿔 HLog 처리 시스템이 빠져도 콘솔이 있는 동안은 처리됩니다.

---

## 🛠️ 채널 생성기 (Channel Generator)

상단 메뉴: **Tools > HB > In-Game Console > Log Channel Generator**

1. **설정 관리**:
   - **설정 에셋**: 채널 목록과 생성 설정은 `LogChannelGeneratorSettings` 에셋에 저장합니다. 없으면 창의 **설정 에셋 생성** 버튼으로 `Assets/LogChannelGeneratorSettings.asset`을 만들거나, `New`로 위치를 골라 만듭니다. `Find`는 프로젝트에서 에셋을 찾습니다.
   - **Conditional 심볼**: `USE_HLOG` 고정 (표시만 됨).
   - **생성 경로 & 파일 이름**: 기본 `Assets/Scripts/LogChannels.cs`.
   - **변수명과 태그명을 동일하게 사용** (기본 켬): 끄면 채널마다 클래스 이름과 다른 태그 이름을 줄 수 있습니다.
   - **에디터 변경 시 자동 코드 갱신** (기본 켬): 켜면 채널 추가 · 삭제 · 전체 비우기 때와 창을 닫을 때 C# 코드를 자동 갱신합니다. 이름만 고친 경우는 창을 닫거나 **스크립트 파일 갱신**을 누를 때 반영됩니다.
2. **채널 추가**:
   - **+ 채널 추가**를 누르면 `Channel{번호}` 채널이 추가됩니다. 이름 칸에 원하는 채널 이름(예: `Battle`, `Inventory`, `Network`)을 입력합니다. `X`로 하나 지우고 `전체 비우기`로 모두 지웁니다.
   - 클래스 이름은 글자 · 숫자 · `_`만 남기고, 숫자로 시작하면 앞에 `_`를 붙입니다. `Common` · `System`은 항상 생성되고, `Common` · `System` · `Core` 이름은 목록에 있어도 건너뜁니다.
   - 갱신 후 즉시 `HLog.Battle.Log(...)` 형태로 사용 가능합니다.
3. **미리보기 · 저장**:
   - 생성될 코드를 창에서 미리 봅니다. `클립보드 복사`로 코드만 복사하고, `스크립트 파일 갱신 (Save File)`으로 파일에 씁니다. 내용이 같으면 파일을 다시 쓰지 않습니다(불필요한 재컴파일 방지).
