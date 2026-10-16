# HB_Cheat

개발·QA용 치트 패널입니다. 모바일 우선이며 PC도 지원합니다. 치트는 어트리뷰트로 정의하고, 서버 치트 문자열을 조합해 전송할 수 있습니다. 릴리즈 빌드에서는 엄격하게 제외되며, Unity CLI 자동화를 지원합니다. (Unity 6.3+, 6 / 7)

---

## 📦 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat
```

UPM 패키지는 읽기 전용입니다. 그래서 **치트 정의, 설정, 생성 코드는 모두 프로젝트 `Assets` 아래**에 둡니다.

---

## 🚀 시작하기

1. `Tools > HB > Cheat`를 엽니다.
2. **Settings** 탭에서 플랫폼별 `Turn On`을 누릅니다. `HB_CHEAT`(패키지)와 `DEV_CHEAT`(프로젝트, 이름 변경 가능)가 함께 켜집니다.
3. `Create Cheat File`로 치트 파일을 만듭니다. 생성 위치는 기본 `Assets/Cheats`입니다.
4. 컴파일이 끝나면 레지스트리(`HBCheatRegistry.g.cs`)가 자동으로 생성됩니다.
5. Play를 누르면 열 수 있습니다. PC는 `F8`, 모바일은 화면의 `CHEAT` 버튼(끌어서 이동 가능)을 씁니다. InGameConsole이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다.

```csharp
#if DEV_CHEAT   // Assembly-CSharp에 둘 때 (asmdef 방식이면 필요 없음)
using System.Collections.Generic;
using HB.Cheat;

public static class ItemCheats
{
    // Local: 클라에서 직접 실행. 반환형 void / bool / string(메시지) / CheatResult
    [Cheat("actor.level", "Actor/Level")]
    public static void SetLevel([CheatRange(1, 100)] int level) => Player.Local.SetLevel(level);

    // Server: 반환한 문자열을 서버로 보낸다
    [Cheat("item.enchant", "Item/Enchant", Target = CheatTarget.Server)]
    public static string Enchant(
        [CheatArg("아이템"), CheatOptions(typeof(ItemTableProvider))] int itemId,
        [CheatArg("강화"), CheatOptions(typeof(EnchantLevelProvider), DependsOn = new[] { "itemId" })] int level)
        => CheatCommand.Join("item_enchant", itemId, level);   // "item_enchant 1001 15"
}

public sealed class ItemTableProvider : ICheatOptionProvider
{
    public int Version => ItemTable.Version;   // 바뀌면 다시 수집 (고정이면 0)

    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        foreach (var item in ItemTable.All)
            results.Add(new CheatOption(item.Id, item.Name));
    }
}

