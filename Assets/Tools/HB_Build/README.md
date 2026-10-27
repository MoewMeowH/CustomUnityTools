# HB_Build

Unity 멀티 플랫폼 커스텀 빌드 매니저 및 Jenkins CI/CD 파이프라인 연동 도구입니다.

---

## 📦 UPM 패키지 설치 (Installation)

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_Build
```

- 패키지 이름 `com.hb.custombuild`, Unity 6.3(`6000.3`) 이상. 에디터 전용 어셈블리 `HB.CustomBuild.Editor` 하나로 구성됩니다.

---

## 주요 기능
- **멀티 플랫폼 빌드**: Windows 64bit, Android (APK/AAB), iOS 빌드를 지원합니다.
  - 산출물 이름은 `<Product Name>_v<버전>_<빌드 타입>`입니다 (LIVE + Development는 끝에 `_DEVBUILD`). Windows는 `<이름>/<이름>.exe`, Android는 `<이름>.apk` · `<이름>.aab`, iOS는 `<이름>/` Xcode 프로젝트 폴더입니다.
  - 빌드 옵션은 항상 `CompressWithLz4HC`이고, Development · Deep Profiling · Clean Build Cache는 체크한 경우에만 더합니다. Deep Profiling은 Development Build일 때만 적용됩니다.
- **빌드 프로필 (`HBBuildProfile`)**: 빌드 설정 한 벌을 `ScriptableObject` 에셋으로 저장합니다. 로컬 빌드와 Jenkins(`-buildProfile`)가 같은 에셋을 씁니다. 창의 `+ 새 프로필`은 `Assets/Editor/HBBuild/Profiles/`에 만듭니다.

  | 필드 | 설명 |
  |---|---|
  | `TargetPlatform` | `PC`(StandaloneWindows64) · `Android` · `iOS` |
  | `BuildType` | `DEV` · `QA` · `LIVE` |
  | `Version` | 앱 버전 (`PlayerSettings.bundleVersion`) |
  | `VersionCode` | Android `versionCode` / iOS `buildNumber`. 기본 빈 값 |
  | `DevBuild` · `DeepProfile` · `CleanBuild` | Development Build · Deep Profiling Support · Clean Build Cache |
  | `IsAab` | Android 전용. 켜면 `.aab`, 끄면 `.apk` |
  | `ZipPackage` | PC 전용. 빌드 폴더를 ZIP으로 압축 |
  | `ProvisionUUID` | iOS 전용. 값이 있으면 자동 서명을 끄고 이 Provisioning Profile로 수동 서명 (종류는 `CustomEntries`의 `ProvisionType`, 기본 Distribution. 아래 "iOS 서명") |
  | `OutputPath` | 비우면 `BuildOutput/<Target>`. 프로젝트 기준 상대 경로 권장 |
  | `Scenes` | 비우면(공백 항목만 있어도) Build Settings의 활성 씬 |
  | `SceneDefineAsset` · `ProjectDefineAsset` | 선택 설정 에셋. 비우면 기본 위치 → 프로젝트 검색 순으로 찾습니다 |
  | `XcodeSettingsAsset` | iOS 전용 선택 설정 에셋 (아래 "iOS Xcode 설정"). 찾는 규칙은 위와 같습니다 (기본 `Assets/Editor/HBBuild/XcodeSettings.asset`) |
  | `CustomEntries` | 스텝에 넘길 Key-Value (`BuildContext.CustomData`). 창에는 없고 Inspector에서 편집합니다 |
  | `DisabledStepIds` | 이 프로필에서 끌 스텝 ID. Build Manager 창의 체크박스로 편집 |

- **환경별 심볼 관리 (`ProjectDefine`)**: `DEV`, `QA`, `LIVE` 빌드 타입 및 플랫폼 종류(`PC` · `MOBILE`)별 Scripting Define Symbols를 `ScriptableObject`(기본 `Assets/Editor/HBBuild/DefineContainer.asset`)로 관리합니다. `MOBILE` 항목은 Android · iOS가 같이 씁니다.

  | 필드 (`Container` 항목마다) | 설명 |
  |---|---|
  | `BuildType` | `DEV` · `QA` · `LIVE` |
  | `PlatformType` | `PC`(Windows) · `MOBILE`(Android · iOS). `NONE`은 쓰지 않습니다 |
  | `DefineArr` | 적용할 심볼 목록. 빈 목록이면 심볼을 모두 지웁니다 |

  - 심볼은 `PlayerSettings.SetScriptingDefineSymbols`로 빌드 타깃 그룹에 씁니다 (빌드 뒤 복원).
- **환경별 씬 제외 (`SceneDefine`)**: 빌드 타입마다 빌드에서 뺄 씬을 정합니다 (기본 `Assets/Editor/HBBuild/SceneDefine.asset`). 에셋이 없으면 건너뜁니다.

  | 필드 (`Data` 항목마다) | 설명 |
  |---|---|
  | `Type` | `DEV` · `QA` · `LIVE` (새 에셋은 세 항목으로 시작) |
  | `IgnoreSceneList` | 뺄 씬 경로 (예: `Assets/Scenes/Debug.unity`). 씬 목록의 경로와 대소문자 무시로 비교합니다 |

- **iOS Xcode 설정 (`HBXcodeSettings` · `HBXcodeFragment`)**: Unity가 만든 Xcode 프로젝트에 손으로 하던 설정을 에셋으로 옮기고, iOS 빌드마다 HB_Build가 적용합니다. 아래 "iOS Xcode 설정"에 자세히 있습니다.
- **`TypeCache` 기반 빌드 스텝 파이프라인 (`BuildStepRegistry`)**:
  - 프로젝트 내 `IPreBuildStep`, `IPostBuildStep` 구현체를 자동 감지하여 `ExecutionOrder` 순으로 실행합니다.
  - 상속 관계에 있는 빌드 스텝이 있을 경우 파생 클래스가 부모 스텝을 자동으로 오버라이드하도록 필터링합니다.
  - `IsActive(context)`가 false이거나 비활성 목록에 있는 스텝은 실행하지 않습니다. 스텝이 예외를 던지면 빌드가 실패합니다.
  - 인스턴스를 만들지 못한 스텝(공개 기본 생성자 없음, 생성자 예외)이 있으면 빌드 시작 때 실패합니다 (빠진 스텝의 검증 · 설정 없이 빌드되는 것 방지).
- **기본 빌드 스텝**: ID는 모두 `HB.CustomBuild.Editor.` 뒤에 아래 이름이 붙습니다.

  | 스텝 | Order | 실행 조건 | 하는 일 |
  |---|---|---|---|
  | `PreBuildStep_FilterScenes` | 5 | 항상 (SceneDefine 없으면 건너뜀) | 빌드 타입의 `IgnoreSceneList` 씬을 목록에서 뺍니다 |
  | `PreBuildStep_PlayerSettings` | 10 | 항상 | `stripEngineCode` 끔, Player Log = Development Build 여부. 스택트레이스: Error · Exception은 ScriptOnly, Log · Warning · Assert는 Development 또는 DEV면 ScriptOnly, 아니면 None |
  | `PreBuildStep_ConfigDefines` | 20 | 항상 (LIVE는 끌 수 없음) | DefineContainer의 심볼을 적용합니다 (아래 "전처리기 심볼") |
  | `PreBuildStep_AndroidSettings` | 30 | Android | 버전 · versionCode, Development · Deep Profiling · Autoconnect Profiler, AAB 여부, Android Build Type(Development면 Debug, 아니면 Release), IL2CPP, ARM64 |
  | `PreBuildStep_WindowsSettings` | 30 | Windows | 버전, Development · Deep Profiling · Autoconnect Profiler |
  | `PreBuildStep_IOSSettings` | 30 | iOS | 버전 · buildNumber, 서명(아래 "iOS 서명"), Development · Deep Profiling · Autoconnect Profiler |
  | `BuildStep_XcodeSettings` | 40 | iOS (XcodeSettings 없으면 건너뜀) | 빌드 전: Xcode 설정 계획 · 충돌 검사(실패하면 BuildPlayer 전에 멈춤). 빌드 뒤: 생성된 Xcode 프로젝트에 적용 (아래 "iOS Xcode 설정"). 빌드 전 · 뒤가 한 스텝이라 체크박스 하나로 같이 끕니다 |
  | `PostBuildStep_ZipArchive` | 100 | Windows + `ZipPackage` | 빌드 폴더를 `<출력경로>/<이름>.zip`으로 압축합니다 (이미 있으면 지우지 않고 `<이름> (2).zip` …) |

  - HB_TestFrameworkHelper를 함께 설치하면 `HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots`(Order 1)가 더해집니다. HB_Build가 UPM 패키지로 설치됐을 때만 켜집니다 (`versionDefines`가 패키지를 보고 정하므로, 이 저장소처럼 `Assets`에 직접 둔 배치에서는 꺼짐).
- **확장 (커스텀 스텝)**: `IPreBuildStep`(`OnPreBuild`) · `IPostBuildStep`(`OnPostBuild`)을 구현한 클래스를 Editor 어셈블리에 두면 자동 등록됩니다. 따로 등록할 필요가 없습니다.

  | 멤버 | 설명 |
  |---|---|
  | `StepId` | 프로필의 On/Off 저장용 ID. 기본값은 타입 전체 이름 |
  | `ExecutionOrder` | 낮을수록 먼저 실행 (필수) |
  | `StepName` | 창과 로그에 보이는 이름 (필수) |
  | `IsActive(BuildContext)` | 이 빌드에서 실행할지. 기본값 true |

  - `BuildContext`로 빌드 값(`Target` · `BuildType` · `Version` · `VersionCode` · `Scenes` · `OutputPath` 등), `PlayerOptions`, 빌드 뒤 `Report`, `Warnings`를 읽고 씁니다. 컨텍스트 값을 바꾼 뒤에는 `RebuildPlayerOptions()`를 불러야 실제 빌드 옵션에 반영됩니다.
  - `AddWarning(message)`: 빌드를 막지 않는 경고를 로그와 경고 파일에 남깁니다. 빌드를 멈추려면 예외를 던지세요 (`BuildHelper.ThrowError(msg)`가 예외 객체를 돌려줌 → `throw`).
  - 스텝 사이 데이터는 `SetCustomData` · `GetCustomData<T>`로 주고받습니다. HB_Build는 `SceneDefine` · `ProjectDefine` · `ProvisionUUID` · `ProvisionType` · `XcodeSettings` · `XcodeSettings.Snapshot` 키를 쓰고, 프로필의 `CustomEntries`가 같은 키면 덮어쓰므로 피하세요.
  - 프로필 빌드(창 · `-buildProfile`)에만 `CustomEntries`가 들어갑니다. 프로필 없는 창 빌드와 전체 풀기 CLI 빌드에는 없습니다.
- **Jenkinsfile (Groovy) 생성 및 CLI 연동**:
  - 에디터 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 생성합니다.
  - CLI 환경에서 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild` 호출을 통한 배치를 지원합니다.
  - 창의 CLI 명령어 생성기는 같은 명령을 복사하거나 `.bat` · `.ps1`로 저장합니다. `.ps1`은 Unity 종료를 기다린 뒤 종료 코드를 그대로 돌려줍니다. `.bat`은 UTF-8(BOM 없음)로 쓰고 첫머리에서 `chcp 65001`로 코드 페이지를 맞춰 한글 경로가 깨지지 않게 합니다 (끝나면 원래 코드 페이지로 되돌림).
  - 생성한 Jenkinsfile은 `skipDefaultCheckout()`로 기본 checkout을 끄고 `Checkout SCM` 단계에서 한 번만 받습니다. 명령줄은 인자 하나마다 줄을 바꿉니다 (값 안의 ` -`는 그대로).
  - 소스를 받는 방식은 두 가지입니다 (창의 `Jenkins 체크아웃`): job의 SCM 설정으로 워크스페이스에 받는 `Scm`, 빌드 PC의 고정 작업 폴더를 git으로 맞추는 `LocalFolder` (아래 "로컬 폴더 모드").
