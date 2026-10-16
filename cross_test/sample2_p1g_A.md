## 1. 지적 목록

### A-01 / `CountJob.Execute`: `cellCounts[cell]++`
- **문제:** 서로 다른 반복이 같은 셀을 동시에 수정한다. `++`는 원자 연산이 아니므로 읽기·수정·쓰기 사이에서 갱신이 유실된다. 또한 `IJobParallelFor`의 기본 쓰기 범위 제한과 맞지 않는 인덱스 접근이다.
- **발생 조건:** 여러 개체가 같은 셀에 속하거나, `cell`이 해당 작업 반복에 허용된 쓰기 범위를 벗어나는 경우.
- **영향:**
  - **안전성 검사 활성 환경:** 병렬 쓰기 범위 관련 예외가 발생할 수 있다.
  - **검사 비활성 환경:** 예외 없이 집계가 누락되어 실행 순서에 따라 결과가 달라질 수 있다.
  - **미확인:** 보고된 예외가 정확히 `InvalidOperationException`인지, 이 접근에서 발생했는지는 스택 트레이스가 필요하다. 범위 검사 오류와 컨테이너 접근 충돌 오류를 동일한 예외로 단정할 수 없다.
- **심각도:** 높음
- **확신도:** 데이터 경쟁과 범위 제한 위반은 높음. 실제 예외 메시지와의 대응은 미확인.
- **검증 방법:** 모든 위치를 `float3.zero`로 고정하고 20,000개를 집계한다. 정상 결과는 `cellCounts[0] == 20000`이고 나머지는 0이다. 검사 활성 상태의 전체 예외와 스택을 수집하고, 수정 후 검사 활성·비활성 빌드에서 반복 비교한다.
- **대응:** 작업별 독점 히스토그램을 만들고 별도 병렬 작업으로 합산한다. 기존 배열에 범위 제한 해제 속성만 붙이는 수정은 경쟁을 해결하지 못한다.

### A-02 / `Start`: `_positions`, `_velocities`의 `Allocator.TempJob`
- **문제:** 여러 프레임 동안 보유하는 배열에 단기 작업용 할당자를 사용했다.
- **발생 조건:** 컴포넌트가 할당 후 4프레임 이내에 배열을 해제하지 않는 경우.
- **영향:** 수명 초과 진단이 발생할 수 있다. 종료 시점에 보이는 메시지가 실제로는 이전부터 유지된 할당의 수명 문제일 가능성도 있다. 이것만으로 경쟁에 의한 집계 불일치를 설명할 수는 없다.
- **심각도:** 높음
- **확신도:** 높음
- **검증 방법:** 작업 경쟁을 먼저 제거한 뒤 10프레임 이상 실행하며 할당 관련 로그를 확인한다. `Persistent`로 변경한 경우와 비교한다.
- **대응:** 컴포넌트 수명 동안 유지할 배열은 `Allocator.Persistent`로 할당하고 종료 시 명시적으로 해제한다.

### A-03 / `OnDestroy`: 작업 완료 및 생성 여부 확인 없이 `Dispose`
- **문제:** 마지막 작업이 완료되었다는 보장 없이 메모리를 해제하며, 할당되지 않은 배열도 해제할 수 있다.
- **발생 조건:**
  - 예약 후 `LateUpdate`의 `Complete()`에 도달하기 전에 파괴되는 경우.
  - 초기화 도중 실패하거나 `Start`가 실행되지 않은 상태에서 정리 콜백이 호출되는 경우.
