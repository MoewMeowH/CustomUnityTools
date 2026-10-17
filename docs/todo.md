# todo

> 2026-10-18: 바탕화면 `todo.txt`(2026-10-16 갱신본)를 이 파일로 합쳤다. Unity 프로젝트 `F:\AI\ToolCreate`, C# 저장소 `F:\AI\ToolWithCS`.

## 패키지 점검 (2026-10-18 기준 미점검)

2026-10-18에 E2String · TestFrameworkHelper · InGameConsole을 점검·수정했다 (`docs/10_18/`). 아래는 아직 같은 방식(코드 검토 → ToolTest6.3 설치 확인 → 결함 수정)으로 보지 않은 패키지.

- [ ] **HB_Build 점검** — 멀티 플랫폼 빌드 매니저 · Jenkinsfile 생성기. 6.3 설치·컴파일, 빌드 단계(IPreBuildStep) 수명주기, 생성 파일 위치(패키지 읽기 전용), TestFrameworkHelper 연동(빌드 전 슬롯 검사).
- [ ] **HB_Cheat 점검** — 2026-10-17 B 확인(설치·UI·감지)은 했지만 오늘 같은 전체 코드 점검은 아직. 아래 HB_Cheat 결함 항목과 함께.
- [ ] **HB_ScreenRecorder 점검** — `com.unity.recorder` 4.0.0 의존, Unity 6.3 호환·에디터 전용 분리.

## HB_InGameConsole

### 후속 기능 (2026-10-18 사용자 결정)
- [ ] 패키지 설치 시 `USE_HLOG`를 Scripting Define에 자동 추가 (지금은 툴바 버튼으로 켜야 함, 없으면 에디터에서도 HLog가 꺼짐).
- [ ] HB_Build 연동: 빌드 종류(LIVE 등)에 따라 `USE_HLOG`를 끄고 켜기 (Define은 플랫폼 단위라 출시 빌드에서 끄는 것을 사람이 챙겨야 하는 문제).

### 보류 (preflight P5 뒤 — 측정 후 판단)
- [ ] PF5-03 FPS 위젯을 꺼도 계측·문자열·클래스 갱신 계속 (`HB_InGameConsoleUI.UpdatePerformanceMetrics`)
- [ ] PF5-06 에디터 콘솔 툴바가 그릴 때마다 Define 조회·Split·HashSet (`HLogConsoleWindow.IsScriptingDefineSymbolDefined`)
- [ ] PF5-07 Watch 변경마다 트리거 수만큼 접미 키 문자열 생성 (`HLogConsoleWindow.CheckWatchTriggers`)
- [ ] PF5-08 초기화 반복 시 채널 탐색·PlayerLoop 조회 반복, 에디터 창 OnEnable의 중복 DiscoverAllChannels
- [ ] PF1 잔여: 메인 스레드 로그는 프레임 예산 밖 즉시 처리 / 고유 채널·태그·Watch 전체 상한 없음 / 값 형식 게터 박싱·Unity 객체 이름 문자열 매 평가 / 첫 열기(또는 이력 전체 교체 후) 최대 1000줄 동기 생성 / Watch 변경 시 에디터 창 스냅샷 전체 복사
- [ ] 측정 우선순위 (P5b): 모바일 로그 부하(메인·워커, 닫힘·열림, 상한 전후, 첫 열기 레이아웃) → Watch 상시 비용 → 생성기 무변경 저장
- [ ] IL2CPP·실기기에서 채널 Exception 태그(ThreadStatic + 동기 콜백 가정) 확인
- 출처: `docs/preflight/2026-10-18_ingameconsole/p5b_perf.md`, `p5a_check.md`

## HB_Cheat

- [ ] **터치 + 모바일 실기기 (Android/iOS)** — 터치 클릭·드래그, OS 키보드 인셋, 안전 영역(노치), 한글 폰트, 검색창·문자열 인자 입력.
  - 순서표(장비·빌드 설정·항목별 통과 기준): `docs/verify/2026-10-17_hb-cheat-b/14_mobile_checklist.md`

- [ ] **cheat_play가 Play 동안만 Game 뷰를 Play Unfocused로 바꾸고 끝나면 되돌리는 옵션 (기본 끔)** — A(CLI Play 동안 runInBackground) 커밋 뒤. A 범위 아님.
  - 먼저 조사: 값을 바꾸는 API가 공개인지 내부인지. 값이 레이아웃 파일·UserSettings·EditorPrefs 중 어디에 저장되는지.
  - 원칙 (runInBackground와 같음): 사람이 누른 Play는 건드리지 않는다. 어느 경로로 끝나도 되돌린다. 디스크에 흔적을 남기지 않는다.
  - 기준 자료: A 실제 확인에서 사람이 드롭다운을 바꾼 기록(바꾼 시각, 해시 비교 결과) — `docs/verify/2026-10-17_cli-run-in-background/30_*`
  - 출처: [계획서 결정 2](plans/2026-10-17_hb-cheat-cli-run-in-background.md)