public sealed class EnchantLevelProvider : ICheatOptionProvider
{
    public int Version => 0;

    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        int max = ItemTable.Get(context.Get<int>("itemId")).MaxEnchant;   // 앞 인자 값
        for (int i = 1; i <= max; i++) results.Add(new CheatOption(i, $"+{i}"));
    }
}
#endif
```

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
- 응답이 `ServerTimeoutSeconds`(기본 10초) 안에 오지 않으면 `TimedOut`이 되고 **서버 큐가 멈춥니다**. 늦게 온 응답이 다음 요청의 결과로 잘못 처리되지 않게 하기 위함입니다.
  - 멈춘 뒤 늦은 응답이 도착하면 그 응답은 버리고 자동으로 재개합니다.
  - 응답이 끝내 오지 않으면(연결 끊김 등) 재접속 처리에서 `CheatSystem.ResumeServerQueue()`를 부르거나, 패널 **History** 탭의 **Resume** 버튼을 누릅니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 처리될 수 있습니다.
  - 응답을 기다리는 중에 치트 시스템이 종료되면(씬 전환 등), 다시 초기화해도 멈춘 상태로 시작합니다.
  - 상태는 `CheatSystem.IsServerQueuePaused`, `ServerQueuePauseReason`, `ServerQueueChanged`로 확인합니다.
- 서버 없이 확인할 때는 `CheatSystem.ServerTransport = new CheatLogTransport();`을 쓰면 명령이 로그로만 출력됩니다.

---

## 📚 샘플

`Window > Package Manager > HB_Cheat > Samples > Cheat Examples > Import`로 가져옵니다. 클라 치트·서버 치트를 종류별로 모은 예제와, 가짜 네트워크에 연결한 전송 예제가 들어 있습니다. 가져온 위치는 `Assets/Samples/HB_Cheat/<버전>/Cheat Examples`입니다.

---

## 🧩 정의 규칙

| 항목 | 내용 |
|---|---|
| `[Cheat(id, path)]` | `static` 메서드(public/internal)에 붙입니다. **id는 고정 식별자**로 즐겨찾기, 히스토리, 자동 QA가 사용하며 path를 옮겨도 바꾸지 않습니다. path의 `/`는 탭을 나누고, 마지막 마디가 치트 이름입니다. |
| 인자 타입 | `int`, `long`, `float`, `double`, `bool`, `string`, `enum` |
| 입력 방식 | 숫자는 스테퍼와 패널 키패드를 씁니다(OS 키보드 없음). `bool`은 토글, `enum`과 `[CheatOptions]`는 목록(검색, 가상화), `string`은 텍스트 입력입니다. |
| `[CheatRange(min, max, Step = n)]` | 숫자 범위와 스테퍼 간격을 지정합니다. |
| `[CheatOptions(typeof(Provider), DependsOn = ...)]` | 목록 선택입니다. 앞 인자가 바뀌면 이 인자는 초기화됩니다. |
| 기본값 | C# 매개변수 기본값을 그대로 사용합니다. |
| `CheatCommand.Join` | 문화권과 무관하게 직렬화합니다. 공백이나 따옴표가 있는 문자열은 `"..."`로 감싸고, enum은 정수로 바꿉니다. |

레지스트리를 생성할 때 잘못된 정의(private, 미지원 타입, 중복 id, 잘못된 DependsOn 등)는 **에러로 보고되고 생성에서 제외**됩니다. `Cheats` 탭에서 확인할 수 있습니다.

---

## 🔒 릴리즈 빌드 제외

| 위치 | 제외 방식 |
|---|---|
| 패키지 런타임 (`HB.Cheat`) | asmdef `defineConstraints: HB_CHEAT` |
| 치트 코드 (Assembly-CSharp) | 파일 전체를 `#if DEV_CHEAT`로 감쌉니다 (템플릿이 자동으로 넣음). 가드를 빠뜨리면 `HB.Cheat` 타입이 없어 컴파일 에러가 납니다. |
| 치트 코드 (asmdef) | `Create Cheat asmdef`로 만든 asmdef는 `HB_CHEAT` + `DEV_CHEAT`를 둘 다 요구합니다. |
| 생성 레지스트리 | `#if HB_CHEAT && DEV_CHEAT` |
| UI | USS, UXML, Resources를 쓰지 않고 코드로만 만듭니다. 설정 에셋은 `Assets/Editor`에 둡니다. |

**빌드 가드** (`CheatBuildGuard`)

1. **빌드 전**: 릴리즈 빌드인데 치트 심볼이 켜져 있으면 실패합니다. 두 심볼 중 하나만 켜져 있어도 실패합니다.
2. **스크립트 컴파일 직후**: 릴리즈 빌드라면 컴파일된 DLL에 `HB.Cheat` 어셈블리나 그 참조가 있는지 검사하고, 있으면 실패합니다. 검사할 DLL이 하나도 없어도 기본적으로 실패합니다.

**릴리즈 판정 기준** (설정에서 변경)

- 환경 변수 `HB_CHEAT_RELEASE=1` (CI용)
- `Release Defines`(기본 `LIVE`) 중 하나가 켜져 있을 때
- Development Build가 아닐 때 (`Treat Non Development As Release`, 기본 ON). Development가 아닌 QA 빌드에도 치트가 필요하면 이 옵션을 끄고 `Release Defines`로 구분하세요.

