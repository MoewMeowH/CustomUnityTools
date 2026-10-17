# HB_TestFrameworkHelper 패키지 점검 로그 (2026-10-18)

## 0. 기준
- 패키지: `Assets/Tools/HB_TestFrameworkHelper` (1.0.0, 커밋 `d62d14f`와 동일)
- 분석기 원본: `F:\AI\ToolWithCS\UnityTestFrameWorkChecker\HB_TestFrameworkHelper` (Analyzer 162줄 + CodeFix 86줄, `Microsoft.CodeAnalysis.CSharp`·`Workspaces` 3.8.0)
- 패키지 DLL = 원본 `bin/Release` (SHA-256 앞 16자 `df49ce5f76b7cb5f`, 확정)
- 구성: Runtime(어트리뷰트, 글로벌 namespace, autoReferenced) / Editor(엔진·창·NUnit 테스트) / Integrations(HB_Build 있을 때만, `versionDefines` `com.hb.custombuild` expression `""`)

## 1. 실제 확인 (ToolTest6.3, 6000.3.23f1)
- manifest·lock은 E2String 때 백업(`tt63_backup/`)과 같은 상태에서 시작. `com.hb.testframeworkhelper` + `com.hb.custombuild` 추가.
- 테스트 폴더 `Assets/TFHCheck/`:
  - `TFH.Slots`(asmdef → Runtime): `SlotHolder` — 단일 `Transform`, `List<GameObject>`(null 원소 1), `GameObject[]`(null 원소 1), 중첩 `[Serializable] Inner`의 슬롯, `[SerializeField]` 없는 슬롯
  - `TFH.Generic`(autoReferenced false): `GenericSlot<T> : MonoBehaviour where T : Component`의 `T _target`
  - `Editor/TFHCheckRunner`: 위 컴포넌트로 프리팹 저장 → `SlotValidatorEngine.ScanPrefabs`

### 결과 (모두 확정)
| # | 항목 | 결과 |
|---|---|---|
| 확인 | 분석기 적용 범위 | asmdef 어셈블리에서 동작 (`SlotHolder.cs(16,47): warning HB_NeedSlotNullCheckSerializeField`) |
| 확인 | Workspaces 참조(CodeFix 동봉) | `CS8032` 등 분석기 로드 경고 없음 |
| 확인 | HB_Build 연동 | `HB.TestFrameworkHelper.BuildIntegration.Editor.dll` 생성 (expression `""` 동작) |
| 확인 | 패키지 테스트 발견 | `file:`·git URL(`#d62d14f…`) 모두 `testables` 없이 `SlotValidationTests` 1건 실행 (`Library/PackageCache/com.hb.testframeworkhelper@a0f93c1f5df1/...`) |
| A | 분석기 오탐 | `GenericSlot.cs(8,55): error HB_NeedSlotNullCheckInvalidType … 타입(T)` — `T : Component` 제약이 있어도 **에러로 컴파일 차단** |
| B | 엔진 범위 | 이슈 2건(`_single`, `_noSerializeField`)만. **List null 원소, 배열 null 원소, 중첩 `Inner._nested` 모두 미검출** |

## 2. 코드 검토로 찾은 것
- **C. test-framework 의존 (추론, 높은 확신).** `SlotValidationTests.cs`가 엔진·창과 같은 `HB.TestFrameworkHelper.Editor`에 있고 `using NUnit.Framework`. NUnit은 `com.unity.ext.nunit`(test-framework 의존)의 `nunit.framework.dll`이 `isExplicitlyReferenced: 0`로 자동 참조돼서 컴파일되는 것. package.json에 의존성이 없으므로 test-framework가 없는 프로젝트에서는 Editor 어셈블리 전체(창·엔진·빌드 연동)가 CS0246으로 깨진다. ToolTest6.3은 다른 패키지(`test-framework.performance`)가 의존해 제거 실험 불가.
- B의 원인: `SlotValidatorEngine.cs:117-153` — `FindProperty(field.Name)`가 배열/List면 `propertyType`이 `Generic`이라 아무 검사도 안 함. 중첩 클래스 필드는 `DeclaringType`이 `Inner`라 `IsAssignableFrom(MonoBehaviour 타입)`에서 빠짐.
- A의 원인: `IsUnityEngineObjectType`이 `ITypeParameterSymbol`의 제약을 보지 않음 (`BaseType`이 null).
- 마이너:
  - `EValidationIssueType.MissingReference`를 만드는 코드가 없음. Missing 참조(삭제된 에셋)는 `objectReferenceValue == null`이라 `NullSlot`으로 보고됨.
  - `SerializedObject`를 Dispose하지 않고, 타입 쌍마다 새로 만듦.
  - 직렬화 안 되는 필드(`_noSerializeField`)도 리플렉션 경로로 `NullSlot` 보고 (분석기 경고와 중복).
  - 검사 대상은 프리팹만 (씬, ScriptableObject 에셋 제외). README는 "프리팹"으로 일관되게 적혀 있음.
  - 창의 검사 폴더 텍스트 입력을 검증하지 않음 (없는 폴더면 `FindAssets` 동작에 맡김).
  - 대량 프리팹 로드에 진행 표시·취소 없음.

