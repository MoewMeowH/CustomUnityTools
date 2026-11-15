# HB_FrameInspector

에디터 Play 모드에서 버튼 한 번으로 한 장면의 렌더링 비용을 모아 창에 보여 주고, 리포트 파일로 저장하는 도구입니다.
저장한 `report.md`는 수치마다 뜻 · 단위 · 측정 한계를 함께 적어, **AI(웹 채팅 포함)에게 이 파일 하나만 넘겨도 분석할 수 있게** 만들었습니다.

---

## 설치

```text
https://github.com/MoewMeowH/CustomUnityTools.git?path=Assets/Tools/HB_FrameInspector
```

- Unity 6.3 (6000.3) 이상, 에디터 전용입니다.
- 오버드로우는 URP(`com.unity.render-pipelines.universal`)가 활성 파이프라인일 때만 잽니다. URP가 아니면 오버드로우 절만 비우고 나머지는 그대로 모읍니다 (Built-in · HDRP 프로젝트에서는 시험하지 않았습니다).

---

## 쓰는 법

1. **Tools > HB > Frame Inspector**로 창을 엽니다.
2. 게임을 Play하고, 보고 싶은 장면에서 **[캡처]**를 누릅니다.
   - **프레임 수**: 평균 · 최대를 낼 프레임 수 (기본 60).
   - **드로우 목록**: Frame Debugger로 드로우 목록을 읽습니다 (기본 켬). 읽는 동안 Play가 잠깐 일시정지되고 Frame Debugger 창이 열렸다 닫힙니다. 끝나면 원래 상태로 돌아갑니다.
   - **Profiler 녹화**: 측정이 끝난 뒤 Profiler를 녹화해 `profiler.data`로 남깁니다 (기본 끔, 최대 300프레임). **Profiler 창의 기존 기록이 지워집니다.** Profiler가 연결된 기기 등 에디터가 아닌 대상에 붙어 있으면 녹화하지 않습니다.
   - **카메라**: 오버드로우 · 화면을 그릴 카메라. 비우면 `Camera.main`.
3. 탭(요약 · 오버드로우 · GPU 패스 · 드로우/셰이더 · CPU · 렌더 그래프)에서 결과를 봅니다.
4. **[저장]**: 저장 위치 아래 `yyyyMMdd_HHmmss_씬이름/` 폴더에 리포트를 씁니다.
5. **[AI용 복사]**: 저장한 뒤 `report.md` 본문과 그림 파일 경로를 클립보드에 넣습니다. 웹 AI에는 붙여 넣고 그림(png)을 따로 첨부합니다.
6. **[폴더 열기]**: 마지막 저장 폴더(없으면 저장 위치)를 엽니다.

저장 위치 기본값은 프로젝트 루트 `FrameReports/`입니다 (Assets 밖이라 임포트되지 않음). 창의 [변경] · [기본]으로 바꾸며, 이 PC · 사용자 설정(EditorPrefs)입니다.
리포트를 저장소에 올리지 않으려면 프로젝트 `.gitignore`에 `/FrameReports/`를 넣습니다.

### 전후 비교

창 위쪽의 **전후 비교**로 바꾸면 저장된 리포트 두 개(이전 · 이후)를 지표별로 비교합니다. Play 모드가 아니어도 됩니다.

1. **[최근 저장 두 개]** 또는 **[고르기]**로 이전(기준) · 이후 리포트 폴더를 고릅니다.
2. **[비교]**: 지표마다 이전 → 이후 · 차이%를 보여 줍니다 (나빠짐 빨강 · 좋아짐 초록). 모든 지표는 낮을수록 좋습니다.
3. **[저장]**: 저장 위치 아래 `yyyyMMdd_HHmmss_compare/`에 `compare.md` · `compare.json`. **[AI용 복사]**: `compare.md` 본문을 클립보드로.

- 측정 조건(Unity 버전 · GPU · 해상도 · 씬 · 카메라 · 배치 모드 · 프레임 수)이 다르면 맨 위에 경고합니다.
- 차이가 1% 미만이거나 단위별 최소 차이(ms 0.01 · 횟수 0.5 · 바이트 1KB 등)보다 작으면 '같음'으로 봅니다 (측정 잡음).
- 한쪽에만 있거나 한쪽에서 측정되지 않은 지표는 '비교 불가'로 따로 적습니다.
- 1.0 때 저장한 리포트도 읽힙니다 (없는 항목은 비교 불가).

---

## 리포트 항목

