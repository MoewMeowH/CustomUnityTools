# HB_Build

Unity 멀티 플랫폼 커스텀 빌드 매니저 및 Jenkins CI/CD 파이프라인 연동 도구입니다.

---

## 📦 UPM 패키지 설치 (Installation)

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build
```

---

## 주요 기능
- **멀티 플랫폼 빌드**: Windows 64bit, Android (APK/AAB), iOS 빌드를 지원합니다.
- **환경별 심볼 관리 (`ProjectDefine`)**: `DEV`, `QA`, `LIVE` 빌드 타입 및 플랫폼별 Scripting Define Symbols를 `ScriptableObject`로 관리합니다.
- **`TypeCache` 기반 빌드 스텝 파이프라인 (`BuildStepRegistry`)**:
  - 프로젝트 내 `IPreBuildStep`, `IPostBuildStep` 구현체를 자동 감지하여 `ExecutionOrder` 순으로 실행합니다.
  - 상속 관계에 있는 빌드 스텝이 있을 경우 파생 클래스가 부모 스텝을 자동으로 오버라이드하도록 필터링합니다.
- **Jenkinsfile (Groovy) 생성 및 CLI 연동**:
  - 에디터 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 생성합니다.
  - CLI 환경에서 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild` 호출을 통한 배치를 지원합니다.
- **네임스페이스**: 모든 타입은 `HB.CustomBuild.Editor`에 있습니다. 커스텀 스텝(`IPreBuildStep` · `IPostBuildStep`)을 만들 때 `using HB.CustomBuild.Editor;`를 추가하세요.
  - 스텝 ID(`StepId`)는 기본적으로 타입의 전체 이름입니다 (예: `HB.CustomBuild.Editor.PreBuildStep_PlayerSettings`). `-disabledSteps`와 프로필의 비활성 목록에 이 이름을 씁니다.

---

## CLI 빌드 예시
```bash
Unity.exe -batchmode -quit -projectPath "<ProjectPath>" \
  -executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild \
  -logFile "build.log" \
  -buildTarget StandaloneWindows64 \
  -buildType DEV \
  -buildVersion 1.0.0 \
  -versionCode 1 \
  -devBuild true \
  -cleanBuild false \
  -isAab false \
  -outputPath "BuildOutput"
```

- `-buildType`, `-devBuild` 등에 잘못된 값을 주면 기본값으로 대체하지 않고 빌드를 실패시킵니다.
- `-disabledSteps "StepFullName1;StepFullName2"`: 이번 빌드에서 제외할 스텝 ID (프로필 빌드는 프로필의 `DisabledStepIds`를 사용)
- 프로필의 플랫폼과 같은 `-buildTarget`을 함께 넘기는 것을 권장합니다. 빌드 도중 플랫폼을 전환하면 스크립트가 재컴파일되지 않아 `#if UNITY_ANDROID` 등이 이전 플랫폼 기준으로 동작합니다.

## 빌드 동작 메모
- **스텝 On/Off**: Build Manager 창의 체크박스 상태는 빌드 프로필 에셋(`DisabledStepIds`)에 저장되어 로컬과 CI에서 동일하게 적용됩니다.
- **설정 복원**: 기본 스텝이 변경한 PlayerSettings / EditorUserBuildSettings(버전, 심볼, 로그 설정 등)는 빌드 성공·실패와 관계없이 빌드 전 값으로 복원됩니다. 커스텀 스텝이 다른 설정을 바꾸면 직접 복원해야 합니다.
- **전처리기 심볼**: DefineContainer에 해당 빌드 타입/플랫폼 항목이 없으면 기존 심볼을 유지하고, 항목은 있는데 리스트가 비어 있으면 심볼을 모두 제거합니다.
- **출력 경로**: 기본값은 프로젝트 기준 `BuildOutput/<Target>`입니다. Windows 빌드는 `<출력경로>/<이름>/<이름>.exe`처럼 버전별 하위 폴더에 생성되고, ZIP은 `<출력경로>/<이름>.zip`으로 생성됩니다. 같은 이름의 폴더가 이미 있으면 덮어쓰지 않고 `<이름> (2)`, `<이름> (3)` 폴더로 분리합니다. ZIP에는 `*_DoNotShip`, `*_ButDontShipItWithYourGame` 폴더가 포함되지 않습니다.
- **로컬 빌드**: 프로필 빌드는 저장된 프로필 값으로 실행됩니다. 창에 저장하지 않은 변경이 있으면 빌드 전에 저장 여부를 묻고, 자동 저장하지 않습니다.
- **Jenkinsfile**: 생성된 파이프라인은 빌드 전에 `BuildOutput/` 폴더만 지웁니다 (`Library/` 캐시는 유지). 소스 정리는 Jenkins job의 checkout 설정을 사용하세요.
  - 명령은 `Unity.exe`를 직접 호출합니다 (environment의 `UNITY_PATH`). Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없습니다.
  - **프로필+오버라이드 모드**: 기본은 고른 프로필(`BUILD_PROFILE`)의 값입니다. Jenkins 파라미터 `BUILD_TYPE` · `DEV_BUILD`(`(profile)`이 아닌 값), `BUILD_VERSION` · `VERSION_CODE`(비어 있지 않은 값)를 넣은 것만 인자로 넘겨 프로필 값을 덮어씁니다.
- **선택 설정 에셋 생성**: `DefineContainer.asset` · `SceneDefine.asset`이 이미 있으면 새로 만들지 않고 기존 에셋을 연결합니다.
- Build Settings에 활성 씬이 없고 프로필에도 씬이 없으면 빌드를 실패시킵니다.

## 에디터 메뉴 경로
- `Tools > HB > Custom Build Manager`
