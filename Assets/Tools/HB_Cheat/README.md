# HB_Cheat

개발·QA용 치트 패널입니다. 모바일 우선이며 PC도 지원합니다. 치트는 어트리뷰트로 정의하고, 서버 치트 문자열을 조합해 전송할 수 있습니다. 릴리즈 빌드에서는 엄격하게 제외되며, Unity CLI 자동화를 지원합니다. (Unity 6.3+, 6 / 7)

---

## 📦 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat
```

- 버전 고정: URL 끝에 `#<태그>` 또는 `#<전체 40자 커밋 해시>`를 붙입니다. 짧은 해시(예: `#869e2614`)는 UPM이 받지 않습니다.

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat#<40자 커밋 해시>
```

UPM 패키지는 읽기 전용입니다. 그래서 **치트 정의, 설정, 생성 코드는 모두 프로젝트 `Assets` 아래**에 둡니다.

필수 의존 패키지는 없습니다. 아래 패키지가 있으면 asmdef `versionDefines`로 해당 기능이 켜집니다.

| 패키지 | 켜지는 심볼 | 켜지는 기능 |
|---|---|---|
| `com.unity.inputsystem` | `HB_CHEAT_INPUT_SYSTEM` | 열기 키를 Input System `Key`로 읽습니다. 없으면 구 Input Manager `KeyCode`로 읽습니다. 패키지가 없어도 `HB.Cheat` asmdef의 `Unity.InputSystem` 참조는 Unity가 무시하므로 컴파일 에러가 나지 않습니다. |
| `com.unity.pipeline` | `HB_CHEAT_PIPELINE` | Unity CLI 명령 어셈블리(`HB.Cheat.Editor.Cli`)가 컴파일됩니다. |
| `com.hb.ingameconsole` | `HB_CHEAT_HAS_CONSOLE` | InGameConsole 연동 어셈블리(`HB.Cheat.InGameConsole`)가 컴파일됩니다. |

---

## 🚀 시작하기

1. `Tools > HB > Cheat`를 엽니다. 탭은 **Settings** · **Cheats** 두 개입니다.
2. **Settings** 탭의 **Cheat Defines**에서 플랫폼별 `Turn On`을 누릅니다(Standalone · Android · iOS, 현재 활성 플랫폼이 그 밖이면 — WebGL · Dedicated Server 등 — 그 플랫폼 줄도 보입니다). `HB_CHEAT`(패키지)와 `DEV_CHEAT`(프로젝트, 이름 변경 가능 — 영문자 · 숫자 · `_`만, 숫자로 시작 불가. 규칙에 안 맞으면 경고 후 `DEV_CHEAT`)가 함께 켜집니다. 상태는 `ON` / `OFF` / `MISMATCH`(한쪽만 켜짐)로 보입니다.
3. 설정 에셋이 없으면 기본값으로 동작합니다. `Create Settings Asset`을 누르면 `Assets/Editor/HBCheat/HBCheatSettings.asset`을 만듭니다(Editor 폴더라 빌드에 들어가지 않음).
4. `Class Name`을 적고 `Create Cheat File`로 치트 파일을 만듭니다. 생성 위치는 `Cheat Folder`(기본 `Assets/Cheats`)입니다.
5. 컴파일이 끝나면 레지스트리(`HBCheatRegistry.g.cs`)가 자동으로 생성됩니다.
6. Play를 누르면 열 수 있습니다. PC는 `F8`, 모바일은 화면의 `CHEAT` 버튼(끌어서 이동 가능)을 씁니다. InGameConsole이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다.

Settings의 Runtime 값(Init Mode, Prefer Console Tab, Show Floating Button, 열기 키, 서버 대기 시간, 히스토리 용량, Dont Destroy On Load)과 `Allow Raw From Automation`은 생성 레지스트리 코드에 들어갑니다. 설정 창 · Inspector · Undo · VCS 풀 어느 경로로든 이 값이 바뀌면 레지스트리를 바로 다시 생성하고(Play 중이면 에디트 모드로 돌아온 뒤), 글자 칸은 Enter나 다른 칸으로 옮길 때 적용됩니다. `Auto Generate`를 끄면 `Generate Registry`를 눌러야 반영됩니다. 열기 키는 키 이름 하나만 받습니다(`F8`, `BackQuote` 등, 대소문자 무시 — 숫자 · `None` · 쉼표로 이은 이름은 경고 후 사용 안 함). Input System 키보드에 없는 키면 Play 중에 경고 후 끕니다. 키를 읽을 입력 시스템이 없으면(Active Input Handling이 Input System인데 패키지가 없는 등) 키 이름과 따로 그 사실을 경고합니다.

프로젝트 심볼 이름(`User Define`)을 바꾸면 옛 이름이 있는 모든 플랫폼(Standalone · Android · iOS · Dedicated Server · 예전에 켜 둔 WebGL 등)의 PlayerSettings 심볼도 옛 이름에서 새 이름으로 바꿉니다. 치트 파일의 `#if`와 치트 asmdef의 `defineConstraints`는 직접 고쳐야 합니다(Console에 경고).

```csharp
#if DEV_CHEAT   // Assembly-CSharp에 둘 때 (asmdef 방식이면 필요 없음)
using System.Collections.Generic;
using HB.Cheat;

public static class ItemCheats
{
    // Local: 클라에서 직접 실행. 반환형 void / bool / string(메시지) / CheatResult
    [Cheat("actor.level", "Actor/Level")]
    public static void SetLevel([CheatRange(1, 100)] int level) => Player.Local.SetLevel(level);

    // Server: 반환한 문자열을 서버로 보낸다 (패널 미리보기가 값이 바뀔 때마다 부르므로 문자열만 만들 것)
    [Cheat("item.enchant", "Item/Enchant", Target = CheatTarget.Server)]
    public static string Enchant(
        [CheatArg("아이템"), CheatOptions(typeof(ItemTableProvider))] int itemId,
        [CheatArg("강화"), CheatOptions(typeof(EnchantLevelProvider), DependsOn = new[] { "itemId" })] int level)
        => CheatCommand.Join("item_enchant", itemId, level);   // "item_enchant 1001 15"
}

public sealed class ItemTableProvider : ICheatOptionProvider
{
    // 치트를 고를 때 · 실행할 때 다시 불린다 (테이블을 다시 로드해도 따로 할 일 없음)
    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        foreach (var item in ItemTable.All)
            results.Add(new CheatOption(item.Id, item.Name));
    }
}

public sealed class EnchantLevelProvider : ICheatOptionProvider
{
    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        if (!context.TryGet("itemId", out int itemId)) return;   // 앞 인자를 아직 안 골랐으면 빈 목록
        var item = ItemTable.Get(itemId);
        if (item == null) return;
        for (int i = 1; i <= item.MaxEnchant; i++) results.Add(new CheatOption(i, $"+{i}"));
    }
}
#endif
```

