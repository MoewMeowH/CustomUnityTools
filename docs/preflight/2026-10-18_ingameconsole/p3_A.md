## 1. 상대 Phase 2 판정에 대한 응답

아래는 설계 검토 결과다. 실제 코드 실행·컴파일·성능 측정은 하지 않았다.

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| A1-01 판정·B2-01 | 수용 | **A3-01.** 워커 Watch와 `PendingWatch`는 제외한다. 다만 메인 스레드 게터의 재진입을 막는 등록 토큰은 여전히 필요하다. |
| A1-02 판정 | 수용 | **A3-02.** 게터가 자기 해제·교체·Clear·Shutdown을 수행하면 복사된 목록은 이미 무효다. 평가 전후와 문자열 변환 후에 등록·수명주기를 재검사한다. |
| A1-03 판정 | 부분 수용 | **A3-03.** 고유 태그와 전달 대기량의 증가 가능성은 유효하다. 다만 수정 후에도 반드시 범용 `Action` 큐가 남는다는 것은 미확인이다. 전용 태그 큐로 바꾸되 캐시 상한은 임의로 추가하지 않는다. |
| A1-04 판정 | 수용 | **A3-04.** `USE_HLOG`만 사용한다. 씬 배치 컴포넌트와 외부 뷰 API까지 OFF 경로를 점검한다. |
| A1-05 판정 | 수용 | **A3-05.** Clear는 대기 로그도 폐기한다. 단순 큐 비우기만으로는 이미 인출한 로그와 진행 중인 생산자를 막지 못하므로 세대 검사를 함께 적용한다. |
| A1-06 판정 | 부분 수용 | **A3-06.** 이전 태그 복원과 수신 시점 스냅샷은 필요하다. 그러나 콜백의 동기·동일 스레드 실행 및 중첩 무관 로그의 구분은 아직 검증되지 않았다. |
| A1-07 판정·B2-03 | 부분 수용 | **A3-07.** 확정 7에 따라 닫을 때 기존 행을 유지한다. 기존의 닫을 때 행·선택 참조 해제 제안은 수정한다. `selectedRow`는 해당 행 제거·재구성 때 정리하며, 닫기만으로 반드시 비우지는 않는다. |
| A1-08 판정 | 수용 | **A3-08.** 재바인딩 전 기존 요소와 기존 델리게이트로 해제한다. 실제 중복 발생 여부는 동일 루트·새 루트 시험으로 확인한다. |
| A1-09 판정 | 수용 | **A3-09.** 캐시 무효화 필요성은 유지하되 현재 구현의 결함으로 확정하지 않는다. 자료 부족: 에디터 창 전환·캐시 코드 전체. |
| A1-10 판정 | 수용 | **A3-10.** 확정 6으로 해소됐다. Git 공유 비-YAML 마커를 사용한다. |
| A1-11 판정·B2-04 | 수용 | **A3-11.** 게터와 이벤트의 예외 경계를 분리한다. 구독자 오류 진단은 코어 재수집을 차단하여 재귀·지연 오류 반복을 방지한다. |
| A1-12 판정 | 수용 | **A3-12.** Shutdown은 수집·평가·배출을 모두 중단한다. 재초기화 이전 콜백·배출 작업도 세대로 무효화한다. |
| PF1-01 판정·B2-02 | 부분 수용 | **A3-13.** 고정 건수 예산과 전용 태그 큐를 채택한다. 다만 `Stopwatch` 비용·비결정성만으로 시간 예산이 일반적으로 위험하다고 결론 내릴 근거는 없다. 건수 상한은 실행 시간 상한이 아니다. 메인 즉시 로그 의미는 보존한다. |
| PF1-02 판정 | 부분 수용 | **A3-14.** 무제한 고유 이름의 비용은 남는다. 닫힌 Watch·태그 UI까지 지연하는 변경은 확정된 로그 행 정책과 별개인 추가 최적화이므로 이번 필수 변경에는 넣지 않는다. |
| PF1-03 판정 | 수용 | **A3-15.** 제거된 키의 Watch 행만 제거한다. 실제 이벤트 인자는 자료 부족이므로 기존 시그니처를 유지하면서 내부 키 변경 집합으로 처리할 수 있다. |
| PF1-04 판정·B2-05 | 부분 수용 | **A3-16.** 박싱 잔존과 API 확장 회피는 타당하다. 그러나 임의 객체에 `Equals`를 먼저 적용해 문자열 변환을 생략하는 대안은 안전하지 않다. 동일 객체의 내부 상태·Unity 객체 이름 변경을 놓칠 수 있어 채택하지 않는다. |
| PF1-05 판정 | 부분 수용 | **A3-17.** 초기화·패널 재로드의 지연 생성은 적용한다. 닫을 때 기존 행을 유지하는 것은 확정된 메모리·재열기 비용 절충이며 누수로 단정하지 않는다. |
| PF1-06 판정 | 수용 | **A3-18.** UI 선두 배열 이동을 제거한다. 최초 일괄 생성 비용은 남으며 기기 측정이 필요하다. 분할 생성·가상화는 이번에 도입하지 않는다. |
| PF1-07 판정·B2-06 | 부분 수용 | **A3-19.** 깊은 복사의 비용은 타당하지만 실제 GUI별 조회 빈도·심각도는 미확인이다. 공개 API 추가 대신 기존 이벤트에 따른 에디터 내부 캐시 무효화를 우선한다. |

