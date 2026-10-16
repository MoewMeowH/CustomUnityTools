using System;
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
    [SerializeField] private int seed = 12345;
    // 기본값은 원본의 누적 동작을 보존한다.
    [SerializeField] private bool accumulateCounts = true;

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<int> _cellCounts;
    private NativeArray<int> _partialCounts;
    private JobHandle _handle;
    private int _chunkCount;
    private bool _initialized;

    private void Start()
    {
        try
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "개체 수는 양수여야 합니다.");

            _chunkCount = 1 + (count - 1) / ChunkSize;
            int partialLength = checked(_chunkCount * CellCount);
            _positions = new NativeArray<float3>(count, Allocator.Persistent);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent);
            _cellCounts = new NativeArray<int>(CellCount, Allocator.Persistent);
            _partialCounts = new NativeArray<int>(partialLength, Allocator.Persistent);

            var previousState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(seed);
                for (int i = 0; i < count; i++)
                    _positions[i] = UnityEngine.Random.insideUnitSphere * 50f;
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }
            _initialized = true;
        }
        catch (Exception exception)
        {
            DisposeArrays();
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    [BurstCompile]
    private struct CountChunksJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        // 각 청크는 서로 겹치지 않는 한 행만 초기화하고 갱신한다.
        [NativeDisableParallelForRestriction]
        public NativeArray<int> partialCounts;

        public void Execute(int chunk)
        {
            int row = chunk * CellCount;
            for (int cell = 0; cell < CellCount; cell++)
                partialCounts[row + cell] = 0;

            int begin = chunk * ChunkSize;
            int end = begin + math.min(ChunkSize, positions.Length - begin);
            for (int i = begin; i < end; i++)
            {
                int cell = (int)(math.abs(positions[i].x) % CellCount);
                partialCounts[row + cell]++;
            }
        }
    }

    [BurstCompile]
    private struct ReduceCountsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> partialCounts;
        public NativeArray<int> cellCounts;
        public int chunkCount;
        public bool accumulate;

        public void Execute(int cell)
        {
            int sum = 0;
            for (int chunk = 0; chunk < chunkCount; chunk++)
                sum += partialCounts[chunk * CellCount + cell];

            cellCounts[cell] = accumulate ? unchecked(cellCounts[cell] + sum) : sum;
        }
    }

    [BurstCompile]
    private struct MoveJob : IJobParallelFor
    {
        public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> velocities;
        public float dt;

        public void Execute(int i)
        {
            positions[i] += velocities[i] * dt;
        }
    }

    private void Update()
    {
        if (!_initialized)
            return;

        try
        {
            _handle = new CountChunksJob
            {
                positions = _positions,
                partialCounts = _partialCounts
            }.Schedule(_chunkCount, 1, _handle);

            _handle = new ReduceCountsJob
            {
                partialCounts = _partialCounts,
                cellCounts = _cellCounts,
                chunkCount = _chunkCount,
                accumulate = accumulateCounts
            }.Schedule(CellCount, 64, _handle);

            _handle = new MoveJob
            {
                positions = _positions,
                velocities = _velocities,
                dt = Time.deltaTime
            }.Schedule(_positions.Length, 64, _handle);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            // 비활성화 시 이미 예약된 작업을 완료하고 추가 틱을 중단한다.
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || !enabled)
            return;

        CompleteJobs();
        transform.position = _positions[0];
        Debug.Log($"cell0={_cellCounts[0]}");
    }

    private void OnDisable()
    {
        CompleteJobs();
    }

    private void OnDestroy()
    {
        _initialized = false;
        // 완료가 실패하면 안전한 해제를 보장할 수 없으므로 해제로 진행하지 않는다.
        CompleteJobs();
        DisposeArrays();
    }

    private void CompleteJobs()
    {
        _handle.Complete();
        _handle = default;
    }

    private void DisposeArrays()
    {
        if (_positions.IsCreated)
            _positions.Dispose();
        if (_velocities.IsCreated)
            _velocities.Dispose();
        if (_cellCounts.IsCreated)
            _cellCounts.Dispose();
        if (_partialCounts.IsCreated)
            _partialCounts.Dispose();
    }
}
