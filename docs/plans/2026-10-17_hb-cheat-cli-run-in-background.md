# HB_Cheat — CLI로 시작한 Play 동안만 runInBackground 켜기

지시서: [docs/work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md](../work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md) (A 부분. B는 A 커밋 뒤 별도 계획서)

## 요약
- HB_Cheat에 `cheat_play` / `cheat_stop` CLI 명령을 두고, 이 명령으로 시작한 Play 동안에만 `Application.runInBackground`를 메모리에서 true로 켠 뒤 에디트 모드로 돌아오면 원래 값으로 되돌린다 (권장안 (a)).
- 상태(소유 여부·원래 값)는 `SessionState`에 두고, 복원 코드는 심볼 조건이 없는 `HB.Cheat.Editor` 어셈블리에 둔다. 원래 값이 이미 true면 아무것도 하지 않는다.
- 의심 지점 2 조사 결과: Pipeline 런타임 드라이버는 **설정 파일이 있고 `enableInBuilds`가 켜진 프로젝트에서만** 에디터 Play에서도 돈다. 확인 장소 두 곳에는 그 파일이 없다. 켠 프로젝트를 어떻게 다룰지는 질문 1로 올린다.

## 배경

### 7장에서 확정된 것 (TTESTT `docs/verify/2026-10-17_boid/worklog.md`)
- 환경은 `runInBackground=False`였다. 다른 창이 맨 앞에 있으면 Play가 frame 1~2에서 멈췄다. `editor_focus`는 응답이 `True "Editor focused via DockArea"`였지만 frame은 진행되지 않았다.
- 사람이 Unity 창을 클릭하자 진행됐다(frame 3358 → 5202). 원인은 OS 창 포커스로 확정했다.
- `eval`로 켰을 때 `before app=False ps=False | after app=True ps=True`였다. `Application`을 바꾸면 `PlayerSettings`(메모리)도 같이 바뀐다.
- 켜 둔 동안에도, 되돌린 뒤에도, 에디터를 닫은 뒤에도 `ProjectSettings.asset:89 runInBackground: 0`이었다. 다만 에디터는 **되돌린 뒤에** 닫았으므로, 켜진 채로 닫는 경우는 확인되지 않았다.
- `set_autotick`은 "already enabled"였고 정지를 풀지 못했다 (SampleCheck 기록 B-3).