## 2. 최종 수정안

### A3-20. 적용 범위와 파일별 변경

공개 타입·메서드·필드의 기존 선언은 유지한다. 다음 표의 새 이름은 별도 표시가 없는 한 **비공개 구현용**이며, 실제 원본 이름과 충돌하면 구현자가 조정한다.

자료 부족: 대상 7개 파일 원문·정확한 경로, 공개 반환 타입, 생성 템플릿 전체, 기존 생성 파일 표본, 어셈블리 구성. 따라서 아래 코드는 완성 파일이 아닌 핵심 구조 스케치다.

| 대상 파일 | 변경 |
|---|---|
| `HLogCore.cs` | 단일 심볼 분기, 메인 스레드 검사, 수집 세대·대기 큐 동기화, 코어 링 버퍼, 태그 전용 전달, 공유 배출 예산, Watch 등록 토큰, 예외 경계, 안전한 스냅샷 |
| `LogChannel.cs` | 모든 지정 출력·Watch 메서드의 단일 Conditional, OFF 생성자의 무동작화, 예외 태그 범위 저장·복원 |
| `InGameConsole.cs` | 자동·수동 초기화와 외부 뷰 API의 심볼·스레드 검사, OFF 반환 계약, 종료된 코어의 암묵적 재시작 방지 |
| `InGameConsoleSettings.cs` | 기존 Resources 경로로 검색 통일, 에디터의 다른 위치 에셋 경고, 직렬화 필드명 유지 |
| `HB_InGameConsoleUI.cs` | 로그 행 지연 생성·닫힘 유지·재열기 동기화, UI 행 큐, 선택 참조 정리, 기존 콜백 해제, Watch 단건 제거 |
| `HLogConsoleWindow.cs` | OFF 표시, 플레이 전환 캐시 무효화, 이벤트 기반 Watch 스냅샷 캐시, 비플레이 배출 경로 정리 |
| `LogChannelGeneratorWindow.cs` | 커스텀 Conditional 설정 제거, 템플릿 변경, 프로젝트 공유 마커, 성공 이후 완료 기록 |
| `README` | 실제 API·메뉴·스레드 계약·활성 정책·재생성·상한과 비용 명시 |

생성되는 `LogChannels.cs`도 재생성 대상이다. 기존 에셋·템플릿의 실제 위치는 원본 확인 후 변경 목록에 추가한다.

### A3-21. 활성 조건과 공개 API 호환성

- 런타임 동작 구현은 `#if USE_HLOG`로만 포함한다.
- `UNITY_EDITOR`는 에디터 전용 기능 구분에만 사용하고 활성 조건에는 사용하지 않는다.
- 모든 지정 로그·Watch 오버로드에 `[Conditional("USE_HLOG")]`를 적용한다. 생성 템플릿도 동일하다.
- `BreakIf`·`Assert`·`ReportIf`는 같은 Conditional을 붙이고 몸체만 `#if UNITY_EDITOR`로 제한한다.
- Conditional이 있는 메서드도 OFF 몸체는 비운다. 호출자 어셈블리의 심볼 불일치·반사 호출 등으로 런타임 구현이 실행되지 않게 하기 위한 비활성 구현이다. 별도의 빌드 종류 차단은 추가하지 않는다.
- 생성자·반환값 API에는 Conditional을 붙이지 않는다.
- OFF의 컬렉션은 기존 선언 타입에 맞는 읽기 전용 빈 객체, 객체 반환은 `null`, 상태·개수는 비활성 값을 반환한다. 채널 반환도 확정된 `null` 계약을 따른다.
- OFF에는 로그 수집 구독, PlayerLoop 설치, 리플렉션 탐색, UI 생성, Watch 평가를 하지 않는다.
- 에디터 창·생성기·기존 툴바 버튼은 심볼과 무관하게 존재한다.

```csharp
[System.Diagnostics.Conditional("USE_HLOG")]
public void Error(object message)
{
#if USE_HLOG
    // 기존 시그니처와 출력 방식을 유지한다.
    WriteError(message);
#endif
}

[System.Diagnostics.Conditional("USE_HLOG")]
public static void BreakIf(bool condition)
{
#if UNITY_EDITOR
    // 기존 에디터 동작을 유지한다.
    BreakIfInEditor(condition);
#endif
}
```

위 시그니처는 속성 배치 예시다. 실제 오버로드·접근 제한·선택 인자는 원본대로 유지한다.

### A3-22. 스레드 계약과 최초 1회 진단

새 내부 구성:

