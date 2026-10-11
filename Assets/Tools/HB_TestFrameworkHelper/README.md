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
2. **Roslyn Analyzer & CodeFix (`HB_TestFrameworkHelper.dll`)**:
   - `private` 필드에 `[NeedSlotNullCheck]`를 선언하고 `[SerializeField]`를 누락한 경우 IDE에서 컴파일 경고를 표시하며, CodeFix 액션으로 `[SerializeField]`를 자동 추가합니다.
3. **프리팹 스캐너 엔진 (`SlotValidatorEngine`)**:
   - `TypeCache.GetFieldsWithAttribute<NeedSlotNullCheckAttribute>()`를 사용해 대상 필드를 수집하고, 프리팹 내 미할당 슬롯(`NullSlot`)과 끊어진 컴포넌트(`MissingComponent`)를 검사합니다.
4. **에디터 검증 윈도우 (`Tools > HB > Test Framework Helper`)**:
   - 지정 폴더 내 전체 프리팹을 검사하고 누락된 오브젝트 위치를 에디터에서 바로 Ping하여 확인할 수 있습니다.
5. **Unity Test Framework & 빌드 파이프라인 연동**:
   - `SlotValidationTests`를 통해 EditMode Test Runner 및 CI 환경에서 자동 검증을 수행합니다.
   - `PreBuildStep_ValidateInspectorSlots`(`IPreBuildStep`)를 통해 플레이어 빌드 전 사전 검사를 수행하며, `LIVE` 빌드 시 누락이 발견되면 빌드를 중단합니다.

## 사용 예시

```csharp
using UnityEngine;
using UnityEngine.UI;

public class MyPopupUI : MonoBehaviour
{
    [NeedSlotNullCheck("닫기 버튼 필수 연결")]
    [SerializeField] private Button m_BtnClose;

    [NeedSlotNullCheck]
    [SerializeField] private Image m_FrameImage;
}
```
