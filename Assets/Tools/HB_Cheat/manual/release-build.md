# 릴리즈 빌드 제외

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

## IDE(Rider · Visual Studio) 표시

치트 심볼이 꺼져 있으면 IDE에서 치트 코드가 다르게 보입니다. **실제 Unity 컴파일에는 문제가 없습니다**.

| 치트 코드 위치 | 심볼 OFF일 때 IDE 표시 |
|---|---|
| Assembly-CSharp (`#if DEV_CHEAT`) | `#if` 안쪽이 회색(비활성)으로 보임 |
| 치트 전용 asmdef | Unity가 그 asmdef의 csproj를 **파일 없이** 만들기 때문에, 치트 파일이 프로젝트 밖 파일로 잡혀 **빨간 에러**로 보임 |

치트를 작성하거나 수정하는 동안에는 에디터 플랫폼(보통 Standalone)에서 심볼을 켜 두세요. 릴리즈 빌드는 빌드 가드가 막습니다.
`Tools > HB > Cheat`는 `defineConstraints`에 `HB_CHEAT`를 요구하는 프로젝트 asmdef(`Assets` 아래, 이름이 `HB.Cheat`로 시작하는 것 제외)가 있는데 현재 플랫폼의 심볼이 꺼져 있으면 경고와 `<플랫폼>에서 켜기` 버튼을 보여줍니다.
(External Tools에서 Git packages의 csproj 생성을 켰다면 패키지 코드도 같은 이유로 빨갛게 보일 수 있습니다.)
