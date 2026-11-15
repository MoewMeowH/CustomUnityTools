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

            Task<FrameCaptureResult> task = FrameCapture.CaptureAsync(new FrameCaptureOptions { Frames = 5, DrawList = true, Camera = cam });
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
