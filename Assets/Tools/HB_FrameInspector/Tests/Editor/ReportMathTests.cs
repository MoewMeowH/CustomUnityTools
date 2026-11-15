using HB.FrameInspector.Editor;
using NUnit.Framework;

namespace HB.FrameInspector.Tests
{
    public class ReportMathTests
    {
        [TestCase("UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera/ExecuteRenderGraph/(RP 1:0) DrawOpaqueObjects/RenderLoop.ScheduleDraw/RenderLoop.DrawSRPBatcher", "DrawOpaqueObjects")]
        [TestCase("UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera/ExecuteRenderGraph/(RP 0:0) Draw Main Light Shadowmap/MainLightShadow", "Draw Main Light Shadowmap")]
        [TestCase("UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera/ExecuteRenderGraph/UberPost/Graphics.Blit", "UberPost")]
        [TestCase("Camera.Render/Drawing/Render.OpaqueGeometry", "Drawing")]
        [TestCase("Clear", "Clear")]
        [TestCase("", "(이름 없음)")]
        public void PassNameFromEventPath(string path, string expected)
        {
            Assert.AreEqual(expected, ReportMath.PassNameFromEventPath(path));
        }

        [TestCase("GUIView.DoPaint", true)]
        [TestCase("GUITexture.Draw", true)]
        [TestCase("UIR.DrawChain", true)]
        [TestCase("DrawOpaqueObjects", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsEditorUiMarker(string name, bool expected)
        {
            Assert.AreEqual(expected, ReportMath.IsEditorUiMarker(name));
        }

        [Test]
        public void CountBy_SortsByCountThenName()
        {
            var result = ReportMath.CountBy(new[] { "b", "a", "b", "c", "a", "b" });

            Assert.AreEqual("b", result[0].name);
            Assert.AreEqual(3, result[0].count);
            Assert.AreEqual("a", result[1].name);
            Assert.AreEqual("c", result[2].name);
        }

        [Test]
        public void CountBy_Limit()
        {
            Assert.AreEqual(2, ReportMath.CountBy(new[] { "a", "b", "c" }, 2).Count);
        }

        static TimingStats Timing(float? cpu, float? gpu)
        {
            var t = new TimingStats();
            if (cpu.HasValue) t.cpuMainThreadMs = new Stat { available = true, avg = cpu.Value };
            if (gpu.HasValue) t.gpuFrameMs = new Stat { available = true, avg = gpu.Value };
            return t;
        }

        [TestCase(2f, 10f, 0f, "gpu")]
        [TestCase(10f, 2f, 0f, "cpu")]
        [TestCase(5f, 5.5f, 0f, "balanced")]
        [TestCase(1f, 0.1f, 2.4f, "cpu")]
        public void Bottleneck_Side(float cpu, float gpu, float editorUi, string expected)
        {
            Assert.AreEqual(expected, ReportMath.Bottleneck(Timing(cpu, gpu), editorUi).side);
        }

        [Test]
        public void Bottleneck_RenderThreadCounts()
        {
            TimingStats t = Timing(1f, 2f);
            t.cpuRenderThreadMs = new Stat { available = true, avg = 6f };

            BottleneckHint hint = ReportMath.Bottleneck(t, 0f);

            Assert.AreEqual("cpu", hint.side, "렌더 스레드 6ms > GPU 2ms");
            StringAssert.Contains("렌더 스레드", hint.reason);
        }

        [Test]
        public void Bottleneck_NumbersUseInvariantCulture()
        {
            System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                StringAssert.Contains("1.50ms", ReportMath.Bottleneck(Timing(1.5f, 0.2f), 0f).reason);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        [Test]
        public void Bottleneck_Unknown_WhenMissing()
        {
            Assert.AreEqual("unknown", ReportMath.Bottleneck(Timing(null, 1f), 0f).side);
            Assert.AreEqual("unknown", ReportMath.Bottleneck(Timing(1f, null), 0f).side);
        }

        [Test]
        public void Bottleneck_MentionsEditorUi_OnlyWhenLargerThanGameGpu()
        {
            StringAssert.Contains("에디터 창 그리기", ReportMath.Bottleneck(Timing(1f, 0.1f), 2.4f).reason);
            StringAssert.DoesNotContain("에디터 창 그리기", ReportMath.Bottleneck(Timing(1f, 3f), 2.4f).reason);
        }
    }
}