> `BuildPlayerOptions.extraScriptingDefines`로 넣은 심볼은 빌드 리포트에서 읽을 수 없습니다. 그 경로로 릴리즈를 빌드한다면 `HB_CHEAT_RELEASE=1`을 함께 설정하세요. (HB_Build는 PlayerSettings를 쓰므로 해당되지 않습니다.)
> 빌드 가드는 산출물을 만들기 전에 멈추는 장치입니다. 배포 단계(CI)의 검사를 대신하지 않습니다.

### IDE(Rider · Visual Studio) 표시

치트 심볼이 꺼져 있으면 IDE에서 치트 코드가 다르게 보입니다. **실제 Unity 컴파일에는 문제가 없습니다**.

| 치트 코드 위치 | 심볼 OFF일 때 IDE 표시 |
|---|---|
| Assembly-CSharp (`#if DEV_CHEAT`) | `#if` 안쪽이 회색(비활성)으로 보임 |
| 치트 전용 asmdef | Unity가 그 asmdef의 csproj를 **파일 없이** 만들기 때문에, 치트 파일이 프로젝트 밖 파일로 잡혀 **빨간 에러**로 보임 |

치트를 작성하거나 수정하는 동안에는 에디터 플랫폼(보통 Standalone)에서 심볼을 켜 두세요. 릴리즈 빌드는 빌드 가드가 막습니다.
`Tools > HB > Cheat`는 치트 asmdef가 있는데 현재 플랫폼의 심볼이 꺼져 있으면 경고와 `켜기` 버튼을 보여줍니다.
(External Tools에서 Git packages의 csproj 생성을 켰다면 패키지 코드도 같은 이유로 빨갛게 보일 수 있습니다.)

---

## 🖥 InGameConsole 연동

