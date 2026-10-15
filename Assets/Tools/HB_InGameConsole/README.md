# HB_InGameConsole & HLog

Unity 6.3 이상(6 / 7)을 지원하는 고성능 인게임 콘솔 및 에디터 로깅 / 디버깅 툴킷입니다.

---

## 📦 UPM 패키지 설치 (Installation)

Unity Package Manager에서 아래 Git URL을 입력하여 설치합니다:
```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole
```

설치 즉시 프로젝트의 `Assets/Scripts/LogChannels.cs` 파일이 자동 생성(Auto-Bootstrap)되므로 추가 설정 없이 바로 `HLog`를 호출할 수 있습니다.

- **콘솔 설정 (InitMode, AutoCreateUI 등)**: UPM으로 설치된 패키지는 읽기 전용이라 패키지 안의 기본 설정은 수정할 수 없습니다. `Tools > HB > In-Game Console > Settings` 메뉴를 실행하면 기본값을 복사한 `Assets/Resources/HB_InGameConsoleUserSettings.asset`이 생성되며, 이 에셋이 있으면 패키지 기본 설정보다 우선 적용됩니다.
- **다른 스레드 로그**: Task, Job 등 메인 스레드가 아닌 곳에서 찍은 로그도 수집합니다. 메인 스레드에서 다음 프레임에 콘솔에 추가되므로 메인 스레드 로그와 순서가 약간 어긋날 수 있습니다.
- **Watch 해제**: `Watch(key, getter, this)`처럼 owner를 넘기면 owner가 파괴될 때 자동 해제되며, `Unwatch(key)`로 직접 해제할 수도 있습니다.
- **asmdef를 쓰는 프로젝트**: 기본 생성 위치(`Assets/Scripts`)는 Assembly-CSharp에 포함되어 asmdef 코드에서 `HLog`를 참조할 수 없습니다. LogChannel Generator 설정에서 출력 폴더를 해당 asmdef 폴더 안으로 지정하세요.

---

## 📌 주요 특징 (Key Features)

1. **클래스 기반 HLog 채널 아키텍처 (Namespace Conflict Free)**
   - `HLog`는 네임스페이스가 아닌 **전역 정적 partial 클래스(`public static partial class HLog`)**로 구현되어 있어, `using UnityEngine.UI;` 등 기존 엔진/프로젝트 네임스페이스와의 명칭 충돌이 전혀 발생하지 않습니다.
   - `HLog.UI.Log(...)`, `HLog.Common.Log(...)`, `HLog.System.Log(...)` 형태로 직관적이고 일관된 채널 접근을 제공합니다.

2. **커스텀 Conditional 심볼 지원 (`USE_HLOG`)**
   - 설정 파일(`LogChannelGeneratorSettings`)에서 컴파일 스트리핑에 사용될 **Conditional 심볼**을 자유롭게 지정할 수 있습니다 (기본값: `"USE_HLOG"`).
   - 모든 채널 로깅 메서드(`Log`, `Warning`, `LogWarning`)에 `[Conditional("USE_HLOG")]`가 적용되어, 릴리즈 빌드나 심볼 미정의 환경에서는 컴파일 타임에 IL 호출 자체가 제로 오버헤드로 스트리핑됩니다.

3. **에디터 & 인게임 실시간 LogEnable 제어**
   - **에디터 툴바**: `Log: ON/OFF` 런타임 토글 및 `USE_HLOG: ON/OFF` Scripting Define Symbol 원클릭 스위치 제공.
   - **인게임 콘솔 Settings 탭**: 콘솔 헤더의 `Settings` 탭을 통해 게임 플레이 도중에도 마스터 로그 On/Off 및 **채널별(Common, UI, Network 등) 개별 On/Off**를 손쉽게 조작할 수 있습니다.

