# todo

## HB_Cheat

- [ ] **cheat_play가 Play 동안만 Game 뷰를 Play Unfocused로 바꾸고 끝나면 되돌리는 옵션 (기본 끔)** — A(CLI Play 동안 runInBackground) 커밋 뒤. A 범위 아님.
  - 먼저 조사: 값을 바꾸는 API가 공개인지 내부인지. 값이 레이아웃 파일·UserSettings·EditorPrefs 중 어디에 저장되는지.
  - 원칙 (runInBackground와 같음): 사람이 누른 Play는 건드리지 않는다. 어느 경로로 끝나도 되돌린다. 디스크에 흔적을 남기지 않는다.
  - 기준 자료: A 실제 확인에서 사람이 드롭다운을 바꾼 기록(바꾼 시각, 해시 비교 결과) — `docs/verify/2026-10-17_cli-run-in-background/30_*`
  - 출처: [계획서 결정 2](plans/2026-10-17_hb-cheat-cli-run-in-background.md)
