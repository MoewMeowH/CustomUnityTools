using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.FrameInspector.Editor
{
    public sealed class FrameCaptureOptions
    {
        /// <summary>평균 · 최대를 낼 프레임 수.</summary>
        public int Frames = 60;

        /// <summary>Frame Debugger로 드로우 목록을 읽는다 (읽는 동안 Play가 잠깐 일시정지된다).</summary>
        public bool DrawList = true;

        /// <summary>오버드로우 · 화면을 그릴 카메라. 비우면 Camera.main.</summary>
        public Camera Camera;
    }

    public sealed class FrameCaptureResult
    {
        public FrameReport Report;
        public byte[] OverdrawPng;
        public byte[] ScreenPng;
    }

    /// <summary>
    /// 에디터 Play 모드에서 한 번 캡처: 프레임 시간 · 카운터 · CPU 마커 → 패스 GPU 시간 → 오버드로우 → 드로우 목록 · 셰이더 순.
    /// 측정 구간이 서로 흔들지 않게 단계마다 나눠 잰다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class FrameCapture
    {
        const string GpuFrameSampler = "FrameTime.GPU";
        const int GpuDiscoveryFrames = 3;
        const int MaxGpuPasses = 40;

        static readonly (ProfilerCategory category, string name, string unit)[] Counters =
        {
            (ProfilerCategory.Render, "Draw Calls Count", "count"),
            (ProfilerCategory.Render, "Batches Count", "count"),
            (ProfilerCategory.Render, "SetPass Calls Count", "count"),
            (ProfilerCategory.Render, "Triangles Count", "count"),
            (ProfilerCategory.Render, "Vertices Count", "count"),
            (ProfilerCategory.Memory, "GC Allocated In Frame", "bytes"),
            (ProfilerCategory.Memory, "GC Allocation In Frame Count", "count"),
        };

        // 다른 AI에게도 넘길 최소 CPU 요약. 깊은 분석(hot path · 마커 집계)은 Profiler 녹화로 한다.
        static readonly string[] CpuMarkers =
        {
            "PlayerLoop",
            "Update.ScriptRunBehaviourUpdate",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "FixedUpdate.PhysicsFixedUpdate",
            "PreLateUpdate.DirectorUpdateAnimationBegin",
            "PreLateUpdate.DirectorUpdateAnimationEnd",
            "PostLateUpdate.UpdateAllRenderers",
            "PostLateUpdate.FinishFrameRendering",
            "GC.Collect",
        };

        static bool s_Running;

        public static bool IsRunning => s_Running;

        public static async Task<FrameCaptureResult> CaptureAsync(FrameCaptureOptions options, IProgress<string> progress = null)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play 모드에서만 캡처할 수 있다.");
            if (EditorApplication.isPaused)
                throw new InvalidOperationException("Play가 일시정지 상태다. 일시정지를 풀고 다시 캡처한다.");
            if (s_Running)
                throw new InvalidOperationException("이미 캡처 중이다.");

            options ??= new FrameCaptureOptions();
            int frames = Mathf.Max(1, options.Frames);
            s_Running = true;
            try
            {
                var report = new FrameReport();
                Camera camera = options.Camera != null ? options.Camera : Camera.main;
                FillMeta(report.meta, camera, frames);

                progress?.Report("프레임 시간 · 카운터 · CPU 마커");
                await SampleTimingAsync(report, frames);

                progress?.Report("렌더 패스 GPU 시간");
                await SampleGpuPassesAsync(report, frames);

                // 에디터 UI 샘플러도 서로 중첩되므로 더하지 않고 가장 큰 것(바깥 샘플러)을 쓴다.
                float editorGpu = report.editorGpu.Count > 0 ? report.editorGpu.Max(s => s.avg) : 0f;
                report.bottleneck = ReportMath.Bottleneck(report.timing, editorGpu);

                progress?.Report("오버드로우");
                OverdrawCapture.Output od = OverdrawCapture.Capture(camera, report.meta.screenWidth, report.meta.screenHeight);
                report.overdraw = od.Report;

                FrameDebuggerReader.Result fd = null;
                if (options.DrawList)
                {
                    progress?.Report("드로우 목록 (Frame Debugger)");
                    fd = await FrameDebuggerReader.ReadAsync();
                    report.drawList = FrameDebuggerReader.ToReport(fd);
                }
                else
                {
                    report.drawList.unavailableReason = "드로우 목록 수집을 끈 상태로 캡처함";
                }

                progress?.Report("셰이더 정보");
                report.shaders = ShaderCollector.Collect(ShaderCollector.DrawCountsFrom(fd));

                AddStandingNotes(report);
                return new FrameCaptureResult { Report = report, OverdrawPng = od.OverdrawPng, ScreenPng = od.ScreenPng };
            }
            finally
            {
                s_Running = false;
            }
        }

        static void FillMeta(ReportMeta meta, Camera camera, int frames)
        {
            meta.capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            meta.unityVersion = Application.unityVersion;
            meta.renderPipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in";
            meta.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            meta.gpuName = SystemInfo.graphicsDeviceName;
            meta.cpuName = SystemInfo.processorType;
            meta.platform = Application.platform.ToString();
            meta.scene = SceneManager.GetActiveScene().name;
            meta.camera = camera != null ? camera.name : "(없음)";
            // 창 버튼(OnGUI)에서 불리면 Screen.width/height는 그 창 크기라 Game 뷰 크기를 따로 얻는다.
            Vector2 gameView = Handles.GetMainGameViewSize();
            meta.screenWidth = (int)gameView.x;
            meta.screenHeight = (int)gameView.y;
            meta.sampledFrames = frames;
            meta.targetFrameMs = Application.targetFrameRate > 0 ? 1000f / Application.targetFrameRate : 0f;
        }

        static async Task NextFrameAsync()
        {
            int frame = Time.frameCount;
            while (Time.frameCount == frame)
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("캡처 중 Play 모드가 끝났다.");
                // 일시정지(버튼 · Error Pause · Debug.Break)면 프레임이 넘어가지 않아 기다리지 않고 끝낸다.
                if (EditorApplication.isPaused)
                    throw new InvalidOperationException("캡처 중 Play가 일시정지됐다. 일시정지를 풀고 다시 캡처한다.");
                await Task.Yield();
            }
        }

        static async Task SampleTimingAsync(FrameReport report, int frames)
        {
            var cpu = new Accumulator();
            var main = new Accumulator();
            var render = new Accumulator();
            var gpu = new Accumulator();
            var ftmGpu = new Accumulator();
            var timings = new FrameTiming[1];

            // Recorder의 켜짐 상태는 샘플러마다 공유되므로(Recorder.Get이 다른 객체를 줘도 같음, 7000.0.0a8 실측) 원래 값으로 되돌린다.
            Recorder gpuRecorder = Recorder.Get(GpuFrameSampler);
            bool gpuWasEnabled = gpuRecorder.isValid && gpuRecorder.enabled;
            var counterRecorders = new List<(ProfilerRecorder rec, string name, string unit, Accumulator acc)>();
            var markerRecorders = new List<(Recorder rec, string name, bool wasEnabled, Accumulator acc)>();
            try
            {
                if (gpuRecorder.isValid) gpuRecorder.enabled = true;
                foreach (var (category, name, unit) in Counters)
                {
                    var rec = ProfilerRecorder.StartNew(category, name);
                    if (rec.Valid) counterRecorders.Add((rec, name, unit, new Accumulator()));
                    else
                    {
                        rec.Dispose();
                        report.notes.Add($"카운터 '{name}'을(를) 이 Unity 버전에서 찾지 못함");
                    }
                }

                foreach (string name in CpuMarkers)
                {
                    Recorder rec = Recorder.Get(name);
                    if (!rec.isValid) continue;
                    markerRecorders.Add((rec, name, rec.enabled, new Accumulator()));
                    rec.enabled = true;
                }

                await NextFrameAsync();
                for (int i = 0; i < frames; i++)
                {
                    await NextFrameAsync();
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        cpu.Add((float)timings[0].cpuFrameTime);
                        main.Add((float)timings[0].cpuMainThreadFrameTime);
                        render.Add((float)timings[0].cpuRenderThreadFrameTime);
                        ftmGpu.Add((float)timings[0].gpuFrameTime);
                    }

                    if (gpuRecorder.isValid && gpuRecorder.gpuSampleBlockCount > 0)
                        gpu.Add(gpuRecorder.gpuElapsedNanoseconds / 1e6f);

                    foreach (var c in counterRecorders)
                        c.acc.Add(c.rec.LastValue);

                    foreach (var m in markerRecorders)
                        if (m.rec.sampleBlockCount > 0)
                            m.acc.Add(m.rec.elapsedNanoseconds / 1e6f, m.rec.sampleBlockCount);
                }
            }
            finally
            {
                if (gpuRecorder.isValid) gpuRecorder.enabled = gpuWasEnabled;
                foreach (var c in counterRecorders) c.rec.Dispose();
                foreach (var m in markerRecorders) m.rec.enabled = m.wasEnabled;
            }

            report.timing.cpuFrameMs = cpu.ToStat();
            report.timing.cpuMainThreadMs = main.ToStat();
            report.timing.cpuRenderThreadMs = render.ToStat();
            if (gpu.Count > 0)
            {
                report.timing.gpuFrameMs = gpu.ToStat();
                report.timing.gpuSource = "Recorder(FrameTime.GPU)";
            }
            else
            {
                report.timing.gpuFrameMs = ftmGpu.ToStat();
                report.timing.gpuSource = "FrameTimingManager (에디터에서는 실제보다 작게 나올 수 있음)";
            }

            report.counters = counterRecorders.Select(c => c.acc.ToValueStat(c.name, c.unit)).ToList();
            report.cpuMarkers = markerRecorders.Where(m => m.acc.Count > 0)
                .Select(m => m.acc.ToValueStat(m.name, "ms"))
                .OrderByDescending(s => s.avg)
                .ToList();
        }

        static async Task SampleGpuPassesAsync(FrameReport report, int frames)
        {
            var names = new List<string>();
            Sampler.GetNames(names);
            var all = names.Distinct()
                .Where(n => n != GpuFrameSampler)
                .Select(n => (name: n, rec: Recorder.Get(n)))
                .Where(x => x.rec.isValid)
                .Select(x => (x.name, x.rec, wasEnabled: x.rec.enabled))
                .ToList();

            var selected = new List<(string name, Recorder rec, Accumulator acc)>();
            try
            {
                // 모든 샘플러를 몇 프레임만 켜서 GPU 샘플이 있는 것만 고르고 나머지는 바로 끈다.
                foreach (var x in all) x.rec.enabled = true;
                var hasGpu = new HashSet<string>();
                for (int i = 0; i < GpuDiscoveryFrames; i++)
                {
                    await NextFrameAsync();
                    foreach (var x in all)
                        if (x.rec.gpuSampleBlockCount > 0)
                            hasGpu.Add(x.name);
                }

                foreach (var x in all)
                {
                    if (hasGpu.Contains(x.name)) selected.Add((x.name, x.rec, new Accumulator()));
                    else x.rec.enabled = x.wasEnabled;
                }

                for (int i = 0; i < frames; i++)
                {
                    await NextFrameAsync();
                    foreach (var s in selected)
                        if (s.rec.gpuSampleBlockCount > 0)
                            s.acc.Add(s.rec.gpuElapsedNanoseconds / 1e6f, s.rec.gpuSampleBlockCount);
                }
            }
            finally
            {
                foreach (var x in all) x.rec.enabled = x.wasEnabled;
            }

            if (selected.Count == 0)
                report.notes.Add($"렌더 패스 GPU 시간 없음 — 이 그래픽 API({SystemInfo.graphicsDeviceType})에서 GPU 샘플이 잡히지 않음");

            List<ValueStat> stats = selected.Where(s => s.acc.Count > 0)
                .Select(s => s.acc.ToValueStat(s.name, "ms"))
                .OrderByDescending(s => s.avg)
                .ToList();
            report.gpuPasses = stats.Where(s => !ReportMath.IsEditorUiMarker(s.name)).Take(MaxGpuPasses).ToList();
            report.editorGpu = stats.Where(s => ReportMath.IsEditorUiMarker(s.name)).ToList();
        }

        static void AddStandingNotes(FrameReport report)
        {
            report.notes.Add("에디터 Play 모드에서 잰 값이다 — 에디터 오버헤드가 섞이고, 빌드 · 실제 기기와 다르다.");
            report.notes.Add("렌더 패스 GPU 시간은 중첩된다 (부모 샘플러가 자식 시간을 포함). 합계를 내지 말 것.");
            report.notes.Add("렌더링 카운터는 이 프레임에 그린 모든 카메라 합이다 (Scene 뷰가 열려 있으면 그 카메라도 포함될 수 있음).");
            report.notes.Add("오버드로우는 선택한 카메라 하나만 그린 결과다 (Screen Space Overlay UI · 다른 카메라는 제외).");
        }
    }
}
