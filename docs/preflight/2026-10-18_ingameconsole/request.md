# 요청 원문 (preflight)

## 대상
Unity UPM 패키지 `HB_InGameConsole` (Unity 6.3+, git URL로 다른 프로젝트에 배포 예정, 배포 이력 없음). 런타임 인게임 디버그 콘솔(UI Toolkit) + HLog 로깅·Watch.

## 요청
점검·교차 리뷰로 확인된 결함을 고친다. 수정안은 `docs/plans/2026-10-18_ingameconsole-package-fix.md`(계획서)에 있다. 이 계획과 현재 코드를 검토해, 계획대로 고쳤을 때 남는 결함·빠진 것·더 나은 구조가 있는지 본다.

## 확인된 문제 (실측·코드 재확인)
1. 백그라운드 스레드에서 새 채널(`HLog.Get("새 이름")`) → `OnTagDiscovered`가 호출 스레드에서 발생 → 인게임 UI가 버튼 생성 → `UnityException: VisualElementCreation can only be called from the main thread`가 호출자에게 던져짐 (Unity 6.3 PlayMode 실측).
2. 출시(비개발) 빌드에서도 자동 초기화되어 콘솔 UI·로그 수집이 동작.
3. 콘솔이 닫혀 있어도 로그마다 UI 행(VisualElement 5개+람다) 생성, 최대 1000줄. 에디터 실측 로그 1줄당 GC 할당 ≈9KB 추가. 코어·UI 모두 상한 후 `RemoveAt(0)`.
4. Watch: 다른 스레드에서 `Watch(key, UnityObject)`가 `uObj.name`(메인 전용) 접근 / 워커 갱신은 큐·메인 갱신은 즉시라 순서 역전, `Unwatch` 뒤 부활 / 이벤트 큐 상한 없음 / 폴링·Unwatch 경합 / 게터 폴링이 UI `Update`에만 있어 UI 없으면 갱신·owner 해제 멈춤 / 개수 표시마다 Dictionary 전체 복사.
5. 기타: 도메인 리로드 끈 상태에서 로그 OFF가 다음 플레이로 이어짐, 다른 스레드 로그 처리에 프레임당 상한 없음, 여러 줄 로그 태그 파싱 실패, 워커 로그 시각이 처리 시점, 채널 `Exception()`이 채널 태그를 잃음, 설정 에셋을 에디터는 어디서든·플레이어는 Resources에서만 찾음, `Watches`가 가변 엔트리 공유, README 예제 API 불일치(`LogWarning`/`LogError` 없음), 메뉴 경로 불일치, `LogChannels.cs` 자동 생성이 지워도 매번 재생성.

## 사용자 결정 (원문)
> 1. 응 에디터와 Development Build 에서 허용하고 설정체크는 Manual Init 으로 처리 가능할까? 2. 응 메모리 생각하면 그게 좋겠다. 3. 아마 저거 Log 만 빠졌을거야 README 만 지금 코드로 최신화 해줘 4. 한번만 하자 5. 지금 구조에 문제가 많으니 수정하자 preflight 로 한번 돌려보자

> 알겠어. 어차피 adb 나 우회로 로그는 확인 가능하니까 InGameConsole 을 키는것만으로는 로그만 확인 가능하니 위험성이 있긴하지만 크리티컬하다고 생각하지 않아서 괜찮을거같아

> 아 그리고 다시 수정할게 DevelopmentBuild 에서만 활성화 하자 위험성 그냥 빼는게 좋다

(1 = 최종: 에디터와 Development Build에서만 동작, 릴리즈 빌드는 직접 Initialize해도 동작하지 않음(조건부 컴파일로 동작 코드 제거, 공개 API는 유지). 2 = 닫힌 콘솔은 행을 만들지 않고 열 때 생성 + 코어 링 버퍼. 3 = API는 그대로, README만 실제 코드로. 4 = 자동 생성 프로젝트당 1회. 5 = Watch 구조 변경.)

## 제약
- 외부 사용 API 유지: HB_Cheat가 `InGameConsole.Initialize / IsInitialized / AddExternalView / RemoveExternalView / ShowExternalView / IsExternalViewActive / Close` 사용. 에디터 콘솔 창이 `HLogCore.Watches / History / OnWatchUpdated / OnLogAdded / OnTagDiscovered` 사용. 생성 코드(`HLog` 정적 partial 클래스, 사용자 프로젝트에 이미 생성됨)가 `HLogCore.Watch/Unwatch/GetChannel/Initialize/Shutdown/Clear`, `LogChannel` 생성자·메서드를 호출.
- Unity 6.3 ~ 7 동작. `UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER` 분기 유지.
- 게임 코드는 Task/Job 등 다른 스레드에서 로그·HLog.Get·Watch를 호출할 수 있다. 모바일 대상 포함.
- 패키지에 YAML 에셋(.asset/.prefab)을 넣지 않는다(상위 Unity 버전 직렬화 호환).
