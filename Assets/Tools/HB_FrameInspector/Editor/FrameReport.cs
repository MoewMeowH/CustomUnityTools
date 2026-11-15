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
        public List<string> notes = new List<string>();
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