4. **사전 채널 감지 & 가로 스크롤 필터 바 (Editor & In-Game)**
   - 로그를 한 번도 찍지 않아도 프로젝트에 정의된 모든 채널이 필터 탭에 즉시 표시됩니다.
   - 채널이 많아져 창 너비를 초과하는 경우 에디터 창과 인게임 콘솔 모두 **가로 스크롤(마우스 휠 및 터치 드래그)**을 지원합니다.
   - **다중 채널 구독**: 보고 싶은 여러 개의 채널을 동시에 체크/구독하여 복합 필터링할 수 있습니다.

5. **실시간 람다 Watch (Lambda Getter Monitoring)**
   - `HLog.Common.Watch("Player/ImageSlot", () => m_Image);` 형태로 **람다(게터)**를 등록할 수 있습니다.
   - 매 프레임 `Update()`에서 반복 호출하지 않고 `Start()`에서 1회만 등록해 두면, 인스펙터나 코드에서 값이 변경되는 즉시 시스템이 실시간으로 감지하여 업데이트합니다.

6. **조건부 에디터 Pause & 스냅샷 리포트 (Condition Triggers & Reports)**
   - **조건 트리거 (Condition Triggers)**: 특정 Watch 변수가 조건(예: `IsNotNull`, `== 0`, `> 100`)을 만족하면 에디터를 즉시 일시정지(`Debug.Break()`)시킵니다.
   - **스냅샷 리포트 (`ReportIf` 및 트리거 자동 덤프)**: 문제 발생 시 전체 Watch 변수 목록과 콜스택, 프레임 정보를 `Reports/` 폴더에 텍스트 파일로 자동 덤프합니다.

7. **Unity 6 / 7 수명 주기 관리 호환 (Lifecycle Safe)**
   - 채널 클래스에 `[NoAutoStaticsCleanup]`을 적용하여 도메인 리로드 시 인스턴스가 `null`로 초기화되는 문제를 원천 방지했습니다.
   - 에디터 모드(Edit Mode)와 플레이 모드(Play Mode) 양쪽 모두에서 안심하고 사용할 수 있습니다.

---

## 🚀 빠른 시작 (Quick Start)

### 1. 기본 로그 출력 (Basic Logging)
```csharp
// 1) 정적 채널 (코드 자동 생성)
HLog.Common.Log("[Player] 플레이어가 스폰되었습니다.");
HLog.Common.LogWarning("[Player] 주의: 체력이 20% 이하입니다.");
HLog.Common.LogError("[Network] 네트워크 연결 실패!");
HLog.UI.Log("[UI] 인벤토리 창 열림");

// 2) 동적 채널 (코드 생성 없이 즉시 사용)
HLog.Get("CustomChannel").Log("동적 채널 메시지");
HLog.Channel("Quest").LogWarning("퀘스트 진행 지연");
```

### 2. 실시간 변수 감시 (Watch)
```csharp
private void Start()
{
    // 람다 등록 (권장): 변수 값이 바뀔 때마다 실시간 자동 감지
    HLog.Common.Watch("Player/ImageSlot", () => m_Image);
    HLog.Common.Watch("Player/Health", () => player.CurrentHp);
}

private void Update()
{
    // 값 직접 전달 (매 프레임 갱신 시)
    HLog.Common.Watch("Player/Position", transform.position);
    HLog.Common.Watch("Player/MoveSpeed", moveSpeed);
}
```

### 3. 조건부 일시정지 및 리포트 덤프
```csharp
// 1) BreakIf: 특정 조건 시 에디터 즉시 Pause
HLog.Common.BreakIf(moveSpeed > 100f, $"비정상 이동 속도 감지: {moveSpeed}");

// 2) ReportIf: 조건 만족 시 현재 모든 Watch 변수를 텍스트 파일로 덤프 후 에디터 Pause
HLog.Common.ReportIf(currentHp <= 0 && isInvincible, "무적 상태 사망 버그 발생",
    ("PlayerState", currentState),
    ("LastAttacker", attackerName),
    ("FrameCount", Time.frameCount)
);
```

---

## 🖥️ 에디터 창 사용법 (Editor Window)

상단 메뉴: **Tools > InGameConsole > Console Window**

