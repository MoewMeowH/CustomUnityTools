# todo

## HB_Cheat

- [ ] **cheat_play가 Play 동안만 Game 뷰를 Play Unfocused로 바꾸고 끝나면 되돌리는 옵션 (기본 끔)** — A(CLI Play 동안 runInBackground) 커밋 뒤. A 범위 아님.
  - 먼저 조사: 값을 바꾸는 API가 공개인지 내부인지. 값이 레이아웃 파일·UserSettings·EditorPrefs 중 어디에 저장되는지.
  - 원칙 (runInBackground와 같음): 사람이 누른 Play는 건드리지 않는다. 어느 경로로 끝나도 되돌린다. 디스크에 흔적을 남기지 않는다.
  - 기준 자료: A 실제 확인에서 사람이 드롭다운을 바꾼 기록(바꾼 시각, 해시 비교 결과) — `docs/verify/2026-10-17_cli-run-in-background/30_*`
  - 출처: [계획서 결정 2](plans/2026-10-17_hb-cheat-cli-run-in-background.md)

- [ ] **HB_Cheat README 보강: 콘솔 탭 모드에서는 CHEAT 버튼이 없음** — B 13 관측 (`docs/verify/2026-10-17_hb-cheat-b/README.md` "13").
  - 지금 README "InGameConsole 연동" 절은 "콘솔이 있으면 콘솔의 Cheat 탭으로 들어간다"만 적고, 이때 독립 패널과 CHEAT 떠 있는 버튼이 만들어지지 않는다는 점, `Prefer Console Tab`(설정)·`CheatRuntimeOptions.PreferConsoleTab`을 끄면 독립 패널·CHEAT 버튼을 쓸 수 있다는 점은 없다.

- [ ] **HB_Cheat 사용성 결함: 런타임 설정을 바꿔도 레지스트리가 다시 생성되지 않아 반영되지 않음** — B 13-B 관측 (`docs/verify/2026-10-17_hb-cheat-b/README.md` "13").
  - 관측: 설정 창에서 Prefer Console Tab을 끄고 Play를 끝낸 뒤 30초 동안 레지스트리 재생성·컴파일 없음, `HBCheatRegistry.g.cs`는 `PreferConsoleTab = true` 그대로. 설정 에셋도 디스크에 저장되지 않음(`preferConsoleTab: 1`, 메모리는 False).
  - 원인 (코드): 런타임 옵션은 생성 레지스트리로 들어가는데(`CheatCodeGenerator.cs:351`), 설정 창은 `ApplyModifiedProperties`만 하고 다시 생성하지 않는다(`CheatEditorWindow.cs:159-173`). 자동 생성은 컴파일 뒤 에디트 모드에서만(`CheatCodeGenerator.cs:71-76`).
  - 후보 1: 런타임 설정이 바뀌면 다시 생성 (에디트 모드에서, Play 중이면 Play가 끝난 뒤).
  - 후보 2: 설정 창과 README에 "런타임 설정을 바꾸면 Generate Registry를 누른다" 안내.
  - 위험 (추정, 사용자 추가): 설정 에셋이 디스크에 저장되지 않아, 에디터를 다시 시작한 뒤 재생성하면 설정이 원래대로 돌아갈 수 있다. 확인은 수정 작업 때 한다.

## HB_InGameConsole (2026-10-18 preflight P5 뒤 보류 — 측정 후 판단)
- [ ] PF5-03 FPS 위젯을 꺼도 계측·문자열·클래스 갱신 계속 (`HB_InGameConsoleUI.UpdatePerformanceMetrics`)
- [ ] PF5-06 에디터 콘솔 툴바가 그릴 때마다 Define 조회·Split·HashSet (`HLogConsoleWindow.IsScriptingDefineSymbolDefined`)
- [ ] PF5-07 Watch 변경마다 트리거 수만큼 접미 키 문자열 생성 (`HLogConsoleWindow.CheckWatchTriggers`)
- [ ] PF5-08 초기화 반복 시 채널 탐색·PlayerLoop 조회 반복, 에디터 창 OnEnable의 중복 DiscoverAllChannels
- [ ] PF1 잔여: 메인 스레드 로그는 프레임 예산 밖 즉시 처리 / 고유 채널·태그·Watch 전체 상한 없음 / 값 형식 게터 박싱·Unity 객체 이름 문자열 매 평가 / 첫 열기(또는 이력 전체 교체 후) 최대 1000줄 동기 생성 / Watch 변경 시 에디터 창 스냅샷 전체 복사
- [ ] 측정 우선순위 (P5b): 모바일 로그 부하(메인·워커, 닫힘·열림, 상한 전후, 첫 열기 레이아웃) → Watch 상시 비용 → 생성기 무변경 저장
- [ ] IL2CPP·실기기에서 채널 Exception 태그(ThreadStatic + 동기 콜백 가정) 확인
- 출처: `docs/preflight/2026-10-18_ingameconsole/p5b_perf.md`, `p5a_check.md`
