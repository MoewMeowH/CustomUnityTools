using System.Collections;
using System.Threading.Tasks;
using HB.FrameInspector.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
#if HB_FI_URP
using UnityEngine.Rendering.Universal;
#endif

namespace HB.FrameInspector.Tests
{
    /// <summary>에디터 테스트에서 Play 모드에 들어가 실제로 캡처하고, 끝난 뒤 에디터 상태가 원래대로인지 본다.</summary>
    public class FrameCapturePlayModeTests
    {
        const int QuadCount = 6;

        [UnityTest]
        public IEnumerator Capture_StackedTransparentQuads_CountsOverdrawAndRestoresState()
        {
#if !HB_FI_URP
            Assert.Ignore("URP 패키지가 없는 프로젝트");
            yield break;
#else
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                Assert.Ignore("URP가 활성 파이프라인이 아닌 프로젝트");

            yield return new EnterPlayMode();
            var camGo = new GameObject("FI_TestCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1, 1, 1, 0.1f) };
            for (int i = 0; i < QuadCount; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "FI_TestQuad" + i;
                q.transform.position = new Vector3(0, 0, -5 + i * 0.1f);
                q.transform.localScale = Vector3.one * 4;
                q.GetComponent<Renderer>().sharedMaterial = mat;
            }
            yield return null;

            var rs = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
            DebugOverdrawMode modeBefore = rs.overdrawMode;
            bool profilerBefore = UnityEditorInternal.ProfilerDriver.enabled;

            Task<FrameCaptureResult> task = FrameCapture.CaptureAsync(new FrameCaptureOptions { Frames = 5, DrawList = true, Camera = cam, RecordProfiler = true });
            while (!task.IsCompleted) yield return null;
            Assert.IsNull(task.Exception, task.Exception?.ToString());
            FrameReport r = task.Result.Report;

            Assert.IsTrue(r.overdraw.available, r.overdraw.unavailableReason);
            Assert.AreEqual(QuadCount, r.overdraw.all.histogramPercent.FindLastIndex(p => p > 0f), "겹친 쿼드 수만큼 겹친 픽셀이 있어야 함");
            Assert.IsNotNull(task.Result.OverdrawPng);
            Assert.IsNotNull(task.Result.ScreenPng);
            Assert.IsTrue(r.timing.cpuMainThreadMs.available);
            Assert.Greater(r.counters.Count, 0);

            // 배치 모드(-batchmode)에서는 창을 그리지 않아 Frame Debugger가 프레임을 받지 못한다 (6000.3.23f1 실측).
            if (!Application.isBatchMode)
            {
                Assert.IsTrue(r.drawList.available, "드로우 목록을 읽지 못함: " + r.drawList.unavailableReason);
                Assert.GreaterOrEqual(r.drawList.eventCount, QuadCount);
                Assert.IsTrue(r.shaders.Exists(s => s.shader == "Sprites/Default" && s.drawCount >= QuadCount));
            }
            else
            {
                Assert.IsTrue(r.meta.batchMode);
                Assert.IsFalse(r.timing.gpuFrameMs.available, "배치 모드는 GPU 시간을 측정하지 않는다");
                Assert.IsFalse(r.counters.Exists(c => c.name == "Draw Calls Count"), "배치 모드는 렌더링 카운터를 빼고 적는다");
            }

            // Profiler 녹화 (배치 모드에서는 저장되지 않아 건너뛴다 — 6000.3.23f1 실측)
            if (!Application.isBatchMode)
            {
                Assert.IsTrue(r.profiler.recorded, r.profiler.unavailableReason);
                Assert.IsTrue(System.IO.File.Exists(task.Result.ProfilerTempFile));
                System.IO.File.Delete(task.Result.ProfilerTempFile);
            }
            else
            {
                Assert.IsFalse(r.profiler.recorded);
                StringAssert.Contains("배치 모드", r.profiler.unavailableReason);
            }
            Assert.AreEqual(profilerBefore, UnityEditorInternal.ProfilerDriver.enabled, "ProfilerDriver.enabled 원래대로");

            // 렌더 그래프: 리더와 같은 조건(세션 관리자 · 라이브 세션 타입이 있음 = URP core 17.7+)이고 배치가 아니면
            // 지표가 있고, 끝나면 우리가 연 세션은 닫힌다
            const System.Reflection.BindingFlags anyStatic = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.Assembly core = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph).Assembly;
            System.Type manager = core.GetType("UnityEngine.Rendering.RenderGraphModule.RenderGraphDebugSessionManager");
            System.Type liveSession = core.GetType("UnityEngine.Rendering.RenderGraphModule.LiveRenderGraphDebugSession");
            if (manager != null && liveSession != null && !Application.isBatchMode)
            {
                Assert.IsTrue(r.renderGraph.available, r.renderGraph.unavailableReason);
                Assert.Greater(r.renderGraph.executions.Count, 0);
                Assert.Greater(r.renderGraph.executions[0].totalPasses, 0, "0을 실측처럼 남기지 않음");
                Assert.IsFalse((bool)manager.GetProperty("hasActiveDebugSession", anyStatic).GetValue(null), "렌더 그래프 세션 닫힘");
            }
            else
            {
                Assert.IsFalse(r.renderGraph.available);
                Assert.IsNotEmpty(r.renderGraph.unavailableReason);
            }

            // 원상 복구
            Assert.AreEqual(modeBefore, rs.overdrawMode, "오버드로우 모드 원래대로");
            Assert.IsFalse(FrameDebugger.enabled, "Frame Debugger 꺼짐");
            Assert.IsFalse(EditorApplication.isPaused, "일시정지 풀림");
            Assert.IsNull(cam.targetTexture, "카메라 targetTexture 원래대로");
            Assert.IsFalse(FrameCapture.IsRunning);
            yield return new ExitPlayMode();
#endif
        }
    }
}
