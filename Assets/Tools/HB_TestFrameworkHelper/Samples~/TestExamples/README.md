# Test Examples

HB_TestFrameworkHelper의 테스트 작성 도우미(`TestScope` · `HBTestFixture` · `HBAssert` · `HBWait` · `TempDirectory` · `TempAssetFolder` · `TempScene`)를 쓰는 예제 테스트입니다. 모든 테스트는 통과하도록 쓰여 있습니다.

## 준비

1. `com.unity.test-framework`가 설치되어 있어야 합니다.
2. git URL 등으로 패키지를 설치했다면 `Packages/manifest.json`에 `testables`를 넣습니다. 없으면 예제 테스트가 `HB.TestFrameworkHelper` 네임스페이스를 찾지 못해 컴파일 에러가 납니다.

   ```json
   "testables": [ "com.hb.testframeworkhelper" ]
   ```

3. `Window > General > Test Runner`를 열면 EditMode · PlayMode 탭에 `HB.TestFrameworkHelper.Samples.Tests` 테스트가 보입니다.

## 폴더 구성

예제는 어셈블리 3개로 나뉩니다. 테스트 asmdef는 Assembly-CSharp를 참조할 수 없으므로, 테스트할 게임 코드도 asmdef로 묶었습니다.

| 폴더 | 어셈블리 | 내용 |
|---|---|---|
| `Runtime` | `HB.TestFrameworkHelper.Samples.TestExamples` | 테스트 대상인 가짜 게임 코드 |
| `Tests/EditMode` | `…Samples.TestExamples.Tests.Editor` | EditMode 테스트 (에디터 전용) |
| `Tests/PlayMode` | `…Samples.TestExamples.Tests.PlayMode` | PlayMode 테스트 (에디터 어셈블리를 참조하지 않음) |

내 프로젝트에 테스트 asmdef를 만들 때는 두 테스트 asmdef를 복사한 뒤 두 곳을 바꿉니다.

* `name`(과 `rootNamespace`)을 내 프로젝트 이름으로 바꿉니다. 샘플을 지우지 않았다면 같은 이름의 어셈블리가 둘이 되어 컴파일이 막힙니다.
* `references`의 `HB.TestFrameworkHelper.Samples.TestExamples` 항목을 내 게임 코드 어셈블리로 바꿉니다.

## 파일

| 파일 | 보여 주는 것 |
|---|---|
| `Runtime/ExampleGameSettings.cs` | 정적 설정 값 → `Preserve`로 되돌릴 대상 |
| `Runtime/ExampleItemConfig.cs` | ScriptableObject → `NewInstance`, 임시 에셋 폴더에 에셋으로 저장 |
| `Runtime/ExampleInventory.cs` | 할당 없는 `Add` → `NoGCAlloc`, 칸 설명 문자열 목록 → `AnyContains` · `NoneContains` |
| `Runtime/ExampleHealth.cs` | 죽으면 잠시 뒤 스스로 Destroy하는 컴포넌트 → PlayMode `HBWait` |
| `Runtime/ExampleSaveFile.cs` | 폴더에 세이브 파일 쓰기 · 읽기 → `TempDirectory` |
| `Tests/EditMode/FixtureExamples.cs` | `HBTestFixture` 상속, 파생 `[SetUp]`에서 Scope 쓰기, `NewGameObject` · `NewInstance` · `Track` · `Preserve` · `Defer` |
| `Tests/EditMode/TestScopeExamples.cs` | 다른 베이스를 상속했을 때 `TestScope`를 필드로 쓰기, `using` 블록, 역순 정리, Component만 `Track` |
| `Tests/EditMode/AssertExamples.cs` | `HBAssert.NoGCAlloc` · `AnyContains` · `NoneContains` (실패 메시지 예시는 주석) |
| `Tests/EditMode/TempResourceExamples.cs` | `NewTempDirectory` · `Own`(파일을 폴더보다 먼저 닫기) · `NewTempAssetFolder`(+`CreateAsset`) · `NewTempScene` |
| `Tests/PlayMode/WaitExamples.cs` | `HBWait.Frames`(Start가 도는 다음 프레임) · `HBWait.Until`(조건 · 시간 제한) |

## 참고

* `TempAssetFolder`는 `Assets/__HBTestTemp/`에 폴더를 만듭니다. 이 경로를 프로젝트 `.gitignore`에 넣어 두기를 권장합니다.
* `TempScene` 예제는 저장하지 않은 변경이 있는 씬이 열려 있으면 Inconclusive로 끝납니다 (사용자 변경을 저장하지도 버리지도 않음).
* `Runtime` 어셈블리는 플레이어 빌드에도 들어갑니다. 예제를 다 봤다면 폴더째 지워도 됩니다.
