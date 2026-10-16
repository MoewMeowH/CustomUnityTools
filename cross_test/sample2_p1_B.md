1. 지적 목록

- **ID**: B-01
  - **위치**: `CountJob` 구조체 (`cellCounts` 필드 및 `Execute` 메서드)
  - **문제**:[사실] `IJobParallelFor`에서 루프 인덱스 `i`가 아닌 계산된 인덱스 `cell`에 대해 비원자적 쓰기(`cellCounts[cell]++`)를 수행하여 경쟁 상태(Race Condition) 및 안전성 검사 위반이 발생함.
  - **발생 조건**: 서로 다른 워커 스레드가 실행하는 배치(batch)에서 계산된 `cell` 값이 동일할 때, 또는 `cell` 값이 현재 스레드에 할당된 인덱스 범위를 벗어날 때 발생.
  - **영향**: 
    1. 에디터(Safety Checks 활성화): 스레드에 할당된 인덱스 `[i]` 외의 인덱스에 접근하거나 다중 스레드가 동시에 쓰기를 시도하여 `InvalidOperationException` 발생 (증상 1의 원인).
    2. 빌드(Safety Checks 비활성화): 예외 없이 실행되나, `cellCounts[cell]++` 연산(읽기-수정-쓰기)이 원자적이지 않아 워커 스레드 간 덮어쓰기(Lost Update)가 발생하여 실행마다 `cell0` 값이 달라짐 (증상 2의 원인 1).
  - **심각도**: 높음 (우선순위 1)
  - **확신도**: 높음
  - **검증 방법**: 에디터에서 Jobs -> Burst -> Safety Checks를 켠 상태로 실행 시 `CountJob` 스케줄링 또는 완료 시점에 인덱스 범위/동시 접근 관련 `InvalidOperationException` 콜스택이 출력되는지 확인. 수정 후 릴리즈 빌드에서 반복 실행 시 `cell0` 값이 항상 일정한지 확인.

- **ID**: B-02
  - **위치**: `OnDestroy()` 메서드
  - **문제**: [사실] 실행 중이거나 스케줄링된 Job의 완료를 보장하는 `_handle.Complete()` 호출 없이 `NativeArray.Dispose()`를 호출함.
  - **발생 조건**: `Update()`에서 Job이 스케줄링된 후 `LateUpdate()`가 호출되기 전에 씬이 언로드되거나 오브젝트가 파괴될 때, 또는 비활성화 상태에서 스케줄링된 핸들이 남아있을 때.
  - **영향**: 워커 스레드가 아직 메모리를 읽고 쓰는 중에 메인 스레드가 메모리를 해제하려고 시도하여, 에디터에서는 Safety System 에러가 발생하고 빌드에서는 메모리 오염이나 크래시가 간헐적으로 발생함 (증상 3의 원인 1).
  - **심각도**: 높음 (우선순위 2)
  - **확신도**: 높음
  - **검증 방법**: `Update()` 직후(예: 다른 스크립트의 `Update`에서) `SceneManager.LoadScene` 또는 `Destroy(gameObject)`를 호출하여 `LateUpdate`를 건너뛰고 `OnDestroy`가 실행되게 한 뒤 에러 발생 여부 확인.

- **ID**: B-03
  - **위치**: `Start()` 메서드의 `_positions`, `_velocities` 할당부
  - **문제**: [사실] 컴포넌트 수명 주기 동안 유지되는 배열에 수명이 4프레임으로 제한된 `Allocator.TempJob`을 사용함.
  - **발생 조건**: 게임 실행 후 4프레임이 경과할 때.
  - **영향**: Unity 내부 메모리 할당자에서 경고를 출력하며, 씬 종료 시점(`OnDestroy`)에 이미 수명이 만료된 할당자를 해제하려다 할당자 내부 상태에 따라 에러가 발생할 수 있음 (증상 3의 원인 2).
  - **심각도**: 높음 (우선순위 3)
  - **확신도**: 높음
  - **검증 방법**: 에디터 콘솔에서 4프레임 이후 `"Internal: JobTempAlloc has allocations that are more than 4 frames old"` 에러/경고 로그가 발생하는지 확인.

- **ID**: B-04
  - **위치**: `Start()` 메서드의 `_positions` 초기화 루프
  - **문제**: [사실] 시드(Seed)가 고정되지 않은 `UnityEngine.Random.insideUnitSphere`를 사용하여 초기 위치를 생성함.
  - **발생 조건**: 앱/씬을 새로 시작할 때마다 매번 발생.
  - **영향**: 실행할 때마다 초기 `_positions` 배열의 값 분포가 달라지므로, 경쟁 상태를 해결하더라도 실행할 때마다 `cell0`의 초기 카운트 값이 달라짐 (증상 2의 원인 2).
  - **심각도**: 중간 (우선순위 4)
  - **확신도**: 높음
  - **검증 방법**: 고정된 시드를 사용하는 `Unity.Mathematics.Random`으로 교체한 뒤 재실행 시 초기 `_positions` 값들이 동일하게 생성되는지 확인.

