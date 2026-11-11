using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.Memory;
using UnityEngine;
using UnityEngine.Rendering;

namespace HB.PerfProbe
{
    /// <summary>
    /// <see cref="PerfProbe"/>가 만드는 MonoBehaviour. 매 프레임 지표를 모아 1초마다 요약 한 줄을 logcat에 쓰고,
    /// 명령 소켓으로 받은 캡처 명령을 메인 스레드에서 처리한다.
    /// 명령은 <c>token=&lt;토큰&gt;</c>이 맞아야 처리한다. 토큰은 logcat 요약 줄에만 실린다 —
    /// Android에서 127.0.0.1은 같은 기기의 다른 앱도 붙을 수 있지만, 다른 앱은 남의 logcat을 읽지 못한다.
    /// </summary>
    internal sealed class PerfProbeRunner : MonoBehaviour
    {
        internal static PerfProbeRunner s_Instance;

        /// <summary>한 구간 marks 상한 — logcat 한 줄(약 4KB) 안에 요약이 들어가게.</summary>
        internal const int MaxMarksLength = 1024;
        /// <summary>snapshot 콜백을 기다리는 상한 (초). CommandServer 답 대기보다 짧게.</summary>
        const double SnapshotTimeoutSeconds = 240;

        PerfProbeOptions _options;
        CommandServer _server;
        CaptureStore _store;
        LogRing _logs;
        Ring<FrameSample> _frames;
        Ring<IntervalSummary> _summaries;
        readonly IntervalAggregator _aggregator = new IntervalAggregator();
        readonly StringBuilder _pendingMarks = new StringBuilder();
        readonly object _marksLock = new object();
        bool _marksTruncated;
        readonly StringBuilder _lineBuilder = new StringBuilder(512);
        Action<string, Action<string>> _handler;
        double _intervalStart;
        bool _busy;
        bool _shutdown;
        string _latestSummaryJson;
        string _token;
        int _pid;

        ProfilerRecorder _mainThread, _gcAlloc, _gcUsed, _totalUsed, _totalReserved, _systemUsed, _drawCalls, _setPass;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass _androidLog;
#endif

        internal int Port => _server.Port;
        internal string Root => _store.Root;
        internal string LatestSummaryJson => _latestSummaryJson;
        internal string Token => _token;

        /// <summary>실패하면 예외를 던진다. 호출부(PerfProbe.Start)가 GameObject를 지우고, OnDestroy가 여기까지 연 것을 정리한다.</summary>
        internal void Initialize(PerfProbeOptions options)
        {
            _options = options;
            // 소켓을 먼저 연다 — 포트가 쓰이고 있으면 다른 것을 시작하기 전에 실패한다.
            _server = new CommandServer(options.Port);
            _handler = HandleCommand;
            _token = NewToken();
            _pid = CurrentPid();
            string root = string.IsNullOrEmpty(options.CaptureRoot)
                ? Path.Combine(Application.persistentDataPath, "HBProbe")
                : options.CaptureRoot;
            _store = new CaptureStore(root, options.MaxCaptures);
            _frames = new Ring<FrameSample>(Math.Max(1, options.FrameCapacity));
            _summaries = new Ring<IntervalSummary>(Math.Max(1, options.SummaryCapacity));
            _logs = new LogRing(Math.Max(1, options.LogLines));
            _logs.Attach();

            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _gcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory", 1);
            _totalUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            _totalReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory", 1);
            _systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (options.WriteToLogcat) _androidLog = new AndroidJavaClass("android.util.Log");
#endif

            _intervalStart = Time.realtimeSinceStartupAsDouble;
            s_Instance = this;
            Debug.Log($"[HBProbe] Started. port={_server.Port} root={_store.Root}");
        }

        void OnDestroy() => Shutdown();