### 설정 항목 (Settings 탭)

| 묶음 | 항목 | 기본값 | 내용 |
|---|---|---|---|
| Define | `User Define` | `DEV_CHEAT` | 프로젝트 치트 코드용 심볼. `HB_CHEAT`와 항상 함께 켜고 끕니다. |
| Code | `Cheat Folder` | `Assets/Cheats` | `Create Cheat File` · `Create Cheat asmdef`가 파일을 만드는 폴더입니다. |
| Code | `Assembly C Sharp Output Folder` | `Assets/Cheats/Generated` | Assembly-CSharp에 있는 치트의 레지스트리 생성 폴더입니다. asmdef에 있는 치트는 그 asmdef 폴더의 `Generated`에 만듭니다. |
| Code | `Cheat Asmdef Name` | `Game.Cheats` | `Create Cheat asmdef`가 만드는 asmdef 이름입니다. |
| Code | `Cheat Asmdef References` | (비어 있음) | 치트 asmdef가 `HB.Cheat`에 더해 참조할 게임 asmdef 이름입니다. |
| Code | `Auto Generate` | 켬 | 컴파일 후 · 설정 변경 후 레지스트리를 자동으로 다시 생성합니다. |
| Runtime | `Init Mode` | `AutoOnGameStart` | `AutoOnGameStart`면 첫 씬 로드 뒤(`AfterSceneLoad`) 자동 초기화합니다. `Manual`이면 `CheatSystem.Initialize()`를 직접 부릅니다. |
| Runtime | `Prefer Console Tab` | 켬 | InGameConsole이 있으면 콘솔 탭으로 붙습니다. |
| Runtime | `Show Floating Button` | 켬 | 독립 패널일 때 화면에 `CHEAT` 버튼을 띄웁니다. |
| Runtime | `Toggle Key` | `F8` | PC 열기/닫기 키 이름. 비우면 사용하지 않습니다. |
| Runtime | `Server Timeout Seconds` | `10` | 서버 응답 대기 시간(초). 최소 0.5, NaN · 무한대면 10입니다. |
| Runtime | `History Capacity` | `50` | 히스토리 보존 건수. 최소 1입니다. |
| Runtime | `Dont Destroy On Load` | 켬 | 끄면 씬이 바뀔 때 패널과 함께 치트 시스템이 종료됩니다. 다시 쓰려면 `CheatSystem.Initialize()`를 부릅니다. |
| Automation | `Allow Raw From Automation` | 끔 | CLI `cheat_raw` · 브리지 `raw` 허용 (아래 raw 참고). |
| Automation | `Run In Background During Cli Play` | 켬 | `cheat_play`로 시작한 Play 동안만 runInBackground를 켭니다 (아래 cheat_play 참고). |
| Release Guard | `Treat Non Development As Release` | 켬 | Development Build가 아니면 릴리즈로 봅니다. |
| Release Guard | `Release Defines` | `LIVE` | 이 심볼 중 하나라도 켜져 있으면 릴리즈로 봅니다. |
| Release Guard | `Fail When Unverifiable` | 켬 | 릴리즈 빌드에서 검사할 스크립트 DLL이 하나도 없으면 빌드를 실패시킵니다. |

- Runtime 묶음은 생성 레지스트리가 `RuntimeInitializeLoadType.SubsystemRegistration`에서 `CheatSystem.Options`에 넣습니다. 코드로 바꾸려면 그 뒤, `Initialize` 전(예: `BeforeSceneLoad`)에 `CheatSystem.Options`를 고칩니다. 독립 패널의 `PanelSettings`는 코드로만 넣습니다.
- Automation의 `Run In Background During Cli Play`와 Release Guard 묶음은 에디터가 설정 에셋을 직접 읽습니다(레지스트리에 들어가지 않음).
- 설정 창은 창이 포커스를 잃거나 닫힐 때 설정 에셋을 저장합니다.

### 치트 파일 · asmdef 만들기

- `Create Cheat File`: `<Cheat Folder>/<Class Name>.cs`를 만듭니다. Local 예제(`<분류>.log`), Server 예제(`<분류>.add`), 예제 Provider(`<Class Name>SampleProvider`)가 들어갑니다. 분류는 클래스 이름에서 `Cheats`를 뗀 값입니다(`ItemCheats` → `Item/Log`, id `item.log`).
  - `Class Name`은 C# 식별자여야 합니다(글자 · 숫자 · `_`, 숫자로 시작 불가, 키워드 불가). 생성 코드가 쓰는 이름(`System` · `UnityEngine` · `HB` · `Debug` · `List` · `Cheat*` 타입)도 그것을 가려 컴파일되지 않으므로 쓸 수 없습니다. 아니면 만들지 않고 알립니다.
  - 같은 파일, 같은 이름의 전역 타입, 같은 이름의 스크립트 파일(심볼이 꺼져 컴파일되지 않은 치트 파일 포함, 예제 Provider 포함)이 이미 있으면 `ItemCheats2`, `ItemCheats3` … 처럼 뒤에 번호를 붙여 만듭니다. 이때 분류와 id에도 번호가 붙습니다(`ItemCheats2` → `Item2/Log`, id `item2.log`).
- 폴더가 Assembly-CSharp면 파일 전체를 `#if <User Define>`로 감쌉니다. asmdef 폴더면 감싸지 않습니다(asmdef의 `defineConstraints`가 막음). 설정 화면에 그 폴더가 어느 어셈블리인지 보입니다.
- `Create Cheat asmdef in '<폴더>'`: 폴더가 Assembly-CSharp일 때만 보입니다. `Cheat Asmdef Name`으로 asmdef를 만들고 `HB.Cheat` + `Cheat Asmdef References`를 참조, `defineConstraints`에 `HB_CHEAT` + `User Define`을 넣습니다. 이미 있으면 경고만 냅니다.

### 레지스트리 생성

- `[Cheat]` 메서드를 `TypeCache`로 찾아 **어셈블리마다** `HBCheatRegistry.g.cs` 하나를 만듭니다. 런타임 리플렉션 없이 호출 코드를 미리 만들어 둡니다.
- 생성 위치: Assembly-CSharp는 `Assembly C Sharp Output Folder`, asmdef는 `<asmdef 폴더>/Generated`. `Assets` 밖 asmdef, Assembly-CSharp도 asmdef도 아닌 어셈블리, 생성 폴더가 다른 어셈블리로 컴파일되는 경우는 에러로 보고하고 쓰지 않습니다.
- 내용이 같으면 파일을 쓰지 않습니다. 치트가 없어진 어셈블리의 파일, 출력 폴더를 바꾼 뒤 남은 이전 파일은 지웁니다.
- 생성기는 **현재 에디터 플랫폼**에서 치트 심볼이 켜져 있을 때만 있습니다. 꺼져 있으면 `Generate Registry`가 비활성이고, 켜지면 생성기가 로드되며 생성합니다.
- `Generate Registry`: 지금 생성합니다(경고도 Console에 남김). `Delete Generated`: `Assets` 아래의 생성 파일을 모두 지웁니다(심볼이 꺼져 있어도 찾음). 생성된 파일 목록은 버튼 아래 `Generated:`에 보입니다.

