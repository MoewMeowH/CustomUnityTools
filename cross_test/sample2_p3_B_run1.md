### 1. 상대 Phase 2 판정에 대한 응답

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| A2-01 | 수용 | [사실] `IJobParallelFor`의 인덱스 범위 위반(안전성 시스템 검출 대상)과 비원자적 읽기-수정-쓰기로 인한 데이터 경쟁(갱신 유실 원인)은 별개의 메커니즘이므로 구분하여 기술하는 것이 정확하다. 실제 예외 스택이 없으므로 단정하지 않는다(확정 사항 6). |
| A2-02 | 수용 | [사실] 정상적인 프레임 루프에서 `LateUpdate()`를 거친 뒤 파괴되면 미완료 Job이 없으며, `Update()` 직후 `LateUpdate()` 전에 파괴되는 특정 생명주기 경로에서만 미완료 해제가 발생한다. 모든 예약 단계의 핸들을 추적하고 완료 후 해제하도록 반영한다. |
| A2-03 | 수용 | [사실] `Allocator.TempJob`의 4프레임 초과가 곧바로 메모리 자동 해제를 뜻하는 것은 아니며, 할당자 수명 규약 위반 경고/오류의 원인이다. 장기 보관 배열이므로 `Allocator.Persistent`로 변경한다. |
| A2-04 | 수용 | [사실] 전역 난수 상태가 고정되지 않아 실행 간 입력이 달라질 수 있으나, 우연히 같은 셀 집계가 나올 가능성을 배제할 수는 없다. 확정 사항 1에 따라 고정 시드 기반의 `Unity.Mathematics.Random`을 도입하여 같은 시드·같은 틱 수에서 동일한 결과가 나오도록 보장한다. |
| A2-05 | 수용 | [사실] 프레임별 초기화가 없다는 점은 추론이 아니라 코드상 사실이다. 확정 사항 5에 따라 프레임별 새로 세기(기본값)와 누적 집계를 인스펙터 옵션(`accumulateCellCounts`)으로 선택할 수 있게 구현한다. |
| A2-06 | 수용 | [사실] 한 번도 활성화되지 않은 GameObject에서는 `OnDestroy()`가 호출되지 않으므로, 활성 GameObject에 부착된 비활성 컴포넌트나 `Start()` 도중 예외가 발생한 경로로 발생 조건을 정정한다. `IsCreated` 확인과 초기화 완료 플래그(`_initialized`)로 방어한다. |
| A2-07 | 수용 | [사실] 사용자 확정 사항 4에서 `unsafe` 코드를 허용하지 않기로 결정되었으므로, 포인터와 `Interlocked` 방식을 완전히 폐기하고 청크별 독립 히스토그램(`CountChunksJob`) 및 병렬 합산(`ReduceCountsJob`) 방식을 채택한다. |
| A2-08 | 수용 | [사실] `UnsafeUtility.AddressOf` 등 포인터 연산을 쓰려면 `unsafe` 컨텍스트가 필요하다는 지적은 맞다. 내 Phase 1의 비고 설명이 부정확했음을 인정하며, 확정 사항 4에 따라 `unsafe` 없는 방식으로 대체한다. |
| A2-09 | 수용 | [사실] 다단계 Job 예약 중 후속 예약이 실패할 경우 앞선 Job의 핸들을 잃지 않도록 매 `Schedule` 직후 `_handle`을 즉시 갱신해야 한다. |
| A2-10 | 수용 | [사실] 방향 벡터에 선형 난수(`NextFloat(0f, 50f)`)를 곱하면 구 중심부로 밀도가 쏠린다. 원본(`insideUnitSphere * 50f`)의 구 내부 균일 분포를 보존하기 위해 반지름에 세제곱근(`math.pow(rng.NextFloat(), 1f / 3f)`)을 적용한다. |
| A2-11 | 수용 | [사실] 메인 스레드에서 `MemClear`를 수행하면 `unsafe`가 필요할 뿐만 아니라 미완료 Job과의 경쟁 위험이 있다. `ReduceCountsJob`에서 프레임별 덮어쓰기 또는 누적 덧셈을 수행하도록 처리하여 메인 스레드 초기화를 제거한다. |
| A2-12 | 수용 | [사실] 구 내부 균일 분포 수식을 기본으로 사용하는 것에 동의한다. |
| A2-13 | 수용 | [사실] `Start()`에서 `count <= 0`이거나 할당 중 예외가 발생했을 때 `Update()`·`LateUpdate()`가 실행되지 않도록 `_initialized` 플래그와 `try-catch` 정리 로직을 추가한다. |

