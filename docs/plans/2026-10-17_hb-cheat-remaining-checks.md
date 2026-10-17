# HB_Cheat — 남은 확인 항목 11~15 (B)

지시서: [docs/work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md](../work-orders/2026-10-17_2042_HB_Cheat-CLI-Play-runInBackground-남은-확인-11-15.md) (B 부분, 사용자 결정 1~3)

## 요약
- ToolTest6.3(6000.3.23f1)에 com.hb.cheat를 git URL `#869e2614…`(0.1.6)로 설치해 11(컴파일)·15(Samples Import, versionDefines 꺼짐/켜짐)를 지휘자가 확인하고, 같은 설치 상태에서 12(서버 큐 멈춤 UI)·13(실제 클릭·드래그)를 사람 조작과 함께 확인한다.
- 목적은 관찰과 기록이다. 결함이 보이면 고치지 않고 기록한 뒤 멈춘다. 코드는 바꾸지 않는다.
- 끝나면 manifest·lock·디파인·ProjectSettings를 시작 전으로 되돌리고 해시로 비교한다. `Assets/`에 생긴 것은 지우지 않는다(사용자 결정 2-4).

## 배경

### 확정된 결정 (지시서)
- 비교 방법: 파일 해시. git init 안 함. 스냅샷 `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/` 파일별 SHA-256. `Library/`·`Temp/`·`Logs/` 제외. 줄바꿈만 다르면 "내용 diff 0"으로 따로 표시. (사용자 결정 1-1)
- 15 순서: (1) com.hb.cheat만 → 컴파일(11) → pipeline 연동 꺼짐 (2) com.unity.pipeline 추가 → `cheat_*` 명령 보임 (3) InGameConsole 잠시 빼서 꺼짐 → 다시 넣어 켜짐 (4) manifest·lock·디파인 되돌림, 해시 비교, InGameConsole 원래대로 컴파일·동작. (사용자 결정 1-2, 2-2, 2-3)
- 설치 기준: `#869e2614fca423e4d327eefed7abe954a765d772`(0.1.6, A 커밋). 짧은 해시가 안 되면 전체 해시. 계획 당시 기준은 `#84016a8`(0.1.5)였다. 바꾼 이유: A의 새 CLI 명령까지 배포 형태로 확인하기 위해. (사용자 결정 3, 2-1)
- `Assets/` 아래 생긴 것(샘플, 생성 레지스트리, 설정 에셋)은 기록·삭제하지 않는다. 되돌린 뒤 Safe Mode가 안 나는지만 본다. 오류가 나면 멈춘다. 샘플 파일의 보호(`#if DEV_CHEAT` 등)를 본다. (사용자 결정 2-4)
- A 실제 확인에서 정해진 실사용 조건: Game 뷰 Play Unfocused + runInBackground 기능 켬 (A 결정 2). 드롭다운은 사람이 바꾼다.

### ToolTest6.3 현재 상태 (확정, 조사)
- 경로 `F:/AI/ToolTest6.3/ToolTest6.3`, Unity 6000.3.23f1, git 저장소 아님.
- manifest: `com.hb.ingameconsole`(git URL, lock `bfd6415a…`), `com.hb.screenrecorder`. `com.hb.cheat`·`com.unity.pipeline` 없음.
- Standalone 디파인: `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY` (`HB_CHEAT`·`DEV_CHEAT` 없음).
- `ProjectSettings.asset` runInBackground 0, Enter Play Mode Options `enabled 1 / options 0`.
- 열린 에디터 없음.

