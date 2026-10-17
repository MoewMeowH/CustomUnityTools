# HB_E2String 패키지 점검 결과 확인과 수정

## 요약
2026-10-18 점검에서 나온 1~6번과 마이너 항목을 순서대로 처리한다. 먼저 ToolTest6.3(6000.3.23f1)에 git URL로 설치해 1·3·4·5·6번을 실제로 확인한다.
수정 방향을 사용자가 정해야 하는 항목(특히 1번 어트리뷰트 위치)에서는 멈추고 묻는다.
결과는 `docs/10_18/worklog_Estring.md`에 정리한다.

## 배경
- 점검 대상: `Assets/Tools/HB_E2String`(패키지)과 원본 생성기 `F:\AI\ToolWithCS\EString\Script\E2StringGenerator.cs`.
- 패키지 DLL은 원본 Release 빌드와 SHA-256이 같다 (확정).
- 점검 항목 (번호는 점검 보고 순서):
  1. 기본 위치(`Assets/Scripts/E2String`)에 만든 어트리뷰트는 `Assembly-CSharp`에 들어가므로, asmdef 어셈블리의 enum에서 쓸 수 없다.
  2. 어트리뷰트가 이미 있으면 매니저가 생성 UI를 숨겨, README의 "다시 생성(덮어쓰기)"이 불가능하다.
  3. 빈 Runtime asmdef 때문에 "will not be compiled" 경고가 뜬다 (이 프로젝트 Editor.log로 확정).
  4. UPM 설치 시 분석기 적용 범위가 확인되지 않았다 (문서상 기본 어셈블리만, 이 프로젝트 `Assets/` 배치에서는 전체 어셈블리).
  5. 이름이 같은 enum 두 개면 `To{Enum}Fast` 확장 호출이 모호하다 (CS0121, 추론).
  6. 대소문자만 다른 hintName이면 생성기 전체가 멈출 수 있다 (추정).
  - 마이너: 창 IsFlags 인식, 창 목록 범위, 첫 스캔 동기 I/O, 네임스페이스 키워드 검사, 생성 코드 `global::`, 캐시 키의 Location, CHANGELOG·LICENSE.

## 계획
1. ToolTest6.3 실제 확인
   - `Packages/manifest.json`, `packages-lock.json`을 백업한다 (그 프로젝트는 git 저장소가 아님).
   - `com.hb.e2string`을 커밋 `10c3f80`의 40자 해시로 고정해 추가한다.
   - `Assets/E2StringCheck/`에 테스트 스크립트를 넣는다:
     - 어트리뷰트를 `Assembly-CSharp`에 둔 경우 / asmdef(`E2Check.Attr`)에 둔 경우
     - enum: Assembly-CSharp, asmdef 어셈블리(`E2Check.Game`)
     - 같은 이름 enum(5번), 대소문자만 다른 네임스페이스(6번)
   - 배치 모드로 열어 컴파일하고, 로그(에러·경고)와 `Library/Bee/artifacts/**/*.rsp`(EString.dll이 붙은 어셈블리)를 본다.
   - 시나리오마다 컴파일 에러가 서로를 가리지 않도록 나눠서 돌린다.
   - 끝나면 테스트 폴더를 지우고 manifest·lock을 되돌린 뒤 다시 열어 기준선과 비교한다.
2. 결과에 따라 항목별 수정. 패키지(`Assets/Tools/HB_E2String`)와 원본 생성기(`ToolWithCS`) 양쪽에 걸친다. 생성기를 고치면 Release로 다시 빌드해 DLL을 복사하고 해시를 맞춘다.
3. 버전은 이번 작업 범위에서 따로 정한다 (현재 1.0.0, 배포 이력 없음).

## 버린 대안과 이유
- 이 프로젝트(Unity 7 alpha)에서만 확인: 패키지 위치(`Packages/`)와 6.3 동작이 확인되지 않는다.
- 테스트를 한 번에 몰아서 돌리기: 한 시나리오의 컴파일 에러가 다른 어셈블리 컴파일을 막아 결과가 가려진다.

## 위험
- git 패키지 resolve가 Hub 상태에 따라 멈춘 적이 있다 (2026-10-17 관측). 타임아웃으로 끊는다.
- ToolTest6.3의 Library가 바뀐다. 마지막에 원래 manifest로 다시 열어 정리한다.

## 테스트
- ToolTest6.3 배치 컴파일 (위 시나리오).
- 생성기를 고치면 고친 항목마다 같은 시나리오로 다시 확인한다.

