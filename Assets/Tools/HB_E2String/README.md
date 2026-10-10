# HB E2String

Zero-Allocation Enum to String Roslyn Source Generator 및 전용 에디터 매니저 툴입니다.

## 주요 기능
- **Roslyn Source Generator (`EString.Script.Generator.dll`)**:
  - `[E2String]` 또는 `[EnumToString]` 어트리뷰트가 붙은 모든 Enum에 대해 무할당(Zero-Allocation) 확장 메서드 클래스(`_Extensions.g.cs`)를 자동 생성합니다.
  - `ToStringFast()`, `ToEnumFast()`, `TryParseFast()`, `ToIntFast()`, `IsDefined()`, `AllNames`, `AllValues` 지원.
- **전용 에디터 윈도우 (`Tools/HB/E2String Manager`)**:
  - 프로젝트 내 어트리뷰트가 적용된 모든 Enum 목록 실시간 시각화 (멤버 목록, 파일 경로, 빠른 점프).
  - 어트리뷰트 스크립트(`E2StringAttribute.cs`) 존재 여부 자동 진단 및 원하는 경로(서버 공유 폴더 등)로 원클릭 생성.
  - 글로벌 네임스페이스 또는 커스텀 네임스페이스 선택 지원.

## 메뉴 경로
- Unity Editor 상단 메뉴: `Tools` > `HB` > `E2String Manager`