- **빌드 전 검증**: 아래 조건이면 빌드하지 않고 실패합니다.
  - Product Name · 버전에 파일 이름에 못 쓰는 문자, `/` · `\`, `..`가 있음 (출력 경로 밖으로 벗어나는 것 방지, 사전 스텝이 바꾼 값까지 보도록 스텝 뒤에 확인)
  - 지원하지 않는 빌드 대상 (Windows · Android · iOS 밖, 예: `StandaloneOSX`) — 창 · 프로필 · CLI 모두, 플랫폼 전환 전에
  - 인스턴스를 만들지 못한 스텝이 있음, 같은 `StepId`로 둘 이상 등록된 스텝이 있음 (예: 같은 기본 스텝을 덮어쓴 형제 클래스 둘 — 한쪽이 다른 쪽을 상속하거나 각자 `StepId`를 정의)
  - 비활성 목록에 등록되지 않은 스텝 ID가 있음 (LIVE · 배치 모드만, 로컬 DEV · QA는 경고)
  - LIVE인데 심볼 설정 스텝을 껐거나 DefineContainer · 해당 항목이 없음, 기본 경로에 없는 DefineContainer · SceneDefine이 여러 개 (아래 "전처리기 심볼")
  - 모바일 버전 코드가 비었거나 형식이 틀림, 빌드할 씬이 없음, 배치 모드의 활성 플랫폼이 빌드 대상과 다름
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

프로필 빌드 (프로필 값 + 넘긴 인자만 덮어쓰기):
```bash
Unity.exe -batchmode -quit -projectPath "<ProjectPath>" \
  -executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild \
  -logFile "build.log" \
  -buildTarget Android \
  -buildProfile "Assets/Editor/HBBuild/Profiles/BuildProfile_Android_LIVE.asset" \
  -versionCode 42