### 코드 근거 (확정)
- `HB.Cheat.Editor.Cli.asmdef`: defineConstraints `HB_CHEAT`·`HB_CHEAT_PIPELINE`, versionDefines `com.unity.pipeline` ≥ 0.0.1 → `HB_CHEAT_PIPELINE`.
- `HB.Cheat.InGameConsole.asmdef`: defineConstraints `HB_CHEAT`·`HB_CHEAT_HAS_CONSOLE`, versionDefines `com.hb.ingameconsole` ≥ 1.0.0 → `HB_CHEAT_HAS_CONSOLE`.
- 런타임 `HB.Cheat`는 `HB_CHEAT` 조건. 따라서 (1)에서 "pipeline 연동 꺼짐"을 보려면 `HB_CHEAT`가 켜져 있어야 한다.
- 12: `CheatSystem.Options.ServerTimeoutSeconds`(하한 0.5, `CheatSystem.cs:648`), `CheatLogTransport`(`CheatExecution.cs:79`, 응답하지 않음 → 시간 초과), 패널 "Server queue paused — …" + Resume 버튼(`CheatPanel.cs:496-501`), 대기 건 메시지 `Paused: …`(`CheatSystem.cs:518`).
- 13: CHEAT 버튼 드래그(`CheatRunner.cs:178-188, 225, 315` 이하, 임계 12px).
- 샘플(`Samples~/CheatExamples`): 치트 파일 6개는 `#if DEV_CHEAT`, `ExampleGameData.cs`만 보호 없음(HB.Cheat 참조 없음). 서버 치트·전송 예제 포함.

## 계획

### 0. 기준선
- ToolTest6.3: manifest·lock·`ProjectSettings/` SHA-256, 디파인, runInBackground 줄, `Assets/` 목록(참고용, 비교·삭제 대상 아님) → `docs/verify/2026-10-17_hb-cheat-b/00_*`.
- ToolCreate git status.

### 1. 11 + 15-(1): com.hb.cheat만 설치
- manifest에 `"com.hb.cheat": "https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat#869e2614"` 추가 (실패하면 전체 해시).
- `unity open` → resolve → lock에 hash `869e2614…` 확인.
- 디파인 `HB_CHEAT;DEV_CHEAT`를 Standalone에 추가 (방법은 질문 3).
- **통과 기준 (11):** `compilationFailed:false`, Safe Mode 아님, `HB.Cheat`·`HB.Cheat.Editor`·`HB.Cheat.Editor.Generator` 어셈블리가 로드됨.
- **통과 기준 (15-1):** `HB.Cheat.Editor.Cli` 어셈블리가 없음, `unity command --query cheat` 결과에 `cheat_*` 없음, `HB.Cheat.InGameConsole`은 있음(InGameConsole 설치 상태).

### 2. 15 Samples Import
- Cheat Examples 샘플을 Import (방법은 질문 4).
- **통과 기준:** `Assets/Samples/HB_Cheat/0.1.6/Cheat Examples/`(경로는 Unity 규칙, 확인 후 기록)에 파일이 생기고 컴파일 통과, 레지스트리 생성 후 `cheat_list`에 샘플 치트가 나옴(2단계 뒤 pipeline 추가 후에 확인).

### 3. 15-(2): com.unity.pipeline 추가
- manifest에 `com.unity.pipeline` 0.8.0-exp.1 추가 (TTESTT·SampleCheck와 같은 버전).
- **통과 기준:** `HB.Cheat.Editor.Cli` 로드, `cheat_list`·`cheat_play`·`cheat_stop` 등 8개 명령 보임, `cheat_list`가 `ok:true`(Play 밖이면 `not_initialized`가 아니라 목록 — 실제 응답을 기록).

### 4. 15-(3): InGameConsole 꺼짐 → 켜짐
- manifest에서 `com.hb.ingameconsole`을 잠시 뺀다 → 컴파일.
- **통과 기준 (꺼짐):** 컴파일 통과, `HB.Cheat.InGameConsole` 어셈블리 없음.
- 다시 넣는다 → **통과 기준 (켜짐):** `HB.Cheat.InGameConsole` 로드, 컴파일 통과.

