# 빌드 프로필 · 심볼 · 씬

## 멀티 플랫폼 빌드

Windows 64bit, Android (APK/AAB), iOS 빌드를 지원합니다.

- 산출물 이름은 `<Product Name>_v<버전>_<빌드 타입>`입니다 (LIVE + Development는 끝에 `_DEVBUILD`).
  - Windows: `<이름>/<이름>.exe`
  - Android: `<이름>.apk` · `<이름>.aab`
  - iOS: `<이름>/` Xcode 프로젝트 폴더
- 빌드 옵션은 항상 `CompressWithLz4HC`입니다.
- Development · Deep Profiling · Clean Build Cache는 체크한 경우에만 더합니다. Deep Profiling은 Development Build일 때만 적용됩니다.

## 빌드 프로필 (`HBBuildProfile`)

빌드 설정 한 벌을 `ScriptableObject` 에셋으로 저장합니다.

- 로컬 빌드와 Jenkins(`-buildProfile`)가 같은 에셋을 씁니다.
- 창의 `+ 새 프로필`은 `Assets/Editor/HBBuild/Profiles/`에 만듭니다.

| 필드 | 설명 |
|---|---|
| `TargetPlatform` | `PC`(StandaloneWindows64) · `Android` · `iOS` |
| `BuildType` | `DEV` · `QA` · `LIVE` |
| `Version` | 앱 버전 (`PlayerSettings.bundleVersion`) |
| `VersionCode` | Android `versionCode` / iOS `buildNumber`. 기본 빈 값 |
| `DevBuild` · `DeepProfile` · `CleanBuild` | Development Build · Deep Profiling Support · Clean Build Cache |
| `IsAab` | Android 전용. 켜면 `.aab`, 끄면 `.apk` |
| `ZipPackage` | PC 전용. 빌드 폴더를 ZIP으로 압축 |
| `ProvisionUUID` | iOS 전용. 값이 있으면 자동 서명을 끄고 이 Provisioning Profile로 수동 서명 ([iOS 서명](ios.md#ios-서명)) |
| `OutputPath` | 비우면 `BuildOutput/<Target>`. 프로젝트 기준 상대 경로 권장 |
| `Scenes` | 비우면(공백 항목만 있어도) Build Settings의 활성 씬 |
| `SceneDefineAsset` · `ProjectDefineAsset` | 선택 설정 에셋. 비우면 기본 위치 → 프로젝트 검색 순으로 찾습니다 |
| `XcodeSettingsAsset` | iOS 전용 선택 설정 에셋 ([iOS Xcode 설정](ios.md#ios-xcode-설정)) |
| `CustomEntries` | 스텝에 넘길 Key-Value (`BuildContext.CustomData`). 창에는 없고 Inspector에서 편집합니다 |
| `DisabledStepIds` | 이 프로필에서 끌 스텝 ID. Build Manager 창의 체크박스로 편집 |

- `ProvisionUUID`의 서명 종류는 `CustomEntries`의 `ProvisionType`으로 정합니다 (기본 Distribution).
- `XcodeSettingsAsset`을 찾는 규칙은 `SceneDefineAsset` · `ProjectDefineAsset`과 같습니다 (기본 `Assets/Editor/HBBuild/XcodeSettings.asset`).

## 환경별 심볼 관리 (`ProjectDefine`)

빌드 타입(`DEV` · `QA` · `LIVE`)과 플랫폼 종류(`PC` · `MOBILE`)별 Scripting Define Symbols를 `ScriptableObject`로 관리합니다.

- 기본 위치는 `Assets/Editor/HBBuild/DefineContainer.asset`입니다.
- `MOBILE` 항목은 Android · iOS가 같이 씁니다.

| 필드 (`Container` 항목마다) | 설명 |
|---|---|
| `BuildType` | `DEV` · `QA` · `LIVE` |
| `PlatformType` | `PC`(Windows) · `MOBILE`(Android · iOS). `NONE`은 쓰지 않습니다 |
| `DefineArr` | 적용할 심볼 목록. 빈 목록이면 심볼을 모두 지웁니다 |

심볼은 `PlayerSettings.SetScriptingDefineSymbols`로 빌드 타깃 그룹에 씁니다 (빌드 뒤 복원).
찾는 순서와 실패 조건은 [전처리기 심볼](build-behavior.md#전처리기-심볼)에 있습니다.

## 환경별 씬 제외 (`SceneDefine`)

빌드 타입마다 빌드에서 뺄 씬을 정합니다.

- 기본 위치는 `Assets/Editor/HBBuild/SceneDefine.asset`입니다.
- 에셋이 없으면 건너뜁니다.

| 필드 (`Data` 항목마다) | 설명 |
|---|---|
| `Type` | `DEV` · `QA` · `LIVE` (새 에셋은 세 항목으로 시작) |
| `IgnoreSceneList` | 뺄 씬 경로 (예: `Assets/Scenes/Debug.unity`). 씬 목록의 경로와 대소문자 무시로 비교합니다 |