```

| 인자 | 값 | 설명 |
|---|---|---|
| `-buildProfile` | 프로필 에셋 경로 | 있으면 프로필 빌드. 경로가 틀리면 다른 프로필로 대체하지 않고 실패하며, 있는 프로필 목록을 로그에 찍습니다 |
| `-buildTarget` | `StandaloneWindows64` · `Android` · `iOS` | Unity가 이 플랫폼으로 프로젝트를 엽니다. 프로필 없는 빌드는 HB_Build도 이 값으로 빌드합니다 (`BuildTarget` 이름, 필수) |
| `-buildType` | `DEV` · `QA` · `LIVE` | 대소문자 무시. 없으면 DEV (프로필 빌드는 프로필 값) |
| `-buildVersion` | 문자열 | 없으면 PlayerSettings의 버전 (프로필 빌드는 프로필 값) |
| `-versionCode` | 문자열 | Android · iOS 필수. 기본값 없음 |
| `-devBuild` · `-deepProfile` · `-cleanBuild` | `true` · `false` | 없으면 false. `-deepProfile`은 Development Build일 때만 적용 |
| `-isAab` · `-zipPackage` | `true` · `false` | Android AAB / PC ZIP |
| `-outputPath` | 경로 | 없으면 `BuildOutput/<Target>`. 상대 경로는 프로젝트 루트 기준 |
| `-sceneList` | `"씬1;씬2"` (`;` · `,` · `$` 구분) | 프로필 없는 빌드만. 없거나 `NONE`이면 Build Settings의 활성 씬 |
| `-disabledSteps` | `"ID1;ID2"` | 이번 빌드에서 끌 스텝 ID. `NONE` 또는 빈 값이면 비활성 목록을 비웁니다 |
| `-autoProfile` | `true` · `false` | Development Build면 플레이어가 에디터 Profiler에 자동 연결합니다 (`BuildOptions.ConnectWithProfiler` + `EditorUserBuildSettings.connectProfiler`) |

- 인자 이름은 `-`로 시작하는 토큰만 봅니다 (다른 인자의 값이 우연히 이름과 같아도 이름으로 보지 않음).
- 값 자리에 `-`로 시작하는 토큰이 오면 값이 없는 것으로 봅니다. Unity는 빈 인자(`""`)를 버리므로 `-buildVersion "" -versionCode ""`는 `-buildVersion -versionCode`로 들어옵니다 → 버전은 PlayerSettings 값, 버전 코드는 빈 값(모바일은 실패). `true` · `false` 인자가 이렇게 비면 실패합니다.
- `-buildType`, `-devBuild` 등에 잘못된 값을 주면 기본값으로 대체하지 않고 빌드를 실패시킵니다. `true` · `false` 인자를 값 없이 쓰거나 다른 값을 줘도 실패합니다.
- `-disabledSteps "StepFullName1;StepFullName2"`: 이번 빌드에서 제외할 스텝 ID. 프로필 빌드는 프로필의 `DisabledStepIds`를 쓰고, `-disabledSteps`를 넘기면 그 목록으로 바꿉니다 (합치지 않음).
- 프로필 빌드에서 덮어쓸 수 있는 인자: `-buildType` · `-buildVersion` · `-versionCode` · `-devBuild` · `-deepProfile` · `-cleanBuild` · `-isAab` · `-zipPackage` · `-outputPath` · `-disabledSteps`. `-sceneList`는 읽지 않습니다.
- 실패는 예외로 끝나므로 Unity가 0이 아닌 종료 코드를 돌려줍니다. 빌드 스크립트 · Jenkins는 종료 코드로 성공 여부를 봅니다.
- **`-buildTarget`은 필수입니다** (프로필 빌드도 프로필의 플랫폼과 같게). 배치 모드에서 활성 플랫폼이 빌드 대상과 다르면 도중에 전환하지 않고 실패합니다 (전환하면 스크립트가 재컴파일되지 않아 `#if UNITY_ANDROID` 등이 이전 플랫폼 기준으로 남음). 생성기가 만드는 명령에는 들어 있습니다.
- **버전 코드**: Android · iOS 빌드는 `-versionCode`(또는 프로필 값)가 비어 있으면 실패합니다. 기본값으로 채우지 않습니다 (새 프로필 · 창도 빈 값으로 시작). 사전 스텝이 채우는 경우를 위해 스텝 실행 뒤, 빌드 직전에 확인합니다.
  - 값을 채우는 커스텀 스텝은 플랫폼 설정 스텝(Order 30)보다 먼저 실행되어야 합니다. Android 설정 스텝은 값이 틀리면 그 자리에서 실패하고, 두 플랫폼 스텝 모두 그때의 값을 PlayerSettings에 씁니다.
  - Android `versionCode`: 양의 정수. 스토어에 올리는 모든 빌드에서 계속 증가해야 합니다.
  - iOS `buildNumber`(CFBundleVersion): 점으로 구분한 정수 1~3개 (예: `12`, `1.0.3`). 같은 표시 버전 안에서만 고유하면 되고, 표시 버전이 바뀌면 다시 시작할 수 있습니다. 그래서 두 플랫폼은 따로 관리하세요.

## 빌드 동작 메모
- **빌드 순서**: 이전 경고 파일 삭제 → 지원 대상 · 활성 플랫폼 확인 → 산출물 이름의 커밋 · Jenkins 확인 → `git status` 기록 → 설정 스냅샷 → 빌드 전 검증 → 사전 스텝 → 출력 이름 · 버전 코드 · 씬 확인 → `BuildPipeline.BuildPlayer` → 사후 스텝 → 설정 복원 → 바뀐 파일 목록 → 경고 파일 작성.
- **활성 플랫폼**: 로컬(에디터) 빌드는 활성 플랫폼이 다르면 빌드 전에 전환하고, 빌드 뒤 되돌리지 않습니다 (전환 비용이 큼). 배치 모드는 전환하지 않고 실패합니다.
- **스텝 On/Off**: Build Manager 창의 체크박스 상태는 빌드 프로필 에셋(`DisabledStepIds`)에 저장되어 로컬과 CI에서 동일하게 적용됩니다.
- **설정 복원**: 기본 스텝이 변경한 PlayerSettings / QualitySettings / EditorUserBuildSettings(버전, 심볼, 로그 설정 등)는 빌드 성공·실패와 관계없이 빌드 전 값으로 복원됩니다. 커스텀 스텝이 다른 설정을 바꾸면 직접 복원해야 합니다.
  - 복원 대상: `stripEngineCode` · `usePlayerLog` · `bundleVersion` · 스택트레이스(5종) · Scripting Define Symbols · Scripting Backend · Development · Deep Profiling · Autoconnect Profiler. Android는 versionCode · AAB · Android Build Type · Target Architectures, iOS는 buildNumber · 수동 Provisioning Profile 타입 · ID · 자동 서명 여부.
  - `QualitySettings.skinWeights`는 기본 스텝이 바꾸지 않습니다 (프로젝트 품질 설정을 그대로 씀).
  - 심볼 · Scripting Backend는 바뀐 경우에만 되돌립니다 (같은 값을 다시 써도 재컴파일될 수 있음). 저장은 PlayerSettings · QualitySettings 에셋만 합니다 (사용자의 저장하지 않은 다른 에셋은 저장하지 않음).
  - 빌드가 성공했는데 복원이 실패하면 빌드를 실패로 처리합니다. 빌드가 이미 실패했으면 원래 오류를 가리지 않도록 경고만 남깁니다.
- **전처리기 심볼**: 항목이 있는데 리스트가 비어 있으면 심볼을 모두 제거합니다. DefineContainer 또는 해당 빌드 타입/플랫폼 항목이 없거나 심볼 설정 스텝을 끄면 **LIVE는 실패**하고(커밋된 ProjectSettings의 개발용 심볼이 그대로 들어가는 것 방지), DEV · QA는 기존 심볼을 유지합니다 (항목이 없으면 경고).
  - DefineContainer는 프로필(또는 창)에 넣은 에셋 → `Assets/Editor/HBBuild/DefineContainer.asset`(있는데 읽지 못하면 실패) → 프로젝트에서 찾은 `ProjectDefine` 에셋 순으로 씁니다. 마지막 단계에서 여러 개가 나오면 **LIVE는 실패**, DEV · QA는 경로 순 첫 번째를 쓰고 경고합니다. `SceneDefine`도 같은 규칙입니다. 창은 기본 경로에 없고 여러 개면 자동으로 연결하지 않습니다 (빌드 때 위 규칙으로 처리).
  - HB_InGameConsole을 쓰면 LIVE 항목에서 `USE_HLOG`를 빼서 출시 빌드의 HLog를 끕니다 (켜진 채 릴리즈 빌드하면 InGameConsole이 경고).
- **iOS 서명**: 프로필 `CustomEntries`의 `ProvisionType`과 `ProvisionUUID`로 정합니다. `ProvisionType`은 대소문자 무시, 없으면 `Distribution`, 아래 값 밖이면 빌드 실패.
  - `Development` · `Distribution` · `Automatic`: Unity `ProvisioningProfileType`과 같은 뜻입니다 (`Automatic` = 프로필 종류를 Xcode 빌드 때 자동 판정). UUID가 있으면 자동 서명을 끄고 그 종류 · UUID로 수동 서명합니다 (빌드 뒤 원래 값으로 복원).
  - `AutoSign`(HB_Build 전용): 자동 서명을 켜고 UUID는 넣지 않습니다 (UUID가 있으면 경고). Team ID는 프로젝트 설정 값을 씁니다.
  - UUID가 없으면 프로젝트의 서명 설정을 그대로 씁니다 (`ProvisionType`만 넣었으면 경고).
