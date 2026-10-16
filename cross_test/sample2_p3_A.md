## 1. 상대 Phase 2 판정에 대한 응답

정적 검토 결과이며, 아래 코드를 Unity에서 컴파일·실행하지 않았다.

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| B2-01 | 부분 수용 | **A3-01 / 높음.** 공유 셀의 비원자적 증가와 병렬 쓰기 범위 위반은 사실이다. 다만 보고된 예외의 형식·발생 지점은 미확인이다. 독립 청크 집계로 수정하고, 테스트 2·4에서 정확성과 실제 예외 스택을 확인한다. |
| B2-02 | 부분 수용 | **A3-02 / 중간·사양 의존.** 초기화하지 않는 것은 사실이나 누적 자체가 결함인지는 미정이다. 시간 관리 변경은 확정 사항상 범위 밖이다. 원본처럼 누적을 기본값으로 보존하고, 프레임별 집계도 선택 가능하게 한다. 테스트 3으로 구분한다. |
| B2-03 | 부분 수용 | **A3-03 / 중간.** 전역 난수 상태를 고정하지 않아 초기 입력이 달라질 수 있다. 그러나 매번 반드시 달라지거나 `cell0`가 달라진다고 단정할 수는 없다. 고정 시드로 원본 샘플러를 호출하고 전역 상태를 복원한다. 테스트 5로 초기 배열과 전체 셀을 비교한다. |
| B2-04 | 부분 수용 | **A3-04 / 높음.** 장기 보유에 `TempJob`은 부적합하다. 다만 4프레임 뒤 메모리가 자동으로 해제된다는 의미는 아니다. 소유 배열을 `Persistent`로 변경하고 테스트 8에서 수명 진단·누수를 확인한다. |
| B2-05 | 부분 수용 | **A3-05 / 높음.** 미완료 작업이 남은 종료 경로에서는 완료 후 해제가 필요하다. 정상 `LateUpdate()` 이후에는 이미 완료되어 있다. 한 번도 활성화되지 않은 오브젝트의 파괴만으로 재현된다고 단정하지 않는다. 생성 여부 검사와 완료 처리를 적용하고 테스트 7·8에서 실제 콜백 순서를 기록한다. |
| B2-06 | 수용 | **A3-06 / 높음.** 뒤쪽 예약이 실패하면 이전에 성공한 예약의 핸들을 잃을 수 있다. 예약 성공마다 즉시 저장한다. 테스트 7에서 예약 사이 예외를 주입한다. |
| B2-07 | 수용 | **A3-07 / 낮음.** 양수가 아닌 개체 수는 할당 또는 첫 요소 접근에 문제가 된다. 초기화 전에 거부하고 후속 실행을 차단한다. 테스트 6으로 확인한다. |
| B2-08 | 수용 | **A3-08 / 중간.** 내 Phase 1 기본안이 구 표면 분포로 바뀐 것은 잘못이었다. 이를 명시적으로 철회한다. 최종안은 원본의 `insideUnitSphere` 자체를 유지한다. 테스트 5로 원본 샘플러와 일치하는지 확인한다. |
| B2-09 | 반박 | **A3-09 / 낮음.** 제공된 코드에서는 시작 이후 `count`만 바꿔도 배열 길이와 `_chunkCount`가 바뀌지 않으므로 제시한 불일치가 발생하지 않는다. 배열 재할당 경로는 없다. 최종안은 할당된 배열 길이로 청크 수를 계산한다. 런타임 재할당 기능은 범위 밖이며 테스트 9로 현재 경계를 확인한다. |
| B2-10 | 부분 수용 | **A3-10 / 중간.** 독립 히스토그램은 공유 카운터의 갱신 경쟁을 제거하고 포인터 없이 구현할 수 있다. 그러나 모든 캐시 경합의 제거, 성능 우위, 모든 대상에서 추가 메모리가 무시 가능하다는 주장은 측정 근거가 없다. 약 316 KiB의 추가 버퍼를 사용하며, 실제 비용은 테스트 10에서 측정한다. |
| B2-11 | 부분 수용 | **A3-11 / 높음.** unsafe 원자 증가와 메모리 초기화 방식을 철회하는 것은 확정 사항에 맞는다. 다만 원자 연산의 경합이 실제로 “심각하다”는 판정은 미확인이고, 무조건 출력 덮어쓰기는 누적 동작을 바꾼다. 최종 합산에서 누적·덮어쓰기를 선택한다. 테스트 2·3·10으로 검증한다. |
| B2-12 | 수용 | **A3-12 / 중간.** 균등 반지름과 구 내부 균일 분포는 다르다. 세제곱근 방식은 수학적으로 타당한 대안이나, 최종안은 불필요한 샘플러 교체 없이 원본 API를 보존한다. 테스트 5로 검증한다. |
| B2-13 | 수용 | **A3-13 / 높음.** 단계별 핸들 저장과 입력 검증을 적용한다. 추가로 초기화 실패 후 배열 접근을 막는 성공 상태를 둔다. 테스트 6·7로 검증한다. |
| B2-14 | 수용 | **A3-14 / 높음·미확인.** 확인된 결함과 관찰된 예외의 일대일 대응은 아직 할 수 없다. 자료 부족: 실제 예외 메시지·스택, 패키지 버전, 안전성 검사 설정. 테스트 4·8에서 확보한다. |
| B2-15 | 수용 | **A3-15 / 사양 미확정.** 누적 여부는 선택 가능하게 남기고 원본 동작을 기본값으로 삼는다. 선택값은 시작 시 고정하며, 이동 전 집계 순서도 보존한다. 테스트 3으로 검증한다. |
| B2-16 | 부분 수용 | **A3-16 / 확정 사항 반영.** 결정성의 의미는 이제 확정되었다. 현재 속도는 0으로 유지한다. 속도가 채워질 때의 시간 입력과 플랫폼 간 부동소수점 동일성은 미해결·범위 밖으로 남긴다. 현재 조건은 테스트 5로 검증한다. |

