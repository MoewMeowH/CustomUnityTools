using System;
using UnityEngine;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// URP 오버드로우 디버그 출력을 횟수 · 통계 · 색으로 바꾼다.
    /// 카메라 RT에는 두 가지 형태가 온다 — 그 패스가 URP의 최종 패스로 잡히는지(프로젝트 · 카메라 설정)에 따라 다르다:
    ///  · 원값: R = 겹친 횟수 / 최대 표시 횟수, 흑백 (7000.0.0a8 ForAI 실측)
    ///  · 색 히트맵 + 아래쪽 범례 (6000.3.23f1 ForAI, HDR PC_RPAsset 실측)
    /// </summary>
    public static class OverdrawMath
    {
        /// <summary>범례가 차지하는 아래쪽 픽셀 수. SRP Core Debug.hlsl DrawOverdrawLegend: 띠 테두리 y 16~44, 숫자 y 50~66.</summary>
        public const int LegendRows = 70;

        /// <summary>통계에서 뺀 픽셀(범례 자리)을 나타내는 값.</summary>
        public const int Excluded = -1;

        const float GrayTolerance = 0.02f;

        public static int ToCount(float red, int maxCount)
        {
            if (maxCount <= 0) return 0;
            int count = Mathf.RoundToInt(red * maxCount);
            return Mathf.Clamp(count, 0, maxCount);
        }

        /// <summary>색이 있는 픽셀이 하나라도 있으면 색 히트맵 출력이다 (원값 출력은 흑백, 색 출력의 범례 띠는 항상 색이 있다).</summary>
        public static bool LooksColorized(Color[] px)
        {
            foreach (Color c in px)
                if (Mathf.Abs(c.r - c.g) > GrayTolerance || Mathf.Abs(c.g - c.b) > GrayTolerance)
                    return true;
            return false;
        }

        /// <summary>
        /// 픽셀 배열(아래 행부터, Texture2D.GetPixels 순서)을 겹친 횟수로 바꾼다.
        /// 색 출력이면 색표에서 가장 가까운 색으로 되돌리고 아래쪽 범례 행은 <see cref="Excluded"/>로 둔다.
        /// </summary>
        public static int[] Decode(Color[] px, int width, int maxCount, out bool colorized)
        {
            colorized = LooksColorized(px);
            var counts = new int[px.Length];
            if (!colorized)
            {
                for (int i = 0; i < px.Length; i++)
                    counts[i] = ToCount(px[i].r, maxCount);
                return counts;
            }

            var palette = new Color[maxCount + 1];
            for (int n = 0; n <= maxCount; n++)
                palette[n] = ToColor(n, maxCount);

            int legendEnd = LegendRows * width;
            for (int i = 0; i < px.Length; i++)
                counts[i] = i < legendEnd ? Excluded : Nearest(px[i], palette);
            return counts;
        }

        static int Nearest(Color c, Color[] palette)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int n = 0; n < palette.Length; n++)
            {
                float dr = c.r - palette[n].r, dg = c.g - palette[n].g, db = c.b - palette[n].b;
                float d = dr * dr + dg * dg + db * db;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = n;
                }
            }
            return best;
        }

        /// <summary><see cref="Excluded"/> 픽셀은 빼고 센다.</summary>
        public static OverdrawStats Compute(int[] counts, int maxCount)
        {
            var stats = new OverdrawStats();
            if (counts == null || maxCount <= 0)
                return stats;

            var histogram = new int[maxCount + 1];
            long sum = 0;
            int included = 0, covered = 0, atLeast3 = 0, atLeast5 = 0;
            foreach (int raw in counts)
            {
                if (raw == Excluded) continue;
                int c = Mathf.Clamp(raw, 0, maxCount);
                included++;
                histogram[c]++;
                sum += c;
                if (c > 0) covered++;
                if (c >= 3) atLeast3++;
                if (c >= 5) atLeast5++;
            }

            if (included == 0)
                return stats;

            float total = included;
            stats.avgPerPixel = sum / total;
            stats.avgPerCoveredPixel = covered > 0 ? (float)sum / covered : 0f;
            stats.coveredPercent = covered * 100f / total;
            stats.atLeast3Percent = atLeast3 * 100f / total;
            stats.atLeast5Percent = atLeast5 * 100f / total;
            stats.atMaxPercent = histogram[maxCount] * 100f / total;
            for (int i = 0; i <= maxCount; i++)
                stats.histogramPercent.Add(histogram[i] * 100f / total);
            return stats;
        }

        /// <summary>SRP Core <c>Debug.hlsl GetOverdrawColor</c>와 같은 색표 (파랑 → 빨강 → 하양, 0은 검정).</summary>
        public static Color32 ToColor(int count, int maxCount)
        {
            if (count == Excluded)
                return new Color32(48, 48, 48, 255);
            if (count <= 0 || maxCount <= 0)
                return new Color32(0, 0, 0, 255);

            const float initialHue = 240f;
            const float deltaHue = 20f;
            const float xLight = 0.95f;
            const float minHue = deltaHue - 360f + initialHue;
            float xCostOne = 1f / maxCount;
            float x = Mathf.Clamp01((float)count / maxCount);

            float hueDeg = Math.Max(Math.Min((x - xCostOne) * (deltaHue - 360f) * (1f / (xLight - xCostOne)) + initialHue, initialHue), minHue);
            float hue = (hueDeg % 360f) / 360f;
            float saturation = Mathf.Clamp01((-1f / (1f - xLight)) * (x - xLight));
            return HsvToRgb(hue, saturation, 1f);
        }

        // SRP Core Color.hlsl HsvToRgb (음수 hue는 frac으로 감싼다)
        static Color32 HsvToRgb(float h, float s, float v)
        {
            float Channel(float k)
            {
                float t = h + k;
                float p = Mathf.Abs((t - Mathf.Floor(t)) * 6f - 3f);
                return v * Mathf.Lerp(1f, Mathf.Clamp01(p - 1f), s);
            }

            return new Color(Channel(1f), Channel(2f / 3f), Channel(1f / 3f), 1f);
        }
    }
}
