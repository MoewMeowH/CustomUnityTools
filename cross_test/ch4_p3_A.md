## 1. 상대 Phase 2 판정에 대한 응답

| 상대 ID | 수용 · 부분 수용 · 반박 | 근거 |
|---|---|---|
| B2-01 / A3-01 | 부분 수용 | **높음.** 공유 셀의 비원자적 증가와 병렬 쓰기 범위 위반은 확인된다. 다만 실제 `InvalidOperationException`과의 일대일 대응은 **자료 부족: 예외 전문·스택 트레이스**. 안전성 검사가 같은 Job 내부의 데이터 경쟁 자체를 검출한다고 단정하지 않는다. 독립 청크 집계로 수정하고 테스트 2·3에서 검증한다. |
| B2-02 / A3-02 | 부분 수용 | **중간, 사양 의존.** 초기화하지 않는 것은 사실이지만 누적이 결함인지는 미정이다. 같은 틱 수를 비교하는 이번 기준에서 프레임 레이트 차이는 실패 원인으로 취급하지 않는다. 원본처럼 누적을 기본값으로 보존하고 재집계도 선택할 수 있게 한다. 테스트 4에서 두 모드를 검증한다. |
| B2-03 / A3-03 | 부분 수용 | **중간.** 전역 난수 상태가 다르면 입력이 달라질 수 있지만, 매 실행마다 반드시 다른 입력이나 집계가 나온다는 주장은 과도하다. 고정 시드로 기존 샘플러를 사용하고 전역 상태는 복원한다. 테스트 5에서 위치 배열 자체와 전체 셀을 비교한다. |
| B2-04 / A3-04 | 수용 | **높음.** 여러 프레임 보유하는 배열에 `TempJob`은 부적합하다. 단, 수명 규약을 자동 해제나 자동 만료로 해석하지 않는다. 모든 소유 배열을 `Persistent`로 바꾸고 테스트 8에서 장기 실행·종료 진단을 확인한다. |
| B2-05 / A3-05 | 부분 수용 | **높음.** 미완료 작업이 있는 상태에서 해제하면 위험하다. 정상 `LateUpdate()` 완료 후에는 그 경쟁이 없으며, 비활성 오브젝트 파괴만으로 특정 콜백 순서를 보장하지도 못한다. 완료 후 생성된 배열만 해제한다. 테스트 7·8에서는 콜백 순서도 기록한다. |
| B2-06·B2-07·B2-13 / A3-06 | 수용 | **높음: 핸들 유실, 낮음: 잘못된 개체 수.** 예약 성공 직후마다 핸들을 저장한다. 양수가 아닌 개체 수는 할당 전에 거부하고, 초기화 실패 뒤 후속 접근도 막는다. 테스트 6·7에서 부분 실패를 주입한다. |
| B2-08·B2-12 / A3-07 | 수용 | **중간.** 내 Phase 1 기본 예제가 구 표면 분포로 바뀐 것은 잘못이었다. 이를 명시적으로 철회한다. 최종안은 기존 `insideUnitSphere` 자체를 유지하므로 세제곱근 대체식도 필요하지 않다. 테스트 5에서 구 내부 분포와 재현성을 확인한다. |
| B2-09 / A3-08 | 부분 수용 | **낮음, 향후 재할당 경로가 생기는 경우에 한함.** 제공 코드에서는 `count` 필드만 바꿔도 배열 길이와 `_chunkCount`는 함께 그대로이므로 불일치가 발생하지 않는다. 배열 재할당까지 추가하면 위험하다는 조건부 지적은 타당하다. 최종안은 초기화 시 크기를 고정한다. 테스트 9로 확인하며 동적 크기 변경 구현은 범위 밖이다. |
| B2-10·B2-11 / A3-09 | 부분 수용 | 청크별 독립 집계는 unsafe 없이 경쟁을 제거하는 대안이므로 채택한다. 그러나 **성능 우수성·캐시 경합 완전 제거·추가 메모리의 무시 가능성은 미확인**이다. 청크 경계의 거짓 공유와 합산 비용도 고려해야 한다. **심각도: 낮음, 성능 요구가 있는 경우 영향 가능.** 테스트 10에서 측정한다. 결과 덮어쓰기는 재집계 모드에만 적용한다. |
| B2-14 / A3-10 | 부분 수용 | 확인된 결함을 수정하는 데 동의한다. 다만 로그 없이 보고된 세 증상의 직접 원인이나 그 가능성 순위를 확정하지 않는다. **자료 부족: 실제 로그·스택, 패키지 버전, 안전성 설정.** 테스트 1·8에서 수집한다. |
| B2-15 / A3-11 | 수용 | 집계 사양은 여전히 미정이다. 기본값은 원본의 누적 동작을 보존하고 선택 필드를 제공한다. 모드에 따른 기대값은 테스트 4에서 나눈다. |
| B2-16 / A3-12 | 부분 수용 | 결정성 범위는 사용자 확정으로 해결되었다. 현재 0인 속도와 `Time.deltaTime`은 보존한다. 속도 도입 후의 시간 입력 재현과 플랫폼 간 비트 동일성은 이번 범위 밖으로 남긴다. 같은 시드·같은 틱 수·같은 설정의 검증은 테스트 3·5에서 수행한다. |