- [ ] **HB_Cheat README 보강: 콘솔 탭 모드에서는 CHEAT 버튼이 없음** — B 13 관측 (`docs/verify/2026-10-17_hb-cheat-b/README.md` "13").
  - 지금 README "InGameConsole 연동" 절은 "콘솔이 있으면 콘솔의 Cheat 탭으로 들어간다"만 적고, 이때 독립 패널과 CHEAT 떠 있는 버튼이 만들어지지 않는다는 점, `Prefer Console Tab`(설정)·`CheatRuntimeOptions.PreferConsoleTab`을 끄면 독립 패널·CHEAT 버튼을 쓸 수 있다는 점은 없다.

- [ ] **HB_Cheat README 설치 안내 보강** — git URL 버전 고정은 전체 40자 커밋 해시나 태그로 (UPM은 짧은 해시를 받지 않음). 루트 README에는 이미 있음, HB_Cheat README에는 없음.

- [ ] **HB_Cheat 사용성 결함: 런타임 설정을 바꿔도 레지스트리가 다시 생성되지 않아 반영되지 않음** — B 13-B 관측 (`docs/verify/2026-10-17_hb-cheat-b/README.md` "13").
  - 관측: 설정 창에서 Prefer Console Tab을 끄고 Play를 끝낸 뒤 30초 동안 레지스트리 재생성·컴파일 없음, `HBCheatRegistry.g.cs`는 `PreferConsoleTab = true` 그대로. 설정 에셋도 디스크에 저장되지 않음(`preferConsoleTab: 1`, 메모리는 False).
  - 원인 (코드): 런타임 옵션은 생성 레지스트리로 들어가는데(`CheatCodeGenerator.cs:351`), 설정 창은 `ApplyModifiedProperties`만 하고 다시 생성하지 않는다(`CheatEditorWindow.cs:159-173`). 자동 생성은 컴파일 뒤 에디트 모드에서만(`CheatCodeGenerator.cs:71-76`).
  - 후보 1: 런타임 설정이 바뀌면 다시 생성 (에디트 모드에서, Play 중이면 Play가 끝난 뒤).
  - 후보 2: 설정 창과 README에 "런타임 설정을 바꾸면 Generate Registry를 누른다" 안내.
  - 위험 (추정, 사용자 추가): 설정 에셋이 디스크에 저장되지 않아, 에디터를 다시 시작한 뒤 재생성하면 설정이 원래대로 돌아갈 수 있다. 확인은 수정 작업 때 한다.

- 참고 (배포 환경에서만 보인 사실): UPM은 짧은 커밋 해시를 받지 않는다 / Unity Hub가 꺼져 있으면 패키지 추가 resolve가 멈춘다 / Pipeline screenshot 명령에는 UI(UI Toolkit 오버레이)가 담기지 않는다.

## HB_E2String

### 벤치마크 문서 갱신 (`Assets/Tools/Benchmarks/E2String/Benchmark.md`)
- [ ] **IL2CPP 크기 재측정** — 메뉴 `Tools > HB > E2String Benchmark > Measure IL2CPP Size Variants` (5 clean builds, 약 11분), 결과 `Temp/E2StringIl2CppSize.csv`.
  - 반영 위치: 6-1 표, 1. 요약의 "비용" 행, 패키지 README 성능 절의 "Enum 하나당 약 +19 KB". 예상 약 +13 KB (추정, 실측 필요).
  - DLL 기준: 2026-10-18 생성기 변경(대소문자 hintName 해시, E2S005 경고, 생성 코드 `global::` 정규화) 뒤 현재 DLL md5 `8f4ba2ef…`. `global::`는 이름만 정규화하므로 IL은 같을 것으로 봄(추정) — 재측정 때 함께 확인.
- [ ] **ignoreCase 제거 반영 문구 정리** — 측정 대상 DLL 해시 표기, 생성 코드 설명의 "ignoreCase는 제거 예정이라 생략", 6-1 마지막 줄 "ignoreCase 포함 DLL로 측정, 재측정 필요"(재측정 후 삭제), 부록 D "대소문자 무시 파싱: 제거 예정" → "제거됨". 시간·할당 수치는 그대로 유효(생성 코드 diff로 ignoreCase 외 변경 없음 확인).

