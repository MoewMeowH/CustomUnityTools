# HB_Build

Unity 멀티 플랫폼 커스텀 빌드 매니저와 Jenkins CI/CD 파이프라인 연동 도구입니다.

| 할 수 있는 일 | 설명서 |
|---|---|
| 빌드 설정을 프로필 에셋으로 저장하고, 빌드 타입별 심볼 · 씬을 관리 | [빌드 프로필 · 심볼 · 씬](manual/profiles-and-defines.md) |
| 빌드 전후 스텝을 자동 등록하고 프로필에서 끄고 켜기 | [빌드 스텝](manual/build-steps.md) |
| 배치 모드 명령줄 빌드, `.bat` · `.ps1` 생성 | [CLI 빌드](manual/cli.md) |
| 빌드 뒤 설정 복원, 경고 파일, 산출물 이름, 빌드 전 git 정리 | [빌드 동작](manual/build-behavior.md) |
| Jenkinsfile 생성 (SCM · 빌드 PC 로컬 폴더) | [Jenkins](manual/jenkins.md) |
| iOS 서명과 Xcode 프로젝트 설정을 에셋으로 관리 | [iOS 서명 · Xcode 설정](manual/ios.md) |

---

## 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build
```

- 패키지 이름은 `com.hb.custombuild`이고, Unity 6.3(`6000.3`) 이상을 지원합니다.
- 에디터 전용 어셈블리 `HB.CustomBuild.Editor` 하나로 구성됩니다.

---

## 에디터 메뉴

- `Tools > HB > Custom Build Manager`
- `Assets > Create > HB_Build > Build Profile` · `DefineContainer` · `SceneDefine` · `Xcode Settings` · `Xcode Fragment (SDK)`
  - Xcode Fragment를 뺀 나머지는 창의 버튼으로도 만들 수 있습니다.

### Build Manager 창

| 창 영역 | 기능 |
|---|---|
| 빌드 프로필 | `+ 새 프로필`(창의 현재 값으로 `BuildProfile_<플랫폼>_<타입>` 생성) · `프로필 저장` · `복제`, 오브젝트 필드 · 목록으로 선택 |
| 타겟 플랫폼 & 환경 | Target Platform(`PC` · `Android` · `iOS`) · Build Type |
| 선택적 설정 에셋 | Scene Define · Define Container · Xcode Settings (iOS) 슬롯, 비어 있으면 `+ 에셋 생성` |
| 빌드 파이프라인 실행 순서 | 사전 스텝 → BuildPlayer → 사후 스텝 목록 (아래 설명) |
| 코드 미리보기 | 스텝 이름을 누르면 C# 소스를 보입니다 (이 클래스만 · 전체 파일, IDE에서 열기, 에셋 위치 Ping) |
| 버전 정보 | App Version · Version Code, iOS면 Provision UUID |
| 빌드 세부 옵션 | Development Build(켜면 Deep Profiling), Clean Build Cache, Android면 AAB, PC면 ZIP |
| 빌드 출력 경로 | 직접 입력 · 폴더 선택 · 열기. 비우면 기본 경로 표시 |
| 빌드 씬 목록 | Build Settings에서 불러오기 · 현재 씬 추가 · 전체 비우기 · 하나씩 제거 |
| Jenkins & Unity CLI 명령어 생성기 | 모드(순수 프로필 · 전체 풀기 · 프로필+오버라이드), 미리보기, 복사, `.bat` · `.ps1` 저장 |
| 하단 버튼 | `Local Player Build 실행` · `Generate Jenkinsfile` |

빌드 파이프라인 실행 순서 영역에서는 체크박스로 스텝을 끄고 켭니다.
필터(조건 만족만 · 전체), 새로고침, 등록되지 않은 비활성 ID 제거도 여기서 합니다.

---

## 설명서

| 문서 | 내용 |
|---|---|
| [빌드 프로필 · 심볼 · 씬](manual/profiles-and-defines.md) | 플랫폼별 산출물, `HBBuildProfile` 필드, `ProjectDefine` · `SceneDefine` |
| [빌드 스텝](manual/build-steps.md) | 스텝 파이프라인, 기본 스텝 표, 커스텀 스텝, 빌드 전 검증 |
| [CLI 빌드](manual/cli.md) | `-executeMethod` 명령, 인자 표, 버전 코드 규칙, 명령어 생성기 |
| [빌드 동작](manual/build-behavior.md) | 빌드 순서, 설정 복원, 심볼, 경고 파일, 산출물 이름, 빌드 전 git 정리, 출력 경로 |
| [Jenkins](manual/jenkins.md) | Jenkinsfile 생성, 파라미터 모드, 로컬 폴더 모드, 라이브 프로필 운영 |
| [iOS 서명 · Xcode 설정](manual/ios.md) | 서명 종류, `HBXcodeSettings` · `HBXcodeFragment`, 충돌 규칙, 지역화 |
