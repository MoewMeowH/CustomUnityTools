# Unity CLI 명령 규칙

`com.unity.pipeline`(Unity CLI · MCP)으로 부르는 에디터 명령을 만들 때 따르는 규칙입니다.
Pipeline 1.0 권장 형식을 그대로 따릅니다. 아래 예시를 기준으로 합니다.
실제 사용 예는 HB_Cheat `Editor/Cli/CheatCliCommands.cs`입니다. 다만 브리지 응답과 모양을 맞추려고 규칙 5의 예외를 둡니다 (규칙 5 참고).

## 예시

```csharp
using Unity.Pipeline.Commands;

internal static class ItemCliCommands
{
    private const string Group = "item";   // unity command item give --id 1001

    [CliGroupCommand(Group, "Give an item to the test account (Play Mode)", Effect = CommandEffect.Additive)]
    internal static ExitCode Give(CommandContext cmd,
        [CliArg("id", "Item id", Required = true)] int id,
        [CliArg("count", "How many (default 1)")] int count = 1)
    {
        if (!ItemTools.TryGive(id, count, out var result, out string code, out string error))
        {
            cmd.Log.Error($"[{code}] {error}");
            return code == "unknown_item" ? ExitCode.NotFound : ExitCode.Failed;
        }
        cmd.Result.Write(new { id, count = result.Count });
        return ExitCode.Success;
    }
}
```

## 규칙

1. **그룹**: `const string Group = "<도구 이름 소문자>"`를 두고 `[CliGroupCommand(Group, "설명", Effect = …)]`로 선언한다. 명령은 `unity command <그룹> <이름>`이 된다.
   - `[CliCommand]`는 쓰지 않는다.
   - Pipeline이 쓰는 그룹(`asset` · `build` · `editor` · `scene` · `test` 등)과 `global`은 쓰지 않는다. 같은 그룹 + 같은 이름이면 Pipeline 명령만 남고 우리 명령은 버려진다. 지금 겹치지 않아도 Pipeline이 나중에 같은 이름을 더하면 예고 없이 깨진다.
2. **모양**: `internal static ExitCode 동사(CommandContext cmd, …)`. 여러 프레임에 걸치면 `Task<ExitCode>`.
   - 명령 이름은 메서드 이름에서 나온다 (`ListCheats` → `list_cheats`). 메서드 이름을 바꾸면 명령 이름도 바뀌어 부르는 쪽이 깨진다.
3. **Effect 필수**: `ReadOnly`(조회) · `Idempotent`(다시 해도 같은 결과인 설정) · `Additive`(만들기 · 실행) · `Destructive`(지우기 · 덮어쓰기 · 임의 코드 실행). 가장 센 효과를 고른다. MCP 클라이언트는 이 값으로 실행 전에 확인을 물을지 정한다.
   - 예외: 테스트 계정 · 개발 빌드에서만 도는 명령(치트 등)은 `Additive`로 둔다.
4. **인자**: `[CliArg("kebab-case", "설명")]`, 설명 필수.
   - `format` · `json` · `fields` · `verbose` · `omit-nulls`는 실행기가 가져가는 이름이라 쓰지 않는다.
   - 이미 나간 명령의 인자 순서 · `Required`는 바꾸지 않는다 (위치 인자로 부르는 클라이언트가 깨진다). 새 인자는 선택 인자로 맨 뒤에 붙인다.
5. **결과**: 하나면 `cmd.Result.Write(객체)` 한 번, 목록이면 항목마다 `cmd.Result.WriteRecord(항목)`.
   - **JSON 문자열을 Write하지 않는다** — `--format json`에서 따옴표 친 문자열이 된다. 객체(익명 객체 · DTO · `JToken`)를 넘긴다.
   - JSON 문자열을 `JToken`으로 바꿀 때는 `DateParseHandling.None`으로 읽는다 (기본값은 날짜 모양 문자열을 Date로 바꾼다).
   - 목록을 `{ total, items }`처럼 객체로 감싸지 않는다. 예외: HB_Cheat는 브리지(`CheatProtocol`)와 같은 응답(`{"ok":true, …}`, 목록을 감싼 객체)을 그대로 Write한다 — 이미 쓰는 응답 계약을 CLI와 브리지가 함께 쓰기 위해서다. 새 명령은 따르지 않는다.
6. **실패**: `cmd.Log.Error("[<code>] <사람용 문장>")` 한 줄 + 종료 코드를 돌려준다. 실패 본문은 Write하지 않는다.
   - MCP 클라이언트는 실패 때 오류 문구만 받으므로, 기계가 갈라 볼 값(`code`)은 문구 맨 앞 `[…]`에 둔다.
   - 종료 코드: 대상이 없음 → `NotFound`(13), 인자 · 요청 형식 → `InvalidUsage`(2), 그 밖 → `Failed`(6).
   - 예상하지 못한 예외는 잡지 않는다 — Pipeline이 예외 문구와 `Failed`(`ArgumentException`이면 `InvalidUsage`)로 보고한다. 이때 문구에는 `[code]`가 없다.
7. **얇게**: 명령은 인자 받기 → 로직 호출 → Write/Log만 한다. 로직은 일반 어셈블리 함수로 두고 그 함수를 테스트한다.
   - `CommandContext`는 테스트에서 만들 수 없어서 명령 메서드는 직접 테스트하지 못한다.
8. **asmdef**: 명령은 Editor 전용 asmdef에 둔다. `references`에는 `Unity.Pipeline.Attributes`만 넣는다 (결과에 `JToken`을 쓰면 Newtonsoft는 자동 참조).
   - `versionDefines`로 `com.unity.pipeline` `1.0.0-pre.1` 이상일 때 심볼을 켜고, 그 심볼을 `defineConstraints`에 넣는다 — Pipeline이 없는 프로젝트에서도 컴파일이 깨지지 않는다.

```json
"defineConstraints": [ "MYTOOL_PIPELINE" ],
"versionDefines": [ { "name": "com.unity.pipeline", "expression": "1.0.0-pre.1", "define": "MYTOOL_PIPELINE" } ]
```

## 검사 테스트

`Assets/Tools/HB_Cheat/Tests/Cli/CliCommandConventionTests.cs`가 `HB.*` 어셈블리의 모든 명령을 훑어 어긴 것을 한꺼번에 알려 준다.
- 보는 것: 규칙 1(`[CliCommand]` 금지 · Pipeline 그룹 금지), 규칙 2의 `static` · 반환형 · 첫 인자 `CommandContext`(`internal`은 권장이라 보지 않음), 규칙 3 · 4.
- HB_Cheat 시험 어셈블리 안에 있어서 `HB_CHEAT` 심볼과 `com.unity.pipeline` 1.0.0-pre.1 이상이 있는 프로젝트에서만 돈다. 둘 중 하나가 없으면 실패하지 않고 그냥 빠진다.
- 다른 프로젝트에서는 이 파일을 테스트 asmdef(`Unity.Pipeline.Attributes` · 테스트 러너 참조)에 복사하고 `AssemblyPrefix`만 바꾼다.

## 확인

```bash
unity command pipeline list_commands --group <그룹>   # Effect가 readOnly · destructive · idempotent로 보이는지
unity command <그룹> <이름> --format json             # 결과가 객체로 나오는지, 실패면 [code] 문구 · 종료 코드
```