## 2. 최종 수정 코드

### A3-17 — `BoidSystem.cs`

`accumulateCounts`의 기본값은 원본의 누적 동작을 보존한다. `NativeDisableParallelForRestriction`은 C# unsafe 코드가 아니며, 서로 겹치지 않는 청크 행에 대해서만 병렬 쓰기 범위 제한을 해제한다.

```csharp
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
    [Tooltip("켜면 누적하고, 끄면 매 틱 새로 셉니다. 시작할 때 적용됩니다.")]
    [SerializeField] private bool accumulateCounts = true;

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<int> _cellCounts;
    private NativeArray<int> _partialCounts;
    private JobHandle _handle;
    private int _chunkCount;
    private bool _initialized;
    private bool _accumulate;

    private void Start()
    {
        try
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "개체 수는 양수여야 합니다.");

            _positions = new NativeArray<float3>(count, Allocator.Persistent);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent);
            _cellCounts = new NativeArray<int>(CellCount, Allocator.Persistent);
            _chunkCount = (_positions.Length - 1) / ChunkSize + 1;
            _partialCounts = new NativeArray<int>(
                checked(_chunkCount * CellCount), Allocator.Persistent);

            var savedState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(seed);
                for (int i = 0; i < _positions.Length; i++)
                    _positions[i] = UnityEngine.Random.insideUnitSphere * 50f;
            }
            finally
            {
                UnityEngine.Random.state = savedState;
            }

            _accumulate = accumulateCounts;
            _initialized = true;
        }
        catch (Exception exception)
        {
            _initialized = false;
            DisposeArrays();
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    [BurstCompile]
    private struct CountChunksJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        // 각 실행은 자기 청크의 행만 쓰며, 서로의 쓰기 구간은 겹치지 않습니다.
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

            if (accumulate)
                cellCounts[cell] += sum;
            else
                cellCounts[cell] = sum;
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
                accumulate = _accumulate
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
            _initialized = false;
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (!_initialized)
            return;

        _handle.Complete();
        transform.position = _positions[0];
        Debug.Log($"cell0={_cellCounts[0]}");
    }

    private void OnDisable()
    {
        _handle.Complete();
    }

    private void OnDestroy()
    {
        _initialized = false;
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

## 3. 자기 보고

**A3-18**

| 항목 | 수량 |
|---|---:|
| 전체 줄 수 | 189 |
| 빈 줄 제외 줄 수 | 165 |
| 병렬 Job 구조체 수 | 3 |
| 직렬 Job 구조체 수 | 0 |
| `NativeArray` 필드 수 | 10 |

줄 수는 코드 블록 내부만 기준으로 수동 집계했다. `NativeArray` 필드는 클래스의 소유 필드 4개와 Job 구조체의 필드 6개를 합한 수이며, 실제 소유 버퍼는 4개다.

## 4. 확정 사항 반영 내역

| ID | 확정 사항 | 코드 반영 |
|---|---|---|
| A3-19 | 같은 시드·같은 틱 수에서 같은 결과 | `Start()`에서 `seed`로 위치를 초기화한다. 청크마다 독립 행을 쓰고 `ReduceCountsJob`이 셀별 단독 작성자가 된다. 현재처럼 속도가 0인 조건에서 동일한 집계 틱 수의 결과를 비교한다. |
| A3-20 | 프레임 레이트 독립성·플랫폼 간 비트 동일성은 범위 밖 | `Update()`와 `Time.deltaTime`을 유지한다. 고정 시간 체계나 부동소수점 연산 정책을 추가하지 않았다. 속도 도입 시 재검토 항목은 A3-25에 남겼다. |
| A3-21 | 스케줄링에 따른 집계 흔들림 제거 | `CountChunksJob`의 청크별 쓰기 구간은 `[chunk * 1024, (chunk + 1) * 1024)`로 겹치지 않는다. `ReduceCountsJob`은 실행 인덱스에 해당하는 출력 셀만 쓴다. 모든 예약을 `_handle` 의존성으로 연결한다. |
| A3-22 | unsafe 금지 | 포인터, `unsafe` 블록, `GetUnsafePtr`, `Interlocked`, `MemClear`를 사용하지 않는다. 해당 네임스페이스에서는 범위 제한 속성만 사용한다. |
| A3-23 | 누적·속도·셀 수식 미확정 | 누적은 기본값으로 보존하면서 `accumulateCounts`로 선택 가능하게 했다. 속도는 기본 0을 유지한다. 셀 수식과 이동 전 집계를 보존한다. |
| A3-24 | 실제 예외는 실행 단계에서 확보 | 확인되지 않은 예외를 특정 원인으로 단정하지 않는다. 초기화·예약 실패는 `Debug.LogException`으로 기록한다. `Complete()` 예외는 삼키지 않으며, 종료에서 완료가 실패하면 무조건 해제로 진행하지 않는다. |

## 5. 미해결 쟁점

| ID | 쟁점·심각도 | 발생 조건·영향 | 대응·검증 |
|---|---|---|---|
| A3-25 | 시간 입력과 플랫폼 결정성 / 현재 범위 밖 | 속도가 채워지면 같은 틱 수라도 `Time.deltaTime` 차이로 위치와 이후 집계가 달라질 수 있다. CPU·Burst 설정에 따른 부동소수점 차이도 별도다. | 속도 구현 시 고정 틱 또는 기록된 시간 입력 등 대안을 선택한다. 현재는 변경하지 않는다. 선택 후 같은 시간 입력을 재생하여 위치·전체 셀을 비교한다. |
| A3-26 | 최종 집계 사양·누적 범위 / 중간·사양 의존 | 누적을 선택하면 셀의 `int` 범위를 넘을 수 있다. 20,000개가 한 셀에 몰리면 107,375번째 집계에서 범위를 넘는다. | 현재는 원본 자료형을 보존한다. 자료 부족: 누적 기간과 초과 시 정책. 장기 누적이 필요하면 `long`, 검사 후 중단 등의 대안을 별도 확정하고 경계 테스트한다. 실행 중 집계 모드 전환도 이번 구현에는 없다. |
| A3-27 | 속도·셀 정의 / 사양 미확정 | 현재 위치는 움직이지 않으며 양·음의 x가 같은 셀로 합쳐진다. 외부에서 비유한 위치를 넣는 기능이 추가되면 현재 셀 계산의 유효성도 재검토해야 한다. | 임의로 이동이나 격자 공식을 추가하지 않는다. 자료 부족: 속도 초기화·갱신 코드와 셀 정의. 추가 시 유한성 검사 및 경계 입력 테스트를 설계한다. |
| A3-28 | 실제 예외 식별·완료 실패 복구 / 높음·미확인 | `Complete()` 자체가 실패한 경우의 계속 실행·자원 회수 정책은 정해지지 않았다. 무조건 해제하면 미완료 작업의 메모리 접근 위험이 있다. | 자료 부족: 실제 메시지·스택, Burst/Collections 버전, 검사 설정, 실패 후 중단·복구 정책. 현재는 예외를 노출하고 완료 실패 후 무조건 해제하지 않는다. 안전한 일반 복구안은 제공 자료만으로 확정할 수 없다. |
| A3-29 | 런타임 재할당 / 범위 밖 | 향후 배열 크기 변경 기능을 추가하면 청크 수·부분 버퍼를 함께 재구성해야 한다. 현재 `count` 필드 변경만으로는 재할당되지 않는다. | 필요해지면 기존 핸들 완료 후 전체 관련 버퍼를 교체한다. 현재 구현에 해당 기능은 추가하지 않는다. |
| A3-30 | 실행·성능 미검증 / 중간 | 추가 히스토그램 초기화·합산 비용, 매 틱 로그 비용은 대상 환경에 따라 달라진다. | 자료 부족: 실제 패키지 버전, 대상 기기, 프레임 예산 및 측정값. 테스트 1·10으로 확인한다. 성능 비교 시 로그 비용은 별도로 분리하되 최종 코드에서는 원본 로그를 보존했다. |

## 6. 테스트 계획

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | Unity 6의 실제 패키지 구성에서 unsafe 허용을 끄고 에디터·개발 빌드·일반 빌드 컴파일 | C# 및 Burst 컴파일 오류가 없다. 사용한 버전·Burst·안전성 검사 설정을 기록한다. |
| 2 | 테스트 전용 입력을 모두 원점으로 설정하고 워커 수·배치 크기를 변경하며 반복 집계 | 프레임별 모드에서는 셀 0이 매번 `count`, 나머지는 0이다. 누적 모드에서는 오버플로우 전까지 완료한 집계 횟수 `k`에 대해 셀 0이 `k * count`다. 전체 1024셀을 검사한다. |
| 3 | 고정 위치를 직렬 참조 집계와 비교하고 두 모드를 각각 여러 틱 실행 | 프레임별 모드는 매 틱 참조 히스토그램과 같다. 누적 모드는 틱별 참조 히스토그램 합과 같다. 합계 검사는 넓은 정수형으로 수행한다. 직렬 집계는 테스트의 정답 생성에만 사용한다. |
| 4 | 수정 전 코드로 안전성 검사 활성화 상태의 예외를 수집한 뒤 같은 입력으로 수정본 실행 | 수정 전 메시지·스택·발생 시점을 확보한다. 수정본에서는 병렬 범위 위반과 집계 경쟁 증상이 재현되지 않는다. 다른 예외는 별도 원인으로 분류한다. |
| 5 | 같은 시드로 재시작하고 동일 틱의 초기 배열·전체 셀 비교. 원본 샘플러 및 전역 난수 상태도 확인 | 현재 속도 0 조건에서 반복 결과가 같다. 별도 저장·복원 구간에서 같은 시드로 생성한 `insideUnitSphere` 표본과 초기 위치가 같다. 초기화 전후 전역 난수 상태가 복원된다. 플랫폼 간 비트 동일성은 통과 조건이 아니다. |
| 6 | `count`를 0·음수로 설정하고 각 할당 직후 테스트용 예외 주입 | 원인 예외가 기록되고 컴포넌트가 비활성화된다. 후속 예약·첫 요소 접근이 없으며 생성된 배열이 정리된다. |
| 7 | 각 예약 성공 직후 예외를 주입하고 비활성화·파괴 | 마지막 성공 예약까지 `_handle`이 추적한다. `OnDisable()` 또는 `OnDestroy()`에서 완료 후에만 해제하며, 부분 예약 결과를 정상 틱처럼 출력하지 않는다. |
| 8 | 장시간 실행, 씬 전환, 예약 후 비활성화, 시작 전 파괴 경로를 나누어 콜백·누수 추적 | 콜백 순서를 실제로 확인한다. 미생성 배열 해제, 사용 중 해제, `TempJob` 수명 진단, 네이티브 누수가 없다. 종료 오류가 발생하면 전체 스택을 확보한다. |
| 9 | 개체 수 1·255·256·257·20,000으로 실행하고 시작 후 인스펙터의 `count`만 변경 | 모든 크기에서 참조 집계와 일치한다. 마지막 부분 청크도 정확하다. 시작 후 필드 변경은 할당된 배열과 집계 대상 수를 바꾸지 않는다. |
| 10 | 대상 기기에서 일반 분포·단일 셀 집중 입력의 Job 시간, 메모리, 로그 비용 측정 | 정확성을 유지하고 합의된 프레임 예산을 충족한다. 자료 부족: 성능 예산. 예산 확정 전에는 성능 우위를 통과했다고 판정하지 않는다. |