### 5. 12 서버 큐 멈춤 UI (사람 조작 포함)
- 사람: Game 뷰 드롭다운 Play Unfocused로 (지휘자는 값만 기록).
- 지휘자: `cheat_play` → eval로 `CheatSystem.Options.ServerTimeoutSeconds = 0.5f`, `CheatSystem.ServerTransport = new CheatLogTransport()` → 샘플 서버 치트 2개를 `cheat_run` → 0.5초 뒤 `cheat_status`로 1번 TimedOut·2번 blocked(`Paused: …`) 확인.
- 사람: 패널을 열고 History 탭을 본다 → 화면 상태를 적어 보낸다 → Resume 버튼 클릭 (사람 조작).
- 지휘자: `cheat_status`/`cheat_history`로 2번이 전송됐는지(다시 TimedOut이 되는지) 확인.
- **통과 기준:** History 탭에 "Server queue paused" 안내와 Resume 버튼이 보임(사람 보고), 대기 건 메시지 `Paused: …`(사람 보고 + JSON), Resume 클릭 뒤 2번이 전송됨(JSON: 2번 상태가 Pending 전송 → 시간 초과로 진행).
- 화면 기록: Pipeline에 스크린샷 명령이 있으면 Game 뷰 스크린샷을 함께 남긴다(질문 5). 스크린샷은 관찰 기록이고 클릭을 대신하지 않는다.

### 6. 13 실제 클릭·드래그 (사람 조작)
- 사람: CHEAT 버튼 클릭 → 패널 열림, 패널 안 버튼 클릭 → 치트 실행, CHEAT 버튼 드래그 → 위치 이동, 짧게 누름(12px 미만) → 드래그가 아니라 클릭.
- 지휘자: 각 조작 뒤 `cheat_history`로 실행 기록(`source: Panel`)을 읽고, 버튼 위치는 eval로 읽을 수 있으면 읽는다.
- **통과 기준:** 클릭으로 패널이 열리고 실행이 History에 `Panel`로 남음, 드래그로 버튼 위치가 바뀜(사람 보고 + 가능하면 좌표), 짧게 누름은 열기/닫기로 동작.
- 코드 호출로 대신한 것은 "대체 확인"으로 표시하고 원래 항목은 미확인으로 둔다.

### 7. 14 모바일 실기기 — 질문 2

### 8. 정리 (15-(4))
- Play 종료, 드롭다운 Play Focused로 (사람), 에디터 정상 종료.
- manifest·lock을 시작 전 스냅샷으로 되돌림 (com.hb.cheat·pipeline 빠짐, InGameConsole 원래대로). 디파인 되돌림.
- `unity open` → 컴파일 통과(Safe Mode 아님) 확인, InGameConsole 원래대로 컴파일·동작(Play 진입 뒤 콘솔이 뜨는지 등, 방법은 실행 때 기록).
- `Assets/`에 남은 샘플·생성 레지스트리·설정 에셋 때문에 컴파일 오류가 나지 않는지 확인. 오류가 나면 지우지 않고 멈춤.
- 해시 비교. 디파인 외 남은 차이는 되돌리지 않고 보고.
- ToolCreate git status 비교.

## 버린 대안과 이유
- 11을 TTESTT·SampleCheck의 선행 결과로 갈음: 요청이 ToolTest6.3이고, 이번 기준은 0.1.6이라 선행 결과(d22291e)와 다르다.
- 12를 EditMode 테스트로만: 화면과 클릭이 목적이라 맞지 않는다(코드 경로는 이미 테스트됨).
- 13을 `onClick.Invoke()`·이벤트 주입으로: 실제 입력 경로 확인이 목적이라 맞지 않는다.

## 위험
- git URL 해시 해석 실패 → 전체 해시로 다시, 기록.
- pipeline 추가 시 다른 패키지 의존성 변경으로 lock이 크게 바뀜 → 정리 때 스냅샷으로 되돌림.
- 디파인 변경이 `ProjectSettings.asset`을 쓴다 → 정리에서 되돌리고 해시 비교. Unity가 줄바꿈 등으로 다시 쓰면 내용 diff 0으로 표시해 보고.
- 샘플 Import 뒤 manifest·디파인을 되돌리면 `ExampleGameData.cs`는 보호 없이 남는다 → HB.Cheat를 참조하지 않으므로 컴파일은 될 것으로 추정. 오류면 멈춤.
- 12에서 패널이 Play Unfocused + 창 뒤 상태에서 그려지는지 → 사람이 화면을 볼 때는 Unity 창을 앞으로 가져와야 한다. 그 시점부터는 포커스 조건이 의미 없다(12의 판정과 무관).

