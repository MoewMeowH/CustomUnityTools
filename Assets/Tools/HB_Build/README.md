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
  | `ProvisionUUID` | iOS 전용. 값이 있으면 수동 Provisioning Profile(Distribution)로 설정 |
  | `OutputPath` | 비우면 `BuildOutput/<Target>`. 프로젝트 기준 상대 경로 권장 |
  | `Scenes` | 비우면(공백 항목만 있어도) Build Settings의 활성 씬 |
  | `SceneDefineAsset` · `ProjectDefineAsset` | 선택 설정 에셋. 비우면 기본 위치 → 프로젝트 검색 순으로 찾습니다 |
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

- **`TypeCache` 기반 빌드 스텝 파이프라인 (`BuildStepRegistry`)**:
  - 프로젝트 내 `IPreBuildStep`, `IPostBuildStep` 구현체를 자동 감지하여 `ExecutionOrder` 순으로 실행합니다.
  - 상속 관계에 있는 빌드 스텝이 있을 경우 파생 클래스가 부모 스텝을 자동으로 오버라이드하도록 필터링합니다.
  - `IsActive(context)`가 false이거나 비활성 목록에 있는 스텝은 실행하지 않습니다. 스텝이 예외를 던지면 빌드가 실패합니다.
  - 인스턴스를 만들지 못한 스텝(공개 기본 생성자 없음, 생성자 예외)이 있으면 빌드 시작 때 실패합니다 (빠진 스텝의 검증 · 설정 없이 빌드되는 것 방지).
- **기본 빌드 스텝**: ID는 모두 `HB.CustomBuild.Editor.` 뒤에 아래 이름이 붙습니다.

  | 스텝 | Order | 실행 조건 | 하는 일 |
  |---|---|---|---|
  | `PreBuildStep_FilterScenes` | 5 | 항상 (SceneDefine 없으면 건너뜀) | 빌드 타입의 `IgnoreSceneList` 씬을 목록에서 뺍니다 |
  | `PreBuildStep_PlayerSettings` | 10 | 항상 | `stripEngineCode` 끔, `QualitySettings.skinWeights` = FourBones, Player Log = Development Build 여부. 스택트레이스: Error · Exception은 ScriptOnly, Log · Warning · Assert는 Development 또는 DEV면 ScriptOnly, 아니면 None |
  | `PreBuildStep_ConfigDefines` | 20 | 항상 (LIVE는 끌 수 없음) | DefineContainer의 심볼을 적용합니다 (아래 "전처리기 심볼") |
  | `PreBuildStep_AndroidSettings` | 30 | Android | 버전 · versionCode, Development · Deep Profiling · Autoconnect Profiler, AAB 여부, Android Build Type(Development면 Debug, 아니면 Release), IL2CPP, ARM64 |
  | `PreBuildStep_WindowsSettings` | 30 | Windows | 버전, Development · Deep Profiling · Autoconnect Profiler |
  | `PreBuildStep_IOSSettings` | 30 | iOS | 버전 · buildNumber, `ProvisionUUID`가 있으면 Distribution 프로필 + UUID, Development · Deep Profiling · Autoconnect Profiler |
  | `PostBuildStep_ZipArchive` | 100 | Windows + `ZipPackage` | 빌드 폴더를 `<출력경로>/<이름>.zip`으로 압축합니다 |

  - HB_TestFrameworkHelper를 함께 설치하면 `HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots`(Order 1)가 더해집니다.
