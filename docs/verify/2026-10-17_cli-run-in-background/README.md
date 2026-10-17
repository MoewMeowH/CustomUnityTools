# 실제 확인 — cheat_play 동안만 runInBackground (TTESTT)

계획서: [docs/plans/2026-10-17_hb-cheat-cli-run-in-background.md](../../plans/2026-10-17_hb-cheat-cli-run-in-background.md)

## 준비 (확정)
- 기준선: `00_*` (TTESTT git status·HEAD·manifest/lock/ProjectSettings SHA-256·runInBackground 줄, ToolCreate git status·HB_Cheat 폴더 해시)
- TTESTT `Packages/manifest.json`의 com.hb.cheat → `file:F:/AI/ToolCreate/Assets/Tools/HB_Cheat` (lock도 `source: local`로 바뀜)
- `unity open .` → ready (pid 9548), `compilationFailed:false`, `cheat_play`·`cheat_stop` 등록 확인
- 열기 직후 ToolCreate HB_Cheat 폴더 해시: 변화 없음
- 열기 직후 값: `app=False ps=False playing=False frame=0`, 디스크 `ProjectSettings.asset:89 runInBackground: 0`, `EditorSettings m_EnterPlayModeOptionsEnabled: 1 / m_EnterPlayModeOptions: 0`

## 계획과 다른 점 (확정)
- frame 읽기: TTESTT에는 `boid.status` 치트가 없다(`[Cheat]` 파일 0개). 7장 4.5의 TTESTT 회차와 같이 `eval`로 `Time.frameCount`를 읽는다.
- 세 값 읽기: `eval`로 `Application.runInBackground`·`PlayerSettings.runInBackground`, 디스크는 `grep runInBackground ProjectSettings/ProjectSettings.asset`.
- 0행 시작: 계획은 "설정 끔 + `cheat_play`"였으나 `editor_play`로 한다 (사용자 승인). 근거: TTESTT에는 HB_Cheat 설정 에셋이 없고, 이때 `CheatSettings.Load()`는 저장하지 않는 기본값 인스턴스(켬)를 매번 새로 만든다(`CheatSettings.cs` Load) → 에셋을 만들지 않고는 끌 수 없다. 설정이 꺼져 있으면 `cheat_play`는 `CliPlayRunInBackground.Begin`이 아무것도 쓰지 않고(`reason:"disabled"`) `EditorApplication.isPlaying = true`만 하므로 `editor_play`(`PlayModeCommands.cs:15-26`, `isPlaying = true` 한 줄)와 같은 경로다. 설정 끔 자체는 EditMode 테스트 `Disabled_DoesNothing`으로 확인했다. TTESTT에 설정 에셋은 만들지 않는다.
- 4행 예외: 계획은 "예외를 던지는 치트 실행"이었으나 TTESTT에는 치트가 없어 `eval`로 Play 중 예외를 던진다 (사용자 승인).
- 3행 재컴파일: 파일을 바꾸지 않도록 `eval`로 `CompilationPipeline.RequestScriptCompilation()`을 부른다 (사용자 승인).

## 사람 조작 1 — 0행·1행 (연속)

### 사람이 할 일
1. **메모장**을 열고 메모장 창을 한 번 클릭해 OS 맨 앞에 둔다. Unity 창은 최소화하지 않고 메모장 뒤에 둔다.
2. 배치를 마치면 아래를 적어 보낸다. 보낸 뒤에는 **지휘자가 "끝"이라고 할 때까지(최대 약 5분) 키보드·마우스를 쓰지 않는다.**
   - 창 배치를 마친 시각 (HH:MM:SS)
   - 눈으로 본 상태: 맨 앞 창 이름, Unity 창이 보이는지(일부/전부 가려짐), Unity가 어느 모니터에 있는지
   - 모니터 구성: 몇 대, 메모장과 Unity가 같은 모니터인지
3. 지휘자가 "끝"이라고 하면 평소대로 써도 된다.

