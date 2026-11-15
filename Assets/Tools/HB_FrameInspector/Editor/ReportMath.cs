using System;
using System.Collections.Generic;
using System.Linq;

namespace HB.FrameInspector.Editor
{
    /// <summary>프레임마다 값을 모아 평균 · 최대 · 최소를 낸다.</summary>
    public sealed class Accumulator
    {
        double _sum;
        long _callSum;
        public int Count { get; private set; }
        public float Max { get; private set; } = float.MinValue;
        public float Min { get; private set; } = float.MaxValue;

        public void Add(float value, int calls = 0)
        {
            _sum += value;
            _callSum += calls;
            Count++;
            if (value > Max) Max = value;
            if (value < Min) Min = value;
        }

        public float Avg => Count > 0 ? (float)(_sum / Count) : 0f;
        public int CallsAvg => Count > 0 ? (int)Math.Round((double)_callSum / Count) : 0;

        public Stat ToStat()
        {
            if (Count == 0) return new Stat();
            return new Stat { available = true, avg = Avg, max = Max, min = Min };
        }

        public ValueStat ToValueStat(string name, string unit) =>
            new ValueStat { name = name, unit = unit, avg = Avg, max = Count > 0 ? Max : 0f, callsAvg = CallsAvg };
    }

    public static class ReportMath
    {
        // 게임 화면이 아니라 에디터 창을 그리는 샘플러 (IMGUI · UI Toolkit 렌더러, 7000.0.0a8에서 GPU 샘플이 잡힌 것)
        static readonly string[] EditorUiPrefixes = { "GUIView.", "GUITexture.", "UIR." };

        public static bool IsEditorUiMarker(string name) =>
            !string.IsNullOrEmpty(name) && EditorUiPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));

        /// <summary>
        /// Frame Debugger 이벤트 이름(샘플러 경로)에서 렌더 패스 이름을 뽑는다.
        /// 예: ".../ExecuteRenderGraph/(RP 1:0) DrawOpaqueObjects/RenderLoop.Draw" → "DrawOpaqueObjects".
        /// </summary>
        public static string PassNameFromEventPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(이름 없음)";
            string[] parts = path.Split('/');
            foreach (string part in parts)
            {
                if (part.StartsWith("(RP ", StringComparison.Ordinal))
                {
                    int close = part.IndexOf(')');
                    if (close >= 0 && close + 1 < part.Length)
                        return part.Substring(close + 1).Trim();
                }
            }

            int graph = Array.IndexOf(parts, "ExecuteRenderGraph");
            if (graph >= 0 && graph + 1 < parts.Length) return parts[graph + 1];
            return parts.Length > 1 ? parts[1] : parts[0];
        }

        public static List<CountEntry> CountBy(IEnumerable<string> keys, int limit = int.MaxValue) =>
            keys.GroupBy(k => k)
                .Select(g => new CountEntry { name = g.Key, count = g.Count() })
                .OrderByDescending(e => e.count)
                .ThenBy(e => e.name, StringComparer.Ordinal)
                .Take(limit)
                .ToList();

        /// <summary>
        /// 병목 쪽 힌트. 판정이 아니라 어디부터 볼지 고르는 근거 수치를 같이 남긴다.
        /// gpuFrameMs(FrameTime.GPU)에는 에디터 창 그리기 GPU가 들어가지 않는다
        /// (7000.0.0a8 실측: FrameTime.GPU 0.10ms, GUIView.DoPaint 2.4ms) — 그래서 빼지 않고 따로 알린다.
        /// </summary>
        public static BottleneckHint Bottleneck(TimingStats t, float editorUiGpuMs)
        {
            if (!t.cpuMainThreadMs.available)
                return new BottleneckHint { side = "unknown", reason = "CPU 메인 스레드 시간을 얻지 못함" };

            float main = t.cpuMainThreadMs.avg;
            float render = t.cpuRenderThreadMs.available ? t.cpuRenderThreadMs.avg : 0f;
            if (!t.gpuFrameMs.available)
                return new BottleneckHint { side = "unknown", reason = FormattableString.Invariant($"GPU 시간을 얻지 못함 (CPU main {main:F2}ms, render {render:F2}ms)") };

            // CPU 쪽은 메인 · 렌더 스레드 중 긴 쪽 (드로우콜이 많으면 렌더 스레드가 먼저 막힌다).
            bool renderThreadLonger = render > main;
            float cpu = renderThreadLonger ? render : main;
            float gpu = t.gpuFrameMs.avg;
            string numbers = FormattableString.Invariant($"CPU main {main:F2}ms, CPU render {render:F2}ms, 게임 GPU {gpu:F2}ms.");
            if (editorUiGpuMs > gpu)
                numbers += FormattableString.Invariant($" 에디터 창 그리기 GPU {editorUiGpuMs:F2}ms가 게임보다 큼 (게임 GPU 값에는 안 들어감) — 잴 때 Scene 뷰 등 다른 창을 닫으면 에디터 영향이 줄어든다.");

            if (gpu > cpu * 1.2f)
                return new BottleneckHint { side = "gpu", reason = "게임 GPU 시간이 CPU 스레드보다 김 — 패스별 GPU · 오버드로우 · 셰이더부터. " + numbers };
            if (cpu > gpu * 1.2f)
            {
                string where = renderThreadLonger
                    ? "CPU 렌더 스레드가 가장 김 — 드로우콜 · SetPass · 배칭부터. "
                    : "CPU 메인 스레드가 가장 김 — CPU 요약(스크립트 · 물리 · 애니메이션)부터. ";
                return new BottleneckHint { side = "cpu", reason = where + numbers };
            }
            return new BottleneckHint { side = "balanced", reason = "CPU와 GPU가 비슷함. " + numbers };
        }
    }
}
