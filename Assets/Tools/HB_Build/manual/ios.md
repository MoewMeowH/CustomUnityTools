# iOS 서명 · Xcode 설정

## iOS 서명

프로필 `CustomEntries`의 `ProvisionType`과 `ProvisionUUID`로 정합니다.

- `ProvisionType`은 대소문자를 무시하고, 없으면 `Distribution`입니다. 아래 값 밖이면 빌드가 실패합니다.

| `ProvisionType` | 동작 |
|---|---|
| `Development` · `Distribution` · `Automatic` | Unity `ProvisioningProfileType`과 같은 뜻입니다 (`Automatic` = 프로필 종류를 Xcode 빌드 때 자동 판정). UUID가 있으면 자동 서명을 끄고 그 종류 · UUID로 수동 서명합니다 |
| `AutoSign` (HB_Build 전용) | 자동 서명을 켜고 UUID는 넣지 않습니다 (UUID가 있으면 경고). Team ID는 프로젝트 설정 값을 씁니다 |

- 수동 서명 설정은 빌드 뒤 원래 값으로 복원합니다.
- UUID가 없으면 프로젝트의 서명 설정을 그대로 씁니다 (`ProvisionType`만 넣었으면 경고).

## iOS Xcode 설정

Unity가 만든 Xcode 프로젝트에 손으로 하던 설정을 에셋(`HBXcodeSettings` · `HBXcodeFragment`)으로 옮깁니다.
대상은 Info.plist 키, Capabilities · entitlements, Build Settings, 프레임워크 링크, 파일 복사입니다.
iOS 빌드마다 `BuildStep_XcodeSettings`가 적용하므로 Xcode에서 체크박스를 다시 누를 일이 없습니다.

### 에셋

- `HBXcodeSettings` (공용, 기본 `Assets/Editor/HBBuild/XcodeSettings.asset`)
  - 인스펙터 탭이 Xcode 이름 그대로입니다: `Signing & Capabilities` · `Info` · `Build Settings` · `Build Phases` · `Files`
  - 그 밖에 `묶음 · 결정` · `결과 미리보기` 탭이 있습니다.
- `HBXcodeFragment` (SDK 묶음)
  - 같은 다섯 탭을 갖습니다.
  - 공용 에셋의 `Fragments` 목록에 넣고 체크박스로 켜고 끕니다. SDK를 빼면 묶음만 끕니다.
- 모든 행에 `Enabled`와 빌드 타입 필터(`DEV` · `QA` · `LIVE`, 여럿 선택)가 있습니다.
  - 빌드 타입을 하나도 고르지 않은 행은 적용되지 않고 경고합니다.
  - 인스펙터 `+`로 만든 새 행은 Unity가 0으로 채웁니다. 그래서 처음 그릴 때 `Enabled` · 빌드 타입 `All` 등 기본값을 넣습니다.

### 값의 주인 (겹침 방지)

값마다 주인은 하나입니다. 아래 키를 Xcode 설정에 넣으면 빌드가 실패하고 주인을 알려 줍니다 (`XcodeOwnership`).

| 주인 | Info.plist 키 | Build Settings 키 |
|---|---|---|
| HB_Build 프로필 | `CFBundleShortVersionString` · `CFBundleVersion` | `CODE_SIGN_STYLE` · `CODE_SIGN_IDENTITY` · `PROVISIONING_PROFILE*` · `OTHER_CODE_SIGN_FLAGS` · `MARKETING_VERSION` · `CURRENT_PROJECT_VERSION` |
| PlayerSettings (iOS) | 아래 목록 | `DEVELOPMENT_TEAM` · `PRODUCT_BUNDLE_IDENTIFIER` · `IPHONEOS_DEPLOYMENT_TARGET` |
| Xcode 설정 자체 · Info 탭 | — | `INFOPLIST_FILE` · `CODE_SIGN_ENTITLEMENTS` · `GENERATE_INFOPLIST_FILE` · `INFOPLIST_*` |
| 서명 (entitlements) | `application-identifier` · `com.apple.developer.team-identifier` · `get-task-allow` (Entitlements 탭) | — |

PlayerSettings(iOS)가 주인인 Info.plist 키:

