using System.IO;
using HB.FrameInspector.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HB.FrameInspector.Tests
{
    public class ReportOutput2Tests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "HBFrameInspectorTests2_" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        static FrameReport Sample(string capturedAt, float gpu)
        {
            var r = new FrameReport();
            r.meta.capturedAt = capturedAt;
            r.meta.scene = "Bench";
            r.timing.gpuFrameMs = new Stat { available = true, avg = gpu, max = gpu };
            r.timing.cpuMainThreadMs = new Stat { available = true, avg = 1f, max = 1f };
            return r;
        }

        [TestCase(0f, "bytes", 0, "0.0 KB")]
        [TestCase(2048f, "bytes", 0, "2.0 KB")]
        [TestCase(1572864f, "bytes", 0, "1.5 MB")]
        [TestCase(82f, "count", 0, "82")]
        [TestCase(0.12345f, "ms", 3, "0.123")]
        [TestCase(2f, "ms", 3, "2")]
        public void FormatCounter(float value, string unit, int decimals, string expected)
        {
            Assert.AreEqual(expected, ReportMath.FormatCounter(value, unit, decimals));
        }

        [Test]
        public void Markdown_NewSections_AndBatchRow()
        {
            FrameReport r = Sample("2026-11-15 10:00:00", 1f);
            r.meta.batchMode = true;
            r.renderGraph.unavailableReason = "이유A";
            r.profiler.recorded = true;
            r.profiler.frames = 30;
            r.profiler.file = ReportWriter.ProfilerFile;

            string md = ReportWriter.ToMarkdown(r, false, false);

            StringAssert.Contains("## 렌더 그래프", md);
            StringAssert.Contains("이유A", md);
            StringAssert.Contains("## Profiler 녹화", md);
            StringAssert.Contains("30프레임", md);
            StringAssert.Contains("배치 모드 | 예", md);
        }

        [Test]
        public void Markdown_RenderGraphTable()
        {
            FrameReport r = Sample("2026-11-15 10:00:00", 1f);
            r.renderGraph.available = true;
            r.renderGraph.executions.Add(new RenderGraphExecution
            {
                graphName = "URPRenderGraph", executionName = "Main Camera", totalPasses = 10, unmergedPasses = 7,
                insights = { "Native pass merging: 7 merge breaks" },
                unmerged = { new RenderGraphUnmergedPass { name = "Draw Opaque Objects", nextPassName = "Draw Skybox", breakReason = "sizes\ndo not match" } },
            });

            string md = ReportWriter.ToMarkdown(r, false, false);

            StringAssert.Contains("### URPRenderGraph / Main Camera", md);
            StringAssert.Contains("Native pass merging", md);
            StringAssert.Contains("| Draw Opaque Objects | Draw Skybox | sizes do not match |", md);
        }

        [Test]
        public void LoadReport_ReadsOldFormatWithoutNewFields()
        {
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, "report.json");
            File.WriteAllText(path, "{\"meta\":{\"scene\":\"Old\",\"toolVersion\":\"1.0.0\"},\"timing\":{\"gpuFrameMs\":{\"available\":true,\"avg\":3.0}}}");

            FrameReport r = FrameInspectorApi.LoadReport(_root);

            Assert.AreEqual("Old", r.meta.scene);
            Assert.AreEqual(3f, r.timing.gpuFrameMs.avg);
            Assert.IsFalse(r.renderGraph.available);
            Assert.IsFalse(r.profiler.recorded);
        }

        [Test]
        public void LoadReport_MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => FrameInspectorApi.LoadReport(Path.Combine(_root, "nope")));
        }

        [Test]
        public void CompareAndSave_WritesCompareFiles_AndListSkipsCompareFolders()
        {
            string a = FrameInspectorApi.Save(new FrameCaptureResult { Report = Sample("2026-11-15 10:00:00", 4f) }, _root);
            string b = FrameInspectorApi.Save(new FrameCaptureResult { Report = Sample("2026-11-15 10:05:00", 6f) }, _root);

            string folder = FrameInspectorApi.CompareAndSave(a, b, _root);

            string md = File.ReadAllText(Path.Combine(folder, ReportWriter.CompareMarkdownFile));
            StringAssert.Contains("## 나빠진 것", md);
            StringAssert.Contains("GPU 프레임 평균", md);
            StringAssert.Contains("## 허용치 검사", md);
            Assert.IsTrue(File.Exists(Path.Combine(folder, ReportWriter.CompareJsonFile)));
            CollectionAssert.AreEqual(new[] { b, a }, FrameInspectorApi.ListReportFolders(_root), "최근 것부터, 비교 폴더는 빠짐");
            StringAssert.Contains("## 나빠진 것", FrameInspectorApi.BuildAiClipboardTextForComparison(folder));
        }

        [Test]
        public void Save_MovesProfilerTempFileIntoReportFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), $"HBFrameInspector_test_{System.Guid.NewGuid():N}.data");
            File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });
            var result = new FrameCaptureResult { Report = Sample("2026-11-15 10:00:00", 1f), ProfilerTempFile = temp };
            result.Report.profiler.recorded = true;

            string folder = FrameInspectorApi.Save(result, _root);
            string again = FrameInspectorApi.Save(result, _root);

            Assert.IsTrue(File.Exists(Path.Combine(folder, ReportWriter.ProfilerFile)));
            Assert.IsFalse(File.Exists(temp), "임시 파일은 지움");
            Assert.IsTrue(File.Exists(Path.Combine(again, ReportWriter.ProfilerFile)), "다시 저장해도 사본에서 복사");
            StringAssert.Contains("\"file\": \"profiler.data\"", File.ReadAllText(Path.Combine(folder, ReportWriter.JsonFile)));
        }

        [Test]
        public void Save_AfterSavedCopyDeleted_DoesNotPointToMissingFile()
        {
            string temp = Path.Combine(Path.GetTempPath(), $"HBFrameInspector_test_{System.Guid.NewGuid():N}.data");
            File.WriteAllBytes(temp, new byte[] { 1 });
            var result = new FrameCaptureResult { Report = Sample("2026-11-15 10:00:00", 1f), ProfilerTempFile = temp };
            result.Report.profiler.recorded = true;
            string first = FrameInspectorApi.Save(result, _root);
            Directory.Delete(first, true);

            string again = FrameInspectorApi.Save(result, _root);

            Assert.IsFalse(File.Exists(Path.Combine(again, ReportWriter.ProfilerFile)));
            Assert.IsFalse(result.Report.profiler.recorded);
            StringAssert.DoesNotContain("`profiler.data`", File.ReadAllText(Path.Combine(again, ReportWriter.MarkdownFile)));
        }
    }
}
