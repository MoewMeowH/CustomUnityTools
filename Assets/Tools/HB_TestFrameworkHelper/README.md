# HB_TestFrameworkHelper

유니티 프로젝트의 프리팹 인스펙터 슬롯 누락(`[NeedSlotNullCheck]`) 및 Missing Script를 코딩 타임, 테스트 타임, 빌드 타임에 검증하는 에디터 도구입니다.

---

## 📦 UPM 패키지 설치 (Installation)

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_TestFrameworkHelper
```

---

## 주요 구성 및 기능
1. **`[NeedSlotNullCheck]` 어트리뷰트**: 필수 연결이 필요한 직렬화 필드에 선언하여 검증 대상으로 등록합니다.
   - 네임스페이스는 `HB.TestFrameworkHelper`입니다. 사용하는 스크립트에 `using HB.TestFrameworkHelper;`를 추가하세요 (CodeFix는 using을 자동으로 넣지 않습니다).
   - `[NeedSlotNullCheck("설명")]`: 리포트에 설명을 붙입니다.
   - `[NeedSlotNullCheck(warnNullElements: false)]`: 배열 · List 슬롯의 빈 원소 경고를 끕니다 (기본 true). 단일 슬롯과 Missing 원소(삭제된 에셋 참조, 항상 에러)에는 영향이 없습니다.
   - 필드 전용 `PropertyAttribute`이며 런타임 어셈블리 `HB.TestFrameworkHelper.Runtime`(자동 참조)에 있어 플레이어 빌드 코드에서도 쓸 수 있습니다.
2. **Roslyn Analyzer & CodeFix (`HB_TestFrameworkHelper.dll`)**:
   - `Plugins/HB_TestFrameworkHelper.dll`에 `RoslynAnalyzer` 라벨이 붙어 있어 설치만 하면 IDE · 컴파일에 적용됩니다 (모든 플랫폼 비활성이라 빌드에는 들어가지 않음).
   - public이 아닌 필드(`private` · `protected` · `internal`)에 `[NeedSlotNullCheck]`를 선언하고 `[SerializeField]`를 누락한 경우 IDE에서 컴파일 경고를 표시하며, CodeFix 액션으로 `[SerializeField]`를 자동 추가합니다.
   - `static` · `readonly` · `const` · `[NonSerialized]` 필드처럼 Unity가 직렬화하지 않는 필드에 `[NeedSlotNullCheck]`를 선언하면 경고를 표시합니다 (`[SerializeField]`로도 해결되지 않음).
   - 필드 타입이 `UnityEngine.Object` 계열(또는 그 1차원 배열 · `List<T>`, 그런 제약이 있는 제네릭 `T`)이 아니면 컴파일 에러를 표시합니다. `HashSet` · `Dictionary` · 다차원 · 가변 배열은 엔진이 검사할 수 없어 에러입니다.

     | 진단 ID | 심각도 | 내용 |
     |---|---|---|
     | `HB_NeedSlotNullCheckSerializeField` | 경고 | public이 아닌 필드에 `[SerializeField]` 누락 (CodeFix: `[SerializeField]` 추가) |
     | `HB_NeedSlotNullCheckNotSerializable` | 경고 | `static` · `readonly` · `const` · `[NonSerialized]` 필드 |
     | `HB_NeedSlotNullCheckInvalidType` | 에러 | 슬롯이 될 수 없는 타입 |

     `.editorconfig`에서 `dotnet_diagnostic.<진단 ID>.severity`로 심각도를 바꾸거나 끌 수 있습니다.
3. **프리팹 스캐너 엔진 (`SlotValidatorEngine`)**:
   - 지정 폴더에서 `AssetDatabase.FindAssets("t:Prefab")`로 찾은 프리팹을 하나씩 열어 비활성 자식까지 검사합니다. 프리팹 100개마다 쓰지 않는 에셋을 내려 큰 프로젝트에서도 메모리가 계속 늘지 않게 합니다 (결과가 가리키는 프리팹은 남음). 저장하지 않은 수정 중 에셋(dirty)은 내려가지 않지만, 어디서도 참조하지 않는 임시 객체(`HideFlags` 없이 만든 `ScriptableObject` · `Material` 등)는 정리됩니다.
   - 프리팹의 MonoBehaviour 직렬화 필드를 따라가며 검사합니다. 상속된 필드, 중첩 `[Serializable]` 클래스 · 구조체(배열 · List 원소 포함)도 내려갑니다.
   - 직렬화 필드는 public 또는 `[SerializeField]`인 인스턴스 필드입니다. `readonly` · `const` · `[NonSerialized]` · `[SerializeReference]` 필드는 건너뜁니다.
   - 중첩은 10단계까지 내려갑니다. `UnityEngine` · `UnityEditor` · `System` 네임스페이스 타입(`Vector3` 등)은 내려가지 않습니다.

   | 상황 | 종류 | 심각도 |
   |---|---|---|
   | 단일 슬롯이 비어 있음 | `NullSlot` | 에러 |
   | 슬롯이 삭제된 에셋을 가리킴 (Missing) — 단일 슬롯 · 배열 · List 원소 모두 | `MissingReference` | 에러 |
   | 배열 · List 슬롯의 원소가 비어 있음 (`warnNullElements: false`면 보고 안 함) | `NullElement` | 경고 |
   | 스크립트 연결이 끊어진 컴포넌트 (GameObject마다 1건, 개수는 설명에) | `MissingComponent` | 에러 |

   - 빈 배열 · List(원소 0개)는 문제로 보지 않습니다.
   - 빈 칸과 Missing은 직렬화된 instanceID로 구분합니다 (빈 칸은 0).
   - 중첩 프리팹 · 프리팹 변형이 원본에서 덮어쓰지 않고 물려받은 값은, 원본 프리팹이 이번 검사 대상이면 원본에서만 보고합니다 (같은 문제를 여러 번 세지 않음). 덮어쓴 값, 이 프리팹에서 늘린 배열 · List 원소, 원본이 검사 대상 밖(다른 폴더 · 패키지)인 값은 그 프리팹에서 보고합니다. 원본 값이 있는데 이 프리팹에서만 비거나 Missing이 된 값도 그 프리팹에서 보고합니다. Missing 스크립트도 원본에 있던 개수를 빼고 늘어난 것만 보고합니다.
     - 한계: Missing 스크립트는 개수로만 비교하므로, 원본의 Missing 컴포넌트를 지우고(제거 override) 다른 Missing 컴포넌트를 더하면 상쇄되어 보고되지 않습니다.
     - 원본을 일부러 비워 두고 쓰는 곳마다 채우는 패턴에서는 원본만 에러로 보이고, 채우지 않은 중첩 · 변형 위치는 따로 보고되지 않습니다 (원본을 고치거나 원본 폴더를 빼고 검사하면 보입니다).
     - 원본 프리팹을 불러오지 못하면(깨진 프리팹) 그 원본에서 물려받은 문제도 보고되지 않습니다.
   - 직렬화되지 않는 필드는 검사하지 않습니다 (분석기가 경고).
   - 스크립트 API (`HB.TestFrameworkHelper.Editor`):

     | 멤버 | 설명 |
     |---|---|
     | `SlotValidatorEngine.ScanPrefabs(string[] searchFolders = null)` | 폴더 안 프리팹을 검사해 `List<SlotValidationIssue>` 반환. null · 빈 배열이면 `Assets` |
     | `SlotValidatorEngine.ScanPrefabs(string[] searchFolders, bool showProgress, out bool canceled)` | 취소 가능한 진행 표시를 띄움. 취소하면 그때까지의 결과 반환 |
     | `SlotValidationIssue` | `AssetPath` · `PrefabAsset` · `GameObjectPath` · `ComponentName` · `FieldName` · `Description` · `IssueType` · `Severity` |
     | `EValidationIssueType` / `EValidationSeverity` | 위 표의 종류 / `Error` · `Warning` |
4. **에디터 검증 윈도우 (`Tools > HB > Test Framework Helper`)**:
   - 지정 폴더 내 전체 프리팹을 검사하고 문제가 있는 프리팹을 에디터에서 바로 Ping하여 확인할 수 있습니다. 검사 중 진행 표시에서 취소할 수 있습니다.
   - **검사 폴더**(기본 `Assets`)를 입력하거나 **폴더 선택**으로 고릅니다 (`Assets` 안만, 경로 대소문자는 구분하지 않음). 프로젝트에 없는 폴더면 알림만 띄우고 검사하지 않습니다.
   - **전체 검사 실행** 후 에러 · 경고 개수와 항목마다 종류 · `컴포넌트 > 필드` · 프리팹 경로(GameObject 경로) · 설명을 보여줍니다. 경고 항목은 노란색입니다.
   - **에셋 열기 (Ping)**: 해당 프리팹 에셋을 Project 창에서 Ping하고 선택합니다.
   - 취소하면 취소 전까지 검사한 프리팹만 결과에 있다고 경고합니다. 원본을 검사하기 전에 취소했으면 중첩 · 변형에서 물려받은 문제도 빠질 수 있습니다.
5. **Unity Test Framework & 빌드 파이프라인 연동**:
   - `SlotValidationTests`를 통해 EditMode Test Runner 및 CI 환경에서 자동 검증을 수행합니다. 에러만 실패로 보고하고 경고는 로그로 남깁니다.
     - 테스트 `Validate_AllPrefabs_NoNullInspectorSlots_And_NoMissingScripts` 하나가 `Assets` 전체 프리팹을 검사합니다.
     - 경고는 `Debug.LogWarning`으로 남기고, 테스트 결과 메시지(실패 · 통과 모두)에도 목록을 적어 Test Runner 결과 화면에서 보입니다. 항목은 `[심각도][종류] 에셋 > GameObject 경로 > 컴포넌트.필드 (설명)` 형식입니다.
     - 테스트는 별도 어셈블리(`HB.TestFrameworkHelper.Tests.Editor`)에 있으며 `com.unity.test-framework`가 설치된 경우에만 컴파일됩니다 (`versionDefines` → `HB_TFH_HAS_TEST_FRAMEWORK`). Test Framework가 없는 프로젝트에서도 나머지 기능은 그대로 동작합니다.
   - `PreBuildStep_ValidateInspectorSlots`(`IPreBuildStep`)를 통해 플레이어 빌드 전 사전 검사를 수행하며, `LIVE` 빌드 시 에러가 발견되면 빌드를 중단합니다 (경고는 로그만). `com.hb.custombuild`가 설치된 경우에만 활성화됩니다.
     - HB_Build(`com.hb.custombuild`) 연동 스텝입니다. 별도 어셈블리 `HB.TestFrameworkHelper.BuildIntegration.Editor`에 있고, 패키지가 있을 때만 `HB_CUSTOM_BUILD_ENABLED`가 정의되어 컴파일됩니다.
     - `ExecutionOrder`는 1(가장 먼저)이고, `Assets` 전체 프리팹을 검사합니다. 첫 줄에 `[Inspector Validation] 필수 슬롯/스크립트 문제 N개 (에러 E · 경고 W)`, 이어서 문제마다 `[심각도][종류] 에셋 > GameObject 경로 > 컴포넌트.필드 (설명)`를 빌드 경고 로그로 남깁니다.
     - 에러가 있으면 첫 줄을 HB_Build 경고(`context.AddWarning` → `BuildOutput/HB_BUILD_WARNINGS.txt`, Jenkins UNSTABLE)로도 남깁니다. 경고(빈 원소)만 있으면 로그만 남깁니다.
     - `StepId`는 `HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots`로 고정되어 있어, 빌드 프로필의 비활성 스텝 목록에 넣어 끌 수 있고 클래스 이름이 바뀌어도 설정이 유지됩니다.

## 사용 예시

```csharp
using System.Collections.Generic;
using HB.TestFrameworkHelper;
using UnityEngine;
using UnityEngine.UI;