## 테스트
- 코드 변경 없음. EditMode 테스트는 돌리지 않는다 (필요하면 ToolTest6.3 manifest `testables`가 필요 — 이번 범위 아님).

## 실제 확인
- 위 1~8의 통과 기준을 실행 전에 고정한다. 결과를 보고 바꾸지 않는다.
- 기록: `docs/verify/2026-10-17_hb-cheat-b/README.md`와 단계별 파일. 사람 조작은 "사람 조작"으로 표시.

## 사용자에게 물을 것
1. 순서: 0 → 1(11·15-1) → 2(샘플) → 3(15-2 pipeline) → 4(15-3 InGameConsole) → 5(12) → 6(13) → 8(정리). 지시서 제안(11 → 15 → 12 → 13 → 14)과 같고, 15를 12·13보다 먼저 끝낸다. 이대로 할까요?
2. 14(모바일 실기기)를 이번에 할까요? 권장: 이번에는 순서표만 만들고 미룬다. 장비·빌드 시간이 크고, 11~13·15와 설치 상태를 공유하지 않는다.
3. 디파인을 켜는 방법: (가) HB_Cheat 메뉴(`Tools > HB > Cheat`의 디파인 토글, 사람 조작) (나) 지휘자가 eval로 `PlayerSettings.SetScriptingDefineSymbols`. 권장: (가) — 배포 사용자가 쓰는 실제 경로를 같이 확인한다. (나)는 "대체 확인"으로 표시.
4. Samples Import 방법: (가) 사람이 Package Manager 창에서 Import 버튼 (나) 지휘자가 eval로 `UnityEditor.PackageManager.UI.Sample.FindByPackage(...).Import()`. 권장: (가) — 15의 목적이 배포 환경 확인이라 실제 UI 경로를 본다.
5. 12의 화면 기록: Pipeline 스크린샷 명령이 있으면 Game 뷰 스크린샷을 남길까요? (클릭은 사람이 하고, 스크린샷은 관찰 기록으로만 쓴다)

## 결정

사용자 답 원문:

> 계획 승인합니다. 질문 답과 덧붙일 것입니다.
>
> 답
>
> 순서: 제안대로 0 → 11·15-1 → 샘플 → pipeline → InGameConsole → 12 → 13 → 정리.
> 14 모바일: 이번에는 순서표만 만들고 미룹니다. 순서표에는 장비, 빌드 설정, 확인 항목(키보드 인셋, 노치, 한글 폰트, 검색창·문자열 인자), 항목별 통과 기준을 넣어 주세요.
> 디파인: (가) 내가 HB_Cheat 메뉴 토글로 켭니다.
> 샘플 Import: (가) 내가 Package Manager에서 Import를 누릅니다.
> 12 화면 기록: 스크린샷 명령이 있으면 남깁니다. 기록에는 "관찰 보조"라고 적고, 판정은 내가 화면에서 본 것과 JSON으로 합니다. 스크린샷을 찍을 때 창 포커스가 바뀌는지도 같이 적어 주세요.
>
> 덧붙일 것
>
> ToolTest6.3 기준선: 0단계에서 manifest, lock, ProjectSettings/, UserSettings/ 해시를 먼저 뜹니다. git init은 하지 않습니다(이전 결정 그대로). Game 뷰 드롭다운 값은 읽어서 기록만 합니다.
> 13의 범위: PC에서는 마우스 클릭과 드래그만 확인합니다. "터치"는 이번에 확인할 수 없으니 미확인으로 두고 14 순서표로 넘깁니다. 마우스로 확인한 것을 터치 확인으로 쓰지 않습니다.
> 사람 조작 행은 순서표를 주고 멈춥니다. 행마다 내가 할 조작, 지휘자가 읽을 값, 통과 기준을 적어 주세요. 사람 조작은 기록에 "사람 조작"으로 따로 표시합니다.
> 결함이 나오면 고치지 말고 원인 추정과 함께 기록한 뒤 멈추고 보고합니다(지시서 B 원칙).
> 정리 단계의 Safe Mode 확인과 InGameConsole 원상 확인은 좋습니다. 끝나면 기준선 해시와 비교한 표를 주세요.

