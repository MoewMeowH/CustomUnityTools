using System.IO;
using HB.FrameInspector.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HB.FrameInspector.Tests
{
    public class ReportOutputTests
    {
        string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "HBFrameInspectorTests_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
        }

        static FrameReport SampleReport(string scene = "Stage1")
        {
            var r = new FrameReport();
            r.meta.capturedAt = "2026-11-15 16:26:27";
            r.meta.scene = scene;
            r.meta.sampledFrames = 30;
            r.timing.cpuMainThreadMs = new Stat { available = true, avg = 1.5f, max = 3f, min = 1f };
            r.timing.gpuFrameMs = new Stat { available = true, avg = 4f, max = 5f, min = 3f };
            r.timing.gpuSource = "Recorder(FrameTime.GPU)";
            r.bottleneck = ReportMath.Bottleneck(r.timing, 0f);
            r.gpuPasses.Add(new ValueStat { name = "DrawTransparentObjects", unit = "ms", avg = 2.5f, max = 3f, callsAvg = 1 });
            r.overdraw.available = true;
            r.overdraw.maxDisplayedCount = 10;
            r.overdraw.all = OverdrawMath.Compute(new[] { 0, 1, 10 }, 10);
            r.drawList.unavailableReason = "드로우 목록 수집을 끈 상태로 캡처함";
            r.shaders.Add(new ShaderStat { shader = "Sprites/Default", drawCount = 20, transparent = true, minRenderQueue = 3000, maxRenderQueue = 3000 });
            r.notes.Add("참고 문장");
            return r;
        }

        [Test]
        public void Markdown_HasEverySectionAndExplainsUnits()
        {
            string md = ReportWriter.ToMarkdown(SampleReport(), hasOverdrawImage: true, hasScreenImage: true);

            foreach (string heading in new[] { "## 측정 조건", "## 병목 힌트", "## 프레임 시간 (ms)", "## 렌더링 · 메모리 카운터", "## CPU 요약", "## 렌더 패스 GPU 시간", "## 오버드로우", "## 드로우 목록", "## 셰이더", "## 참고 · 한계" })
                StringAssert.Contains(heading, md);

            StringAssert.Contains("ms(밀리초)", md);
            StringAssert.Contains("**gpu**", md);
            StringAssert.Contains("DrawTransparentObjects", md);
            StringAssert.Contains("![overdraw](overdraw.png)", md);
            StringAssert.Contains("드로우 목록 수집을 끈 상태로 캡처함", md);
            StringAssert.Contains("Sprites/Default", md);
        }

        [Test]
        public void Markdown_EscapesPipeInCells()
        {
            FrameReport r = SampleReport();
            r.shaders[0].shader = "A|B";

            StringAssert.Contains("A\\|B", ReportWriter.ToMarkdown(r, false, false));
        }

        [Test]
        public void Json_RoundTrips()
        {
            FrameReport r = SampleReport();

            var back = JsonUtility.FromJson<FrameReport>(ReportWriter.ToJson(r));

            Assert.AreEqual("Stage1", back.meta.scene);
            Assert.AreEqual(4f, back.timing.gpuFrameMs.avg);
            Assert.AreEqual("DrawTransparentObjects", back.gpuPasses[0].name);
            Assert.AreEqual(11, back.overdraw.all.histogramPercent.Count);
        }

        [Test]
        public void Save_WritesFilesIntoTimestampSceneFolder()
        {
            var result = new FrameCaptureResult { Report = SampleReport(), OverdrawPng = new byte[] { 1, 2, 3 }, ScreenPng = null };

            string folder = FrameInspectorApi.Save(result, _tempRoot);

            Assert.AreEqual("20261115_162627_Stage1", Path.GetFileName(folder));
            Assert.IsTrue(File.Exists(Path.Combine(folder, ReportWriter.MarkdownFile)));
            Assert.IsTrue(File.Exists(Path.Combine(folder, ReportWriter.JsonFile)));
            Assert.IsTrue(File.Exists(Path.Combine(folder, ReportWriter.OverdrawFile)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, ReportWriter.ScreenFile)), "화면 그림이 없으면 파일도 없음");
            StringAssert.DoesNotContain("![screen]", File.ReadAllText(Path.Combine(folder, ReportWriter.MarkdownFile)));
        }

        [Test]
        public void Save_SameSecondTwice_GetsSuffix_AndUntitledScene()
        {
            var result = new FrameCaptureResult { Report = SampleReport(scene: "") };

            string first = FrameInspectorApi.Save(result, _tempRoot);
            string second = FrameInspectorApi.Save(result, _tempRoot);

            Assert.AreEqual("20261115_162627_Untitled", Path.GetFileName(first));
            Assert.AreEqual("20261115_162627_Untitled_2", Path.GetFileName(second));
        }

        [Test]
        public void AiClipboardText_HasMarkdownBodyAndImagePaths()
        {
            var result = new FrameCaptureResult { Report = SampleReport(), OverdrawPng = new byte[] { 1 }, ScreenPng = new byte[] { 2 } };
            string folder = FrameInspectorApi.Save(result, _tempRoot);

            string text = FrameInspectorApi.BuildAiClipboardText(folder);

            StringAssert.Contains("## 병목 힌트", text);
            StringAssert.Contains(Path.Combine(folder, ReportWriter.OverdrawFile), text);
            StringAssert.Contains(Path.Combine(folder, ReportWriter.ScreenFile), text);
        }
    }
}
