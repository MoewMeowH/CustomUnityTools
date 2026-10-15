# Custom Unity Tools

Unity 프로젝트 개발 생산성, 로깅, 최적화, 자동화 및 QA 검증을 위한 커스텀 패키지 도구 모음입니다.  
각 툴은 Unity Package Manager (UPM)의 Git URL을 통해 프로젝트에 독립적으로 임포트하여 사용할 수 있습니다.

## 저장소의 의의

AI 에 익숙해지고 익숙해지는 김에 평소 필요하다 느꼈던 툴이나 만들까? 가 점점 커져버림 
오케스트레이션을 구축하면서 방향과 위험사항, 사용 사례등을 내가 정리하고 그에 대한 생성을 오케스트레이션이 함

---

## UPM 패키지 설치 방법 (Installation)

1. Unity 에디터 상단 메뉴에서 **Window > Package Manager**를 엽니다.
2. 좌측 상단의 `+` 버튼을 클릭하고 **`Add package from git URL...`**을 선택합니다.
3. 아래 목록에서 원하는 도구의 **UPM Git URL**을 복사하여 입력한 뒤 **`Add`**를 클릭합니다.

---

## 도구 목록 (Tools)

| 도구명 | UPM Git URL | 설명 | 문서 링크 |
| :--- | :--- | :--- | :---: |
| **HB_InGameConsole** | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole` | UI Toolkit 기반 런타임 인게임 콘솔 및 고성능 `HLog` 로깅/Watch/Report 툴킷. 다중 채널 필터링, 실시간 람다 Watch, 조건부 Pause 및 스냅샷 리포트 지원. | [상세 문서](Assets/Tools/HB_InGameConsole/README.md) |
| **HB_E2String** | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_E2String` | Roslyn Source Generator 기반 Enum to String 변환 제로 할당(Zero-Allocation) 최적화 및 관리 에디터 윈도우. | [상세 문서](Assets/Tools/HB_E2String/README.md) |
| **HB_Build** | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build` | 멀티 플랫폼 커스텀 빌드 매니저 및 Jenkins CI/CD 자동화 파이프라인(Groovy Jenkinsfile) 생성기. | [상세 문서](Assets/Tools/HB_Build/README.md) |
| **HB_TestFrameworkHelper** | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_TestFrameworkHelper` | 프리팹 인스펙터 슬롯 누락(`[NeedSlotNullCheck]`) 및 Missing Script를 컴파일/테스트/빌드 타임에 검증하는 QA 헬퍼. | [상세 문서](Assets/Tools/HB_TestFrameworkHelper/README.md) |
| **HB_ScreenRecorder** | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_ScreenRecorder` | Unity Recorder 기반 GameView 화면 녹화(MP4), 스크린샷 캡처 및 시스템 진단 데이터/로그 자동 덤프 QA 도구. | [상세 문서](Assets/Tools/HB_ScreenRecorder/README.md) |

---

## 📄 라이선스 (License)

본 저장소의 모든 도구는 상업적 및 비상업적 프로젝트에서 자유롭게 사용할 수 있습니다.
