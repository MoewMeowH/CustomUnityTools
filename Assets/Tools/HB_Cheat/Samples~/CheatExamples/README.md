# Cheat Examples

HB_Cheat 치트를 종류별로 모은 예제입니다. 모든 치트 파일은 `#if DEV_CHEAT`로 감싸 있어서, 치트 심볼이 꺼져 있으면 컴파일되지 않습니다.

## 실행 방법

1. `Tools > HB > Cheat` → Settings에서 사용할 플랫폼을 `Turn On`합니다.
2. 컴파일이 끝나면 레지스트리가 자동으로 생성됩니다 (설정의 `Assembly CSharp Output Folder`, 기본 `Assets/Cheats/Generated`).
3. Play한 뒤 `F8`(PC), 화면의 `CHEAT` 버튼(모바일), 또는 InGameConsole의 `Cheat` 탭을 누릅니다.

## 파일

| 파일 | 내용 |
|---|---|
| `ExampleGameData.cs` | 가짜 게임 코드: 아이템 테이블, 플레이어 값, 패킷(`C2P_RequestCheat` / `P2C_ResultCheat`), 0.3초 뒤 응답하는 가짜 네트워크 |
| `ExampleCheatProviders.cs` | 목록 Provider: 테이블 전체, 조건 필터, 앞 인자에 따라 바뀌는 목록(`DependsOn`), 문자열 값 목록 |
| `ItemCheats.cs` | 서버 치트: 목록 + 개수, `DependsOn`, enum, 인자 없음 |
| `AccountCheats.cs` | 서버 치트: enum + `long`, 공백이 있는 문자열, 3단 경로 |
| `ActorCheats.cs` | 클라 치트: 반환형 `void` / `string` / `bool` / `CheatResult`, 토글, 텍스트 입력, 소수 step |
| `SystemCheats.cs` | 클라 치트: 배속, 씬 이동(문자열 목록), FPS, 테이블 리로드(목록 캐시 갱신), `Order` |
| `ExampleCheatTransport.cs` | `ICheatServerTransport` 연결과 `ReportServerResult` 응답 처리 |

`ExampleCheatTransport`는 프로젝트가 등록한 전송이 이미 있으면 등록하지 않습니다. 샘플을 다 봤다면 폴더째 지워도 됩니다. 이때 생성 레지스트리는 컴파일 후 자동으로 정리됩니다. 컴파일 에러가 남으면 `Delete Generated`를 누르세요.
