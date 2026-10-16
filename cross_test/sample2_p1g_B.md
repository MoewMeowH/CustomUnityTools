1. 지적 목록

| ID | 위치 | 문제 | 발생 조건 | 영향 | 심각도 | 확신도 | 검증 방법 |
|---|---|---|---|---|---|---|---|
| B-01 | `CountJob.Execute` | `IJobParallelFor`에서 현재 배치 인덱스(`i`)가 아닌 임의 인덱스(`cell`)에 쓰기 수행 및 비원자적 증가 연산(`++`)으로 인한 경쟁 상태(Race Condition) 발생 | 워커 스레드가 2개 이상이고, 서로 다른 인덱스 `i`가 동일한 `cell` 값을 계산하여 동시에 접근할 때 | [증상 1 & 2의 원인] 에디터에서는 Job 안전성 검사기가 스레드 간 인덱스 범위 충돌을 감지하여 `InvalidOperationException`을 던짐. 안전성 검사가 비활성화된 빌드에서는 예외 없이 데이터 경합이 발생하여 업데이트가 유실되고 `cell0` 값이 매번 달라짐 | 높음 | 사실 | 에디터에서 `Jobs > Use Job Threads`를 켠 상태로 실행 시 예외 발생 확인. 빌드에서 고정 시드 사용 후 반복 실행 시 `cell0` 값 변동 확인 |
| B-02 | `Start` | 컴포넌트 수명 동안 유지되는 `_positions`, `_velocities` 배열에 `Allocator.TempJob` 사용 | 씬 실행 후 4프레임이 경과했을 때 | `TempJob` 할당 수명(4프레임) 초과로 메모리 누수 경고 발생 및 할당자 내부 상태 오염 위험. 씬 종료 시점의 메모리 해제 오류에 기여 | 높음 | 사실 | 에디터 콘솔에서 4프레임 이후 "Internal: JobTempAlloc has allocations that are more than 4 frames old" 경고 출력 확인 |
| B-03 | `OnDestroy`, `Update` | 진행 중인 Job을 완료(`Complete()`)하지 않고 `NativeArray.Dispose()` 호출 및 비활성화 중 Job 스케줄링 미방지 | `Update`에서 Job이 스케줄링된 후 `LateUpdate`가 실행되기 전에 씬이 닫히거나 오브젝트가 파괴될 때 (또는 `Start` 전에 파괴될 때) | [증상 3의 원인] 워커 스레드가 사용 중인 메모리를 해제하려 시도하여 `InvalidOperationException` 발생. 초기화 전 파괴 시 `Dispose` 에러 발생 | 높음 | 사실 | `Update` 직후 다른 스크립트에서 `Destroy(gameObject)` 또는 `SceneManager.LoadScene`을 호출하여 `LateUpdate`를 건너뛰게 한 뒤 에러 확인 |
| B-04 | `Start` | 난수 생성기(`UnityEngine.Random`)의 시드가 고정되지 않음 | 매 실행 시 `Start()` 호출 | [증상 2의 복합 원인] B-01이 해결되더라도 초기 위치값이 매 실행마다 달라지므로 실행 간 `cell0`의 결정론적(deterministic) 결과가 보장되지 않음 | 중간 | 사실 | B-01 수정 후 게임을 여러 번 재실행하여 첫 프레임의 `cell0` 값이 동일하게 나오는지 비교 |
| B-05 | `Update`, `CountJob` | 매 프레임 `_cellCounts` 배열을 0으로 초기화하지 않음 | 2프레임 이상 실행될 때 | 프레임당 개수가 아니라 이전 프레임의 값에 계속 누적됨. 프레임 레이트 변동에 따라 특정 시점의 `cell0` 값이 달라짐 | 중간 | 추론 | 프레임마다 출력되는 `cell0` 값이 계속 증가하는지 로그 확인 |
| B-06 | `LateUpdate` | 매 프레임 문자열 보간(`$"..."`) 및 `Debug.Log` 호출 | 매 프레임 `LateUpdate` 실행 시 | 프로젝트 지침 위반(Update 계열 내 힙 할당). GC Alloc 발생 및 콘솔 I/O로 인한 심각한 프레임 드랍 | 낮음 | 사실 | Unity Profiler에서 `LateUpdate`의 `GC.Alloc` 크기 확인 |