## 실제 확인
- 2번(창 UI)은 배치 모드로 확인하지 않는다. 고친 뒤 에디터에서 사용자 확인이 필요하다.

## 사용자에게 물을 것
- 1번 수정 방향 (테스트 결과를 보고 선택지를 제시).
- 결과 문서 경로: 요청은 `doc/10_18/worklog_Estring.md`이고, 기존 관례에 맞춰 `docs/10_18/`에 둔다.

## 결정
> ㅇㅋㅇㅋ 1번부터 마이너까지 순서대로 전부 진행해줘 의견이 필요하면 멈추고 말해주고 doc/10_18/worklog_Estring.md 로 정리해줘

- 바뀐 계획 항목: 없음 (요청 자체가 이 계획의 출발점).

## 결정 2 (1번 수정 방향)
> 매니저가 asmdef도 생성 (Recommended)
>
> 이러면 사용하는 프로젝트가 asmdef 를 안쓰는 상황에서도 서버와 호환을 위해 따로 빼놔도 잘 작동해? 반대에 경우에서도 서버와 호환이 되고?

- 바뀐 계획 항목:
  - 1번: 매니저가 어트리뷰트 옆에 `HB.E2String.Attributes.asmdef`(autoReferenced, noEngineReferences)를 같이 만든다.
    - 대상 폴더가 이미 asmdef/asmref에 속하면 만들지 않는다 (그 어셈블리를 쓴다).
    - 폴더 아래 다른 스크립트가 새 어셈블리로 옮겨지게 되면 개수를 알리고 묻는다.
    - 같은 이름 asmdef가 이미 있으면 만들지 않고 알린다.
    - 창에 어트리뷰트가 속한 어셈블리를 보여주고, Assembly-CSharp면 asmdef에서 쓸 수 없다고 안내한다.
  - 3번: 빈 Runtime asmdef 삭제, Editor asmdef의 참조 제거.
- 질문 답 (테스트 근거): asmdef 없는 프로젝트 = `E2Check.Attr` + Assembly-CSharp `CsState` 컴파일 통과, asmdef 프로젝트 = `E2Check.Game`→`E2Check.Attr` 통과. `.asmdef`는 Unity 전용이라 서버 빌드에 영향 없음.

## 결정 3 (5번 수정 방향)
> 경고 진단만 추가 (Recommended)

- 바뀐 계획 항목:
  - 5번: 생성 API 이름은 그대로 둔다. 같은 namespace에서 `To{Enum}Fast` 이름이 겹치는 enum마다 경고 `E2S005`를 낸다 (정적 호출 안내). 다른 namespace끼리 겹치는 경우는 README에만 적는다.
  - 6번(사용자 결정 불필요로 판단): hintName이 대소문자만 다르게 겹치는 enum은 hintName 끝에 ClassKey의 FNV-1a 해시를 붙여 둘 다 생성한다 (C#에서는 서로 다른 클래스이므로 거절할 이유가 없음).

## 결정 4 (마지막 우려 사항 · 작성자 표시)
> 1번은 아직 필요없어, 일단 내가 작성하긴 했지만 뭐 할필요가 읎엉 나는 에디터 열어서 확인해볼테니 너는 이제 우려되는 사항 마지막으로 살펴봐줘

> ㅇㅋ 3번은 다시 생성하고 컴파일 날리면 경고 안남아 그리구 Tools 쪽 패키지 받은게 이름들이 하트비트로 표시되는대 이름과 메일은 EString 패키지 대로 해줘

> 음 테스트 끝나면 돌려주고 Tools 에서 UPM 공유할때 쓰는 json 에 바꿔서 채워줘 에디터 확인은 끝났어

- 바뀐 계획 항목:
  - CHANGELOG·LICENSE: 넣지 않음.
  - 마지막 점검의 우려 1·2·4 수정: 다시 생성은 매니저가 만든 파일(헤더)만, 다른 스크립트가 옮겨지는 폴더에는 asmdef를 만들지 않음, 다시 생성 옵션을 기존 파일에서 채움. 우려 3(어셈블리 표시)은 사용자 확인으로 문제 없음.
  - Tools 패키지 json: 작성자는 6개 모두 이미 E2String과 같음. TestFrameworkHelper description의 "HeartBeat" 제거.
  - ToolTest6.3은 백업으로 되돌림 (InGameConsole·ScreenRecorder의 예전 커밋 고정 포함 그대로).