| 타입·필드·메서드 | 역할 |
|---|---|
| `private enum MainThreadApi` | 검사 대상 API 식별 |
| `private static int s_MainThreadId` | 신뢰할 수 있는 메인 초기화 지점에서만 설정 |
| `private static int[] s_ThreadViolationReported` | API별 원자적 최초 진단 플래그 |
| `private static bool RequireMainThread(MainThreadApi api)` | 위반 거절·진단 |
| `[ThreadStatic] private static bool s_SuppressCoreCapture` | 내부 오류 보고의 코어 재수집 차단 |

- 에디터 메인 초기화 지점과 플레이어 `SubsystemRegistration`에서 스레드 ID를 설정한다. 정적 생성자·최초 공개 호출자를 기준으로 삼지 않는다.
- ID가 아직 없으면 Get·로그 외 API는 안전하게 거절한다. 워커를 메인으로 등록하지 않는다.
- Get·Channel·GetChannel과 로그 출력만 워커 허용이다. 나머지 공개 진입점은 Unity API 접근·상태 변경보다 먼저 검사한다.
- 읽기 API도 계약상 메인 전용인 경우 위반 시 비활성 반환값을 사용한다.
- 진단 플래그는 API 단위로 `Interlocked.CompareExchange`를 사용한다. **가정:** 오버로드는 메서드 이름 단위로 묶고 플레이 세션 리셋 때 초기화한다.
- **가정:** OFF의 빈 동작을 우선하여 위반 진단도 출력하지 않는다. Conditional로 제거된 호출은 검사에 도달하지 않는다.
- ON의 위반 진단은 Unity 로그에 출력하되 코어에는 재수집하지 않는다.

```csharp
private static bool RequireMainThread(MainThreadApi api)
{
    int mainThreadId = System.Threading.Volatile.Read(ref s_MainThreadId);
    if (mainThreadId != 0 &&
        System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId)
        return true;

    int index = (int)api;
    if (System.Threading.Interlocked.CompareExchange(
            ref s_ThreadViolationReported[index], 1, 0) == 0)
    {
        ReportInternalError("메인 스레드 전용 API 호출 위반: " + api);
    }

    return false;
}
```

에러 문자열 할당은 최초 실패 경로에만 발생한다. `ReportInternalError`는 이전 억제 상태를 `finally`에서 복원하고, 출력 핸들러 자체의 예외를 다시 로그로 보고하지 않는다.

### A3-23. 로그 수집·Clear·Shutdown의 선형화

새 내부 구성:

| 타입·필드·메서드 | 역할 |
|---|---|
| `private readonly struct PendingLog` | 원문·스택·종류·수신 시각·예외 태그·수집 세대 |
| `private static readonly object s_IngressGate` | 수집 허용·세대·대기 큐를 함께 보호 |
| `private static Queue<PendingLog> s_PendingLogs` | 짧은 잠금으로 관리하는 bounded queue |
| `private static long s_LogGeneration` | Clear·Shutdown·정적 리셋 경계 |
| `private static bool s_IsRunning` | 수집·평가·배출 허용 상태 |
| `private static void TickRuntime()` | 플레이 중 공통 처리 진입점 |
| `private static void FlushPendingLogsInternal()` | 공유 예산을 사용하는 내부 배출 |

처리 규칙:

1. Initialize는 메인에서만 수행한다. 중복 구독·중복 PlayerLoop 설치를 하지 않는다.
2. 로그 콜백 진입 시 잠금 안에서 실행 상태와 현재 세대를 접수한다.
3. 수신 시각과 태그는 콜백에서 캡처한다. 지연 처리 시각으로 바꾸지 않는다.
4. 워커 로그를 큐에 넣기 전 같은 잠금에서 실행 상태·세대를 재검사한다.
5. 큐 용량은 기존 상한을 유지하고 초과 시 기존 폐기 정책을 보존한다. 별도 원자 카운터는 제거하고 잠금 안의 `Count`만 사용한다.
6. 배출은 한 건씩 인출한다. 잠금 밖에서 파싱·이벤트를 수행하며, 기록 직전 세대를 다시 검사한다.
7. 메인 스레드 로그는 기존 즉시 반영 의미를 유지한다. 따라서 워커 배출 상한은 전체 로그 실행 시간 상한이 아니다.
8. Clear는 같은 잠금에서 세대를 증가시키고 대기 큐를 비운다. 코어 기록·집계·Watch 등 기존 Clear 대상도 유지한다. 태그를 불필요하게 재탐색하지 않는다.
9. Shutdown은 같은 잠금에서 실행 상태를 끄고 세대를 증가시키며 대기 로그를 폐기한다. 이후 구독·루프를 해제한다.
10. 이미 실행 중인 사용자 콜백을 강제 중단할 수는 없다. 복귀 후 이전 세대 결과와 추가 배출을 막는다.

```csharp
private static void EnqueuePendingLog(PendingLog pending)
{
    lock (s_IngressGate)
    {
        if (!s_IsRunning || pending.Generation != s_LogGeneration)
            return;

        // 기존 정책이 오래된 대기 로그 폐기인지 원본에서 확인한다.
        if (s_PendingLogs.Count == PendingLogCapacity)
            s_PendingLogs.Dequeue();

        s_PendingLogs.Enqueue(pending);
    }
}
```

