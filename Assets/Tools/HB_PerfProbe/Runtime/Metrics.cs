namespace HB.PerfProbe
{
    /// <summary>한 프레임 지표. 얻지 못한 카운터는 -1.</summary>
    internal struct FrameSample
    {
        /// <summary>Time.realtimeSinceStartupAsDouble (초) — 오래 도는 세션에서도 ms 해상도를 지키려고 double.</summary>
        public double Time;
        public float FrameMs;
        public float MainThreadMs;
        public long GcAllocBytes;
        public int DrawCalls;
        public int SetPassCalls;
    }

    /// <summary>프레임마다 바뀌지 않는 메모리 지표. 얻지 못한 값은 -1.</summary>
    internal struct MemorySample
    {
        public long GcUsed;
        public long TotalUsed;
        public long TotalReserved;
        public long SystemUsed;
    }

    /// <summary>집계 구간(기본 1초) 하나의 요약. logcat 한 줄 · summaries.json 한 항목이 된다.</summary>
    internal struct IntervalSummary
    {
        public long Seq;
        /// <summary>구간 끝 Time.realtimeSinceStartupAsDouble (초)</summary>
        public double Time;
        public int Frames;
        public double Fps;
        public double FrameAvgMs;
        public double FrameMaxMs;
        public double MainAvgMs;
        public double MainMaxMs;
        /// <summary>구간 전체 GC 할당 합. 카운터가 없으면 -1.</summary>
        public long GcAllocBytes;
        public double DrawCallsAvg;
        public double SetPassAvg;
        public MemorySample Memory;
        /// <summary>이 구간에 들어온 mark 이름들 (쉼표로 이음). 없으면 null.</summary>
        public string Marks;

        /// <param name="token">logcat 줄에만 붙이는 명령 토큰 · 프로세스 id. 캡처 파일(summaries.json)에는 넣지 않는다.</param>
        public void WriteJson(ProbeJson w, string token = null, int pid = 0)
        {
            w.BeginObject()
                .Prop("seq", Seq)
                .Prop("t", Time)
                .Prop("frames", Frames)
                .Prop("fps", Fps)
                .Prop("frameAvg", FrameAvgMs)
                .Prop("frameMax", FrameMaxMs)
                .Prop("mainAvg", MainAvgMs)
                .Prop("mainMax", MainMaxMs)
                .Prop("gcAlloc", GcAllocBytes)
                .Prop("draw", DrawCallsAvg)
                .Prop("setPass", SetPassAvg)
                .Prop("gcUsed", Memory.GcUsed)
                .Prop("totalUsed", Memory.TotalUsed)
                .Prop("totalReserved", Memory.TotalReserved)
                .Prop("systemUsed", Memory.SystemUsed);
            if (Marks != null) w.Prop("marks", Marks);
            if (token != null) w.Prop("token", token).Prop("pid", pid);
            w.EndObject();
        }
    }

    /// <summary>
    /// 프레임 지표를 모아 구간 요약을 만든다. 할당 없이 합 · 최댓값만 쌓는다.
    /// 카운터 하나라도 -1인 프레임이 있으면 그 카운터는 구간 전체를 -1로 둔다 (일부 프레임만 더한 값은 오해를 부른다).
    /// </summary>
    internal sealed class IntervalAggregator
    {
        int _frames;
        double _frameSum, _frameMax, _mainSum, _mainMax;
        long _allocSum;
        long _drawSum, _setPassSum;
        bool _mainMissing, _allocMissing, _drawMissing, _setPassMissing;
        long _seq;

        public int Frames => _frames;

        public void Add(in FrameSample s)
        {
            _frames++;
            _frameSum += s.FrameMs;
            if (s.FrameMs > _frameMax) _frameMax = s.FrameMs;

            if (s.MainThreadMs < 0) _mainMissing = true;
            else
            {
                _mainSum += s.MainThreadMs;
                if (s.MainThreadMs > _mainMax) _mainMax = s.MainThreadMs;
            }

            if (s.GcAllocBytes < 0) _allocMissing = true;
            else _allocSum += s.GcAllocBytes;

            if (s.DrawCalls < 0) _drawMissing = true;
            else _drawSum += s.DrawCalls;

            if (s.SetPassCalls < 0) _setPassMissing = true;
            else _setPassSum += s.SetPassCalls;
        }

        /// <summary>쌓인 프레임으로 요약을 만들고 비운다. 프레임이 없으면 false.</summary>
        public bool Flush(double time, in MemorySample memory, string marks, out IntervalSummary summary)
        {
            summary = default;
            if (_frames == 0) return false;

            summary.Seq = ++_seq;
            summary.Time = time;
            summary.Frames = _frames;
            summary.FrameAvgMs = _frameSum / _frames;
            summary.Fps = _frameSum > 0 ? _frames * 1000.0 / _frameSum : 0;
            summary.FrameMaxMs = _frameMax;
            summary.MainAvgMs = _mainMissing ? -1 : _mainSum / _frames;
            summary.MainMaxMs = _mainMissing ? -1 : _mainMax;
            summary.GcAllocBytes = _allocMissing ? -1 : _allocSum;
            summary.DrawCallsAvg = _drawMissing ? -1 : (double)_drawSum / _frames;
            summary.SetPassAvg = _setPassMissing ? -1 : (double)_setPassSum / _frames;
            summary.Memory = memory;
            summary.Marks = marks;

            Reset();
            return true;
        }

        void Reset()
        {
            _frames = 0;
            _frameSum = _frameMax = _mainSum = _mainMax = 0;
            _allocSum = _drawSum = _setPassSum = 0;
            _mainMissing = _allocMissing = _drawMissing = _setPassMissing = false;
        }
    }
}