1. **상단 툴바**:
   - `Log: ON / OFF`: 에디터 및 런타임 마스터 로깅 On/Off 즉시 제어.
   - `USE_HLOG: ON / OFF`: Scripting Define Symbol 원클릭 추가/제거 스위치.
   - `Clear`: 로그 히스토리 비우기.
   - `Auto Scroll`: 맨 아래를 보고 있을 때 새 로그를 따라 내려갑니다. 위로 스크롤하면 따라가기를 멈추고, 다시 맨 아래로 내리면 이어서 따라갑니다. 끄면 새 로그가 와도 스크롤 위치가 바뀌지 않습니다.
   - `Pause on Error`: 에러 발생 시 자동 일시정지.
2. **Logs 탭**:
   - 상단 채널 바: 다중 채널 구독/해제 (`✓ Common`, `✓ UI` 등), 마우스 휠 가로 스크롤.
   - 우클릭 메뉴: `Only '[채널명]'` (단독 표시), `Subscribe All` (전체 구독), `Toggle Mute` (채널 음소거).
   - 로그 상세 창: 스택트레이스 라인 클릭 시 소스 코드로 즉시 점프.
3. **Watch 탭**:
   - **Watch Values**: 실시간 변수 트리, 값 변경 시각화, `null` 강조 표시, `+ Trigger` 버튼으로 즉시 감시 조건 등록.
   - **Condition Triggers**: 변수 조건식(`==`, `!=`, `>`, `<`, `IsNull`, `IsNotNull`), `Pause`(자동 일시정지), `Report`(스냅샷 덤프), `Once`(단일 발동) 설정.
   - 플레이 모드 진입 시 `Once` 발동 상태가 자동 초기화되어 매번 안정적으로 테스트 가능.

---

## 📱 인게임 콘솔 UI (In-Game Console UI)

런타임 환경에서 우상단 `Logs` 플로팅 버튼을 터치/클릭하여 열 수 있습니다.

- **`Logs` 탭**: 게임 런타임 로그 목록, 로그 레벨 필터(All/Info/Warn/Error), 검색창, 가로 스크롤 채널 필터 바.
- **`Watch` 탭**: 게임 내 실시간 등록된 Watch 변수 목록 및 실시간 값 모니터링.
- **`Settings` 탭 (분리된 전용 설정 뷰)**:
  - **Master Log Enable**: 런타임 전체 로깅 On/Off 토글 버튼.
  - **Show FPS Widget**: 좌상단 FPS 및 프레임타임 그래프 위젯 표시 On/Off.
  - **Auto Scroll**: 맨 아래를 보고 있을 때 새 로그를 따라 내려갑니다. 위로 스크롤하면 멈추고, 다시 맨 아래로 내리거나 `▼ Bottom`을 누르면 이어서 따라갑니다. 기본값은 `InGameConsoleSettings`의 `Auto Scroll`, 코드에서는 `InGameConsole.Instance.AutoScroll`로 바꿀 수 있습니다.
  - **Channel Log Enable (Per Channel)**: 프로젝트의 모든 채널(`Common`, `UI`, `System` 등)을 칩(Chip) 버튼으로 나열하여 **각 채널별로 로그 On/Off**를 독립 제어.
  - **Clear All Log History**: 런타임 로그 히스토리 전체 비우기.

---

## 🛠️ 채널 생성기 (Channel Generator)

상단 메뉴: **Tools > InGameConsole > Log Channel Generator**

1. **설정 관리**:
   - **Conditional 심볼**: 메서드 스트리핑에 사용할 디파인 심볼 설정 (기본값: `USE_HLOG`).
   - **생성 경로 & 파일 이름**: 기본 `Assets/Scripts/LogChannels.cs`.
   - **설정 저장 시 자동 코드 갱신**: 체크 시 채널 추가/수정 즉시 C# 코드 자동 갱신.
2. **채널 추가**:
   - 원하는 채널 이름(예: `Battle`, `Inventory`, `Network`)을 입력하고 **채널 추가 (+)** 클릭.
   - 갱신 후 즉시 `HLog.Battle.Log(...)` 형태로 사용 가능합니다.
