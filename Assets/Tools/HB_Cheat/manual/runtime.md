# 패널 · 서버 전송 · 코드 API

## 패널

| 탭 | 내용 |
|---|---|
| Browse | 경로의 `/`마다 탭 줄을 하나씩 보여 줍니다. 그룹은 `이름  >` 탭, 마지막 마디는 치트 버튼입니다. 서버 치트 버튼은 왼쪽에 주황 테두리가 붙습니다. 위쪽 검색은 경로와 id로 찾습니다. |
| Favorites | 즐겨찾기한 치트만 보여 줍니다. 치트 제목 옆 `+ Fav` / `- Fav`로 바꾸며, `PlayerPrefs`(`HB_Cheat.Favorites`)에 저장됩니다. |
| History | 최근 실행을 새것부터 보여 줍니다. 상태 · 주체(패널이 아니면 `[CLI]` 등) · 설명 · 보낸 명령 · 메시지가 나옵니다. `Edit`는 그 값으로 치트를 다시 열고, raw는 `Reuse`로 Raw 탭에 채웁니다. 서버 큐가 멈추면 맨 위에 이유와 `Resume` 버튼이 나옵니다. |
| Raw | 문자열을 조합 없이 그대로 서버에 보냅니다(`Send`). |

- 치트를 고르면 인자가 칩(`이름: 값`)으로 나오고, 칩을 누르면 그 인자의 편집기만 열립니다. 값이 비어 있으면 `(select)`이고, 처음에는 비어 있는 첫 인자부터 엽니다.
- 숫자: 큰 값 표시, `±step` · `±step×10` 버튼, 범위가 있으면 `Min` · `Max`, 패널 키패드(정수형은 `.` 없음, `+/-` · `<-` · `C`).
- 목록: 검색 칸, `걸러진 수/전체 수`, 가상화 리스트. 고르면 다음 인자로 넘어갑니다. 라벨과 값이 다르면 `라벨   (값)`으로 보입니다.
- `bool`은 `True` / `False` 버튼, `string`은 텍스트 칸(OS 키보드)입니다.
- 아래 미리보기에 `Send: <명령>`(서버) 또는 `Run: <경로 인자=값 ...>`(로컬)이 나오고, 검증에 실패하면 `(incomplete) <이유>`와 함께 `Run` 버튼이 꺼집니다.
- 치트를 다시 열면 마지막으로 쓴 값으로 채웁니다(없으면 기본값).
- 레이아웃: 패널 너비가 640 이상이고 가로가 더 길면 좌우 2단, 아니면 위아래입니다. 독립 패널 창은 화면 너비 1100 이상이면 오른쪽 60%, 아니면 안전 영역 안 전체입니다. 안전 영역과 OS 키보드 높이만큼 여백을 둡니다.
- 독립 패널은 자체 `UIDocument`(정렬 순서 1001)로 뜨고, `Close`로 닫습니다. 콘솔 탭에서는 `Close` 버튼을 숨깁니다.

## 서버 전송 연결

```csharp
public sealed class PacketCheatTransport : ICheatServerTransport
{
    public bool TrySend(string command, out string error)
    {
        error = null;
        if (!Network.IsConnected) { error = "Not connected"; return false; }
        Network.Send(new C2P_RequestCheat { Command = command });
        return true;
    }
}

// 초기화 코드 어딘가에서
CheatSystem.ServerTransport = new PacketCheatTransport();

// 서버 응답 핸들러에서 (네트워크 스레드여도 됨: 다음 프레임에 메인 스레드에서 처리)
CheatSystem.ReportServerResult(packet.Result == Result.Success, packet.Result.ToString());

// 재접속에 성공했을 때 (시간 초과로 멈춘 큐 재개)
CheatSystem.ResumeServerQueue();
```