### 코드 조사 (확정, 파일:줄)
| 대상 | 내용 |
|---|---|
| Pipeline `Editor/Commands/PlayModeCommands.cs:15-26` | `editor_play`는 `EditorApplication.isPlaying = true` 한 줄이다. 인자(run_in_background 등)가 없다. |
| Pipeline `Editor/**/*.cs` | 명령 실행 전후의 공개 이벤트가 없다. 유일한 `event`는 `CodeReloadRegistry.OverridesChanged`(internal)뿐이다. `PipelineTransactionLog`도 internal이다. |
| Pipeline `Editor/Commands/FocusEditorCommand.cs` | 메인 창의 HostView에 `GUIView.Focus()`를 리플렉션으로 호출한다. 주석에는 "maps to the native MonoGUIView::Focus, which activates the OS window"라고 적혀 있다. 7장의 "소스 미확인"은 이 내용으로 갱신한다. 그런데도 실제로는 앞으로 오지 않았다. 원인은 Windows가 백그라운드 프로세스의 전경 창 전환을 막기 때문으로 추정한다. |
| Pipeline `Runtime/PlayerSupport/RuntimePipelineBootstrap.cs:16-64` | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`이므로 에디터 Play에서도 실행된다. 다만 `RuntimePipelineConfig.Load()`가 null이거나 `enableInBuilds == false`면 드라이버를 만들지 않는다. |
| Pipeline `Runtime/PlayerSupport/RuntimePipelineConfig.cs:21,60,69-91` | 에디터에서는 `ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`을 읽는다. 파일이 없으면 null이고, `enableInBuilds` 기본값은 false다. |
| 같은 파일, 프로젝트별 | TTESTT·SampleCheck·ToolCreate 모두 이 파일이 없다 → **세 곳 모두 에디터 Play에서 드라이버가 생기지 않는다.** |
| Pipeline `RuntimePipelineDriver.cs:120-125, 640-670, 700-723` | 드라이버가 생기고 `autoStart`가 켜져 있으면 `StartServer`에서 원래 값을 기록하고 true로 켠다. `StopServer`(OnDestroy 경로)에서 그 값으로 되돌린다. 그러면 에디터 Play에서도 Play 내내 true가 된다. |
| `HB.Cheat.Editor.Cli.asmdef` | defineConstraints `HB_CHEAT`·`HB_CHEAT_PIPELINE`. 참조는 `HB.Cheat`, `Unity.Pipeline.Attributes`. |
| `HB.Cheat.Editor.asmdef` | 참조·제약이 없어 심볼과 무관하게 컴파일된다 (`CheatEditorHooks.cs:5-8` 주석과 같은 의도). |
| `CheatSettings.cs:49-51` | Automation 헤더에 `allowRawFromAutomation`(기본 꺼짐)이 있다. 새 설정은 여기에 둔다. |
| `CheatCliCommands.cs:84-95` | `Guard`가 예외를 `internal_error` JSON으로 바꾼다. 새 명령도 이것을 쓴다. |
| `Tests/Editor/HB.Cheat.Tests.Editor.asmdef` | `HB.Cheat`만 참조한다. 컨트롤러를 테스트하려면 `HB.Cheat.Editor` 참조를 추가해야 한다. |
| 규칙서 `@orch project-files/CLAUDE.md:107-131` | 이 세션의 `CLAUDE.md` "Unity 테스트" 절과 같다 (diff 0). **Play 절차는 지금 없다.** 그래서 새 하위 절을 제안한다. |

### 지시서 의심 지점 판정
| # | 판정 | 근거 |
|---|---|---|
| 1 editor_play 신호 없음 | **확정** | 위 표. (b)에는 직접 신호가 없다. |
| 2 Pipeline 드라이버와 소유자 충돌 | **범위 조정** | 설정 파일 + `enableInBuilds` + `autoStart`일 때만 충돌한다. 확인 장소에는 해당하지 않는다. 사용자 결정 2-6의 갈래로 보면 "기본 설정에서는 플레이어 빌드에서만 돈다"이고, 설정을 켜면 "Play 내내 true"다 → 질문 1 |
| 3 editor_focus 소스 | **확정**(소스 있음) / 원인은 추정 | 위 표 |
| 4 관찰 엇갈림 | 추정 유지 | SampleCheck D절은 editor_focus 뒤에도 정지가 없었다. 그때 다른 창이 앞에 있었는지는 기록이 없다. 규칙서 문안에는 "앞에 오지 않을 수 있다"로만 쓴다. |
| 5 asmdef 제약 | **확정** → 계획 반영 | 복원 코드를 `HB.Cheat.Editor`에 둔다. |
| 6 덮기 중 저장 | 추정 유지 → 실제 확인 5행·9행 | 7장은 켠 채로 저장하거나 닫은 적이 없다. |
| 7 프로젝트 차이 | 확정, 사용자 결정으로 정리 | ToolCreate는 쓰지 않는다. 리로드 끔은 옵션을 잠시 바꿔서 본다. |
| 8 재컴파일·비정상 종료 | 추정 유지 → 계획·실제 확인 | 아래 "수명주기" 참조 |

## 계획

### 1. 구분 방법: (a) `cheat_play` / `cheat_stop` (권장, 질문 2)
- `[CliCommand("cheat_play")]`: 에디트 모드에서만 받는다. 설정이 켜져 있으면 컨트롤러에 "이번 Play는 자동화"라고 표시한 뒤 `EditorApplication.isPlaying = true`로 들어간다.
- `[CliCommand("cheat_stop")]`: `EditorApplication.isPlaying = false`. 사람이 시작한 Play도 끌 수 있다 (끄는 것은 값과 무관하다).
- 사람이 툴바나 단축키로 누른 Play는 이 명령을 거치지 않으므로 표시가 없다 → 손대지 않는다.
- `editor_play`는 그대로 쓸 수 있다. 다만 그 경우 runInBackground는 기존과 같다 (지금 동작과 같음).
- 명령은 CLI 어셈블리(`HB_CHEAT` + pipeline 조건)에 두고, 상태와 복원은 `HB.Cheat.Editor`의 컨트롤러가 맡는다. CLI asmdef에 `HB.Cheat.Editor` 참조를 추가한다.

### 2. 설정
- `CheatSettings` Automation 헤더에 `runInBackgroundDuringCliPlay`(bool)를 추가한다. 기본값은 질문 3.
- 꺼져 있으면 `cheat_play`는 Play만 시작하고 값은 건드리지 않는다. 응답의 override는 `active:false, reason:"disabled"`다.
- 설정은 컨트롤러가 직접 읽지 않는다. `cheat_play`가 표시할 때만 확인한다 → 복원 경로는 설정·심볼과 무관하게 돈다.

### 3. 컨트롤러 `CliPlayRunInBackground` (HB.Cheat.Editor, `[InitializeOnLoad]`, internal)
SessionState 키: `HB.Cheat.CliPlay.Owned`(bool), `HB.Cheat.CliPlay.Original`(bool), `HB.Cheat.CliPlay.Requested`(bool), `HB.Cheat.CliPlay.RequestFrame`(int).

| 시점 | 동작 |
|---|---|
| `cheat_play` 호출 (에디트 모드) | `Requested=true`. 이 시점의 `Application.runInBackground`를 "원래 값"으로 읽는다 (사용자 결정 2-6). **원래 값이 true면** 아무것도 쓰지 않고 `Owned=false`, 응답은 `active:false, reason:"already_true"`. **false면** `Original=false`, `Owned=true`로 기록한 뒤 `Application.runInBackground=true`로 쓰고 `isPlaying=true`. |
| `ExitingPlayMode` | `Owned`이면 원래 값을 다시 적용한다. 소유권은 유지한다 (종료 중 다른 쓰기 대비, uLoop 구조). |
| `EnteredEditMode` | `Owned`이면 원래 값을 적용하고 키를 모두 지운다. Pipeline 드라이버의 OnDestroy 복원이 ExitingPlayMode 뒤에 오더라도 이 단계가 마지막에 쓴다. |
| 정적 생성자 (도메인 리로드·에디터 시작) | 이벤트를 다시 구독한다. `Owned`이고 `!EditorApplication.isPlayingOrWillChangePlaymode`면 → 원래 값을 적용하고 키를 지운다 (Play 중 재컴파일 뒤 에디트 모드로 돌아온 경우, 종료 경로를 놓친 경우). Play 중이면 그대로 둔다. |
| Play 진입 실패 (컴파일 에러 등으로 isPlaying이 되지 않음) | `Requested` 뒤 일정 프레임(`RequestFrame` 기준, 예: 에디터 update 300회) 안에 `EnteredPlayMode`가 오지 않고 에디트 모드면 → 복원하고 키를 지운다. |
| `EditorApplication.quitting` | `Owned`이면 원래 값을 적용한다 (켜진 채 저장·종료되는 것을 막음. 효과는 실제 확인 5행에서 본다). |
| 에디터 강제 종료 | SessionState가 없어진다. 다음 시작 때 메모리 값은 디스크에서 다시 읽힌다 (추정). 디스크에 쓰지 않았다면 남는 것이 없다 → 실제 확인 5행 |

- 디스크 쓰기는 하지 않는다: `AssetDatabase.SaveAssets`, `PlayerSettings` setter, `SaveProjectSettings`를 부르지 않는다. 쓰는 것은 `Application.runInBackground` 하나다.
- 로직은 접근자(get/set runInBackground, 키 저장소, isPlaying)를 주입받는 순수 클래스로 나누고, `[InitializeOnLoad]` 껍데기만 Unity API에 붙인다 → EditMode 테스트가 가능하다.
- 정적 상태: Unity 6.5+ 자동 정적 정리 규칙은 기존 코드(`CheatCodeGenerator.cs:1,48`)와 같은 방식을 따른다. 소유 상태는 static 필드가 아니라 SessionState에 두므로, 도메인 리로드를 끈 경우에도 Play 사이에 static 잔류가 없다.

### 4. 응답 JSON (질문 4)
- `cheat_play` 성공: `{"ok":true,"playMode":"entering","runInBackgroundOverride":{"active":true,"original":false}}`
  - 원래 true: `{"active":false,"original":true,"reason":"already_true"}`, 설정 꺼짐: `{"active":false,"reason":"disabled"}`
- `cheat_stop` 성공: `{"ok":true,"playMode":"exiting"}`. 복원은 에디트 모드에 들어간 뒤 일어나므로 응답 시점에는 아직 켜져 있을 수 있다.
- 다른 `cheat_*` 응답에 넣을지(예: `cheat_status`에 현재 override 상태)는 질문 4.
- 새 code (질문 5): `already_playing`(cheat_play를 Play 중에 호출), `not_playing`(cheat_stop을 에디트 모드에서 호출), `play_mode_unavailable`(컴파일 중 등으로 Play에 들어갈 수 없음). 6장 원칙대로 요청 문제이므로 `ok:false` + code.
  - 계약 테스트(`CheatErrorCodeTests`)는 "모든 계약 code가 나온다"를 검사한다. 새 code는 CLI 어셈블리에서만 나오므로, 판정 함수를 `HB.Cheat.Editor`에 두고 테스트에서 직접 부른다.

### 5. 규칙서 문안 (`@orch project-files/CLAUDE.md`, "Unity 테스트" 절 아래 새 하위 절. 이 세션에서는 고치지 않음)
```markdown
### Play 모드 자동화

