# CLI 빌드

배치 빌드는 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild`로 부릅니다.
**빌드 프로필(`-buildProfile`)이 필수**이고, 프로필 값 중 버전 · 버전 코드 · Development만 인자로 덮어쓸 수 있습니다.

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
| `-buildProfile` | 프로필 에셋 경로 | 필수. 없거나 비면 실패 |
| `-buildTarget` | `StandaloneWindows64` · `Android` · `iOS` | 필수. 프로필의 플랫폼과 같게 ([-buildTarget](#-buildtarget)) |
| `-buildVersion` | 문자열 | 넣으면 프로필의 버전을 덮어씀 |
| `-versionCode` | 문자열 | 넣으면 프로필의 버전 코드를 덮어씀 ([버전 코드](#버전-코드)) |
| `-devBuild` | `true` · `false` | 넣으면 프로필의 Development Build를 덮어씀. `false`면 Deep Profiling도 꺼짐 |

- `-buildProfile`: 경로가 틀리면 다른 프로필로 대체하지 않고 실패하며, 있는 프로필 목록을 로그에 찍습니다.
- 덮어쓴 값은 로그에 `[CLI Override] Version: 1.0.0 -> 1.2.0`처럼 찍힙니다.
- 빌드 타입 · AAB · ZIP · Deep Profiling · Clean Build · Profiler 자동 연결 · 씬 · 꺼 둘 스텝은 프로필에서만 정합니다.
  - 아래 인자를 넘기면 무시하지 않고 빌드가 실패합니다: `-buildType` · `-isAab` · `-zipPackage` · `-deepProfile` · `-cleanBuild` · `-disabledSteps` · `-sceneList` · `-outputPath` · `-autoProfile`
  - 다른 값으로 빌드해야 하면 프로필을 하나 더 만드세요 (예: `BuildProfile_Android_LIVE`와 `BuildProfile_Android_QA`).
- 출력은 항상 `BuildOutput/<Target>`입니다 ([출력 경로](build-behavior.md#출력-경로)).

## 인자 해석

- 인자 이름은 `-`로 시작하는 토큰만 봅니다. 다른 인자의 값이 우연히 이름과 같아도 이름으로 보지 않습니다.
- 값 자리에 `-`로 시작하는 토큰이 오면 값이 없는 것으로 봅니다.
  - Unity는 빈 인자(`""`)를 버립니다. 그래서 `-buildVersion "" -versionCode ""`는 `-buildVersion -versionCode`로 들어옵니다.
  - 결과: 값이 없는 덮어쓰기 인자는 넘기지 않은 것과 같습니다 (프로필 값 그대로).
  - `-devBuild`가 이렇게 비면 실패합니다.
- `-devBuild`에 `true` · `false`가 아닌 값을 주거나 값 없이 쓰면 기본값으로 대체하지 않고 빌드를 실패시킵니다.
- 실패는 예외로 끝나므로 Unity가 0이 아닌 종료 코드를 돌려줍니다. 빌드 스크립트 · Jenkins는 종료 코드로 성공 여부를 봅니다.

## -buildTarget

**`-buildTarget`은 필수입니다.** 프로필의 플랫폼과 같게 넣습니다. 생성기가 만드는 명령에는 들어 있습니다.

- Unity가 이 플랫폼으로 프로젝트를 엽니다. HB_Build는 이 값을 읽지 않고 프로필의 플랫폼으로 빌드합니다.
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
- 명령은 `-buildTarget <프로필 플랫폼>` · `-buildProfile <선택한 프로필>`만 넣습니다 (덮어쓰기 인자 없음). 저장된 프로필 값으로 빌드되므로 창에서 고친 뒤 `프로필 저장`을 하세요.
- `.ps1`은 Unity 종료를 기다린 뒤 종료 코드를 그대로 돌려줍니다.
- `.bat`은 UTF-8(BOM 없음)로 씁니다. 첫머리에서 `chcp 65001`로 코드 페이지를 맞춰 한글 경로가 깨지지 않게 하고, 끝나면 원래 코드 페이지로 되돌립니다.
