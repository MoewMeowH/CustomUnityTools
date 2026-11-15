using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HB.FrameInspector.Editor
{
    [Serializable]
    public class ComparisonReport
    {
        public string baselineLabel;
        public string currentLabel;
        public List<string> conditionDiffs = new List<string>();
        public List<MetricDelta> metrics = new List<MetricDelta>();
    }

    /// <summary>허용치 검사에서 이 지표를 어떤 기준으로 볼지.</summary>
    public enum CheckGroup { None, Time, Counter, Overdraw }

    [Serializable]
    public class MetricDelta
    {
        public string group;
        public string name;
        public string unit;
        public CheckGroup check;
        public bool comparable;
        public string reason;
        public float baseline;
        public float current;
        public float delta;
        public float deltaPercent;
        // 모든 지표는 낮을수록 좋다: 1 나빠짐 · -1 좋아짐 · 0 차이 1% 미만
        public int direction;
    }

    /// <summary>CI 검사 허용치. 기본값은 사용자 결정 "제안값으로" (시간 +10% 그리고 +0.3ms, 카운터 · 메모리 +10%, 오버드로우 픽셀당 +0.2).</summary>
    [Serializable]
    public class FrameThresholds
    {
        public float timePercent = 10f;
        public float timeMinMs = 0.3f;
        public float counterPercent = 10f;
        public float overdrawPerPixel = 0.2f;
    }

    /// <summary>저장된 리포트 두 개를 지표별로 비교한다. 한쪽에 없거나 배치 모드라 측정되지 않은 지표는 비교 불가로 남긴다.</summary>
    public static class FrameReportComparer
    {
        const float SamePercent = 1f;

        // 이보다 작은 차이는 측정 잡음으로 보고 '같음' (0.000ms대 마커가 +200%로 상위에 오르는 것을 막음)
        static float NoiseFloor(string unit) => unit switch
        {
            "ms" => 0.01f,
            "bytes" => 1024f,
            "count" => 0.5f,
            "%" => 0.1f,
            _ => 0.01f,
        };

        public static ComparisonReport Compare(FrameReport baseline, FrameReport current, string baselineLabel = null, string currentLabel = null)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (current == null) throw new ArgumentNullException(nameof(current));

            var c = new ComparisonReport
            {
                baselineLabel = baselineLabel ?? baseline.meta.capturedAt,
                currentLabel = currentLabel ?? current.meta.capturedAt,
            };
            AddConditionDiffs(c.conditionDiffs, baseline.meta, current.meta, baseline.timing.gpuSource, current.timing.gpuSource);

            var m = c.metrics;
            TimingStats ta = baseline.timing, tb = current.timing;
            AddStat(m, "프레임 시간", "CPU 메인 스레드 평균", ta.cpuMainThreadMs, tb.cpuMainThreadMs, s => s.avg, CheckGroup.Time);
            AddStat(m, "프레임 시간", "CPU 메인 스레드 최대", ta.cpuMainThreadMs, tb.cpuMainThreadMs, s => s.max, CheckGroup.None);
            AddStat(m, "프레임 시간", "CPU 렌더 스레드 평균", ta.cpuRenderThreadMs, tb.cpuRenderThreadMs, s => s.avg, CheckGroup.Time);
            // GPU 값 출처(Recorder · FrameTimingManager)가 다르면 같은 장면도 값이 크게 달라 비교하지 않는다.
            string gpuReason = string.Equals(ta.gpuSource ?? "", tb.gpuSource ?? "", StringComparison.Ordinal) ? null : "GPU 값 출처가 다름";
            AddStat(m, "프레임 시간", "GPU 프레임 평균", ta.gpuFrameMs, tb.gpuFrameMs, s => s.avg, CheckGroup.Time, gpuReason);
            AddStat(m, "프레임 시간", "GPU 프레임 최대", ta.gpuFrameMs, tb.gpuFrameMs, s => s.max, CheckGroup.None, gpuReason);

            AddMatched(m, "카운터", baseline.counters, current.counters, CheckGroup.Counter);

            OverdrawReport oa = baseline.overdraw, ob = current.overdraw;
            string odReason = oa.available && ob.available ? null : "오버드로우가 한쪽에 없음";
            Add(m, "오버드로우", "픽셀당 평균", "회", CheckGroup.Overdraw, odReason, oa.all.avgPerPixel, ob.all.avgPerPixel);
            Add(m, "오버드로우", "덮인 픽셀당 평균", "회", CheckGroup.None, odReason, oa.all.avgPerCoveredPixel, ob.all.avgPerCoveredPixel);
            Add(m, "오버드로우", "5회 이상 픽셀", "%", CheckGroup.None, odReason, oa.all.atLeast5Percent, ob.all.atLeast5Percent);

            string drawReason = baseline.drawList.available && current.drawList.available ? null : "드로우 목록이 한쪽에 없음";
            Add(m, "드로우 목록", "드로우 이벤트 수", "count", CheckGroup.Counter, drawReason, baseline.drawList.eventCount, current.drawList.eventCount);

            AddRenderGraph(m, baseline.renderGraph, current.renderGraph);
            AddMatched(m, "렌더 패스 GPU", baseline.gpuPasses, current.gpuPasses, CheckGroup.None);
            AddMatched(m, "CPU 마커", baseline.cpuMarkers, current.cpuMarkers, CheckGroup.None);
            return c;
        }

        /// <summary>허용치를 넘은 지표를 사람이 읽을 문장으로 돌려준다. 비어 있으면 통과.</summary>
        public static List<string> Check(ComparisonReport comparison, FrameThresholds thresholds = null)
        {
            thresholds ??= new FrameThresholds();
            var violations = new List<string>();
            foreach (MetricDelta d in comparison.metrics)
            {
                if (!d.comparable || d.direction <= 0) continue;
                bool over = d.check switch
                {
                    CheckGroup.Time => d.deltaPercent > thresholds.timePercent && d.delta > thresholds.timeMinMs,
                    CheckGroup.Counter => d.deltaPercent > thresholds.counterPercent,
                    CheckGroup.Overdraw => d.delta > thresholds.overdrawPerPixel,
                    _ => false,
                };
                if (over)
                    violations.Add($"[{d.group}] {d.name}: {ReportMath.FormatCounter(d.baseline, d.unit, 3)} → {ReportMath.FormatCounter(d.current, d.unit, 3)} {UnitSuffix(d.unit)}(+{ReportMath.FormatCounter(d.delta, d.unit, 3)}, +{d.deltaPercent.ToString("0.#", CultureInfo.InvariantCulture)}%)");
            }
            return violations;
        }

        public static List<string> Check(FrameReport baseline, FrameReport current, FrameThresholds thresholds = null) =>
            Check(Compare(baseline, current), thresholds);

        // bytes는 FormatCounter가 이미 KB · MB를 붙인다
        static string UnitSuffix(string unit) => unit == "bytes" ? "" : unit + " ";

        static void AddConditionDiffs(List<string> diffs, ReportMeta a, ReportMeta b, string gpuSourceA, string gpuSourceB)
        {
            void Diff(string label, string x, string y)
            {
                if (!string.Equals(x ?? "", y ?? "", StringComparison.Ordinal))
                    diffs.Add($"{label}: {x} → {y}");
            }

            Diff("Unity", a.unityVersion, b.unityVersion);
            Diff("렌더 파이프라인", a.renderPipeline, b.renderPipeline);
            Diff("그래픽 API", a.graphicsApi, b.graphicsApi);
            Diff("GPU", a.gpuName, b.gpuName);
            Diff("씬", a.scene, b.scene);
            Diff("카메라", a.camera, b.camera);
            Diff("Game 뷰 해상도", $"{a.screenWidth}×{a.screenHeight}", $"{b.screenWidth}×{b.screenHeight}");
            Diff("배치 모드", a.batchMode ? "예" : "아니오", b.batchMode ? "예" : "아니오");
            Diff("GPU 값 출처", gpuSourceA, gpuSourceB);
            Diff("잰 프레임 수", a.sampledFrames.ToString(CultureInfo.InvariantCulture), b.sampledFrames.ToString(CultureInfo.InvariantCulture));
        }

        static void AddStat(List<MetricDelta> m, string group, string name, Stat a, Stat b, Func<Stat, float> pick, CheckGroup check, string extraReason = null)
        {
            string reason = !(a.available && b.available) ? "한쪽에서 측정 안 됨 (배치 모드 · API 미지원 등)" : extraReason;
            Add(m, group, name, "ms", check, reason, a.available ? pick(a) : 0f, b.available ? pick(b) : 0f);
        }

        static void AddMatched(List<MetricDelta> m, string group, List<ValueStat> a, List<ValueStat> b, CheckGroup check)
        {
            var byName = b.GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
            foreach (ValueStat sa in a)
            {
                if (byName.TryGetValue(sa.name, out ValueStat sb))
                    Add(m, group, sa.name, sa.unit, check, null, sa.avg, sb.avg);
                else
                    Add(m, group, sa.name, sa.unit, check, "이후 리포트에 없음", sa.avg, 0f);
            }
            var aNames = new HashSet<string>(a.Select(s => s.name));
            foreach (ValueStat sb in b.Where(s => !aNames.Contains(s.name)))
                Add(m, group, sb.name, sb.unit, check, "이전 리포트에 없음", 0f, sb.avg);
        }

        static void AddRenderGraph(List<MetricDelta> m, RenderGraphReport a, RenderGraphReport b)
        {
            if (!a.available || !b.available)
            {
                Add(m, "렌더 그래프", "합쳐지지 못한 패스", "count", CheckGroup.None, "렌더 그래프 지표가 한쪽에 없음", 0f, 0f);
                return;
            }

            string Key(RenderGraphExecution e) => $"{e.graphName}/{e.executionName}";
            var byKey = b.executions.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
            foreach (RenderGraphExecution ea in a.executions)
            {
                string key = Key(ea);
                byKey.TryGetValue(key, out RenderGraphExecution eb);
                string reason = eb == null ? "이후 리포트에 이 카메라 없음" : null;
                Add(m, "렌더 그래프", $"{key} 패스", "count", CheckGroup.None, reason, ea.totalPasses, eb?.totalPasses ?? 0);
                Add(m, "렌더 그래프", $"{key} 합쳐지지 못한 패스", "count", CheckGroup.Counter, reason, ea.unmergedPasses, eb?.unmergedPasses ?? 0);
            }

            // 새로 생긴 카메라(오프스크린 패스 등)도 회귀의 흔한 원인이라 이후에만 있는 것도 남긴다.
            var aKeys = new HashSet<string>(a.executions.Select(Key));
            foreach (RenderGraphExecution eb in b.executions.Where(e => !aKeys.Contains(Key(e))))
            {
                Add(m, "렌더 그래프", $"{Key(eb)} 패스", "count", CheckGroup.None, "이전 리포트에 이 카메라 없음", 0f, eb.totalPasses);
                Add(m, "렌더 그래프", $"{Key(eb)} 합쳐지지 못한 패스", "count", CheckGroup.Counter, "이전 리포트에 이 카메라 없음", 0f, eb.unmergedPasses);
            }
        }

        static void Add(List<MetricDelta> m, string group, string name, string unit, CheckGroup check, string reason, float baseline, float current)
        {
            var d = new MetricDelta
            {
                group = group,
                name = name,
                unit = unit,
                check = check,
                comparable = reason == null,
                reason = reason,
                baseline = baseline,
                current = current,
            };
            if (d.comparable)
            {
                d.delta = current - baseline;
                d.deltaPercent = baseline != 0f ? d.delta / Math.Abs(baseline) * 100f : (d.delta == 0f ? 0f : 100f);
                d.direction = Math.Abs(d.deltaPercent) < SamePercent || Math.Abs(d.delta) < NoiseFloor(unit) ? 0 : Math.Sign(d.delta);
            }
            m.Add(d);
        }
    }
}