스케치의 초과 정책은 **원본이 오래된 항목 폐기일 때** 적용한다. 자료 부족: 실제 초과 정책 전체.

Clear의 경계는 잠금 안에서의 세대 전환이다. 그 이전에 접수된 로그는 폐기하고 이후 접수된 새 로그는 허용한다. 로그 출력 함수의 호출 시작 시각까지 소급하여 분류하지 않는다.

Shutdown 이후 Watch 등록·조회 및 기존 기록 보유 의미는 원본을 보존한다. 다만 명시적 Initialize 전에는 UI 폴백·Flush·Poll이 수집이나 평가를 재시작하지 않는다.

### A3-24. 코어 이력과 태그 전달

- 이력은 용량 1000의 배열 링 버퍼로 한다.
- 추가 시 가득 찼으면 퇴출 항목의 집계를 먼저 감소시키고 새 항목 집계를 증가시킨다.
- `History`는 오래된 순서의 독립 스냅샷을 반환한다.
- 여러 줄 태그 파싱은 기존 정규식에 `RegexOptions.Singleline`을 적용한다.
- 채널 캐시는 워커 동시 접근을 보호한다. 채널 생성 중 Unity UI·메인 전용 API를 호출하지 않는다.
- `OnTagDiscovered`는 메인에서만 전달한다. 범용 캡처 `Action` 큐 대신 전용 태그 데이터 큐를 사용한다.
- 태그 이벤트의 전달 대기는 중복을 막는다. 구독자는 초기 바인딩 때 `KnownTags`로 전체 상태를 동기화한다.
- Clear는 기존 태그 보존 의미를 유지한다.
- Shutdown 동안 태그 이벤트를 배출하지 않는다. 재초기화 시 미전달 태그를 중복 없이 전달할 수 있도록 대기 상태를 유지하거나 재구성한다.
- 고유 태그·채널의 자동 퇴출이나 개수 제한은 추가하지 않는다.

### A3-25. 처리 주체와 예산

**내부 구현 상수 제안:** 워커 로그 프레임당 1000건, 태그 전달 프레임당 64건. 공개 설정·API는 추가하지 않는다.

- 모든 플레이 중 Flush 진입점은 프레임별 잔여 예산을 공유한다.
- 예산은 이벤트 호출 **전에** 차감한다. 이벤트가 Flush를 재호출해도 초과 처리하지 않도록 배출 재진입도 차단한다.
- Watch 폴링의 프레임 가드와 로그 배출 예산은 분리한다.
- PlayerLoop를 기본 처리 주체로 사용한다.
- UI 폴백도 동일한 `TickRuntime()`을 사용한다. 해당 프레임에 이미 폴링했다면 다시 평가하지 않는다.
- 에디터 플레이 중 `EditorApplication.update`는 처리하지 않는다.
- 에디터 비플레이 업데이트는 대기 로그·태그만 배출하고 Watch 게터를 평가하지 않는다. 이 경우 업데이트 1회가 예산 주기다.
- 정적 리셋·재초기화 시 프레임 표식과 재진입 플래그를 리셋한다.
- PlayerLoop 교체가 있어도 기존 관련 시스템을 중복 삽입하지 않는다.
- 기존 `UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER` 분기는 유지한다.

동기 Watch·메인 로그·개별 게터·이벤트 구독자의 실행 시간에는 상한을 보장하지 않는다.

### A3-26. Watch 재진입 안전성

새 내부 구성:

| 타입·필드·메서드 | 역할 |
|---|---|
| `private sealed class GetterRegistration` | 게터·owner·평가 상태를 담는 등록 토큰 |
| `private static List<GetterRegistration> s_GetterPollBuffer` | 재사용 순회 버퍼 |
| `private static bool s_IsPollingWatchGetters` | 재귀 폴링 차단 |
| `private static bool IsCurrentRegistration(...)` | 실행 상태·세션·동일 등록 확인 |
| `private static void EvaluateRegistration(...)` | 첫 평가·주기 평가의 공통 경로 |
| `private static void ApplyGetterResult(...)` | 게터를 제거하지 않는 내부 값 반영 |

처리 순서:

1. 값 Watch는 해당 키의 게터 등록을 먼저 제거한다.
2. 그 뒤 기존 값 변환 규칙을 적용한다. 사용자 `ToString()` 재진입도 고려하여 변환 전후 키 변경 토큰을 확인한다.
3. 게터 Watch는 새 등록 객체로 교체한 뒤 기존 의미대로 첫 평가를 수행한다.
4. 폴링은 등록 목록을 재사용 버퍼에 복사한다.
5. 각 게터 실행 전 현재 등록·owner·실행 세대를 확인한다.
6. 게터 실행 및 값 변환 뒤 각각 다시 확인한다.
7. 결과 반영은 공개 값 Watch를 호출하지 않는다. 그렇지 않으면 확정 5에 의해 자기 게터를 제거하게 된다.
8. 이벤트 호출 후에는 이전 결과를 다시 덮어쓰지 않는다.
9. Clear·리셋·Shutdown은 진행 중 평가를 무효화한다.
10. owner의 부재와 파괴를 구분한다. `ReferenceEquals`로 실제 미지정을 확인하고 Unity 객체 생존 판정에는 Unity의 null 비교를 사용한다.

