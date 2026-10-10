# HB_TestFrameworkHelper (`com.hb.testframeworkhelper`)

방치형 게임 및 유니티 프로젝트 전반에서 사용할 수 있는 확장형 테스트 도구 모음입니다.  
인스펙터 슬롯 누락 방지(`[NeedSlotNullCheck]`) 및 테스트용 더미 데이터 주입 규약(`IAutoTestMockable`)이 탑재되어 있습니다.

## 🚀 주요 기능
1. **단일 직관적 슬롯 어트리뷰트**: UI 코드의 필수 직렬화 필드에 `[NeedSlotNullCheck]`만 붙이면 검증 대상 등록.
2. **테스트 목 데이터 주입 규약 (`IAutoTestMockable`)**: UI 컴포넌트가 `IAutoTestMockable`을 구현하면 테스트 러너가 안전하게 더미 데이터를 자동 주입하고 클릭 테스트를 수행.
3. **에디터 전용 확장 메서드 (`TrySetupAutoTest`)**: `[Conditional("UNITY_EDITOR")]`가 적용되어 릴리즈 빌드 시 호출 코드가 0바이트로 자동 제거.
4. **원클릭 에디터 윈도우**: `Tools > HB > Test Framework Helper` 창에서 프로젝트 전체 프리팹 즉시 스캔 및 Ping 점프.
5. **Unity Test Framework (UTF) 연동**: `Window > General > Test Runner`에서 EditMode 테스트로 자동 검증.
6. **빌드 파이프라인 연동**: `HB Custom Build Manager` 빌드 실행 시 사전 검증 스텝(`IPreBuildStep`)으로 자동 등록되어 누락 시 경고/차단.
7. **Roslyn Analyzer 탑재**: `Plugins/HB_TestFrameworkHelper.dll`을 통해 IDE(Rider/VS)에서 누락 규칙 실시간 감지.

## 💻 사용 예시

```csharp
using UnityEngine;
using UnityEngine.UI;

public class MyPopupUI : MonoBehaviour, IAutoTestMockable
{
    // 인스펙터에서 실수로 연결하지 않으면 테스트 및 빌드 시 즉시 에러 감지!
    [NeedSlotNullCheck("닫기 버튼 필수 연결")]
    [SerializeField] private Button m_BtnClose;

    [NeedSlotNullCheck]
    [SerializeField] private Image m_FrameImage;

    // 💡 IAutoTestMockable 구현: 테스트 환경에서 쓸 안전한 더미 데이터 셋업
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void SetupForAutoTest()
    {
        // 테스트용 더미 데이터 주입
    }
}
```