### 0행 — 기준선 (정지 재현)
- 시작: `editor_play` (설정 끔과 같은 경로 — 아래 "사용자 확인" 참조)
- 순서: 값 읽기 → `editor_play` → `status --until-ready` → frame #1 → 1초 → frame #2 → `editor_focus` → frame #3 → 1초 → frame #4 → 값 읽기 → `editor_stop` → 값 읽기
- **통과 기준 (고정):** frame #2 == frame #1 (늘지 않음). Play 중·종료 후 세 값 False / False / 0.
- 기록만: editor_focus 뒤 frame #4 == #3 인지 (7장 관찰 재현 여부).
- **frame #2 > #1이면 정지 재현 실패 → 1행으로 가지 않고 멈추고 보고한다.**

### 1행 — 기능 효과
- 순서: `cheat_play` (응답 저장) → `status --until-ready` → 값 읽기 → frame #1 → 1초 → frame #2 → `cheat_stop` → 에디트 모드 대기 → 값 읽기 → Console에서 `[HB_Cheat] runInBackground` 줄
- **통과 기준 (고정):**
  - 응답 `ok:true`, `runInBackgroundOverride` = `active:true, original:false, reason:null`, `pipeline.config:"none"`
  - Play 중 `app=True ps=True`, 디스크 0
  - frame #2 > frame #1
  - 종료 후 `app=False ps=False`, 디스크 0
  - Console에 켬 한 줄(`on for this CLI Play`)과 되돌림 한 줄(`restored to false (CLI Play ended)`)

## 사람 조작 2 — 2행 (사람이 누른 Play)
1. 사람: Unity 창을 클릭해 앞으로 가져온 뒤 툴바 **Play** 버튼을 누른다. 누른 시각을 적어 보낸다. 그대로 둔다.
2. 지휘자: 값을 읽고 Console을 확인한 뒤 "멈춰 주세요"라고 보낸다.
3. 사람: 툴바 Play 버튼을 다시 눌러 정지하고, 정지한 시각을 적어 보낸다.
4. 지휘자: 값을 읽는다.
- **통과 기준 (고정):** Play 중·종료 후 세 값 False / False / 0. Console에 `[HB_Cheat] runInBackground` 줄 없음.

## 지휘자 단독 행 (사람 조작 뒤)
3 (Play 중 재컴파일: `CompilationPipeline.RequestScriptCompilation()`을 eval로 — 파일을 바꾸지 않는다), 4 (Play 중 eval 예외), 5 (켠 채로 `unity close` → 다시 열기), 6 (eval로 메모리 값만 true → cheat_play), 8 (도메인 리로드 끔으로 1·3·6 다시, EditorSettings 해시 복원), 9 (켠 채로 SaveAssets → 디스크), 10 (정리·비교). 기준은 계획서 표 그대로.

## 결과

### 사람 조작 1 — 창 배치 (사용자 원문)
> 마친 시각 , 지금 바로
> 맨 앞 창의 이름 : qqqqq.txt
> 유니티창 안가림
> 모니터 메인 모니터 (1번)
> 모니터 3개