- **LIVE + Development Build**: 라이브 환경 프로파일링 · 로그 확인용으로 막지 않습니다. 대신 산출물 이름에 `_DEVBUILD`가 붙고(예: `Game_v1.0.0_42_LIVE_DEVBUILD_a1b2c3d_Jenkins128.apk`), 빌드 시작 · 끝에 경고 블록이 찍히고, 로컬 빌드는 확인 창을 띄우고, Jenkins는 결과가 UNSTABLE이 됩니다.
- **경고 파일**: 빌드를 막지 않는 경고(LIVE + Development, 심볼 항목 없음, 등록되지 않은 비활성 ID 등)가 있으면 `BuildOutput/HB_BUILD_WARNINGS.txt`에 남깁니다. 생성한 Jenkinsfile은 이 파일이 있으면 내용을 출력하고 UNSTABLE로 표시합니다.
  - 출력 경로와 관계없이 항상 프로젝트의 `BuildOutput/`에 씁니다. 빌드를 시작할 때 이전 파일을 지우고, 빌드가 실패해도 그때까지의 경고를 씁니다. 로컬 빌드는 완료 창에 경고 수를 보입니다.
- **빌드가 바꾼 파일**: 빌드 전후 `git status`를 비교해 빌드 중 바뀐 파일(ProjectSettings 재저장 등)을 로그에 출력합니다. 자동으로 되돌리지 않으니 직접 확인하고 되돌리세요 (git이 PATH에 없으면 건너뜀).
  - 새로 바뀐 파일, 이미 바뀐 상태에서 다시 쓰인 파일(수정 시각), 빌드 중 원래대로 돌아간 파일을 모두 보입니다. `BuildOutput/` · 이번 출력 경로 · 빌드 로그 파일은 뺍니다.
  - 다시 쓰인 파일은 수정 시각으로 판단하므로 내용이 같게 재저장된 파일도 목록에 나옵니다 (내용 비교는 하지 않음).
  - 프로젝트가 저장소의 하위 폴더여도 저장소 루트 기준으로 봅니다. git 명령이 15초 안에 끝나지 않으면 건너뜁니다.
- **직접 빌드 경고**: Unity 기본 빌드(File > Build 등)처럼 HB_Build를 거치지 않은 플레이어 빌드는 콘솔에 경고합니다. HB_Build 스텝(심볼, 검증, 설정 복원)이 실행되지 않으므로 팀 빌드는 Build Manager로 하세요.
- **산출물 이름**: `{제품명}_v{버전}[_{버전코드}]_{빌드타입}[_DEVBUILD]_{커밋}[_Jenkins{빌드번호}]` — QA 리포트에 적힌 이름만으로 어느 커밋 · 어느 Jenkins 빌드인지 찾을 수 있게 합니다.
  - 예: Jenkins Android `MyGame_v1.2.0_45_QA_a1b2c3d_Jenkins128.apk`, 로컬 PC `MyGame_v1.2.0_DEV_a1b2c3d-dirty/MyGame_v1.2.0_DEV_a1b2c3d-dirty.exe`.
  - 버전 코드가 비면(PC) 그 칸을 뺍니다. 버전 코드도 이름에 들어가므로 파일 이름에 못 쓰는 문자 · `/` · `\` · `..`가 있으면 실패합니다.
  - 커밋: Unity 프로젝트 폴더의 짧은 해시(7자 이상). 프로젝트 폴더에 커밋하지 않은 변경 · 추적하지 않는 파일이 있으면 `-dirty`(해시만으로는 이 빌드를 다시 만들 수 없음, 막지는 않고 경고), `git status`가 실패하면 `-unknown`(경고 파일 → Jenkins UNSTABLE), git이 없거나 저장소가 아니면 `nogit`(이유는 로그에). 빌드 산출물 폴더 · 빌드 로그는 변경으로 보지 않습니다. 단 출력 경로가 프로젝트 루트 · `Assets` · `Packages` · `ProjectSettings`를 덮으면 제외하지 않고 경고합니다.
  - Jenkins: `BUILD_NUMBER`(숫자)가 있고 `JENKINS_HOME` · `JENKINS_URL` · `BUILD_TAG`(`jenkins-…`) 중 하나가 있으면 Jenkins 빌드로 보고 `_Jenkins{BUILD_NUMBER}`를 붙입니다. 창에서 저장한 `.bat` · `.ps1`로 돌린 배치 빌드나, 개발자 PC 환경 변수에 `JENKINS_URL`만 있는 경우에는 붙지 않습니다.
  - 빌드 시작 로그에 `Revision: <커밋> | Jenkins #<번호>`가 찍힙니다.
  - 이 표시는 **추적용이지 진본 보장이 아닙니다** (환경 변수 · 출력 경로로 바꿀 수 있음). 출시 판단은 Jenkins 빌드 기록과 저장소로 하세요.
- **빌드 전 git 작업 폴더 정리** (Build Manager 창, 로컬 빌드만): 켜면 빌드 전에 Unity 프로젝트 폴더를 현재 커밋 상태로 되돌려 `-dirty` 없는 빌드를 만듭니다.
  - 지우는 것: 커밋하지 않은 수정 · 스테이징(`git restore --source=HEAD --staged --worktree` — 커밋 전에 스테이징만 한 새 파일은 **삭제**), 추적하지 않는 파일(`git clean -fd`, `-x` 없음). 남는 것: `Library/` 등 `.gitignore` 대상, 빌드 산출물 폴더(`BuildOutput/` · 출력 경로), 프로젝트 밖 파일(저장소가 더 넓어도), 프로젝트 안 서브모듈의 작업 트리(되돌리지 않음 — 서브모듈 변경은 직접 정리). **되돌릴 수 없습니다.**
  - 확인 세 번: 옵션을 켜면 창에 경고가 뜨고, 그 아래 확인 체크박스를 켜야 하며, 정리 직전에 지울 목록(`[삭제]` · `[수정]` · `[복원]`, 앞 20개, Console에는 500개까지)을 대화상자로 보여 줍니다. 확인한 뒤 목록을 다시 구해 그 사이 바뀌었으면 정리하지 않습니다. 정리를 한 번 하면 두 체크박스는 꺼집니다. 프로필에 저장되지 않습니다.
  - 저장하지 않은 씬 · 프리팹 편집이 있거나 git을 쓸 수 없으면 정리 · 빌드를 하지 않습니다. 저장하지 않은 다른 에셋(ScriptableObject · 머티리얼 등)은 먼저 저장해서 정리 목록에 나오게 합니다.
  - 빌드 산출물을 지킬 수 없으면 정리하지 않습니다: 출력 경로가 프로젝트 루트 · `Assets` · `Packages` · `ProjectSettings`를 덮을 때, 출력 폴더 이름에 git 패턴 글자(`[ ] * ? ! # "` 등)가 있을 때, 산출물이 정리 목록에 섞였을 때. 출력 폴더를 `.gitignore`에 넣어 두면 가장 안전합니다.
  - 스크립트 · asmdef · DLL · `package.json` · `Packages/manifest.json` · `ProjectSettings.asset`이 바뀌면(새 미추적 폴더 안의 파일 포함) 컴파일이 필요해 **정리만 하고 빌드는 하지 않습니다.** 컴파일이 끝나면 빌드를 다시 누르세요.
  - 프로필 "저장 후 빌드"를 골랐는데 그 프로필 파일이 커밋 전이면 정리 목록에 나옵니다 (정리하면 저장한 값이 되돌아감). 선택한 프로필이 커밋되지 않은 파일이라 정리로 지워지면 빌드하지 않습니다. 정리 뒤 값으로 새로 LIVE + Development가 되면 다시 묻습니다.
  - git 명령은 영어 출력으로 실행하고(번역된 메시지를 잘못 읽지 않게), 에디터를 띄운 셸의 `GIT_DIR` · `GIT_WORK_TREE` 등은 무시합니다. macOS에서는 Hub로 띄운 에디터의 PATH가 짧으므로 `/opt/homebrew/bin` · `/usr/local/bin`을 PATH 뒤에 붙여 실행합니다 (Homebrew `git-lfs`를 찾도록, git 자체는 원래 고르던 것). 이 처리는 산출물 이름의 커밋 확인에도 적용됩니다. 정리 명령은 오래 걸려도 중간에 끊지 않습니다 (10분이 넘으면 끊지 않고 실패만 알림 — 끝난 뒤 `git status`와 `.git/index.lock`을 확인).
  - CLI · Jenkins 배치 빌드에는 없습니다 (Jenkins 로컬 폴더 모드는 자체 갱신 단계가 있음).
