# 에디터 설정 · 생성

`Tools > HB > Cheat` 창의 **Settings** · **Cheats** 탭을 설명합니다. 처음 쓰는 순서는 [README의 시작하기](../README.md#시작하기)에 있습니다.

## 설정 값 반영

Settings의 Runtime 값과 `Allow Raw From Automation`은 생성 레지스트리 코드에 들어갑니다.
Runtime 값은 Init Mode, Prefer Console Tab, Show Floating Button, 열기 키, 서버 대기 시간, 히스토리 용량, Dont Destroy On Load입니다.

- 설정 창 · Inspector · Undo · VCS 풀 어느 경로로든 이 값이 바뀌면 레지스트리를 바로 다시 생성합니다. Play 중이면 에디트 모드로 돌아온 뒤 생성합니다.
- 글자 칸은 Enter를 누르거나 다른 칸으로 옮길 때 적용됩니다.
- `Auto Generate`를 끄면 `Generate Registry`를 눌러야 반영됩니다.

### 열기 키

- 키 이름 하나만 받습니다 (`F8`, `BackQuote` 등, 대소문자 무시).
- 숫자 · `None` · 쉼표로 이은 이름은 경고 후 쓰지 않습니다.
- Input System 키보드에 없는 키면 Play 중에 경고 후 끕니다.
- 키를 읽을 입력 시스템이 없으면(Active Input Handling이 Input System인데 패키지가 없는 등) 키 이름과 따로 그 사실을 경고합니다.

### 프로젝트 심볼 이름 바꾸기

프로젝트 심볼 이름(`User Define`)을 바꾸면 옛 이름이 있는 모든 플랫폼의 PlayerSettings 심볼도 새 이름으로 바꿉니다.
대상은 Standalone · Android · iOS · Dedicated Server · 예전에 켜 둔 WebGL 등입니다.

치트 파일의 `#if`와 치트 asmdef의 `defineConstraints`는 직접 고쳐야 합니다 (Console에 경고).

## 설정 항목 (Settings 탭)

| 묶음 | 항목 | 기본값 | 내용 |
|---|---|---|---|
| Define | `User Define` | `DEV_CHEAT` | 프로젝트 치트 코드용 심볼. `HB_CHEAT`와 항상 함께 켜고 끕니다. |
| Code | `Cheat Folder` | `Assets/Cheats` | `Create Cheat File` · `Create Cheat asmdef`가 파일을 만드는 폴더입니다. |
| Code | `Assembly C Sharp Output Folder` | `Assets/Cheats/Generated` | Assembly-CSharp에 있는 치트의 레지스트리 생성 폴더입니다. |
| Code | `Cheat Asmdef Name` | `Game.Cheats` | `Create Cheat asmdef`가 만드는 asmdef 이름입니다. |
| Code | `Cheat Asmdef References` | (비어 있음) | 치트 asmdef가 `HB.Cheat`에 더해 참조할 게임 asmdef 이름입니다. |
| Code | `Auto Generate` | 켬 | 컴파일 후 · 설정 변경 후 레지스트리를 자동으로 다시 생성합니다. |
| Runtime | `Init Mode` | `AutoOnGameStart` | 초기화 시점 (아래 설명) |
| Runtime | `Prefer Console Tab` | 켬 | InGameConsole이 있으면 콘솔 탭으로 붙습니다. |
| Runtime | `Show Floating Button` | 켬 | 독립 패널일 때 화면에 `CHEAT` 버튼을 띄웁니다. |
| Runtime | `Toggle Key` | `F8` | PC 열기/닫기 키 이름. 비우면 쓰지 않습니다. |
| Runtime | `Server Timeout Seconds` | `10` | 서버 응답 대기 시간(초). 최소 0.5, NaN · 무한대면 10입니다. |
| Runtime | `History Capacity` | `50` | 히스토리 보존 건수. 최소 1입니다. |
| Runtime | `Dont Destroy On Load` | 켬 | 씬 전환 때 치트 시스템을 유지합니다 (아래 설명) |
| Automation | `Allow Raw From Automation` | 끔 | CLI `cheat_raw` · 브리지 `raw` 허용 ([raw 전송](automation.md#raw-전송)) |
| Automation | `Run In Background During Cli Play` | 켬 | `cheat_play`로 시작한 Play 동안만 runInBackground를 켭니다 ([cheat_play와 cheat_stop](automation.md#cheat_play와-cheat_stop)) |
| Release Guard | `Treat Non Development As Release` | 켬 | Development Build가 아니면 릴리즈로 봅니다. |
| Release Guard | `Release Defines` | `LIVE` | 이 심볼 중 하나라도 켜져 있으면 릴리즈로 봅니다. |
| Release Guard | `Fail When Unverifiable` | 켬 | 릴리즈 빌드에서 검사할 스크립트 DLL이 하나도 없으면 빌드를 실패시킵니다. |

- `Assembly C Sharp Output Folder`: asmdef에 있는 치트는 이 폴더가 아니라 그 asmdef 폴더의 `Generated`에 만듭니다.
- `Init Mode`
  - `AutoOnGameStart`면 첫 씬 로드 뒤(`AfterSceneLoad`) 자동으로 초기화합니다.
  - `Manual`이면 `CheatSystem.Initialize()`를 직접 부릅니다.
- `Dont Destroy On Load`를 끄면 씬이 바뀔 때 패널과 함께 치트 시스템이 종료됩니다. 다시 쓰려면 `CheatSystem.Initialize()`를 부릅니다.
- Runtime 묶음은 생성 레지스트리가 `RuntimeInitializeLoadType.SubsystemRegistration`에서 `CheatSystem.Options`에 넣습니다.
  - 코드로 바꾸려면 그 뒤, `Initialize` 전(예: `BeforeSceneLoad`)에 `CheatSystem.Options`를 고칩니다.
  - 독립 패널의 `PanelSettings`는 코드로만 넣습니다.
- Automation의 `Run In Background During Cli Play`와 Release Guard 묶음은 에디터가 설정 에셋을 직접 읽습니다 (레지스트리에 들어가지 않음).
- 설정 창은 창이 포커스를 잃거나 닫힐 때 설정 에셋을 저장합니다.

## 치트 파일 · asmdef 만들기

### Create Cheat File

`<Cheat Folder>/<Class Name>.cs`를 만듭니다. 들어가는 예제는 다음과 같습니다.

- Local 예제(`<분류>.log`)
- Server 예제(`<분류>.add`)
- 예제 Provider(`<Class Name>SampleProvider`)

분류는 클래스 이름에서 `Cheats`를 뗀 값입니다 (`ItemCheats` → `Item/Log`, id `item.log`).

`Class Name` 규칙:

- C# 식별자여야 합니다 (글자 · 숫자 · `_`, 숫자로 시작 불가, 키워드 불가).
- 생성 코드가 쓰는 이름(`System` · `UnityEngine` · `HB` · `Debug` · `List` · `Cheat*` 타입)도 쓸 수 없습니다. 그 이름을 가려 컴파일되지 않기 때문입니다.
- 규칙에 맞지 않으면 만들지 않고 알립니다.

이름이 겹치면 `ItemCheats2`, `ItemCheats3` …처럼 뒤에 번호를 붙여 만듭니다.

- 겹침으로 보는 것: 같은 파일, 같은 이름의 전역 타입, 같은 이름의 스크립트 파일 (심볼이 꺼져 컴파일되지 않은 치트 파일과 예제 Provider 포함)
- 이때 분류와 id에도 번호가 붙습니다 (`ItemCheats2` → `Item2/Log`, id `item2.log`).

폴더가 Assembly-CSharp면 파일 전체를 `#if <User Define>`로 감쌉니다.
asmdef 폴더면 감싸지 않습니다 (asmdef의 `defineConstraints`가 막음). 설정 화면에 그 폴더가 어느 어셈블리인지 보입니다.

### Create Cheat asmdef

버튼 이름은 `Create Cheat asmdef in '<폴더>'`이고, 폴더가 Assembly-CSharp일 때만 보입니다.

- `Cheat Asmdef Name`으로 asmdef를 만듭니다.
- 참조: `HB.Cheat` + `Cheat Asmdef References`
- `defineConstraints`: `HB_CHEAT` + `User Define`
- 이미 있으면 경고만 냅니다.

## 레지스트리 생성

- `[Cheat]` 메서드를 `TypeCache`로 찾아 **어셈블리마다** `HBCheatRegistry.g.cs` 하나를 만듭니다. 런타임 리플렉션 없이 호출 코드를 미리 만들어 둡니다.
- 생성 위치: Assembly-CSharp는 `Assembly C Sharp Output Folder`, asmdef는 `<asmdef 폴더>/Generated`입니다.
- 아래 경우는 에러로 보고하고 쓰지 않습니다.
  - `Assets` 밖 asmdef
  - Assembly-CSharp도 asmdef도 아닌 어셈블리
  - 생성 폴더가 다른 어셈블리로 컴파일되는 경우
- 내용이 같으면 파일을 쓰지 않습니다. 치트가 없어진 어셈블리의 파일, 출력 폴더를 바꾼 뒤 남은 이전 파일은 지웁니다.
- 생성기는 **현재 에디터 플랫폼**에서 치트 심볼이 켜져 있을 때만 있습니다. 꺼져 있으면 `Generate Registry`가 비활성이고, 켜지면 생성기가 로드되며 생성합니다.
- `Generate Registry`: 지금 생성합니다 (경고도 Console에 남김).
- `Delete Generated`: `Assets` 아래의 생성 파일을 모두 지웁니다 (심볼이 꺼져 있어도 찾음).
- 생성된 파일 목록은 버튼 아래 `Generated:`에 보입니다.

## Cheats 탭 (에디터 창)

- `Rescan`(다시 스캔만) · `Generate`(생성) · 검색 칸이 있습니다.
- 스캔에서 나온 에러 · 경고를 메서드별로 보여 줍니다.
- 등록될 치트를 어셈블리별로 보여 줍니다 (경로, id, Target, 인자 타입 · 이름 · 입력 방식).
- `Script`는 해당 스크립트를 엽니다 (파일 이름이 클래스 이름과 같아야 함).
- Play Mode에서는 `Show`로 패널에서 그 치트를 열고, 아래에 최근 실행 20건을 보여 줍니다.