### 추가 측정
- [ ] Android ARM64 IL2CPP 측정 (기기 필요)
- [ ] Managed Stripping Medium / High에서 미사용 생성 코드가 빠지는지 확인 (Low까지는 안 빠짐)

### 정리
- [ ] 프로젝트용 어트리뷰트 `Assets/Scripts/E2String/E2StringAttribute.cs`를 매니저에서 다시 생성 — 7줄 주석에 `ignoreCase` 문구 남아 있음 (2026-10-18 확인). 작업용 파일이라 커밋 대상 아님. 매니저의 **다시 생성 (덮어쓰기)** 사용 가능.
- [ ] 안 쓰는 빌드 폴더 삭제 (git 무시 대상, 2026-10-18 아직 있음): `Builds/E2StringBenchmark_IL2CPP`, `_IL2CPP_Dev`, `_IL2CPP_Release`, `_CoreCLR`, `_CoreCLR_Dev`, `_CoreCLR_Release`, `_Size`, `_Size_Low`
- [ ] 에디터 Profiler에 로드된 IL2CPP .raw 캡처 정리 (Profiler 창에서 Clear)

### Unity 7.0(CoreCLR) 정식 출시 후
- [ ] CoreCLR 기준 벤치마크 재측정 — 컴파일러(Roslyn) 버전이 바뀌면 switch(string) IL이 달라질 수 있고 런타임도 Mono → RyuJIT (기존 `Builds/E2StringBenchmark_CoreCLR` 구성 재사용).
- [ ] C# 11 이상이 되면 `To{Enum}Fast(ReadOnlySpan<char>)` 오버로드 검토 ([Flags] "A, B" 파싱도 부분 문자열 할당 없이).
- 참고 (2026-10 기준): 7.0 알파는 C# 9 / .NET Standard 2.1, C# 14 · .NET 10은 날짜 미정, MSBuild 컴파일은 실험 기능.

## HB_Orchestration (`F:\AI\ToolWithCS\Orchestration`)

- [ ] **호출 기록 본문(프롬프트 · 응답)을 `blobs/<sha256>.txt`로 빼고 `calls.jsonl`에는 해시만 남길지 결정**
  - 언제: 기록이 수천 건 넘게 쌓였을 때 (2026-10-17 기준 74건 3.1MB, 한 건 평균 43KB, 그중 프롬프트가 절반).
  - 먼저 확인: 프롬프트 중복(같은 첨부 · 지침)이 실제로 많은지 → 많으면 이득이 큼.
  - 하게 되면: 응답 앞 200자 정도는 기록에 남김(목록 마우스 오버 미리보기), 검색은 본문 제외(이유 · 모델 · 파일 등 메타데이터만) / ResultSaver(save_result)와 호출 상세 화면이 blob에서 읽게 / 예전 기록(본문이 기록 안에 있음)도 읽히게.
- [ ] OpenAI 단가를 공식 가격 페이지에서 확인 (지금 값은 집계 사이트 값).
- [ ] 코드 기본 `MaxInputChars`를 250,000으로 바꾼 것(2026-10-18, 사용자)이 다음 빌드·배포에 들어갔는지 확인 — 지금은 `Publish_Output/appsettings.json` 값(250,000)으로 동작 중.

## 끝난 항목 (2026-10-18 합칠 때 정리)

- E2String: package.json 버전 → 모든 도구 1.0.0으로 통일(배포 이력 없음, `5774df7`) / Unity 6.3 UPM 설치 확인 → 패키지 점검에서 확인(`docs/10_18/worklog_Estring.md`) / 패키지 DLL 교체 → `d62d14f` / 최소 버전 6000.3 유지 결정(취소 항목) / 커밋하지 않은 작업 파일(ProjectSettings·Assets/Settings) → 작업 트리 깨끗함 확인.
- HB_Cheat: "(릴리즈 전) InGameConsole 1.1.0 + 감지 하한 1.1.0" → 사용자 결정으로 삭제(모든 도구 1.0.0 통일).
- HB_Orchestration: 캐시 쓰기 단가(하지 않기로) / 대시보드 호출 목록 run id 배지·검색·이유 칸(`fbd9d87`) / IssueList.md 제거(`4a9778d`) / calls.jsonl 한글 이스케이프 끄기·대시보드 변경 시만 다시 읽기·토큰 상한 검사 파일 끝에서부터 읽기.