```csharp
private static void EvaluateRegistration(
    string key, GetterRegistration registration)
{
    if (!IsCurrentRegistration(key, registration))
        return;

    object value;
    try
    {
        value = registration.Getter();
    }
    catch (System.Exception exception)
    {
        if (IsCurrentRegistration(key, registration))
            ApplyExistingGetterError(key, registration, exception);

        return;
    }

    if (!IsCurrentRegistration(key, registration))
        return;

    // 사용자 문자열 변환에서도 재진입할 수 있다.
    string text = ConvertWatchValueUsingExistingRules(value);

    if (!IsCurrentRegistration(key, registration))
        return;

    // 공개 값 Watch와 달리 게터 등록을 제거하지 않는다.
    ApplyGetterResult(key, registration, text);
}
```

변환 실패는 게터 실행 실패와 구분해 처리하며 기존 표시 정책을 보존한다. 자료 부족: 현재 변환 실패 처리 규칙.

- `WatchCount`는 스냅샷 없이 내부 개수만 반환한다.
- `Watches`는 사전과 각 `WatchEntry`를 복사한다.
- 공개 엔트리 안에 가변 참조 필드가 있다면 복사 범위를 확인해야 한다. 자료 부족: `WatchEntry` 전체 선언.
- 폴링 빈도·트리거 관찰 빈도는 변경하지 않는다.
- 임의 객체의 `Equals` 결과만으로 변환을 생략하지 않는다.

### A3-27. 이벤트와 예외 태그

이벤트 처리:

- 게터 호출의 `try` 안에 이벤트 호출을 넣지 않는다.
- 구독자별 예외 격리를 적용하여 뒤 구독자와 다음 항목 처리를 계속한다.
- 이벤트별 `GetInvocationList()`는 구독 목록이 바뀔 때 캐시를 갱신하여 매 로그·매 폴링 할당을 피한다. 기존 이벤트 시그니처는 유지한다.
- 오류 진단은 `s_SuppressCoreCapture` 범위에서 출력한다.
- 일반 구독자가 의도적으로 로그를 무한 생성하는 행위까지 차단하는 것은 이번 범위 밖이다.

예외 태그:

```csharp
string previousTag = s_CurrentChannelTag;
try
{
    s_CurrentChannelTag = channelTag;
    EmitOriginalException(exception);
}
finally
{
    s_CurrentChannelTag = previousTag;
}
```

- 수신 콜백은 예외 종류에 한해 태그를 즉시 `PendingLog`로 복사한다.
- `LogEntry` 생성 경로는 명시적 태그가 있으면 이를 사용하고, 없으면 기존 문자열 파싱을 사용한다.
- 원본 예외와 스택 출력을 유지한다.
- **미확인:** 사용자 로그 핸들러가 같은 범위에서 별개의 예외를 출력하면 종류 검사만으로 정확히 구분할 수 없다. 콜백이 비동기라면 TLS 방식도 성립하지 않는다.
- 따라서 Mono·IL2CPP 및 사용자 핸들러 시험 통과를 병합 조건으로 둔다. 실패 시 조용히 잘못된 태그를 붙이지 말고 전달 설계를 재검토한다. 자료 부족: 실제 예외 출력 경로·핸들러 지원 범위. 현재 자료로 확정할 수 있는 범용 대안은 없다.

### A3-28. 로그 UI와 Watch UI

새 내부 구성:

| 타입·필드·메서드 | 역할 |
|---|---|
| `private Queue<LogRowBinding> createdLogRows` | 기존 행 수명 관리의 선두 이동 제거 |
| `private bool isLogsDirty` | 닫힌 동안 로그 변경 |
| `private void RemoveLogRow(...)` | 선택·콜백·참조 정리 후 행 제거 |
| `private void SynchronizeLogsOnOpen()` | 최신 History와 행 동기화 |
| `private void UnbindRootCallbacks()` | 이전 루트 콜백 해제 |

로그 처리:

