# Custom Unity Tools - HB In-Game Console & HLog

Unity 6 / 7 및 최신 런타임을 지원하는 고성능 인게임 콘솔 및 에디터 로깅 / 디버깅 툴킷입니다.

깃허브 저장소 패키지 설치:
```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole
```

---

## 📌 주요 특징 (Key Features)

1. **클래스 기반 HLog 채널 아키텍처 (Namespace Conflict Free)**
   - `HLog`는 네임스페이스가 아닌 **전역 정적 클래스(`public static partial class HLog`)**로 구현되어 있어, `using UnityEngine.UI;` 등 기존 엔진/프로젝트 네임스페이스와의 명칭 충돌이 전혀 발생하지 않습니다.
   - `HLog.UI.Log(...)`, `HLog.Common.Log(...)`, `HLog.Core.Log(...)` 형태로 직관적이고 일관된 채널 접근을 제공합니다.

2. **UPM 제로 설정 자동 부트스트랩 (Zero-Config UPM Auto-Bootstrap)**
   - UPM(Git URL 또는 패키지 매니저)으로 패키지를 설치하자마자, 에디터가 사용자 프로젝트 영역(`Assets/Scripts/LogChannels.cs`)에 기본 `HLog` 클래스 파일을 **자동으로 1회 생성**합니다.
   - 별도로 생성기 메뉴를 열거나 버튼을 누르지 않아도 설치 즉시 `HLog.Common.Log(...)`가 오류 없이 작동합니다.
   - 설정 파일 변경 시에도 백그라운드에서 C# 코드가 자동으로 실시간 갱신됩니다.

3. **정적 채널 & 동적 채널 하이브리드 지원 (Static & Dynamic Channels)**
   - **정적 채널 (인텔리센스/자동완성)**: `HLog.UI.Log(...)`, `HLog.Battle.Log(...)`
   - **동적 채널 (코드 생성 불필요)**: `HLog.Get("Inventory").Log(...)`, `HLog.Channel("Quest").Log(...)`
   - 임시 디버깅이나 빠른 프로토타이핑 시 코드 생성 없이도 즉시 임의의 채널 로깅이 가능합니다.

4. **에디터 콘솔 윈도우 (Editor Console Window)**
   - **다중 채널 구독 (Multi-Channel Subscription)**: 단일 선택 방식이 아닌, 보고 싶은 여러 개의 채널을 동시에 체크/구독하여 복합 필터링할 수 있습니다.
   - **우클릭 컨텍스트 메뉴**: 특정 채널만 보기(`Only`), 전체 구독(`Subscribe All`), 채널 음소거(`Mute`) 지원.
   - **스택 트레이스 점프**: 로그 클릭 시 소스 코드의 호출 라인으로 즉시 이동.

5. **실시간 Watch 탭 (Runtime Variable Monitoring)**
   - `HLog.[Channel].Watch("Category/Name", value)`로 런타임 변수 실시간 모니터링.
   - 계층형 슬래시(`/`) 카테고리 트리 지원.
   - 값 변경 감지 시 시각적 하이라이트 및 `null` 레퍼런스 안전 표시.

6. **스냅샷 리포트 & 브레이크포인트 (Report & BreakIf)**
   - `HLog.[Channel].BreakIf(condition, message)`: 이상 감지 시 즉시 에디터 일시정지(`Debug.Break()`).
   - `HLog.[Channel].ReportIf(condition, title, params)`: 조건 충족 시 현재 등록된 모든 Watch 변수 및 커스텀 데이터를 텍스트 파일(`Reports/HLog_Report_*.txt`)로 덤프 후 에디터 자동 일시정지.

7. **인게임 오버레이 UI (UI Toolkit 기반)**
   - 모바일/PC 런타임 빌드에서 터치/클릭으로 열 수 있는 오버레이 콘솔.
   - FPS & 성능 통계 그래프, 실시간 태그 필터, 검색창 제공.

8. **Unity 최신 수명 주기 관리 호환 (Unity 6 / 7)**
   - `[AutoStaticsCleanup]` / `[NoAutoStaticsCleanup]` 및 전처리기 분기를 적용하여 최신 Unity 버전의 정적 수명 주기 관리 분석기 경고(`UAL0013`)를 완벽히 해결.
   - Release 빌드 시 `[Conditional("UNITY_EDITOR")]` 및 `[Conditional("ENABLE_LOG")]`을 통해 오버헤드 0 및 GC 제로 지원.

---

## 🚀 빠른 시작 (Quick Start)

### 1. 기본 로그 출력 (Basic Logging)
```csharp
// 1) 정적 채널 (코드 자동 생성)
HLog.Common.Log("플레이어가 스폰되었습니다.");
HLog.Common.LogWarning("주의: 체력이 20% 이하입니다.");
HLog.Common.LogError("네트워크 연결 실패!");
HLog.UI.Log("UI 인벤토리 창 열림");

// 2) 동적 채널 (코드 생성 없이 즉시 사용)
HLog.Get("CustomChannel").Log("동적 채널 메시지");
HLog.Channel("Network").LogWarning("패킷 지연 발생");
```