## 3. 결정과 계획서
- 계획서: [docs/plans/2026-10-18_testframeworkhelper-package-check.md](../plans/2026-10-18_testframeworkhelper-package-check.md) (`결정` 절에 사용자 답 원문)
- B: 배열/List null 원소는 경고만, 어트리뷰트 인자 `warnNullElements`(기본 true). C: 테스트 asmdef 분리.

## 4. 항목별 처리
| 항목 | 처리 | 파일 |
|---|---|---|
| A 제네릭 오탐 | 타입 매개변수면 제약(매개변수끼리 이어진 제약 포함)에 `UnityEngine.Object` 계열이 있을 때 통과. 방문 집합으로 순환 제약에서 멈춤 | 분석기 |
| A+ 컬렉션 범위 (리뷰) | 슬롯 컬렉션을 1차원 배열·`List<T>`로 한정 (`HashSet`·`Dictionary`·다차원·가변 배열은 에러) — 엔진이 검사하는 범위와 맞춤 | 분석기 |
| B 엔진 범위 | MonoBehaviour 직렬화 필드 그래프를 SerializedProperty 경로로 따라감 (상속·중첩·중첩 배열/List). 단일 null = Error `NullSlot`, 배열/List null 원소 = Warning `NullElement`(인자로 끔), 빈 목록 통과, 비직렬화 필드 제외 | `SlotValidatorEngine.cs`, `NeedSlotNullCheckAttribute.cs` |
| C test-framework | `Editor/Tests/HB.TestFrameworkHelper.Tests.Editor.asmdef` 분리 (아래 "계획과 달라진 것") | 테스트 asmdef·.meta |
| 테스트·빌드 | 테스트는 Error만 실패·Warning은 로그, LIVE 빌드는 Error만 차단 | `SlotValidationTests.cs`, `PreBuildStep_ValidateInspectorSlots.cs` |
| 마이너: SerializedObject | 컴포넌트당 하나, `using`으로 Dispose | 엔진 |
| 마이너: 중복 보고 | 비직렬화 필드는 엔진에서 제외 (분석기 경고만) | 엔진 |
| 마이너: 진행·취소 | `ScanPrefabs(folders, showProgress, out canceled)` 오버로드, 창에서 사용 | 엔진·창 |
| 마이너: 폴더 검증 | `AssetDatabase.IsValidFolder` | 창 |
| 마이너: MissingReference | **하지 않음.** 필요한 `SerializedProperty.objectReferenceInstanceIDValue`가 6.3 DLL엔 있으나 공식·XML 문서에 없음(obsolete/숨김 추정), 리플렉션은 에디터 fake null로 구분 불가. README에 "Missing도 NullSlot" 명시, enum 값은 공개 API라 유지 | README |
| 마이너: 씬·SO 미검사 | **유지** (README가 "프리팹"으로 일관, 설계 범위) | — |

- README: 패키지(인자·심각도 표·중첩 검사·테스트 asmdef 조건·빌드 차단 조건·컬렉션 범위), 분석기 원본(제네릭 제약·컬렉션 한정).
- 분석기 DLL: Release 빌드(경고 0) → 패키지 복사, SHA-256 앞 16자 `1d1efc56f21c16d9` 일치.
- 버전 1.0.0 유지 (배포 이력 없음).

### 계획과 달라진 것
- 계획은 표준 테스트 어셈블리(`UNITY_INCLUDE_TESTS` + TestRunner 참조 + `nunit.framework.dll` 명시)였다. 실측: 패키지의 테스트 어셈블리는 프로젝트 manifest `testables`에 패키지가 있어야만 컴파일됨(없으면 임포트만 되고 0건). 기존에는 설치만 하면 자동으로 Test Runner에 나왔으므로, **일반 Editor asmdef + `versionDefines`(`com.unity.test-framework` → `HB_TFH_HAS_TEST_FRAMEWORK`) + `defineConstraints`**로 바꿨다. 결정("asmdef로 분리")의 범위 안이라 묻지 않고 진행.
- 새 폴더·asmdef `.meta`는 수동 생성 (git 패키지는 읽기 전용이라 .meta가 없으면 무시됨). `SlotValidationTests.cs`는 `git mv`로 GUID 유지.