- **출력 경로**: 기본값은 프로젝트 기준 `BuildOutput/<Target>`입니다. Windows 빌드는 `<출력경로>/<이름>/<이름>.exe`처럼 버전별 하위 폴더에 생성되고, ZIP은 `<출력경로>/<이름>.zip`으로 생성됩니다. 같은 이름의 폴더나 ZIP이 이미 있으면 덮어쓰지 않고 `<이름> (2)`, `<이름> (3)` 폴더(와 ZIP)로 분리합니다. ZIP에는 `*_DoNotShip`, `*_ButDontShipItWithYourGame` 폴더가 포함되지 않습니다.
  - 창의 폴더 선택으로 프로젝트 안 폴더를 고르면 상대 경로로 저장합니다. 절대 경로는 다른 PC · Jenkins에서도 그대로 쓰이므로 창이 경고합니다.
- **로컬 빌드**: 프로필 빌드는 저장된 프로필 값으로 실행됩니다. 창에 저장하지 않은 변경이 있으면 빌드 전에 저장 여부를 묻고, 자동 저장하지 않습니다.
- **프로필 복제**: 창에 저장하지 않은 변경이 있으면 복사본에 적용할지, 버리고 복제할지 묻습니다. 원본 프로필은 바꾸지 않습니다.
  - 선택지는 "저장 후 빌드" · "저장된 값으로 빌드" · "취소"입니다. 프로필을 고르지 않으면 창의 현재 값으로 빌드합니다.
  - 빌드는 창 그리기(OnGUI)가 끝난 뒤 실행하고, 끝나면 성공 · 실패 창을 띄웁니다.
- **Jenkinsfile**: 생성된 파이프라인은 빌드 전에 `BuildOutput/` 폴더만 지웁니다 (`Library/` 캐시는 유지). 소스 정리는 SCM 모드면 Jenkins job의 checkout 설정을, 로컬 폴더 모드면 생성된 `Update Source (git)` 단계를 따릅니다 (아래).
  - 생성하면 클립보드에 복사하고, 프로젝트 루트에 `Jenkinsfile`로 저장할지 묻습니다.
  - 단계: `Checkout SCM`(로컬 폴더 모드는 `Update Source (git)`) → `Clean Build Output` → `Unity Build Execution` → `Archive Artifacts`(`BuildOutput/**`). 끝나면 항상 경고 파일을 확인하고 `build.log`를 보관합니다.
  - 명령은 Unity 에디터를 직접 호출합니다. 경로는 에이전트 환경변수 `UNITY_PATH`, 없으면 `ProjectSettings/ProjectVersion.txt`의 버전으로 `${UNITY_EDITORS_ROOT}/<버전>/Editor/Unity.exe`(기본 `C:/Program Files/Unity/Hub/Editor`, macOS는 `/Applications/Unity/Hub/Editor/<버전>/Unity.app/...`)를 씁니다. Jenkinsfile에 경로를 박지 않으므로 Unity를 올려도 다시 생성할 필요가 없습니다. Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없습니다.
  - 자유 입력 값은 명령줄에 들어가므로 검사합니다: `BUILD_VERSION` · `VERSION_CODE`는 영문 · 숫자 · `.` · `_` · `-`만(`-`로 시작 불가), 프로필 경로는 글자 · 숫자 · 공백 · `.` · `_` · `/` · `-`로 된 `.asset` 경로만 통과합니다.
  - 프로필 모드는 생성 시점의 "프로필 경로 → 플랫폼" 표로 `-buildTarget`을 넘깁니다. 프로필의 플랫폼을 바꾸면 Jenkinsfile을 다시 생성하세요 (그대로 두면 배치 빌드가 플랫폼 불일치로 실패).
  - 목록(`BUILD_PROFILE`)에 없는 프로필(빌드 PC 전용 프로필 등)은 `BUILD_PROFILE_PATH`에 경로를, `BUILD_TARGET`에 플랫폼을 직접 넣습니다. 플랫폼이 틀리면 배치 빌드가 불일치로 실패합니다.
  - 프로필 모드의 Jenkins 빌드는 프로필의 출력 경로 대신 `BuildOutput/<플랫폼>`에 만듭니다 (정리 · 보관 대상과 일치).
  - Linux 에이전트는 `UNITY_PATH`를 반드시 설정하세요 (기본 경로 찾기는 Windows · macOS Hub 경로만 지원).
  - **전체 풀기 모드**: `BUILD_TARGET` · `BUILD_TYPE` · `BUILD_VERSION` · `VERSION_CODE`와 `DEV_BUILD` · `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE`(booleanParam)가 Jenkins 파라미터입니다. 기본값은 생성한 사람의 창과 무관한 안전값(첫 선택지, 체크 모두 끔, 버전 · 버전 코드 빈 값)이고, Jenkins에서 바꾼 값이 우선합니다. `BUILD_VERSION`을 비우면 PlayerSettings의 버전을 씁니다. 씬 목록과 비활성 스텝은 생성 시 창 값으로 고정됩니다.
  - **프로필+오버라이드 모드**: 기본은 고른 프로필(`BUILD_PROFILE`)의 값입니다. Jenkins 파라미터 `BUILD_TYPE` · `DEV_BUILD`(`(profile)`이 아닌 값), `BUILD_VERSION` · `VERSION_CODE`(비어 있지 않은 값)를 넣은 것만 인자로 넘겨 프로필 값을 덮어씁니다.
  - **로컬 폴더 모드** (`Jenkins 체크아웃` = `LocalFolder`): 빌드 PC에 프로젝트 작업 폴더를 두고 Jenkins가 배치 git 명령으로 갱신한 뒤 그 폴더에서 빌드합니다.
    - job: 생성한 내용을 **Pipeline script**(SCM 없음)에 붙여 넣습니다. **job마다 폴더 하나**를 쓰세요 (예: 플랫폼별 job · 폴더). 같은 job의 동시 실행은 `disableConcurrentBuilds()`로 막지만, 서로 다른 job이 같은 폴더를 쓰는 것은 막지 못합니다.
    - 창 입력: **빌드 PC 프로젝트 폴더**(필수, Jenkinsfile에 상수로 들어감), 기본 브랜치, **에이전트 라벨**(그 폴더가 있는 노드. 비우면 `agent any` — 노드가 하나일 때만 쓰세요).
      - 폴더는 빌드 파라미터로 열지 않습니다. 빌드 권한만으로 같은 PC의 다른 git 폴더를 지정해 지우는 일을 막기 위해서입니다. 폴더를 바꾸려면 Jenkinsfile을 다시 생성하세요.
      - 끝의 `\` · `/`는 떼고, 네트워크 경로(UNC, `\\` · `//`로 시작) · `..` 구간 · 허용 밖 문자(글자 · 숫자 · 공백 · `. _ : \ / ( ) -` 외)가 있으면 생성하지 않습니다. 한글 경로는 bat 코드 페이지에 따라 깨질 수 있어 확인 전까지 **ASCII 경로를 권장**합니다.
      - Unity 프로젝트가 **저장소 루트**에 있어야 합니다 (`<폴더>/.git` 확인). 저장소 하위 폴더 구조는 지원하지 않습니다.
    - 파라미터: `BRANCH`(origin 기준 브랜치, 비우면 폴더의 현재 브랜치. 영문 · 숫자 · `. _ / -`만, `-`로 시작 · `..` 불가), `GIT_REF`(**태그**, 또는 원격 브랜치 · 태그에 들어 있는 **7~40자리 커밋 해시**. 넣으면 `BRANCH`보다 우선. 브랜치 이름 · `origin/x` · `HEAD` 등은 실패 — 로컬의 오래된 상태를 빌드하지 않게).
    - 첫 준비는 사람이 합니다: `git clone` → `git lfs install` → Unity로 한 번 열어 임포트. 폴더가 없거나 git 작업 폴더가 아니면 clone하지 않고 실패합니다.
      - Jenkins 서비스 계정이 폴더를 만든 계정과 다르면 git이 "dubious ownership"으로 거부합니다. 서비스 계정에서 `git config --global --add safe.directory <폴더>`를 해 두세요. LFS 필터는 단계가 `git lfs install --local`로 저장소에 직접 겁니다.
    - `Update Source (git)` 단계: `git lfs install --local` → 오래된(10분 넘은) `.git/index.lock` 삭제 → `git fetch origin --prune --prune-tags --tags --force`(원격에서 지운 태그는 로컬에서도 지움) → 버려질 것을 로그에 출력(`git status --porcelain`, HEAD와 로컬 브랜치 중 어느 원격에도 없는 커밋, `git clean -nd`) → `git checkout -f -B <BRANCH> origin/<BRANCH>`(또는 `--detach <커밋>`) → `git clean -fd` → (`.gitmodules`가 있으면 서브모듈 sync · 갱신 · 정리 · LFS) → `git lfs pull` → `git status`가 비어 있지 않으면 실패. 빌드한 커밋은 Jenkins 빌드 설명과 `BuildOutput/BUILD_COMMIT.txt`에 남습니다.
    - **빌드 PC 폴더에서 직접 고친 내용 · 로컬 커밋 · 추적되지 않는 파일은 다음 빌드에서 사라집니다.** 고칠 것은 커밋하세요. 빌드 PC 전용 파일(라이브 프로필 등)은 `.gitignore`에 넣으면 남습니다 — **`.asset`과 짝인 `.meta`(폴더째 무시하면 `폴더.meta`까지) 함께** 무시하세요. `.meta`를 함께 무시하지 않으면 그 `.meta`가 매 빌드 지워져 GUID가 바뀌고, 그 에셋을 참조하던 것이 끊깁니다.
    - `git clean -fd`는 `-x` 없이 실행하므로 `Library/` 등 무시된 파일은 남습니다 (재임포트 없음). `-fdx`는 `Library/`까지 지웁니다.
    - 갱신 뒤 `git status`가 비어 있지 않으면(줄바꿈 변환 · LFS 문제 · 브랜치를 바꿔 사라진 서브모듈 폴더 등) 실패합니다. 검사를 느슨하게 하지 말고 저장소를 정리하세요 (`.gitattributes` · 재정규화, 남은 서브모듈 폴더는 직접 삭제).
    - 에이전트 요구: `git` · `git-lfs`, Jenkins 서비스 계정에서 쓸 수 있는 git 인증. 모든 단계가 그 폴더(`dir(...)`) 안에서 실행되고, Jenkins가 폴더 옆에 `<폴더>@tmp`(배치 실행용 임시 폴더)를 만듭니다.
    - **macOS 빌드 PC** (확인 전, 추정 포함): 노드가 둘 이상(예: Windows + Mac mini)이면 **에이전트 라벨을 꼭** 넣으세요. launchd로 띄운 Jenkins는 PATH가 짧아 Homebrew의 `git-lfs`(`/opt/homebrew/bin`)를 못 찾으므로 노드 환경 변수 PATH에 추가하세요 (없으면 첫 단계 `git lfs version`에서 실패). 프로젝트 `.gitignore`에 `.DS_Store`를 넣고, 한글 파일 이름이 있으면 `git config core.precomposeunicode true`를 확인하세요 (아니면 갱신 뒤 상태 검사에서 실패할 수 있음).
    - **권한 주의**: 이 job의 빌드 권한이 있으면 origin의 아무 브랜치 · 커밋을 빌드할 수 있고, Unity 배치 빌드는 그 코드의 에디터 스크립트를 빌드 PC의 서비스 계정으로 실행합니다. 빌드 권한은 그 브랜치에 push할 수 있는 사람과 같은 범위로 두세요.

  | 파라미터 | 순수 프로필 | 전체 풀기 | 프로필+오버라이드 |
  |---|---|---|---|
  | `BUILD_PROFILE` | 선택 (생성 PC의 프로필, 고른 프로필이 첫 항목) | - | 선택 |
  | `BUILD_PROFILE_PATH` | 문자열 (비우면 `BUILD_PROFILE`) | - | 문자열 |
  | `BUILD_TARGET` | `(profile)` · `StandaloneWindows64` · `Android` · `iOS` | `StandaloneWindows64` · `Android` · `iOS` | `(profile)` · … |
  | `BUILD_TYPE` | - | `DEV` · `QA` · `LIVE` | `(profile)` · `DEV` · `QA` · `LIVE` |
  | `DEV_BUILD` | - | booleanParam (false) | `(profile)` · `true` · `false` |
  | `BUILD_VERSION` · `VERSION_CODE` | - | 문자열 (빈 값) | 문자열 (비우면 프로필 값) |
  | `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE` | - | booleanParam (false) | - |

