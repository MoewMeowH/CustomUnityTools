# P4 — 지휘자 종합 보고 (HB_InGameConsole)

## 산출물
- 최종 코드: `final_code/` (패키지 실제 파일과 같음)
  - 런타임: `HLogCore.cs`(재작성), `LogChannel.cs`, `InGameConsole.cs`, `InGameConsoleSettings.cs`, `HB_InGameConsoleUI.cs`
  - 에디터: `HLogConsoleWindow.cs`, `LogChannelGeneratorWindow.cs`, `LogChannelGeneratorSettings.cs`
  - 문서: `README.md`
  - 검증용: `IGCCheckTests.cs.txt` (ToolTest6.3 PlayMode 테스트, 패키지에 포함하지 않음)
- 패키지 diff: 9개 파일, +1481 / -496.

## 두 수정안(P3)에서 고른 것과 이유
| 쟁점 | 채택 | 이유 |
|---|---|---|
| 심볼 OFF의 `GetChannel` | B: 빈 동작 채널 | `var ch = HLog.Get(..); ch.Enabled = ..` 같은 코드가 OFF에서 NRE 나지 않음 |
| Clear·Shutdown 경계 | A: 세대 번호 | 이미 큐에서 꺼낸 이전 로그도 기록하지 않음 (AddEntry가 세대 비교) |
| 프레임당 예산 | A: 로그 1000 / 태그 64 | 대기 큐 상한(1000)과 같아 한 프레임에 비울 수 있음 |
| 같은 값 변환 생략 | B: 값 형식·string만 | 불변이라 안전, 참조 형식은 매번 변환 |
| 닫힌 Watch·태그 UI | B: dirty 후 열 때 반영 | 로그 행과 같은 규칙 |
| 에디터 Watch 캐시 | A: 이벤트 무효화 | 공개 API 추가는 `WatchCount` 하나로 제한 |
| 씬 배치 컴포넌트(OFF) | B: `enabled = false` | 구체적 |
| Exception 태그 | A: 시험 통과를 조건으로 | T5 통과(에디터 Mono). IL2CPP 미확인 |
| 자동 생성 마커 | A: `ProjectSettings/HB_InGameConsole.LogChannels.generated.txt` | 이름에 용도 |
| 이벤트 구독자 격리 | A: 추가·제거 때만 배열 갱신(`EventList`) | 호출마다 `GetInvocationList` 할당 없음 |
| 대기 로그 큐 | 둘 다: 잠금 + 고정 용량 `Queue` | 별도 원자 카운터 제거 |

## 버린 것
- B의 공개 `WatchVersion` (공개 API 추가 최소화).
- A의 "게터 등록 세대 번호": 등록 객체 참조 동일성(`ReferenceEquals`)으로 같은 효과 — 교체·해제 시 객체가 바뀌거나 빠지므로.
- 열기 분할 생성·가상화(두 안 모두 범위 밖).
- 메인 스레드 식별을 최초 호출자로 확정하는 방식: 대신 `SubsystemRegistration`·에디터 `InitializeOnLoadMethod`에서 확정하고, 그 전에 불리면 `UnitySynchronizationContext` 여부로만 메인으로 인정(워커를 메인으로 등록하지 않음).

## 계획·확정과 다르게 한 것 / 추가한 것
- `HLogCore.Watch`·`Unwatch` 자체에도 `[Conditional("USE_HLOG")]` (직접 호출자의 인자 계산도 제거).
- 생성 템플릿의 최상위 `HLog.Watch`/`Unwatch`, `HLog.Core.Watch`/`Unwatch`에도 Conditional (기존 템플릿은 없었음).
- 구독자 예외 보고는 코어가 다시 수집하지 않음(재귀 방지). 스레드 위반 에러는 일반 로그라 콘솔에 보임.
- `InGameConsole`의 `Instance?.` 호출을 명시적 null 비교로 (UnityEngine.Object에 `?.` 금지 규칙).
- 설정 반사 실패 `catch { }`는 주석·타입 지정, 채널 탐색 실패는 에디터에서 경고 1회.

## 확인 (실측)
- 컴파일
  - ToolCreate (7000.0.0a8, USE_HLOG ON): exit 0. 경고는 기존 `CS0108` 하나.
  - ToolTest6.3 (6000.3.23f1, `file:` 패키지): ON exit 0 / OFF exit 0 (새 템플릿으로 재생성한 `LogChannels.cs` 포함).
- PlayMode (ToolTest6.3, ON): 8/8 통과
  - T1 워커 새 채널: 예외 없음, 태그 이벤트 메인 스레드
  - T2 워커에서 Watch·Clear: 상태 불변, API별 에러 1회
  - T3 자기 해제 게터 부활 없음, 값 Watch가 게터 해제
  - T4 UI 없이 게터 평가(3프레임 4회), owner 파괴 시 해제
  - T5 Exception 태그 메인·워커 `T5Net`, 다음 일반 로그 태그 없음
  - T6 Clear 뒤 대기 로그 0
  - T7 닫힘 행 0 → 열기 1000 → 닫기 후 로그 10개에도 1000 유지 → 재열기 1000(최신 반영), 링 버퍼 최근 1000개 순서 정확
  - T8 로그당 할당: +HLogCore ≈2.9KB, +닫힌 콘솔 UI ≈2.97KB (수정 전 ≈12.5KB)
- PlayMode (ToolTest6.3, OFF): 1/1 통과 — UI null·미초기화·기록/Watch/태그 0, `GetChannel` 비null.
- 첫 실행에서 T2·T5·T6이 실패한 원인은 테스트 하네스(`LogAssert` 무시 설정이 SetUp에서 본문까지 유지되지 않음)였고, 각 테스트 첫 줄에서 설정해 해결. 코드 변경 없음.

## 확인하지 못한 것
- IL2CPP·실기기(모바일)에서 Exception 태그, 열기 순간 1000줄 생성 비용.
- 인게임 UI 수동 동작(필터·검색·Watch 탭·외부 탭) — 에디터 플레이에서 사용자 확인 필요.
- 자동 생성 마커는 배치 모드에서 `delayCall`이 돌지 않아 미확인 — 에디터에서 확인 필요.
- 도메인 리로드 끈 상태의 에디터 창 캐시.