1. 닫힌 초기화는 로그 행을 생성하지 않는다.
2. 닫힐 때 기존 행과 유효한 선택은 유지한다.
3. 닫힌 동안 로그가 추가되면 dirty만 설정하고 개수를 갱신한다.
4. 다시 열 때 dirty가 아니면 재구성하지 않는다.
5. dirty이면 최신 History와 동기화한다. 기존 `LogEntry`의 안정적 식별이 가능하면 남아 있는 행은 재사용하고 퇴출 행만 제거한다.
6. 자료 부족: `LogEntry`의 동일성·식별자. 안전한 대응 관계를 만들 수 없다면 dirty 재열기 때 전체 재구성을 사용한다.
7. 재구성·행 제거 시 `selectedRow`와 관련 상세 참조·콜백을 정리한다.
8. 이미 열린 상태의 Open 재호출은 중복 재구성하지 않는다.
9. Clear는 닫혀 있어도 기존 행·선택을 즉시 제거한다.
10. 패널 루트 교체는 이전 콜백과 행 참조를 정리한다. 새 루트가 닫혀 있으면 행은 다음 열기까지 만들지 않는다.
11. 열린 상태에서 상한 초과 시 큐의 선두 행을 제거한다. `List.RemoveAt(0)`은 사용하지 않는다.

Watch 처리:

- Watch 제거는 키 단위로 행을 제거한다.
- 제거 이벤트가 키를 제공하지 않는다면 변경 알림을 dirty로 모아 UI 갱신 단계에서 한 번 비교한다. 이벤트 시그니처는 바꾸지 않는다.
- 트리거 검사는 기존 코어 이벤트마다 수행하고 UI 표시 병합과 분리한다.
- 닫힌 Watch 행·태그 버튼의 전면 지연 생성은 이번 필수 변경에서 제외한다.

### A3-29. 에디터·설정·생성기·문서

**에디터 창**

- OFF이면 `"USE_HLOG 꺼짐"`을 표시하고 초기화·평가를 시도하지 않는다.
- 로그·Watch·플레이 상태 변경 이벤트에서 캐시를 무효화한다.
- `Watches`는 dirty일 때 한 번만 읽고 GUI 이벤트 사이에 창 소유 스냅샷을 재사용한다.
- 정적 리셋 뒤 이벤트 통지가 부족한 경우 플레이 모드 전환에서 전체 캐시를 무효화한다.
- 구독 유지가 필요하면 유지하되 등록은 멱등적으로, 해제는 창 종료에 대응시킨다.

**설정**

- 에디터·플레이어 모두 기존 `Resources/HB_InGameConsoleUserSettings`만 사용한다.
- 다른 위치의 에셋은 에디터에서 한 번 경고한다.
- 직렬화 필드명은 유지한다. 이름 변경이 불가피하면 `[FormerlySerializedAs]`를 적용한다.
- 패키지에 YAML 에셋은 추가하지 않는다.

**생성기**

제안 마커: `ProjectSettings/HB_InGameConsole.LogChannels.generated.txt`.

처리 순서:

1. 마커가 있으면 자동 생성하지 않는다.
2. 없으면 기존 생성 파일을 확인한다.
3. 기존 파일이 있거나 생성 성공이 확인된 뒤에만 마커를 쓴다.
4. 마커는 임시 파일 작성 후 최종 경로로 교체하고 Git에 포함한다.
5. 생성·쓰기 실패는 한 번 보고하고 성공으로 기록하지 않는다.
6. 수동 생성은 마커 유무와 무관하게 허용한다.
7. 자동 생성 1회 마커는 템플릿 버전마다 무효화하지 않는다.
8. 기존 생성 파일의 새 Conditional 적용은 명시적 재생성으로 수행한다.

**README**

- 활성 조건·호출 제거 범위·남는 Get 호출 비용.
- 비개발 빌드에서도 ON이면 동작하며 출시 전 OFF는 빌드 담당자 책임.
- 기존 생성 파일 재생성 필요.
- 확정된 스레드 계약과 워커 위반 처리.
- Shutdown·Clear 경계.
- 실제 API·메뉴 경로만 기재하며 별칭 API는 추가하지 않음.
- 닫힘 행 유지, 최초 열기 비용, 고유 태그·채널 무제한 증가 가능성.
- 직접 `Debug.Log`는 범위 밖이지만 ON의 수집 대상임을 명시.

## 3. 자기 보고

### A3-30. 변경 파일·줄 수

실제 파일은 수정하지 않았다. 설계 변경 대상은 런타임·에디터 7개 파일, README 및 재생성 산출물이다.

| 파일 | 전체 줄 수 / 빈 줄 제외 줄 수 |
|---|---|
| `HLogCore.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `LogChannel.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `InGameConsole.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `InGameConsoleSettings.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `HB_InGameConsoleUI.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `HLogConsoleWindow.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `LogChannelGeneratorWindow.cs` | 자료 부족: 원본 및 최종 구현 파일 |
| `README` | 자료 부족: 원본 및 최종 문서 |
| 생성 `LogChannels.cs`·공유 마커 | 자료 부족: 실제 생성 결과 |

바뀐 결정 목록:

- 활성 기준을 `USE_HLOG` 단독으로 통일.
- 워커 Watch 명령 구조 삭제, 메인 전용 검사 도입.
- Clear·Shutdown과 로그 수집 사이에 세대 경계 도입.
- 큐와 별도 카운터 대신 짧은 잠금의 bounded queue 사용.
- 게터 결과 반영과 공개 값 Watch 분리.
- 닫기 시 행·유효 선택 유지, 행 제거·재구성 시 정리.
- UI 선두 배열 이동 제거.
- 공개 버전 API 대신 에디터 내부 캐시 사용.
- 공유 비-YAML 자동 생성 마커 사용.

