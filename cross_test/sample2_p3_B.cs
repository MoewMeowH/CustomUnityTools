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