### 패널

| 탭 | 내용 |
|---|---|
| Browse | 경로의 `/`마다 탭 줄을 하나씩 보여 줍니다. 그룹은 `이름  >` 탭, 마지막 마디는 치트 버튼입니다. 서버 치트 버튼은 왼쪽에 주황 테두리가 붙습니다. 위쪽 검색은 경로와 id로 찾습니다. |
| Favorites | 즐겨찾기한 치트만 보여 줍니다. 치트 제목 옆 `+ Fav` / `- Fav`로 바꾸며, `PlayerPrefs`(`HB_Cheat.Favorites`)에 저장됩니다. |
| History | 최근 실행을 새것부터 보여 줍니다. 상태 · 주체(패널이 아니면 `[CLI]` 등) · 설명 · 보낸 명령 · 메시지가 나옵니다. `Edit`는 그 값으로 치트를 다시 열고, raw는 `Reuse`로 Raw 탭에 채웁니다. 서버 큐가 멈추면 맨 위에 이유와 `Resume` 버튼이 나옵니다. |
| Raw | 문자열을 조합 없이 그대로 서버에 보냅니다(`Send`). |

- 치트를 고르면 인자가 칩(`이름: 값`)으로 나오고, 칩을 누르면 그 인자의 편집기만 열립니다. 값이 비어 있으면 `(select)`이고, 처음에는 비어 있는 첫 인자부터 엽니다.
- 숫자: 큰 값 표시, `±step` · `±step×10` 버튼, 범위가 있으면 `Min` · `Max`, 패널 키패드(정수형은 `.` 없음, `+/-` · `<-` · `C`).
- 목록: 검색 칸, `걸러진 수/전체 수`, 가상화 리스트. 고르면 다음 인자로 넘어갑니다. 라벨과 값이 다르면 `라벨   (값)`으로 보입니다.
- `bool`은 `True` / `False` 버튼, `string`은 텍스트 칸(OS 키보드)입니다.
- 아래 미리보기에 `Send: <명령>`(서버) 또는 `Run: <경로 인자=값 ...>`(로컬)이 나오고, 검증에 실패하면 `(incomplete) <이유>`와 함께 `Run` 버튼이 꺼집니다.
- 치트를 다시 열면 마지막으로 쓴 값으로 채웁니다(없으면 기본값).
- 레이아웃: 패널 너비가 640 이상이고 가로가 더 길면 좌우 2단, 아니면 위아래입니다. 독립 패널 창은 화면 너비 1100 이상이면 오른쪽 60%, 아니면 안전 영역 안 전체입니다. 안전 영역과 OS 키보드 높이만큼 여백을 둡니다.
- 독립 패널은 자체 `UIDocument`(정렬 순서 1001)로 뜨고, `Close`로 닫습니다. 콘솔 탭에서는 `Close` 버튼을 숨깁니다.

### Cheats 탭 (에디터 창)

- `Rescan`(다시 스캔만) · `Generate`(생성) · 검색 칸이 있습니다.
- 스캔에서 나온 에러 · 경고를 메서드별로 보여 줍니다.
- 등록될 치트를 어셈블리별로 보여 줍니다(경로, id, Target, 인자 타입 · 이름 · 입력 방식). `Script`는 해당 스크립트를 엽니다(파일 이름이 클래스 이름과 같아야 함). Play Mode에서는 `Show`로 패널에서 그 치트를 열고, 아래에 최근 실행 20건을 보여 줍니다.

### 서버 전송 연결

```csharp
public sealed class PacketCheatTransport : ICheatServerTransport
{
    public bool TrySend(string command, out string error)
    {
        error = null;
        if (!Network.IsConnected) { error = "Not connected"; return false; }
        Network.Send(new C2P_RequestCheat { Command = command });
        return true;
    }
}

// 초기화 코드 어딘가에서
CheatSystem.ServerTransport = new PacketCheatTransport();

// 서버 응답 핸들러에서 (네트워크 스레드여도 됨: 다음 프레임에 메인 스레드에서 처리)
CheatSystem.ReportServerResult(packet.Result == Result.Success, packet.Result.ToString());

// 재접속에 성공했을 때 (시간 초과로 멈춘 큐 재개)
CheatSystem.ResumeServerQueue();
```

- 응답에 요청 식별자가 없으므로 **서버 치트는 한 번에 하나만 전송**합니다. 나머지는 순서대로 대기합니다 (`Queued`).
- `TrySend` 안에서 `ReportServerResult`를 바로 불러도 됩니다. `TrySend`가 false를 돌려주거나 예외를 내면 그 실행은 `Failed`(이유는 `error`)입니다.
- 전송이 등록돼 있지 않거나, `CheatSystem`이 초기화되지 않았거나, 명령 문자열이 비어 있으면 서버 치트는 `Failed`입니다.
- `ReportServerResult`는 처리했거나(메인 스레드가 아니면 다음 프레임 처리로 넘기고) true, 메인 스레드에서 처리했는데 기다리는 건이 없으면 false를 돌려줍니다.
- 응답이 `ServerTimeoutSeconds`(기본 10초) 안에 오지 않으면 `TimedOut`이 되고 **서버 큐가 멈춥니다**. 늦게 온 응답이 다음 요청의 결과로 잘못 처리되지 않게 하기 위함입니다.
  - 멈춘 뒤 늦은 응답이 도착하면 그 응답은 버리고 자동으로 재개합니다.
  - 응답이 끝내 오지 않으면(연결 끊김 등) 재접속 처리에서 `CheatSystem.ResumeServerQueue()`를 부르거나, 패널 **History** 탭의 **Resume** 버튼을 누릅니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 처리될 수 있습니다. 재개할 때 아직 처리하지 않은 응답은 이전 연결의 것으로 보고 버립니다.
  - 치트 시스템이 종료되면(씬 전환 등) 대기 중인 서버 치트는 `Failed`("Cheat system shut down")로 끝납니다. 응답을 기다리던 건이 있었다면 다시 초기화해도 멈춘 상태로 시작합니다.
  - 상태는 `CheatSystem.IsServerQueuePaused`, `ServerQueuePauseReason`, `QueuedServerCount`, `InFlightServerExecution`, `ServerQueueChanged`로 확인합니다.
- 서버 없이 확인할 때는 `CheatSystem.ServerTransport = new CheatLogTransport();`을 쓰면 명령이 로그로만 출력됩니다. 응답은 `ReportServerResult`로 직접 넣습니다(넣지 않으면 시간 초과).

### 코드 API

