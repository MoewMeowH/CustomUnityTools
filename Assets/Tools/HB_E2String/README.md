# HB_E2String

`Enum.ToString()` 호출 시 발생하는 박싱(Boxing) 및 문자열 할당을 제거하기 위한 Roslyn Source Generator 및 에디터 관리 도구입니다.

## 주요 기능
- **Roslyn Source Generator (`EString.dll`)**:
  - `[E2String]` 어트리뷰트가 선언된 Enum을 컴파일 타임에 탐색하여 상수 문자열 리터럴 `switch` 식 기반의 `ToStringFast()` 및 문자열 역변환 확장 메서드를 자동 생성합니다.
- **에디터 매니저 윈도우 (`Tools > HB > E2String Manager`)**:
  - 프로젝트 내 `[E2String]`이 적용된 모든 Enum 목록과 멤버, 선언 위치를 스캔하여 표시합니다.
  - 프로젝트 내 `E2StringAttribute.cs` 존재 여부를 확인하고, 서버 공유 디렉터리 등 지정한 경로에 어트리뷰트 스크립트를 생성하는 기능을 제공합니다.