2. 사양 확인이 필요한 항목
- **질문 1 (`_cellCounts` 초기화 여부):** `_cellCounts`는 매 프레임 현재 위치 기준의 셀별 개체 수를 세는 용도입니까, 아니면 실행 시간 동안 누적하는 용도입니까?
  - *조건부 결론:* 일반적인 공간 분할(Spatial Partitioning) 목적이라면 매 프레임 Job 시작 전에 0으로 초기화해야 합니다.
- **질문 2 (`_velocities` 초기화 누락):** `_velocities` 배열에 값을 할당하는 코드가 없습니다. 의도된 생략입니까?
  - *조건부 결론:* 현재 코드로는 모든 속도가 `(0,0,0)`이어서 `MoveJob`이 실행되어도 위치가 변하지 않습니다.
- **질문 3 (결정론적 결과의 범위):** "실행마다 같은 값"이라는 목표가 '매 실행 시 동일한 초기 배치를 가짐'을 의미합니까, 아니면 '주어진 초기 배치에 대해 스레드 경합 없이 정확한 계산 결과가 나옴'을 의미합니까?
  - *조건부 결론:* 전자까지 포함한다면 고정 시드 기반의 `Unity.Mathematics.Random`을 사용해야 합니다.

3. 수정 방향
- **B-01 해결 (원자적 연산 및 안전성 검사 통과):**
  `CountJob`에서 `NativeArray<int>`에 `[NativeDisableParallelForRestriction]`을 붙여 인덱스 제한을 해제하고, `System.Threading.Interlocked.Increment`를 사용하여 원자적으로 값을 증가시킵니다.
  ```csharp
  using System.Threading;
  using Unity.Collections.LowLevel.Unsafe;

  [BurstCompile]
  struct CountJob : IJobParallelFor
  {
      [ReadOnly] public NativeArray<float3> positions;
      [NativeDisableParallelForRestriction] public NativeArray<int> cellCounts;

      public unsafe void Execute(int i)
      {
          int cell = (int)(math.abs(positions[i].x) % 1024);
          int* ptr = (int*)cellCounts.GetUnsafePtr();
          Interlocked.Increment(ref ptr[cell]);
      }
  }
  ```
  *(참고: `unsafe` 컨텍스트 허용이 불가능한 환경이라면, 크기가 `count`인 임시 배열에 각 개체의 `cell` 인덱스를 병렬로 기록한 뒤 단일 스레드 Job(`IJob`)에서 합산하는 방식을 대안으로 사용합니다.)*

- **B-02 해결 (할당자 변경):**
  `Start()`에서 `_positions`와 `_velocities`의 할당자를 `Allocator.Persistent`로 변경합니다.

- **B-03 해결 (안전한 생명주기 관리):**
  `OnDestroy()`(또는 `OnDisable()`) 최상단에서 `_handle.Complete()`를 먼저 호출한 뒤, `IsCreated`를 확인하고 `Dispose()`를 호출합니다.
  ```csharp
  void OnDestroy()
  {
      _handle.Complete();
      if (_positions.IsCreated) _positions.Dispose();
      if (_velocities.IsCreated) _velocities.Dispose();
      if (_cellCounts.IsCreated) _cellCounts.Dispose();
  }
  ```

- **B-04 해결 (결정론적 난수):**
  `Start()`에서 `UnityEngine.Random` 대신 고정 시드를 가진 `Unity.Mathematics.Random`을 사용합니다.
  ```csharp
  var rng = new Unity.Mathematics.Random(12345);
  for (int i = 0; i < count; i++)
      _positions[i] = rng.NextFloat3Direction() * rng.NextFloat(0f, 50f);
  ```

- **B-05 해결 (카운트 초기화 - 조건부):**
  프레임별 계산이 목적이라면 `CountJob` 스케줄링 전에 배열을 초기화하는 Job을 선행하거나 `UnsafeUtility.MemClear`를 수행합니다.
