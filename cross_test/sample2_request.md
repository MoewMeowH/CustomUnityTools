# 검토 요청: 디버깅 (Unity 6 / Job System · Burst)

## 1. 검토 대상
- 종류: 버그 상황 + 코드
- 증상: "에디터에서 실행하면 가끔 InvalidOperationException이 나고, 빌드에서는 예외 없이 cell0 값이 실행마다 달라져요. 그리고 씬을 닫을 때 간헐적으로 에러가 떠요."

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class BoidSystem : MonoBehaviour
{
    [SerializeField] private int count = 20000;

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<int> _cellCounts;
    private JobHandle _handle;

    void Start()
    {
        _positions  = new NativeArray<float3>(count, Allocator.TempJob);
        _velocities = new NativeArray<float3>(count, Allocator.TempJob);
        _cellCounts = new NativeArray<int>(1024, Allocator.Persistent);

        for (int i = 0; i < count; i++)
            _positions[i] = UnityEngine.Random.insideUnitSphere * 50f;
    }

    [BurstCompile]
    struct CountJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        public NativeArray<int> cellCounts;

        public void Execute(int i)
        {
            int cell = (int)(math.abs(positions[i].x) % 1024);
            cellCounts[cell]++;
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
        var countJob = new CountJob { positions = _positions, cellCounts = _cellCounts };
        var h1 = countJob.Schedule(_positions.Length, 64);

        var moveJob = new MoveJob
        {
            positions = _positions,
            velocities = _velocities,
            dt = Time.deltaTime
        };
        _handle = moveJob.Schedule(_positions.Length, 64, h1);
    }

    void LateUpdate()
    {
        _handle.Complete();
        transform.position = _positions[0];
        Debug.Log($"cell0={_cellCounts[0]}");
    }

    void OnDestroy()
    {
        _positions.Dispose();
        _velocities.Dispose();
        _cellCounts.Dispose();
    }
}
```

## 2. 목표
위 세 가지 증상(에디터 예외, 빌드에서 cell0 값 불일치, 씬 종료 시 간헐적 에러)의 원인을 찾고, 근본 원인을 해결하는 수정안을 얻는 것.

## 3. 제약조건
- 언어/버전: C# 9, Unity 6 (6000.x)
- 사용 기술: Job System, Burst, Unity.Collections (NativeArray)
- 유지해야 할 것: 개체 수(약 20000)에서 Job 기반 병렬 처리 구조는 유지 (메인 스레드 단일 처리로의 후퇴는 불가)
- 환경 차이: 증상이 에디터와 빌드에서 다르게 나타남
- 확인하지 못한 것: 이 코드를 Unity에서 실제로 컴파일·실행해본 적은 없음

## 4. 성공 기준
- 세 증상이 각각 어떤 원인에서 오는지 구분되어 설명될 것.
- 에디터 예외를 단순히 억제하는 방식이 아니라, 에디터와 빌드 양쪽에서 결과가 결정적(deterministic)으로 나오는 수정안일 것.

## 5. 현재 의견/가설
- 없음 (독립 검토 상태로 진행)
