# HB_Cheat

개발·QA용 치트 패널입니다. 모바일 우선이며 PC도 지원합니다. 치트는 어트리뷰트로 정의하고, 서버 치트 문자열을 조합해 전송할 수 있습니다. 릴리즈 빌드에서는 엄격하게 제외되며, Unity CLI 자동화를 지원합니다. (Unity 6.3+, 6 / 7)

---

## 📦 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat
```

- 버전 고정: URL 끝에 `#<태그>` 또는 `#<전체 40자 커밋 해시>`를 붙입니다. 짧은 해시(예: `#869e2614`)는 UPM이 받지 않습니다.

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat#<40자 커밋 해시>
```

UPM 패키지는 읽기 전용입니다. 그래서 **치트 정의, 설정, 생성 코드는 모두 프로젝트 `Assets` 아래**에 둡니다.

필수 의존 패키지는 없습니다. 아래 패키지가 있으면 asmdef `versionDefines`로 해당 기능이 켜집니다.

| 패키지 | 켜지는 심볼 | 켜지는 기능 |
|---|---|---|
| `com.unity.inputsystem` | `HB_CHEAT_INPUT_SYSTEM` | 열기 키를 Input System `Key`로 읽습니다. 없으면 구 Input Manager `KeyCode`로 읽습니다. 패키지가 없어도 `HB.Cheat` asmdef의 `Unity.InputSystem` 참조는 Unity가 무시하므로 컴파일 에러가 나지 않습니다. |
| `com.unity.pipeline` | `HB_CHEAT_PIPELINE` | Unity CLI 명령 어셈블리(`HB.Cheat.Editor.Cli`)가 컴파일됩니다. |
| `com.hb.ingameconsole` | `HB_CHEAT_HAS_CONSOLE` | InGameConsole 연동 어셈블리(`HB.Cheat.InGameConsole`)가 컴파일됩니다. |

---

## 🚀 시작하기

1. `Tools > HB > Cheat`를 엽니다. 탭은 **Settings** · **Cheats** 두 개입니다.
2. **Settings** 탭의 **Cheat Defines**에서 플랫폼별 `Turn On`을 누릅니다(Standalone · Android · iOS, 현재 활성 플랫폼이 그 밖이면 — WebGL · Dedicated Server 등 — 그 플랫폼 줄도 보입니다). `HB_CHEAT`(패키지)와 `DEV_CHEAT`(프로젝트, 이름 변경 가능 — 영문자 · 숫자 · `_`만, 숫자로 시작 불가. 규칙에 안 맞으면 경고 후 `DEV_CHEAT`)가 함께 켜집니다. 상태는 `ON` / `OFF` / `MISMATCH`(한쪽만 켜짐)로 보입니다.
3. 설정 에셋이 없으면 기본값으로 동작합니다. `Create Settings Asset`을 누르면 `Assets/Editor/HBCheat/HBCheatSettings.asset`을 만듭니다(Editor 폴더라 빌드에 들어가지 않음).
4. `Class Name`을 적고 `Create Cheat File`로 치트 파일을 만듭니다. 생성 위치는 `Cheat Folder`(기본 `Assets/Cheats`)입니다.
5. 컴파일이 끝나면 레지스트리(`HBCheatRegistry.g.cs`)가 자동으로 생성됩니다.
6. Play를 누르면 열 수 있습니다. PC는 `F8`, 모바일은 화면의 `CHEAT` 버튼(끌어서 이동 가능)을 씁니다. InGameConsole이 있으면 콘솔의 `Cheat` 탭으로 들어갑니다.

```csharp
#if DEV_CHEAT   // Assembly-CSharp에 둘 때 (asmdef 방식이면 필요 없음)
using System.Collections.Generic;
using HB.Cheat;

public static class ItemCheats
{
    // Local: 클라에서 직접 실행. 반환형 void / bool / string(메시지) / CheatResult
    [Cheat("actor.level", "Actor/Level")]
    public static void SetLevel([CheatRange(1, 100)] int level) => Player.Local.SetLevel(level);

