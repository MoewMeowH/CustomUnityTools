# HB_TestFrameworkHelper 패키지 점검 결과 수정

## 요약
점검(`docs/10_18/worklog_TestFrameworkHelper.md`)에서 확인한 A(제네릭 슬롯 오탐), B(List·배열·중첩 슬롯 미검사), C(test-framework 없는 프로젝트에서 패키지 전체 컴파일 실패)와 마이너를 고친다.
B는 배열/List null 원소를 경고로만 보고하고 어트리뷰트 인자(기본 true)로 끌 수 있게 한다. C는 테스트를 별도 asmdef로 분리한다.
패키지(ToolCreate)와 분석기 원본(ToolWithCS) 두 저장소에 걸친다.

## 배경
- 실제 확인(ToolTest6.3, 6000.3.23f1): A·B 재현, 분석기 asmdef 적용·CodeFix 동봉 로드·HB_Build 연동·패키지 테스트 발견은 문제 없음.
- C는 추론: NUnit이 test-framework(ext.nunit)의 자동 참조 DLL로만 들어온다.

## 계획
1. **어트리뷰트** (`Runtime/NeedSlotNullCheckAttribute.cs`): 생성자에 `bool warnNullElements = true` 추가(뒤에 붙여 기존 호출 호환), `WarnNullElements` 속성.
2. **엔진** (`SlotValidatorEngine.cs`):
   - 이슈에 `Severity`(`Error`/`Warning`) 추가, 이슈 종류 `NullElement` 추가.
   - 검사를 "필드 선언 타입 = MonoBehaviour 계열" 조회에서, MonoBehaviour 타입의 **직렬화 필드 그래프를 따라가는** 방식으로 바꾼다. 중첩 `[Serializable]` 클래스·구조체(배열/List 원소 포함)까지 내려간다. 깊이 상한 10 (Unity 직렬화 깊이 상한과 같음).
   - 단일 슬롯: null → `NullSlot`(Error). 배열/List 슬롯: null 원소마다 `NullElement`(Warning), `WarnNullElements == false`면 보고 안 함. 빈 목록은 문제 아님.
   - 직렬화되지 않는 필드는 검사 대상에서 뺀다 (분석기 경고와 중복이던 것).
   - 타입별 "검사 대상 필드가 그래프 안에 있는가"를 캐시해 대상 없는 MonoBehaviour는 건너뛴다.
   - `SerializedObject`는 `using`으로 Dispose, 컴포넌트당 하나.
   - 진행 표시·취소: `ScanPrefabs(folders, showProgress, out canceled)` 오버로드. 창만 사용, 테스트·빌드는 기존 시그니처(진행 표시 없음).
3. **테스트 분리** (C): `Editor/Tests/HB.TestFrameworkHelper.Tests.Editor.asmdef` — `defineConstraints: UNITY_INCLUDE_TESTS`, `overrideReferences` + `nunit.framework.dll`, `UnityEngine.TestRunner`·`UnityEditor.TestRunner` 참조. `SlotValidationTests.cs`를 `git mv`(GUID 유지). 새 폴더·asmdef의 `.meta`는 직접 만든다 (git 패키지는 읽기 전용이라 .meta가 없으면 Unity가 무시함).
   - 테스트는 Error 이슈만 실패, Warning은 `Debug.LogWarning`.
4. **빌드 연동**: LIVE 차단은 Error 이슈만, Warning은 로그만.
5. **창**: 이슈 심각도 표시(`NullElement` 라벨), 검사 폴더를 `AssetDatabase.IsValidFolder`로 검증, 진행 표시·취소 사용.
6. **분석기** (A, ToolWithCS): `ITypeParameterSymbol`이면 제약 타입 중 하나라도 `UnityEngine.Object` 계열이면 통과. 제약 없는 `T`는 지금처럼 에러. Release 빌드 → 패키지 DLL 복사, 해시 확인.
7. README(패키지·원본): 인자, 경고/에러 구분, 중첩·배열 검사, 테스트 asmdef, Missing 참조가 `NullSlot`으로 보고되는 한계.

## 버린 대안과 이유
- Missing 참조 구분(`MissingReference`): 필요한 `SerializedProperty.objectReferenceInstanceIDValue`가 6.3 DLL에는 있지만 공식 문서·XML 문서에 없음(obsolete/숨김 추정). 리플렉션 값은 에디터의 fake null 때문에 미할당과 구분이 안 됨. → 이번엔 하지 않고 README에 한계로 적는다. enum 값은 공개 API라 지우지 않는다.
- 전역 설정 플래그: 사용자 결정으로 어트리뷰트 인자.
- package.json에 test-framework 의존성: 사용자 결정으로 asmdef 분리.

## 위험
- 엔진 재작성이 기존 검출(단일 필드)을 깨뜨릴 수 있음 → 같은 시나리오 + 상속 필드 시나리오로 재확인.
- 새 .meta를 수동 생성 → git URL 설치에서 경고 없이 임포트되는지 확인.

## 테스트
- ToolTest6.3: 단일/List/배열/중첩/중첩 배열/상속 필드/`warnNullElements: false` 프리팹 스캔, 제네릭 `T : Component` 컴파일, 테스트 asmdef로 Test Runner 실행(Error만 실패), 빌드 연동 컴파일.
- 로컬 `file:`로 확인 후, 커밋·푸시 전이라 git URL 재확인은 하지 않는다 (.meta는 file: 단계에서 "no meta file" 경고 여부로 확인).

## 사용자에게 물을 것
- 없음 (결정 반영).

## 결정
> 배열슬롯 검사는 근대 warning 만 출력하고 bool 플래그 하나로 warning 출력할지 명시하는 방향으로 가자 기본값은 true (로그 활성화)

> 어트리뷰트 인자로 하자 2번은 asmdef 로 분리하자

- 반영: 계획 1·2(배열/List null 원소는 Warning, 어트리뷰트 인자 `warnNullElements` 기본 true), 3(테스트 asmdef 분리).