| API | 내용 |
|---|---|
| `CheatSystem.Initialize()` | 러너(`[HB_Cheat]` GameObject)와 패널을 만듭니다. 이미 초기화됐거나 종료 중이면 무시합니다. |
| `CheatSystem.IsInitialized` · `IsHostedInConsole` | 초기화 여부, 패널이 콘솔 탭으로 붙었는지. |
| `CheatSystem.Open()` · `Close()` · `Toggle()` · `IsOpen` | 패널 열기 · 닫기. 콘솔 탭이면 콘솔의 `Cheat` 탭을 엽니다. |
| `CheatSystem.ShowCheat(id, values = null)` | 패널을 열고 그 치트를 고릅니다(값을 넘기면 그 값으로 채움). 초기화 전이거나 없는 id면 false. |
| `CheatSystem.Execute(definition, values[, source])` | 실행합니다. 주체를 빼면 `Code`. 로컬 치트는 초기화 전에도 실행되고, 서버 치트는 초기화가 필요합니다. 인자 검증에 실패해도 `Failed` 실행으로 히스토리에 남습니다. |
| `CheatSystem.ExecuteRaw(command[, source])` | 문자열을 그대로 서버로 보냅니다(앞뒤 공백 제거). |
| `CheatSystem.Validate(definition, values, out error[, out code])` | 인자 검증(빈 값, 범위, 목록 포함). `code`는 `CheatErrorCode` 값입니다. |
| `CheatSystem.ReportServerResult(success, message)` · `ResumeServerQueue()` | 서버 응답 보고, 멈춘 큐 재개 (위 참고). |
| `CheatSystem.History` · `FindExecution(id)` · `SessionId` | 히스토리(오래된 것부터), id로 찾기, 실행 id 공간의 세션 값(8자리 16진). |
| `CheatSystem.GetLastValues(id)` | 이 치트에 마지막으로 쓴 값(복사본, 없으면 null). |
| `CheatSystem.GetOptions(definition, argIndex, values, out error)` · `InvalidateOptions()` | 목록 후보 조회(캐시), 캐시 전체 버리기(테이블 리로드 뒤 등). |
| `CheatSystem.IsFavorite(id)` · `SetFavorite(id, bool)` | 즐겨찾기 조회 · 변경(`PlayerPrefs` 저장). |
| `CheatSystem.ExecutionChanged` · `FavoritesChanged` · `ServerQueueChanged` | 실행 추가 · 상태 변경, 즐겨찾기 변경, 서버 큐 멈춤 · 재개 이벤트. 구독자 하나의 예외는 다른 구독자와 큐 진행을 끊지 않습니다(실행 · 큐 이벤트). |
| `CheatSystem.RegisterBridge(bridge)` | `ICheatBridge` 등록. 초기화 때 `Start`, 종료 때 `Stop`이 불립니다(이미 초기화됐으면 바로 `Start`). |
| `CheatSystem.Options` · `ServerTransport` · `ConsoleHost` | 런타임 옵션, 서버 전송, 패널 호스트(`ICheatPanelHost`, InGameConsole 연동이 넣음). |
| `CheatRegistry.Register` · `TryGet` · `All` · `Count` · `Changed` | 등록된 치트 목록. 같은 id는 교체합니다(경로가 다르면 경고). `All`은 부모 경로 → `Order` → 경로 → id 순입니다. |
| `CheatCommand.Join(name, args...)` · `Format(value)` | 서버 치트 문자열 조립 (정의 규칙 참고). |
| `CheatValueParser.TryParse` · `ToText` | 문자열 ↔ 인자 값 변환(InvariantCulture). |
| `CheatResult.Ok(message)` · `Fail(message)` | 로컬 치트의 결과. |
| `CheatExecution` | `Id` · `Definition`(raw면 null) · `Values` · `Command` · `Source` · `Session` · `Status` · `Message` · `StartedAt` · `SentAt` · `FinishedAt` · `IsDone` · `IsServer` · `Describe()`. 시각은 `Time.realtimeSinceStartup`입니다. |

---

## 📚 샘플

`Window > Package Manager > HB_Cheat > Samples > Cheat Examples > Import`로 가져옵니다. 클라 치트·서버 치트를 종류별로 모은 예제와, 가짜 네트워크에 연결한 전송 예제가 들어 있습니다. 가져온 위치는 `Assets/Samples/HB_Cheat/<버전>/Cheat Examples`입니다.

---

## 🧩 정의 규칙

| 항목 | 내용 |
|---|---|
| `[Cheat(id, path)]` | `static` 메서드(public/internal)에 붙입니다. **id는 고정 식별자**로 즐겨찾기, 히스토리, 자동 QA가 사용하며 path를 옮겨도 바꾸지 않습니다. path의 `/`는 탭을 나누고, 마지막 마디가 치트 이름입니다. |
| `Target` | `Local`(기본): 클라에서 메서드를 실행합니다. `Server`: 메서드가 반환한 문자열을 `ICheatServerTransport`로 보냅니다. **Server 치트 메서드는 명령 문자열만 만들고 부작용이 없어야 합니다** — 패널 미리보기가 인자 값이 바뀔 때마다 불러 보낼 문자열을 보여 줍니다. |
| `Description` · `Order` | 패널 제목 아래 설명, 같은 탭 안 정렬 순서(작을수록 앞, 기본 0). |
| 반환형 | Local: `void`(예외 없으면 성공) · `bool`(false면 실패) · `string`(성공 + 메시지) · `CheatResult`(직접 지정). 예외는 `Failed`와 예외 메시지가 됩니다. Server: `string`(보낼 명령)만. |
| 인자 타입 | `int`, `long`, `float`, `double`, `bool`, `string`, `enum`. `ref` · `out` · `params`는 안 됩니다. |
| `[CheatArg("라벨")]` | 패널에 보일 인자 이름입니다. 없으면 매개변수 이름을 씁니다. |
| 입력 방식 | 숫자는 스테퍼와 패널 키패드를 씁니다(OS 키보드 없음). `bool`은 토글, `enum`과 `[CheatOptions]`는 목록(검색, 가상화), `string`은 텍스트 입력입니다. |
| `[CheatRange(min, max, Step = n)]` | 숫자 범위와 스테퍼 간격(기본 1)을 지정합니다. 숫자가 아닌 인자에 붙이면 경고, `min > max`는 에러입니다. |
| `[CheatOptions(typeof(Provider), DependsOn = ...)]` | 목록 선택입니다. Provider는 `ICheatOptionProvider` 구현 + public 매개변수 없는 생성자가 필요합니다. `DependsOn`에는 **앞쪽** 매개변수 이름만 쓸 수 있고, 앞 인자가 바뀌면 이 인자는 초기화됩니다. |
| Provider 동작 | 메인 스레드에서 불립니다. 치트를 고를 때(패널) · 실행할 때(CLI `cheat_run` 포함) · CLI로 후보를 조회할 때(`cheat_options`) 다시 수집하고, 같은 치트를 편집하는 동안은 (치트, 인자)마다 캐시합니다(`DependsOn` 값이 바뀌면 다시 수집). 열려 있는 목록을 바로 바꾸려면 `CheatSystem.InvalidateOptions()`를 부르세요. 앞 인자 값은 `context.Get<T>(이름)` / `TryGet`으로 읽습니다. Provider 인스턴스는 레지스트리마다 타입당 하나입니다. 예외가 나면 그 목록은 비고 에러로 보고됩니다. |
| `enum` | 정의된 이름이 목록이 되고, 서버 문자열에는 정수 값이 들어갑니다. 자동화 입력은 이름(대소문자 무시) 또는 정의된 정수 값을 받습니다. |
| 기본값 | C# 매개변수 기본값을 그대로 사용합니다. 없으면 숫자 0(범위 안으로 맞춤), `bool` false, `string` 빈 문자열, `enum` 첫 값이고, Provider 목록 인자는 고를 때까지 비어 있습니다. |
| `CheatCommand.Join` | 문화권과 무관하게 직렬화합니다. 공백이나 따옴표 · `\`가 있는 문자열과 빈 문자열은 `"..."`로 감싸고(`\"` · `\\` 이스케이프), `bool`은 `true`/`false`, enum은 정수로 바꿉니다. |