## 5. 테스트 (ToolTest6.3, `file:` 로컬 패키지, 모두 exit 0 또는 의도한 실패)
- 엔진 (`TFHCheckRunner`): Error — `_single`, 상속 private `_baseSlot`, `_inner._nested`, `_inners[0]._nested`, 순환 타입 `_cycle.a._slot`, `SystemGameplay` namespace `_gameplay._slot` / Warning — `_list[0]`, `_array[0]` / 미보고 — `warnNullElements:false` 목록, 빈 목록, 비직렬화 필드. (중간에 MonoBehaviour를 다른 이름 파일에 넣은 테스트 실수로 Missing 스크립트가 잡혔고, 덕분에 `MissingComponent` 검출도 확인)
- 분석기: `GenericSlot<T> where T : Component` 컴파일 통과 / `SelfRef<T> where T : List<T>` → InvalidType 에러, 크래시 없음 / `HashSet<GameObject>`·`GameObject[][]`·`GameObject[,]` → InvalidType 에러 3건.
- 테스트 asmdef: `testables` 없이 `HB.TestFrameworkHelper.Tests.Editor.dll`에서 1건 실행, Error 4건으로 실패·경고 2건 `Debug.LogWarning` / 조건 패키지를 없는 이름으로 바꾸면 테스트 어셈블리만 빠지고 Editor·Runtime·BuildIntegration 컴파일 (확인 후 원복) / .meta 누락 경고 없음.
- HB_Build(git `#d62d14f…`)와 함께 BuildIntegration 컴파일.
- 정리: `Assets/TFHCheck/` 삭제, manifest·lock 백업 복원(해시 `48b5d225…`/`4931fbfa…` 일치), 다시 열어 exit 0, PackageCache com.hb는 ingameconsole·screenrecorder만.

## 6. 외부 리뷰 (review, runId df3a9a49f8, gpt_main_sol)
- [major] `HasTargetFields`가 순환 탐색 중 잠정 false를 확정 캐시 → 슬롯 누락 → **채택.** 루트별로 방문 집합 BFS로 도달 여부를 계산해 확정값만 캐시, 깊이 인자 제거(깊이는 `CheckFields`가 끊음). 순환 시나리오로 확인.
- [major] 분석기 `where T : List<T>`에서 무한 재귀(스택 오버플로) → **채택.** 이번에 넣은 제네릭 처리에서 생긴 문제. 방문 집합으로 해결, 확인.
- [major] 분석기가 엔진이 검사 못 하는 컬렉션을 허용 → **채택.** 1차원 배열·`List<T>`로 한정, 확인.
- [minor] namespace 접두사 비교로 `SystemGameplay` 등 사용자 타입 제외 → **채택.** `.` 경계 비교, 확인.
- 자체 리뷰 관찰 중 1(순환 캐시)은 리뷰가 실제 결함으로 짚어 고침. 나머지(직렬화 판별 근사, MonoBehaviour만, 자동 등장 테스트, 특수 제약 무시)는 수용.

## 7. 남은 것
- 창 UI(심각도 색, 취소 안내, 폴더 검증 대화상자)는 배치로 확인하지 않음 → 에디터에서 확인 필요.
- 커밋 안 함: ToolCreate(패키지·docs), ToolWithCS(분석기·README).

## 8. 사용자 테스트 후 수정
- 사용자 보고: 배열 슬롯이 비었는데 검사가 연결된 것으로 판단하는 듯. 원인: 사용자 `Test.cs`의 `a4`가 `warnNullElements: true`라 빈 원소는 Warning, 테스트 실패 메시지에는 Error(`a1`·`a3`)만 적히고 Warning은 `Debug.LogWarning`(콘솔)에만 나와 Test Runner 결과 화면에서 안 보였음. 프리팹 YAML은 `a4`·`a5` 모두 `{fileID: 0}` ×3 (씬 오브젝트 링크는 프리팹에 저장되지 않음, 확인).
- 수정: 테스트 결과 메시지에 Warning 목록을 함께 적음 (실패 시 "테스트 실패 사유 아님" 절, 통과 시 `Assert.Pass` 메시지). Warning이 테스트를 실패시키지 않는 규칙은 유지.
- 확인: ToolCreate(7000.0.0a8) 배치 컴파일 exit 0, `HB.TestFrameworkHelper.Tests.Editor.dll` 생성. ToolTest6.3은 사용자가 열어 두어 실행하지 않음.