- 응답에 요청 식별자가 없으므로 **서버 치트는 한 번에 하나만 전송**합니다. 나머지는 순서대로 대기합니다 (`Queued`).
- `TrySend` 안에서 `ReportServerResult`를 바로 불러도 됩니다. `TrySend`가 false를 돌려주거나 예외를 내면 그 실행은 `Failed`(이유는 `error`)입니다.
- 전송이 등록돼 있지 않거나, `CheatSystem`이 초기화되지 않았거나, 명령 문자열이 비어 있으면 서버 치트는 `Failed`입니다.
- `ReportServerResult`는 처리했거나(메인 스레드가 아니면 다음 프레임 처리로 넘기고) true, 메인 스레드에서 처리했는데 기다리는 건이 없으면 false를 돌려줍니다.
- 응답이 `ServerTimeoutSeconds`(기본 10초) 안에 오지 않으면 `TimedOut`이 되고 **서버 큐가 멈춥니다**. 늦게 온 응답이 다음 요청의 결과로 잘못 처리되지 않게 하기 위함입니다.
  - 멈춘 뒤 늦은 응답이 도착하면 그 응답은 버리고 자동으로 재개합니다.
  - 응답이 끝내 오지 않으면(연결 끊김 등) 재접속 처리에서 `CheatSystem.ResumeServerQueue()`를 부르거나, 패널 **History** 탭의 **Resume** 버튼을 누릅니다. 재접속 전에 재개하면 늦은 응답이 다음 결과로 처리될 수 있습니다. 재개할 때 아직 처리하지 않은 응답은 이전 연결의 것으로 보고 버립니다.
  - 치트 시스템이 종료되면(씬 전환 등) 대기 중인 서버 치트는 `Failed`("Cheat system shut down")로 끝납니다. 응답을 기다리던 건이 있었다면 다시 초기화해도 멈춘 상태로 시작합니다.
  - 상태는 `CheatSystem.IsServerQueuePaused`, `ServerQueuePauseReason`, `QueuedServerCount`, `InFlightServerExecution`, `ServerQueueChanged`로 확인합니다.
- 서버 없이 확인할 때는 `CheatSystem.ServerTransport = new CheatLogTransport();`을 쓰면 명령이 로그로만 출력됩니다. 응답은 `ReportServerResult`로 직접 넣습니다(넣지 않으면 시간 초과).

## 코드 API

