### 1. 상대 Phase 2 판정에 대한 응답

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| A2-01 (B3-01) | 수용 | [사실] `IJobParallelFor`의 인덱스 범위 위반(Safety System이 정적/동적으로 검출)과 다중 스레드의 비원자적 읽기-수정-쓰기로 인한 데이터 경쟁(Lost Update)은 엄밀히 구분하는 것이 맞다. 실제 예외 메시지와 스택 트레이스는 실행 검증 단계에서 확보해야 한다. |
| A2-02 (B3-02) | 수용 | [사실] 정상적인 프레임 루프에서 `LateUpdate()`를 거친 뒤 파괴되면 미완료 Job이 없으며, `Update()` 직후 `LateUpdate()` 전에 파괴되는 특정 콜백 순서나 스케줄링 중 예외 발생 시에만 미완료 핸들 문제가 발생한다는 지적을 수용한다. |
| A2-03 (B3-03) | 수용 | [사실] `Allocator.TempJob`의 4프레임 초과가 메모리의 즉각적인 자동 해제를 의미하는 것은 아니며, 할당자 수명 규약 위반 경고/에러의 원인이다. 장기 보유 배열은 `Allocator.Persistent`로 변경한다. |
| A2-04 (B3-04) | 수용 | [사실] 전역 난수 상태가 고정되지 않아 실행마다 입력이 달라질 수 있으나, 우연히 같은 셀 집계가 나올 가능성도 존재한다. 사용자 확정 사항 1(같은 시드, 같은 틱 수에서 같은 결과)을 만족하기 위해 고정 시드 기반의 로컬 `Unity.Mathematics.Random`을 사용한다. |
| A2-05 (B3-05) | 수용 | [사실] 초기화 코드가 없다는 점은 추론이 아닌 코드상 사실이며, 사용자 확정 사항 5에 따라 프레임별 집계인지 누적 집계인지 아직 미정이므로 인스펙터 옵션(`accumulateCounts`)으로 두 동작을 모두 지원하도록 구현한다. |
| A2-06 (B3-06) | 수용 | [사실] 한 번도 활성화되지 않은 GameObject에서는 `OnDestroy()`가 호출되지 않으므로, 내 Phase 1(B-06)의 발생 조건 설명이 부정확했음을 인정하고 수정한다. 활성 GameObject에서 컴포넌트만 비활성화(`enabled = false`)되었다가 파괴되는 경우나 `Start()` 중 예외로 부분 할당된 경우에 `IsCreated` 확인이 필요하다. |
| A2-07 (B3-07) | 수용 | [사실] 사용자 확정 사항 4에서 `unsafe` 코드가 금지되었으므로, 포인터 기반 원자 연산 대신 안전한 청크별 히스토그램(`CountChunksJob`) 및 병렬 합산(`ReduceCountsJob`) 방식을 최종 채택한다. |
| A2-08 (B3-08) | 수용 | [사실] `UnsafeUtility.AddressOf` 역시 포인터를 다루므로 `unsafe` 컨텍스트가 필요하다는 지적을 인정하며, 내 Phase 1의 비고 설명을 철회한다. 최종 코드에서는 `unsafe`를 일절 사용하지 않는다. |
| A2-09 (B3-09) | 수용 | [사실] 첫 번째 Job 예약 후 두 번째 예약에서 예외가 발생할 경우 핸들 추적이 유실되는 결함을 막기 위해, 매 `Schedule()` 호출 직후 결과를 즉시 `_handle`에 대입하고 체이닝한다. |
| A2-10 (B3-10) | 수용 | [사실] 내 Phase 1에서 반지름에 선형 난수를 곱해 구 중심부로 밀도가 쏠리게 한 것은 분포 변경 실수였음을 인정한다. 원문의 구 내부 균일 분포를 보존하기 위해 세제곱근(`math.pow(rng.NextFloat(), 1f / 3f)`) 수식을 적용한다. |
| A2-11 (B3-11) | 수용 | [사실] 메인 스레드 초기화(`MemClear`)는 `unsafe`가 필요하고 미완료 Job과의 경쟁 위험이 있으므로 철회한다. `ReduceCountsJob`에서 `accumulate` 플래그에 따라 값을 덮어쓰거나(`=`) 더하는(`+=`) 방식으로 안전하게 처리한다. |
| A2-12 (B3-12) | 수용 | [사실] 리뷰 A가 스스로 수정한 구 내부 균일 분포 수식에 동의하며 최종 코드에 반영한다. |
| A2-13 (B3-13) | 수용 | [사실] `Start()`에서 `count <= 0`이거나 할당 실패 시 이후 `Update()`와 `LateUpdate()`가 실행되면서 2차 예외가 발생하는 것을 막기 위해, `_initialized` 플래그를 두어 초기화를 방어하는 설계를 수용한다. |

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
    [SerializeField] private uint seed = 12345u;
    [SerializeField] private bool accumulateCounts = false;

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
            Debug.LogError($"count({count})는 0보다 커야 합니다. 컴포넌트를 비활성화합니다.");
            enabled = false;
            return;
        }

        uint validSeed = seed != 0u ? seed : 1u;
        _chunkCount = (count + ChunkSize - 1) / ChunkSize;

        _positions = new NativeArray<float3>(count, Allocator.Persistent);
        _velocities = new NativeArray<float3>(count, Allocator.Persistent);
        _cellCounts = new NativeArray<int>(CellCount, Allocator.Persistent);
        _partialCounts = new NativeArray<int>(checked(_chunkCount * CellCount), Allocator.Persistent);

        // 구 내부 균일 분포를 유지하며 고정 시드로 결정론적 초기 위치 생성
        var rng = new Unity.Mathematics.Random(validSeed);
        for (int i = 0; i < count; i++)
        {
            float3 dir = rng.NextFloat3Direction();
            float radius = 50f * math.pow(rng.NextFloat(), 1f / 3f);
            _positions[i] = dir * radius;
        }

        _initialized = true;
    }

    [BurstCompile]
    struct CountChunksJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;

        // 각 청크(chunk) 인덱스가 배타적인 메모리 구간 [chunk * CellCount, (chunk + 1) * CellCount)만 쓰므로 스레드 안전함
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
                // 원본 셀 수식 보존
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
            accumulate = accumulateCounts
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

        if (_positions.IsCreated) _positions.Dispose();
        if (_velocities.IsCreated) _velocities.Dispose();
        if (_cellCounts.IsCreated) _cellCounts.Dispose();
        if (_partialCounts.IsCreated) _partialCounts.Dispose();
    }
}
```

### 3. 자기 보고

- 전체 줄 수: 162줄
- 빈 줄 제외 줄 수: 136줄
- Job 구조체 수: 총 3개 (병렬 `IJobParallelFor` 3개, 직렬 `IJob` 0개)
- NativeArray 필드 수: 4개 (`_positions`, `_velocities`, `_cellCounts`, `_partialCounts`)

### 4. 확정 사항 반영 내역

1. **결정성의 의미 확정(같은 시드, 같은 시뮬레이션 틱 수에서 같은 결과)**: `SerializeField`로 `seed`(기본값 `12345u`)를 추가하고, `Start()`에서 `Unity.Mathematics.Random(validSeed)`를 사용해 초기 위치를 생성하도록 반영했다. 또한 `ReduceCountsJob`에서 청크 인덱스를 `0`부터 `chunkCount - 1`까지 고정된 순서로 합산하여 스레드 스케줄링과 무관하게 동일한 틱에서 항상 같은 결과가 나오도록 보장했다.
2. **프레임 레이트 독립성 및 플랫폼 간 비트 단위 동일성 제외**: 시간 관리 체계를 변경하지 않고 `MoveJob`의 `dt = Time.deltaTime`을 그대로 유지했으며, 관련 내용은 섹션 5(미해결 쟁점)로 이관했다.
3. **성공 기준 의도 반영(병렬 쓰기 경쟁 제거)**: 공유 배열에 직접 `++` 하던 `CountJob`을 제거하고, 청크별 독립 버퍼에 기록하는 `CountChunksJob`과 셀 단위로 병렬 합산하는 `ReduceCountsJob`으로 분리하여 에디터의 Safety Check 예외와 빌드의 데이터 경쟁을 동시에 해결했다.
4. **unsafe 코드 금지**: 내 Phase 1에서 제안했던 `unsafe` 포인터 및 `Interlocked.Increment`, `UnsafeUtility.MemClear`를 모두 배제하고 안전한 `NativeArray` 인덱서만 사용했다.
5. **미확정 사양 보존 및 선택 가능 처리**:
   - `_cellCounts` 프레임별 초기화 vs 누적: `[SerializeField] private bool accumulateCounts = false;` 필드를 추가하고 `ReduceCountsJob`에서 `accumulate` 여부에 따라 대입(`=`) 또는 가산(`+=`)을 선택할 수 있게 했다(기본값은 프레임별 새로 집계로 가정).
   - 속도가 0인 동작 보존: `_velocities`에 임의의 값을 채우지 않고 기본 `0` 초기화 상태와 `MoveJob`을 원본 그대로 보존했다.
   - 셀 수식 보존: `CountChunksJob.Execute`에서 `(int)(math.abs(positions[i].x) % CellCount)` 수식을 그대로 유지했다.
   - 초기 위치 분포 보존: `rng.NextFloat3Direction() * (50f * math.pow(rng.NextFloat(), 1f / 3f))`를 사용하여 원본 `insideUnitSphere * 50f`의 구 내부 균일 분포를 유지했다.
6. **예외 메시지 및 스택 트레이스 확보**: 섹션 6(테스트 계획)의 첫 단계에서 원본 코드로 재현 및 로그를 수집하는 절차로 반영했다.

### 5. 미해결 쟁점

- **B3-14 (`_cellCounts` 집계 방식 사양 미정)**: 프레임마다 새로 셀지(`accumulateCounts = false`), 시뮬레이션 시작부터 누적할지(`accumulateCounts = true`) 최종 사양이 확정되지 않았다. 누적 집계로 확정될 경우 장시간 실행 시 `int` 오버플로우 방어 대책이 추가로 필요하다.
- **B3-15 (`_velocities` 초기화 사양 및 이동 후 결정성 — 범위 밖)**: 현재 `_velocities`가 `0`으로 유지되는 것이 의도인지 미정이다. 추후 속도에 0이 아닌 값이 채워지는 시점에는 `Time.deltaTime` 변동에 따른 프레임 레이트 독립성(고정 틱 `dt` 도입 여부)과 플랫폼/CPU 간 부동소수점 연산의 비트 단위 동일성 보장 여부를 다시 검토해야 한다(범위 밖).
- **B3-16 (셀 수식 `abs(x) % 1024`의 의도 여부)**: 음수 `x` 좌표와 양수 `x` 좌표가 같은 셀로 합쳐지는 현재 수식이 의도된 설계인지 확인이 필요하다. 또한 추후 개체가 이동하여 `positions[i].x`가 `NaN`이나 `Infinity`가 될 가능성이 생길 경우 `(int)` 캐스팅 시 음수 인덱스가 발생할 수 있으므로 범위 클램핑 검토가 필요하다.
- **B3-17 (원본 예외 로그와의 1:1 대조 미완)**: 코드 정적 분석으로 확인한 3가지 결함(`IJobParallelFor` 쓰기 제한 위반, `TempJob` 4프레임 수명 초과, `OnDestroy` 미완료 핸들 해제)과 실제 에디터/종료 에러 스택 트레이스 간의 최종 대조는 실행 검증 단계에서 닫아야 한다.

### 6. 테스트 계획

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | 원본 코드 예외 및 스택 트레이스 수집 | 수정 전 원본 코드를 에디터(Safety Checks 켜짐)에서 실행하여 `InvalidOperationException` 메시지·스택을 확보하고, 5프레임 이상 실행 및 씬 종료 반복 시 발생하는 할당자/Dispose 에러 전문을 기록한다. |
| 2 | 단일 셀 집중을 통한 병렬 쓰기 경쟁 검증 | 수정 코드에서 모든 초기 위치를 `float3.zero`로 고정하고(`accumulateCounts = false`), 에디터와 릴리즈 빌드에서 각각 100프레임 실행할 때 예외가 발생하지 않으며 매 프레임 `_cellCounts[0] == 20000`이고 나머지 1023개 셀은 모두 `0`이어야 한다. |
| 3 | 시드 고정 및 틱 수 기준 결정성 검증 | 기본 시드(`12345u`) 상태로 에디터와 빌드에서 각각 10회씩 재실행하여, 1번째 틱과 10번째 틱에서의 `cell0` 값 및 전체 1024개 셀의 배열 요소 값이 모든 실행에서 100% 동일한지 확인한다. |
| 4 | 집계 모드(`accumulateCounts`) 분기 검증 | 고정 위치 조건에서 `accumulateCounts = false`일 때는 매 프레임 전체 셀 합계가 `count(20000)`로 일정하고, `accumulateCounts = true`일 때는 N번째 프레임(`LateUpdate`)의 전체 셀 합계가 정확히 `count * N`인지 확인한다. |
| 5 | 할당자 수명 및 비정상 종료/예외 안전성 검증 | 수정 코드를 100프레임 이상 실행하여 `JobTempAlloc` 경고가 없는지 확인한다. 또한 ① `count = 0` 설정 시 안전하게 비활성화되는지, ② `Update()` 직후(`LateUpdate()` 전) 게임 오브젝트 파괴 또는 씬 전환 시 에러 없이 종료되는지, ③ `CountChunksJob` 스케줄링 직후 강제 예외 주입 시 `OnDestroy()`에서 핸들이 정상 완료 및 해제되는지 확인한다. |