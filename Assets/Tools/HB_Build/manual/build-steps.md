# 빌드 스텝

## 스텝 파이프라인 (`BuildStepRegistry`)

`TypeCache`로 빌드 스텝을 찾습니다.

- 프로젝트 안 `IPreBuildStep`, `IPostBuildStep` 구현체를 자동으로 찾아 `ExecutionOrder` 순으로 실행합니다.
- 상속 관계에 있는 빌드 스텝이 있으면 파생 클래스가 부모 스텝을 자동으로 대신합니다.
- `IsActive(context)`가 false이거나 비활성 목록에 있는 스텝은 실행하지 않습니다.
- 스텝이 예외를 던지면 빌드가 실패합니다.
- 인스턴스를 만들지 못한 스텝(공개 기본 생성자 없음, 생성자 예외)이 있으면 빌드 시작 때 실패합니다.
  빠진 스텝의 검증 · 설정 없이 빌드되는 것을 막기 위해서입니다.

## 기본 빌드 스텝

ID는 모두 `HB.CustomBuild.Editor.` 뒤에 아래 이름이 붙습니다.

| 스텝 | Order | 실행 조건 | 하는 일 |
|---|---|---|---|
| `PreBuildStep_FilterScenes` | 5 | 항상 (SceneDefine 없으면 건너뜀) | 빌드 타입의 `IgnoreSceneList` 씬을 목록에서 뺍니다 |
| `PreBuildStep_PlayerSettings` | 10 | 항상 | `stripEngineCode` 끔, Player Log · 스택트레이스 설정 (아래 설명) |
| `PreBuildStep_ConfigDefines` | 20 | 항상 (LIVE는 끌 수 없음) | DefineContainer의 심볼을 적용합니다 ([전처리기 심볼](build-behavior.md#전처리기-심볼)) |
| `PreBuildStep_AndroidSettings` | 30 | Android | 버전 · versionCode 등 (아래 설명) |
| `PreBuildStep_WindowsSettings` | 30 | Windows | 버전, Development · Deep Profiling · Autoconnect Profiler |
| `PreBuildStep_IOSSettings` | 30 | iOS | 버전 · buildNumber, 서명([iOS 서명](ios.md#ios-서명)), Development · Deep Profiling · Autoconnect Profiler |
| `BuildStep_XcodeSettings` | 40 | iOS (XcodeSettings 없으면 건너뜀) | Xcode 설정 검사 · 적용 (아래 설명) |
| `PostBuildStep_ZipArchive` | 100 | Windows + `ZipPackage` | 빌드 폴더를 `<출력경로>/<이름>.zip`으로 압축합니다 (아래 설명) |

- `PreBuildStep_PlayerSettings`
  - Player Log는 Development Build 여부를 따릅니다.
  - 스택트레이스: Error · Exception은 ScriptOnly입니다. Log · Warning · Assert는 Development 또는 DEV면 ScriptOnly, 아니면 None입니다.
- `PreBuildStep_AndroidSettings`: 버전 · versionCode, Development · Deep Profiling · Autoconnect Profiler, AAB 여부, Android Build Type(Development면 Debug, 아니면 Release), IL2CPP, ARM64
- `BuildStep_XcodeSettings` ([iOS Xcode 설정](ios.md#ios-xcode-설정))
  - 빌드 전: Xcode 설정 계획 · 충돌 검사. 실패하면 BuildPlayer 전에 멈춥니다.
  - 빌드 뒤: 생성된 Xcode 프로젝트에 적용합니다.
  - 빌드 전 · 뒤가 한 스텝이라 체크박스 하나로 같이 끕니다.
- `PostBuildStep_ZipArchive`: ZIP이 이미 있으면 지우지 않고 `<이름> (2).zip` …으로 만듭니다.

### HB_TestFrameworkHelper 스텝

HB_TestFrameworkHelper를 함께 설치하면 `HB.TestFrameworkHelper.Editor.PreBuildStep_ValidateInspectorSlots`(Order 1)가 더해집니다.

- HB_Build가 UPM 패키지로 설치됐을 때만 켜집니다.
- `versionDefines`가 패키지를 보고 정하므로, HB_Build를 `Assets`에 직접 둔 배치에서는 꺼집니다.

## 커스텀 스텝

`IPreBuildStep`(`OnPreBuild`) · `IPostBuildStep`(`OnPostBuild`)을 구현한 클래스를 Editor 어셈블리에 두면 자동 등록됩니다. 따로 등록할 필요가 없습니다.

모든 타입은 `HB.CustomBuild.Editor` 네임스페이스에 있습니다. 커스텀 스텝을 만들 때 `using HB.CustomBuild.Editor;`를 추가하세요.

| 멤버 | 설명 |
|---|---|
| `StepId` | 프로필의 On/Off 저장용 ID. 기본값은 타입 전체 이름 |
| `ExecutionOrder` | 낮을수록 먼저 실행 (필수) |
| `StepName` | 창과 로그에 보이는 이름 (필수) |
| `IsActive(BuildContext)` | 이 빌드에서 실행할지. 기본값 true |

### BuildContext

- 빌드 값(`Target` · `BuildType` · `Version` · `VersionCode` · `Scenes` · `OutputPath` 등), `PlayerOptions`, 빌드 뒤 `Report`, `Warnings`를 읽고 씁니다.
- 컨텍스트 값을 바꾼 뒤에는 `RebuildPlayerOptions()`를 불러야 실제 빌드 옵션에 반영됩니다.
- `AddWarning(message)`: 빌드를 막지 않는 경고를 로그와 경고 파일에 남깁니다.
- 빌드를 멈추려면 예외를 던지세요. `BuildHelper.ThrowError(msg)`가 예외 객체를 돌려주므로 `throw`합니다.

### 스텝 사이 데이터

- `SetCustomData` · `GetCustomData<T>`로 주고받습니다.
- HB_Build가 쓰는 키: `SceneDefine` · `ProjectDefine` · `ProvisionUUID` · `ProvisionType` · `XcodeSettings` · `XcodeSettings.Snapshot`
  - 프로필의 `CustomEntries`가 같은 키면 덮어쓰므로 피하세요.
- `CustomEntries`는 프로필 빌드(창 · `-buildProfile`)에만 들어갑니다. 프로필 없는 창 빌드와 전체 풀기 CLI 빌드에는 없습니다.

### 스텝 ID

- 스텝 ID(`StepId`)는 기본적으로 타입의 전체 이름입니다.
- 기본 스텝은 ID가 고정되어 있어 클래스 이름이 바뀌어도 그대로입니다 (예: `HB.CustomBuild.Editor.PreBuildStep_PlayerSettings`).
- 커스텀 스텝은 `StepId`를 직접 정의하면 이름을 바꿔도 프로필이 깨지지 않습니다.
- 기본 스텝을 상속해 오버라이드한 스텝은 부모의 고정 ID를 그대로 이어받습니다. 부모 대신 등록되므로 같은 ID로 끄고 켭니다.
- 프로필의 비활성 목록(또는 `-disabledSteps`)에 등록되지 않은 ID가 있으면 LIVE · 배치 빌드는 실패하고, 로컬 DEV · QA는 경고합니다.
  Build Manager 창에서 제거할 수 있습니다.

## 빌드 전 검증

아래 조건이면 빌드하지 않고 실패합니다.

- Product Name · 버전에 파일 이름에 못 쓰는 문자, `/` · `\`, `..`가 있음
  - 출력 경로 밖으로 벗어나는 것을 막습니다. 사전 스텝이 바꾼 값까지 보도록 스텝 뒤에 확인합니다.
- 지원하지 않는 빌드 대상 (Windows · Android · iOS 밖, 예: `StandaloneOSX`)
  - 창 · 프로필 · CLI 모두, 플랫폼 전환 전에 확인합니다.
- 인스턴스를 만들지 못한 스텝이 있음
- 같은 `StepId`로 둘 이상 등록된 스텝이 있음
  - 예: 같은 기본 스텝을 덮어쓴 형제 클래스 둘 (한쪽이 다른 쪽을 상속하거나 각자 `StepId`를 정의)
- 비활성 목록에 등록되지 않은 스텝 ID가 있음 (LIVE · 배치 모드만, 로컬 DEV · QA는 경고)
- LIVE인데 심볼 설정 스텝을 껐거나 DefineContainer · 해당 항목이 없음 ([전처리기 심볼](build-behavior.md#전처리기-심볼))
- 기본 경로에 없는 DefineContainer · SceneDefine이 여러 개
- 모바일 버전 코드가 비었거나 형식이 틀림
- 빌드할 씬이 없음
- 배치 모드의 활성 플랫폼이 빌드 대상과 다름
