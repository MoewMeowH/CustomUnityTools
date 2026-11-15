using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// report.md(사람 · AI가 이 파일 하나만 받아도 읽히게 뜻 · 단위 · 한계를 같이 적음)와 report.json을 만든다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class ReportWriter
    {
        public const string MarkdownFile = "report.md";
        public const string JsonFile = "report.json";
        public const string OverdrawFile = "overdraw.png";
        public const string ScreenFile = "screen.png";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string ToJson(FrameReport report) => JsonUtility.ToJson(report, true);

        public static string ToMarkdown(FrameReport r, bool hasOverdrawImage, bool hasScreenImage)
        {
            var sb = new StringBuilder();
            ReportMeta m = r.meta;

            sb.AppendLine("# Unity 프레임 리포트");
            sb.AppendLine();
            sb.AppendLine($"> {m.tool} {m.toolVersion}가 Unity 에디터 Play 모드에서 {m.sampledFrames}프레임을 재서 만든 리포트다.");
            sb.AppendLine("> 시간 단위는 ms(밀리초), 평균 · 최대 · 최소는 잰 프레임 기준이다. 같은 데이터의 기계용 형식은 `report.json`.");
            sb.AppendLine("> 읽는 순서: ① 병목 힌트로 CPU · GPU 중 어디부터 볼지 고른다 ② 그쪽 절의 수치를 본다 ③ 맨 아래 '참고 · 한계'를 감안한다.");
            sb.AppendLine();

            sb.AppendLine("## 측정 조건");
            sb.AppendLine();
            Table(sb, new[] { "항목", "값" }, new List<string[]>
            {
                new[] { "시각", m.capturedAt },
                new[] { "Unity", m.unityVersion },
                new[] { "렌더 파이프라인", m.renderPipeline },
                new[] { "그래픽 API", m.graphicsApi },
                new[] { "GPU", m.gpuName },
                new[] { "CPU", m.cpuName },
                new[] { "플랫폼", m.platform },
                new[] { "씬", string.IsNullOrEmpty(m.scene) ? "(저장 안 된 씬)" : m.scene },
                new[] { "카메라", m.camera },
                new[] { "Game 뷰 해상도", $"{m.screenWidth}×{m.screenHeight}" },
                new[] { "잰 프레임 수", m.sampledFrames.ToString(Inv) },
                new[] { "목표 프레임 시간", m.targetFrameMs > 0 ? $"{F(m.targetFrameMs)} ms" : "지정 없음 (Application.targetFrameRate)" },
            });

            sb.AppendLine("## 병목 힌트");
            sb.AppendLine();
            sb.AppendLine($"- 쪽: **{r.bottleneck.side}** (cpu · gpu · balanced · unknown)");
            sb.AppendLine($"- 근거: {r.bottleneck.reason}");
            sb.AppendLine("- 판정이 아니라 어디부터 볼지 고르는 힌트다.");
            sb.AppendLine();

            sb.AppendLine("## 프레임 시간 (ms)");
            sb.AppendLine();
            Table(sb, new[] { "항목", "평균", "최대", "최소" }, new List<string[]>
            {
                StatRow("CPU 프레임 전체", r.timing.cpuFrameMs),
                StatRow("CPU 메인 스레드", r.timing.cpuMainThreadMs),
                StatRow("CPU 렌더 스레드", r.timing.cpuRenderThreadMs),
                StatRow("GPU 프레임", r.timing.gpuFrameMs),
            });
            sb.AppendLine($"GPU 값 출처: {r.timing.gpuSource}. CPU 값 출처: FrameTimingManager.");
            sb.AppendLine();

            sb.AppendLine("## 렌더링 카운터 (프레임당)");
            sb.AppendLine();
            if (r.counters.Count == 0) sb.AppendLine("(없음)").AppendLine();
            else Table(sb, new[] { "카운터", "평균", "최대", "단위" }, r.counters.Select(c => new[] { c.name, F(c.avg, 0), F(c.max, 0), c.unit }).ToList());

            sb.AppendLine("## CPU 요약 (주요 마커, ms)");
            sb.AppendLine();
            sb.AppendLine("Profiler 마커 몇 개만 고정으로 잰 최소 요약이다. 함수 단위 원인은 Profiler 녹화로 따로 본다.");
            sb.AppendLine();
            if (r.cpuMarkers.Count == 0) sb.AppendLine("(이번 프레임들에 샘플이 잡힌 마커 없음)").AppendLine();
            else Table(sb, new[] { "마커", "평균", "최대", "호출/프레임" }, r.cpuMarkers.Select(c => new[] { c.name, F(c.avg), F(c.max), c.callsAvg.ToString(Inv) }).ToList());

            sb.AppendLine("## 렌더 패스 GPU 시간 (ms, 큰 순)");
            sb.AppendLine();
            sb.AppendLine("Profiler 샘플러별 GPU 시간이다. **중첩된다** — 부모(예: `UniversalRenderPipeline.RenderSingleCameraInternal`, `ExecuteRenderGraph`)가 자식 패스(`DrawOpaqueObjects` 등)를 포함하므로 더하지 않는다.");
            sb.AppendLine();
            if (r.gpuPasses.Count == 0) sb.AppendLine("(없음 — 참고 · 한계 절 참고)").AppendLine();
            else Table(sb, new[] { "샘플러", "평균", "최대", "블록/프레임" }, r.gpuPasses.Select(c => new[] { c.name, F(c.avg, 4), F(c.max, 4), c.callsAvg.ToString(Inv) }).ToList());

            if (r.editorGpu.Count > 0)
            {
                sb.AppendLine("### 에디터 창 그리기 GPU 시간 (게임과 무관, 위 GPU 프레임 값에 안 들어감, ms)");
                sb.AppendLine();
                Table(sb, new[] { "샘플러", "평균", "최대" }, r.editorGpu.Select(c => new[] { c.name, F(c.avg, 4), F(c.max, 4) }).ToList());
            }

            WriteOverdraw(sb, r.overdraw, hasOverdrawImage, hasScreenImage);
            WriteDrawList(sb, r.drawList);
            WriteShaders(sb, r.shaders);

            sb.AppendLine("## 참고 · 한계");
            sb.AppendLine();
            foreach (string n in r.notes) sb.AppendLine($"- {n}");
            return sb.ToString();
        }

        static void WriteOverdraw(StringBuilder sb, OverdrawReport o, bool hasOverdrawImage, bool hasScreenImage)
        {
            sb.AppendLine("## 오버드로우");
            sb.AppendLine();
            if (!o.available)
            {
                sb.AppendLine($"(없음 — {o.unavailableReason})");
                sb.AppendLine();
                if (hasScreenImage) sb.AppendLine($"화면: ![screen]({ScreenFile})").AppendLine();
                return;
            }

            sb.AppendLine($"픽셀마다 몇 번 그려졌는지(URP Rendering Debugger Overdraw, {o.width}×{o.height}). {o.maxDisplayedCount}회 이상은 {o.maxDisplayedCount}회로 센다.");
            if (o.excludedBottomRows > 0)
                sb.AppendLine($"URP가 색 히트맵으로 내보내 색에서 횟수를 되돌렸고, 범례가 그려진 아래쪽 {o.excludedBottomRows}px 줄은 통계에서 뺐다 (그림에서는 회색).");
            sb.AppendLine("'덮인 픽셀'은 1번 이상 그려진 픽셀. 반투명이 겹친 곳 · 화면을 크게 덮는 파티클/UI가 흔한 원인이다.");
            sb.AppendLine();
            Table(sb, new[] { "대상", "픽셀당 평균", "덮인 픽셀당 평균", "덮인 픽셀 %", "3회 이상 %", "5회 이상 %", $"{o.maxDisplayedCount}회 이상 %" }, new List<string[]>
            {
                OverdrawRow("불투명 + 반투명", o.all),
            });
            sb.AppendLine("전체 히스토그램 (겹친 횟수: 픽셀 %): " + string.Join(", ", o.all.histogramPercent.Select((p, i) => $"{i}{(i == o.maxDisplayedCount ? "+" : "")}: {F(p, 1)}")));
            sb.AppendLine();
            if (hasOverdrawImage) sb.AppendLine($"오버드로우 색 그림 (검정 0 → 파랑 1 → 빨강 → 하양 {o.maxDisplayedCount}+): ![overdraw]({OverdrawFile})");
            if (hasScreenImage) sb.AppendLine($"같은 카메라 일반 화면: ![screen]({ScreenFile})");
            sb.AppendLine();
        }

        static void WriteDrawList(StringBuilder sb, DrawListReport d)
        {
            sb.AppendLine("## 드로우 목록 (Frame Debugger)");
            sb.AppendLine();
            if (!d.available)
            {
                sb.AppendLine($"(없음 — {d.unavailableReason})").AppendLine();
                return;
            }

            sb.AppendLine($"이벤트 {d.eventCount}개 (한 프레임). SRPBatch 이벤트 하나는 여러 오브젝트를 묶은 것이라 오브젝트가 비어 있다.");
            sb.AppendLine();
            Table(sb, new[] { "패스", "이벤트 수" }, d.byPass.Select(e => new[] { e.name, e.count.ToString(Inv) }).ToList());
            Table(sb, new[] { "종류", "이벤트 수" }, d.byType.Select(e => new[] { e.name, e.count.ToString(Inv) }).ToList());
            if (d.byObject.Count > 0)
                Table(sb, new[] { "오브젝트 (상위 30)", "이벤트 수" }, d.byObject.Select(e => new[] { e.name, e.count.ToString(Inv) }).ToList());
        }

        static void WriteShaders(StringBuilder sb, List<ShaderStat> shaders)
        {
            sb.AppendLine("## 셰이더 (이번 프레임에 그려진 머티리얼 기준)");
            sb.AppendLine();
            if (shaders.Count == 0)
            {
                sb.AppendLine("(없음)").AppendLine();
                return;
            }

            sb.AppendLine("화면에 보이는 Renderer 기준. 드로우 수는 Frame Debugger에서 오브젝트가 잡힌 드로우만 센 근사값이라, SRP Batcher로 묶인 드로우(SRPBatch)는 0으로 남는다. 패스 수는 활성 SubShader 기준. 명령어 · 사이클 비용은 이 리포트에 없다.");
            sb.AppendLine();
            Table(sb, new[] { "셰이더", "드로우", "Renderer", "머티리얼", "패스", "반투명", "RenderQueue", "오류", "키워드" },
                shaders.Select(s => new[]
                {
                    s.shader, s.drawCount.ToString(Inv), s.rendererCount.ToString(Inv), s.materialCount.ToString(Inv), s.passCount.ToString(Inv),
                    s.transparent ? "예" : "아니오",
                    s.minRenderQueue == s.maxRenderQueue ? s.minRenderQueue.ToString(Inv) : $"{s.minRenderQueue}~{s.maxRenderQueue}",
                    s.hasErrors ? "있음" : "-",
                    s.keywords.Count == 0 ? "-" : string.Join(" ", s.keywords),
                }).ToList());
        }

        static string[] StatRow(string name, Stat s) =>
            s.available ? new[] { name, F(s.avg), F(s.max), F(s.min) } : new[] { name, "없음", "-", "-" };

        static string[] OverdrawRow(string name, OverdrawStats s) => new[]
        {
            name, F(s.avgPerPixel), F(s.avgPerCoveredPixel), F(s.coveredPercent, 1), F(s.atLeast3Percent, 1), F(s.atLeast5Percent, 1), F(s.atMaxPercent, 1),
        };

        static string F(float v, int digits = 2) => v.ToString("F" + digits, Inv);

        static void Table(StringBuilder sb, string[] header, List<string[]> rows)
        {
            sb.AppendLine("| " + string.Join(" | ", header.Select(Cell)) + " |");
            sb.AppendLine("|" + string.Concat(header.Select(_ => "---|")));
            foreach (string[] row in rows)
                sb.AppendLine("| " + string.Join(" | ", row.Select(Cell)) + " |");
            sb.AppendLine();
        }

        static string Cell(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace("|", "\\|").Replace("\n", " ");
    }
}