| 항목 | 내용 | 출처 |
|---|---|---|
| 측정 조건 | Unity 버전 · 파이프라인 · 그래픽 API · GPU · CPU · 씬 · 카메라 · Game 뷰 해상도 · 프레임 수 | `SystemInfo` 등 |
| 병목 힌트 | CPU 메인 스레드와 게임 GPU 시간을 비교한 `cpu` · `gpu` · `balanced` · `unknown`과 근거 수치 | 아래 프레임 시간 |
| 프레임 시간 | CPU 프레임 · 메인 · 렌더 스레드, GPU 프레임 (평균 · 최대 · 최소 ms) | CPU: `FrameTimingManager`, GPU: `Recorder("FrameTime.GPU")` |
| 렌더링 · 메모리 카운터 | Draw Calls · Batches · SetPass · Triangles · Vertices · 프레임당 GC 할당 바이트 · 횟수, Total · GC · Gfx Used Memory · Texture · Mesh Memory · Render Textures Bytes (바이트는 KB/MB로 표시) | `ProfilerRecorder` |
| CPU 요약 | `PlayerLoop` · Update · LateUpdate · Physics · Animation · 렌더러 갱신 등 고정 마커 평균 | `Recorder` |
| 렌더 패스 GPU 시간 | GPU 샘플이 잡히는 Profiler 샘플러별 시간 (중첩됨 — 더하지 않음). 에디터 창 그리기는 따로 | `Recorder.gpuElapsedNanoseconds` |
| 오버드로우 | 픽셀당 평균 · 덮인 픽셀당 평균 · 3회+ · 5회+ · 최대+ 비율, 히스토그램, 색 그림 | URP Rendering Debugger Overdraw |
| 드로우 목록 | 패스별 · 종류별 · 오브젝트별 드로우 이벤트 수 | Frame Debugger (내부 API) |
| 셰이더 | 그려진 머티리얼의 셰이더별 드로우 수 · Renderer · 머티리얼 · 패스 수 · 반투명 · RenderQueue · 오류 · 키워드 | `ShaderUtil` |
| 렌더 그래프 (URP core 17.7+) | 카메라별 패스 · 버려진 패스 · 합쳐지지 못한(native pass merge 실패) 패스 수와 이유, 리소스 수, Unity가 낸 요약 문장 | 렌더 그래프 디버그 세션 (내부 API) |
| Profiler 녹화 | 녹화 프레임 수 · `profiler.data` 이름과 여는 법 | `ProfilerDriver` |

저장 폴더에는 `report.md` · `report.json`(같은 데이터, 기계용) · `overdraw.png` · `screen.png`, Profiler 녹화를 켰으면 `profiler.data`가 생깁니다.

---

## 알아 둘 점

- **에디터에서 잰 값**입니다. 에디터 오버헤드가 섞이고 빌드 · 실제 기기와 다릅니다. 잴 때 Scene 뷰 등 다른 창을 닫으면 에디터 영향이 줄어듭니다.
- GPU 프레임 시간은 `Recorder("FrameTime.GPU")`를 씁니다. 에디터에서 `FrameTimingManager`의 GPU 값은 이보다 훨씬 작게 나왔습니다 (7000.0.0a8 · D3D12에서 0.01ms vs 0.10ms). `FrameTime.GPU`가 없을 때만 `FrameTimingManager` 값을 쓰고 리포트에 적습니다.
- 에디터 창 그리기 GPU 시간(`GUIView.*` · `GUITexture.*` · `UIR.*`)은 게임 GPU 프레임 값에 들어가지 않아 따로 보여 줍니다.
- 렌더 패스 GPU 시간은 그래픽 API · 드라이버가 GPU 타이밍을 주지 않으면 비어 있습니다 (리포트에 그렇게 적음).
- 오버드로우는 선택한 카메라 하나만 그린 결과입니다. Screen Space Overlay UI · 다른 카메라는 들어가지 않습니다.
  - URP Overdraw 모드 `All` 기준입니다. `Opaque` · `Transparent` 모드는 세지 않는 쪽 물체도 1로 덮어써서 따로 내지 않습니다.
  - 카메라 RT 출력이 흑백 원값(횟수 ÷ 최대)일 때와 색 히트맵 + 범례일 때가 있습니다 (그 패스가 URP 최종 패스로 잡히는지에 따라 — 프로젝트 · 카메라 설정마다 다름). 색 출력이면 색표에서 횟수를 되돌리고, 범례가 그려지는 아래쪽 70px 줄은 통계에서 뺍니다 (그림에서 회색).
