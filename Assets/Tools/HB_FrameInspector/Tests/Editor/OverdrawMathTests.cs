using HB.FrameInspector.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HB.FrameInspector.Tests
{
    public class OverdrawMathTests
    {
        [TestCase(0f, 10, 0)]
        [TestCase(0.1f, 10, 1)]
        [TestCase(0.3f, 10, 3)]
        [TestCase(0.29f, 10, 3)]
        [TestCase(1f, 10, 10)]
        [TestCase(1.7f, 10, 10)]
        [TestCase(-0.2f, 10, 0)]
        public void ToCount_RoundsAndClamps(float red, int max, int expected)
        {
            Assert.AreEqual(expected, OverdrawMath.ToCount(red, max));
        }

        [Test]
        public void Compute_KnownPixels()
        {
            // 10픽셀: 0 4개, 1 2개, 3 1개, 5 1개, 10 2개 (최대 10)
            int[] counts = { 0, 0, 0, 0, 1, 1, 3, 5, 10, 10 };

            OverdrawStats s = OverdrawMath.Compute(counts, 10);

            Assert.AreEqual(3.0f, s.avgPerPixel, 1e-4f);          // 30 / 10
            Assert.AreEqual(5.0f, s.avgPerCoveredPixel, 1e-4f);   // 30 / 6
            Assert.AreEqual(60f, s.coveredPercent, 1e-4f);
            Assert.AreEqual(40f, s.atLeast3Percent, 1e-4f);
            Assert.AreEqual(30f, s.atLeast5Percent, 1e-4f);
            Assert.AreEqual(20f, s.atMaxPercent, 1e-4f);
            Assert.AreEqual(11, s.histogramPercent.Count);
            Assert.AreEqual(40f, s.histogramPercent[0], 1e-4f);
            Assert.AreEqual(20f, s.histogramPercent[10], 1e-4f);
        }

        [Test]
        public void Compute_ClampsCountsAboveMax()
        {
            OverdrawStats s = OverdrawMath.Compute(new[] { 15, 10 }, 10);

            Assert.AreEqual(10f, s.avgPerPixel, 1e-4f);
            Assert.AreEqual(100f, s.atMaxPercent, 1e-4f);
        }

        [Test]
        public void Compute_Empty_ReturnsZeros()
        {
            OverdrawStats s = OverdrawMath.Compute(new int[0], 10);

            Assert.AreEqual(0f, s.avgPerPixel);
            Assert.AreEqual(0, s.histogramPercent.Count);
        }

        [Test]
        public void ToColor_MatchesUrpPalette_Ends()
        {
            Assert.AreEqual(new Color32(0, 0, 0, 255), OverdrawMath.ToColor(0, 10), "0회 = 검정");
            Assert.AreEqual(new Color32(0, 0, 255, 255), OverdrawMath.ToColor(1, 10), "1회 = 파랑 (hue 240)");
            Assert.AreEqual(new Color32(255, 255, 255, 255), OverdrawMath.ToColor(10, 10), "최대 = 하양 (채도 0)");
        }

        [Test]
        public void Compute_SkipsExcludedPixels()
        {
            OverdrawStats s = OverdrawMath.Compute(new[] { OverdrawMath.Excluded, OverdrawMath.Excluded, 0, 4 }, 10);

            Assert.AreEqual(2f, s.avgPerPixel, 1e-4f, "뺀 픽셀은 분모에도 안 들어감");
            Assert.AreEqual(50f, s.coveredPercent, 1e-4f);
        }

        [Test]
        public void Decode_RawGrayscale_UsesRedChannel()
        {
            var px = new[] { new Color(0f, 0f, 0f), new Color(0.3f, 0.3f, 0.3f), new Color(1f, 1f, 1f) };

            int[] counts = OverdrawMath.Decode(px, 3, 10, out bool colorized);

            Assert.IsFalse(colorized);
            CollectionAssert.AreEqual(new[] { 0, 3, 10 }, counts);
        }

        [Test]
        public void Decode_Colorized_InvertsPaletteAndExcludesLegendRows()
        {
            const int width = 2;
            const int height = OverdrawMath.LegendRows + 1;
            var px = new Color[width * height];
            for (int i = 0; i < OverdrawMath.LegendRows * width; i++)
                px[i] = OverdrawMath.ToColor(7, 10);   // 범례 자리 — 무엇이 있어도 뺀다
            int top = OverdrawMath.LegendRows * width;
            px[top] = new Color(1f, 0.672f, 0f);       // 6000.3 실측 6회 색 (색표 값과 약간 다름)
            px[top + 1] = OverdrawMath.ToColor(2, 10);

            int[] counts = OverdrawMath.Decode(px, width, 10, out bool colorized);

            Assert.IsTrue(colorized);
            Assert.AreEqual(OverdrawMath.Excluded, counts[0]);
            Assert.AreEqual(OverdrawMath.Excluded, counts[top - 1]);
            Assert.AreEqual(6, counts[top]);
            Assert.AreEqual(2, counts[top + 1]);
        }

        [Test]
        public void ToColor_PaletteIsDistinctPerCount()
        {
            var seen = new System.Collections.Generic.HashSet<Color32>();
            for (int n = 0; n <= 10; n++)
                Assert.IsTrue(seen.Add(OverdrawMath.ToColor(n, 10)), $"{n}회 색이 다른 횟수와 겹침");
        }

        [Test]
        public void ToColor_HighCountIsReddish()
        {
            Color32 c = OverdrawMath.ToColor(8, 10);

            Assert.Greater(c.r, c.b, "높은 횟수는 빨강 쪽");
        }
    }
}