| API | 내용 |
|---|---|
| `CheatSystem.Initialize()` | 러너(`[HB_Cheat]` GameObject)와 패널을 만듭니다. 이미 초기화됐거나 종료 중이면 무시합니다. |
| `CheatSystem.IsInitialized` · `IsHostedInConsole` | 초기화 여부, 패널이 콘솔 탭으로 붙었는지. |
| `CheatSystem.Open()` · `Close()` · `Toggle()` · `IsOpen` | 패널 열기 · 닫기. 콘솔 탭이면 콘솔의 `Cheat` 탭을 엽니다. |
| `CheatSystem.ShowCheat(id, values = null)` | 패널을 열고 그 치트를 고릅니다(값을 넘기면 그 값으로 채움). 초기화 전이거나 없는 id면 false. |
| `CheatSystem.Execute(definition, values[, source])` | 실행합니다. 주체를 빼면 `Code`. 로컬 치트는 초기화 전에도 실행되고, 서버 치트는 초기화가 필요합니다. 인자 검증에 실패해도 `Failed` 실행으로 히스토리에 남습니다. |
| `CheatSystem.ExecuteRaw(command[, source])` | 문자열을 그대로 서버로 보냅니다(앞뒤 공백 제거). |
| `CheatSystem.Validate(definition, values, out error[, out code])` | 인자 검증(빈 값, 범위, 목록 포함). `code`는 `CheatErrorCode` 값입니다. |
| `CheatSystem.ReportServerResult(success, message)` · `ResumeServerQueue()` | 서버 응답 보고, 멈춘 큐 재개 (위 참고). |
| `CheatSystem.History` · `FindExecution(id)` · `SessionId` | 히스토리(오래된 것부터), id로 찾기, 실행 id 공간의 세션 값(8자리 16진). |
| `CheatSystem.GetLastValues(id)` | 이 치트에 마지막으로 쓴 값(복사본, 없으면 null). |
| `CheatSystem.GetOptions(definition, argIndex, values, out error)` · `InvalidateOptions()` | 목록 후보 조회(캐시), 캐시 전체 버리기(테이블 리로드 뒤 등). |
| `CheatSystem.IsFavorite(id)` · `SetFavorite(id, bool)` | 즐겨찾기 조회 · 변경(`PlayerPrefs` 저장). |
| `CheatSystem.ExecutionChanged` · `FavoritesChanged` · `ServerQueueChanged` | 실행 추가 · 상태 변경, 즐겨찾기 변경, 서버 큐 멈춤 · 재개 이벤트. 구독자 하나의 예외는 다른 구독자와 큐 진행을 끊지 않습니다(실행 · 큐 이벤트). |
| `CheatSystem.RegisterBridge(bridge)` | `ICheatBridge` 등록. 초기화 때 `Start`, 종료 때 `Stop`이 불립니다(이미 초기화됐으면 바로 `Start`). |
| `CheatSystem.Options` · `ServerTransport` · `ConsoleHost` | 런타임 옵션, 서버 전송, 패널 호스트(`ICheatPanelHost`, InGameConsole 연동이 넣음). |
| `CheatRegistry.Register` · `TryGet` · `All` · `Count` · `Changed` | 등록된 치트 목록. 같은 id는 교체합니다(경로가 다르면 경고). `All`은 부모 경로 → `Order` → 경로 → id 순입니다. |
| `CheatCommand.Join(name, args...)` · `Format(value)` | 서버 치트 문자열 조립 (정의 규칙 참고). |
| `CheatValueParser.TryParse` · `ToText` | 문자열 ↔ 인자 값 변환(InvariantCulture). |
| `CheatResult.Ok(message)` · `Fail(message)` | 로컬 치트의 결과. |
| `CheatExecution` | `Id` · `Definition`(raw면 null) · `Values` · `Command` · `Source` · `Session` · `Status` · `Message` · `StartedAt` · `SentAt` · `FinishedAt` · `IsDone` · `IsServer` · `Describe()`. 시각은 `Time.realtimeSinceStartup`입니다. |

## 🖥 InGameConsole 연동

- `com.hb.ingameconsole`이 설치돼 있으면 연동 어셈블리가 자동으로 컴파일됩니다 (`versionDefines`). 콘솔이 패키지가 아니라 Assets 폴더에 있다면, 치트 심볼을 켤 때 `HB_CHEAT_HAS_CONSOLE`도 함께 켜지고 끌 때 함께 꺼집니다(설정 창에 안내가 보임).
- `CheatSystem`이 초기화되는 시점에 콘솔이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다. 콘솔이 자동 시작 설정이면 순서와 관계없이 콘솔을 먼저 초기화합니다. 콘솔이 없거나, 수동 초기화 설정인데 아직 초기화되지 않았거나, 콘솔 UI가 없어 탭을 붙이지 못하면 **독립 패널**로 뜹니다.
- 콘솔 탭으로 들어가면 **독립 패널과 화면의 `CHEAT` 버튼은 만들지 않습니다.** 열 때는 콘솔을 열고 `Cheat` 탭을 고르거나 열기 키(`F8`)를 누릅니다(콘솔의 `Cheat` 탭이 열림). `CheatSystem.Close()`는 콘솔을 닫습니다. 콘솔이 있어도 독립 패널 · `CHEAT` 버튼을 쓰려면 `Tools > HB > Cheat` 설정의 **Prefer Console Tab**을 끕니다 (코드로는 `CheatSystem.Initialize` 전에 `CheatSystem.Options.PreferConsoleTab = false`).
- 치트 시스템이 종료되면 콘솔에서 `Cheat` 탭을 뗍니다(콘솔이 더 오래 살아 있어도 패널을 붙잡지 않음).
- 독립 패널에서 한글이 깨지면 `CheatSystem.Options.PanelSettings`에 프로젝트 UI의 PanelSettings(테마, 한글 폰트 포함)를 지정하세요. 기본은 코드로 만든 PanelSettings(1280×720 기준 화면 크기 맞춤)와 내장 폰트, OS 폴백입니다.