- **확장 (커스텀 스텝)**: `IPreBuildStep`(`OnPreBuild`) · `IPostBuildStep`(`OnPostBuild`)을 구현한 클래스를 Editor 어셈블리에 두면 자동 등록됩니다. 따로 등록할 필요가 없습니다.

  | 멤버 | 설명 |
  |---|---|
  | `StepId` | 프로필의 On/Off 저장용 ID. 기본값은 타입 전체 이름 |
  | `ExecutionOrder` | 낮을수록 먼저 실행 (필수) |
  | `StepName` | 창과 로그에 보이는 이름 (필수) |
  | `IsActive(BuildContext)` | 이 빌드에서 실행할지. 기본값 true |

  - `BuildContext`로 빌드 값(`Target` · `BuildType` · `Version` · `VersionCode` · `Scenes` · `OutputPath` 등), `PlayerOptions`, 빌드 뒤 `Report`, `Warnings`를 읽고 씁니다. 컨텍스트 값을 바꾼 뒤에는 `RebuildPlayerOptions()`를 불러야 실제 빌드 옵션에 반영됩니다.
  - `AddWarning(message)`: 빌드를 막지 않는 경고를 로그와 경고 파일에 남깁니다. 빌드를 멈추려면 예외를 던지세요 (`BuildHelper.ThrowError(msg)`가 예외 객체를 돌려줌 → `throw`).
  - 스텝 사이 데이터는 `SetCustomData` · `GetCustomData<T>`로 주고받습니다. HB_Build는 `SceneDefine` · `ProjectDefine` · `ProvisionUUID` 키를 쓰고, 프로필의 `CustomEntries`가 같은 키면 덮어쓰므로 피하세요.
  - 프로필 빌드(창 · `-buildProfile`)에만 `CustomEntries`가 들어갑니다. 프로필 없는 창 빌드와 전체 풀기 CLI 빌드에는 없습니다.
- **Jenkinsfile (Groovy) 생성 및 CLI 연동**:
  - 에디터 설정값을 바탕으로 Jenkins Declarative Pipeline 스크립트를 생성합니다.
  - CLI 환경에서 `-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild` 호출을 통한 배치를 지원합니다.
  - 창의 CLI 명령어 생성기는 같은 명령을 복사하거나 `.bat` · `.ps1`로 저장합니다. `.ps1`은 Unity 종료를 기다린 뒤 종료 코드를 그대로 돌려줍니다.
