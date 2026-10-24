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

- 지원 버전: **Unity 6.3 (6000.3) 이상**
- 버전 고정: URL 끝에 `#<태그>` 또는 `#<전체 40자 커밋 해시>`를 붙입니다. 짧은 해시(예: `#869e2614`)는 UPM이 받지 않습니다.
  ```text
  https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat#<40자 커밋 해시>
  ```
- 버전을 고정하지 않으면 처음 설치할 때의 커밋이 `packages-lock.json`에 고정되고, 그 뒤로는 자동으로 올라가지 않습니다.

---

## 도구 목록 (Tools)

| 도구명 | 버전 | UPM Git URL | 설명 | 문서 링크 |
| :--- | :---: | :--- | :--- | :---: |
| **HB_InGameConsole** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole` | UI Toolkit 기반 런타임 인게임 콘솔 및 고성능 `HLog` 로깅/Watch/Report 툴킷. 다중 채널 필터링, 실시간 람다 Watch, 조건부 Pause(`BreakWhen` · `ReportWhen`) 및 스냅샷 리포트, 에디터 콘솔 창(로그 여러 줄 선택 · 복사), `USE_HLOG` 심볼 하나로 호출까지 제거. 처음 설치하면 `USE_HLOG`를 자동으로 켜고, 릴리즈 빌드에 켜져 있으면 경고. | [상세 문서](Assets/Tools/HB_InGameConsole/README.md) |
| **HB_Cheat** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat` | UI Toolkit 기반 개발·QA용 치트 패널(모바일 우선). 어트리뷰트로 치트 정의, 서버 치트 문자열 조합·전송, 릴리즈 빌드에서 엄격히 제외, Unity CLI 자동화(`cheat_*` 명령, JSON 응답). InGameConsole이 있으면 콘솔 탭으로 들어감. | [상세 문서](Assets/Tools/HB_Cheat/README.md) |
| **HB_E2String** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_E2String` | Roslyn Source Generator 기반 Enum to String 변환 제로 할당(Zero-Allocation) 최적화 및 관리 에디터 윈도우. | [상세 문서](Assets/Tools/HB_E2String/README.md) |
| **HB_Build** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build` | 멀티 플랫폼 커스텀 빌드 매니저(빌드 프로필 · 확장 가능한 빌드 스텝 · 환경별 심볼 · 씬 제외) 및 Jenkins CI/CD 파이프라인(Groovy Jenkinsfile) 생성기. 빌드 뒤 설정 복원, LIVE 검증 · 경고 파일, 빌드 중 바뀐 파일 보고. | [상세 문서](Assets/Tools/HB_Build/README.md) |
| **HB_TestFrameworkHelper** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_TestFrameworkHelper` | 프리팹 인스펙터 슬롯 누락(`[NeedSlotNullCheck]`) 및 Missing Script를 컴파일(분석기) · 테스트(Test Runner) · 빌드(HB_Build 빌드 스텝) 시점에 검증하는 QA 헬퍼. 에디터 검사 창 포함. | [상세 문서](Assets/Tools/HB_TestFrameworkHelper/README.md) |
| **HB_ScreenRecorder** | 1.0.0 | `https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_ScreenRecorder` | Unity Recorder 기반 GameView 화면 녹화(MP4), 스크린샷 캡처 및 시스템 진단 데이터/로그 자동 덤프 QA 도구. | [상세 문서](Assets/Tools/HB_ScreenRecorder/README.md) |

### 도구 간 연동

모두 선택 사항입니다. 상대 패키지가 없으면 그 연동만 빠지고 나머지는 그대로 동작합니다.

| 도구 | 함께 쓰는 패키지 | 연동 내용 |
| :--- | :--- | :--- |
| HB_Cheat | `com.hb.ingameconsole` | 치트 패널이 인게임 콘솔의 `Cheat` 탭으로 들어감 |
| HB_Cheat | `com.unity.pipeline` | Unity CLI 명령(`cheat_list` · `cheat_run` · `cheat_play` 등, JSON 응답) |
| HB_ScreenRecorder | `com.hb.ingameconsole` | 녹화 · 스크린샷 진단 파일에 HLog 기록을 함께 덤프 (리플렉션, 컴파일 참조 없음) |
| HB_TestFrameworkHelper | `com.hb.custombuild`(HB_Build) | 빌드 시작 전에 프리팹 슬롯 · Missing Script 검사 스텝이 먼저 돎 |
| HB_TestFrameworkHelper | `com.unity.test-framework` | 전체 프리팹 검사 테스트가 Test Runner에 보임 |
| HB_InGameConsole | HB_Build | DefineContainer의 LIVE 항목에서 `USE_HLOG`를 빼면 출시 빌드에서 HLog가 꺼짐 (켜진 채 릴리즈 빌드하면 InGameConsole이 경고) |

---

## 📄 라이선스 (License)

본 저장소의 모든 도구는 상업적 및 비상업적 프로젝트에서 자유롭게 사용할 수 있습니다.
