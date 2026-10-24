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
  - 스텝 ID(`StepId`)는 기본적으로 타입의 전체 이름입니다. 기본 스텝은 ID가 고정되어 있어 클래스 이름이 바뀌어도 그대로입니다 (예: `HB.CustomBuild.Editor.PreBuildStep_PlayerSettings`). 커스텀 스텝은 `StepId`를 직접 정의하면 이름을 바꿔도 프로필이 깨지지 않습니다. 기본 스텝을 상속해 오버라이드한 스텝은 부모의 고정 ID를 그대로 이어받습니다 (부모 대신 등록되므로 같은 ID로 끄고 켭니다).
  - 프로필의 비활성 목록(또는 `-disabledSteps`)에 등록되지 않은 ID가 있으면 LIVE · 배치 빌드는 실패하고, 로컬 DEV · QA는 경고합니다. Build Manager 창에서 제거할 수 있습니다.

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
- **`-buildTarget`은 필수입니다** (프로필 빌드도 프로필의 플랫폼과 같게). 배치 모드에서 활성 플랫폼이 빌드 대상과 다르면 도중에 전환하지 않고 실패합니다 (전환하면 스크립트가 재컴파일되지 않아 `#if UNITY_ANDROID` 등이 이전 플랫폼 기준으로 남음). 생성기가 만드는 명령에는 들어 있습니다.
- **버전 코드**: Android · iOS 빌드는 `-versionCode`(또는 프로필 값)가 비어 있으면 실패합니다. 기본값으로 채우지 않습니다 (새 프로필 · 창도 빈 값으로 시작). 사전 스텝이 채우는 경우를 위해 스텝 실행 뒤, 빌드 직전에 확인합니다.
  - Android `versionCode`: 양의 정수. 스토어에 올리는 모든 빌드에서 계속 증가해야 합니다.
  - iOS `buildNumber`(CFBundleVersion): 점으로 구분한 정수 1~3개 (예: `12`, `1.0.3`). 같은 표시 버전 안에서만 고유하면 되고, 표시 버전이 바뀌면 다시 시작할 수 있습니다. 그래서 두 플랫폼은 따로 관리하세요.

## 빌드 동작 메모
- **스텝 On/Off**: Build Manager 창의 체크박스 상태는 빌드 프로필 에셋(`DisabledStepIds`)에 저장되어 로컬과 CI에서 동일하게 적용됩니다.
- **설정 복원**: 기본 스텝이 변경한 PlayerSettings / EditorUserBuildSettings(버전, 심볼, 로그 설정 등)는 빌드 성공·실패와 관계없이 빌드 전 값으로 복원됩니다. 커스텀 스텝이 다른 설정을 바꾸면 직접 복원해야 합니다.
- **전처리기 심볼**: 항목이 있는데 리스트가 비어 있으면 심볼을 모두 제거합니다. DefineContainer 또는 해당 빌드 타입/플랫폼 항목이 없거나 심볼 설정 스텝을 끄면 **LIVE는 실패**하고(커밋된 ProjectSettings의 개발용 심볼이 그대로 들어가는 것 방지), DEV · QA는 기존 심볼을 유지합니다 (항목이 없으면 경고).
  - HB_InGameConsole을 쓰면 LIVE 항목에서 `USE_HLOG`를 빼서 출시 빌드의 HLog를 끕니다 (켜진 채 릴리즈 빌드하면 InGameConsole이 경고).