    // Server: 반환한 문자열을 서버로 보낸다 (패널 미리보기가 값이 바뀔 때마다 부르므로 문자열만 만들 것)
    [Cheat("item.enchant", "Item/Enchant", Target = CheatTarget.Server)]
    public static string Enchant(
        [CheatArg("아이템"), CheatOptions(typeof(ItemTableProvider))] int itemId,
        [CheatArg("강화"), CheatOptions(typeof(EnchantLevelProvider), DependsOn = new[] { "itemId" })] int level)
        => CheatCommand.Join("item_enchant", itemId, level);   // "item_enchant 1001 15"
}

public sealed class ItemTableProvider : ICheatOptionProvider
{
    // 치트를 고를 때 · 실행할 때 다시 불린다 (테이블을 다시 로드해도 따로 할 일 없음)
    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        foreach (var item in ItemTable.All)
            results.Add(new CheatOption(item.Id, item.Name));
    }
}

public sealed class EnchantLevelProvider : ICheatOptionProvider
{
    public void Collect(CheatOptionContext context, List<CheatOption> results)
    {
        if (!context.TryGet("itemId", out int itemId)) return;   // 앞 인자를 아직 안 골랐으면 빈 목록
        var item = ItemTable.Get(itemId);
        if (item == null) return;
        for (int i = 1; i <= item.MaxEnchant; i++) results.Add(new CheatOption(i, $"+{i}"));
    }
}
#endif
```

---

## 📚 샘플

`Window > Package Manager > HB_Cheat > Samples > Cheat Examples > Import`로 가져옵니다. 클라 치트·서버 치트를 종류별로 모은 예제와, 가짜 네트워크에 연결한 전송 예제가 들어 있습니다. 가져온 위치는 `Assets/Samples/HB_Cheat/<버전>/Cheat Examples`입니다.

---

## 설명서

| 문서 | 내용 |
|---|---|
| [에디터 설정 · 생성](manual/editor.md) | Settings 탭 항목, 치트 파일 · asmdef 만들기, 레지스트리 생성, Cheats 탭 |
| [치트 정의 규칙](manual/defining-cheats.md) | `[Cheat]` · 인자 어트리뷰트 · Provider · 반환형 규칙 |
| [패널 · 서버 전송 · 코드 API](manual/runtime.md) | 패널 탭, 서버 전송 연결과 큐, `CheatSystem` API, InGameConsole 연동 |
| [릴리즈 빌드 제외](manual/release-build.md) | asmdef 제약, 빌드 가드, 릴리즈 판정, IDE 표시 |
| [자동화 (Unity CLI)](manual/automation.md) | `cheat_*` 명령, 응답 JSON · 오류 코드, 폴링, `cheat_play`, raw |

---

## 테스트

`Tests/Editor`에 EditMode 테스트(`HB.Cheat.Tests.Editor`)가 있습니다. 이 저장소에서는 바로 돌고, 패키지로 설치한 프로젝트에서는 `Packages/manifest.json`의 `"testables": ["com.hb.cheat"]`에 넣어야 보입니다. 테스트 어셈블리도 `HB_CHEAT`가 켜져 있어야 컴파일됩니다.

```bash
unity command run_tests --mode EditMode --filter HB.Cheat.Tests   # 에디터가 열려 있을 때
```

---

## ⚠️ 제한

- 자동 QA는 현재 에디터 Play Mode만 지원합니다. 기기는 브리지를 구현해야 합니다.
- Play Mode 중 재컴파일되면 CheatSystem과 등록된 치트 목록이 함께 풀립니다(자동화에는 `not_initialized`). `Initialize`를 다시 불러도 치트 목록은 비어 있으니 Play Mode를 다시 시작하세요.
- 치트를 지운 뒤 생성 레지스트리 때문에 컴파일 에러가 나면 `Delete Generated`를 누른 뒤 다시 생성하세요.
- 치트는 Assembly-CSharp나 `Assets` 아래 asmdef에만 둘 수 있습니다(레지스트리를 그 어셈블리에 생성해야 하므로).
- 프리셋(인자 조합 저장)은 아직 없습니다. 마지막으로 쓴 값, 즐겨찾기, 히스토리 재사용(`Edit`)은 지원합니다. 마지막으로 쓴 값과 히스토리는 메모리에만 있어 앱을 다시 켜거나 Play Mode를 다시 시작하면 사라집니다. 즐겨찾기는 `PlayerPrefs`에 남습니다.
- `[Flags]` enum은 정의된 단일 값 중 하나만 고를 수 있습니다.
