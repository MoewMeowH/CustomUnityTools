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
   - `[NeedSlotNullCheck(warnNullElements: false)]`: 배열 · List 슬롯의 빈 원소 경고를 끕니다 (기본 true). 단일 슬롯에는 영향이 없습니다.
   - 필드 전용 `PropertyAttribute`이며 런타임 어셈블리 `HB.TestFrameworkHelper.Runtime`(자동 참조)에 있어 플레이어 빌드 코드에서도 쓸 수 있습니다.
2. **Roslyn Analyzer & CodeFix (`HB_TestFrameworkHelper.dll`)**:
   - `Plugins/HB_TestFrameworkHelper.dll`에 `RoslynAnalyzer` 라벨이 붙어 있어 설치만 하면 IDE · 컴파일에 적용됩니다 (모든 플랫폼 비활성이라 빌드에는 들어가지 않음).
   - public이 아닌 필드(`private` · `protected` · `internal`)에 `[NeedSlotNullCheck]`를 선언하고 `[SerializeField]`를 누락한 경우 IDE에서 컴파일 경고를 표시하며, CodeFix 액션으로 `[SerializeField]`를 자동 추가합니다.
   - `static` · `readonly` · `const` · `[NonSerialized]` 필드처럼 Unity가 직렬화하지 않는 필드에 `[NeedSlotNullCheck]`를 선언하면 경고를 표시합니다 (`[SerializeField]`로도 해결되지 않음).
   - 필드 타입이 `UnityEngine.Object` 계열(또는 그 1차원 배열 · `List<T>`, 그런 제약이 있는 제네릭 `T`)이 아니면 컴파일 에러를 표시합니다. `HashSet` · `Dictionary` · 다차원 · 가변 배열은 엔진이 검사할 수 없어 에러입니다.
3. **프리팹 스캐너 엔진 (`SlotValidatorEngine`)**:
   - 지정 폴더에서 `AssetDatabase.FindAssets("t:Prefab")`로 찾은 프리팹을 하나씩 열어 비활성 자식까지 검사합니다.
   - 프리팹의 MonoBehaviour 직렬화 필드를 따라가며 검사합니다. 상속된 필드, 중첩 `[Serializable]` 클래스 · 구조체(배열 · List 원소 포함)도 내려갑니다.
   - 직렬화 필드는 public 또는 `[SerializeField]`인 인스턴스 필드입니다. `readonly` · `const` · `[NonSerialized]` · `[SerializeReference]` 필드는 건너뜁니다.
   - 중첩은 10단계까지 내려갑니다. `UnityEngine` · `UnityEditor` · `System` 네임스페이스 타입(`Vector3` 등)은 내려가지 않습니다.

   | 상황 | 종류 | 심각도 |
   |---|---|---|
   | 단일 슬롯이 비어 있음 | `NullSlot` | 에러 |
   | 배열 · List 슬롯의 원소가 비어 있음 (`warnNullElements: false`면 보고 안 함) | `NullElement` | 경고 |
   | 스크립트 연결이 끊어진 컴포넌트 (GameObject마다 1건, 개수는 설명에) | `MissingComponent` | 에러 |

   - 빈 배열 · List(원소 0개)는 문제로 보지 않습니다.
   - 삭제된 에셋을 가리키는 참조(Missing)도 `NullSlot`으로 보고합니다 (Missing을 따로 구분하지 않음).
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
   - **검사 폴더**(기본 `Assets`)를 입력하거나 **폴더 선택**으로 고릅니다 (`Assets` 안만). 프로젝트에 없는 폴더면 알림만 띄우고 검사하지 않습니다.
   - **전체 검사 실행** 후 에러 · 경고 개수와 항목마다 종류 · `컴포넌트 > 필드` · 프리팹 경로(GameObject 경로) · 설명을 보여줍니다. 경고 항목은 노란색입니다.
   - **에셋 열기 (Ping)**: 해당 프리팹 에셋을 Project 창에서 Ping하고 선택합니다.
   - 취소하면 취소 전까지 검사한 프리팹만 결과에 있다고 경고합니다.
5. **Unity Test Framework & 빌드 파이프라인 연동**:
   - `SlotValidationTests`를 통해 EditMode Test Runner 및 CI 환경에서 자동 검증을 수행합니다. 에러만 실패로 보고하고 경고는 로그로 남깁니다.
     - 테스트 `Validate_AllPrefabs_NoNullInspectorSlots_And_NoMissingScripts` 하나가 `Assets` 전체 프리팹을 검사합니다.
     - 경고는 `Debug.LogWarning`으로 남기고, 테스트 결과 메시지(실패 · 통과 모두)에도 목록을 적어 Test Runner 결과 화면에서 보입니다.
     - 테스트는 별도 어셈블리(`HB.TestFrameworkHelper.Tests.Editor`)에 있으며 `com.unity.test-framework`가 설치된 경우에만 컴파일됩니다 (`versionDefines` → `HB_TFH_HAS_TEST_FRAMEWORK`). Test Framework가 없는 프로젝트에서도 나머지 기능은 그대로 동작합니다.
   - `PreBuildStep_ValidateInspectorSlots`(`IPreBuildStep`)를 통해 플레이어 빌드 전 사전 검사를 수행하며, `LIVE` 빌드 시 에러가 발견되면 빌드를 중단합니다 (경고는 로그만). `com.hb.custombuild`가 설치된 경우에만 활성화됩니다.
     - HB_Build(`com.hb.custombuild`) 연동 스텝입니다. 별도 어셈블리 `HB.TestFrameworkHelper.BuildIntegration.Editor`에 있고, 패키지가 있을 때만 `HB_CUSTOM_BUILD_ENABLED`가 정의되어 컴파일됩니다.
     - `ExecutionOrder`는 1(가장 먼저)이고, `Assets` 전체 프리팹을 검사합니다. 문제마다 `[심각도][종류] 경로 > 컴포넌트.필드`를 빌드 경고 로그로 남깁니다.
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