- **LIVE + Development Build**: 라이브 환경 프로파일링 · 로그 확인용으로 막지 않습니다. 대신 산출물 이름에 `_DEVBUILD`가 붙고(예: `Game_v1.0.0_LIVE_DEVBUILD.apk`), 빌드 시작 · 끝에 경고 블록이 찍히고, 로컬 빌드는 확인 창을 띄우고, Jenkins는 결과가 UNSTABLE이 됩니다.
- **경고 파일**: 빌드를 막지 않는 경고(LIVE + Development, 심볼 항목 없음, 등록되지 않은 비활성 ID 등)가 있으면 `BuildOutput/HB_BUILD_WARNINGS.txt`에 남깁니다. 생성한 Jenkinsfile은 이 파일이 있으면 내용을 출력하고 UNSTABLE로 표시합니다.
- **빌드가 바꾼 파일**: 빌드 전후 `git status`를 비교해 빌드 중 바뀐 파일(ProjectSettings 재저장 등)을 로그에 출력합니다. 자동으로 되돌리지 않으니 직접 확인하고 되돌리세요 (git이 PATH에 없으면 건너뜀).
- **직접 빌드 경고**: Unity 기본 빌드(File > Build 등)처럼 HB_Build를 거치지 않은 플레이어 빌드는 콘솔에 경고합니다. HB_Build 스텝(심볼, 검증, 설정 복원)이 실행되지 않으므로 팀 빌드는 Build Manager로 하세요.
- **출력 경로**: 기본값은 프로젝트 기준 `BuildOutput/<Target>`입니다. Windows 빌드는 `<출력경로>/<이름>/<이름>.exe`처럼 버전별 하위 폴더에 생성되고, ZIP은 `<출력경로>/<이름>.zip`으로 생성됩니다. 같은 이름의 폴더가 이미 있으면 덮어쓰지 않고 `<이름> (2)`, `<이름> (3)` 폴더로 분리합니다. ZIP에는 `*_DoNotShip`, `*_ButDontShipItWithYourGame` 폴더가 포함되지 않습니다.
- **로컬 빌드**: 프로필 빌드는 저장된 프로필 값으로 실행됩니다. 창에 저장하지 않은 변경이 있으면 빌드 전에 저장 여부를 묻고, 자동 저장하지 않습니다.
- **Jenkinsfile**: 생성된 파이프라인은 빌드 전에 `BuildOutput/` 폴더만 지웁니다 (`Library/` 캐시는 유지). 소스 정리는 Jenkins job의 checkout 설정을 사용하세요.
  - 명령은 Unity 에디터를 직접 호출합니다. 경로는 에이전트 환경변수 `UNITY_PATH`, 없으면 `ProjectSettings/ProjectVersion.txt`의 버전으로 `${UNITY_EDITORS_ROOT}/<버전>/Editor/Unity.exe`(기본 `C:/Program Files/Unity/Hub/Editor`, macOS는 `/Applications/Unity/Hub/Editor/<버전>/Unity.app/...`)를 씁니다. Jenkinsfile에 경로를 박지 않으므로 Unity를 올려도 다시 생성할 필요가 없습니다. Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없습니다.
  - 프로필 모드는 생성 시점의 "프로필 경로 → 플랫폼" 표로 `-buildTarget`을 넘깁니다. 프로필의 플랫폼을 바꾸면 Jenkinsfile을 다시 생성하세요 (그대로 두면 배치 빌드가 플랫폼 불일치로 실패).
  - 목록(`BUILD_PROFILE`)에 없는 프로필(빌드 PC 전용 프로필 등)은 `BUILD_PROFILE_PATH`에 경로를, `BUILD_TARGET`에 플랫폼을 직접 넣습니다. 플랫폼이 틀리면 배치 빌드가 불일치로 실패합니다.
  - 프로필 모드의 Jenkins 빌드는 프로필의 출력 경로 대신 `BuildOutput/<플랫폼>`에 만듭니다 (정리 · 보관 대상과 일치).
  - Linux 에이전트는 `UNITY_PATH`를 반드시 설정하세요 (기본 경로 찾기는 Windows · macOS Hub 경로만 지원).
  - **전체 풀기 모드**: `BUILD_TARGET` · `BUILD_TYPE` · `BUILD_VERSION` · `VERSION_CODE`와 `DEV_BUILD` · `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE`(booleanParam)가 Jenkins 파라미터입니다. 기본값은 생성한 사람의 창과 무관한 안전값(첫 선택지, 체크 모두 끔, 버전 코드 빈 값)이고, Jenkins에서 바꾼 값이 우선합니다. 씬 목록과 비활성 스텝은 생성 시 창 값으로 고정됩니다.
  - **프로필+오버라이드 모드**: 기본은 고른 프로필(`BUILD_PROFILE`)의 값입니다. Jenkins 파라미터 `BUILD_TYPE` · `DEV_BUILD`(`(profile)`이 아닌 값), `BUILD_VERSION` · `VERSION_CODE`(비어 있지 않은 값)를 넣은 것만 인자로 넘겨 프로필 값을 덮어씁니다.
- **선택 설정 에셋 생성**: `DefineContainer.asset` · `SceneDefine.asset`이 이미 있으면 새로 만들지 않고 기존 에셋을 연결합니다.
- Build Settings에 활성 씬이 없고 프로필에도 씬이 없으면 빌드를 실패시킵니다.

## 운영 가이드: 라이브 프로필
- 프로필 에셋은 로컬 빌드와 Jenkins가 같이 쓰므로, 테스트하려고 바꾼 값(검증 스텝 끄기, Development Build 등)이 커밋되면 라이브 빌드에도 들어갑니다.
- 라이브 프로필은 빌드 PC 전용 폴더(예: `Assets/Editor/HBBuild/Profiles/BuildMachine/`)에 두고 그 폴더를 프로젝트 `.gitignore`에 등록하세요. Jenkins 워크스페이스는 `BuildOutput/`만 지우므로 빌드 PC에 둔 프로필이 유지됩니다 (Jenkins job의 checkout에서 `git clean -fdx`처럼 무시 파일까지 지우는 옵션을 쓰면 함께 지워지니 주의). Jenkins에서는 `BUILD_PROFILE_PATH`에 이 경로를, `BUILD_TARGET`에 플랫폼을 넣어 빌드합니다 (개발자 PC에서 만든 Jenkinsfile 목록에는 이 프로필이 없음).

## 에디터 메뉴 경로
- `Tools > HB > Custom Build Manager`
