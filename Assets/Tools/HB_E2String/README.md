# HB_E2String

`Enum.ToString()`, `Enum.Parse()`, `Enum.IsDefined()`, `Enum.GetValues()` 호출 시 발생하는 리플렉션·박싱(Boxing) 및 힙 할당을 제거하기 위한 Roslyn Source Generator(`EString.dll`) 및 에디터 관리 도구입니다.

---

## 📦 UPM 패키지 설치 (Installation)

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_E2String
```

---

## 주요 기능
- **Roslyn Source Generator (`E2StringGenerator`)**:
  - `[E2String]` 어트리뷰트가 선언된 Enum을 컴파일 타임에 탐색하여 `{EnumName}_Extensions.g.cs` 파일을 자동 생성합니다.
  - **상수 및 캐시 컬렉션**: `Count`, `Names`(`string[]`), `AllNames`(`IReadOnlyList<string>`), `Values`(`T[]`), `AllValues`(`IReadOnlyList<T>`)
  - **Enum <-> String 변환**: `switch` 식 기반의 `ToStringFast(fallback)`, `To{EnumName}(fallback)`, `TryParseFast(value, out result, fallback)`
  - **Enum <-> Int 변환**: Unity 2021.3 이상에서는 `UnsafeUtility.As<T, int>`를, 그 외 환경에서는 명시적 캐스팅을 사용하는 `ToInt()`, `ToIntFast()`, `To{EnumName}(int, fallback)`, `To{EnumName}Fast(int)`
  - **유효성 검사**: `switch` 식 기반의 `IsDefined(T value)` 및 `IsDefined(int value)`
- **에디터 매니저 윈도우 (`Tools > HB > E2String Manager`)**:
  - 프로젝트 내 `[E2String]`이 적용된 모든 Enum 목록과 멤버, 선언 위치를 스캔하여 시각화합니다.
  - 프로젝트 내 `E2StringAttribute.cs` 존재 여부를 확인하고, 서버 공유 디렉터리 등 `Assets` 하위 경로에 어트리뷰트 스크립트를 생성하는 기능을 제공합니다.

## 사용 예시

```csharp
[E2String]
public enum CharacterState
{
    Idle,
    Move,
    Attack,
    Die
}

// 1. 제로 할당 문자열 변환
string stateName = CharacterState.Move.ToStringFast(); // "Move"

// 2. 빠른 파싱
if (CharacterStateExtensions.TryParseFast("Attack", out CharacterState result))
{
    // result == CharacterState.Attack
}

// 3. 캐시된 값 순회 (GC Alloc 0)
foreach (var name in CharacterStateExtensions.AllNames)
{
    // ...
}
```