- **빌드 전 검증**: 아래 조건이면 빌드하지 않고 실패합니다.
  - Product Name · 버전에 파일 이름에 못 쓰는 문자, `/` · `\`, `..`가 있음 (출력 경로 밖으로 벗어나는 것 방지)
  - 인스턴스를 만들지 못한 스텝이 있음
  - 비활성 목록에 등록되지 않은 스텝 ID가 있음 (LIVE · 배치 모드만, 로컬 DEV · QA는 경고)
  - LIVE인데 심볼 설정 스텝을 껐거나 DefineContainer · 해당 항목이 없음
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
| `-autoProfile` | `true` · `false` | 플랫폼 설정 스텝이 Autoconnect Profiler(`EditorUserBuildSettings.connectProfiler`)를 켭니다 |

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
- **빌드 순서**: 이전 경고 파일 삭제 → 활성 플랫폼 확인 → `git status` 기록 → 설정 스냅샷 → 빌드 전 검증 → 사전 스텝 → 버전 코드 · 씬 확인 → `BuildPipeline.BuildPlayer` → 사후 스텝 → 설정 복원 → 바뀐 파일 목록 → 경고 파일 작성.
- **활성 플랫폼**: 로컬(에디터) 빌드는 활성 플랫폼이 다르면 빌드 전에 전환하고, 빌드 뒤 되돌리지 않습니다 (전환 비용이 큼). 배치 모드는 전환하지 않고 실패합니다.
- **스텝 On/Off**: Build Manager 창의 체크박스 상태는 빌드 프로필 에셋(`DisabledStepIds`)에 저장되어 로컬과 CI에서 동일하게 적용됩니다.
- **설정 복원**: 기본 스텝이 변경한 PlayerSettings / QualitySettings / EditorUserBuildSettings(버전, 심볼, 로그 설정 등)는 빌드 성공·실패와 관계없이 빌드 전 값으로 복원됩니다. 커스텀 스텝이 다른 설정을 바꾸면 직접 복원해야 합니다.
  - 복원 대상: `stripEngineCode` · `usePlayerLog` · `bundleVersion` · 스택트레이스(5종) · Scripting Define Symbols · Scripting Backend · `QualitySettings.skinWeights` · Development · Deep Profiling · Autoconnect Profiler. Android는 versionCode · AAB · Android Build Type · Target Architectures, iOS는 buildNumber · 수동 Provisioning Profile 타입 · ID.
  - 심볼 · Scripting Backend는 바뀐 경우에만 되돌립니다 (같은 값을 다시 써도 재컴파일될 수 있음). 저장은 PlayerSettings · QualitySettings 에셋만 합니다 (사용자의 저장하지 않은 다른 에셋은 저장하지 않음).
  - 빌드가 성공했는데 복원이 실패하면 빌드를 실패로 처리합니다. 빌드가 이미 실패했으면 원래 오류를 가리지 않도록 경고만 남깁니다.
- **전처리기 심볼**: 항목이 있는데 리스트가 비어 있으면 심볼을 모두 제거합니다. DefineContainer 또는 해당 빌드 타입/플랫폼 항목이 없거나 심볼 설정 스텝을 끄면 **LIVE는 실패**하고(커밋된 ProjectSettings의 개발용 심볼이 그대로 들어가는 것 방지), DEV · QA는 기존 심볼을 유지합니다 (항목이 없으면 경고).
  - DefineContainer는 프로필(또는 창)에 넣은 에셋 → `Assets/Editor/HBBuild/DefineContainer.asset` → 프로젝트에서 처음 찾은 `ProjectDefine` 에셋 순으로 씁니다. `SceneDefine`도 같은 순서입니다.
  - HB_InGameConsole을 쓰면 LIVE 항목에서 `USE_HLOG`를 빼서 출시 빌드의 HLog를 끕니다 (켜진 채 릴리즈 빌드하면 InGameConsole이 경고).
- **LIVE + Development Build**: 라이브 환경 프로파일링 · 로그 확인용으로 막지 않습니다. 대신 산출물 이름에 `_DEVBUILD`가 붙고(예: `Game_v1.0.0_LIVE_DEVBUILD.apk`), 빌드 시작 · 끝에 경고 블록이 찍히고, 로컬 빌드는 확인 창을 띄우고, Jenkins는 결과가 UNSTABLE이 됩니다.
- **경고 파일**: 빌드를 막지 않는 경고(LIVE + Development, 심볼 항목 없음, 등록되지 않은 비활성 ID 등)가 있으면 `BuildOutput/HB_BUILD_WARNINGS.txt`에 남깁니다. 생성한 Jenkinsfile은 이 파일이 있으면 내용을 출력하고 UNSTABLE로 표시합니다.
  - 출력 경로와 관계없이 항상 프로젝트의 `BuildOutput/`에 씁니다. 빌드를 시작할 때 이전 파일을 지우고, 빌드가 실패해도 그때까지의 경고를 씁니다. 로컬 빌드는 완료 창에 경고 수를 보입니다.
- **빌드가 바꾼 파일**: 빌드 전후 `git status`를 비교해 빌드 중 바뀐 파일(ProjectSettings 재저장 등)을 로그에 출력합니다. 자동으로 되돌리지 않으니 직접 확인하고 되돌리세요 (git이 PATH에 없으면 건너뜀).
  - 새로 바뀐 파일, 이미 바뀐 상태에서 다시 쓰인 파일(수정 시각), 빌드 중 원래대로 돌아간 파일을 모두 보입니다. `BuildOutput/` · 이번 출력 경로 · 빌드 로그 파일은 뺍니다.
  - 프로젝트가 저장소의 하위 폴더여도 저장소 루트 기준으로 봅니다. git 명령이 15초 안에 끝나지 않으면 건너뜁니다.
- **직접 빌드 경고**: Unity 기본 빌드(File > Build 등)처럼 HB_Build를 거치지 않은 플레이어 빌드는 콘솔에 경고합니다. HB_Build 스텝(심볼, 검증, 설정 복원)이 실행되지 않으므로 팀 빌드는 Build Manager로 하세요.
- **출력 경로**: 기본값은 프로젝트 기준 `BuildOutput/<Target>`입니다. Windows 빌드는 `<출력경로>/<이름>/<이름>.exe`처럼 버전별 하위 폴더에 생성되고, ZIP은 `<출력경로>/<이름>.zip`으로 생성됩니다. 같은 이름의 폴더가 이미 있으면 덮어쓰지 않고 `<이름> (2)`, `<이름> (3)` 폴더로 분리합니다. ZIP에는 `*_DoNotShip`, `*_ButDontShipItWithYourGame` 폴더가 포함되지 않습니다.
  - 창의 폴더 선택으로 프로젝트 안 폴더를 고르면 상대 경로로 저장합니다. 절대 경로는 다른 PC · Jenkins에서도 그대로 쓰이므로 창이 경고합니다.
- **로컬 빌드**: 프로필 빌드는 저장된 프로필 값으로 실행됩니다. 창에 저장하지 않은 변경이 있으면 빌드 전에 저장 여부를 묻고, 자동 저장하지 않습니다.
  - 선택지는 "저장 후 빌드" · "저장된 값으로 빌드" · "취소"입니다. 프로필을 고르지 않으면 창의 현재 값으로 빌드합니다.
  - 빌드는 창 그리기(OnGUI)가 끝난 뒤 실행하고, 끝나면 성공 · 실패 창을 띄웁니다.
- **Jenkinsfile**: 생성된 파이프라인은 빌드 전에 `BuildOutput/` 폴더만 지웁니다 (`Library/` 캐시는 유지). 소스 정리는 Jenkins job의 checkout 설정을 사용하세요.
  - 생성하면 클립보드에 복사하고, 프로젝트 루트에 `Jenkinsfile`로 저장할지 묻습니다.
  - 단계: `Checkout SCM` → `Clean Build Output` → `Unity Build Execution` → `Archive Artifacts`(`BuildOutput/**`). 끝나면 항상 경고 파일을 확인하고 `build.log`를 보관합니다.
  - 명령은 Unity 에디터를 직접 호출합니다. 경로는 에이전트 환경변수 `UNITY_PATH`, 없으면 `ProjectSettings/ProjectVersion.txt`의 버전으로 `${UNITY_EDITORS_ROOT}/<버전>/Editor/Unity.exe`(기본 `C:/Program Files/Unity/Hub/Editor`, macOS는 `/Applications/Unity/Hub/Editor/<버전>/Unity.app/...`)를 씁니다. Jenkinsfile에 경로를 박지 않으므로 Unity를 올려도 다시 생성할 필요가 없습니다. Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없습니다.
  - 자유 입력 값은 명령줄에 들어가므로 검사합니다: `BUILD_VERSION` · `VERSION_CODE`는 영문 · 숫자 · `.` · `_` · `-`만, 프로필 경로는 글자 · 숫자 · 공백 · `.` · `_` · `/` · `-`로 된 `.asset` 경로만 통과합니다.
  - 프로필 모드는 생성 시점의 "프로필 경로 → 플랫폼" 표로 `-buildTarget`을 넘깁니다. 프로필의 플랫폼을 바꾸면 Jenkinsfile을 다시 생성하세요 (그대로 두면 배치 빌드가 플랫폼 불일치로 실패).
  - 목록(`BUILD_PROFILE`)에 없는 프로필(빌드 PC 전용 프로필 등)은 `BUILD_PROFILE_PATH`에 경로를, `BUILD_TARGET`에 플랫폼을 직접 넣습니다. 플랫폼이 틀리면 배치 빌드가 불일치로 실패합니다.
  - 프로필 모드의 Jenkins 빌드는 프로필의 출력 경로 대신 `BuildOutput/<플랫폼>`에 만듭니다 (정리 · 보관 대상과 일치).
  - Linux 에이전트는 `UNITY_PATH`를 반드시 설정하세요 (기본 경로 찾기는 Windows · macOS Hub 경로만 지원).
  - **전체 풀기 모드**: `BUILD_TARGET` · `BUILD_TYPE` · `BUILD_VERSION` · `VERSION_CODE`와 `DEV_BUILD` · `DEEP_PROFILE` · `CLEAN_BUILD` · `IS_AAB` · `ZIP_PACKAGE`(booleanParam)가 Jenkins 파라미터입니다. 기본값은 생성한 사람의 창과 무관한 안전값(첫 선택지, 체크 모두 끔, 버전 · 버전 코드 빈 값)이고, Jenkins에서 바꾼 값이 우선합니다. `BUILD_VERSION`을 비우면 PlayerSettings의 버전을 씁니다. 씬 목록과 비활성 스텝은 생성 시 창 값으로 고정됩니다.
  - **프로필+오버라이드 모드**: 기본은 고른 프로필(`BUILD_PROFILE`)의 값입니다. Jenkins 파라미터 `BUILD_TYPE` · `DEV_BUILD`(`(profile)`이 아닌 값), `BUILD_VERSION` · `VERSION_CODE`(비어 있지 않은 값)를 넣은 것만 인자로 넘겨 프로필 값을 덮어씁니다.

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

## 운영 가이드: 라이브 프로필
- 프로필 에셋은 로컬 빌드와 Jenkins가 같이 쓰므로, 테스트하려고 바꾼 값(검증 스텝 끄기, Development Build 등)이 커밋되면 라이브 빌드에도 들어갑니다.
- 라이브 프로필은 빌드 PC 전용 폴더(예: `Assets/Editor/HBBuild/Profiles/BuildMachine/`)에 두고 그 폴더를 프로젝트 `.gitignore`에 등록하세요. Jenkins 워크스페이스는 `BuildOutput/`만 지우므로 빌드 PC에 둔 프로필이 유지됩니다 (Jenkins job의 checkout에서 `git clean -fdx`처럼 무시 파일까지 지우는 옵션을 쓰면 함께 지워지니 주의). Jenkins에서는 `BUILD_PROFILE_PATH`에 이 경로를, `BUILD_TARGET`에 플랫폼을 넣어 빌드합니다 (개발자 PC에서 만든 Jenkinsfile 목록에는 이 프로필이 없음).

## 에디터 메뉴 경로
- `Tools > HB > Custom Build Manager`
- `Assets > Create > HB_Build > Build Profile` · `DefineContainer` · `SceneDefine` (창의 버튼으로도 만들 수 있음)

| 창 영역 | 기능 |
|---|---|
| 빌드 프로필 | `+ 새 프로필`(창의 현재 값으로 `BuildProfile_<플랫폼>_<타입>` 생성) · `프로필 저장` · `복제`, 오브젝트 필드 · 목록으로 선택 |
| 타겟 플랫폼 & 환경 | Target Platform(`PC` · `Android` · `iOS`) · Build Type |
| 선택적 설정 에셋 | Scene Define · Define Container 슬롯, 비어 있으면 `+ 에셋 생성` |
| 빌드 파이프라인 실행 순서 | 사전 스텝 → BuildPlayer → 사후 스텝 목록. 체크박스로 끄고 켜기, 필터(조건 만족만 · 전체), 새로고침, 등록되지 않은 비활성 ID 제거 |
| 코드 미리보기 | 스텝 이름을 누르면 C# 소스를 보입니다 (이 클래스만 · 전체 파일, IDE에서 열기, 에셋 위치 Ping) |
| 버전 정보 | App Version · Version Code, iOS면 Provision UUID |
| 빌드 세부 옵션 | Development Build(켜면 Deep Profiling), Clean Build Cache, Android면 AAB, PC면 ZIP |
| 빌드 출력 경로 | 직접 입력 · 폴더 선택 · 열기. 비우면 기본 경로 표시 |
| 빌드 씬 목록 | Build Settings에서 불러오기 · 현재 씬 추가 · 전체 비우기 · 하나씩 제거 |
| Jenkins & Unity CLI 명령어 생성기 | 모드(순수 프로필 · 전체 풀기 · 프로필+오버라이드), 미리보기, 복사, `.bat` · `.ps1` 저장 |
| 하단 버튼 | `Local Player Build 실행` · `Generate Jenkinsfile` |