public class MyPopupUI : MonoBehaviour
{
    [NeedSlotNullCheck("닫기 버튼 필수 연결")]
    [SerializeField] private Button m_BtnClose;

    [NeedSlotNullCheck]
    [SerializeField] private Image m_FrameImage;

    [NeedSlotNullCheck] // 빈 원소는 경고
    [SerializeField] private Image[] m_StarIcons;

    [NeedSlotNullCheck(warnNullElements: false)] // 빈 칸이 있어도 되는 목록
    [SerializeField] private List<Transform> m_OptionalSockets;
}
```

---

## 테스트 작성 도우미 (Testing)

테스트에서 반복되는 준비 · 정리 코드를 줄이는 유틸입니다. `com.unity.test-framework`가 있고 테스트가 컴파일될 때(`UNITY_INCLUDE_TESTS`)만 컴파일되며, 일반 플레이어 빌드에는 들어가지 않습니다 (테스트용 플레이어 빌드에는 들어감).

### 참조 설정

자동 참조가 아니므로 쓰는 쪽 테스트 asmdef의 `references`에 직접 넣습니다.

| 어셈블리 | 내용 | 쓰는 곳 |
|---|---|---|
| `HB.TestFrameworkHelper.Testing` | `TestScope` · `HBTestFixture` · `HBAssert` · `HBWait` | EditMode · PlayMode 테스트 |
| `HB.TestFrameworkHelper.Testing.Editor` | `TempDirectory` · `TempAssetFolder` · `TempScene` | EditMode(에디터 전용) 테스트 |

EditMode 테스트 asmdef (에디터 전용):

```json
"references": [
    "HB.TestFrameworkHelper.Testing",
    "HB.TestFrameworkHelper.Testing.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
],
"includePlatforms": [ "Editor" ],
"overrideReferences": true,
"precompiledReferences": [ "nunit.framework.dll" ],
"defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
```

PlayMode 테스트 asmdef (테스트용 플레이어에서도 돌므로 에디터 어셈블리를 넣지 않음):

```json
"references": [
    "HB.TestFrameworkHelper.Testing",
    "UnityEngine.TestRunner"
],
"includePlatforms": [],
"overrideReferences": true,
"precompiledReferences": [ "nunit.framework.dll" ],
"defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
```

> **`testables` 필수:** 이 유틸 어셈블리는 Unity가 테스트 어셈블리로 다루므로, git URL 등으로 설치한 프로젝트는 `Packages/manifest.json`에 `"testables": ["com.hb.testframeworkhelper"]`를 넣어야 컴파일됩니다 (없으면 쓰는 쪽 테스트가 `HB` 네임스페이스를 찾지 못함). 이렇게 해도 이 패키지 자체 테스트(`Testing/Tests`)는 보이지 않습니다 — 그 테스트는 Scripting Define Symbol `HB_TFH_SELF_TESTS`가 있을 때만 컴파일됩니다 (패키지를 고치는 사람용). 기존 슬롯 검사 테스트(`SlotValidationTests`)는 `testables`만으로 보입니다.

### 사용 예시

```csharp
using HB.TestFrameworkHelper;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEngine;