        /// <summary>소켓 · 수집을 정리한다. 여러 번 불러도 된다. 처리 중인 명령은 "probe stopped"로 답한다.</summary>
        internal void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            if (s_Instance == this) s_Instance = null;
            _server?.Dispose();
            _logs?.Detach();
            _mainThread.Dispose();
            _gcAlloc.Dispose();
            _gcUsed.Dispose();
            _totalUsed.Dispose();
            _totalReserved.Dispose();
            _systemUsed.Dispose();
            _drawCalls.Dispose();
            _setPass.Dispose();
#if UNITY_ANDROID && !UNITY_EDITOR
            _androidLog?.Dispose();
            _androidLog = null;
#endif
        }

        void Update()
        {
            if (_shutdown || _server == null) return;

            var sample = new FrameSample
            {
                Time = Time.realtimeSinceStartupAsDouble,
                FrameMs = Time.unscaledDeltaTime * 1000f,
                MainThreadMs = _mainThread.Valid ? _mainThread.LastValue / 1e6f : -1f,
                GcAllocBytes = _gcAlloc.Valid ? _gcAlloc.LastValue : -1,
                DrawCalls = _drawCalls.Valid ? (int)_drawCalls.LastValue : -1,
                SetPassCalls = _setPass.Valid ? (int)_setPass.LastValue : -1,
            };
            _frames.Add(sample);
            _aggregator.Add(sample);

            if (sample.Time - _intervalStart >= _options.IntervalSeconds)
            {
                _intervalStart = sample.Time;
                string marks = TakeMarks();
                if (_aggregator.Flush(sample.Time, ReadMemory(), marks, out IntervalSummary summary))
                    Emit(summary);
            }

            _server.Pump(_handler);
        }

        MemorySample ReadMemory()
        {
            return new MemorySample
            {
                GcUsed = _gcUsed.Valid ? _gcUsed.LastValue : -1,
                TotalUsed = _totalUsed.Valid ? _totalUsed.LastValue : -1,
                TotalReserved = _totalReserved.Valid ? _totalReserved.LastValue : -1,
                SystemUsed = _systemUsed.Valid ? _systemUsed.LastValue : -1,
            };
        }

        void Emit(in IntervalSummary summary)
        {
            _summaries.Add(summary);
            _lineBuilder.Clear();
            summary.WriteJson(new ProbeJson(_lineBuilder));
            _latestSummaryJson = _lineBuilder.ToString();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_androidLog != null)
            {
                _lineBuilder.Clear();
                summary.WriteJson(new ProbeJson(_lineBuilder), _token, _pid);
                _androidLog.CallStatic<int>("i", PerfProbe.LogcatTag, _lineBuilder.ToString());
            }
#endif
        }

        internal void AddMark(string name)
        {
            lock (_marksLock)
            {
                if (_marksTruncated) return;
                if (_pendingMarks.Length + name.Length + 1 > MaxMarksLength)
                {
                    _marksTruncated = true;
                    return;
                }
                if (_pendingMarks.Length > 0) _pendingMarks.Append(',');
                _pendingMarks.Append(name);
            }
        }

        string TakeMarks()
        {
            lock (_marksLock)
            {
                if (_pendingMarks.Length == 0 && !_marksTruncated) return null;
                if (_marksTruncated) _pendingMarks.Append(_pendingMarks.Length > 0 ? ",..." : "...");
                string marks = _pendingMarks.ToString();
                _pendingMarks.Clear();
                _marksTruncated = false;
                return marks;
            }
        }

        static string NewToken()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(32);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static int CurrentPid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var process = new AndroidJavaClass("android.os.Process")) return process.CallStatic<int>("myPid");
