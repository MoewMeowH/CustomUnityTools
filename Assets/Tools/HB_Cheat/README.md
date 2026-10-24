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

Settings의 Runtime 값(열기 키, 서버 대기 시간 등)은 생성 레지스트리 코드에 들어갑니다. 설정 창에서 바꾸면 레지스트리를 바로 다시 생성하고(Play 중이면 에디트 모드로 돌아온 뒤), 글자 칸은 Enter나 다른 칸으로 옮길 때 적용됩니다. `Auto Generate`를 끄면 `Generate Registry`를 눌러야 반영됩니다. 열기 키는 키 이름만 받습니다(`F8`, `BackQuote` 등 — 숫자·`None`은 경고 후 사용 안 함).

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
unity command cheat_play   # Play 시작. 이 Play 동안만 runInBackground (아래 참고)
unity command cheat_stop   # Play 종료
```

실기기 연결(ADB, 로컬 소켓 등)은 `ICheatBridge`를 구현해 `CheatSystem.RegisterBridge`로 등록합니다. 받은 한 줄을 `CheatProtocol.Handle(line)`에 넘기고 결과 JSON을 돌려주면 됩니다(`list` / `options` / `run` / `raw` / `status <id> [session=S]` / `history`). `Handle`은 **메인 스레드에서** 부릅니다(소켓 스레드에서 받았다면 메인 스레드로 넘긴 뒤 호출).

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
  | `out_of_range_value` | 숫자가 `[CheatRange]` 범위 밖 |
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

- `run` / `raw` / `status` / `history` 응답에는 세션과 서버 큐 상태가 붙습니다.

  ```json
  {"ok":true,"session":"1a2b3c4d",
   "serverQueue":{"paused":true,"reason":"#7 timed out","queued":1,"inFlight":null},
   "execution":{"id":8,"cheatId":"item.clear","target":"Server","source":"Cli",
                "status":"Pending","done":false,"blocked":true,"message":"Paused: ...","command":"item_clear","describe":"..."}}
  ```

  - `session`: 실행 id 공간의 식별자. Play Mode 재시작이나 Play Mode 중 재컴파일로 정적 상태가 초기화되면 id가 1부터 다시 발급되고 세션 값도 바뀝니다. 씬 전환에 따른 재초기화(`Shutdown` → `Initialize`)로는 바뀌지 않습니다.
  - `source`: 실행 주체 `Panel` / `Cli` / `Bridge` / `Code`. 패널 히스토리에도 패널이 아닌 실행은 `[CLI]`처럼 표시됩니다.
  - `done`: 실행이 끝남 (`Succeeded` / `Failed` / `TimedOut`).
  - `blocked`: 끝나지 않았는데 서버 큐가 멈춰 **스스로는 진행되지 않는** 서버 실행. 큐는 늦은 응답이 오거나 사람이 패널 History 탭의 Resume(또는 재접속 코드의 `CheatSystem.ResumeServerQueue`)을 눌러야 진행됩니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 붙기 때문에 자동화용 재개 명령은 일부러 두지 않았습니다.

### 폴링

- `cheat_run` 응답의 `execution.id`와 `session`을 저장하고 `cheat_status --execution <id> --session <session>`으로 조회합니다. **`--session`을 빼면** run과 status 사이에 초기화가 끼었을 때 다른 실행의 결과를 받을 수 있습니다.
- 끝나지 않은 실행은 히스토리에서 지워지지 않습니다(최근 `HistoryCapacity`건 + 끝나지 않은 실행을 남기므로 그만큼 용량을 넘을 수 있습니다). 끝난 실행은 새 실행이 용량 이상 쌓이기 전에 조회합니다.
- 예: 자동화는 `done`이면 결과를 읽고, `blocked`이거나 `ok:false`면 멈추고 사람에게 넘긴다.

### cheat_play / cheat_stop (Play 동안만 runInBackground)

에디터는 Unity 창이 OS 맨 앞에 없으면 Play 프레임이 1~2에서 멈출 수 있습니다(관찰). `editor_focus`가 성공을 돌려줘도 창이 앞에 오지 않을 수 있습니다. `cheat_play`로 Play를 시작하면 **그 Play 동안만** `Application.runInBackground`를 켜고, 에디트 모드로 돌아오면 원래 값으로 되돌립니다.

- **설치된 프로젝트의 설정을 존중하고 강제하지 않습니다.** 메모리상 값만 바꾸고 `ProjectSettings.asset`·PlayerSettings를 디스크에 쓰지 않습니다. 원래 값이 이미 true면 아무것도 하지 않습니다.
- 사람이 누른 Play와 `editor_play`로 시작한 Play에는 영향이 없습니다.
- 되돌리는 경로: 정상 종료, Play 중 재컴파일·도메인 리로드 뒤(에디트 모드에서 남은 상태를 발견하면), Play 진입 실패(컴파일 에러 등), 에디터 종료. 상태는 `SessionState`에 둡니다.
- Play 중에 사용자가 직접 바꾼 runInBackground도 종료 때 원래 값으로 덮입니다.
- 설정: `Tools > HB > Cheat` 설정의 **Run In Background During Cli Play** (기본 켬). 끄면 `cheat_play`는 Play만 시작합니다.
- Pipeline 런타임 드라이버도 runInBackground를 켜고 되돌립니다. 소유자를 하나로 두기 위해, `ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`이 있고 `enableInBuilds`가 true면 HB_Cheat는 아무것도 하지 않습니다. 이 판정은 Pipeline API가 아니라 파일만 읽습니다 (**Pipeline 0.8.0-exp.1 기준 형식, 추정**).

```json
{"ok":true,"playMode":"entering",
 "runInBackgroundOverride":{"active":true,"original":false,"reason":null,
                            "pipeline":{"config":"none","enableInBuilds":null,"autoStart":null}}}
```

- `active`가 false면 `reason`: `disabled`(설정 꺼짐) / `already_true`(원래 true) / `pipeline_driver` / `pipeline_config_unreadable`(파일은 있는데 읽지 못함). `original`은 읽지 않았으면 null.
- `pipeline.config`: `none`(파일 없음) / `disabled` / `enabled` / `unreadable`.
- `cheat_stop` 응답은 `{"ok":true,"playMode":"exiting"}`입니다. 복원은 에디트 모드에 들어간 뒤라 응답 시점에는 아직 켜져 있을 수 있습니다.
- 켜고 되돌릴 때 Console에 `[HB_Cheat] runInBackground ...` 한 줄씩 남깁니다.

### raw (자동화 경로는 기본 꺼짐)

`cheat_raw`와 브리지 `raw`는 서버로 임의 문자열을 보내므로 **기본으로 거절**합니다(`code: "raw_disabled"`). 필요하면 `Tools > HB > Cheat` 설정의 **Allow Raw From Automation**을 켜거나 `CheatSystem.Options.AllowRawFromAutomation = true`로 엽니다. 패널 Raw 탭과 코드의 `CheatSystem.ExecuteRaw`는 영향이 없습니다. ("옵션을 비워 두면 기존 동작" 원칙의 의도적 예외입니다.)

### 예약 인자 이름

`query`, `limit`은 프로토콜 옵션으로 쓰므로 치트 인자 이름으로 쓰면 자동화에서 값을 넣을 수 없습니다. `Tools > HB > Cheat`의 Cheats 탭에 경고로 표시합니다(`Generate Registry`를 누르면 Console에도 남음).

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