- **CLI 명령어 생성기 (로컬)**: 현재 에디터 실행 파일과 이 프로젝트 경로로 명령을 만들고, 로그는 `build.log`에 씁니다. 프로필+오버라이드 모드의 로컬 명령은 창의 버전 · 버전 코드 · 빌드 타입 · Development 값을 덮어쓰기 인자로 넣습니다.
- **선택 설정 에셋 생성**: `DefineContainer.asset` · `SceneDefine.asset`이 이미 있으면 새로 만들지 않고 기존 에셋을 연결합니다. 그 경로에 불러올 수 없는 파일이 있으면 덮어쓰지 않고 오류 창을 띄웁니다.
- Build Settings에 활성 씬이 없고 프로필에도 씬이 없으면 빌드를 실패시킵니다.

## iOS Xcode 설정
Unity가 만든 Xcode 프로젝트에 손으로 하던 설정(Info.plist 키, Capabilities · entitlements, Build Settings, 프레임워크 링크, 파일 복사)을 에셋으로 옮깁니다. iOS 빌드마다 `BuildStep_XcodeSettings`가 적용하므로 Xcode에서 체크박스를 다시 누를 일이 없습니다.

- **에셋**
  - `HBXcodeSettings` (공용, 기본 `Assets/Editor/HBBuild/XcodeSettings.asset`): 인스펙터 탭이 Xcode 이름 그대로입니다 — `Signing & Capabilities` · `Info` · `Build Settings` · `Build Phases` · `Files`, 그리고 `묶음 · 결정` · `결과 미리보기`.
  - `HBXcodeFragment` (SDK 묶음): 같은 다섯 탭을 갖습니다. 공용 에셋의 `Fragments` 목록에 넣고 체크박스로 켜고 끕니다. SDK를 빼면 묶음만 끕니다.
  - 모든 행에 `Enabled`와 빌드 타입 필터(`DEV` · `QA` · `LIVE`, 여럿 선택)가 있습니다. 빌드 타입을 하나도 고르지 않은 행은 적용되지 않고 경고합니다. 인스펙터 `+`로 만든 새 행은 Unity가 0으로 채우므로(확인함) 처음 그릴 때 `Enabled` · 빌드 타입 `All` 등 기본값을 넣습니다.