- 드로우 목록은 Unity 내부 API(Frame Debugger)를 리플렉션으로 읽습니다. 읽지 못하면 그 절만 비우고 이유를 적습니다. `-batchmode`에서는 창을 그리지 않아 읽지 못합니다.
- 셰이더 목록은 화면에 보이는 Renderer 기준입니다. 드로우 수는 Frame Debugger에서 오브젝트가 잡힌 드로우만 세므로, SRP Batcher로 묶인 드로우는 0으로 남습니다. 셰이더 명령어 수 · 사이클 같은 비용 수치는 없습니다.
- 병목 힌트의 CPU 쪽은 메인 · 렌더 스레드 중 긴 쪽입니다. 목표 프레임(vsync · targetFrameRate)에 묶여 기다리는 경우는 따로 구분하지 않습니다.
- 캡처 중에 Play를 끝내거나 일시정지하면(Error Pause · `Debug.Break()` 포함) 캡처가 실패로 끝납니다. 드로우 목록을 읽는 동안의 일시정지는 도구가 걸었다 푸는 것이라 괜찮습니다.
- 메모리 카운터는 에디터 자신이 쓰는 메모리도 포함합니다. 절대값보다 전후 비교에 씁니다.
- 렌더 그래프 지표는 Unity 내부 API(렌더 그래프 디버그 세션)를 리플렉션으로 읽습니다. Render Graph Viewer 창이 열려 있으면 그 세션을, 없으면 세션을 잠깐 열었다 닫습니다. URP core 17.7(Unity 7000) 미만이면 그 절이 비어 있습니다.
- **배치 모드(`-batchmode`)**에서는 게임 화면을 그리지 않아 GPU 시간 · 렌더 스레드 · 렌더링 카운터(드로우콜 등) · 드로우 목록 · 렌더 그래프가 측정되지 않고, Profiler 녹화도 저장되지 않습니다. 리포트에 그렇게 적고 0을 실측처럼 쓰지 않습니다. 오버드로우 · CPU 메인 스레드 · GC · 메모리는 측정됩니다.

---

## 스크립트 · AI 에이전트에서 쓰기

```csharp
using HB.FrameInspector.Editor;

// Play 모드에서: 캡처 → 저장 → 저장 폴더 경로
string folder = await FrameInspectorApi.CaptureAndSaveAsync(frames: 60, drawList: true, recordProfiler: false);

// 저장된 두 리포트 비교 → compare.md · compare.json 폴더
string compare = FrameInspectorApi.CompareAndSave(baselineFolder, folder);
```

- `FrameCapture.CaptureAsync(options)` → `FrameCaptureResult`(리포트 · png 바이트 · Profiler 임시 파일), `FrameInspectorApi.Save(result, root)` → 폴더 경로.
- `FrameInspectorApi.LoadReport(folder)` · `ListReportFolders(root)` · `FrameReportComparer.Compare(a, b)` · `FrameReportComparer.Check(a, b, thresholds)`.
- `FrameInspectorApi.BuildAiClipboardText(folder)` · `BuildAiClipboardTextForComparison(compareFolder)` → [AI용 복사]와 같은 글.
- 함수 단위 CPU 원인(hot path · 마커 집계 · GC 콜스택)은 `profiler.data`로 봅니다. `com.unity.pipeline` 1.0.0 이상이 있으면 Unity CLI의 `diagnostics profiler_session`으로 열어 분석할 수 있습니다.

### 성능 회귀 검사 (CI)

기준 리포트(`report.json`)를 저장소에 두고, 벤치 장면 테스트에서 캡처한 결과와 허용치로 비교합니다.

```csharp
[UnityTest]
public IEnumerator Bench_NoRegression()
{
    yield return new EnterPlayMode();
    // 벤치 장면을 열고 안정될 때까지 기다린 뒤
    var task = FrameCapture.CaptureAsync(new FrameCaptureOptions { Frames = 120, DrawList = false });
    while (!task.IsCompleted) yield return null;

    FrameReport baseline = FrameInspectorApi.LoadReport("Benchmarks/Baseline/report.json");
    List<string> over = FrameReportComparer.Check(baseline, task.Result.Report, new FrameThresholds());
    Assert.IsEmpty(over, string.Join("\n", over));
    yield return new ExitPlayMode();
}
```

- 기본 허용치 `FrameThresholds`: 시간(CPU 메인 · 렌더 스레드 · GPU 평균) +10% **그리고** +0.3ms 초과, 카운터 · 메모리 · 드로우 이벤트 · 합쳐지지 못한 패스 +10% 초과, 오버드로우 픽셀당 +0.2회 초과. 필드로 바꿉니다.
- 좋아진 지표는 실패하지 않습니다. 비교 불가 지표는 검사하지 않습니다.
- 기준과 같은 조건(같은 PC · GPU · 해상도 · 배치 여부)에서 재야 의미가 있습니다. CI가 배치 모드면 GPU · 렌더링 카운터는 검사되지 않습니다.

---

## 테스트

Test Runner(EditMode)에 `HB.FrameInspector.Tests.Editor`가 보이려면 설치한 프로젝트의 `Packages/manifest.json`에 `testables`를 넣습니다.

```json
"testables": [ "com.hb.frameinspector" ]
```

통합 테스트 1개는 Play 모드에 들어가 실제로 캡처(Profiler 녹화 포함)하고 에디터 상태(오버드로우 모드 · Frame Debugger · 일시정지 · 카메라 RT · ProfilerDriver · 렌더 그래프 세션)가 원래대로인지 봅니다. URP가 아니면 건너뜁니다. 에디터 창이 포커스를 잃어 Play 루프가 멈추면 시간 초과로 실패할 수 있습니다 (Run In Background를 켜고 돌림).