#else
            return System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
        }

        /// <summary>길이가 다르면 바로 false, 같으면 끝까지 비교한다 (응답 시간으로 토큰을 한 글자씩 맞히지 못하게).</summary>
        static bool TokenEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        #region Commands

        internal void HandleCommand(string line, Action<string> reply)
        {
            if (!ProbeCommand.TryParse(line, out ProbeCommand cmd, out string error))
            {
                reply(ProbeJson.Error(null, error));
                return;
            }
            if (!TokenEquals(cmd.Get("token"), _token))
            {
                reply(ProbeJson.Error(cmd.Name, "unauthorized: token missing or wrong (read it from logcat tag HBProbe)"));
                return;
            }

            switch (cmd.Name)
            {
                case "status":
                    reply(Status());
                    return;
                case "mark":
                    string markName = cmd.Get("name");
                    if (markName == null)
                    {
                        reply(ProbeJson.Error(cmd.Name, "usage: mark name=<name>"));
                        return;
                    }
                    AddMark(markName);
                    reply(new ProbeJson().BeginObject().Prop("ok", true).Prop("cmd", cmd.Name).Prop("name", markName).EndObject().ToString());
                    return;
                case "drop":
                    string dropName = cmd.Get("name");
                    if (dropName == null)
                    {
                        reply(ProbeJson.Error(cmd.Name, "usage: drop name=<capture folder>"));
                        return;
                    }
                    reply(_store.Drop(dropName, out string dropError)
                        ? new ProbeJson().BeginObject().Prop("ok", true).Prop("cmd", cmd.Name).Prop("name", dropName).EndObject().ToString()
                        : ProbeJson.Error(cmd.Name, dropError));
                    return;
                case "capture":
                case "census":
                case "snapshot":
                    if (_busy)
                    {
                        reply(ProbeJson.Error(cmd.Name, "busy: another capture in progress"));
                        return;
                    }
                    string reason = cmd.Get("reason", cmd.Name == "capture" ? "manual" : cmd.Name);
                    if (reason.Length > ProbeCommand.MaxReasonLength)
                    {
                        reply(ProbeJson.Error(cmd.Name, $"reason longer than {ProbeCommand.MaxReasonLength}"));
                        return;
                    }
                    _busy = true;
                    StartCoroutine(Capture(cmd.Name, reason, reply));
                    return;
                default:
                    reply(ProbeJson.Error(cmd.Name, "unhandled command"));
                    return;
            }
        }

        string Status()
        {
            var w = new ProbeJson();
            w.BeginObject()
                .Prop("ok", true)
                .Prop("cmd", "status")
                .Prop("app", Application.identifier)
                .Prop("version", Application.version)
                .Prop("unity", Application.unityVersion)
                .Prop("device", SystemInfo.deviceModel)
                .Prop("pid", _pid)
                .Prop("port", _server.Port)
                .Prop("root", _store.Root)
                .Prop("busy", _busy)
                .Prop("time", Time.realtimeSinceStartupAsDouble);
            w.Key("latest");
            if (_latestSummaryJson == null) w.Value((string)null);
            else
            {
                // 이미 JSON인 요약을 그대로 끼운다.
                var sb = new StringBuilder(w.ToString().Length + _latestSummaryJson.Length + 2);
                sb.Append(w.ToString()).Append(_latestSummaryJson).Append('}');
                return sb.ToString();
            }
            w.EndObject();
            return w.ToString();
        }

        IEnumerator Capture(string kind, string reason, Action<string> reply)
        {
            string name = null, dir = null;
            var files = new List<string>();
            string error = null;

            try
            {
                (name, dir) = _store.Create(reason, DateTime.Now);
                // census를 먼저 찍는다 — 뒤에 만드는 스크린샷 텍스처가 집계에 섞이지 않게.
                if (kind == "census")
                {
                    File.WriteAllText(Path.Combine(dir, "census.json"), Census.Take().ToJson(NowText()), Utf8);
                    files.Add("census.json");
                }
                WriteCommon(dir, kind, reason, files);
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }

            if (error == null && !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                // 배치 모드 · 그래픽 장치 없음에서는 WaitForEndOfFrame이 돌아오지 않으므로 스크린샷을 건너뛴다.
                yield return new WaitForEndOfFrame();
                string shotError = TryScreenshot(Path.Combine(dir, "screenshot.png"));
                if (shotError == null) files.Add("screenshot.png");
                else files.Add("screenshot: " + shotError);
            }

            if (error == null && kind == "snapshot")
            {
                bool done = false, ok = false;
                string snapPath = Path.Combine(dir, "memory.snap");
                try
                {
                    MemoryProfiler.TakeSnapshot(snapPath, (path, success) => { ok = success; done = true; });
                }
                catch (Exception e)
                {
                    error = "snapshot: " + e.GetType().Name + ": " + e.Message;
                    done = true;
                }
                double until = Time.realtimeSinceStartupAsDouble + SnapshotTimeoutSeconds;
                while (!done && Time.realtimeSinceStartupAsDouble < until) yield return null;
                if (error == null)
                {
                    if (!done) error = $"snapshot did not finish in {SnapshotTimeoutSeconds}s";
                    else if (ok) files.Add("memory.snap");
                    else error = "snapshot failed";
                }
            }

            _busy = false;
            if (error != null)
            {
                reply(ProbeJson.Error(kind, error));
                yield break;
            }

            var w = new ProbeJson();
            w.BeginObject().Prop("ok", true).Prop("cmd", kind).Prop("reason", reason).Prop("name", name).Prop("dir", dir).Key("files").BeginArray();
            foreach (string f in files) w.Value(f);
            w.EndArray().EndObject();
            reply(w.ToString());
        }

        void WriteCommon(string dir, string kind, string reason, List<string> files)
        {
            var meta = new ProbeJson();
            meta.BeginObject()
                .Prop("kind", kind)
                .Prop("reason", reason)
                .Prop("time", NowText())
                .Prop("realtime", Time.realtimeSinceStartupAsDouble)
                .Prop("app", Application.identifier)
                .Prop("version", Application.version)
                .Prop("unity", Application.unityVersion)
                .Prop("device", SystemInfo.deviceModel)
                .Prop("os", SystemInfo.operatingSystem)
                .Prop("gpu", SystemInfo.graphicsDeviceName)
                .Prop("systemMemoryMB", SystemInfo.systemMemorySize)
                .Prop("screen", Screen.width + "x" + Screen.height)
                .Prop("targetFrameRate", Application.targetFrameRate)
                .Prop("qualityLevel", QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "")
                .EndObject();
            File.WriteAllText(Path.Combine(dir, "meta.json"), meta.ToString(), Utf8);
            files.Add("meta.json");

            File.WriteAllText(Path.Combine(dir, "frames.json"), FramesJson(), Utf8);
            files.Add("frames.json");

            File.WriteAllText(Path.Combine(dir, "summaries.json"), SummariesJson(), Utf8);
            files.Add("summaries.json");

            _logs.WriteTo(Path.Combine(dir, "logs.txt"));
            files.Add("logs.txt");
        }

        /// <summary>최근 RingSeconds 구간 프레임. 열 이름을 한 번만 쓰고 프레임은 배열로 줄인다.</summary>
        internal string FramesJson()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double from = now - _options.RingSeconds;
            var w = new ProbeJson(new StringBuilder(64 * _frames.Count + 128));
            w.BeginObject()
                .Prop("ringSeconds", _options.RingSeconds)
                .Prop("now", now)
                .Key("columns").BeginArray().Value("t").Value("frameMs").Value("mainMs").Value("gcAlloc").Value("draw").Value("setPass").EndArray()
                .Key("frames").BeginArray();
            for (int i = 0; i < _frames.Count; i++)
            {
                FrameSample s = _frames[i];
                if (s.Time < from) continue;
                w.BeginArray().Value(s.Time).Value(s.FrameMs).Value(s.MainThreadMs).Value(s.GcAllocBytes).Value(s.DrawCalls).Value(s.SetPassCalls).EndArray();
            }
            w.EndArray().EndObject();
            return w.ToString();
        }

        internal string SummariesJson()
        {
            var w = new ProbeJson(new StringBuilder(320 * _summaries.Count + 64));
            w.BeginObject().Prop("intervalSeconds", _options.IntervalSeconds).Key("summaries").BeginArray();
            for (int i = 0; i < _summaries.Count; i++) _summaries[i].WriteJson(w);
            w.EndArray().EndObject();
            return w.ToString();
        }

        static string TryScreenshot(string path)
        {
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return null;
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
        }

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static string NowText() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

        #endregion
    }
}