- **값의 주인 (겹침 방지)**: 값마다 주인은 하나입니다. 아래 키를 Xcode 설정에 넣으면 빌드가 실패하고 주인을 알려 줍니다 (`XcodeOwnership`).

  | 주인 | Info.plist 키 | Build Settings 키 |
  |---|---|---|
  | HB_Build 프로필 | `CFBundleShortVersionString` · `CFBundleVersion` | `CODE_SIGN_STYLE` · `CODE_SIGN_IDENTITY` · `PROVISIONING_PROFILE*` · `OTHER_CODE_SIGN_FLAGS` · `MARKETING_VERSION` · `CURRENT_PROJECT_VERSION` |
  | PlayerSettings (iOS) | `CFBundleIdentifier` · `CFBundleDisplayName` · `CFBundleName` · `CFBundleURLTypes` · `MinimumOSVersion` · `NSCameraUsageDescription` · `NSLocationWhenInUseUsageDescription` · `NSMicrophoneUsageDescription` · `UIRequiresFullScreen` · `UIStatusBarHidden` · `UISupportedInterfaceOrientations(~ipad)` · `UILaunchStoryboardName` · `UIRequiresPersistentWiFi` · `UIApplicationExitsOnSuspend` · `CADisableMinimumFrameDurationOnPhone` | `DEVELOPMENT_TEAM` · `PRODUCT_BUNDLE_IDENTIFIER` · `IPHONEOS_DEPLOYMENT_TARGET` |
  | Xcode 설정 자체 · Info 탭 | — | `INFOPLIST_FILE` · `CODE_SIGN_ENTITLEMENTS` · `GENERATE_INFOPLIST_FILE` · `INFOPLIST_*`(접두사 — `INFOPLIST_KEY_*` 등은 Info 값을 우회함) |
  | 서명 (entitlements) | `application-identifier` · `com.apple.developer.team-identifier` · `get-task-allow` (Entitlements 탭) | — |

  - `UIBackgroundModes`는 PlayerSettings(Background Modes)도 쓰지만 목록이라 합집합으로 합칩니다 (SDK 묶음마다 추가 가능).
  - `KEY[sdk=...]` 같은 조건부 키도 앞부분으로 판정합니다. `Signing & Capabilities` 탭 위쪽에 주인이 다른 값들을 읽기 전용으로 보여 줍니다 (`Player Settings 열기` 버튼).
- **탭별 내용**
  - `Signing & Capabilities`: Push Notifications(`aps-environment` development/production) · Sign in with Apple · Associated Domains · Background Modes · In-App Purchase · Game Center · Keychain Sharing · App Groups, 그리고 entitlements 직접 행. Capability는 entitlements · Info.plist 값으로 바꿔 다른 행과 함께 충돌 검사합니다 (Push → `aps-environment`, Background Modes → `UIBackgroundModes` 등).
  - `Info`: Key / Type(String · Bool · Integer · Real · StringArray · Raw) / Value. Raw는 plist XML 값 하나입니다(예: `SKAdNetworkItems`). 키 오른쪽 `▾`에 자주 쓰는 키가 있습니다.
  - `Build Settings`: 타깃(`App` · `UnityFramework` · `GameAssembly` · `All`) · 구성(비우면 모든 구성) · 키 · `Set`/`Add` · 값. `All`은 App + UnityFramework + GameAssembly(있을 때)에 각각 씁니다(Tests 제외). 타깃은 이름이 아니라 Unity API로 찾습니다 (Unity 7 Swift 형식은 앱 타깃 이름이 제품 이름).
  - `Build Phases`: 시스템 프레임워크 · 라이브러리 링크(`AdSupport.framework` · `libz.tbd`, Optional = weak). 이미 **다른** Required/Optional로 링크돼 있으면 실패합니다 (Unity 7은 `AdSupport.framework`를 Required로 링크해 둠). 바꾸려면 Framework 결정 행에 `OverwriteExisting` — 다시 링크합니다. `.tbd`는 `usr/lib`에서 링크하고 Optional로 지정할 수 없습니다. `Assets/Plugins/iOS`의 프레임워크 파일과 임베드는 Unity Plugin Inspector가 관리합니다.
  - `Files`: 원본(Unity 프로젝트 기준 상대 경로) → 대상(Xcode 프로젝트 폴더 기준 상대 경로, `..` · 절대 경로 불가), 파일 / 폴더 참조, 대상 타깃(App · UnityFramework), Copy Bundle Resources 포함 여부. 다른 후처리기가 참조만 만들어 둔 파일도 Resources에 넣습니다.
    - 원본은 **Unity 프로젝트 안**이어야 하고 심볼릭 링크 · 정션이면 실패합니다. 비밀 · 서명 파일(`.p8` `.p12` `.pfx` `.pem` `.key` `.cer` `.mobileprovision` `.provisionprofile` `.keystore` `.jks` `.env`)은 파일이든 폴더 안이든 실패합니다 — 원본 폴더 밖으로 옮기세요.
    - 폴더 복사 때 `.meta`와 숨김 파일 · 폴더(`.`으로 시작, `.DS_Store` · `.git` 등)는 빠지고, 복사한 파일 목록을 로그에 남깁니다.
    - 대상이 Info.plist · entitlements 파일 · `*.xcodeproj`이거나 그 위 · 안이면 실패합니다 (Info · Entitlements 탭을 쓰세요). Windows 장치 이름(`CON` · `NUL` 등), 끝이 점 · 공백인 조각, 심볼릭 링크 대상도 실패합니다.
    - 대상에 같은 내용이 이미 있으면 다시 복사하지 않습니다.
    - 예: 빌드 타입별 `GoogleService-Info.plist`(같은 대상 경로에 행 두 개, 빌드 타입 필터만 다르게).
- **에셋 참조**: 프로필 · 창의 슬롯이 깨진 참조(Missing)이거나 `CustomEntries`가 `XcodeSettings` 키를 다른 값으로 쓰면 실패합니다. 켜진 `Fragments` 칸이 비었거나 Missing이면 **LIVE는 실패**, DEV · QA는 경고합니다 (그 SDK 설정이 통째로 빠지므로).
- **LIVE 경고** (실패시키지 않고 경고 파일 · Jenkins UNSTABLE): `aps-environment`가 development, `NSAppTransportSecurity`의 `NSAllowsArbitraryLoads` 또는 도메인별 `NSExceptionAllowsInsecureHTTPLoads`가 true.
- **SDK를 붙일 때**: SDK마다 `HBXcodeFragment`를 하나 만들고(`Note`에 SDK · 버전), SDK 문서가 요구하는 키 · 권한 · Capability · 링크 옵션을 넣은 뒤 공용 에셋 `Fragments`에 추가합니다. Info · Entitlements 탭의 `붙여넣기` 버튼에 SDK 문서의 plist 조각(`<key>…</key><string>…</string>` 나열, `<dict>`, 전체 plist)을 넣으면 행으로 바뀝니다.
  - 예: Firebase Cloud Messaging 묶음 = Push Notifications(`aps-environment`는 서명 종류에 맞춰 직접 고름) + Background Modes `remote-notification` + Info `FirebaseAppDelegateProxyEnabled`.
- **합치기 · 충돌 (항상 빌드 실패)**
  - 단일 값(String · Bool · 숫자, Build Settings `Set`): 값이 같으면 하나로, 다르면 실패하고 어느 행끼리 부딪혔는지 보여 줍니다.
  - 목록 값(StringArray, `UIBackgroundModes` 등, Build Settings `Add`): 합집합. `SKAdNetworkItems`는 `SKAdNetworkIdentifier` 기준(대소문자 무시) 합집합. 그 밖의 Raw 복합 값은 완전히 같아야 합니다.
  - 같은 키의 타입이 다름, 같은 칸에 `Set`과 `Add`가 섞임, 같은 프레임워크의 Required/Optional이 다름, 같은 대상 경로에 내용 · 방식 · 타깃이 다른 파일, 한 대상이 다른 대상 폴더 안에 있음 → 실패.
  - 충돌 판정 단위: Build Settings는 (타깃, 구성, 키), 나머지는 키 · 대상 경로. `All`과 특정 타깃 행은 펼친 뒤 비교합니다 (특정 타깃이 자동으로 이기지 않음).
  - **Xcode 프로젝트에 이미 있는 값과 달라도 실패**합니다 (Unity 기본값 · 다른 SDK 후처리기 구분 없이). 목록 값은 기존 값과 합칩니다. 같은 값이면 통과합니다.
  - **해결은 `묶음 · 결정` 탭의 결정 행으로만**: 종류 · (타깃 · 구성) · 키 · `ChosenSource`(이길 에셋) · `OverwriteExisting`(기존 값 덮어쓰기) · 메모(필수). 결정할 충돌이 없어진 결정 행은 경고합니다.
  - Build Settings에서 `OTHER_LDFLAGS` 같은 목록 설정을 `$(inherited)` 없이 `Set`하면 경고합니다 (CocoaPods xcconfig 값이 가려짐 — `Add`를 쓰세요).