레지스트리를 생성할 때 잘못된 정의(private, 미지원 타입, 중복 id, 잘못된 DependsOn 등)는 **에러로 보고되고 생성에서 제외**됩니다. `Cheats` 탭에서 확인할 수 있습니다.

- 에러: 빈 id · path, static 아님, 제네릭, public/internal 아님, 선언 타입이 private · protected 중첩, 런타임(플레이어) 어셈블리가 아님(Editor 어셈블리 등), 반환형, 지원하지 않는 인자 타입, 생성 코드에서 접근할 수 없는 enum · Provider, 중복 id(겹친 치트 모두 제외).
- 경고(생성은 함): 여러 치트가 같은 path를 씀, 숫자가 아닌 인자의 `[CheatRange]`, 예약 인자 이름(`query`, `limit`).

---

## 🔒 릴리즈 빌드 제외

| 위치 | 제외 방식 |
|---|---|
| 패키지 런타임 (`HB.Cheat`) | asmdef `defineConstraints: HB_CHEAT` |
| 패키지 에디터 생성기 · CLI · 콘솔 연동 · 테스트 | 각 asmdef `defineConstraints`에 `HB_CHEAT` (CLI는 `HB_CHEAT_PIPELINE`, 콘솔 연동은 `HB_CHEAT_HAS_CONSOLE`, 테스트는 `UNITY_INCLUDE_TESTS`도 요구) |
| 패키지 에디터 기본 (`HB.Cheat.Editor`) | 심볼과 무관하게 컴파일되는 Editor 전용 어셈블리입니다(설정 창, 심볼 관리, 빌드 가드, runInBackground 복원). |
| 치트 코드 (Assembly-CSharp) | 파일 전체를 `#if DEV_CHEAT`로 감쌉니다 (템플릿이 자동으로 넣음). 가드를 빠뜨리면 `HB.Cheat` 타입이 없어 컴파일 에러가 납니다. |
| 치트 코드 (asmdef) | `Create Cheat asmdef`로 만든 asmdef는 `HB_CHEAT` + `DEV_CHEAT`를 둘 다 요구합니다. |
| 생성 레지스트리 | `#if HB_CHEAT && DEV_CHEAT` |
| UI | USS, UXML, Resources를 쓰지 않고 코드로만 만듭니다. 설정 에셋은 `Assets/Editor`에 둡니다. |

**빌드 가드** (`CheatBuildGuard`)

1. **빌드 전**: 릴리즈 빌드인데 치트 심볼이 켜져 있으면 실패합니다. 두 심볼 중 하나만 켜져 있으면 릴리즈가 아니어도 실패합니다. 다른 빌드 단계가 심볼을 바꾼 뒤에 검사하도록 늦게(`callbackOrder` 10000) 돕니다.
2. **스크립트 컴파일 직후**: 릴리즈 빌드라면 컴파일된 DLL에 패키지 어셈블리(`HB.Cheat.dll` · `HB.Cheat.*.dll`)가 있거나, DLL 메타데이터에 `HB.Cheat` 어셈블리 참조나 `HB.Cheat.*` 네임스페이스 이름이 있으면 실패합니다(이름 끝이 맞을 때만 — `HB.CheatSheet` 같은 다른 이름이나 문자열 상수는 걸리지 않음. 컴파일러가 끝이 같은 이름을 접어 저장해도 놓치지 않도록 앞쪽은 보지 않아 `MyHB.Cheat` 같은 이름은 걸립니다). 검사할 DLL이 하나도 없어도 기본적으로 실패합니다(`Fail When Unverifiable`).

검사하는 심볼은 빌드 대상 플랫폼(Dedicated Server 빌드면 Server — 에디터 설정이 아니라 그 빌드의 서브타깃)의 PlayerSettings 심볼과 **활성 Build Profile**의 Scripting Defines를 합친 것입니다. 설정 창은 활성 Build Profile에도 치트 심볼이 있으면 경고합니다(`Turn On`/`Turn Off`는 PlayerSettings만 바꿈).

**릴리즈 판정 기준** (설정에서 변경)

- 환경 변수 `HB_CHEAT_RELEASE=1` 또는 `true` (CI용)
- `Release Defines`(기본 `LIVE`) 중 하나가 켜져 있을 때
- Development Build가 아닐 때 (`Treat Non Development As Release`, 기본 ON). Development가 아닌 QA 빌드에도 치트가 필요하면 이 옵션을 끄고 `Release Defines`로 구분하세요.

> `BuildPlayerOptions.extraScriptingDefines`로 넣은 심볼은 빌드 리포트에서 읽을 수 없습니다. 그 경로로 릴리즈를 빌드한다면 `HB_CHEAT_RELEASE=1`을 함께 설정하세요. (HB_Build는 PlayerSettings를 쓰므로 해당되지 않습니다.)
> 빌드 가드는 산출물을 만들기 전에 멈추는 장치입니다. 배포 단계(CI)의 검사를 대신하지 않습니다.

**라이브 환경에서 치트가 필요할 때**

- 라이브 서버에 붙는 QA 빌드(Development Build, `LIVE` 심볼 없음)를 만들어 쓰세요. 접속할 서버 주소는 게임 쪽 설정이라 빌드 가드와 상관없습니다.
- `LIVE` 심볼이 켜진 빌드나 Development가 아닌 빌드는 릴리즈로 보고 치트를 막습니다(위 판정 기준). LIVE + Development 빌드(HB_Build의 `_DEVBUILD`)도 `LIVE` 심볼 때문에 막힙니다.
- 서버 치트(`CheatTarget.Server`)가 라이브 서버에서 동작할지는 서버가 정합니다(클라가 보낸 치트 명령을 받아 줄지).

