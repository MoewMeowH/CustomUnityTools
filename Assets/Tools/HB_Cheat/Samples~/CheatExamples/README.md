# Cheat Examples

HB_Cheat 치트를 종류별로 모은 예제입니다.

- 치트 파일은 모두 `#if DEV_CHEAT`로 감싸 있어서, 치트 심볼이 꺼져 있으면 컴파일되지 않습니다.
- `ExampleGameData.cs`는 치트가 아니라 가짜 게임 코드라서 감싸지 않습니다.
- 프로젝트 심볼 이름(`User Define`)을 `DEV_CHEAT`가 아닌 이름으로 바꿨다면 샘플 파일의 `#if DEV_CHEAT`도 같이 바꿔야 합니다.

샘플에는 asmdef가 없으므로 Assembly-CSharp로 컴파일됩니다.

## 실행 방법

1. `Tools > HB > Cheat` → Settings에서 사용할 플랫폼을 `Turn On`합니다. 레지스트리 생성기는 현재 에디터 플랫폼(보통 Standalone)의 심볼이 켜져 있어야 동작합니다.
2. 컴파일이 끝나면 레지스트리가 자동으로 생성됩니다 (설정의 `Assembly CSharp Output Folder`, 기본 `Assets/Cheats/Generated`).
3. Play한 뒤 `F8`(PC), 화면의 `CHEAT` 버튼(모바일), 또는 InGameConsole의 `Cheat` 탭을 누릅니다.

자동화로도 확인할 수 있습니다 (`com.unity.pipeline` 필요, Play 중).

```bash
unity command cheat_run --id actor.level --args "level=30"
unity command cheat_run --id item.enchant --args "itemId=1001 level=3"   # 서버 치트: Pending → 0.3초 뒤 Succeeded
unity command cheat_status --execution <id> --session <session>
```

## 파일

| 파일 | 내용 |
|---|---|
| `ExampleGameData.cs` | 가짜 게임 코드: 아이템 테이블(`ItemTable`, `Reload`하면 `Version` 증가), 플레이어 값(`ExamplePlayer`), 패킷(`C2P_RequestCheat` / `P2C_ResultCheat`), 0.3초 뒤 응답하는 가짜 네트워크(`ExampleNetwork`) (아래 설명) |
| `ExampleCheatProviders.cs` | 목록 Provider: 테이블 전체(`ItemProvider`), 조건 필터(`EnchantableItemProvider`, 강화 가능한 아이템만), 앞 인자에 따라 바뀌는 목록(`EnchantLevelProvider`, `DependsOn`), 문자열 값 목록(`BuildSceneProvider`, Build Settings의 씬) |
| `ItemCheats.cs` | 서버 치트: 목록 + 개수(`Step = 10`), `DependsOn`, enum(서버로는 정수 값, `Epic` → 3), 인자 없음, `Order` |
| `AccountCheats.cs` | 서버 치트: enum + `long`(`Step = 1000`), 공백이 있는 문자열(`"..."`로 감쌈), 기본값이 있는 목록 인자, 3단 경로(`Account/Currency/Add`, `Account/Mail/Send`) |
| `ActorCheats.cs` | 클라 치트: 반환형 `void` / `string` / `bool` / `CheatResult`, 토글, 텍스트 입력, 인자 없음, enum + 소수 step(`Step = 0.5`), 3단 경로(`Actor/Stat/Set`) |
| `SystemCheats.cs` | 클라 치트: 배속(소수 step), 씬 이동(문자열 목록), FPS, 테이블 리로드(다음에 고르거나 실행할 때 목록을 새로 읽음), `Order` |
| `ExampleCheatTransport.cs` | `ICheatServerTransport` 연결과 `ReportServerResult` 응답 처리. `BeforeSceneLoad`에서 등록합니다. `ExampleNetwork.IsConnected`가 false면 전송 실패(`Failed`)가 됩니다. 독립 패널에 프로젝트 `PanelSettings`를 넣는 예는 주석으로 들어 있습니다. |

가짜 서버는 `item_` · `currency_` · `mail_`로 시작하는 명령만 성공(`Success`)으로 돌려주고, 나머지는 `InvalidCommand`로 돌려줍니다.

## 치트 목록

| id | 경로 | Target | 인자 |
|---|---|---|---|
| `item.add` | `Item/Add` | Server | `itemId`(목록), `count`(1~9999, 기본 1) |
| `item.enchant` | `Item/Enchant` | Server | `itemId`(강화 가능 목록), `level`(`itemId`에 따른 목록) |
| `item.grade` | `Item/Grade Up` | Server | `itemId`(목록), `grade`(`ItemGrade`, 기본 `Epic`) |
| `item.clear` | `Item/Clear Inventory` | Server | 없음 |
| `account.currency.add` | `Account/Currency/Add` | Server | `type`(`CurrencyType`), `amount`(1~1000000000, 기본 10000) |
| `account.currency.reset` | `Account/Currency/Reset` | Server | 없음 |
| `account.mail.send` | `Account/Mail/Send` | Server | `title`(문자열), `itemId`(목록, 기본 3001), `count`(1~999, 기본 1) |
| `actor.level` | `Actor/Level` | Local | `level`(1~100, 기본 1) |
| `actor.god` | `Actor/God Mode` | Local | `enabled`(bool, 기본 true) |
| `actor.heal` | `Actor/Heal` | Local | 없음 (이미 가득이면 실패) |
| `actor.nickname` | `Actor/Nickname` | Local | `nickname`(문자열) |
| `actor.stat.set` | `Actor/Stat/Set` | Local | `stat`(`StatType`), `value`(0~1000, step 0.5, 기본 10) |
| `actor.stat.reset` | `Actor/Stat/Reset` | Local | 없음 |
| `system.timescale` | `System/Time Scale` | Local | `scale`(0~10, step 0.25, 기본 1) |
| `system.loadscene` | `System/Load Scene` | Local | `sceneName`(씬 목록) |
| `system.fps` | `System/Target FPS` | Local | `fps`(15~240, step 15, 기본 60) |
| `system.reloadtables` | `System/Reload Tables` | Local | 없음 |

`ExampleCheatTransport`는 프로젝트가 등록한 전송이 이미 있으면 등록하지 않습니다. 샘플을 다 봤다면 폴더째 지워도 됩니다. 이때 생성 레지스트리는 컴파일 후 자동으로 정리됩니다. 컴파일 에러가 남으면 `Delete Generated`를 누르세요.