```text
CFBundleIdentifier · CFBundleDisplayName · CFBundleName · CFBundleURLTypes · MinimumOSVersion
NSCameraUsageDescription · NSLocationWhenInUseUsageDescription · NSMicrophoneUsageDescription
UIRequiresFullScreen · UIStatusBarHidden · UISupportedInterfaceOrientations(~ipad) · UILaunchStoryboardName
UIRequiresPersistentWiFi · UIApplicationExitsOnSuspend · CADisableMinimumFrameDurationOnPhone
```

- `INFOPLIST_*`는 접두사입니다. `INFOPLIST_KEY_*` 등은 Info 값을 우회하기 때문입니다.
- `UIBackgroundModes`는 PlayerSettings(Background Modes)도 쓰지만 목록이라 합집합으로 합칩니다 (SDK 묶음마다 추가 가능).
- `KEY[sdk=...]` 같은 조건부 키도 앞부분으로 판정합니다.
- `Signing & Capabilities` 탭 위쪽에 주인이 다른 값들을 읽기 전용으로 보여 줍니다 (`Player Settings 열기` 버튼).

### 탭별 내용

**`Signing & Capabilities`**

- Push Notifications(`aps-environment` development/production) · Sign in with Apple · Associated Domains · Background Modes · In-App Purchase · Game Center · Keychain Sharing · App Groups
- entitlements 직접 행
- Capability는 entitlements · Info.plist 값으로 바꿔 다른 행과 함께 충돌 검사합니다 (Push → `aps-environment`, Background Modes → `UIBackgroundModes` 등).

**`Info`**

- Key / Type(String · Bool · Integer · Real · StringArray · Raw) / Value
- Raw는 plist XML 값 하나입니다 (예: `SKAdNetworkItems`).
- 키 오른쪽 `▾`에 자주 쓰는 키가 있습니다.

**`Build Settings`**

- 타깃(`App` · `UnityFramework` · `GameAssembly` · `All`) · 구성(비우면 모든 구성) · 키 · `Set`/`Add` · 값
- `All`은 App + UnityFramework + GameAssembly(있을 때)에 각각 씁니다 (Tests 제외).
- 타깃은 이름이 아니라 Unity API로 찾습니다 (Unity 7 Swift 형식은 앱 타깃 이름이 제품 이름).

**`Build Phases`**

- 시스템 프레임워크 · 라이브러리 링크 (`AdSupport.framework` · `libz.tbd`, Optional = weak)
- 이미 **다른** Required/Optional로 링크돼 있으면 실패합니다 (Unity 7은 `AdSupport.framework`를 Required로 링크해 둠).
  바꾸려면 Framework 결정 행에 `OverwriteExisting`을 넣습니다. 다시 링크합니다.
- `.tbd`는 `usr/lib`에서 링크하고 Optional로 지정할 수 없습니다.
- `Assets/Plugins/iOS`의 프레임워크 파일과 임베드는 Unity Plugin Inspector가 관리합니다.

**`Files`**

- 원본(Unity 프로젝트 기준 상대 경로) → 대상(Xcode 프로젝트 폴더 기준 상대 경로, `..` · 절대 경로 불가)
- 파일 / 폴더 참조, 대상 타깃(App · UnityFramework), Copy Bundle Resources 포함 여부
- 다른 후처리기가 참조만 만들어 둔 파일도 Resources에 넣습니다.
- 대상에 같은 내용이 이미 있으면 다시 복사하지 않습니다.
- 폴더 복사 때 `.meta`와 숨김 파일 · 폴더(`.`으로 시작, `.DS_Store` · `.git` 등)는 빠지고, 복사한 파일 목록을 로그에 남깁니다.
- 예: 빌드 타입별 `GoogleService-Info.plist` (같은 대상 경로에 행 두 개, 빌드 타입 필터만 다르게)

`Files`가 실패하는 경우:

- 원본이 **Unity 프로젝트 밖**이거나 심볼릭 링크 · 정션임
- 비밀 · 서명 파일이 파일이든 폴더 안이든 있음 — 원본 폴더 밖으로 옮기세요.
  ```text
  .p8 .p12 .pfx .pem .key .cer .mobileprovision .provisionprofile .keystore .jks .env
  ```
- 대상이 Info.plist · entitlements 파일 · `*.xcodeproj`이거나 그 위 · 안임 (Info · Entitlements 탭을 쓰세요)
- 대상에 Windows 장치 이름(`CON` · `NUL` 등), 끝이 점 · 공백인 조각, 심볼릭 링크가 있음

