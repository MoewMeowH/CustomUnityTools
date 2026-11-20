# 자동화 (Unity CLI)

`com.unity.pipeline` 1.0.0-pre.1 이상이 있으면 에디터 Play Mode용 명령이 등록됩니다. 결과는 JSON입니다.

```bash
unity command cheat_list
unity command cheat_options --id item.enchant --arg level --args "itemId=1001"
unity command cheat_run --id item.enchant --args "itemId=1001 level=15"
unity command cheat_status --execution 3 --session 1a2b3c4d   # 서버 치트는 Pending → Succeeded / Failed / TimedOut
unity command cheat_history --limit 10
unity command cheat_raw --command "item_add 1001 10"  # 기본 꺼짐 (raw 전송 참고)
unity command editor.playmode start   # Play 시작 (Pipeline 기본 명령, Play 시작 · 종료 참고)
unity command editor.playmode stop    # Play 종료
```

## 명령

| 명령 | 인자 | 내용 |
|---|---|---|
| `cheat_list` | 없음 | 등록된 치트와 인자 목록 (Play Mode에서만 — 치트는 Play 진입 때 등록됩니다). |
| `cheat_options` | `--id` · `--arg`(필수), `--args`(앞 인자 값), `--query`(라벨 · 값 검색), `--limit`(기본 200) | 인자의 선택 목록. |
| `cheat_run` | `--id`(필수), `--args` | 실행. 빠진 인자는 기본값을 씁니다. 서버 치트는 `Pending`으로 시작합니다. |
| `cheat_status` | `--execution`(필수), `--session` | 실행 상태 조회. |
| `cheat_history` | `--limit`(기본 20) | 최근 실행. |
| `cheat_raw` | `--command`(필수) | raw 전송 (기본 꺼짐, [raw 전송](#raw-전송)). |

- 모든 명령은 Play Mode가 아니거나 초기화되지 않았으면 `not_initialized`입니다.
- 코드에서 직접 부르는 `CheatProtocol.Status` · `History`는 초기화 전에도 남은 기록을 읽습니다 (CLI는 위 규칙으로 막음).
- `--args`는 `k=v`를 공백으로 나눕니다. 값에 공백이 있으면 `"..."`로 감싸고, 안의 `"`는 `\"`로 씁니다.

## 실기기 연결 (브리지)

실기기 연결(ADB, 로컬 소켓 등)은 `ICheatBridge`를 구현해 `CheatSystem.RegisterBridge`로 등록합니다.
받은 한 줄을 `CheatProtocol.Handle(line)`에 넘기고 결과 JSON을 돌려주면 됩니다.

받는 줄의 형식 (동사는 대소문자 무시):

```text
list
options <id> <arg> [k=v ...] [query=텍스트] [limit=N]
run <id> [k=v ...]
raw <명령...>
status <id> [session=S]
history [limit=N]
```

- `Handle`은 **메인 스레드에서** 부릅니다. 소켓 스레드에서 받았다면 메인 스레드로 넘긴 뒤 호출합니다.
- 브리지 경로의 실행 주체는 `Bridge`로 남습니다. `CheatProtocol.Handle(line, source)`로 주체를 지정할 수도 있습니다.

## 응답 JSON

- 성공은 `{"ok":true,...}`, 실패는 예외가 아니라 항상 `{"ok":false,"error":"...","code":"..."}`입니다.
- **인자 · 요청 문제는 `ok:false` + `code`** 입니다.
- **실행 결과 문제**는 `ok:true` + `execution.status`(`Failed` / `TimedOut`)입니다.
  로컬 치트의 실패 · 예외, 서버 응답 실패, 전송 실패, 시간 초과가 여기에 들어갑니다.
- 범위 밖 · 목록에 없는 값 같은 인자 문제는 자동화 경로에서 실행을 만들지 않습니다. 패널은 기존대로 실행한 뒤 `Failed`로 남깁니다.
- 숫자가 NaN · 무한대면 `null`로 씁니다 (예: `cheat_list`에서 `[CheatRange]`의 `min`/`max`를 무한대로 둔 경우).

### 오류 코드

`code`는 **계약**입니다. 이름을 바꾸지 않습니다.
`error` 문구는 사람용이라 바뀔 수 있으니 분기는 `code`로 합니다. 상수는 `HB.Cheat.CheatErrorCode`에 있습니다.

| code | 언제 나오나 |
|---|---|
| `not_initialized` | Play Mode가 아니거나, Play Mode 중 재컴파일 등으로 CheatSystem 초기화가 풀림 |
| `session_changed` | `--session`(브리지 `session=`)이 현재 세션과 다름. Play Mode 재시작 · 재컴파일로 실행 id가 1부터 다시 발급됨 |
| `raw_disabled` | 자동화 경로(CLI · 브리지)의 raw가 꺼져 있음 |
| `evicted_execution` | 현재 세션에서 실행됐지만 히스토리 용량으로 지워진 실행 id |
| `unknown_execution` | 현재 세션에서 발급된 적 없는 실행 id (세션을 넘기면 이전 세션 id는 `session_changed`) |
| `empty_command` | 브리지에 빈 줄 |
| `unknown_command` | 브리지의 모르는 동사 |
| `invalid_usage` | 브리지 동사의 위치 인자(치트 id · 인자 이름 · 실행 id)가 없거나 형식이 틀림 |
| `malformed_argument` | `k=v` 형식이 아닌 토큰 (CLI `--args`, 브리지) |
| `unknown_cheat` | 등록되지 않은 치트 id |
| `unknown_argument` | 치트에 없는 인자 이름 (`--arg`, `--args`의 키) |
| `argument_without_options` | 선택 목록이 없는 인자에 `options` 요청 |
| `unparsable_value` | 값을 인자 타입으로 바꿀 수 없음 (예: int 인자에 `abc`) |
| `missing_argument` | 기본값 없는 인자를 넣지 않음 |
| `out_of_range_value` | 숫자가 `[CheatRange]` 범위 밖 (NaN 포함) |
| `unlisted_value` | 값이 선택 목록에 없음 |
| `option_provider_failed` | 선택 목록 Provider가 예외를 냄 |
| `empty_raw_command` | raw 명령이 비어 있음 |
| `internal_error` | 예상하지 못한 예외 |

처리는 쓰는 쪽이 정합니다. 예를 들어 자동화는 `ok:false`면 `code`를 기록하고 멈춘 뒤 사람에게 넘깁니다.

Unity CLI 프레임워크가 명령을 부르기 전에 거절하는 오류는 이 JSON이 아니라 CLI 봉투로 옵니다. `code`가 없습니다.
필수 인자 누락, `--execution abc` 같은 타입 오류가 여기에 들어갑니다.

```json
{"success":false,"command":"unity command cheat_status","data":null,
 "errors":[{"code":"INVALID_COMMAND_ARGS","message":"... --execution에는 Int32이(가) 필요하지만 abc이(가) 지정되었습니다."}]}
```

### cheat_list 응답

- 치트마다 `id` · `path` · `target` · `description` · `favorite` · `args`가 있습니다.
- 인자마다 다음 값이 있습니다.
  - `name` · `label` · `type`(enum은 enum 타입 이름)
  - `editor`(`Number` / `Toggle` / `Options` / `Text`) · `default`(문자열) · `hasOptions`
  - 범위가 있으면 `min` · `max`, 의존하면 `dependsOn`(인자 이름 배열)

```json
{"ok":true,"cheats":[{"id":"item.enchant","path":"Item/Enchant","target":"Server","description":null,"favorite":false,
  "args":[{"name":"itemId","label":"아이템","type":"Int","editor":"Options","default":"","hasOptions":true},
          {"name":"level","label":"강화","type":"Int","editor":"Options","default":"","hasOptions":true,"dependsOn":["itemId"]}]}]}
```

### cheat_options 응답

```json
{"ok":true,"total":N,"options":[{"value":"1001","label":"..."}]}
```

- `total`은 검색 · 개수 제한 전 전체 후보 수입니다.
- `value`는 `--args`에 그대로 넣을 수 있는 문자열입니다.

### run · raw · status · history 응답

세션과 서버 큐 상태가 붙습니다. `history`는 `"history":[실행...]` 배열(오래된 것부터)입니다.

```json
{"ok":true,"session":"1a2b3c4d",
 "serverQueue":{"paused":true,"reason":"#7 timed out","queued":1,"inFlight":null},
 "execution":{"id":8,"cheatId":"item.clear","target":"Server","source":"Cli",
              "status":"Pending","done":false,"blocked":true,"message":"Paused: ...","command":"item_clear","describe":"..."}}
```

| 필드 | 뜻 |
|---|---|
| `session` | 실행 id 공간의 식별자 (아래 설명) |
| `serverQueue` | `paused`(멈춤), `reason`(멈춘 이유, 아니면 null), `queued`(전송을 기다리는 수, 전송 중 제외), `inFlight`(응답을 기다리는 실행 id, 없으면 null) |
| `cheatId` | raw 실행이면 null |
| `command` | 서버로 보낸 문자열 (로컬이면 null) |
| `describe` | `경로 인자=값 ...` 또는 raw 명령 |
| `source` | 실행 주체 `Panel` / `Cli` / `Bridge` / `Code`. 패널 히스토리에도 패널이 아닌 실행은 `[CLI]`처럼 표시됩니다. |
| `done` | 실행이 끝남 (`Succeeded` / `Failed` / `TimedOut`) |
| `blocked` | 끝나지 않았는데 서버 큐가 멈춰 **스스로는 진행되지 않는** 서버 실행 (아래 설명) |

- `session`: Play Mode 재시작이나 Play Mode 중 재컴파일로 정적 상태가 초기화되면 id가 1부터 다시 발급되고 세션 값도 바뀝니다.
  씬 전환에 따른 재초기화(`Shutdown` → `Initialize`)로는 바뀌지 않습니다.
- `blocked`: 큐는 늦은 응답이 오거나, 사람이 패널 History 탭의 Resume(또는 재접속 코드의 `CheatSystem.ResumeServerQueue`)을 눌러야 진행됩니다.
  재접속 전에 재개하면 늦은 응답이 다음 결과로 붙기 때문에 자동화용 재개 명령은 일부러 두지 않았습니다.

## 폴링

- `cheat_run` 응답의 `execution.id`와 `session`을 저장하고 `cheat_status --execution <id> --session <session>`으로 조회합니다.
- **`--session`을 빼면** run과 status 사이에 초기화가 끼었을 때 다른 실행의 결과를 받을 수 있습니다.
- 끝나지 않은 실행은 히스토리에서 지워지지 않습니다.
  최근 `HistoryCapacity`건 + 끝나지 않은 실행을 남기므로 그만큼 용량을 넘을 수 있습니다.
- 정리는 새 실행이 들어올 때만 하므로 끝난 직후에는 조회할 수 있습니다. 끝난 실행은 새 실행이 용량 이상 쌓이기 전에 조회합니다.
- 예: 자동화는 `done`이면 결과를 읽고, `blocked`이거나 `ok:false`면 멈추고 사람에게 넘깁니다.

## Play 시작 · 종료

Pipeline 기본 명령 `editor.playmode start` / `stop`을 씁니다 (예전의 `cheat_play` · `cheat_stop`은 없앴습니다).

- `editor.playmode start`는 그 Play 동안 `Application.runInBackground`를 켜고 Play가 끝나면 원래 값으로 되돌립니다 (Pipeline 1.0.0-pre.1부터). Unity 창이 OS 맨 앞에 없어도 Play 프레임이 멈추지 않습니다.
- 프로젝트 값을 그대로 두려면 `--run-in-background false`를 줍니다.

## raw 전송

자동화 경로의 raw는 기본으로 꺼져 있습니다.

`cheat_raw`와 브리지 `raw`는 서버로 임의 문자열을 보내므로 **기본으로 거절**합니다 (`code: "raw_disabled"`). 여는 방법은 둘입니다.

- `Tools > HB > Cheat` 설정의 **Allow Raw From Automation**을 켭니다.
- `CheatSystem.Options.AllowRawFromAutomation = true`를 넣습니다.

패널 Raw 탭과 코드의 `CheatSystem.ExecuteRaw`는 영향이 없습니다.

브리지 `raw`는 동사 뒤의 원문을 그대로 보냅니다 (따옴표 유지, 앞뒤 공백 제거).

## 예약 인자 이름

`query`, `limit`은 프로토콜 옵션으로 씁니다. 그래서 치트 인자 이름으로 쓰면 자동화에서 값을 넣을 수 없습니다.
`Tools > HB > Cheat`의 Cheats 탭에 경고로 표시합니다 (`Generate Registry`를 누르면 Console에도 남음).