- `com.hb.ingameconsole`이 설치돼 있으면 연동 어셈블리가 자동으로 컴파일됩니다 (`versionDefines`). 콘솔이 패키지가 아니라 Assets 폴더에 있다면, 치트 심볼을 켤 때 `HB_CHEAT_HAS_CONSOLE`도 함께 켜집니다.
- `CheatSystem`이 초기화되는 시점에 콘솔이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다. 콘솔이 자동 시작 설정이면 순서와 관계없이 콘솔을 먼저 초기화합니다. 콘솔이 없거나, 수동 초기화 설정인데 아직 초기화되지 않았다면 **독립 패널**로 뜹니다.
- 독립 패널에서 한글이 깨지면 `CheatSystem.Options.PanelSettings`에 프로젝트 UI의 PanelSettings(테마, 한글 폰트 포함)를 지정하세요. 기본은 내장 폰트와 OS 폴백입니다.

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
```

실기기 연결(ADB, 로컬 소켓 등)은 `ICheatBridge`를 구현해 `CheatSystem.RegisterBridge`로 등록합니다. 받은 한 줄을 `CheatProtocol.Handle(line)`에 넘기고 결과 JSON을 돌려주면 됩니다(`list` / `options` / `run` / `raw` / `status <id> [session=S]` / `history`). `Handle`은 **메인 스레드에서** 부릅니다(소켓 스레드에서 받았다면 메인 스레드로 넘긴 뒤 호출).

### 응답 JSON

- 실패는 예외가 아니라 항상 `{"ok":false,"error":"..."}`입니다. 자동화가 분기해야 하는 실패에는 `code`가 붙습니다.

  | code | 뜻 |
  |---|---|
  | `evicted` | 이번 세션에 실행됐지만 히스토리 용량으로 지워짐 |
  | `unknown` | 발급된 적 없는 실행 id |
  | `session_changed` | `--session`이 현재 세션과 다름 (Play Mode 재시작·재컴파일로 id가 1부터 다시 발급됨) |
  | `not_initialized` | Play Mode가 아니거나 재컴파일 등으로 CheatSystem 초기화가 풀림 |
  | `raw_disabled` | 자동화 경로의 raw가 꺼져 있음 |

- `run` / `raw` / `status` / `history` 응답에는 서버 큐 상태가 붙습니다.

  ```json
  {"ok":true,"session":"1a2b3c4d",
   "serverQueue":{"paused":true,"reason":"#7 timed out","queued":1,"inFlight":null},
   "execution":{"id":8,"cheatId":"item.clear","target":"Server","source":"Cli",
                "status":"Pending","done":false,"blocked":true,"message":"Paused: ...","command":"item_clear","describe":"..."}}
  ```

  - `session`: 실행 id 공간의 식별자. Play Mode 재시작이나 Play Mode 중 재컴파일로 정적 상태가 초기화되면 id가 1부터 다시 발급되고 세션 값도 바뀝니다. 씬 전환에 따른 재초기화(`Shutdown` → `Initialize`)로는 바뀌지 않습니다.
  - `source`: 실행 주체 `Panel` / `Cli` / `Bridge` / `Code`. 패널 히스토리에도 패널이 아닌 실행은 `[CLI]`처럼 표시됩니다.
  - `blocked`: 끝나지 않았는데 서버 큐가 멈춰 **스스로는 진행되지 않는** 서버 실행.

### 자동화 폴링 규칙

1. `cheat_run` 응답의 `execution.id`와 `session`을 저장하고, `cheat_status --execution <id> --session <session>`으로 폴링합니다. **`--session`을 빼면** run과 status 사이에 초기화가 끼었을 때 다른 실행의 결과를 받을 수 있습니다.
2. `done == true`면 끝(`Succeeded` / `Failed` / `TimedOut`).
3. `blocked == true`, 또는 `code`가 `session_changed` / `not_initialized`면 **폴링을 멈추고 사람에게 넘깁니다.** 큐는 늦은 응답이 오거나 사람이 패널 History 탭의 Resume(또는 재접속 코드의 `CheatSystem.ResumeServerQueue`)을 눌러야 진행됩니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 붙기 때문에 자동화용 재개 명령은 일부러 두지 않았습니다.
4. 끝나지 않은 실행은 히스토리에서 지워지지 않습니다(최근 `HistoryCapacity`건 + 끝나지 않은 실행을 남기므로 그만큼 용량을 넘을 수 있습니다). 끝난 실행은 새 실행이 용량 이상 쌓이기 전에 조회합니다.

### raw (자동화 경로는 기본 꺼짐)

`cheat_raw`와 브리지 `raw`는 서버로 임의 문자열을 보내므로 **기본으로 거절**합니다(`code: "raw_disabled"`). 필요하면 `Tools > HB > Cheat` 설정의 **Allow Raw From Automation**을 켜거나 `CheatSystem.Options.AllowRawFromAutomation = true`로 엽니다. 패널 Raw 탭과 코드의 `CheatSystem.ExecuteRaw`는 영향이 없습니다. ("옵션을 비워 두면 기존 동작" 원칙의 의도적 예외입니다.)

### 예약 인자 이름

`query`, `limit`은 프로토콜 옵션으로 쓰므로 치트 인자 이름으로 쓰면 자동화에서 값을 넣을 수 없습니다.

### 테스트

`Tests/Editor`에 EditMode 테스트(`HB.Cheat.Tests.Editor`)가 있습니다. 이 저장소에서는 바로 돌고, 패키지로 설치한 프로젝트에서는 `Packages/manifest.json`의 `"testables": ["com.hb.cheat"]`에 넣어야 보입니다.

```bash
unity command run_tests --mode EditMode --filter HB.Cheat.Tests   # 에디터가 열려 있을 때
```

---

## ⚠️ 제한

- 자동 QA는 현재 에디터 Play Mode만 지원합니다. 기기는 브리지를 구현해야 합니다.
- Play Mode 중 재컴파일되면 CheatSystem과 등록된 치트 목록이 함께 풀립니다(자동화에는 `not_initialized`). `Initialize`를 다시 불러도 치트 목록은 비어 있으니 Play Mode를 다시 시작하세요.
- 치트를 지운 뒤 생성 레지스트리 때문에 컴파일 에러가 나면 `Delete Generated`를 누른 뒤 다시 생성하세요.
- 프리셋(인자 조합 저장)은 아직 없습니다. 마지막으로 쓴 값, 즐겨찾기, 히스토리 재사용(`Edit`)은 지원합니다.
- `[Flags]` enum은 정의된 단일 값 중 하나만 고를 수 있습니다.
