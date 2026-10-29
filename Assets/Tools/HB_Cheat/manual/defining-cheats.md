# 치트 정의 규칙

## 어트리뷰트와 속성

| 항목 | 내용 |
|---|---|
| `[Cheat(id, path)]` | `static` 메서드(public/internal)에 붙입니다. |
| `Target` | `Local`(기본) 또는 `Server` |
| `Description` · `Order` | 패널 제목 아래 설명, 같은 탭 안 정렬 순서(작을수록 앞, 기본 0). |
| `[CheatArg("라벨")]` | 패널에 보일 인자 이름입니다. 없으면 매개변수 이름을 씁니다. |
| `[CheatRange(min, max, Step = n)]` | 숫자 범위와 스테퍼 간격(기본 1)을 지정합니다. 숫자가 아닌 인자에 붙이면 경고, `min > max`는 에러입니다. |
| `[CheatOptions(typeof(Provider), DependsOn = ...)]` | 목록 선택입니다 ([Provider](#provider)). |

### id와 path

- **id는 고정 식별자**입니다. 즐겨찾기, 히스토리, 자동 QA가 사용하며 path를 옮겨도 바꾸지 않습니다.
- path의 `/`는 탭을 나누고, 마지막 마디가 치트 이름입니다.

### Target

- `Local`(기본): 클라에서 메서드를 실행합니다.
- `Server`: 메서드가 반환한 문자열을 `ICheatServerTransport`로 보냅니다.
- **Server 치트 메서드는 명령 문자열만 만들고 부작용이 없어야 합니다.** 패널 미리보기가 인자 값이 바뀔 때마다 메서드를 불러 보낼 문자열을 보여 주기 때문입니다.

## 반환형과 인자

| 항목 | 내용 |
|---|---|
| 반환형 (Local) | `void`(예외 없으면 성공) · `bool`(false면 실패) · `string`(성공 + 메시지) · `CheatResult`(직접 지정). 예외는 `Failed`와 예외 메시지가 됩니다. |
| 반환형 (Server) | `string`(보낼 명령)만 |
| 인자 타입 | `int`, `long`, `float`, `double`, `bool`, `string`, `enum`. `ref` · `out` · `params`는 안 됩니다. |
| 입력 방식 | 숫자는 스테퍼와 패널 키패드(OS 키보드 없음), `bool`은 토글, `enum`과 `[CheatOptions]`는 목록(검색, 가상화), `string`은 텍스트 입력 |
| `enum` | 정의된 이름이 목록이 되고, 서버 문자열에는 정수 값이 들어갑니다. 자동화 입력은 이름(대소문자 무시) 또는 정의된 정수 값을 받습니다. |
| 기본값 | C# 매개변수 기본값을 그대로 씁니다 (아래 설명). |

매개변수 기본값이 없으면 다음 값을 씁니다.

- 숫자 0 (범위 안으로 맞춤)
- `bool` false, `string` 빈 문자열, `enum` 첫 값
- Provider 목록 인자는 고를 때까지 비어 있습니다.

## Provider

- Provider는 `ICheatOptionProvider` 구현 + public 매개변수 없는 생성자가 필요합니다.
- `DependsOn`에는 **앞쪽** 매개변수 이름만 쓸 수 있습니다. 앞 인자가 바뀌면 이 인자는 초기화됩니다.
- 앞 인자 값은 `context.Get<T>(이름)` / `TryGet`으로 읽습니다.
- 메인 스레드에서 불립니다.
- 다시 수집하는 때: 치트를 고를 때(패널), 실행할 때(CLI `cheat_run` 포함), CLI로 후보를 조회할 때(`cheat_options`)
- 같은 치트를 편집하는 동안은 (치트, 인자)마다 캐시합니다. `DependsOn` 값이 바뀌면 다시 수집합니다.
- 열려 있는 목록을 바로 바꾸려면 `CheatSystem.InvalidateOptions()`를 부르세요.
- Provider 인스턴스는 레지스트리마다 타입당 하나입니다.
- 예외가 나면 그 목록은 비고 에러로 보고됩니다.

## CheatCommand.Join

문화권과 무관하게 직렬화합니다.

- 공백이나 따옴표 · `\`가 있는 문자열과 빈 문자열은 `"..."`로 감쌉니다 (`\"` · `\\` 이스케이프).
- `bool`은 `true`/`false`, enum은 정수로 바꿉니다.

## 잘못된 정의

레지스트리를 생성할 때 잘못된 정의(private, 미지원 타입, 중복 id, 잘못된 DependsOn 등)는 **에러로 보고되고 생성에서 제외**됩니다.
`Cheats` 탭에서 확인할 수 있습니다.

에러:

- 빈 id · path
- static 아님, 제네릭, public/internal 아님
- 선언 타입이 private · protected 중첩
- 런타임(플레이어) 어셈블리가 아님 (Editor 어셈블리 등)
- 반환형, 지원하지 않는 인자 타입
- 생성 코드에서 접근할 수 없는 enum · Provider
- 중복 id (겹친 치트 모두 제외)

경고 (생성은 함):

- 여러 치트가 같은 path를 씀
- 숫자가 아닌 인자의 `[CheatRange]`
- 예약 인자 이름(`query`, `limit`)