* Play는 `unity command cheat_play`로 시작하고 `cheat_stop`으로 끝낸다 (HB_Cheat 설치 프로젝트).
  이 명령으로 시작한 Play 동안에만 `Application.runInBackground`가 켜지고, 에디트 모드로 돌아오면 원래 값으로 되돌아간다.
  디스크의 프로젝트 설정은 바뀌지 않는다. 원래 값이 이미 true면 아무것도 하지 않는다.
* `editor_play`로 시작하면 runInBackground가 꺼진 프로젝트에서는 Unity 창이 OS 맨 앞에 없을 때 Play가 1~2 프레임에서 멈출 수 있다.
  * `editor_focus`는 성공을 돌려줘도 OS 창이 앞에 오지 않을 수 있다 (7장 관찰, Windows).
  * `set_autotick`으로는 Play 프레임 정지가 풀리지 않았다 (7장 관찰).
* Play 직후 프레임이 진행되는지 한 번 확인한다 (예: 1초 간격으로 frame 두 번 읽기). 진행되지 않으면 다음 단계로 넘어가지 말고 멈추고 보고한다.
```

### 6. README
- "🤖 자동화 (Unity CLI)" 절에 `cheat_play`/`cheat_stop`, 설정, 응답 필드, code 세 개를 추가한다. 패널 동작은 바꾸지 않는다.
- package.json 0.1.5 → 0.1.6.

## 버린 대안과 이유
- **(b) `editor_play` + 다른 신호:** 공개 훅이 없다(확정). 남는 신호는 모두 간접적이다.
  - Play 진입 시 `InternalEditorUtility.isApplicationActive`: 창 활성 상태일 뿐 "CLI가 시작했다"는 신호가 아니다. 자동화가 창이 앞에 있을 때 Play를 시작한 뒤 사람이 다른 창으로 옮기면 놓친다. "포커스 없는 Play면 켠다"는 규칙은 사람 Play와 자동화 Play를 구분하지 못한다.
  - `PipelineTransactionLog` 파일 읽기: internal 형식이고, 응답 뒤에 기록되는지 앞에 기록되는지 보장이 없다. 패키지 버전에 묶인다.
- **(c) 사전 무장 명령 (`cheat_bg_arm` → `editor_play`):** 구분은 확실하다. 하지만 명령이 두 번이 되고, 무장한 뒤 사람이 먼저 Play를 누르면 그 Play가 켜진다(만료 시간으로 줄일 수는 있다). (a)가 같은 확실성을 한 번 호출로 준다.
- **Play 동안 내내가 아니라 7장처럼 세션 내내 켜 두기:** 사람 Play에도 영향을 준다 → 원칙 위반.
- **`PlayerSettings.runInBackground` setter로 쓰기:** 디스크 저장 경로와 가까워진다. `Application` 쪽만 쓴다.

## 위험
- **켠 채로 저장:** 덮기 중 다른 코드(사용자 도구 포함)가 `AssetDatabase.SaveAssets`나 프로젝트 설정 저장을 부르면 `ProjectSettings.asset`에 1이 쓰일 수 있다 (추정, 7장에서 확인되지 않음). 실제 확인 9행에서 본다. 쓰인다면 이 설계의 전제가 깨지므로 멈추고 보고한다.
- **Pipeline 드라이버를 켠 프로젝트:** 순서상 우리가 EnteredEditMode에서 마지막에 쓰므로 최종 값은 맞을 것이다 (추정). 그러나 Play 중에는 두 쪽이 모두 true로 켠다. 원래 값 기록은 서로 다르다(우리: 에디트 모드 값 / 드라이버: 우리가 켠 뒤의 true). 드라이버의 복원은 true로 되돌린다. → 질문 1
- **도메인 리로드 끔:** SessionState를 쓰므로 static 잔류 문제는 없을 것이다. 다만 `[InitializeOnLoad]` 정적 생성자가 Play 진입 때 다시 돌지 않으므로, 구독이 Play 사이에 한 번만 유지되는지 확인한다 (실제 확인 8행).
- **Play 중 재컴파일:** Unity 설정(Script Changes While Playing)에 따라 Play가 멈추거나 계속된다. 어느 쪽이든 정적 생성자에서 상태를 다시 판단한다. Play가 계속되면 소유권도 유지된다.
- **사용자가 Play 중 직접 runInBackground를 바꿈:** 종료 때 원래 값으로 덮인다. 의도된 동작으로 README에 적는다.

## 테스트 (EditMode, `--filter HB.Cheat`)
컨트롤러 로직(주입형)에 대한 테스트:
1. 원래 false + 자동화 → 켜짐 → ExitingPlayMode 원래 값 재적용 → EnteredEditMode 복원, 키 없음
2. 원래 true → 쓰기 0회, 키 없음, `reason:"already_true"`
3. 설정 꺼짐 → 쓰기 0회
4. 표시 없음(사람 Play) → 상태 전환 이벤트가 와도 쓰기 0회
5. 정적 생성자 복구: `Owned`가 남은 채 에디트 모드 → 복원 후 키 없음 / Play 중 → 유지
6. Play 진입 실패: 프레임 한도 안에 EnteredPlayMode가 없음 → 복원
7. ExitingPlayMode 뒤 다른 쓰기(true)가 와도 EnteredEditMode에서 원래 값이 된다
8. quitting → 원래 값 적용
9. code 계약: `already_playing`·`not_playing`·`play_mode_unavailable`이 판정 함수에서 나오고, 계약 목록에 들어 있다
- 기존 HB.Cheat 테스트 전부 통과 (회귀 없음)

## 실제 확인 (통과 기준은 실행 전에 고정한다. 결과를 보고 바꾸지 않는다)
장소: TTESTT (원래 값 0). TTESTT를 쓸 수 없으면 SampleCheck. ToolCreate는 쓰지 않는다.
시작 전: `git status --porcelain`, `ProjectSettings/ProjectSettings.asset`·`EditorSettings.asset` SHA-256과 runInBackground 줄을 파일로 저장한다.
세 값은 `Application.runInBackground` / `PlayerSettings.runInBackground` / `ProjectSettings.asset`의 runInBackground 줄이다.

| # | 시나리오 | 통과 기준 |
|---|---|---|
| 1 | 다른 창을 맨 앞에 둔 채(사람 조작: 창 배치) `cheat_play` → 1초 간격 `boid.status` 두 번 → `cheat_stop` | frame이 늘어난다. Play 중 Application·PlayerSettings는 True, 디스크 0. 에디트 모드로 돌아온 뒤 세 값 False/False/0 |
| 2 | 사람이 툴바로 Play → 정지 (사람 조작) | Play 중·종료 후 세 값 False/False/0. HB_Cheat 쓰기 로그 없음 |
| 3 | `cheat_play` 중 스크립트 수정 → 재컴파일 | 재컴파일 뒤 Play가 계속되면 True, 끝난 뒤 False/False/0. Play가 멈추면 에디트 모드에서 False/False/0 |
| 4 | `cheat_play` 중 예외를 던지는 치트 실행 → `cheat_stop` | 종료 후 False/False/0 |
| 5 | `cheat_play` 중 에디터 정상 종료(`unity close`) → 다시 열기. 강제 종료는 질문 6 | 다시 연 뒤 False/False/0, `ProjectSettings.asset` 해시가 시작 전과 같다 |
| 6 | Play 전에 `eval`로 메모리 값만 true로 바꿈(시험 준비, 따로 기록) → `cheat_play` → `cheat_stop` → `eval`로 false 복구 | 응답 `active:false, reason:"already_true"`. Play 내내 True. HB_Cheat 쓰기·복원 로그 없음. 디스크 0 |
| 7 | 설정 끔 → `cheat_play` (다른 창 맨 앞) | 응답 `reason:"disabled"`. 세 값 Play 중에도 False/False/0 (프레임 정지는 기존 동작과 같음) |
| 8 | Enter Play Mode Options에서 도메인 리로드 끔으로 바꿔 1·3·6행 다시 → 끝나면 옵션 되돌림 | 각 행 기준과 같다. `EditorSettings.asset` 해시가 시작 전과 같다 |
| 9 | `cheat_play` 중 `eval`로 `AssetDatabase.SaveAssets()` 호출 → 디스크 확인 → `cheat_stop` | 디스크 0 그대로. 1이 쓰이면 **멈추고 보고** |
| 10 | 끝 | `git status --porcelain`이 시작 전과 같다 (줄바꿈만 다른 파일은 "내용 diff 0"으로 따로 표시) |

- HB_Cheat 쓰기·복원 로그: 컨트롤러가 켜고 되돌릴 때 `[HB_Cheat] runInBackground ...` 한 줄씩 남긴다. 6행의 "아무것도 하지 않음"은 이 로그가 없는 것과 응답으로 판정한다.
- review: 구현 → 컴파일 → EditMode 테스트 뒤, 실제 확인 전에 `review`(reason·ownReview)를 부른다. 의심 지점은 input에 넣지 않는다.

## 사용자에게 물을 것
1. **Pipeline 런타임 드라이버를 켠 프로젝트** (`RuntimePipelineConfig.json` + `enableInBuilds` + `autoStart`): 그 프로젝트에서는 에디터 Play에서도 Pipeline이 Play 내내 true로 켠다 (사용자 결정 2-6의 "멈추고 보고" 갈래에 해당하지만, 그 설정을 켠 프로젝트에서만이다). 확인 장소 두 곳과 ToolCreate에는 해당하지 않는다.
   - (가) 그대로 진행한다. 위 순서상 최종 값은 우리가 맞춘다. 드라이버가 활성이면 응답에 `pipelineRuntimeDriver:true`를 표시하고, README에 "이 경우 이 기능은 필요 없다"고 적는다.
   - (나) 드라이버가 활성이면 HB_Cheat는 아무것도 하지 않는다 (`reason:"pipeline_driver"`). 소유자가 하나가 된다.
   - (다) 이 경우는 이번 범위 밖으로 두고 README에 제한으로만 적는다.
   - 권장: (나). 소유자를 하나로 두는 쪽이 "강제하지 않는다" 원칙과 맞고, 복원 순서를 시험할 필요가 없다.
2. 구분 방법은 (a) `cheat_play`/`cheat_stop`으로 할까? (권장. 대안 비교는 "버린 대안" 절)
3. 설정 기본값을 켬으로 할까, 끔으로 할까? `cheat_play`는 자동화가 명시적으로 부르는 새 명령이라, 켬이어도 기존 동작(`editor_play`, 사람 Play)은 바뀌지 않는다. 권장: **켬**. 4장 언어 검사, 6장 raw 차단 같은 "의도적 예외"와는 성격이 다르다 (끔이 안전한 기본값인 경우가 아니다).
4. 응답 JSON: `cheat_play` 응답에 `runInBackgroundOverride: {active, original, reason}`를 넣을까? 다른 `cheat_*` 응답(예: `cheat_status`)에도 현재 상태를 넣을까? 권장: `cheat_play`에만.
5. 새 code 세 개(`already_playing`, `not_playing`, `play_mode_unavailable`)를 이 이름으로 계약에 넣을까? (계약은 이름을 바꾸지 않는다)
6. 실제 확인 5행에서 에디터 **강제 종료**(프로세스 kill)도 할까? Library·열린 씬에 영향이 있을 수 있다. 권장: 정상 종료만 하고 강제 종료는 테스트 5(정적 생성자 복구)로 대신한다.
7. 규칙서 문안(계획 5절)을 이대로 둘까?

## 결정

사용자 답 원문:

> 계획 좋다. 답과 수정 요청이야.
>
> [질문 답]
> 1. (나)로 한다. 드라이버가 활성이면 HB_Cheat는 아무것도 하지 않는다(reason:"pipeline_driver").
>    - 판정 방법을 계획서에 적어 줘. cheat_play 시점에는 드라이버가 아직 없다(BeforeSceneLoad). 그래서 설정 파일로 판단한다.
>      - RuntimePipelineConfig.json이 없음 → 비활성(코드 근거로 확정) → 진행
>      - 있음 → enableInBuilds·autoStart를 읽어 판단
>      - 파일은 있는데 읽기 실패 → 아무것도 하지 않고 reason:"pipeline_config_unreadable"
>    - Pipeline internal API에 기대지 말고 파일만 읽는다. 패키지 버전이 바뀌어도 깨지지 않게 하기 위해서다. 형식은 추정이니 README에 "Pipeline x.y 기준"으로 적는다.
>    - 테스트에 이 판정 세 갈래를 추가한다.
> 2. (a) cheat_play / cheat_stop으로 한다.
> 3. 기본값은 켬. 근거가 맞다. 새 명령이라 기존 동작이 바뀌지 않는다.
> 4. cheat_play 응답에만 넣는다.
> 5. 세 이름 그대로 계약에 넣는다.
> 6. 정상 종료만 한다. 강제 종료는 테스트 5로 대신한다.
> 7. 규칙서 문안은 아래 수정을 반영하면 된다.
>
> [수정 요청]
> A. 기준선을 통과 기준으로 고정하고 순서를 앞으로 옮긴다.
>    - 7행(설정 끔, 다른 창 맨 앞)을 1행보다 먼저 한다. 통과 기준: frame이 늘지 않는다(정지 재현). 세 값은 False/False/0.
>    - 정지가 재현되지 않으면 1행으로 넘어가지 말고 멈추고 보고한다. 재현이 안 되면 1행의 "frame이 늘어난다"를 기능 효과로 볼 수 없다.
>    - editor_focus만 부른 뒤에도 정지가 이어지는지 한 번 같이 본다(7장 관찰 재현).
>
> B. 확정과 추정을 나눈다.
>    - 배경 14줄 "원인은 OS 창 포커스로 확정" →
>      "현상(창이 뒤면 정지, 클릭하면 진행) 확정. 메커니즘(포커스 없을 때 플레이어 루프 감속) 추정. Unity 공식 문서 미확인. uLoop 주석이 같은 설명"
>    - FocusEditorCommand: "주석은 OS 창 활성화. 실제로는 앞으로 오지 않음(확정). 원인은 Windows 전경 전환 제한(추정)"
>    - 규칙서 문안 둘째 항목을 "멈출 수 있다(7장 관찰)"로 두고 원인은 쓰지 않는다.
>
> C. 규칙서 문안 추가
>    - "cheat_play 응답의 runInBackgroundOverride.active가 false면(already_true / disabled / pipeline_driver …) 이유를 기록한다. 프레임 진행 확인은 그대로 한다."
>
> 이대로 진행해. review는 계획대로 구현·테스트 뒤, 실제 확인 전에 부른다.

### 이 답으로 바뀐 계획 항목
- **Pipeline 드라이버 판정 (질문 1 → (나))**: `cheat_play` 시점에 `<프로젝트>/ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`만 읽는다 (Pipeline API를 쓰지 않음).
  - 파일 없음 → 드라이버가 생기지 않음 (확정: `RuntimePipelineConfig.Load()`가 null → `Bootstrap`이 드라이버를 만들지 않음) → 진행
  - 있음 → `JsonUtility.FromJsonOverwrite`로 `enableInBuilds`(기본 false)·`autoStart`(기본 true)를 읽는다. 기본값은 Pipeline 클래스 선언과 같게 둔다 (0.8.0-exp.1 기준).
    - `enableInBuilds == false` → 진행 (드라이버 없음, 확정)
    - `enableInBuilds == true` → `reason:"pipeline_driver"`, 아무것도 하지 않음. `autoStart`가 false여도 같다. 드라이버는 `enableInBuilds`만으로 만들어지고, 코드 리로드 푸시를 받으면 autoStart와 무관하게 runInBackground를 켜기 때문이다 (확정: `RuntimePipelineDriver.cs:469-477`). 응답에는 읽은 두 값을 같이 넣는다.
  - 파일은 있는데 읽기 실패(IO·JSON 예외) → `reason:"pipeline_config_unreadable"`, 아무것도 하지 않음. 참고(확정): 이 경우 Pipeline 자신은 경고만 남기고 config를 null로 보므로 드라이버를 만들지 않는다. HB_Cheat는 보수적으로 아무것도 하지 않는다.
  - README에 "Pipeline 0.8.0-exp.1 기준 파일 형식(추정)"으로 적는다.
  - 테스트 10 추가: 판정 세 갈래(없음 / 있음·enableInBuilds true·false / 읽기 실패)
- **기본값 켬, `cheat_play` 응답에만 override, code 세 개 이름 그대로, 강제 종료 시험 안 함 (질문 2~6)**: 계획대로.
- **실제 확인 순서 (수정 A)**: 맨 처음에 **0행**(구 7행)을 한다. 설정 끔 + 다른 창 맨 앞 + `cheat_play` → 1초 간격 `boid.status` 두 번. 통과 기준은 **frame이 늘지 않음(정지 재현)**, 세 값 False/False/0이다. 이어서 같은 Play에서 `editor_focus`만 부르고 frame을 다시 두 번 읽어 정지가 이어지는지 본다 (7장 관찰 재현, 기록용). **정지가 재현되지 않으면 1행으로 넘어가지 않고 멈추고 보고한다.** 7행은 0행으로 옮겨져 없어진다.
- **배경 표현 (수정 B)**: 계획 본문은 고치지 않고 여기서 바로잡는다.
  - 배경 "원인은 OS 창 포커스로 확정했다" → 현상(창이 뒤면 정지, 클릭하면 진행)은 확정. 메커니즘(포커스 없을 때 플레이어 루프 감속)은 추정. Unity 공식 문서로는 확인하지 못했고, uLoop 주석이 같은 설명을 한다.
  - FocusEditorCommand → 주석은 OS 창 활성화. 실제로는 앞으로 오지 않았다(확정). 원인은 Windows 전경 전환 제한(추정).
- **규칙서 문안 (질문 7, 수정 B·C)** 최종:
```markdown
### Play 모드 자동화