### 이 답으로 바뀐 계획 항목
- 0단계 기준선에 `UserSettings/` 해시와 Game 뷰 드롭다운 값(읽기만)을 더한다.
- 1단계 디파인과 2단계 샘플 Import는 사람 조작. 지휘자가 eval로 대신하지 않는다.
- 13은 마우스 클릭·드래그만. 터치는 미확인으로 두고 14 순서표로 넘긴다.
- 12 스크린샷은 "관찰 보조". 찍을 때 창 포커스 변화도 기록. 판정은 사람이 본 것과 JSON.
- 14는 순서표만 만든다 (장비, 빌드 설정, 확인 항목, 항목별 통과 기준).
- 정리 뒤 기준선 해시 비교표를 낸다.

## 결정 2 (1단계 결함 뒤)

사용자 답 원문:

> 결함 확인했습니다. 고치지 않고 멈춘 것, 원인을 확정까지 좁힌 것 모두 좋습니다. 아직 릴리즈 전이라 실제 사용자는 없습니다. 그래서 이번에는 고치지 않고 todo로 남깁니다.
>
> 기록: “0.1.6 회차: 11 본체 컴파일 통과, InGameConsole 연동 컴파일 실패(결함, 원인 확정), 15-1 판정 보류”. 이번 회차 기록은 지우지 않습니다.
> todo에 추가 (릴리즈 전): InGameConsole을 1.1.0으로 올리고, HB_Cheat 감지 하한을 1.1.0으로 올립니다. 다른 감지 조건(pipeline 등)의 하한도 같이 점검합니다. 고친 뒤에는 오래된 InGameConsole(1.0.0)에서 연동이 꺼지고 컴파일 에러가 0인지 확인합니다. 규칙서 문안 후보에 "패키지에 공개 API를 추가하면 버전을 올리고, 쓰는 쪽 감지 하한도 맞춘다"를 추가합니다.
> B는 (나)로 이어 갑니다. ToolTest6.3의 InGameConsole을 HEAD 커밋(전체 해시)으로 올립니다. 기준선과 달라지는 점으로 기록하고, 정리 단계에서 bfd6415a로 되돌립니다.
> 올린 뒤 11을 다시 판정합니다. 기준은 그대로입니다(컴파일 에러 0, Safe Mode 아님, dll 세 개 있음 + HB.Cheat.InGameConsole.dll 있음).
> 15의 “InGameConsole 빼기 → 꺼짐, 다시 넣기 → 켜짐” 단계는 계획대로 합니다.
> 디파인은 켜 둔 채로 진행합니다. InGameConsole을 올리면 다시 컴파일될 테니, Safe Mode나 에러가 남는지만 확인해 주세요.

### 이 답으로 바뀐 계획 항목
- ToolTest6.3 InGameConsole: manifest를 `#43f1f105a5893fc4bf3603e13879acfbae466413`(HEAD, 전체 해시)로 고정 — 기준선과 달라지는 점. 정리 단계에서 manifest·lock을 기준선(`bfd6415a`)으로 되돌린다.
- 11 재판정 기준: 컴파일 에러 0, Safe Mode 아님, `HB.Cheat`·`HB.Cheat.Editor`·`HB.Cheat.Editor.Generator` dll + `HB.Cheat.InGameConsole.dll` 있음.
- 15-(3) InGameConsole 빼기 → 꺼짐 / 다시 넣기(HEAD 고정 줄) → 켜짐은 계획대로.
- 규칙서 문안 후보: "패키지에 공개 API를 추가하면 버전을 올리고, 쓰는 쪽 감지 하한도 맞춘다".
- 규칙서 문안 후보 (사용자, B 마무리): "확인 0단계에서 해시와 함께 원본 파일 사본도 남긴다 (git이 없는 프로젝트는 특히). 정리 때 해시가 다르면 사본으로 되돌린다."
