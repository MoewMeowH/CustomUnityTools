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