## 2. 최종 수정 코드

**A3-13 — `BoidSystem.cs`**

설정은 두 실행에서 동일하게 유지한다고 가정한다. 시뮬레이션 틱은 정상적으로 완료한 `Update()` 작업 묶음이며, 집계는 원본처럼 이동 전 위치를 대상으로 한다.

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
```

## 3. 자기 보고

| 항목 | 값 |
|---|---|
| 전체 줄 수 | 193줄 |
| 빈 줄 제외 줄 수 | 169줄 |
| Job 구조체 수 | 병렬 3개, 직렬 0개 |
| `NativeArray` 필드 수 | 총 10개: 컴포넌트 4개, Job 내부 6개 |
| 산정 범위 | 코드 블록 내부만 포함. 주석·중괄호 포함, 마지막 개행은 별도 줄로 세지 않음 |
| 실행 검증 | 컴파일·실행하지 않음. 정적 검토 결과 |
| 추가 버퍼 | 20,000개에서 79행 × 1,024개 × 4바이트 = 323,584바이트, 약 316 KiB |

## 4. 확정 사항 반영 내역

| 확정 사항 | 반영 위치와 방법 |
|---|---|
| 1. 같은 시드·같은 틱 수 기준 결정성 | `Start()`에서 고정 시드로 위치를 생성한다. `CountChunksJob`은 청크마다 독립된 행을 소유하고, `ReduceCountsJob`은 셀마다 하나의 실행만 출력한다. 현재 속도가 모두 0이므로 틱별 `deltaTime` 차이는 위치를 바꾸지 않는다. |
| 2. 프레임 레이트 독립성·플랫폼 간 비트 동일성 제외 | `MoveJob`과 `Time.deltaTime`을 유지했다. 고정 시간 체계나 부동소수점 연산 정책은 추가하지 않았다. A3-15에 재검토 조건을 남겼다. |
| 3. 병렬 쓰기 경쟁에 따른 결과 변동 제거 | 청크의 쓰기 구간은 `[chunk × 1024, (chunk + 1) × 1024)`로 서로 겹치지 않는다. 합산은 앞선 Job 완료에 의존하며, 출력은 실행 인덱스 `cell`에만 쓴다. 모든 예약은 이전 `_handle`에 연결한다. |
| 4. unsafe 금지 | 포인터, `unsafe` 블록, `GetUnsafePtr`, 원자 포인터 연산을 사용하지 않는다. `Unity.Collections.LowLevel.Unsafe`는 범위 제한 속성의 네임스페이스일 뿐 unsafe 코드가 아니다. 해당 속성은 독립된 행 쓰기에만 적용하며 다른 안전성 검사를 일괄 해제하지 않는다. |
| 5. 집계·속도·셀 수식 미정 | `accumulateCounts`로 누적과 재집계를 선택하게 했다. 기본값은 원본의 누적을 보존한다. 속도는 기본 0을 유지하며, 셀 수식과 이동 전 집계 순서도 유지한다. |
| 6. 예외 로그는 실행 검증에서 확보 | 초기화·예약 예외를 `Debug.LogException`으로 기록한다. 실제 증상과 코드 결함의 대응은 확정하지 않았으며 테스트 1·7·8에서 스택과 실행 순서를 수집한다. |

## 5. 미해결 쟁점

- **A3-14 — 실제 예외 대응 / 높음**
  - **자료 부족: 에디터·종료 예외 전문과 스택, Collections·Burst 버전, 빌드 안전성 설정.**
  - 확인된 결함과 관찰된 세 증상을 일대일로 확정할 수 없다. 테스트 1·8에서 확보한 기록으로 재판정한다.

- **A3-15 — 속도 도입 이후의 결정성 / 범위 밖**
  - 현재는 속도가 0이므로 시간 간격 차이가 이동에 영향을 주지 않는다.
  - 속도가 채워지면 같은 틱 수라도 서로 다른 `Time.deltaTime` 입력으로 위치와 집계가 달라질 수 있다. **심각도: 중간, 속도 도입 시 발생.**
  - 시간 입력 재현·프레임 레이트 독립성·플랫폼 간 비트 동일성은 그 시점에 재검토한다. 이번 코드에는 대안을 강제하지 않는다.

- **A3-16 — 누적 의미와 정수 범위 / 중간**
  - 누적·재집계의 최종 사양, 누적 초기화 시점, 실행 중 모드 전환 의미는 미정이다. 재현 비교에서는 모드를 고정한다.
  - 누적값이 `int` 범위를 넘으면 `unchecked` 덧셈으로 래핑된다. 한 셀에 20,000개가 집중되면 107,375번째 집계에서 양의 범위를 넘는다.
  - 장기 누적이 필요하다면 `long` 전환이나 명시적 중단 정책을 선택해야 한다. 이번에는 자료형과 비검사 정수 누적 동작을 보존했다. 테스트 11에서 경계를 검증한다.

- **A3-17 — Job 실행 자체의 실패 후 복구 / 높음**
  - 예약 실패 시에는 마지막 성공 핸들을 보존하고 비활성화한다.
  - `Complete()` 자체가 실패하면 안전한 소유권 반환을 확인할 수 없으므로 무조건 해제하지 않는다. 이 경우 자원 회수가 보장되지 않는다.
  - **자료 부족: 실제 실패 로그, Job 실패 후 계속 실행할지 중단할지에 대한 정책.** 일반적인 안전 복구 대안은 현재 자료로 확정할 수 없다.

- **A3-18 — 외부 입력·동적 재할당 / 범위 밖**
  - 제공 코드에는 외부 위치·속도 갱신이나 배열 재할당이 없다. `count`를 실행 중 바꿔도 현재 배열 크기는 바뀌지 않는다.
  - 향후 해당 경로를 추가하면 비유한 위치값, 셀 인덱스 범위, 버퍼 크기, 작업 완료 후 재할당을 함께 검증해야 한다. 현재 초기화와 0 속도에서는 비유한 위치 입력이 생성되지 않는다.

- **A3-19 — 성능 / 미확인**
  - 청크 크기 256, 추가 버퍼, 합산 비용의 적합성은 대상 기기에서 확인해야 한다. `Debug.Log` 비용과 Job 비용은 분리해 측정한다.
  - 정확성 근거는 메모리 구간의 독립성이지, 측정되지 않은 성능 우위가 아니다.

## 6. 테스트 계획

| 순서 | 테스트 | 통과 기준 |
|---|---|---|
| 1 | Unity 6 대상 환경에서 unsafe 허용을 끄고 컴파일. 패키지 버전·Burst·안전성 설정 기록 | 컴파일 성공, unsafe 설정 불필요. 실제 오류가 있으면 전문과 스택 확보 |
| 2 | 위치를 모두 0으로 고정하고 재집계 모드에서 반복 실행 | 매 틱 셀 0은 `count`, 나머지는 0. 안전성 검사 활성 상태에서도 범위·의존성 오류 없음 |
| 3 | 같은 입력으로 에디터·개발 빌드·일반 빌드에서 반복. 가능한 워커 수·배치 조건 변경 | 같은 완료 틱에서 전체 1,024개 셀이 정수 참조 집계와 일치하고 스케줄링에 따라 변하지 않음 |
| 4 | 고정 위치로 재집계·누적 모드를 각각 여러 틱 실행 | 재집계는 매 틱 전체 합이 `count`. 누적은 오버플로우 전까지 완료 틱 수 × `count`. 합 검증은 `long` 사용 |
| 5 | 같은 시드로 재시작하고, 초기화 전후 전역 난수 상태도 검사 | 동일 환경에서 초기 위치와 같은 틱의 전체 셀이 동일. 위치는 구 내부에 분포하며 전역 난수 상태는 복원됨 |
| 6 | `count`를 0·음수로 설정하고 각 할당 직후 테스트용 예외 주입 | 오류가 기록되고 컴포넌트 비활성화. 생성된 배열 회수, 후속 예약·첫 요소 접근 없음 |
| 7 | 각 예약 성공 직후 테스트용 예외 주입. 예약·비활성화·완료·파괴 순서 기록 | 마지막 성공 작업까지 완료한 뒤 해제. 예약 실패 후 추가 틱 실행과 미완료 배열 해제 없음 |
| 8 | 수십 프레임 이상 실행 후 씬 종료, 비활성화·재활성화, 초기화 전 파괴 경로 시험 | 할당 수명 진단·중복 해제·사용 중 해제 오류 없음. 실제 호출된 콜백과 예외 스택 확보 |
| 9 | 개체 수 1·255·256·257·20,000으로 실행하고 실행 중 `count` 필드만 변경 | 마지막 청크까지 정확한 집계. 필드 변경만으로 기존 배열·청크 수가 변하거나 범위 오류가 생기지 않음 |
| 10 | 대상 기기에서 일반 분포와 단일 셀 집중 입력 프로파일링 | 로그 비용을 분리한 Job 시간·메모리 사용량 기록. 성능 합격 여부는 **자료 부족: 대상 기기와 허용 시간·메모리 예산** |
| 11 | 누적 출력값을 시험용으로 `int.MaxValue` 근처에 두고 집계 | 명시한 `unchecked` 래핑과 일치. 장기 누적 정책 확정 전에는 이를 정상적인 개체 수로 해석하지 않음 |