- **영향:** 검사 활성 환경에서는 실행 중 컨테이너 해제 등의 오류가 발생할 수 있고, 검사 비활성 환경에서는 해제된 메모리에 접근할 위험이 있다.
- **조건부 판단:** 정상적으로 매 프레임 `LateUpdate`가 완료된 뒤 씬을 닫는 경로라면 그 프레임 작업은 이미 완료되어 있다. 따라서 이 코드만으로 “씬 종료마다 작업이 실행 중이다”라고 단정할 수 없다.
- **심각도:** 높음
- **확신도:** 정리 경로의 결함은 높음. 실제 종료 오류의 직접 원인인지는 미확인.
- **검증 방법:** 작업 예약 직후 다른 컴포넌트에서 이 컴포넌트를 파괴하는 테스트, 비활성 상태에서 한 번도 시작하지 않은 객체의 파괴 테스트, 일반 씬 언로드 테스트를 분리한다. 각각 전체 스택을 확보한다.
- **대응:** `OnDestroy`에서 마지막 작업 핸들을 먼저 완료한 뒤, 각 배열의 `IsCreated`를 확인해 해제한다. 마지막 핸들은 모든 예약된 작업을 포함해야 한다.

### A-04 / `Update`: `_cellCounts`를 프레임마다 초기화하지 않음
- **문제:** 초기 생성 시 0으로 시작하더라도 이후에는 이전 집계에 계속 더한다.
- **발생 조건:** 의도가 “현재 프레임의 셀별 개체 수”인 경우.
- **영향:** 경쟁을 해결해도 두 번째 프레임부터 기대한 현재 개체 수와 다르다. 누적을 계속하면 `int` 표현 범위를 초과할 수 있다.
- **심각도:** 중간
- **확신도:** 누적 동작은 높음. 버그 여부는 집계 사양에 따라 조건부.
- **검증 방법:** 모든 위치와 속도를 0으로 고정하고 두 프레임 실행한다. 프레임별 집계라면 두 프레임 모두 셀 0이 20,000이어야 한다.
- **대응:** 프레임별 집계는 매 프레임 결과를 덮어쓰도록 구성한다. 누적 집계가 의도라면 별도의 누적 단계와 오버플로 정책이 필요하다.

### A-05 / `Start`: 시드가 고정되지 않은 초기 위치
- **문제:** `UnityEngine.Random`의 공유 상태에 따라 입력 자체가 실행마다 달라질 수 있다. 이는 작업 경쟁과 별개의 비결정성이다.
- **발생 조건:** 동일한 입력이 아니라 애플리케이션을 새로 실행한 결과끼리 비교하는 경우.
- **영향:** 올바른 병렬 집계로 수정해도 `cell0`이 실행마다 같다는 보장이 없다.
- **심각도:** 중간
- **확신도:** 높음
- **검증 방법:** 저장된 동일 위치 배열 또는 고정 시드로 생성한 위치 배열을 사용해 반복 실행한다. 초기 위치 배열 자체도 비교한다.
- **대응:** 공유 난수 상태 대신 고정된 비영 시드를 가진 지역 `Unity.Mathematics.Random`을 사용하거나, 검증용 입력 배열을 고정한다.

### A-06 / `LateUpdate`: `_positions[0]`, 초기화 입력
- **문제:** `count`가 0이면 첫 원소 접근이 잘못되며, 음수면 배열 생성이 실패한다.
- **발생 조건:** 인스펙터나 코드에서 `count <= 0`으로 설정한 경우.
- **영향:** 초기화 또는 `LateUpdate`에서 오류가 발생한다. 부분 초기화 후 A-03의 정리 문제도 노출될 수 있다.
- **심각도:** 중간
- **확신도:** 높음
- **검증 방법:** `count = 0`, `count = -1`을 각각 설정해 초기화 실패와 정리 동작을 확인한다.
- **대응:** 양수만 허용할지 빈 시뮬레이션도 허용할지 정한 뒤 런타임에서 검사한다. 아래 수정 방향은 양수만 허용한다고 가정한다.

## 2. 사양 확인이 필요한 항목

