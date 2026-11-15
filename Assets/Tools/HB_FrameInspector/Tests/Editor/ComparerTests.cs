using System.Collections.Generic;
using System.Linq;
using HB.FrameInspector.Editor;
using NUnit.Framework;

namespace HB.FrameInspector.Tests
{
    public class ComparerTests
    {
        static FrameReport Report(float cpu = 2f, float gpu = 4f, float drawCalls = 100f, float overdraw = 1f, bool batch = false)
        {
            var r = new FrameReport();
            r.meta.unityVersion = "7000.0.0a8";
            r.meta.scene = "Bench";
            r.meta.screenWidth = 800;
            r.meta.screenHeight = 450;
            r.meta.batchMode = batch;
            r.timing.cpuMainThreadMs = new Stat { available = true, avg = cpu, max = cpu * 2 };
            r.timing.gpuFrameMs = batch ? new Stat() : new Stat { available = true, avg = gpu, max = gpu * 2 };
            r.counters.Add(new ValueStat { name = "GC Used Memory", unit = "bytes", avg = 100f * 1024 * 1024 });
            if (!batch) r.counters.Add(new ValueStat { name = "Draw Calls Count", unit = "count", avg = drawCalls });
            r.overdraw.available = true;
            r.overdraw.all = new OverdrawStats { avgPerPixel = overdraw };
            return r;
        }

        static MetricDelta Find(ComparisonReport c, string name) => c.metrics.Single(m => m.name == name);

        [Test]
        public void Compare_DirectionAndPercent()
        {
            ComparisonReport c = FrameReportComparer.Compare(Report(gpu: 4f, cpu: 2f), Report(gpu: 5f, cpu: 1.5f));

            MetricDelta gpu = Find(c, "GPU 프레임 평균");
            Assert.AreEqual(1f, gpu.delta, 1e-4f);
            Assert.AreEqual(25f, gpu.deltaPercent, 1e-3f);
            Assert.AreEqual(1, gpu.direction, "GPU가 늘면 나빠짐");
            Assert.AreEqual(-1, Find(c, "CPU 메인 스레드 평균").direction, "CPU가 줄면 좋아짐");
        }

        [Test]
        public void Compare_UnderOnePercent_IsSame()
        {
            ComparisonReport c = FrameReportComparer.Compare(Report(gpu: 4f), Report(gpu: 4.02f));

            Assert.AreEqual(0, Find(c, "GPU 프레임 평균").direction);
        }

        [Test]
        public void Compare_TinyAbsoluteChange_IsSame()
        {
            FrameReport a = Report(), b = Report();
            a.cpuMarkers.Add(new ValueStat { name = "Initialize Frame", unit = "ms", avg = 0.001f });
            b.cpuMarkers.Add(new ValueStat { name = "Initialize Frame", unit = "ms", avg = 0.003f });

            MetricDelta d = Find(FrameReportComparer.Compare(a, b), "Initialize Frame");

            Assert.AreEqual(200f, d.deltaPercent, 1e-2f);
            Assert.AreEqual(0, d.direction, "0.01ms 미만 차이는 잡음으로 같음");
        }

        [Test]
        public void Check_FormatsBytesAsMegabytes()
        {
            FrameReport a = Report(), b = Report();
            b.counters[0].avg = 120f * 1024 * 1024;

            List<string> v = FrameReportComparer.Check(a, b);

            Assert.IsTrue(v.Any(s => s.Contains("GC Used Memory: 100.0 MB → 120.0 MB")), string.Join("\n", v));
        }

        [Test]
        public void Compare_BatchSide_MakesRenderMetricsNotComparable()
        {
            ComparisonReport c = FrameReportComparer.Compare(Report(), Report(batch: true));

            Assert.IsFalse(Find(c, "GPU 프레임 평균").comparable);
            MetricDelta draw = Find(c, "Draw Calls Count");
            Assert.IsFalse(draw.comparable, "배치 모드 리포트에는 렌더링 카운터가 없다");
            Assert.AreEqual("이후 리포트에 없음", draw.reason);
            Assert.IsTrue(Find(c, "CPU 메인 스레드 평균").comparable);
            Assert.IsTrue(c.conditionDiffs.Any(d => d.StartsWith("배치 모드")));
        }

        [Test]
        public void Compare_ConditionDiffs()
        {
            FrameReport b = Report();
            b.meta.scene = "Other";
            b.meta.screenWidth = 1920;

            ComparisonReport c = FrameReportComparer.Compare(Report(), b);

            Assert.IsTrue(c.conditionDiffs.Any(d => d.StartsWith("씬")));
            Assert.IsTrue(c.conditionDiffs.Any(d => d.StartsWith("Game 뷰 해상도")));
            Assert.IsFalse(c.conditionDiffs.Any(d => d.StartsWith("Unity")));
        }

        [Test]
        public void Compare_UnavailableOverdraw_NotComparable()
        {
            FrameReport b = Report();
            b.overdraw.available = false;

            Assert.IsFalse(Find(FrameReportComparer.Compare(Report(), b), "픽셀당 평균").comparable);
        }

