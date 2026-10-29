# Custom Unity Tools

Unity 프로젝트 개발 생산성, 로깅, 최적화, 자동화 및 QA 검증을 위한 커스텀 패키지 도구 모음입니다.  
각 툴은 Unity Package Manager (UPM)의 Git URL을 통해 프로젝트에 독립적으로 임포트하여 사용할 수 있습니다.

## 저장소의 의의

AI 에 익숙해지고 익숙해지는 김에 평소 필요하다 느꼈던 툴이나 만들까? 가 점점 커져버림 
오케스트레이션을 구축하면서 방향과 위험사항, 사용 사례등을 내가 정리하고 그에 대한 생성을 오케스트레이션이 함

---

## 도구 목록

| 도구 | 패키지 | 버전 | 한 줄 설명 |
| :--- | :--- | :---: | :--- |
| [HB_InGameConsole](Assets/Tools/HB_InGameConsole/README.md) | `com.hb.ingameconsole` | 1.0.0 | 런타임 인게임 콘솔과 `HLog` 로깅 · Watch · Report |
| [HB_Cheat](Assets/Tools/HB_Cheat/README.md) | `com.hb.cheat` | 1.0.0 | 개발 · QA용 치트 패널과 Unity CLI 자동화 |
| [HB_E2String](Assets/Tools/HB_E2String/README.md) | `com.hb.e2string` | 1.0.0 | 할당 없는 Enum ↔ 문자열 변환 Source Generator |
| [HB_Build](Assets/Tools/HB_Build/README.md) | `com.hb.custombuild` | 1.0.0 | 멀티 플랫폼 빌드 매니저와 Jenkinsfile 생성기 |
| [HB_TestFrameworkHelper](Assets/Tools/HB_TestFrameworkHelper/README.md) | `com.hb.testframeworkhelper` | 1.0.0 | 인스펙터 슬롯 · 에셋 건강 검사 QA 헬퍼와 테스트 작성 도우미 |
| [HB_ScreenRecorder](Assets/Tools/HB_ScreenRecorder/README.md) | `com.hb.screenrecorder` | 1.0.0 | GameView 녹화 · 스크린샷과 진단 데이터 덤프 |