- **적용 순서**: 빌드 전에 계획을 세워 위 검사(기존 값 비교 제외)를 하고, 오류가 있으면 BuildPlayer 전에 멈춥니다. 빌드 뒤에는 생성된 프로젝트의 실제 구성 · 타깃으로 다시 계획하고, 기존 값 비교까지 **모든 검사를 통과한 뒤에만** 파일을 씁니다. SDK들의 Unity 빌드 후처리기는 BuildPlayer 안에서 끝나므로 이 스텝이 마지막입니다.
  - Info.plist · entitlements 경로 빌드 설정이 절대 경로 · `..` · `$(변수)`면 실패합니다 (다른 후처리기가 바꾼 경우).
  - 실수 값은 Xcode plist API가 float로 쓰므로 float 정밀도로 비교하고, float로 나타낼 수 없는 값은 실패합니다.
  - 빌드 전에 점검한 내용을 그대로 적용합니다 (BuildPlayer가 쓰지 않는 에셋을 정리하면 넘겨받은 에셋 참조가 끊기는 것을 확인해, 점검 시점에 내용을 복사해 둡니다).
  - Info.plist는 앱 타깃의 `INFOPLIST_FILE`(ObjectiveC 형식 `Info.plist`, Unity 7 Swift 형식 `MainApp/Info.plist`), entitlements는 `CODE_SIGN_ENTITLEMENTS`가 있으면 그 파일, 없으면 Info.plist 옆에 `HBXcode.entitlements`를 만들고 앱 타깃의 모든 구성에 연결합니다.
  - HB_Build의 iOS 빌드는 항상 Replace(산출물 폴더를 새로 만듦)이므로, 새로 생성된 프로젝트에 한 번 적용하는 것을 기준으로 합니다.
- **iOS Build Support 모듈이 없는 PC**: Xcode 프로젝트를 고치는 코드(`XcodeSettingsApplier`)만 `#if UNITY_IOS`로 분리돼 있어 패키지는 그대로 컴파일됩니다. 에셋 편집 · 결과 미리보기는 모듈 없이도 됩니다. 적용할 설정이 있는데 적용 코드가 컴파일돼 있지 않으면 BuildPlayer 전에 실패합니다 — 모듈 설치를 확인하거나, 로컬 빌드가 방금 iOS로 플랫폼을 전환했다면 컴파일이 끝난 뒤 다시 빌드하세요.
- **지역화**
  - 앱 이름 · 권한 문구: `*.lproj/InfoPlist.strings`를 `Files`에 **폴더 참조**로 넣습니다(대상 예: `ko.lproj`, App 타깃, Resources). `FillLocalizationsFromLproj`(기본 켜짐)이면 `*.lproj` 이름으로 `CFBundleLocalizations`를 채웁니다. Unity 템플릿은 `CFBundleDevelopmentRegion`(en) · `CFBundleAllowMixedLocalizations`를 이미 넣습니다.
  - **Mac에서 아직 확인하지 않은 실험 기능입니다** (Windows에서 생성 파일 · pbxproj 내용까지만 확인). `PBXProject.AddLocaleVariantFile`은 쓰지 않습니다 — ObjectiveC 형식에서 Tests 타깃의 `InfoPlist.strings` 그룹에 섞이고, Swift 형식에서 빌드 단계에 들어가지 않으며, 두 번 부르면 항목이 중복됩니다 (7000.0.0a8 실험).
  - 홈 화면 아이콘의 언어별 지역화는 iOS가 지원하지 않습니다. 앱 안에서 바꾸는 alternate icons는 이 도구 범위 밖입니다.
- **확인 범위**: Windows에서 Unity 6000.3.23f1 · 7000.0.0a8(ObjectiveC · Swift 형식)로 실제 iOS 빌드 → 적용 → 생성 파일 · pbxproj · plist 내용까지 확인했습니다. Xcode로 열기 · 서명 · 아카이브 · 기기 동작은 Mac에서 확인 전입니다.
  - `PBXProject.AddCapability`는 pbxproj의 `SystemCapabilities`에 흔적을 남기지 않았습니다. Capability의 실제 효과는 entitlements · Info.plist · `CODE_SIGN_ENTITLEMENTS`로 들어갑니다 (Xcode가 이것으로 Capability를 인식하는지는 Mac 미확인).
- **기본 항목 메모**: 암호화를 쓰지 않거나 면제 암호화만 쓰면 `ITSAppUsesNonExemptEncryption = false`(업로드마다 수출 규정 질문 방지, Unity 매뉴얼), ATT를 쓰면 `NSUserTrackingUsageDescription`. 앱 자체의 개인정보 매니페스트(`PrivacyInfo.xcprivacy`)는 이 도구가 아니라 `Assets/Plugins`에 두면 Unity가 합칩니다. 2026-04-28부터 App Store 업로드는 Xcode 26 / iOS 26 SDK 이상이 필요합니다.

## 운영 가이드: 라이브 프로필
- 프로필 에셋은 로컬 빌드와 Jenkins가 같이 쓰므로, 테스트하려고 바꾼 값(검증 스텝 끄기, Development Build 등)이 커밋되면 라이브 빌드에도 들어갑니다.
- 라이브 프로필은 빌드 PC 전용 폴더(예: `Assets/Editor/HBBuild/Profiles/BuildMachine/`)에 두고 그 폴더를 프로젝트 `.gitignore`에 등록하세요. Jenkins 워크스페이스는 `BuildOutput/`만 지우므로 빌드 PC에 둔 프로필이 유지됩니다 (Jenkins job의 checkout에서 `git clean -fdx`처럼 무시 파일까지 지우는 옵션을 쓰면 함께 지워지니 주의). Jenkins에서는 `BUILD_PROFILE_PATH`에 이 경로를, `BUILD_TARGET`에 플랫폼을 넣어 빌드합니다 (개발자 PC에서 만든 Jenkinsfile 목록에는 이 프로필이 없음).

## 에디터 메뉴 경로
- `Tools > HB > Custom Build Manager`
- `Assets > Create > HB_Build > Build Profile` · `DefineContainer` · `SceneDefine` · `Xcode Settings` · `Xcode Fragment (SDK)` (Xcode Fragment 밖은 창의 버튼으로도 만들 수 있음)

| 창 영역 | 기능 |
|---|---|
| 빌드 프로필 | `+ 새 프로필`(창의 현재 값으로 `BuildProfile_<플랫폼>_<타입>` 생성) · `프로필 저장` · `복제`, 오브젝트 필드 · 목록으로 선택 |
| 타겟 플랫폼 & 환경 | Target Platform(`PC` · `Android` · `iOS`) · Build Type |
| 선택적 설정 에셋 | Scene Define · Define Container · Xcode Settings (iOS) 슬롯, 비어 있으면 `+ 에셋 생성` |
| 빌드 파이프라인 실행 순서 | 사전 스텝 → BuildPlayer → 사후 스텝 목록. 체크박스로 끄고 켜기, 필터(조건 만족만 · 전체), 새로고침, 등록되지 않은 비활성 ID 제거 |
| 코드 미리보기 | 스텝 이름을 누르면 C# 소스를 보입니다 (이 클래스만 · 전체 파일, IDE에서 열기, 에셋 위치 Ping) |
| 버전 정보 | App Version · Version Code, iOS면 Provision UUID |
| 빌드 세부 옵션 | Development Build(켜면 Deep Profiling), Clean Build Cache, Android면 AAB, PC면 ZIP |
| 빌드 출력 경로 | 직접 입력 · 폴더 선택 · 열기. 비우면 기본 경로 표시 |
| 빌드 씬 목록 | Build Settings에서 불러오기 · 현재 씬 추가 · 전체 비우기 · 하나씩 제거 |
| Jenkins & Unity CLI 명령어 생성기 | 모드(순수 프로필 · 전체 풀기 · 프로필+오버라이드), 미리보기, 복사, `.bat` · `.ps1` 저장 |
| 하단 버튼 | `Local Player Build 실행` · `Generate Jenkinsfile` |
