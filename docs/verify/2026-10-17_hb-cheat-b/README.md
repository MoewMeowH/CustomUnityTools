# B 실제 확인 — HB_Cheat 남은 항목 11~15 (ToolTest6.3)

계획서: [docs/plans/2026-10-17_hb-cheat-remaining-checks.md](../../plans/2026-10-17_hb-cheat-remaining-checks.md)

## 0. 기준선 (확정)
- `00_tooltest_sha256_before.txt`: manifest·lock·`ProjectSettings/`·`UserSettings/` 39개 파일.
- 디파인 `00_tooltest_defines.txt` (Standalone `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY`), runInBackground 0, Enter Play Mode Options `1 / 0`.
- Game 뷰 드롭다운 (읽기만): 레이아웃 `UserSettings/Layouts/default-6000.dwlt:1565`, `CurrentMaximizeLayout.dwlt:921` 모두 `m_EnterPlayModeBehavior: 0` (0 = Play Focused로 추정).
- `Library/`는 기준선에 넣지 않았다 (사용자 결정 1-1: 생성 폴더 제외).

## 사건: UPM git URL은 짧은 해시를 받지 않는다 (확정 관측)
- manifest에 `...?path=Assets/Tools/HB_Cheat#869e2614` → `unity open` 중 에러 (사용자 보고 원문):
  > com.hb.cheat: Could not clone [https://github.com/MoewMeowH/CustomUnityTools.git]. Make sure [869e2614] is a valid branch name, tag or full commit hash on the remote registry.
- 원격 확인: `git ls-remote origin` → `refs/heads/main 43f1f105…`, `git branch -r --contains 869e2614…` → `origin/main` (커밋은 원격에 있음).
- 에러 뒤 기준선 비교: manifest만 다름(지휘자가 넣은 줄). lock·`ProjectSettings/`·`UserSettings/`는 기준선과 같음. lock에 com.hb.cheat 없음. `Library/PackageCache/com.hb.cheat*` 없음 (`Library/`는 기준선 밖이라 그 밖의 변화는 모른다).
- 처리 (사용자 결정 2-1): 전체 해시 `#869e2614fca423e4d327eefed7abe954a765d772`로 바꿈. Retry는 사람이 누른다.
- README 설치 안내 후보 (반영은 사용자 결정): "git URL에 버전을 고정할 때는 전체 40자 커밋 해시나 태그를 쓴다. 짧은 해시는 UPM이 받지 않는다."

## 계획과 달라진 점: pipeline이 없어 CLI 대신 Editor.log와 ScriptAssemblies로 관측 (사용자 승인)
- ToolTest6.3에 com.unity.pipeline이 없어 `unity` CLI가 연결되지 않는다 (`STATUS_NO_INSTANCES`, 확정).
- 컴파일: Editor.log에서 **이번 실행 시작 위치부터** `error CS`·`Scripts have compiler errors` 수. Safe Mode 여부는 사람이 화면으로 확인.
  - 이번 실행 시작 위치: 바이트 오프셋 0. 근거: Unity는 실행마다 Editor.log를 새로 쓰고 이전 것을 Editor-prev.log로 옮긴다. 현재 Editor.log 13~15줄이 `-projectPath F:\AI\ToolTest6.3\ToolTest6.3`이다 (확정). 디파인 토글 구간은 토글 직전 크기를 오프셋으로 기록해 그 뒤만 센다.
- 어셈블리: `Library/ScriptAssemblies/HB.*.dll`의 있다/없다 + 수정 시각이 디파인 토글 시각보다 뒤인지 (이전 컴파일 결과와 구분).
- 15-1 판정은 `HB.Cheat.Editor.Cli.dll` 없음으로 한다. "CLI 연결 안 됨"은 pipeline이 없으면 기능과 무관하게 생기므로 판정 근거로 쓰지 않고 관측으로만 남긴다.
- 연동이 감지에 따라 켜지고 꺼진다는 것은 3단계에서 반대쪽(pipeline 추가 뒤 `HB.Cheat.Editor.Cli.dll` 생김, `cheat_*` 8개)까지 봐야 확정한다.

## 1. 11 + 15-(1)
- resolve: lock `com.hb.cheat` `source: git, hash 869e2614fca423e4d327eefed7abe954a765d772` (`11_lock_hbcheat.txt`).
- 디파인 켜기 전 (22:38 무렵, 관측): ScriptAssemblies에 `HB.Cheat.Editor.dll`만 (런타임·Generator·Cli·InGameConsole 연동 없음 — HB_CHEAT 꺼짐으로 예상대로). Editor.log(오프셋 0부터) `error CS` 0. Editor.log 크기 5,365,636B (22:38:04), import 진행 중.
- 22:39:34 import 끝남(관측: Editor.log 5초간 증가 없음). 에디터는 PID 8664 하나 (22:24:58 시작, Retry로 이어서 열림). 31192·35560은 AssetImportWorker. **디파인 토글 구간 시작 오프셋: 5,366,826B**. 이 시점 `error CS` 0, `Scripts have compiler errors` 0.

## 관측: 기존 결함 — ToolTest6.3의 InGameConsole(bfd6415a) 에셋이 Unity 7 직렬화 (범위 밖, 멈추고 보고)
- 사용자가 공유한 에러 (원문 일부): `[Worker0] Failed to load 'Packages/com.hb.ingameconsole/Resources/InGameConsoleSettings.asset'. File may be corrupted or was serialized with a newer version of Unity.` / 같은 내용 `HB_PanelSettings.asset`.
- 원인 (확정): `Library/PackageCache/com.hb.ingameconsole@d2ad937a31db/Resources/*.asset` 첫 줄 `%UNITY 7.0`. 작업 프로젝트 ToolCreate는 7000.0.0a8.
- 이미 수정됨 (확정): `5147845` "InGameConsole: 패키지 .asset 제거, PanelSettings 코드 생성". 설치된 `bfd6415a`(2026-10-15)는 그보다 앞. 현재 저장소 InGameConsole에 `%UNITY 7` 에셋 없음.
- HB_Cheat 무관 (확정): Editor.log 4811·4891줄(import 중, 디파인 토글 전). InGameConsole lock은 기준선과 같음. HB_Cheat 패키지에는 .cs/.asmdef/.json/.md/.meta 밖의 파일이 없다.
- 이번 실행 전부터 났는지: 확인 못 함 (추정: lock이 같으므로 전부터).
- 함께 나온 경고: `Account API did not become accessible within 30 seconds` (com.unity.ai.assistant, HB_Cheat 무관 추정).

## 1단계 결과 — **결함 발견, 멈춤** (12_define_toggle.txt)
- 사람 조작: 디파인 토글 (수신 22:43:28 "(가)로 진행, 디파인 켰어"). 토글 시각·Safe Mode·콘솔 상태는 보고에 없음.
- 디파인: 메모리에서 켜진 것으로 보임 (런타임·Generator dll이 생김). 디스크 `ProjectSettings.asset`은 아직 그대로 (Standalone `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY`, 수정 시각 2026-10-15 12:25).
- ScriptAssemblies (토글 뒤 생성): `HB.Cheat.dll` 22:43:22.295, `HB.Cheat.Editor.dll` 22:43:22.343, `HB.Cheat.Editor.Generator.dll` 22:43:22.344. `HB.Cheat.Editor.Cli.dll` 없음. `HB.Cheat.InGameConsole.dll` 없음 (컴파일 실패).
- Editor.log (오프셋 5,366,826B 뒤): `error CS` 12줄, 4종류 (같은 에러가 반복 기록), 모두 `HB.Cheat.InGameConsole`:
  ```
  Library\PackageCache\com.hb.cheat@e0d44ad8b766\Integration\InGameConsole\InGameConsoleCheatHost.cs(33,32): error CS0117: 'InGameConsole' does not contain a definition for 'AddExternalView'
  ...(39,45): error CS0117: 'InGameConsole' does not contain a definition for 'ShowExternalView'
  ...(43,45): error CS0117: 'InGameConsole' does not contain a definition for 'IsExternalViewActive'
  ...(45,47): error CS0117: 'InGameConsole' does not contain a definition for 'RemoveExternalView'
  ## Script Compilation Error for: Csc Library/Bee/artifacts/1900b0aEDbg.dag/HB.Cheat.InGameConsole.dll (+2 others)
  ```
- 판정: **11 불통과** (기준: 컴파일 에러 0). 15-1의 `HB.Cheat.Editor.Cli.dll` 없음은 기준대로지만, 같은 컴파일에서 에러가 나 판정을 보류한다.

### 결함 원인 (확정, 코드·이력)
- InGameConsole의 외부 탭 API(`AddExternalView`·`ShowExternalView`·`IsExternalViewActive`·`RemoveExternalView`)는 `c451c74`(2026-10-16, "HB_Cheat: … InGameConsole 외부 탭 A…")에서 추가됐다. 이때 InGameConsole `package.json` 버전은 1.0.0 그대로다 (`bfd6415a`와 HEAD 모두 1.0.0).
- `HB.Cheat.InGameConsole.asmdef` versionDefines: `com.hb.ingameconsole` 식 `1.0.0`(= 1.0.0 이상) → `HB_CHEAT_HAS_CONSOLE`.
- ToolTest6.3의 InGameConsole은 `bfd6415a`(2026-10-15, API 추가 전, 버전 1.0.0) → 감지 조건을 만족해 연동 어셈블리가 컴파일되지만 API가 없다.
- 즉 "API를 추가하면서 InGameConsole 버전을 올리지 않았고, HB_Cheat의 versionDefines 하한도 그 API가 생긴 버전을 가리키지 않는다". 같은 문제는 git URL을 고정하지 않은 사용자가 오래된 InGameConsole 캐시·lock을 갖고 있으면 생긴다 (추정).
- 고치지 않았다 (지시서 B 원칙).

### 기록 (사용자 지정 문구)
**0.1.6 회차: 11 본체 컴파일 통과, InGameConsole 연동 컴파일 실패(결함, 원인 확정), 15-1 판정 보류**

## 기준선과 달라지는 점: InGameConsole을 HEAD로 고정 (사용자 결정 B-2)
- ToolTest6.3 manifest `com.hb.ingameconsole` → `...?path=Assets/Tools/HB_InGameConsole#43f1f105a5893fc4bf3603e13879acfbae466413` (origin/main HEAD, 전체 해시). HEAD의 InGameConsole에는 `%UNITY 7` 에셋 없음, 버전 1.0.0.
- 정리 단계에서 manifest·lock을 기준선(`bfd6415a`)으로 되돌린다. 디파인은 켜 둔 채.

## 1단계 재판정 (InGameConsole HEAD, 13_rejudge_11.txt) — **11 통과, 15-1 통과**
- 22:45:55 이후 `error CS` 0, `Scripts have compiler errors` 0, Unity 7 에셋 로드 실패 0. lock InGameConsole `43f1f105…`.
- `HB.Cheat.InGameConsole.dll` 22:50:37 생김 (이번 컴파일). `HB.Cheat`·`HB.Cheat.Editor`·`HB.Cheat.Editor.Generator` dll 22:43:22 (디파인 토글 컴파일에서 생김, 22:38엔 없었음. 재컴파일에서 입력이 같아 다시 빌드되지 않음).
- `HB.Cheat.Editor.Cli.dll` 없음 → 15-1 통과.
- Safe Mode: 사람 화면 보고 없음. 컴파일 에러 0을 근거로 판정.
- 사람 조작: InGameConsole 반영을 위해 Unity 창 클릭 (Install 67/67 대기 보고), 컴파일 완료 22:50:37.
- 순서 변경 (사용자 지시): 2단계(샘플 Import)를 3단계(pipeline) 뒤로.

## 3단계 — pipeline 추가
- 22:51:21 manifest에 `"com.unity.pipeline": "0.8.0-exp.1"` 추가. 로그 오프셋 5583691B.
- 22:55:55 사람 조작: "허브 키니까 설치된다." — pipeline 추가 뒤 Resolving Packages가 3분 이상 멈춤. upm.log에 22:51 뒤 resolve 요청 없음, Editor.log에 `IPCStream (hubIPCService): IPC stream failed to write (Timed out)` 반복. Unity Hub를 켜자 진행 (확정 관측: Hub가 꺼져 있으면 패키지 추가 resolve가 멈춘다).
- 3단계 결과 (`14_pipeline.txt`) — **통과**: lock `com.unity.pipeline 0.8.0-exp.1 (registry)`. 오프셋 5,583,691B 뒤 `error CS` 0. `HB.Cheat.Editor.Cli.dll` 22:55:42 생김 (1단계엔 없음). `cheat_*` 8개 (history, list, options, play, raw, run, status, stop). `cheat_list` 원문 `{"ok":true,"cheats":[]}`.
- **15 pipeline 연동 확정**: pipeline 없음 → `HB.Cheat.Editor.Cli.dll` 없음 (1단계), 있음 → 생김·명령 8개 (3단계).
- 디파인: 메모리 `APP_UI_EDITOR_ONLY;DEV_CHEAT;HB_CHEAT;SENTIS_ANALYTICS_ENABLED`, 디스크 `ProjectSettings.asset`은 아직 `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY` (저장 전).

## 2단계 — Samples Import (순서 변경으로 3단계 뒤)
- 2단계 결과 (`15_samples.txt`) — **통과**: 사람 조작 "Import 눌렀어, 컴파일 끝났어" (수신 22:57:54). `Assets/Samples/HB_Cheat/0.1.6/Cheat Examples/` .cs 7개 + README. `error CS` 0. `[HB_Cheat] Generated Assets/Cheats/Generated/HBCheatRegistry.g.cs (17 cheats)`. 에디트 모드 `cheat_list`는 0개 — 생성 레지스트리가 `#if HB_CHEAT && DEV_CHEAT` + `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`로 Play 때 등록 (코드 확인, 의도된 동작). `cheat_play` 뒤 `cheat_list` 17개 (account·actor·item·system). 샘플 보호: 치트 6개 `#if DEV_CHEAT`, `ExampleGameData.cs`만 없음 (사전 조사와 같음).

## 15-3 준비: InGameConsole을 쓰는 프로젝트 파일 옮김 (사용자 결정 (다))
- 검색 결과 (Assets 전체): `Assets/Scripts/LogChannels.cs`(HLogCore·HLog), `Assets/sdf.cs`(HLog), `Assets/Resources/HB_InGameConsoleUserSettings.asset`(InGameConsole 스크립트 guid). asmdef/asmref 참조 없음.
- `.meta`와 함께 `F:/AI/ToolTest6.3/_B_moved/Assets/...`로 이동 (지우지 않음). 이동 전후 SHA-256 같음 (`16_moved_before.txt`/`16_moved_after.txt`). 정리 단계에서 되돌리지 않는다 (사용자 결정).
- 이동 뒤 `AssetDatabase.Refresh` → `error CS` 0, `compilationFailed:false`.

## 15-3 결과 — **꺼짐 통과, 켜짐 통과** (`17_icon_off.txt`, `18_icon_on.txt`)
- 꺼짐 (23:01:48 manifest에서 InGameConsole 줄 제거 → CLI `Client.Resolve()`): lock 항목 0, `error CS` 0, `compilationFailed:false`. `HB.Cheat.InGameConsole.dll`·`HB.InGameConsole*.dll` 파일 없음, 메모리에 로드 안 됨. 남은 HB dll은 22:55:42 것 (Cli·Editor·Generator·Cheat·ScreenRecorder).
- 켜짐 (23:02:31 HEAD 고정 줄 복원 → Resolve): lock `43f1f105…`, `error CS` 0. `HB.Cheat.InGameConsole.dll` 23:02:40 생김(이번 컴파일), 로드됨. `HB.InGameConsole.dll`·`.Editor.dll` 23:02:40.
- **15 InGameConsole 연동 확정** (두 상태 모두 봄).
- 관측: InGameConsole을 다시 넣어도 HLog 생성기가 `LogChannels.cs`를 다시 만들지 않았다. `Assets/Resources/`도 비어 있음 (설정 에셋 재생성 없음).
- 사람 조작 없이 CLI로 진행.

## 12 — 서버 큐 멈춤 UI (`20_row12_trace.txt`)
- 12-0 사람 조작: 드롭다운 Play Unfocused ("Unfocused로 바꿨어", 23:05:11).
- 12-1 지휘자 — **통과**: `cheat_play` (`active:true, original:false`) → eval `ServerTimeoutSeconds=0.5`, `ServerTransport=CheatLogTransport` → `cheat_run account.currency.reset` (#1), `item.clear` (#2) → #1 `TimedOut` "No server response in 0.5s. Server queue paused", #2 `Pending`·`blocked:true`·"Paused: #1 timed out. Waiting for the late response or ResumeServerQueue", `serverQueue.paused:true reason "#1 timed out" queued 1`. 세션 `9e35b182`.
- 스크린샷 (관찰 보조) `20_row12_1_game.png`: 카메라 렌더만 담겼다 (하늘·바닥, UI 없음). `screenshot` 명령은 UI Toolkit 오버레이를 담지 않는 것으로 보임(추정) → UI 판정에 못 씀. 찍기 전후 맨 앞 창 chrome (포커스 변화 없음).
- 12-2 사람 조작 — **통과**: F8로 열었고 InGameConsole 탭으로 표시. 원문: "1번 확인됬어 resume 도 여기 같이 있어. / pending... CLI Item/Clear Inventory 도 있네 / 다른 한개 더 Account/Currency/Reset Timeout 도 하나있어", 보충 원문: "1. Paused: #1 timed out. Waithing for the late response or ResumeServerQueue" (JSON 원문은 "Waiting"). ① 안내 ② Resume ③ "Paused: …" 모두 보임.
- 12-3 사람 조작: Resume 클릭 ("resume 눌렀어, 지금", 23:09:28).
- 12-4 — **통과**: #2 `TimedOut` "No server response in 0.5s. Server queue paused" (전송 뒤 시간 초과), `serverQueue.paused:true reason "#2 timed out" queued 0`. **12 통과.**

## 13 — 실제 마우스 클릭·드래그
- 관측 (확정, 설계된 동작): `PreferConsoleTab`(기본 켬)이고 InGameConsole이 있으면 패널을 콘솔 탭에 붙이고(`CheatSystem.cs:192`) 독립 패널·CHEAT 버튼을 만들지 않는다. 실행 중 CheatRunner `m_Standalone == null` (eval 리플렉션).
- README "InGameConsole 연동" 절에 이 조건(CHEAT 버튼 없음, Prefer Console Tab으로 독립 패널 선택)은 없다 → `docs/todo.md`에 문서 보강 추가.
- 사용자 결정: 13-A 콘솔 탭 모드에서 치트 버튼 클릭, 13-B Prefer Console Tab 끄고 재시작 뒤 CHEAT 버튼 클릭·드래그 (짧게·길게·화면 끝). 설정 에셋 변화는 남기고 경로·내용 기록. 반영 여부(m_Standalone 생김)도 판정. 터치는 미확인 → 14 순서표.
- 13-A (콘솔 탭 모드) — **통과**: 사람 조작 "System/Reload Tables 눌렀어, 지금" → `cheat_history` #3 `system.reloadtables` `source: Panel` `Succeeded` "ItemTable v2" (`21_row13_trace.txt`).
- B1 사람 조작: "세팅 에셋이 없어서 생성했고 설정 off 했어" (Play 중). 생성된 에셋 `Assets/Editor/HBCheat/HBCheatSettings.asset` (23:13:47).
- Play 중 미반영은 정상으로 본다 (사용자 판단). Play 종료 뒤 관측 (사용자 고정 기준): `cheat_stop` 23:16:18 → 손대지 않고 30초 → 설정 디스크 `preferConsoleTab: 1`·수정 시각 23:13:47 (저장 안 됨), 메모리 False. 레지스트리 22:57:24 그대로, 15줄 `options.PreferConsoleTab = true;`. 컴파일·리로드 0회.
- **판정: 사용성 결함** (30초 안에 바뀌지 않음). 고치지 않음. `docs/todo.md`에 후보 2개(설정 변경 시 재생성 / 설정 창·README 안내)와 설정 에셋 미저장을 기록.
- 사람 조작: "Generate Registry 눌렀어, 지금" (23:17:45). 레지스트리 23:17:25 재생성, 15줄 `options.PreferConsoleTab = false;`. 설정 에셋 디스크는 여전히 `preferConsoleTab: 1`(23:13:47, 저장 안 됨) — 레지스트리는 메모리 값으로 생성됨 (관측).
- B2 — **통과**: `cheat_play` 뒤 런타임 `PreferConsoleTab=False ShowFloatingButton=True`, `m_Standalone` 생김, CHEAT 버튼 `display=Flex left=1192 top=252 76x44`, 패널 1280x720.
- B3 사람 조작 원문: "1, 2번 정상작동 됬어 3번은 가려져서 안보여". 판정: **클릭으로 열림 — 통과**. "CHEAT 버튼으로 닫힘" — **해당 없음 (설계된 동작)**: `Open()`이 버튼을 `display=None`으로 숨기고 `Close()`에서 다시 보인다 (`CheatRunner.cs:256-270`). eval: `IsOpen=True`, 버튼 `display=None`. 기준은 바꾸지 않고 해당 없음으로 기록.
- todo 설정 결함에 위험 추가 (사용자): 디스크 미저장 → 재시작 뒤 재생성하면 원래대로 돌아갈 수 있음 (추정).
- B3 보충 사람 조작: "f8 과 close 버튼으로 각각 2번씩 테스트 해봤어 작동되" → 닫기 F8·Close 버튼 각 2회 동작. 닫은 뒤 eval: "result": "IsOpen=False | button left=1192 top=252 display=Flex"
- B4 짧게 드래그 — **통과**: 사람 조작 "짧게 드래그 했어, 따라 움직이고 놓은 자리에 남았어 클릭시 판도 열린다" / 확인 "b가 맞아" (드래그 뒤 따로 클릭하면 열림, 놓는 순간 열리지 않음). 좌표 1192,252 → 1173.3,224.7 (약 33px), 닫힘 상태.
- B5 길게 드래그 — 사람 조작 "B5 도 잘 작동해". 읽은 값 23:22:08: "result": "IsOpen=False | button left=18.66667 top=665.3333 display=Flex"
- B5 — **통과**: 1173.3,224.7 → 18.7,665.3 (왼쪽 아래), 닫힘 상태.
- B6 화면 끝 밖으로 — **통과**: 사람 조작 "응 밖으로 나가지 않아 혹시몰라서 해상도도 바꾸면서 해봤어". 23:23:28 eval `left=0 top=676 w=76 h=44`, 패널 1280x720 → 왼쪽 경계 0, 아래 경계 720-44=676에 멈춤. Screen 3840x2160 (패널 좌표와 다름, PanelSettings 스케일로 추정). 해상도 변경 중의 좌표는 기록하지 않았다 (사람 보고만).

### 13 결과 요약
| 항목 | 판정 |
|---|---|
| 13-A 콘솔 탭 모드 치트 버튼 클릭 → `source: Panel` | 통과 |
| B1 설정 변경 반영 | 사용성 결함 (Play 종료 뒤 30초 미반영, Generate Registry 필요, 에셋 미저장) → todo |
| B2 재생성 뒤 `PreferConsoleTab=false`·`m_Standalone`·버튼 표시 | 통과 |
| B3 CHEAT 클릭으로 열림 | 통과 / 버튼으로 닫힘: 해당 없음(설계상 열린 동안 숨김), F8·Close로 닫힘 각 2회 동작 |
| B4 짧게 드래그 | 통과 |
| B5 길게 드래그 | 통과 |
| B6 화면 끝 밖으로 | 통과 |
| 터치 | **미확인** → 14 순서표 |

## 정리 (`30_cleanup_trace.txt`)
- C1 `cheat_stop` → `app=False ps=False`, 디스크 0.
- C2·C3 사람 조작: "Focused로 바꾸고 디파인 껐어, 에러는 없당". 디파인 메모리 `APP_UI_EDITOR_ONLY;SENTIS_ANALYTICS_ENABLED`, `compilationFailed:false`, ScriptAssemblies에 `HB.Cheat.Editor.dll`만 남음.
- C4 `unity close .` graceful. 종료 때 저장: `ProjectSettings/ProjectSettings.asset`, `UserSettings/Layouts/default-6000.dwlt`. 디파인이 **순서가 바뀌어** 저장됨 (`APP_UI_EDITOR_ONLY;SENTIS_ANALYTICS_ENABLED`, 기준선은 `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY`) → 디파인이므로 기준선 순서로 되돌림 → 해시 기준선과 같음.
- **지휘자 누락 (확정)**: 0단계에서 manifest·lock·ProjectSettings의 해시만 떴고 원본 사본을 남기지 않았다 (ToolTest6.3은 git도 없음). 그래서 C5는 원본을 재구성한 뒤 해시로 원본과 같은지 확인했다.
- C5 manifest 재구성 (com.hb.cheat·pipeline 줄 삭제, InGameConsole 고정 해시 제거) → 해시 기준선과 같음. lock 재구성 (com.hb.cheat·pipeline 항목 삭제 — pipeline 의존은 모두 다른 패키지도 사용해 고아 없음, InGameConsole 항목을 고정 해시 없는 URL + `bfd6415a`로) → 해시 기준선과 같음.
- 다시 열기 전 비교 (`33_before_reopen.txt`): 기준선과 다른 것은 `UserSettings/Layouts/CurrentMaximizeLayout.dwlt`·`default-6000.dwlt`뿐 (레이아웃 저장, 되돌리지 않음).
- C6 사람 조작: Hub로 열기. 사람 보고: Unity 7 에셋 로드 실패 2건(`HB_PanelSettings.asset`, `InGameConsoleSettings.asset`)만, 그 밖에 없음.
- C7 — **통과(원상)** (`34_c7.txt`): Editor.log(ToolTest6.3 실행) `error CS` 0, `HB.InGameConsole.dll`·`.Editor.dll` 23:27:43 생성, `HB.Cheat*` dll 0개, lock InGameConsole `bfd6415a`, PackageCache `@d2ad937a31db`, Unity 7 에셋 로드 실패 2건(기준선과 같은 상태). 남은 샘플·레지스트리·설정 에셋으로 인한 에러 없음.
- C8 사람 조작: 에디터 닫음.
- C9 최종 비교 (`35_final_sha256.txt`):
  | 대상 | 기준선 대비 |
  |---|---|
  | `Packages/manifest.json` | 같음 |
  | `Packages/packages-lock.json` | 같음 (재구성) |
  | `ProjectSettings/` 25개 | 모두 같음 (디파인 `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY`) |
  | `UserSettings/` 12개 중 10개 | 같음 |
  | `UserSettings/Layouts/default-6000.dwlt` | 다름 (레이아웃 저장, `m_EnterPlayModeBehavior: 0` 기준선과 같음) |
  | `UserSettings/Layouts/CurrentMaximizeLayout.dwlt` | 다름 (`m_EnterPlayModeBehavior` 0 → 2, B6 해상도 변경 때로 추정) |
  | 새로 생기거나 없어진 파일 | 없음 |
  - 39개 중 37개 같음. 레이아웃 2개는 git 밖, 되돌리지 않음 (보고만).
  - `_B_moved/` 3개 파일(+.meta) 그대로. `Assets/`의 샘플·생성 레지스트리·설정 에셋 그대로 (사용자 결정).
- ToolCreate git status: 시작 전 대비 `docs/todo.md`만 바뀜 (이번 작업 기록).

## B 결과 요약
| 항목 | 판정 |
|---|---|
| 11 Unity 6.3 컴파일 (0.1.6, git URL 전체 해시) | 1차: 본체 통과·InGameConsole 연동 실패(결함, 원인 확정, todo) → InGameConsole HEAD로 올린 뒤 재판정 **통과** |
| 15 Samples Import | **통과** (사람이 Package Manager에서 Import) |
| 15 versionDefines: pipeline | **통과** (없음 → Cli dll 없음 / 있음 → Cli dll·명령 8개) |
| 15 versionDefines: InGameConsole | **통과** (빼기 → 연동 dll 없음 / 넣기 → 생김) |
| 12 서버 큐 멈춤 UI | **통과** (사람 화면 + JSON) |
| 13 마우스 클릭·드래그 | **통과** (콘솔 탭 클릭, 독립 패널 클릭·드래그 3종). 버튼으로 닫기는 설계상 해당 없음. 설정 반영은 사용성 결함(todo). 터치 미확인 |
| 14 모바일 실기기 | 미실시, 순서표 `14_mobile_checklist.md` |
| 정리 | manifest·lock·ProjectSettings 기준선과 같음 (재구성), 레이아웃 2개만 다름 |
- 사건·관측: UPM은 짧은 해시를 받지 않음 / Unity Hub가 꺼져 있으면 패키지 추가 resolve가 멈춤 / pipeline이 없으면 CLI가 연결되지 않음 / 창이 뒤면 manifest 변경 뒤 컴파일이 미뤄짐 / `screenshot`은 UI Toolkit 오버레이를 담지 않음 / 기준선에서 원본 사본을 남기지 않음(지휘자 누락, 재구성으로 해결).