도구 이름을 누르면 상세 문서로 갑니다. 도구마다 할 수 있는 일은 [도구별 기능](#도구별-기능)에 있습니다.

---

## 설치

1. Unity 에디터 메뉴에서 **Window > Package Manager**를 엽니다.
2. 왼쪽 위 `+` 버튼을 누르고 **`Add package from git URL...`**을 고릅니다.
3. 아래 URL 중 원하는 도구의 URL을 붙여 넣고 **`Add`**를 누릅니다.

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_InGameConsole
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_E2String
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_TestFrameworkHelper
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_ScreenRecorder
```

- 지원 버전은 **Unity 6.3 (6000.3) 이상**입니다.
- 버전을 고정하려면 URL 끝에 `#<태그>` 또는 `#<전체 40자 커밋 해시>`를 붙입니다.
  짧은 해시(예: `#869e2614`)는 UPM이 받지 않습니다.
  ```text
  https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Cheat#<40자 커밋 해시>
  ```
- 버전을 고정하지 않으면 처음 설치할 때의 커밋이 `packages-lock.json`에 고정됩니다.
  그 뒤로는 자동으로 올라가지 않습니다.

---

## 도구별 기능

### HB_InGameConsole

- UI Toolkit 기반 런타임 인게임 콘솔과 고성능 `HLog` 로깅 · Watch · Report 툴킷입니다.
- 다중 채널 필터링, 실시간 람다 Watch를 지원합니다.
- 조건부 Pause(`BreakWhen` · `ReportWhen`)와 스냅샷 리포트를 남깁니다.
- 에디터 콘솔 창에서 로그 여러 줄을 선택해 복사할 수 있습니다.
- `USE_HLOG` 심볼 하나로 호출까지 제거합니다.
  처음 설치하면 `USE_HLOG`를 자동으로 켜고, 릴리즈 빌드에 켜져 있으면 경고합니다.

### HB_Cheat

- UI Toolkit 기반 개발 · QA용 치트 패널입니다 (모바일 우선).
- 치트는 어트리뷰트로 정의하고, 서버 치트 문자열을 조합해 전송합니다.
- 릴리즈 빌드에서는 엄격하게 제외됩니다.
- Unity CLI 자동화(`cheat_*` 명령, JSON 응답)를 지원합니다.
- InGameConsole이 있으면 콘솔 탭으로 들어갑니다.

### HB_E2String

- Roslyn Source Generator로 Enum ↔ 문자열 변환을 할당 없이(Zero-Allocation) 만듭니다.
- 관리용 에디터 창이 있습니다.

### HB_Build

- 멀티 플랫폼 커스텀 빌드 매니저입니다.
  빌드 프로필, 확장 가능한 빌드 스텝, 환경별 심볼, 씬 제외, iOS Xcode 설정 에셋을 다룹니다.
- Jenkins CI/CD 파이프라인(Groovy Jenkinsfile)을 생성합니다.
  소스는 job의 SCM으로 받거나, 빌드 PC의 고정 작업 폴더를 git으로 갱신해 받습니다.
- 빌드 뒤 설정을 복원하고, LIVE 검증 · 경고 파일을 남기고, 빌드 중 바뀐 파일을 보고합니다.
- 산출물 이름에 커밋 해시와 Jenkins 빌드 번호를 붙여, 이름만으로 어느 빌드인지 찾을 수 있습니다.
- 로컬 빌드 전에 작업 폴더를 현재 커밋 상태로 되돌리는 옵션이 있습니다 (커밋 안 한 변경 없는 빌드).

### HB_TestFrameworkHelper

- 프리팹 인스펙터 슬롯 누락(`[NeedSlotNullCheck]`)과 Missing Script를 검증하는 QA 헬퍼입니다.
- 검증 시점은 컴파일(분석기) · 테스트(Test Runner) · 빌드(HB_Build 빌드 스텝)입니다.
- 에셋 건강 검사로 씬 · ScriptableObject · 머티리얼 · 일반 Missing 참조까지 넓혀 검사합니다 (Project Settings에서 검사마다 켜고 끔).
- 에디터 검사 창이 있습니다.
- 테스트 작성 도우미(`TestScope` · `HBTestFixture` · `HBAssert` · `HBWait` · 임시 폴더 · 임시 씬)로 테스트 준비 · 정리 코드를 줄입니다.

### HB_ScreenRecorder

- Unity Recorder 기반으로 GameView 화면을 MP4로 녹화하고 스크린샷을 찍습니다.
- 녹화 · 캡처 때 시스템 진단 데이터와 로그를 자동으로 덤프하는 QA 도구입니다.

---

## 도구 간 연동

모두 선택 사항입니다. 상대 패키지가 없으면 그 연동만 빠지고 나머지는 그대로 동작합니다.

| 도구 | 함께 쓰는 패키지 | 연동 내용 |
| :--- | :--- | :--- |
| HB_Cheat | `com.hb.ingameconsole` | 치트 패널이 인게임 콘솔의 `Cheat` 탭으로 들어감 |
| HB_Cheat | `com.unity.pipeline` | Unity CLI 명령(`cheat_list` · `cheat_run` · `cheat_play` 등, JSON 응답) |
| HB_ScreenRecorder | `com.hb.ingameconsole` | 녹화 · 스크린샷 진단 파일에 HLog 기록을 함께 덤프 (리플렉션, 컴파일 참조 없음) |
| HB_TestFrameworkHelper | `com.hb.custombuild`(HB_Build) | 빌드 시작 전에 켜진 검사(프리팹 슬롯 · 에셋 건강 검사)를 실제 빌드 씬으로 도는 스텝이 더해짐 |
| HB_TestFrameworkHelper | `com.unity.test-framework` | 프리팹 · 에셋 건강 검사 테스트가 Test Runner에 보이고, 테스트 작성 도우미를 쓸 수 있음 (설치한 프로젝트는 `testables` 등록 필요) |
| HB_InGameConsole | HB_Build | DefineContainer의 LIVE 항목에서 `USE_HLOG`를 빼면 출시 빌드에서 HLog가 꺼짐 (켜진 채 릴리즈 빌드하면 InGameConsole이 경고) |

---

## 라이선스

본 저장소의 모든 도구는 상업적 및 비상업적 프로젝트에서 자유롭게 사용할 수 있습니다.
