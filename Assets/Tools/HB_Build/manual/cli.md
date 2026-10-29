# CLI 빌드

배치 빌드는 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild`로 부릅니다.

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

프로필 빌드 (프로필 값 + 넘긴 인자만 덮어쓰기):

```bash
Unity.exe -batchmode -quit -projectPath "<ProjectPath>" \
  -executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild \
  -logFile "build.log" \
  -buildTarget Android \
  -buildProfile "Assets/Editor/HBBuild/Profiles/BuildProfile_Android_LIVE.asset" \
  -versionCode 42
```

## 인자

| 인자 | 값 | 설명 |
|---|---|---|
| `-buildProfile` | 프로필 에셋 경로 | 있으면 프로필 빌드 (아래 설명) |
| `-buildTarget` | `StandaloneWindows64` · `Android` · `iOS` | 필수 ([-buildTarget](#-buildtarget)) |
| `-buildType` | `DEV` · `QA` · `LIVE` | 대소문자 무시. 없으면 DEV (프로필 빌드는 프로필 값) |
| `-buildVersion` | 문자열 | 없으면 PlayerSettings의 버전 (프로필 빌드는 프로필 값) |
| `-versionCode` | 문자열 | Android · iOS 필수. 기본값 없음 ([버전 코드](#버전-코드)) |
| `-devBuild` · `-deepProfile` · `-cleanBuild` | `true` · `false` | 없으면 false. `-deepProfile`은 Development Build일 때만 적용 |
| `-isAab` · `-zipPackage` | `true` · `false` | Android AAB / PC ZIP |
| `-outputPath` | 경로 | 없으면 `BuildOutput/<Target>`. 상대 경로는 프로젝트 루트 기준 |
| `-sceneList` | `"씬1;씬2"` (`;` · `,` · `$` 구분) | 프로필 없는 빌드만. 없거나 `NONE`이면 Build Settings의 활성 씬 |
| `-disabledSteps` | `"ID1;ID2"` | 이번 빌드에서 끌 스텝 ID. `NONE` 또는 빈 값이면 비활성 목록을 비웁니다 |
| `-autoProfile` | `true` · `false` | Development Build면 플레이어가 에디터 Profiler에 자동 연결합니다 |

- `-buildProfile`: 경로가 틀리면 다른 프로필로 대체하지 않고 실패하며, 있는 프로필 목록을 로그에 찍습니다.
- `-autoProfile`은 `BuildOptions.ConnectWithProfiler` + `EditorUserBuildSettings.connectProfiler`를 켭니다.
- `-disabledSteps "StepFullName1;StepFullName2"`: 이번 빌드에서 뺄 스텝 ID입니다.
  프로필 빌드는 프로필의 `DisabledStepIds`를 쓰고, `-disabledSteps`를 넘기면 그 목록으로 바꿉니다 (합치지 않음).
- 프로필 빌드에서 덮어쓸 수 있는 인자: `-buildType` · `-buildVersion` · `-versionCode` · `-devBuild` · `-deepProfile` · `-cleanBuild` · `-isAab` · `-zipPackage` · `-outputPath` · `-disabledSteps`
  - `-sceneList`는 읽지 않습니다.

## 인자 해석

- 인자 이름은 `-`로 시작하는 토큰만 봅니다. 다른 인자의 값이 우연히 이름과 같아도 이름으로 보지 않습니다.
- 값 자리에 `-`로 시작하는 토큰이 오면 값이 없는 것으로 봅니다.
  - Unity는 빈 인자(`""`)를 버립니다. 그래서 `-buildVersion "" -versionCode ""`는 `-buildVersion -versionCode`로 들어옵니다.
  - 결과: 버전은 PlayerSettings 값, 버전 코드는 빈 값(모바일은 실패)입니다.
  - `true` · `false` 인자가 이렇게 비면 실패합니다.
- `-buildType`, `-devBuild` 등에 잘못된 값을 주면 기본값으로 대체하지 않고 빌드를 실패시킵니다.
  `true` · `false` 인자를 값 없이 쓰거나 다른 값을 줘도 실패합니다.
- 실패는 예외로 끝나므로 Unity가 0이 아닌 종료 코드를 돌려줍니다. 빌드 스크립트 · Jenkins는 종료 코드로 성공 여부를 봅니다.

## -buildTarget

**`-buildTarget`은 필수입니다.** 프로필 빌드도 프로필의 플랫폼과 같게 넣습니다. 생성기가 만드는 명령에는 들어 있습니다.

- Unity가 이 플랫폼으로 프로젝트를 엽니다. 프로필 없는 빌드는 HB_Build도 이 값으로 빌드합니다 (`BuildTarget` 이름).
- 배치 모드에서 활성 플랫폼이 빌드 대상과 다르면 도중에 전환하지 않고 실패합니다.
  전환하면 스크립트가 재컴파일되지 않아 `#if UNITY_ANDROID` 등이 이전 플랫폼 기준으로 남기 때문입니다.

## 버전 코드

Android · iOS 빌드는 `-versionCode`(또는 프로필 값)가 비어 있으면 실패합니다.

- 기본값으로 채우지 않습니다 (새 프로필 · 창도 빈 값으로 시작).
- 사전 스텝이 채우는 경우를 위해 스텝 실행 뒤, 빌드 직전에 확인합니다.
- 값을 채우는 커스텀 스텝은 플랫폼 설정 스텝(Order 30)보다 먼저 실행되어야 합니다.
  - Android 설정 스텝은 값이 틀리면 그 자리에서 실패합니다.
  - 두 플랫폼 스텝 모두 그때의 값을 PlayerSettings에 씁니다.
- Android `versionCode`: 양의 정수. 스토어에 올리는 모든 빌드에서 계속 증가해야 합니다.
- iOS `buildNumber`(CFBundleVersion): 점으로 구분한 정수 1~3개 (예: `12`, `1.0.3`).
  같은 표시 버전 안에서만 고유하면 되고, 표시 버전이 바뀌면 다시 시작할 수 있습니다.

그래서 두 플랫폼의 버전 코드는 따로 관리하세요.

## CLI 명령어 생성기

Build Manager 창의 생성기는 명령을 복사하거나 `.bat` · `.ps1`로 저장합니다.

- 로컬 명령은 현재 에디터 실행 파일과 이 프로젝트 경로로 만들고, 로그는 `build.log`에 씁니다.
- 프로필+오버라이드 모드의 로컬 명령은 창의 버전 · 버전 코드 · 빌드 타입 · Development 값을 덮어쓰기 인자로 넣습니다.
- `.ps1`은 Unity 종료를 기다린 뒤 종료 코드를 그대로 돌려줍니다.
- `.bat`은 UTF-8(BOM 없음)로 씁니다. 첫머리에서 `chcp 65001`로 코드 페이지를 맞춰 한글 경로가 깨지지 않게 하고, 끝나면 원래 코드 페이지로 되돌립니다.