공개 API 변경:

- 기존 시그니처·공개 필드는 유지.
- 계획된 읽기 전용 `WatchCount` 추가.
- 지정 메서드의 Conditional 메타데이터 변경.
- `WatchVersion`·`CopyWatchesTo`·로그 별칭은 추가하지 않음.
- 자료 부족: 기존 공개 선언 전체. 최종 공개 API 대조는 구현 후 필요하다.

## 4. 확정 사항 반영 내역

### A3-31.

| 확정 사항 | 반영 위치·방법 |
|---|---|
| 1 | A3-21·29. 단일 심볼, 전체 지정 Conditional, OFF 구현, 에디터 도구 유지, 재생성·출시 책임 문서화 |
| 2 | A3-22·26. Get·로그만 워커 허용, 나머지 진입점 거절·최초 진단, `PendingWatch` 없음 |
| 3 | A3-24·26·28·29. 링 버퍼·로그 지연 생성 유지, Watch 메인 변경·평가, README API 정정 |
| 4 | A3-23·25·26. Shutdown 전 처리 중단·결과 무효화, Clear 대기 로그 폐기 |
| 5 | A3-26. 값 Watch가 게터를 먼저 제거, 게터 평가 결과는 별도 내부 경로로 반영 |
| 6 | A3-29. ProjectSettings 비-YAML 마커, Git 공유, 기존 파일 확인·생성 성공 후 기록 |
| 7 | A3-28. 닫기 시 행 유지, 닫힌 동안 dirty, 재열기 동기화, 행 제거·재구성 때 선택 정리 |

## 5. 성능 지적 반영 내역

### A3-32.

| PF1 ID | 반영 · 부분 · 미반영 | 이유 |
|---|---|---|
| PF1-01 | 부분 | 공유 고정 배출 예산·전용 태그 큐·재진입 차단 적용. 메인 즉시 로그와 동기 Watch 의미는 보존하며 시간 상한은 보장하지 않음 |
| PF1-02 | 부분 | 대기 Watch 제거, 태그 전달 중복 방지. 캐시 상한·퇴출과 닫힌 태그 UI 전면 지연은 사양 변경이므로 보류 |
| PF1-03 | 반영 | 단건 행 제거 또는 변경 묶음 비교로 반복 전체 재생성 방지 |
| PF1-04 | 부분 | 순회 버퍼 재사용과 측정 계획 적용. 안전하지 않은 범용 동등성 최적화·폴링 빈도 변경·공개 제네릭 확장은 미적용 |
| PF1-05 | 부분 | 최초·재로드 지연 생성과 제거 시 선택 정리 적용. 닫기 때 행 유지라는 확정 사항 우선 |
| PF1-06 | 부분 | UI 선두 이동 제거, 변경 없는 재열기 재구성 방지. 최초 동기 생성 비용은 측정 대상으로 남김 |
| PF1-07 | 반영 | 에디터의 이벤트 기반 스냅샷 캐시 적용. 외부 `Watches`의 독립 복사 계약은 유지 |

## 6. 미해결 쟁점

### A3-33.

| 심각도 | 쟁점·발생 조건·영향 | 대응·검증 |
|---|---|---|
| 높음 | 예외 TLS 태그는 비동기 콜백·무관한 중첩 예외에서 오분류 가능 | A3-27 시험을 병합 조건으로 적용. 자료 부족: 출력 경로·지원 핸들러 범위. 범용 대안 미확정 |
| 높음 | 공개 선언·생성 표본 미첨부로 OFF 반환형·Conditional 전체 적용을 확정할 수 없음 | 자료 부족: 7개 파일·생성 템플릿·기존 생성 파일·asmdef·HB_Cheat 소비 코드. ON/OFF 컴파일 대조 필요 |
| 중간 | OFF 진단과 최초 1회 진단 범위는 명시되지 않음 | A3-22의 가정 적용 여부 확인. OFF 무출력·이름별·세션별 기준으로 시험 |
| 중간 | 고유 Watch·태그·채널은 계속 증가할 수 있음 | 임의 제한 없이 문서화·측정. 자동 퇴출·거절 정책은 후속 사양 |
| 중간 | 최초 열기·긴 메시지·무거운 사용자 콜백은 건수 제한과 무관하게 느릴 수 있음 | 자료 부족: 최소 대상 기기·허용 지연·최대 규모. 실기기 측정 후 풀·분할 생성 별도 검토 |
| 중간 | Shutdown 중 상태 보존과 기존 초기화의 암묵적 동작은 원본 확인 필요 | 정지 계약을 우선하되 기록·Watch 보유 의미는 보존. 자료 부족: 전체 수명주기 코드 |
| 중간 | 기존 생성 파일이 이전 Conditional을 유지하면 호출자 인자 계산이 남을 수 있음 | 재생성 안내·표본 검사. 자동 소스 개작은 범위 밖 |
| 범위 밖 | 설치 시 심볼 자동 추가, HB_Build 연동 | 이번 변경에 포함하지 않음 |
| 범위 밖 | Watch·태그 UI 전면 지연, 가상화·분할 생성, 임의 객체 표시 캐시 | 기존 의미를 유지하고 별도 성능 결과로 판단 |