- **ID**: B-05
  - **위치**: `Update()` 메서드
  - **문제**: [추론] 매 프레임 `_cellCounts` 배열을 0으로 초기화하지 않고 계속 값을 누적함.
  - **발생 조건**: 2프레임 이상 실행 시 매 프레임 발생.
  - **영향**: 현재 프레임의 셀 개수가 아니라 프레임 수에 비례하여 값이 계속 증가함. 프레임 레이트 변동에 따라 특정 시점에 관찰되는 `cell0` 값이 달라질 수 있으며, 장시간 실행 시 `int` 오버플로우 위험이 있음.
  - **심각도**: 중간 (우선순위 5)
  - **확신도**: 높음
  - **검증 방법**: `_velocities`가 0인 상태에서 매 프레임 `cell0` 로그 값이 일정하게 유지되지 않고 배수로 증가하는지 확인.

- **ID**: B-06
  - **위치**: `Start()` 및 `OnDestroy()` 메서드
  - **문제**: [사실] `Start()`가 호출되기 전에 오브젝트가 파괴될 경우 할당되지 않은(`IsCreated == false`) `NativeArray`에 대해 `Dispose()`를 호출하게 됨.
  - **발생 조건**: 비활성화된 상태로 씬에 존재하던 오브젝트가 활성화되지 않은 채로 씬이 종료될 때.
  - **영향**: `OnDestroy()`에서 예외 또는 할당 해제 에러 발생 가능 (증상 3의 잠재적 원인).
  - **심각도**: 낮음 (우선순위 6)
  - **확신도**: 높음
  - **검증 방법**: 게임 오브젝트를 비활성화 상태로 두고 플레이 모드 진입 후 즉시 종료하여 에러 발생 여부 확인.

---

2. 사양 확인이 필요한 항목

- **항목 1 (`_cellCounts` 초기화 여부)**: `_cellCounts`는 매 프레임 현재 위치 기준으로 다시 계산되어야 하는지, 아니면 시뮬레이션 시작부터 끝까지 누적되어야 하는지 사양 확인이 필요함.
  - **조건부 결론**: 일반적인 공간 분할(Spatial Partitioning) 목적이라면 매 프레임 `Update`에서 `CountJob` 실행 전에 0으로 초기화해야 함.
- **항목 2 (결정론적 결과의 범위)**: 성공 기준의 "결과가 결정적(deterministic)으로 나오는 수정안"이 (A) 동일한 초기 입력에 대해 스레드 경합 없이 항상 같은 계산 결과가 나오는 것을 의미하는지, (B) 실행할 때마다 초기 난수 생성까지 완전히 동일한 시뮬레이션이 재현되어야 하는지 확인이 필요함.
  - **조건부 결론**: (B)까지 포함된다면 초기 위치 생성 시 고정 시드 기반의 `Unity.Mathematics.Random`을 사용해야 하며, `MoveJob`의 `Time.deltaTime` 역시 실행마다 달라지므로 고정 델타 타임 사용 여부를 검토해야 함(`_velocities`가 초기화되지 않아 현재 코드에서는 `0`이므로 당장 위치 변화는 없음).

---

3. 수정 방향

- **B-01 대응 (병렬 원자적 연산 적용)**: `CountJob`에서 `NativeArray`의 안전성 검사 제한을 해제하고 `Interlocked.Increment`를 사용하여 스레드 안전하게 카운트를 증가시킴.
```csharp
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;

[BurstCompile]
struct CountJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> positions;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> cellCounts;

    public unsafe void Execute(int i)
    {
        int cell = (int)(math.abs(positions[i].x) % 1024);
        int* ptr = (int*)cellCounts.GetUnsafePtr();
        Interlocked.Increment(ref ptr[cell]);
    }
}
// 비고: unsafe 키워드 사용을 위해 프로젝트 설정(Allow 'unsafe' Code) 활성화 또는 UnsafeUtility.AddressOf 사용 필요.
```

- **B-02, B-03, B-06 대응 (수명 주기 및 메모리 해제 안전성 확보)**:
```csharp
void Start()
{
    // TempJob에서 Persistent로 변경 (B-03)
    _positions  = new NativeArray<float3>(count, Allocator.Persistent);
    _velocities = new NativeArray<float3>(count, Allocator.Persistent);
    _cellCounts = new NativeArray<int>(1024, Allocator.Persistent);

    // 실행 간 결정론적 초기화를 원할 경우 고정 시드 사용 (B-04)
    var rng = new Unity.Mathematics.Random(12345u);
    for (int i = 0; i < count; i++)
        _positions[i] = rng.NextFloat3Direction() * rng.NextFloat(0f, 50f);
}

void OnDestroy()
{
    // 해제 전 반드시 스케줄링된 Job 완료 대기 (B-02)
    _handle.Complete();

    // 할당 여부 확인 후 해제 (B-06)
    if (_positions.IsCreated) _positions.Dispose();
    if (_velocities.IsCreated) _velocities.Dispose();
    if (_cellCounts.IsCreated) _cellCounts.Dispose();
}
```

- **B-05 대응 (프레임별 카운트 초기화가 필요한 경우)**:
```csharp
// Update() 내 CountJob 스케줄링 직전에 추가
unsafe
{
    UnsafeUtility.MemClear(_cellCounts.GetUnsafePtr(), _cellCounts.Length * sizeof(int));
}
```
