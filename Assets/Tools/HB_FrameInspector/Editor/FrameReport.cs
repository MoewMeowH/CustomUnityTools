using System;
using System.Collections.Generic;

namespace HB.FrameInspector.Editor
{
    // JsonUtility로 그대로 report.json이 되므로 필드 이름이 곧 JSON 키다 (단위는 키 이름에).

    [Serializable]
    public class FrameReport
    {
        public ReportMeta meta = new ReportMeta();
        public TimingStats timing = new TimingStats();
        public BottleneckHint bottleneck = new BottleneckHint();
        public List<ValueStat> counters = new List<ValueStat>();
        public List<ValueStat> cpuMarkers = new List<ValueStat>();
        public List<ValueStat> gpuPasses = new List<ValueStat>();
        public List<ValueStat> editorGpu = new List<ValueStat>();
        public OverdrawReport overdraw = new OverdrawReport();
        public DrawListReport drawList = new DrawListReport();
        public List<ShaderStat> shaders = new List<ShaderStat>();
        public RenderGraphReport renderGraph = new RenderGraphReport();
        public ProfilerReport profiler = new ProfilerReport();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    public class ProfilerReport
    {
        public bool recorded;
        public int frames;
        public string file;
        public string unavailableReason;
    }

    [Serializable]
    public class RenderGraphReport
    {
        public bool available;
        public string unavailableReason;
        public List<RenderGraphExecution> executions = new List<RenderGraphExecution>();
    }

    // 필드 이름은 URP core 17.7 RenderGraphDebugSessionManager 출력(GraphMetricsOutput 등)과 같게 둔다 — JsonUtility로 그대로 옮겨 읽는다.
    [Serializable]
    public class RenderGraphExecution
    {
        public string graphName;
        public string executionName;
        public int totalPasses;
        public int culledPasses;
        public int unmergedPasses;
        public int totalResources;
        public int textureCount;
        public int bufferCount;
        public int importedResourceCount;
        public List<string> insights = new List<string>();
        public List<RenderGraphUnmergedPass> unmerged = new List<RenderGraphUnmergedPass>();
        public List<string> culled = new List<string>();
    }

    [Serializable]
    public class RenderGraphUnmergedPass
    {
        public int index;
        public string name;
        public string breakReason;
        public string nextPassName;
    }

    [Serializable]
    public class ReportMeta
    {
        public string tool = "HB_FrameInspector";
        public string toolVersion = "1.0.0";
        public string capturedAt;
        public string unityVersion;
        public string renderPipeline;
        public string graphicsApi;
        public string gpuName;
        public string cpuName;
        public string platform;
        public string scene;
        public string camera;
        public int screenWidth;
        public int screenHeight;
        public int sampledFrames;
        public float targetFrameMs;
        // 배치 모드(-batchmode)는 게임 화면을 그리지 않아 GPU · 렌더 스레드 · 렌더링 카운터 · 드로우 목록이 측정되지 않는다 (6000.3.23f1 실측).
        public bool batchMode;
    }

    [Serializable]
    public class TimingStats
    {
        public Stat cpuFrameMs = new Stat();
        public Stat cpuMainThreadMs = new Stat();
        public Stat cpuRenderThreadMs = new Stat();
        public Stat gpuFrameMs = new Stat();
        public string gpuSource;
    }

    [Serializable]
    public class Stat
    {
        public bool available;
        public float avg;
        public float max;
        public float min;
    }

    [Serializable]
    public class ValueStat
    {
        public string name;
        public string unit;
        public float avg;
        public float max;
        public int callsAvg;
    }

    [Serializable]
    public class BottleneckHint
    {
        public string side;
        public string reason;
    }

    [Serializable]
    public class OverdrawReport
    {
        public bool available;
        public string unavailableReason;
        public int maxDisplayedCount;
        public int width;
        public int height;
        // "raw"(흑백 원값) 또는 "colorized"(색 히트맵에서 되돌림 — 아래쪽 범례 행은 통계에서 뺌)
        public string decodedFrom;
        public int excludedBottomRows;
        // URP Overdraw 모드 All 기준. Opaque · Transparent 모드는 세지 않는 쪽 물체도 1로 덮어써(Blend One Zero) 따로 내지 않는다.
        public OverdrawStats all = new OverdrawStats();
    }

    [Serializable]
    public class OverdrawStats
    {
        public float avgPerPixel;
        public float avgPerCoveredPixel;
        public float coveredPercent;
        public float atLeast3Percent;
        public float atLeast5Percent;
        public float atMaxPercent;
        // histogram[i] = 겹친 횟수가 i인 픽셀 비율(%), 마지막 칸은 최대 표시 횟수 이상
        public List<float> histogramPercent = new List<float>();
    }

    [Serializable]
    public class DrawListReport
    {
        public bool available;
        public string unavailableReason;
        public int eventCount;
        public List<CountEntry> byPass = new List<CountEntry>();
        public List<CountEntry> byType = new List<CountEntry>();
        public List<CountEntry> byObject = new List<CountEntry>();
    }

    [Serializable]
    public class CountEntry
    {
        public string name;
        public int count;
    }

    [Serializable]
    public class ShaderStat
    {
        public string shader;
        public int passCount;
        public int materialCount;
        public int rendererCount;
        public int drawCount;
        public bool transparent;
        public int minRenderQueue;
        public int maxRenderQueue;
        public bool hasErrors;
        public List<string> keywords = new List<string>();
    }
}