### IDE(Rider · Visual Studio) 표시

치트 심볼이 꺼져 있으면 IDE에서 치트 코드가 다르게 보입니다. **실제 Unity 컴파일에는 문제가 없습니다**.

| 치트 코드 위치 | 심볼 OFF일 때 IDE 표시 |
|---|---|
| Assembly-CSharp (`#if DEV_CHEAT`) | `#if` 안쪽이 회색(비활성)으로 보임 |
| 치트 전용 asmdef | Unity가 그 asmdef의 csproj를 **파일 없이** 만들기 때문에, 치트 파일이 프로젝트 밖 파일로 잡혀 **빨간 에러**로 보임 |

치트를 작성하거나 수정하는 동안에는 에디터 플랫폼(보통 Standalone)에서 심볼을 켜 두세요. 릴리즈 빌드는 빌드 가드가 막습니다.
`Tools > HB > Cheat`는 `defineConstraints`에 `HB_CHEAT`를 요구하는 프로젝트 asmdef(`Assets` 아래, 이름이 `HB.Cheat`로 시작하는 것 제외)가 있는데 현재 플랫폼의 심볼이 꺼져 있으면 경고와 `<플랫폼>에서 켜기` 버튼을 보여줍니다.
(External Tools에서 Git packages의 csproj 생성을 켰다면 패키지 코드도 같은 이유로 빨갛게 보일 수 있습니다.)

---

## 🖥 InGameConsole 연동

- `com.hb.ingameconsole`이 설치돼 있으면 연동 어셈블리가 자동으로 컴파일됩니다 (`versionDefines`). 콘솔이 패키지가 아니라 Assets 폴더에 있다면, 치트 심볼을 켤 때 `HB_CHEAT_HAS_CONSOLE`도 함께 켜지고 끌 때 함께 꺼집니다(설정 창에 안내가 보임).
- `CheatSystem`이 초기화되는 시점에 콘솔이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다. 콘솔이 자동 시작 설정이면 순서와 관계없이 콘솔을 먼저 초기화합니다. 콘솔이 없거나, 수동 초기화 설정인데 아직 초기화되지 않았거나, 콘솔 UI가 없어 탭을 붙이지 못하면 **독립 패널**로 뜹니다.
- 콘솔 탭으로 들어가면 **독립 패널과 화면의 `CHEAT` 버튼은 만들지 않습니다.** 열 때는 콘솔을 열고 `Cheat` 탭을 고르거나 열기 키(`F8`)를 누릅니다(콘솔의 `Cheat` 탭이 열림). `CheatSystem.Close()`는 콘솔을 닫습니다. 콘솔이 있어도 독립 패널 · `CHEAT` 버튼을 쓰려면 `Tools > HB > Cheat` 설정의 **Prefer Console Tab**을 끕니다 (코드로는 `CheatSystem.Initialize` 전에 `CheatSystem.Options.PreferConsoleTab = false`).
- 치트 시스템이 종료되면 콘솔에서 `Cheat` 탭을 뗍니다(콘솔이 더 오래 살아 있어도 패널을 붙잡지 않음).
- 독립 패널에서 한글이 깨지면 `CheatSystem.Options.PanelSettings`에 프로젝트 UI의 PanelSettings(테마, 한글 폰트 포함)를 지정하세요. 기본은 코드로 만든 PanelSettings(1280×720 기준 화면 크기 맞춤)와 내장 폰트, OS 폴백입니다.

---

## 🤖 자동화 (Unity CLI)

`com.unity.pipeline`이 있으면 에디터 Play Mode용 명령이 등록됩니다. 결과는 JSON입니다.

```bash
unity command cheat_list
unity command cheat_options --id item.enchant --arg level --args "itemId=1001"
unity command cheat_run --id item.enchant --args "itemId=1001 level=15"
unity command cheat_status --execution 3 --session 1a2b3c4d   # 서버 치트는 Pending → Succeeded / Failed / TimedOut
unity command cheat_history --limit 10
unity command cheat_raw --command "item_add 1001 10"  # 기본 꺼짐 (아래 raw 참고)
unity command cheat_play   # Play 시작. 이 Play 동안만 runInBackground (아래 참고)
unity command cheat_stop   # Play 종료
```

| 명령 | 인자 | 내용 |
|---|---|---|
| `cheat_list` | 없음 | 등록된 치트와 인자 목록 (Play Mode에서만 — 치트는 Play 진입 때 등록됩니다). |
| `cheat_options` | `--id` · `--arg`(필수), `--args`(앞 인자 값), `--query`(라벨 · 값 검색), `--limit`(기본 200) | 인자의 선택 목록. |
| `cheat_run` | `--id`(필수), `--args` | 실행. 빠진 인자는 기본값을 씁니다. 서버 치트는 `Pending`으로 시작합니다. |
| `cheat_status` | `--execution`(필수), `--session` | 실행 상태 조회. |
| `cheat_history` | `--limit`(기본 20) | 최근 실행. |
| `cheat_raw` | `--command`(필수) | raw 전송 (기본 꺼짐). |
| `cheat_play` · `cheat_stop` | 없음 | Play 시작 · 종료. |

- `cheat_play` · `cheat_stop`을 뺀 명령은 Play Mode가 아니거나 초기화되지 않았으면 `not_initialized`입니다.
- 코드에서 직접 부르는 `CheatProtocol.Status` · `History`는 초기화 전에도 남은 기록을 읽습니다(CLI는 위 규칙으로 막음).
- `--args`는 `k=v`를 공백으로 나눕니다. 값에 공백이 있으면 `"..."`로 감싸고, 안의 `"`는 `\"`로 씁니다.

실기기 연결(ADB, 로컬 소켓 등)은 `ICheatBridge`를 구현해 `CheatSystem.RegisterBridge`로 등록합니다. 받은 한 줄을 `CheatProtocol.Handle(line)`에 넘기고 결과 JSON을 돌려주면 됩니다(`list` / `options <id> <arg> [k=v ...] [query=텍스트] [limit=N]` / `run <id> [k=v ...]` / `raw <명령...>` / `status <id> [session=S]` / `history [limit=N]`, 동사는 대소문자 무시). `Handle`은 **메인 스레드에서** 부릅니다(소켓 스레드에서 받았다면 메인 스레드로 넘긴 뒤 호출). 브리지 경로의 실행 주체는 `Bridge`로 남고, `CheatProtocol.Handle(line, source)`로 주체를 지정할 수도 있습니다.

### 응답 JSON