B1-08의 영구 카운터 오염 원인에 대한 기존 반박은 유지한다. 상대 Phase 2에는 그 원인을 입증하는 추가 코드가 없다. bounded queue 선택은 그 주장을 인정해서가 아니라 용량·카운트·리셋의 원자성을 단순하게 확보하기 위한 설계 결정이다.

## 7. 테스트 계획

### A3-34.

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | 공개 API·생성 템플릿 대조 | 기존 시그니처·직렬화 필드 유지, 허용된 `WatchCount` 외 공개 확장 없음 |
| 2 | 에디터·개발·비개발 빌드 × ON/OFF 컴파일 | 같은 심볼 상태면 동일 활성 정책, 기존 생성 코드·HB_Cheat 소비 코드 컴파일 성공 |
| 3 | OFF 우회 진입 | 수동 Initialize·EnsureUIInstance·씬 배치·외부 뷰·직접 코어 호출에서 UI·수집·평가 없음, 읽기 전용 빈 반환 |
| 4 | Conditional 인자 부작용 | ON/OFF 호출자에서 지정 로그·Watch 인자 카운터가 기대대로 실행·제거됨. 별도 Get 호출은 제거된다고 가정하지 않음 |
| 5 | 최초 워커 호출 | 워커 Get·로그는 예외 없이 가능, 금지 API는 상태 불변, 워커가 메인 ID로 등록되지 않음 |
| 6 | API별 동시 위반 | 다중 워커 반복 호출에도 지정 단위당 최초 에러 1회, 재귀 수집 없음 |
| 7 | 대기 큐 동시성·용량 | 생산·배출 교차 후 실제 잔여 수 일치, 용량 준수, 생산자 정지 후 새 로그 정상 처리 |
| 8 | Clear 경합 | 접수·큐 등록·인출·파싱 사이 배리어로 멈춘 이전 세대 로그가 Clear 뒤 나타나지 않음. 이후 새 접수 로그는 표시 |
| 9 | Shutdown·재초기화 | 수집·배출·폴링 정지, 진행 중 이전 결과 무효, 재초기화 후 이전 로그 없음, 구독·루프 중복 없음 |
| 10 | 링 버퍼 경계 | 0·1·999·1000·1001·반복 순환에서 순서·집계·History 스냅샷 정확 |
| 11 | 폴링·배출 주체 | PlayerLoop·UI·공개 Flush가 겹쳐도 예산 초과 없음, 게터 프레임당 최대 1회, 비플레이 게터 평가 없음 |
| 12 | Watch 재진입 | 자기 해제·동일 키 교체·값 Watch·Clear·Shutdown·문자열 변환 재진입에서 부활·덮어쓰기 없음 |
| 13 | 값·게터 전환과 owner | 나중 값 Watch 이후 이전 게터 미실행, owner 파괴 후 자동 해제, UI 없이도 평가·해제 작동 |
| 14 | 구독자 실패 | 첫 구독자 예외에도 뒤 구독자·다음 항목 처리, 정상 Watch 값 오류 문자열 오염 없음, 진단 재귀 없음 |
| 15 | 예외 태그 | 메인·워커·중첩·억제·핸들러 실패·무관 예외를 Mono·IL2CPP에서 검사. 원본 예외·스택 유지, 태그 오염 없음 |
| 16 | 닫힌 로그 UI | 최초 닫힘·닫힌 패널 재로드에서 행 0개, 열기 후 닫으면 행 유지, 변경 없는 재열기 생성 없음 |
| 17 | 닫힌 동안 변경·선택 | 추가·퇴출·Clear 후 재열기 표시가 History와 일치, 제거 행·선택·상세 참조 잔존 없음 |
| 18 | UI 재바인딩·Watch 대량 제거 | 동일·새 루트에서 클릭당 1회, 이전 루트 해제. 대량 제거 때 전체 Watch 행 반복 재생성 없음 |
| 19 | 리로드 조합 | 도메인·씬 리로드 조합별 이전 세션 상태·창 캐시 불일치·중복 구독 없음, 로그 활성 기본값 복원 |
| 20 | 설정·공유 마커 | Resources 검색 일치, 다른 위치 경고 1회. 새 클론에서 삭제한 생성 파일 미재생성, 생성 실패 시 마커 없음 |
| 21 | 성능·메모리 | UI 없음·닫힘·열림을 분리해 CPU·GC·보유 참조·대기 시간을 기록. UI `RemoveAt(0)`과 반복 GUI 깊은 복사 없음. 절대 시간 합격선은 대상 기기·허용 지연 확정 후 설정 |