### 에셋 참조 검사

- 프로필 · 창의 슬롯이 깨진 참조(Missing)이면 실패합니다.
- `CustomEntries`가 `XcodeSettings` 키를 다른 값으로 쓰면 실패합니다.
- 켜진 `Fragments` 칸이 비었거나 Missing이면 **LIVE는 실패**, DEV · QA는 경고합니다 (그 SDK 설정이 통째로 빠지므로).

### LIVE 경고

아래는 실패시키지 않고 경고 파일 · Jenkins UNSTABLE로 알립니다.

- `aps-environment`가 development
- `NSAppTransportSecurity`의 `NSAllowsArbitraryLoads` 또는 도메인별 `NSExceptionAllowsInsecureHTTPLoads`가 true

### SDK를 붙일 때

1. SDK마다 `HBXcodeFragment`를 하나 만듭니다 (`Note`에 SDK · 버전).
2. SDK 문서가 요구하는 키 · 권한 · Capability · 링크 옵션을 넣습니다.
3. 공용 에셋 `Fragments`에 추가합니다.

Info · Entitlements 탭의 `붙여넣기` 버튼에 SDK 문서의 plist 조각을 넣으면 행으로 바뀝니다.
받는 형식은 `<key>…</key><string>…</string>` 나열, `<dict>`, 전체 plist입니다.

예: Firebase Cloud Messaging 묶음

- Push Notifications (`aps-environment`는 서명 종류에 맞춰 직접 고름)
- Background Modes `remote-notification`
- Info `FirebaseAppDelegateProxyEnabled`

### 합치기 · 충돌

충돌은 항상 빌드 실패입니다.

- 단일 값(String · Bool · 숫자, Build Settings `Set`): 값이 같으면 하나로, 다르면 실패하고 어느 행끼리 부딪혔는지 보여 줍니다.
- 목록 값(StringArray, `UIBackgroundModes` 등, Build Settings `Add`): 합집합입니다.
  - `SKAdNetworkItems`는 `SKAdNetworkIdentifier` 기준(대소문자 무시) 합집합입니다.
  - 그 밖의 Raw 복합 값은 완전히 같아야 합니다.
- 아래도 실패합니다.
  - 같은 키의 타입이 다름
  - 같은 칸에 `Set`과 `Add`가 섞임
  - 같은 프레임워크의 Required/Optional이 다름
  - 같은 대상 경로에 내용 · 방식 · 타깃이 다른 파일
  - 한 대상이 다른 대상 폴더 안에 있음
- 충돌 판정 단위: Build Settings는 (타깃, 구성, 키), 나머지는 키 · 대상 경로입니다.
  `All`과 특정 타깃 행은 펼친 뒤 비교합니다 (특정 타깃이 자동으로 이기지 않음).
- **Xcode 프로젝트에 이미 있는 값과 달라도 실패**합니다 (Unity 기본값 · 다른 SDK 후처리기 구분 없이).
  목록 값은 기존 값과 합칩니다. 같은 값이면 통과합니다.
- Build Settings에서 `OTHER_LDFLAGS` 같은 목록 설정을 `$(inherited)` 없이 `Set`하면 경고합니다.
  CocoaPods xcconfig 값이 가려지기 때문입니다. `Add`를 쓰세요.

**해결은 `묶음 · 결정` 탭의 결정 행으로만** 합니다.

- 결정 행: 종류 · (타깃 · 구성) · 키 · `ChosenSource`(이길 에셋) · `OverwriteExisting`(기존 값 덮어쓰기) · 메모(필수)
- 결정할 충돌이 없어진 결정 행은 경고합니다.

### 적용 순서

1. 빌드 전에 계획을 세워 위 검사(기존 값 비교 제외)를 합니다. 오류가 있으면 BuildPlayer 전에 멈춥니다.
2. 빌드 뒤에는 생성된 프로젝트의 실제 구성 · 타깃으로 다시 계획합니다.
3. 기존 값 비교까지 **모든 검사를 통과한 뒤에만** 파일을 씁니다.

SDK들의 Unity 빌드 후처리기는 BuildPlayer 안에서 끝나므로 이 스텝이 마지막입니다.