- 성공은 `{"ok":true,...}`, 실패는 예외가 아니라 항상 `{"ok":false,"error":"...","code":"..."}`입니다.
- **인자·요청 문제는 `ok:false` + `code`**, **실행 결과 문제**(로컬 치트의 실패·예외, 서버 응답 실패, 전송 실패, 시간 초과)는 `ok:true` + `execution.status`(`Failed` / `TimedOut`)입니다. 범위 밖·목록에 없는 값처럼 인자 문제는 자동화 경로에서 실행을 만들지 않습니다(패널은 기존대로 실행 후 `Failed`로 남김).
- 숫자가 NaN · 무한대면 `null`로 씁니다 (예: `cheat_list`에서 `[CheatRange]`의 `min`/`max`를 무한대로 둔 경우).
- `code`는 **계약**입니다. 이름을 바꾸지 않습니다. `error` 문구는 사람용이라 바뀔 수 있으니 분기는 `code`로 합니다. 상수: `HB.Cheat.CheatErrorCode`.

  | code | 언제 나오나 |
  |---|---|
  | `not_initialized` | Play Mode가 아니거나, Play Mode 중 재컴파일 등으로 CheatSystem 초기화가 풀림 |
  | `session_changed` | `--session`(브리지 `session=`)이 현재 세션과 다름. Play Mode 재시작·재컴파일로 실행 id가 1부터 다시 발급됨 |
  | `raw_disabled` | 자동화 경로(CLI·브리지)의 raw가 꺼져 있음 |
  | `evicted_execution` | 현재 세션에서 실행됐지만 히스토리 용량으로 지워진 실행 id |
  | `unknown_execution` | 현재 세션에서 발급된 적 없는 실행 id (세션을 넘기면 이전 세션 id는 `session_changed`) |
  | `empty_command` | 브리지에 빈 줄 |
  | `unknown_command` | 브리지의 모르는 동사 |
  | `invalid_usage` | 브리지 동사의 위치 인자(치트 id · 인자 이름 · 실행 id)가 없거나 형식이 틀림 |
  | `malformed_argument` | `k=v` 형식이 아닌 토큰 (CLI `--args`, 브리지) |
  | `unknown_cheat` | 등록되지 않은 치트 id |
  | `unknown_argument` | 치트에 없는 인자 이름 (`--arg`, `--args`의 키) |
  | `argument_without_options` | 선택 목록이 없는 인자에 `options` 요청 |
  | `unparsable_value` | 값을 인자 타입으로 바꿀 수 없음 (예: int 인자에 `abc`) |
  | `missing_argument` | 기본값 없는 인자를 넣지 않음 |
  | `out_of_range_value` | 숫자가 `[CheatRange]` 범위 밖 (NaN 포함) |
  | `unlisted_value` | 값이 선택 목록에 없음 |
  | `option_provider_failed` | 선택 목록 Provider가 예외를 냄 |
  | `empty_raw_command` | raw 명령이 비어 있음 |
  | `internal_error` | 예상하지 못한 예외 |
  | `already_playing` | `cheat_play`를 Play Mode 중에 호출 |
  | `not_playing` | `cheat_stop`을 에디트 모드에서 호출 |
  | `play_mode_unavailable` | Play Mode 전환 중이거나 컴파일·에셋 갱신 중이라 Play Mode를 바꿀 수 없음 (가라앉은 뒤 다시) |

  처리는 쓰는 쪽이 정합니다. 예: 자동화는 `ok:false`면 `code`를 기록하고 멈춘 뒤 사람에게 넘긴다.

- Unity CLI 프레임워크가 명령을 부르기 전에 거절하는 오류(필수 인자 누락, `--execution abc` 같은 타입 오류)는 이 JSON이 아니라 CLI 봉투로 옵니다. `code`가 없습니다.

  ```json
  {"success":false,"command":"unity command cheat_status","data":null,
   "errors":[{"code":"INVALID_COMMAND_ARGS","message":"... --execution에는 Int32이(가) 필요하지만 abc이(가) 지정되었습니다."}]}
  ```

- `cheat_list` 응답: 치트마다 `id` · `path` · `target` · `description` · `favorite` · `args`, 인자마다 `name` · `label` · `type`(enum은 enum 타입 이름) · `editor`(`Number` / `Toggle` / `Options` / `Text`) · `default`(문자열) · `hasOptions`, 범위가 있으면 `min` · `max`, 의존하면 `dependsOn`(인자 이름 배열)입니다.

  ```json
  {"ok":true,"cheats":[{"id":"item.enchant","path":"Item/Enchant","target":"Server","description":null,"favorite":false,
    "args":[{"name":"itemId","label":"아이템","type":"Int","editor":"Options","default":"","hasOptions":true},
            {"name":"level","label":"강화","type":"Int","editor":"Options","default":"","hasOptions":true,"dependsOn":["itemId"]}]}]}
  ```

- `cheat_options` 응답: `{"ok":true,"total":N,"options":[{"value":"1001","label":"..."}]}`. `total`은 검색 · 개수 제한 전 전체 후보 수이고, `value`는 `--args`에 그대로 넣을 수 있는 문자열입니다.

- `run` / `raw` / `status` / `history` 응답에는 세션과 서버 큐 상태가 붙습니다. `history`는 `"history":[실행...]` 배열(오래된 것부터)입니다.

  ```json
  {"ok":true,"session":"1a2b3c4d",
   "serverQueue":{"paused":true,"reason":"#7 timed out","queued":1,"inFlight":null},
   "execution":{"id":8,"cheatId":"item.clear","target":"Server","source":"Cli",
                "status":"Pending","done":false,"blocked":true,"message":"Paused: ...","command":"item_clear","describe":"..."}}
  ```

  - `session`: 실행 id 공간의 식별자. Play Mode 재시작이나 Play Mode 중 재컴파일로 정적 상태가 초기화되면 id가 1부터 다시 발급되고 세션 값도 바뀝니다. 씬 전환에 따른 재초기화(`Shutdown` → `Initialize`)로는 바뀌지 않습니다.
  - `serverQueue`: `paused`(멈춤), `reason`(멈춘 이유, 아니면 null), `queued`(전송을 기다리는 수, 전송 중 제외), `inFlight`(응답을 기다리는 실행 id, 없으면 null).
  - `cheatId`: raw 실행이면 null. `command`: 서버로 보낸 문자열(로컬이면 null). `describe`: `경로 인자=값 ...` 또는 raw 명령.
  - `source`: 실행 주체 `Panel` / `Cli` / `Bridge` / `Code`. 패널 히스토리에도 패널이 아닌 실행은 `[CLI]`처럼 표시됩니다.
  - `done`: 실행이 끝남 (`Succeeded` / `Failed` / `TimedOut`).
  - `blocked`: 끝나지 않았는데 서버 큐가 멈춰 **스스로는 진행되지 않는** 서버 실행. 큐는 늦은 응답이 오거나 사람이 패널 History 탭의 Resume(또는 재접속 코드의 `CheatSystem.ResumeServerQueue`)을 눌러야 진행됩니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 붙기 때문에 자동화용 재개 명령은 일부러 두지 않았습니다.

### 폴링