### 2. 최종 수정 코드

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class BoidSystem : MonoBehaviour
{
    private const int CellCount = 1024;
    private const int ChunkSize = 256;

    [SerializeField] private int count = 20000;
    [SerializeField] private uint randomSeed = 12345u;
    // 확정 사항 5: 프레임마다 새로 셀지(false) 누적할지(true) 미정이므로 선택 가능하게 제공
    [SerializeField] private bool accumulateCellCounts = false;

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<int> _cellCounts;
    private NativeArray<int> _partialCounts;

    private JobHandle _handle;
    private int _chunkCount;
    private bool _initialized;

    void Start()
    {
        if (count <= 0)
        {
            Debug.LogError($"[BoidSystem] count({count})는 0보다 커야 합니다.");
            enabled = false;
            return;
        }

        uint seed = randomSeed != 0u ? randomSeed : 1u;
        _chunkCount = (count + ChunkSize - 1) / ChunkSize;

        try
        {
            // 장기 보관 배열은 Allocator.Persistent로 할당
            _positions = new NativeArray<float3>(count, Allocator.Persistent);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent);
            _cellCounts = new NativeArray<int>(CellCount, Allocator.Persistent);
            _partialCounts = new NativeArray<int>(checked(_chunkCount * CellCount), Allocator.Persistent);

            // 확정 사항 1: 같은 시드에서 같은 초기 상태를 보장하며, 원본의 구 내부 균일 분포를 보존
            var rng = new Unity.Mathematics.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float radius = 50f * math.pow(rng.NextFloat(), 1f / 3f);
                _positions[i] = rng.NextFloat3Direction() * radius;
            }

            _initialized = true;
        }
        catch
        {
            DisposeArrays();
            enabled = false;
            throw;
        }
    }

    [BurstCompile]
    struct CountChunksJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;

        // 각 chunk 인덱스가 [chunk * CellCount, (chunk + 1) * CellCount) 구간을 독점하므로 스레드 간 쓰기 충돌이 없음
        [NativeDisableParallelForRestriction]
        public NativeArray<int> partialCounts;

        public void Execute(int chunk)
        {
            int row = chunk * CellCount;

            for (int cell = 0; cell < CellCount; cell++)
            {
                partialCounts[row + cell] = 0;
            }

            int begin = chunk * ChunkSize;
            int end = math.min(begin + ChunkSize, positions.Length);

            for (int i = begin; i < end; i++)
            {
                // 확정 사항 5: 원본 셀 수식 보존
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
        public int chunkCount;
        public bool accumulate;

        public void Execute(int cell)
        {
            int sum = 0;
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                sum += partialCounts[chunk * CellCount + cell];
            }

            if (accumulate)
            {
                cellCounts[cell] += sum;
            }
            else
            {
                cellCounts[cell] = sum;
            }
        }
    }

    [BurstCompile]
    struct MoveJob : IJobParallelFor
    {
        public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> velocities;
        public float dt;

        public void Execute(int i)
        {
            positions[i] += velocities[i] * dt;
        }
    }

    void Update()
    {
        if (!_initialized) return;

        // 각 예약 직후 _handle을 즉시 갱신하여 중간 예외 발생 시에도 핸들 추적을 보장
        var countChunksJob = new CountChunksJob
        {
            positions = _positions,
            partialCounts = _partialCounts
        };
        _handle = countChunksJob.Schedule(_chunkCount, 1, _handle);

        var reduceCountsJob = new ReduceCountsJob
        {
            partialCounts = _partialCounts,
            cellCounts = _cellCounts,
            chunkCount = _chunkCount,
            accumulate = accumulateCellCounts
        };
        _handle = reduceCountsJob.Schedule(CellCount, 64, _handle);

        var moveJob = new MoveJob
        {
            positions = _positions,
            velocities = _velocities,
            dt = Time.deltaTime
        };
        _handle = moveJob.Schedule(_positions.Length, 64, _handle);
    }

    void LateUpdate()
    {
        if (!_initialized) return;

        _handle.Complete();
        transform.position = _positions[0];
        Debug.Log($"cell0={_cellCounts[0]}");
    }

    void OnDestroy()
    {
        _handle.Complete();
        DisposeArrays();
    }

    private void DisposeArrays()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_velocities.IsCreated) _velocities.Dispose();
        if (_cellCounts.IsCreated) _cellCounts.Dispose();
        if (_partialCounts.IsCreated) _partialCounts.Dispose();
    }
}
```

### 3. 자기 보고
- 전체 줄 수: 179줄
- 빈 줄 제외 줄 수: 151줄
- Job 구조체 수: 총 3개 (병렬 `IJobParallelFor` 3개, 직렬 0개)
- NativeArray 필드 수: 컴포넌트 필드 4개 (`_positions`, `_velocities`, `_cellCounts`, `_partialCounts`), Job 구조체 내부 필드 6개 (`CountChunksJob` 2개, `ReduceCountsJob` 2개, `MoveJob` 2개)

### 4. 확정 사항 반영 내역
- **확정 사항 1 (결정성: 같은 시드, 같은 시뮬레이션 틱 수에서 같은 결과)**: `Start()`에서 고정 가능한 시드(`randomSeed`, 0일 경우 1u로 보정)를 사용하는 `Unity.Mathematics.Random`으로 초기 위치를 생성하고, `CountChunksJob`과 `ReduceCountsJob`이 정해진 청크 인덱스(`0` ~ `_chunkCount - 1`) 순서대로 합산하도록 구현하여 같은 시드·같은 틱 수에서 항상 동일한 결과가 나오게 반영했다.
- **확정 사항 2 (프레임 레이트 독립성 및 플랫폼 간 비트 단위 동일성 범위 제외)**: `Update()`에서 원본의 `dt = Time.deltaTime`을 그대로 유지하고 별도의 고정 시간 단계 루프를 추가하지 않았으며, 미해결 쟁점(B3-03)으로 분리했다.
- **확정 사항 3 (병렬 쓰기 경쟁으로 인한 `cellCounts` 변동 제거)**: 워커 스레드 간 동일 셀 동시 쓰기를 없애기 위해 `CountChunksJob`에서 청크별 배타적 구간(`[chunk * CellCount, (chunk + 1) * CellCount)`)에 부분 히스토그램을 기록하고, `ReduceCountsJob`에서 셀별로 합산하도록 반영했다.
- **확정 사항 4 (`unsafe` 코드 불허)**: `unsafe` 키워드, 포인터(`GetUnsafePtr`), `Interlocked`, `UnsafeUtility.MemClear`를 모두 사용하지 않고 `NativeArray` 인덱서만으로 동작하도록 구현했다.
- **확정 사항 5 (미확정 사양 보존 및 선택 제공)**:
  - `cellCounts` 프레임별 초기화 vs 누적: `[SerializeField] private bool accumulateCellCounts = false;` 필드를 추가하고 `ReduceCountsJob.Execute()`에서 `accumulate` 값에 따라 덮어쓰기(`= sum`)와 누적(`+= sum`)을 선택할 수 있게 반영했다(기본값은 프레임별 새로 세기).
  - 속도가 0인 상태 보존: `_velocities`를 `Allocator.Persistent`로 생성 시 기본 0 초기화를 유지하고 임의의 속도 값을 채우지 않았다.
  - 셀 수식 보존: `CountChunksJob.Execute()`에서 원본 수식 `(int)(math.abs(positions[i].x) % CellCount)`를 그대로 유지했다.
- **확정 사항 6 (예외 메시지 및 스택 트레이스 확보 예정)**: 미해결 쟁점(B3-01)과 테스트 계획(순서 1)에 실행 검증 단계에서의 로그 확보 절차를 반영했다.

### 5. 미해결 쟁점
- **B3-01 (실제 예외 메시지 및 스택 트레이스 대조)**: 실행 전 정적 검토 단계이므로 원본 코드에서 발생한 `InvalidOperationException`과 씬 종료 에러의 정확한 스택 트레이스가 아직 확보되지 않았다(확정 사항 6). 실행 검증 단계에서 원본 로그를 수집하여 수정된 결함(`CountJob` 인덱스 범위 위반, `TempJob` 수명 초과, 미완료 Job 해제)과 일대일로 대응하는지 최종 확인해야 한다.
- **B3-02 (미확정 도메인 사양 3가지)**: `cellCounts`의 프레임별 초기화 여부(`accumulateCellCounts`), `_velocities`가 0으로 유지되는 것이 의도인지 여부, 셀 계산식 `abs(x) % 1024`의 의도 여부는 추후 기획/설계 사양이 확정될 때 결정해야 한다(확정 사항 5). 또한 `_velocities`에 실제 값이 들어가고 위치의 `x` 좌표가 `NaN`이나 음수로 변환될 수 있는 오버플로우 범위를 가질 경우 `cell` 인덱스 범위 방어가 추가로 필요할 수 있다.
- **B3-03 (범위 밖 — 프레임 레이트 독립성 및 플랫폼 간 비트 단위 동일성)**: 현재 `_velocities`가 0이므로 `Time.deltaTime`이 위치에 영향을 주지 않으나, 추후 속도가 채워지는 시점에는 실행마다 달라지는 `Time.deltaTime`과 CPU/Burst 타겟별 부동소수점 연산 차이로 인해 틱 간 위치가 달라질 수 있다. 속도가 도입될 때 고정 델타 타임(`FixedUpdate` 또는 고정 `dt`) 및 결정론적 연산 요구사항을 다시 검토한다(확정 사항 2).

### 6. 테스트 계획

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | 원본 코드 예외 및 스택 트레이스 수집 | 수정 전 원본 코드를 에디터(Safety Checks 켜짐)에서 실행 및 씬 종료하여 `InvalidOperationException`과 종료 시 에러의 전체 메시지·스택 트레이스를 확보하고, 수정 코드 적용 후 동일 조건에서 해당 에러가 0건인지 확인한다. |
| 2 | 단일 셀 집중 및 병렬 경쟁 검증 | 테스트용으로 모든 `_positions[i]`를 `float3.zero`로 고정하고 에디터와 릴리즈 빌드에서 각각 실행한다. `accumulateCellCounts = false`일 때 매 프레임 `_cellCounts[0] == count`(20000)이고 나머지 `1~1023`번 셀은 모두 `0`이어야 한다. |
| 3 | 시드 고정 및 틱 단위 결정성 검증 | 동일한 `randomSeed`(예: `12345`)로 에디터와 빌드를 각각 3회씩 재시작하여 동일한 시뮬레이션 틱(예: 1프레임, 10프레임)에서 1024개 전체 `_cellCounts` 배열 값과 `_positions[0]` 값이 실행마다 100% 일치하는지 확인한다. |
| 4 | 집계 모드(`accumulateCellCounts`) 분기 검증 | 정지 상태(`_velocities` = 0)에서 `accumulateCellCounts = false`일 때는 `N`프레임 후 전체 셀의 합이 항상 `count`이고, `accumulateCellCounts = true`일 때는 완료된 프레임 수 `N`에 대해 전체 셀의 합이 정확히 `count * N`인지 확인한다. |
| 5 | 초기 위치 구 내부 균일 분포 검증 | `Start()` 직후 생성된 20,000개 위치의 원점 거리 `r = math.length(_positions[i])`가 모두 `0f <= r <= 50f` 범위 내에 있고, 반지름 `25f` 이하인 개체 비율이 약 `(25/50)^3 = 12.5%` 근방(표본 오차 범위 내)으로 분포하는지 확인한다. |
| 6 | 할당자 수명 및 비정상 생명주기 종료 검증 | ① 10프레임 이상 실행하여 `JobTempAlloc` 수명 초과 경고가 없는지 확인한다. ② `Update()` 직후(`LateUpdate()` 전) 씬 전환/오브젝트 파괴 시 에러가 없는지 확인한다. ③ `count = 0` 또는 `-1` 설정 시 에러 로그 출력 후 컴포넌트가 안전하게 비활성화(`enabled == false`)되고 `OnDestroy()`에서 예외가 나지 않는지 확인한다. |