- Info.plist · entitlements 경로 빌드 설정이 절대 경로 · `..` · `$(변수)`면 실패합니다 (다른 후처리기가 바꾼 경우).
- 실수 값은 Xcode plist API가 float로 쓰므로 float 정밀도로 비교하고, float로 나타낼 수 없는 값은 실패합니다.
- 빌드 전에 점검한 내용을 그대로 적용합니다.
  BuildPlayer가 쓰지 않는 에셋을 정리하면 넘겨받은 에셋 참조가 끊기므로, 점검 시점에 내용을 복사해 둡니다.
- Info.plist는 앱 타깃의 `INFOPLIST_FILE`입니다 (ObjectiveC 형식 `Info.plist`, Unity 7 Swift 형식 `MainApp/Info.plist`).
- entitlements는 `CODE_SIGN_ENTITLEMENTS`가 있으면 그 파일입니다.
  없으면 Info.plist 옆에 `HBXcode.entitlements`를 만들고 앱 타깃의 모든 구성에 연결합니다.
- HB_Build의 iOS 빌드는 항상 Replace(산출물 폴더를 새로 만듦)이므로, 새로 생성된 프로젝트에 한 번 적용하는 것을 기준으로 합니다.

### iOS Build Support 모듈이 없는 PC

- Xcode 프로젝트를 고치는 코드(`XcodeSettingsApplier`)만 `#if UNITY_IOS`로 분리돼 있어 패키지는 그대로 컴파일됩니다.
- 에셋 편집 · 결과 미리보기는 모듈 없이도 됩니다.
- 적용할 설정이 있는데 적용 코드가 컴파일돼 있지 않으면 BuildPlayer 전에 실패합니다.
  모듈 설치를 확인하거나, 로컬 빌드가 방금 iOS로 플랫폼을 전환했다면 컴파일이 끝난 뒤 다시 빌드하세요.

### 지역화

**Mac에서 아직 확인하지 않은 실험 기능입니다.** Windows에서 생성 파일 · pbxproj 내용까지만 확인했습니다.

- 앱 이름 · 권한 문구: `*.lproj/InfoPlist.strings`를 `Files`에 **폴더 참조**로 넣습니다 (대상 예: `ko.lproj`, App 타깃, Resources).
- `FillLocalizationsFromLproj`(기본 켜짐)이면 `*.lproj` 이름으로 `CFBundleLocalizations`를 채웁니다.
- Unity 템플릿은 `CFBundleDevelopmentRegion`(en) · `CFBundleAllowMixedLocalizations`를 이미 넣습니다.
- `PBXProject.AddLocaleVariantFile`은 쓰지 않습니다.
  ObjectiveC 형식에서 Tests 타깃의 `InfoPlist.strings` 그룹에 섞이고, Swift 형식에서 빌드 단계에 들어가지 않으며, 두 번 부르면 항목이 중복되기 때문입니다.
- 홈 화면 아이콘의 언어별 지역화는 iOS가 지원하지 않습니다. 앱 안에서 바꾸는 alternate icons는 이 도구 범위 밖입니다.

### 확인 범위

- Windows에서 Unity 6000.3.23f1 · 7000.0.0a8(ObjectiveC · Swift 형식)로 실제 iOS 빌드 → 적용 → 생성 파일 · pbxproj · plist 내용까지 확인했습니다.
- Xcode로 열기 · 서명 · 아카이브 · 기기 동작은 Mac에서 아직 확인하지 않았습니다.
- `PBXProject.AddCapability`는 pbxproj의 `SystemCapabilities`에 흔적을 남기지 않습니다.
  Capability의 실제 효과는 entitlements · Info.plist · `CODE_SIGN_ENTITLEMENTS`로 들어갑니다. Xcode가 이것으로 Capability를 인식하는지는 Mac에서 확인하지 않았습니다.

### 기본 항목 메모

- 암호화를 쓰지 않거나 면제 암호화만 쓰면 `ITSAppUsesNonExemptEncryption = false`를 넣습니다 (업로드마다 수출 규정 질문 방지, Unity 매뉴얼).
- ATT를 쓰면 `NSUserTrackingUsageDescription`을 넣습니다.
- 앱 자체의 개인정보 매니페스트(`PrivacyInfo.xcprivacy`)는 이 도구가 아니라 `Assets/Plugins`에 두면 Unity가 합칩니다.
- 2026-04-28부터 App Store 업로드는 Xcode 26 / iOS 26 SDK 이상이 필요합니다.