        [Test]
        public void Compare_ZeroBaseline_Percent()
        {
            ComparisonReport c = FrameReportComparer.Compare(Report(drawCalls: 0f), Report(drawCalls: 5f));

            Assert.AreEqual(100f, Find(c, "Draw Calls Count").deltaPercent);
        }

        [Test]
        public void Check_TimeNeedsBothPercentAndMs()
        {
            // +20% 이지만 +0.2ms → 허용 (0.3ms 이하)
            Assert.IsEmpty(FrameReportComparer.Check(Report(cpu: 1f), Report(cpu: 1.2f)));
            // +20% 이고 +0.4ms → 넘음
            List<string> v = FrameReportComparer.Check(Report(cpu: 2f), Report(cpu: 2.4f));
            Assert.AreEqual(1, v.Count);
            StringAssert.Contains("CPU 메인 스레드 평균", v[0]);
        }

        [Test]
        public void Check_CounterAndOverdraw()
        {
            List<string> v = FrameReportComparer.Check(Report(drawCalls: 100f, overdraw: 1f), Report(drawCalls: 115f, overdraw: 1.3f));

            Assert.IsTrue(v.Any(s => s.Contains("Draw Calls Count")));
            Assert.IsTrue(v.Any(s => s.Contains("픽셀당 평균")));
        }

        [Test]
        public void Check_ImprovementsNeverFail()
        {
            Assert.IsEmpty(FrameReportComparer.Check(Report(cpu: 5f, gpu: 9f, drawCalls: 300f, overdraw: 3f), Report(cpu: 1f, gpu: 1f, drawCalls: 10f, overdraw: 0.5f)));
        }

        [Test]
        public void Check_CustomThresholds()
        {
            var strict = new FrameThresholds { counterPercent = 1f };

            Assert.IsNotEmpty(FrameReportComparer.Check(FrameReportComparer.Compare(Report(drawCalls: 100f), Report(drawCalls: 105f)), strict));
            Assert.IsEmpty(FrameReportComparer.Check(Report(drawCalls: 100f), Report(drawCalls: 105f)));
        }

        [Test]
        public void Compare_DifferentGpuSource_NotComparableAndListed()
        {
            FrameReport a = Report(), b = Report(gpu: 1f);
            a.timing.gpuSource = "Recorder(FrameTime.GPU)";
            b.timing.gpuSource = "FrameTimingManager";

            ComparisonReport c = FrameReportComparer.Compare(a, b);

            Assert.IsFalse(Find(c, "GPU 프레임 평균").comparable);
            Assert.IsTrue(c.conditionDiffs.Any(d => d.StartsWith("GPU 값 출처")));
            Assert.IsEmpty(FrameReportComparer.Check(c));
        }

        [Test]
        public void Compare_RenderGraph_NewCameraOnlyInCurrent_IsListed()
        {
            FrameReport a = Report(), b = Report();
            a.renderGraph.available = b.renderGraph.available = true;
            a.renderGraph.executions.Add(new RenderGraphExecution { graphName = "URP", executionName = "Main Camera", totalPasses = 10 });
            b.renderGraph.executions.Add(new RenderGraphExecution { graphName = "URP", executionName = "Main Camera", totalPasses = 10 });
            b.renderGraph.executions.Add(new RenderGraphExecution { graphName = "URP", executionName = "Minimap", totalPasses = 6, unmergedPasses = 2 });

            MetricDelta added = Find(FrameReportComparer.Compare(a, b), "URP/Minimap 합쳐지지 못한 패스");

            Assert.IsFalse(added.comparable);
            Assert.AreEqual("이전 리포트에 이 카메라 없음", added.reason);
        }

        [Test]
        public void CompareMarkdown_HasUnitColumn()
        {
            string md = ReportWriter.ToMarkdown(FrameReportComparer.Compare(Report(gpu: 4f), Report(gpu: 6f)));

            StringAssert.Contains("| 묶음 | 지표 | 단위 |", md);
            StringAssert.Contains("| 프레임 시간 | GPU 프레임 평균 | ms |", md);
        }

        [Test]
        public void Compare_RenderGraph_MatchesByExecution()
        {
            FrameReport a = Report(), b = Report();
            a.renderGraph.available = b.renderGraph.available = true;
            a.renderGraph.executions.Add(new RenderGraphExecution { graphName = "URP", executionName = "Main Camera", totalPasses = 10, unmergedPasses = 3 });
            b.renderGraph.executions.Add(new RenderGraphExecution { graphName = "URP", executionName = "Main Camera", totalPasses = 10, unmergedPasses = 7 });

            ComparisonReport c = FrameReportComparer.Compare(a, b);

            MetricDelta unmerged = Find(c, "URP/Main Camera 합쳐지지 못한 패스");
            Assert.AreEqual(4f, unmerged.delta);
            Assert.IsTrue(FrameReportComparer.Check(c).Any(s => s.Contains("합쳐지지 못한 패스")));
        }
    }
}