### 0행 — 정지 재현 실패 → 멈춤 (확정)
트레이스 원문 `20_row0_trace.txt`:
```
21:25:30.260 1 values before: app=False ps=False playing=False disk: 89:  runInBackground: 0
21:25:32.518 2 editor_play: Entered play mode
21:25:41.348 3 status ready
21:25:41.365 4 frame#1 frame=309 paused=False
21:25:45.707 5 frame#2 frame=764 paused=False
21:25:47.697 6 editor_focus: Editor focused via DockArea
21:25:49.618 7 frame#3 frame=1332 paused=False
21:25:52.616 8 frame#4 frame=1767 paused=False
21:25:54.645 9 values during play: app=False ps=False playing=True disk: 89:  runInBackground: 0
21:25:56.662 10 editor_stop: Exited play mode
21:26:00.646 11 values after: app=False ps=False playing=False disk: 89:  runInBackground: 0
```
- 판정: **불통과** (기준 frame #2 == #1, 실제 309 → 764). 세 값은 Play 중·종료 후 모두 False / False / 0.
- 기준대로 1행 이후는 진행하지 않았다. 에디터는 열어 둔 상태(Play 아님).
- 이번 회차에서 기록하지 못한 것: Play 시점에 OS 맨 앞 창이 실제로 무엇이었는지, `InternalEditorUtility.isApplicationActive` 값. 사용자 보고로는 메모장이 맨 앞이었고 Unity 창은 가려지지 않았다.

### 0행 회차 2 — 조건 (가): 메모장 최대화로 Unity를 가림 → 정지 재현 실패 → 멈춤 (확정)
- 사용자 원문: "가로 진행할게 아까랑 똑같아" (보고 항목은 회차 1과 같고, 메모장을 최대화해 Unity를 가린 것으로 이해했다)
- 회차 직전 맨 앞 창(PowerShell `GetForegroundWindow`): `Notepad '*qqqqq.txt - 메모장'`
- 트레이스 원문 `21_row0_run2_trace.txt` (맨 앞 창 열은 인코딩 변환 실패로 비어 있음):
```
21:29:13.860 1 before: app=False ps=False playing=False active=False disk: 89: runInBackground: 0
21:29:16.978 2 editor_play: Entered play mode
21:29:22.541 4 frame#1 frame=1 paused=False active=True
21:29:28.491 5 frame#2 frame=579 paused=False active=True
21:29:31.596 6 editor_focus: Editor focused via DockArea
21:29:34.620 7 frame#3 frame=1475 paused=False active=True
21:29:38.780 8 frame#4 frame=2074 paused=False active=True
21:29:41.891 9 during: app=False ps=False playing=True active=True disk: 89: runInBackground: 0
21:29:47.961 11 after: app=False ps=False playing=False active=True disk: 89: runInBackground: 0
```
- 회차 뒤 맨 앞 창: `Unity 'TTESTT - Sample2_BoidSystem - Windows, Mac, Linux - Unity 6.3 LTS (6000.3.23f1) <DX12>'`
- 판정: **불통과** (frame #2 > #1). 1행 이후는 진행하지 않았다.
- 관측 (확정): `isApplicationActive`가 Play 전 False → `editor_play` 직후 True. 회차 뒤 Unity가 OS 맨 앞.
- 해석 (추정): Play 진입이 Unity 창을 앞으로 가져와 정지 조건이 사라졌다. 후보: Game 뷰 Play 진입 옵션(Play Focused), 열린 씬 차이(7장은 저장 안 한 빈 씬), Windows 전경 전환 허용 조건.

### 0행 회차 3 — 중단 (22_*)
- 사용자 "잠깐 대기"로 `editor_play` 전에 중단. 기록: T 21:32:40, `ForegroundLockTimeout = 0x30d40`(200000ms, 읽기만), Play 전 맨 앞 창 Rider '터미널'.

### 0행 회차 4 — 조건 (다) 타이핑 계속, Play Focused, 메모장 같은 모니터 (23_*)
- frame 1 → 2 (6초), active False, 맨 앞 창 메모장. editor_focus 뒤 Unity가 맨 앞 → 297, 이후 297 → 297.
- 판정: 불통과 (1 → 2). 사용자 판단: 메모장이 에디터와 같은 모니터라 조건이 맞지 않았다 → 다시.

### 0행 회차 5 — 조건 (다), Play Focused, 메모장 다른 모니터 (24_*)
- frame #1 시점 맨 앞 창 Unity (Play 진입 직후 앞으로 나옴), 1 → 171. 판정: 불통과.

### 드롭다운 변경 (사람 조작, 30_* / 31_*)
- 사용자 원문: "지금 바꿧어 focus(현재까지)->unfocus->focus->unfocus 로 했었어" (수신 기록 `31_dropdown_after_time.txt`)
- 전후 비교: `UserSettings/`(레이아웃 포함)·EditorPrefs 레지스트리 변화 없음 → 값은 메모리에만 있음 (에디터 종료 때 레이아웃에 저장될 것으로 추정).
- 같은 시각 21:44:09에 `ProjectSettings/EditorBuildSettings.asset`(diff 내용 0, 줄바꿈만)·`Assets/Settings/Mobile_RPAsset.asset`(URP 에셋 버전 갱신) 저장됨. 원인 미확인. `ProjectSettings.asset`은 쓰이지 않음(19:26 그대로). 되돌림은 A 마무리 때 결정.

### 0행 회차 6 — 조건 (다), Play Unfocused, 메모장 다른 모니터 (32_*)
- frame 1 → 2 (6초), active False, 맨 앞 창 메모장. editor_focus 뒤 429 → 449.
- 판정: **불통과(1 → 2). 사실상 정지 양상. 읽는 시점 문제로 보임(추정)** (사용자 지정 문구)

## 회차 7부터 적용 (사용자 결정, 실행 전 고정 — 바꾸지 않음)
- frame #1은 `status --until-ready` 뒤 **3초** 기다렸다가 읽는다. frame #2는 #1을 읽고 6초 뒤.
- 통과 기준은 그대로: 0행 frame #2 == #1. 1행 기준도 그대로.
- 맨 앞 창(200ms 간격, `40_run7_foreground_200ms.txt`), active, 초당 프레임은 기록만 한다.
- 1행도 같은 시점 규칙. editor_focus는 0·1행 모두 부르지 않는다.
- 한 번에 진행: T+30 0행 `editor_play` → 판정 → `editor_stop` → T+75 1행 `cheat_play` → T+95 `cheat_stop` → 값·로그 확인. 사람은 T+120까지 타이핑. 0행 불통과면 1행은 하지 않는다.

### 회차 7 — 0행·1행 (40_*) — Play Unfocused + 다른 모니터 메모장 타이핑
트레이스 원문 `40_run7_trace.txt`:
```
21:48:41.714 T (메시지 수신: "시작")
21:48:44.759 T+3s before: app=False ps=False playing=False active=False disk: 89: runInBackground: 0
21:49:11.136 T+30s R0 editor_play: Entered play mode
21:49:16.544 T+35s R0 status ready
21:49:22.969 T+41s R0 frame#1 1 active=False
21:49:30.970 T+49s R0 frame#2 1 active=False
21:49:31.076 R0 fps(기록만)=0.0
21:49:31.135 R0 during: app=False ps=False playing=True active=False disk: 89: runInBackground: 0
21:49:33.069 R0 판정: 통과 (frame #2 == #1)
21:49:38.106 T+57s R0 after: app=False ps=False playing=False active=False disk: 89: runInBackground: 0
21:49:56.143 T+75s R1 cheat_play: (응답 원문 미기록 — 아래)
21:50:07.476 T+86s R1 frame#1 358 active=False
21:50:15.511 T+94s R1 frame#2 1566 active=False
21:50:15.632 R1 fps(기록만)=128.7
21:50:15.691 T+94s R1 during: app=True ps=True playing=True active=False disk: 89: runInBackground: 0
21:50:17.808 T+96s R1 cheat_stop
21:50:23.867 T+102s R1 after: app=False ps=False playing=False active=False disk: 89: runInBackground: 0
R1 Editor.log [HB_Cheat] runInBackground 줄:
[HB_Cheat] runInBackground on for this CLI Play (original: false)
[HB_Cheat] runInBackground restored to false (CLI Play ended)
```
- 맨 앞 창 200ms 기록: 585샘플 전부 `Notepad` (Unity가 한 번도 맨 앞에 오지 않음).
- **0행 판정: 통과** (1 → 1, 세 값 False/False/0).
- **1행 판정:**
  | 기준 | 결과 |
  |---|---|
  | 응답 `active:true, original:false, reason:null, pipeline.config:"none"` | **미확인** — 지휘자 기록 실수: grep이 이스케이프된 따옴표에서 끊겨 `{\"`만 남았다. Pipeline도 응답을 남기지 않는다(확정) |
  | Play 중 `app=True ps=True`, 디스크 0 | 통과 |
  | frame #2 > #1 | 통과 (358 → 1566) |
  | 종료 후 `app=False ps=False`, 디스크 0 | 통과 |
  | Console 켬 한 줄·되돌림 한 줄 | 통과 |
- 실사용 조합(Play Unfocused + 기능 켬)에서 Unity가 맨 앞에 오지 않아도 frame이 진행됨 (확정).
- **1행 응답: 미확인(기록 실수). 3·4행에서 대체 확인** (사용자 결정)
- 관측 (기록만, 판정과 무관): 1행의 초당 약 129프레임은 Unity가 앞에 있을 때(초당 600 안팎, 회차 1·2)보다 낮다. runInBackground는 멈춤을 막지만, 백그라운드 속도 제한은 남는다(추정).

## 지휘자 단독 행 (사용자 승인: 3·4·5·6·8·9·10, 불통과면 그 행에서 멈춤)
- 응답 JSON은 `5x_*.json` 파일 전체로 저장한다.

### 3행 — Play 중 재컴파일 (50_*) — **통과**
- `cheat_play` 응답 원문: `{"ok":true,"playMode":"entering","runInBackgroundOverride":{"active":true,"original":false,"reason":null,"pipeline":{"config":"none","enableInBuilds":null,"autoStart":null}}}` → **1행 응답 항목 대체 확인: 통과**
- `RequestScriptCompilation(CleanBuildCache)` → 도메인 리로드 2회, Play 계속(frame 433 → 9921). 재컴파일 중 `cheat_stop`은 `play_mode_unavailable`로 거절(의도한 동작) → 컴파일이 끝난 뒤 다시 `cheat_stop`.
- Play 중 True/True/0, 종료 후 False/False/0, 로그 켬·되돌림 한 줄씩.

### 4행 — Play 중 예외 (51_*) — **통과**
- 응답 원문 3행과 같음 (대체 확인 2번째). `eval`로 `InvalidOperationException` → eval은 `COMMAND_FAILED`, Play·값 유지(True). `cheat_stop` 뒤 False/False/0, 로그 켬·되돌림.

### 5행 — 켠 채로 에디터 정상 종료 → 다시 열기 (52_*) — **통과**
- `unity close .` → `closed:true, method:graceful`. 로그 `restored to false (editor quitting)`.
- 닫은 뒤 디스크 0, `ProjectSettings.asset` SHA-256 `4311030d…2d4c` = 시작 전(`00_`)과 같음.
- 다시 연 뒤 False/False/0, SessionState 키 없음, 컴파일 정상.
- 닫을 때 `UserSettings/Layouts/default-6000.dwlt`가 저장됨 (`89675fdc…` → `03e10606…`). 드롭다운 Play Unfocused가 여기서 디스크에 쓰인 것으로 추정 (todo 기준 자료).

### 6행 — 원래 값 true (53_*) — **통과**
- [시험 준비] eval로 메모리 값만 true (`app=True ps=True`, 디스크 0).
- 응답 원문: `{"ok":true,"playMode":"entering","runInBackgroundOverride":{"active":false,"original":true,"reason":"already_true","pipeline":{"config":"none","enableInBuilds":null,"autoStart":null}}}`
- Play 중 True, HB_Cheat 로그 없음, 디스크 0. [준비 복구] eval false → False/False/0.

### 8행 — 도메인 리로드 끔 (54_*) — 1·3·6 흐름 **통과**, 옵션 복구에 문제
- [옵션 변경] eval `enterPlayModeOptionsEnabled = true; enterPlayModeOptions = DisableDomainReload` → 메모리 `enabled=True options=DisableDomainReload`. 디스크는 바뀌지 않음 (해시 `1542f5af…e2a4` 그대로).
- 8-1: 로그에 `Reloading assemblies for play mode` 없음 (리로드 끔 확인). frame 957 → 2165, Play 중 True/True/0, 종료 후 False/False/0, 로그 켬·되돌림.
- 8-3: 재컴파일 뒤 Play 계속, 종료 후 False/False/0, 로그 켬·되돌림.
- 8-6: `already_true`, HB_Cheat 로그 없음, 준비 복구 뒤 False/False/0.
- **옵션 복구 (확정 관측)**: Unity 6.3에서 두 값이 묶여 있다.
  - `enabled=true; options=None` 순서 → 메모리 `enabled=False options=None`
  - `options=None; enabled=true` 순서 → 메모리 `enabled=True options=DisableDomainReload, DisableSceneReload` (두 리로드가 모두 꺼짐 — 원래와 다른 동작)
  - 다시 `options=None` → `enabled=False options=None` (동작은 원래와 같음: 도메인·씬 모두 리로드)
  - 디스크 `EditorSettings.asset`은 내내 시작 전과 같다 (`m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 0`, 수정 시각 06:50).
  - 변경 전 메모리 값을 읽지 않았다(지휘자 누락). 그래서 원래 메모리 표현이 `enabled=True/None`이었는지 `enabled=False/None`이었는지 모른다.
  - 위험: 이 메모리 값이 저장되면(SaveAssets, 에디터 종료) 디스크가 `m_EnterPlayModeOptionsEnabled: 0`으로 바뀔 수 있다 (추정). → 9행(SaveAssets) 전에 멈춤.

### 사건: 8행 Enter Play Mode Options 복구 (확정)
- 옵션을 바꾸기 전에 메모리 값을 읽지 않았다 (지휘자 누락, 확정).
- Unity 6.3에서는 API로 원래 표현(`enabled 1 / options 0`)을 되살릴 수 없다 (확정 관측: `options=None`이면 enabled가 False로, `enabled=true`면 options가 "둘 다 끔"으로 바뀐다).
- 지금 메모리 `enabled=False options=None` (동작은 원래와 같음). 디스크는 시작 전과 같다.
- 처리 (사용자 결정): 9행은 그대로 진행. 마무리에서 `EditorSettings.asset`이 바뀌었으면 git restore → 에디터를 다시 열어 메모리가 디스크 원본으로 읽히는지 확인 → 해시 비교.

### 9행 — 켠 채로 SaveAssets (55_*) — **통과**
- 9행 전: `EditorSettings.asset` `1542f5af…` mtime 06:50:08, `ProjectSettings.asset` `4311030d…` mtime 19:26:20, 메모리 `enabled=False options=None`.
- `cheat_play` → Play 중 True/True/0 → `AssetDatabase.SaveAssets()` → 직후 디스크 `runInBackground: 0`, 두 파일 해시·수정 시각 그대로 → `cheat_stop` → False/False/0. 로그 켬·되돌림.
- SaveAssets 직후 TTESTT git status: 드롭다운 변경 뒤(`31_`)와 같음.
- 9행 뒤 `EditorSettings.asset` 변화: SaveAssets 직후 없음. 에디터 종료 때는 마무리에서 본다.

### 2행 — 사람이 툴바로 누른 Play (56_*) — **통과**
- 사람 조작: "Play 눌렀어, 지금" (수신 22:06:50) → "지금 눌렀어" (정지, 수신 22:07:28). 드롭다운은 Play Unfocused 상태.
- Play 중 `app=False ps=False playing=True` (frame 1148 → 2047), 디스크 0, SessionState 소유 없음. 종료 후 False/False/0. HB_Cheat runInBackground 로그 0줄.

## 마무리
- M1 스냅샷 A (`60_`), EditorSettings 메모리 `enabled=False options=None`.
- M2 사람 조작: "Focused로 바꿨어, 지금" (`61_dropdown_focused_time.txt`). A → B 디스크 변화 없음.
- M4 `unity close .` graceful (22:08:18). B → C: `EditorSettings.asset`·`ProjectSettings.asset`·`EditorBuildSettings.asset`·`UserSettings/Layouts/default-6000.dwlt` 저장. 세 ProjectSettings 파일은 `git diff` 0줄(내용 diff 0, 바이트만 다름). EditorSettings 디스크 내용은 `1 / 0` 그대로(메모리 `enabled=False`는 쓰이지 않음). `ProjectSettings.asset`의 runInBackground는 0.
- M5 `git restore ProjectSettings/EditorSettings.asset` (사용자 결정대로). 나머지 차이의 되돌리기는 M8 최종 스냅샷 뒤에 사용자 결정 (진행 여부 확인 중).
- M6 `git restore Packages/manifest.json` → com.hb.cheat 원래 줄(git URL), 해시 `d30d854f…` = 시작 전.
- M7 `unity open .` → resolve: lock `source: git, hash d22291e3…`, 컴파일 정상, `cheat_play`·`cheat_stop` 없음(옛 패키지라 예상대로). lock 해시는 시작 전과 다름 → `git diff` 0줄 (줄바꿈: Unity가 LF로 씀, 원본은 CRLF — git 경고 근거, 추정).
  - EditorSettings 메모리 (디스크 원본 `1 / 0`에서 새로 읽음): `enabled=False options=None`. 8행 복구 뒤 메모리 값과 같다 → 8행 사건의 메모리 표현 차이는 결과적으로 없었다 (새로 읽은 값과 같으므로, 확정 관측).
- M8 `unity close .` graceful → 스냅샷 E (`64_`). 시작 전 대비 남은 차이와 처리 (사용자가 미리 고정한 규칙대로):
  | 파일 | 내용 diff | 부류 | 처리 |
  |---|---|---|---|
  | `Packages/packages-lock.json` | 0줄 (줄바꿈 추정) | manifest·lock | git restore |
  | `ProjectSettings/ProjectSettings.asset` | 0줄 (줄바꿈 추정) | 내용 diff 0 | git restore |
  | `ProjectSettings/EditorBuildSettings.asset` | 0줄 (줄바꿈 추정) | 내용 diff 0 | git restore |
  | `Assets/Settings/Mobile_RPAsset.asset` | `k_AssetVersion: 12 → 13` 등 59줄 (`64_mobile_rpasset_diff.txt`) | 사람이나 기능이 만든 변경이 아니라 에디터가 연 김에 바꾼 것 | git restore |
  - 세 부류 밖의 차이: 없음. 새 untracked: 없음. (`EditorSettings.asset`은 M5에서 되돌린 뒤 M8 종료 때 다시 쓰이지 않았다.)
- 최종 비교 (확정):
  - TTESTT `git status --porcelain=v1` = 시작 전 기준선(`00_`)과 같음.
  - manifest·lock·`ProjectSettings/` 27개 파일 SHA-256 = 시작 전과 모두 같음.
  - `UserSettings/Layouts/default-6000.dwlt` (git 밖, 되돌리지 않음): 드롭다운 변경 전 `89675fdc…` → 끝 `e04da9ec…`. 끝 파일에 `m_EnterPlayModeBehavior: 0` (0 = Play Focused로 추정. 5행 종료 때 Unfocused로 저장된 파일의 값은 읽지 않았다).
  - ToolCreate HB_Cheat 폴더 파일 116개 SHA-256 = 시작 전과 같음 (TTESTT 쪽 Unity가 쓴 파일 없음).
  - ToolCreate git status: 시작 전 대비 이번 작업 기록(`docs/todo.md`, `docs/verify/`)만 늘었다.
- `review_tmp/`의 이번 임시 파일(fg 스크립트, EditorPrefs 내보내기, 도우미 스크립트) 삭제.

## 7행 (설정 끔)
- **7행: 미실시 / 대체 확인(EditMode `CliPlayRunInBackgroundTests.Disabled_DoesNothing`)**
- 이유: 계획서 `## 결정` 수정 A로 7행(설정 끔 + 창 뒤)을 0행으로 옮겼다. 0행은 TTESTT에 설정 에셋이 없어 설정을 끌 수 없으므로(에셋을 만들지 않기로 결정) `editor_play`로 했다. 설정이 꺼져 있으면 `cheat_play`는 `Begin`이 아무것도 쓰지 않고(`reason:"disabled"`) `isPlaying = true`만 하므로 같은 경로다. "설정 끔 상태의 cheat_play" 자체는 실제 에디터에서 실행하지 않았다.