- `cheat_run` 응답의 `execution.id`와 `session`을 저장하고 `cheat_status --execution <id> --session <session>`으로 조회합니다. **`--session`을 빼면** run과 status 사이에 초기화가 끼었을 때 다른 실행의 결과를 받을 수 있습니다.
- 끝나지 않은 실행은 히스토리에서 지워지지 않습니다(최근 `HistoryCapacity`건 + 끝나지 않은 실행을 남기므로 그만큼 용량을 넘을 수 있습니다). 정리는 새 실행이 들어올 때만 하므로 끝난 직후에는 조회할 수 있습니다. 끝난 실행은 새 실행이 용량 이상 쌓이기 전에 조회합니다.
- 예: 자동화는 `done`이면 결과를 읽고, `blocked`이거나 `ok:false`면 멈추고 사람에게 넘긴다.

### cheat_play / cheat_stop (Play 동안만 runInBackground)

에디터는 Unity 창이 OS 맨 앞에 없으면 Play 프레임이 1~2에서 멈출 수 있습니다(관찰). `editor_focus`가 성공을 돌려줘도 창이 앞에 오지 않을 수 있습니다. `cheat_play`로 Play를 시작하면 **그 Play 동안만** `Application.runInBackground`를 켜고, 에디트 모드로 돌아오면 원래 값으로 되돌립니다.

- **설치된 프로젝트의 설정을 존중하고 강제하지 않습니다.** 메모리상 값만 바꾸고 `ProjectSettings.asset`·PlayerSettings를 디스크에 쓰지 않습니다. 원래 값이 이미 true면 아무것도 하지 않습니다.
- 사람이 누른 Play와 `editor_play`로 시작한 Play에는 영향이 없습니다.
- 되돌리는 경로: 정상 종료(Play 종료 전환 중에 한 번, 에디트 모드에 들어간 뒤 다시 한 번), Play 중 재컴파일·도메인 리로드 뒤(에디트 모드에서 남은 상태를 발견하면), Play 진입 실패(컴파일 에러 등 — 에디터 update 10회 동안 Play로 들어가지 않으면), 에디터 종료, 다음 `cheat_play` 직전(이전 상태가 남아 있으면). 상태는 `SessionState`에 둡니다.
- 복원은 심볼과 무관한 `HB.Cheat.Editor` 어셈블리가 하므로, 그 사이 `HB_CHEAT`를 꺼도 돕니다.
- Play 중에 사용자가 직접 바꾼 runInBackground도 종료 때 원래 값으로 덮입니다.
- 설정: `Tools > HB > Cheat` 설정의 **Run In Background During Cli Play** (기본 켬). 끄면 `cheat_play`는 Play만 시작합니다.
- Pipeline 런타임 드라이버도 runInBackground를 켜고 되돌립니다. 소유자를 하나로 두기 위해, `ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`이 있고 `enableInBuilds`가 true면 HB_Cheat는 아무것도 하지 않습니다(`autoStart` 값은 판정에 쓰지 않고 응답에만 보여 줌). 이 판정은 Pipeline API가 아니라 파일만 읽습니다 (**Pipeline 0.8.0-exp.1 기준 형식, 추정**).

```json
{"ok":true,"playMode":"entering",
 "runInBackgroundOverride":{"active":true,"original":false,"reason":null,
                            "pipeline":{"config":"none","enableInBuilds":null,"autoStart":null}}}
```

- `active`가 false면 `reason`: `disabled`(설정 꺼짐) / `already_true`(원래 true) / `pipeline_driver` / `pipeline_config_unreadable`(파일은 있는데 읽지 못함). `original`은 읽지 않았으면 null.
- `pipeline.config`: `none`(파일 없음) / `disabled` / `enabled` / `unreadable`. `enableInBuilds` · `autoStart`는 파일에서 읽은 값(읽지 못했거나 파일이 없으면 null)입니다.
- `cheat_stop` 응답은 `{"ok":true,"playMode":"exiting"}`입니다. 복원은 에디트 모드에 들어간 뒤라 응답 시점에는 아직 켜져 있을 수 있습니다.
- 켜고 되돌릴 때 Console에 `[HB_Cheat] runInBackground ...` 한 줄씩 남깁니다.

### raw (자동화 경로는 기본 꺼짐)

`cheat_raw`와 브리지 `raw`는 서버로 임의 문자열을 보내므로 **기본으로 거절**합니다(`code: "raw_disabled"`). 필요하면 `Tools > HB > Cheat` 설정의 **Allow Raw From Automation**을 켜거나 `CheatSystem.Options.AllowRawFromAutomation = true`로 엽니다. 패널 Raw 탭과 코드의 `CheatSystem.ExecuteRaw`는 영향이 없습니다. ("옵션을 비워 두면 기존 동작" 원칙의 의도적 예외입니다.)

브리지 `raw`는 동사 뒤의 원문을 그대로 보냅니다(따옴표 유지, 앞뒤 공백 제거).

### 예약 인자 이름

`query`, `limit`은 프로토콜 옵션으로 쓰므로 치트 인자 이름으로 쓰면 자동화에서 값을 넣을 수 없습니다. `Tools > HB > Cheat`의 Cheats 탭에 경고로 표시합니다(`Generate Registry`를 누르면 Console에도 남음).

### 테스트

`Tests/Editor`에 EditMode 테스트(`HB.Cheat.Tests.Editor`)가 있습니다. 이 저장소에서는 바로 돌고, 패키지로 설치한 프로젝트에서는 `Packages/manifest.json`의 `"testables": ["com.hb.cheat"]`에 넣어야 보입니다. 테스트 어셈블리도 `HB_CHEAT`가 켜져 있어야 컴파일됩니다.

```bash
unity command run_tests --mode EditMode --filter HB.Cheat.Tests   # 에디터가 열려 있을 때
```

---

## ⚠️ 제한

- 자동 QA는 현재 에디터 Play Mode만 지원합니다. 기기는 브리지를 구현해야 합니다.
- Play Mode 중 재컴파일되면 CheatSystem과 등록된 치트 목록이 함께 풀립니다(자동화에는 `not_initialized`). `Initialize`를 다시 불러도 치트 목록은 비어 있으니 Play Mode를 다시 시작하세요.
- 치트를 지운 뒤 생성 레지스트리 때문에 컴파일 에러가 나면 `Delete Generated`를 누른 뒤 다시 생성하세요.
- 치트는 Assembly-CSharp나 `Assets` 아래 asmdef에만 둘 수 있습니다(레지스트리를 그 어셈블리에 생성해야 하므로).
- 프리셋(인자 조합 저장)은 아직 없습니다. 마지막으로 쓴 값, 즐겨찾기, 히스토리 재사용(`Edit`)은 지원합니다. 마지막으로 쓴 값과 히스토리는 메모리에만 있어 앱을 다시 켜거나 Play Mode를 다시 시작하면 사라집니다. 즐겨찾기는 `PlayerPrefs`에 남습니다.
- `[Flags]` enum은 정의된 단일 값 중 하나만 고를 수 있습니다.