public class MyPopupTests : HBTestFixture   // 테스트마다 Scope를 만들고 끝나면 정리
{
    [Test]
    public void Close_HidesPopup()
    {
        GameObject go = Scope.NewGameObject("Popup", typeof(MyPopupUI));
        var config = Scope.NewInstance<MyPopupConfig>();
        Scope.Preserve(() => GameSettings.Speed, v => GameSettings.Speed = v);
        GameSettings.Speed = 2f;

        TempAssetFolder folder = Scope.NewTempAssetFolder();   // Assets/__HBTestTemp/<guid>
        // ... 테스트 ...
    }   // 끝나면 등록 역순으로 정리: 폴더 → Speed 복원 → config → go
}
```

다른 베이스를 이미 상속했다면 `TestScope`를 직접 씁니다.

```csharp
private TestScope m_Scope;
[SetUp] public void SetUp() => m_Scope = new TestScope();
[TearDown] public void TearDown() => m_Scope.Dispose();
```

### API

| 멤버 | 설명 |
|---|---|
| `TestScope.NewGameObject(name, params Type[])` · `NewInstance<T>()` | 만들고 소유. 끝나면 지움 |
| `TestScope.Track(obj)` | 넘긴 객체만 소유 (Component면 그 Component만). 에셋(디스크에 저장된 객체)은 에디터에서 거절, Transform도 거절 (GameObject를 Track) |
| `TestScope.Preserve(getter, setter)` | 지금 값을 기억했다가 되돌림 — **얕은 복원**: 같은 객체의 내부 변경은 되돌리지 않음 |
| `TestScope.Defer(action)` · `Own(disposable)` | 임의 정리 동작 · `IDisposable` 등록 |
| `HBTestFixture` | `Scope` 자동 생성 · 정리. 파생 클래스의 `[UnitySetUp]` · `[SetUp]` · `[TearDown]` · `[UnityTearDown]` 어디서든 Scope를 쓸 수 있음. PlayMode는 정리 뒤 한 프레임 기다려 Destroy가 끝난 다음 테스트로 넘어감 |
| `HBAssert.NoGCAlloc(action, warmup = 1)` | GC 할당이 없는지. warmup회 먼저 부른 뒤 1회 측정 (총 warmup + 1회 호출) |
| `HBAssert.AnyContains` · `NoneContains(values, text)` | 문자열 목록 검사. 입력은 한 번만 열거, 실패하면 전체 목록 출력 |
| `HBWait.Until(predicate, timeoutSec, what)` · `Frames(n)` | `[UnityTest]`에서 `yield return`. 시간은 실제 시간(timeScale 무관), 넘으면 실패 |
| `scope.NewTempDirectory()` · `TempDirectory` | OS 임시 폴더. 읽기 전용 파일 · 폴더도 지움, 링크는 따라가지 않음 |
| `scope.NewTempAssetFolder()` · `TempAssetFolder` | `Assets/__HBTestTemp/<guid>`. 마지막이 지워지면 빈 루트도 지움 |
| `scope.NewTempScene(setup)` · `TempScene` | 새 씬을 열고 끝나면 원래 구성으로 (EditMode 전용) |

### 정리 규칙 · 제한

* **정리 순서:** 모든 항목을 한 스택에 넣고 등록 역순으로 정리합니다. 하나가 실패해도 나머지를 끝까지 정리한 뒤 예외를 던집니다. `Dispose`를 두 번 불러도 안전하고, 그 뒤 등록하면 `ObjectDisposedException`.
* **PlayMode:** `Destroy`는 프레임 끝에 실행됩니다. `TestScope.Dispose()` 직후 같은 프레임에는 객체가 아직 있습니다 (`HBTestFixture`는 한 프레임 기다림). 파괴 전에 비활성화하지 않습니다.
* **`using` 블록:** 본문에서 예외가 나고 정리에서도 예외가 나면 C# 규칙대로 정리 예외가 본문 예외를 가립니다.
* **에셋이 된 객체:** `NewInstance`로 만든 객체를 `AssetDatabase.CreateAsset` 등으로 에셋으로 만들면 Dispose에서 지우지 않습니다 (에셋은 `TempAssetFolder` 같은 파일 쪽이 정리). 다른 Component가 `[RequireComponent]`로 의존하는 Component를 Track하면 Unity가 지우지 않고 에러 로그를 남기므로 GameObject를 Track하세요.
* **`TempAssetFolder`:** 테스트가 중단돼 남은 폴더는 다음에 처음 만들 때 지웁니다 — 이 패키지 표식 파일(`.hb_tfh_owner`)이 있는 폴더만 (지운 경로는 경고 로그). 폴더 안에 링크(심볼릭 링크 · 정션)가 있으면 따라 들어가지 않도록 지우지 않습니다 (남은 폴더는 경고, 사용 중 폴더의 Dispose는 예외). 도메인 리로드(스크립트 · asmdef 생성 등)를 넘는 사용은 지원하지 않습니다. `Assets/__HBTestTemp/`를 프로젝트 `.gitignore`에 넣어 두는 것을 권장합니다.
* **`TempDirectory`:** OS 임시 폴더는 다른 사용자와 공유될 수 있습니다(Linux `/tmp` 등) — 비밀 값이 든 파일은 넣지 마세요. 하드 링크로 넣은 읽기 전용 파일은 지울 때 푼 읽기 전용이 원본에도 남습니다.
* **`TempScene`:** 저장하지 않은 변경이 있는 씬이나 Prefab Mode 프리팹이 열려 있으면 아무것도 바꾸지 않고 테스트를 Inconclusive로 끝냅니다 (변경을 저장하지도 버리지도 않음). 저장된 적 없는 씬은 되돌릴 수 없어, 저장 씬만 되돌리고 저장 씬이 없었으면 새 빈 씬을 엽니다. 테스트가 임시 씬에 한 변경은 버립니다.
* **`NoGCAlloc`:** Unity Test Framework의 `AllocatingGCMemory`(메인 스레드 · 프로파일러 기록 기반)를 씁니다. 에디터에서는 에디터 기준 값입니다.
* 메인 스레드 전용입니다. 같은 프로젝트를 여러 에디터 프로세스가 동시에 쓰는 경우는 다루지 않습니다 (Unity가 프로젝트를 잠금).