- **결정성의 범위:** 같은 위치 입력에 대한 정수 집계의 일치인가, 실행 전체의 위치까지 플랫폼 간 비트 단위로 같아야 하는가? 아래 수정은 **같은 입력에 대한 집계의 결정성**을 보장하는 구조다. 이동까지 포함한 장기 시뮬레이션은 고정 시간 간격, 입력, 부동소수점 연산 정책을 추가로 정해야 한다.
- **집계 시점:** 현재 의존성은 이동 전 위치를 집계한다. 이동 후 위치가 필요하면 `Move → 집계 → 합산` 순서여야 한다.
- **집계 의미:** 매 프레임 개체 수인지 누적 방문 횟수인지 확인이 필요하다.
- **셀 매핑:** 현재는 x좌표의 절댓값만 사용하며 양수·음수 위치를 같은 셀로 합친다. 초기 위치 반경이 50이고 속도 배열은 기본값 0이므로, 제시된 코드에서는 1,024개 셀 전체를 사용하지 않는다. 의도된 매핑이면 유지해도 되며, 변경은 별도 사양이다.
- **속도:** 제시된 코드에서 `_velocities`는 초기값 0이고 이후 쓰기가 없어 이동하지 않는다. 실제 이동이 기대되는 경우 **자료 부족: 속도 초기화·갱신 코드**.
- **환경별 오류 확정:** **자료 부족: 전체 예외 메시지와 스택, Unity 6000.x의 정확한 패치 버전, Collections/Burst 버전, 빌드 안전성 검사 설정**.
- **검증 상태:** 아래 변경은 컴파일·실행 검증하지 않은 제안이다.

## 3. 수정 방향

### A-01·A-04: 독점 부분 집계 → 셀별 병렬 합산

약 20,000개를 256개씩 나누면 부분 작업은 79개다. 각 작업은 자신만의 1,024칸에 쓰고, 최종 작업은 셀마다 부분 결과를 합산한다. 메인 스레드에서 개체를 순회하지 않는다.

```csharp
using Unity.Collections.LowLevel.Unsafe;

const int CellCount = 1024;
const int ItemsPerPartition = 256;

[BurstCompile]
struct CountPartitionsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> positions;

    // p번 실행은 [p * CellCount, (p + 1) * CellCount)만 쓴다.
    // 원래 공유 cellCounts에 이 속성만 붙이는 것과는 다르다.
    [NativeDisableParallelForRestriction]
    public NativeArray<int> partialCounts;

    public void Execute(int p)
    {
        int row = p * CellCount;

        for (int cell = 0; cell < CellCount; cell++)
            partialCounts[row + cell] = 0;

        int begin = p * ItemsPerPartition;
        int end = math.min(begin + ItemsPerPartition, positions.Length);

        for (int i = begin; i < end; i++)
        {
            int cell = (int)(math.abs(positions[i].x) % CellCount);
            partialCounts[row + cell]++;
        }
    }
}

[BurstCompile]
struct ReduceCountsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> partialCounts;
    public NativeArray<int> cellCounts;
    public int partitionCount;

    public void Execute(int cell)
    {
        int sum = 0;

        for (int p = 0; p < partitionCount; p++)
            sum += partialCounts[p * CellCount + cell];

        cellCounts[cell] = sum;
    }
}
```

- `p`가 다르면 쓰는 행이 겹치지 않는다.
- 최종 작업은 자기 `cell`에만 쓴다.
- 매 프레임 부분 결과를 초기화하고 최종 결과를 덮어써 누적을 방지한다.
- `NativeDisableParallelForRestriction`은 **증명된 독점 범위 쓰기**에만 적용한다. 원자성을 제공하는 속성이 아니다.
- 입력 위치는 유한한 값이라는 전제가 필요하다. 외부에서 위치를 수정한다면 NaN·무한대 처리 정책도 정해야 한다.

### A-02·A-06: 장기 할당과 초기화 검사

필드에 부분 집계 저장소와 작업 수를 추가하고 기존 위치·속도 할당도 `Persistent`로 바꾼다.