### 2. 실시간 변수 감시 (Watch)
```csharp
void Update()
{
    // 계층형 경로("그룹/변수명")로 등록
    HLog.Common.Watch("Player/Position", transform.position);
    HLog.Common.Watch("Player/MoveSpeed", moveSpeed);
    HLog.Common.Watch("Player/TargetRef", targetTransform); // null 체크 안전 지원
}
```

### 3. 조건부 에디터 일시정지 (BreakIf)
```csharp
// 이동 속도가 비정상적으로 치솟을 때 에디터 즉시 Pause
HLog.Common.BreakIf(moveSpeed > 100f, $"비정상 이동 속도 감지: {moveSpeed}");
```

### 4. 스냅샷 리포트 자동 생성 (ReportIf)
```csharp
// 조건 만족 시 현재 모든 Watch 변수 + 추가 인자를 텍스트 파일로 저장하고 에디터 Pause
HLog.Common.ReportIf(currentHp <= 0 && isInvincible, "무적 상태 사망 버그 발생",
    ("PlayerState", currentState),
    ("LastAttacker", attackerName),
    ("FrameCount", Time.frameCount)
);
```
- 리포트 출력 경로: `[프로젝트루트]/Reports/HLog_Report_YYYYMMDD_HHMMSS.txt`

---

## 🛠️ 채널 생성 및 관리 (Channel Generator)

새로운 정적 로그 채널을 추가하거나 관리하려면:

1. Unity 상단 메뉴에서 **Tools > InGameConsole > Log Channel Generator** 클릭.
2. 원하는 채널 이름(예: `Battle`, `Inventory`, `Network`)을 입력하고 **"채널 추가 (+)"** 클릭.
3. **"저장 시 자동 코드 갱신"**이 기본 활성화되어 있어 채널 추가 즉시 `LogChannels.cs`가 자동 갱신됩니다.
4. UPM 패키지 환경에서도 프로젝트 영역(`Assets/Scripts/LogChannels.cs`)에 정상 생성되므로 별도 설정이 필요 없습니다.

```csharp
// 생성 후 즉시 사용 가능:
HLog.Battle.Log("공격 성공!");
HLog.Inventory.Log("아이템 획득: 포션");
```

---

## 🖥️ 에디터 콘솔 윈도우 사용법 (Editor Window)

Unity 상단 메뉴에서 **Tools > InGameConsole > Console Window**를 열어 사용할 수 있습니다.

### 다중 채널 구독 모드
- **[All] 버튼**: 클릭 시 모든 채널의 로그를 확인합니다.
- **개별 채널 버튼**: 좌클릭하여 원하는 채널을 다중 선택(`✓ UI`, `✓ Common` 등)할 수 있습니다.
- **우클릭 메뉴 (Context Menu)**:
  - `Only '[채널명]'`: 해당 채널만 단독으로 표시합니다.
  - `Subscribe All`: 모든 채널을 일괄 구독합니다.
  - `Toggle Mute '[채널명]'`: 특정 채널의 로그를 완전히 차단/음소거합니다.

### 탭 구성
- **Logs 탭**: 수집된 로그 목록, 로그 레벨 필터(Info, Warning, Error), 다중 채널 필터, 텍스트/정규식 검색.
- **Watch 탭**: 실시간 런타임 변수 트리, 값 변경 시각화, Null 상태 모니터링, 값 복사 기능.

---

## 📁 프로젝트 구조 (File Structure)

```
Assets/
├── Tools/
│   └── HB_InGameConsole/
│       ├── Scripts/
│       │   ├── HLogCore.cs             # 전역 코어 로깅/Watch/Report 엔진 & 동적 채널 캐시
│       │   ├── LogChannel.cs           # 제네릭 채널 기본 클래스
│       │   ├── InGameConsole.cs        # 런타임 콘솔 인스턴스
│       │   └── UI/
│       │       └── HB_InGameConsoleUI.cs # UI Toolkit 런타임 오버레이
│       ├── Editor/
│       │   ├── HLogConsoleWindow.cs         # 에디터 콘솔 & Watch & Report 윈도우 (다중 채널 구독)
│       │   ├── LogChannelGeneratorWindow.cs # 채널 자동 생성기 & UPM 부트스트랩
│       │   ├── LogChannelGeneratorSettings.cs # 채널 설정 ScriptableObject
│       │   └── LogChannelSettings.cs        
│       └── HB.InGameConsole.asmdef      # 툴킷 어셈블리 정의
└── Scripts/
    ├── LogChannels.cs                  # 자동 생성된 HLog 정적 클래스 및 채널 정의
    └── NewMonoBehaviourScript.cs       # 사용 예시 컴포넌트
```

---

## ⚙️ 호환성 및 사양 (Compatibility)

- **Unity 버전**: Unity 6, Unity 7 및 2022.3 LTS 이상 완벽 지원
- **설치 방식**: UPM (Git URL / Package Manager) 및 Assets 직접 임포트 모두 지원
- **UI 시스템**: UI Toolkit (런타임 오버레이), IMGUI (에디터 윈도우)
- **플랫폼**: Windows, Mac, Android, iOS, WebGL (Release 빌드 시 스트리핑 지원)