* Play는 `unity command cheat_play`로 시작하고 `cheat_stop`으로 끝낸다 (HB_Cheat 설치 프로젝트).
  이 명령으로 시작한 Play 동안에만 `Application.runInBackground`가 켜지고, 에디트 모드로 돌아오면 원래 값으로 되돌아간다.
  디스크의 프로젝트 설정은 바뀌지 않는다. 원래 값이 이미 true면 아무것도 하지 않는다.
* `cheat_play` 응답의 `runInBackgroundOverride.active`가 false면(already_true / disabled / pipeline_driver …) 이유를 기록한다. 프레임 진행 확인은 그대로 한다.
* `editor_play`로 시작하면 runInBackground가 꺼진 프로젝트에서는 Unity 창이 OS 맨 앞에 없을 때 Play가 1~2 프레임에서 멈출 수 있다 (7장 관찰).
  * `editor_focus`는 성공을 돌려줘도 OS 창이 앞에 오지 않을 수 있다 (7장 관찰).
  * `set_autotick`으로는 Play 프레임 정지가 풀리지 않았다 (7장 관찰).
* Play 직후 프레임이 진행되는지 한 번 확인한다 (예: 1초 간격으로 frame 두 번 읽기). 진행되지 않으면 다음 단계로 넘어가지 말고 멈추고 보고한다.
```

## 결정 2 (실제 확인 중, 0행 재현 조건)

사용자 답 원문:

> 실사용 조건은 “Play Unfocused + runInBackground 기능 켬”으로 정했습니다. 지금 진행하는 1행이 이 조합의 확인을 겸합니다.
>
> 이번 A에서는 드롭다운을 사람이 직접 바꿉니다. 지휘자는 값을 읽어 기록만 합니다.
> 규칙서 문안 후보에 아래를 넣어 주세요. 반영은 A 마무리 때 함께 정합니다.
>
> AI 실행 전 Game 뷰 Play 모드 드롭다운 값을 읽어 기록한다. Unfocused가 아니면 사람에게 바꿔 달라고 요청한다. 지휘자가 직접 바꾸지 않는다.
>
> todo에 추가해 주세요. A가 커밋된 뒤에 할 작업이고, A 범위에는 넣지 않습니다.
>
> cheat_play가 Play 동안만 Play Unfocused로 바꾸고 끝나면 되돌리는 옵션 (기본 끔).
>
> 먼저 조사할 것: 이 값을 바꾸는 API가 공개 API인지 내부 API인지, 값이 레이아웃 파일·UserSettings·EditorPrefs 중 어디에 저장되는지.
> runInBackground와 같은 원칙을 지킨다: 사람이 누른 Play는 건드리지 않는다. 어느 경로로 끝나도 되돌린다. 디스크에 흔적을 남기지 않는다.
> 이번 A 시험에서 사람이 드롭다운을 바꾼 기록(바꾼 시각, 해시 비교 결과)을 그 작업의 기준 자료로 쓴다.

### 이 답으로 바뀐 계획 항목
- 실제 확인 0행·1행의 조건: Game 뷰 드롭다운 **Play Unfocused**(사람이 바꿈) + 사람이 다른 모니터의 메모장에서 계속 타이핑. 1행은 실사용 조합(Play Unfocused + 기능 켬) 확인을 겸한다.
- 드롭다운은 사람이 바꾸고 지휘자는 바꾼 시각과 전후 해시(`TTESTT/UserSettings/`, `ProjectSettings/`, EditorPrefs 레지스트리 내보내기)를 기록만 한다. 끝나면 사람이 Play Focused로 되돌리고 같은 비교를 한다.
- 규칙서 문안 후보 (A 마무리 때 반영 여부 결정): "AI 실행 전 Game 뷰 Play 모드 드롭다운 값을 읽어 기록한다. Unfocused가 아니면 사람에게 바꿔 달라고 요청한다. 지휘자가 직접 바꾸지 않는다."
- A 범위 밖 todo: `docs/todo.md`에 추가.
- 규칙서 문안 후보 추가 (사용자, 실제 확인 중): "창이 뒤에 있는 동안의 성능 수치는 앞에 있을 때와 다를 수 있다" (관측: cheat_play + 창 뒤 초당 약 129프레임, 창 앞 초당 600 안팎)
- 규칙서 문안 후보 추가 (사용자, 8행 사건 뒤): "에디터 설정을 시험 때문에 바꿀 때는 바꾸기 전에 디스크 값과 메모리 값을 둘 다 기록한다. API로 원래 표현을 되살릴 수 없는 설정도 있으니, 되돌리기는 git 기준으로 한다."