```csharp
private NativeArray<int> _partialCounts;
private int _partitionCount;

// Start의 배열 할당 전
if (count <= 0)
{
    enabled = false;
    throw new System.ArgumentOutOfRangeException(nameof(count));
}

_partitionCount = (count - 1) / ItemsPerPartition + 1;

_positions = new NativeArray<float3>(count, Allocator.Persistent);
_velocities = new NativeArray<float3>(count, Allocator.Persistent);
_cellCounts = new NativeArray<int>(CellCount, Allocator.Persistent);
_partialCounts = new NativeArray<int>(
    checked(_partitionCount * CellCount),
    Allocator.Persistent);
```

실행 중 인스펙터의 `count`가 바뀌어도 기존 배열 크기는 변하지 않는다. 작업 범위는 생성한 배열 길이를 기준으로 유지하고, 동적 재할당이 필요하면 완료 후 재생성 절차를 별도로 둔다.

### A-03: 의존성과 수명 관리

기존 `MoveJob`은 유지하고 예약 부분만 다음처럼 교체한다. 아래는 기존과 같은 **이동 전 집계**다.

```csharp
var counted = new CountPartitionsJob
{
    positions = _positions,
    partialCounts = _partialCounts
}.Schedule(_partitionCount, 1, _handle);

// 예약 직후 저장: 이후 예약에 실패해도 이미 예약한 작업을 잃지 않는다.
_handle = counted;

_handle = new ReduceCountsJob
{
    partialCounts = _partialCounts,
    cellCounts = _cellCounts,
    partitionCount = _partitionCount
}.Schedule(CellCount, 64, _handle);

_handle = new MoveJob
{
    positions = _positions,
    velocities = _velocities,
    dt = Time.deltaTime
}.Schedule(_positions.Length, 64, _handle);
```

이 순서는 단순성과 종료 처리를 우선한다. 합산과 이동의 동시 실행 최적화는 필수가 아니다.

```csharp
void OnDisable()
{
    _handle.Complete();
}

void OnDestroy()
{
    _handle.Complete();

    if (_positions.IsCreated) _positions.Dispose();
    if (_velocities.IsCreated) _velocities.Dispose();
    if (_cellCounts.IsCreated) _cellCounts.Dispose();
    if (_partialCounts.IsCreated) _partialCounts.Dispose();
}
```

`LateUpdate`에서도 기존처럼 `Complete()` 이후에만 배열을 읽는다. `OnDisable`에서는 해제하지 않으므로 다시 활성화해도 기존 배열을 사용할 수 있다.

### A-05: 비교 입력 고정 및 회귀 검증

초기 분포를 유지하려면 지역 난수 생성기를 사용한다.

```csharp
var rng = new Unity.Mathematics.Random(1u);

for (int i = 0; i < _positions.Length; i++)
{
    float radius = 50f * math.pow(rng.NextFloat(), 1f / 3f);
    _positions[i] = rng.NextFloat3Direction() * radius;
}
```

검증은 다음을 분리한다.

1. **집계 정확성:** 모든 위치를 0으로 고정해 셀 0이 매 프레임 20,000인지 확인한다.
2. **집계 보존:** 모든 셀 합이 매 프레임 `_positions.Length`인지 확인한다.
3. **반복 결정성:** 동일 입력으로 에디터와 빌드에서 1,024개 셀 전체를 비교한다. `cell0` 하나만 비교하지 않는다.
4. **경계:** 1, 255, 256, 257, 20,000개로 마지막 부분 작업의 범위를 검증한다.
5. **수명:** 4프레임을 넘겨 실행한 뒤 비활성화·재활성화, 즉시 파괴, 씬 언로드를 각각 반복한다.
6. **입력과 집계 분리:** 플랫폼 간 입력 생성까지 같다고 가정하지 말고, 저장된 동일 입력으로 집계 결과도 별도로 비교한다.
