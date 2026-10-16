using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using HB.E2String.Benchmarks.SizeProbe;
using Unity.Profiling;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace HB.E2String.Benchmarks
{
    /// <summary>
    /// Enum 기본 API와 E2String 생성 API를 Unity 안에서 비교합니다.
    /// - 한 프레임에 한 케이스만 실행해 "GC Allocated In Frame"이 그 케이스의 할당만 담도록 하고, 빈 프레임(Baseline) 값을 뺍니다.
    /// - 시간은 Stopwatch로 잽니다 (Release 플레이어에서도 동작). GC 카운터는 Development 플레이어 · 에디터에서만 읽힙니다.
    /// - Profiler 창에서는 "E2S.*" 마커로 케이스를 구분할 수 있습니다.
    /// 명령줄: -e2sOut &lt;결과.csv&gt;, -e2sProfileLog &lt;프로파일.raw&gt; (Development 플레이어에서 Profiler 데이터를 파일로 기록)
    /// </summary>
    // 정적 필드는 결과 sink·읽기 전용 입력·직접 해제하는 이벤트뿐이라 Play 모드마다 초기화할 필요가 없다
    [NoAutoStaticsCleanup]
    public sealed class E2StringProfilerBenchmark : MonoBehaviour
    {
        private const int Rounds = 100; // 케이스당 프레임마다 100 라운드 × 100 입력 = 10,000 호출
        private const int ValueCount = 100;
        private const int CallsPerFrame = Rounds * ValueCount;
        private const string OutArg = "-e2sOut";
        private const string ProfileLogArg = "-e2sProfileLog";

        // CoreCLR은 단계별 JIT(Tiered compilation)가 끝나야 수치가 안정되므로 시간 기준으로 충분히 예열한다
        [SerializeField] private float m_WarmupSeconds = 5f;
        [SerializeField] private int m_MinWarmupCycles = 20;
        [SerializeField] private int m_MeasureCycles = 60;
        [SerializeField] private bool m_QuitWhenDone = true;

        private sealed class Case
        {
            public string Name;
            public Action Body;
            public ProfilerMarker Marker;
            // 측정 중 리스트가 커지며 할당하면 그 프레임 케이스의 GC 값에 섞이므로 용량을 미리 잡는다 (AddCase)
            public List<double> TimeNs;
            public List<long> GcBytes;
            public List<long> GcCount;
            public double LastTimeNs;
        }

        /// <summary>
        /// [Flags] 조합 문자열은 조합마다 첫 호출에서 할당 후 캐시된다. 반복 측정에는 이 비용이 안 보이므로 측정 시작 전에 한 번씩 따로 잰다.
        /// 아직 캐시되지 않은 조합 255개(1~255)를 처음 호출하는 비용이며, 타입 초기화 · JIT 등 첫 호출 비용이 포함된다.
        /// 각각 별도 프레임에서 실행해 GC 카운터가 그 케이스의 할당만 담게 한다.
        /// </summary>
        private sealed class OneShot
        {
            public string Name;
            public int Calls;
            public Action Body;
            public double TimeNs;
            public long GcBytes = -1;
        }

        private readonly OneShot[] m_OneShots =
        {
            new OneShot { Name = "Before.FlagsToStringFirstCall", Calls = 255, Body = () => { for (int v = 1; v < 256; v++) s_StringSink = ((BenchFlags8)v).ToString(); } },
            new OneShot { Name = "After.FlagsToStringFastFirstCall", Calls = 255, Body = () => { for (int v = 1; v < 256; v++) s_StringSink = ((BenchFlags8)v).ToStringFast(); } },
        };
        private int m_OneShotStage;

        private static readonly BenchEnum100[] s_Values = (BenchEnum100[])Enum.GetValues(typeof(BenchEnum100));
        private static readonly string[] s_Names = Enum.GetNames(typeof(BenchEnum100));
        private static readonly string[] s_MissNames = new string[ValueCount];
        private static readonly BenchEnum100[] s_ShuffledValues = new BenchEnum100[ValueCount];
        private static readonly BenchSparse100[] s_SparseValues = (BenchSparse100[])Enum.GetValues(typeof(BenchSparse100));
        private static readonly BenchEnum8[] s_SmallValues = new BenchEnum8[ValueCount];
        private static readonly string[] s_SmallNames = new string[ValueCount];
        private static readonly BenchFlags8[] s_Combos = new BenchFlags8[ValueCount];

        // 호출 결과를 정적 필드에 저장해 컴파일러가 호출을 제거하지 못하게 한다
        private static string s_StringSink;
        private static BenchEnum100 s_EnumSink;
        private static BenchEnum8 s_SmallSink;
        private static bool s_BoolSink;

        /// <summary>측정이 끝나면 결과 파일 경로와 함께 호출된다. 에디터 실행 시 Play 모드 종료에 쓴다.</summary>
        public static event Action<string> Completed;

        public static E2StringProfilerBenchmark Spawn()
        {
            return new GameObject(nameof(E2StringProfilerBenchmark)).AddComponent<E2StringProfilerBenchmark>();
        }

        private readonly List<Case> m_Cases = new List<Case>();
        private ProfilerRecorder m_GcBytesRecorder;
        private ProfilerRecorder m_GcCountRecorder;
        private readonly Stopwatch m_Stopwatch = new Stopwatch();
        private float m_StartTime;
        private int m_Frame;
        private int m_Cycle;
        private int m_WarmupEndCycle = -1;
        private int m_PrevCaseIndex = -1;
        private bool m_PrevMeasured;

        private void Awake()
        {
            BuildInputs();
            E2StringSizeProbe.Touch();

            AddCase("Baseline", () => { });

            // 100 멤버, 연속 값
            AddCase("Before.ToString", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_Values[i].ToString(); });
            AddCase("After.ToStringFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_Values[i].ToStringFast(); });
            AddCase("Before.ToStringShuffled", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_ShuffledValues[i].ToString(); });
            AddCase("After.ToStringFastShuffled", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_ShuffledValues[i].ToStringFast(); });
            AddCase("Before.Interpolation", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = $"S:{s_Values[i]}"; });
            AddCase("After.Interpolation", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = $"S:{s_Values[i].ToStringFast()}"; });

            // 띄엄띄엄한 값, 작은 enum
            AddCase("Before.SparseToString", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_SparseValues[i].ToString(); });
            AddCase("After.SparseToStringFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_SparseValues[i].ToStringFast(); });
            AddCase("Before.SmallToString", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_SmallValues[i].ToString(); });
            AddCase("After.SmallToStringFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_SmallValues[i].ToStringFast(); });

            // [Flags]
            AddCase("Before.FlagsToString", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_Combos[i].ToString(); });
            AddCase("After.FlagsToStringFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_StringSink = s_Combos[i].ToStringFast(); });

            // String -> Enum
            AddCase("Before.EnumTryParse", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) Enum.TryParse(s_Names[i], out s_EnumSink); });
            AddCase("After.ToEnumFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_EnumSink = s_Names[i].ToBenchEnum100Fast(); });
            AddCase("Before.EnumTryParseMiss", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_BoolSink = Enum.TryParse(s_MissNames[i], out s_EnumSink); });
            AddCase("After.ToEnumFastMiss", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_EnumSink = s_MissNames[i].ToBenchEnum100Fast(); });
            AddCase("Before.SmallEnumTryParse", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) Enum.TryParse(s_SmallNames[i], out s_SmallSink); });
            AddCase("After.SmallToEnumFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_SmallSink = s_SmallNames[i].ToBenchEnum8Fast(); });

            // 검사 · 순회
            AddCase("Before.EnumIsDefined", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_BoolSink = Enum.IsDefined(typeof(BenchEnum100), s_Values[i]); });
            AddCase("After.IsDefinedFast", () => { for (int r = 0; r < Rounds; r++) for (int i = 0; i < ValueCount; i++) s_BoolSink = BenchEnum100Extensions.IsDefinedFast(s_Values[i]); });
            AddCase("Before.GetValuesForeach", () => { for (int r = 0; r < Rounds; r++) foreach (BenchEnum100 v in Enum.GetValues(typeof(BenchEnum100))) s_EnumSink = v; });
            AddCase("After.ValuesFor", () => { for (int r = 0; r < Rounds; r++) { var values = BenchEnum100Extensions.Values; for (int i = 0; i < values.Length; i++) s_EnumSink = values[i]; } });

            m_GcBytesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_GcCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            StartProfileLogIfRequested();
            m_StartTime = Time.realtimeSinceStartup;
        }

        private static void BuildInputs()
        {
            var random = new System.Random(1234);
            for (int i = 0; i < ValueCount; i++)
            {
                s_ShuffledValues[i] = s_Values[random.Next(ValueCount)];
                s_MissNames[i] = "Missing" + i.ToString("D3");
                s_SmallValues[i] = (BenchEnum8)(i % 8);
                s_SmallNames[i] = s_SmallValues[i].ToString();
                s_Combos[i] = (BenchFlags8)(i * 2 + 3);
            }
        }

        private void AddCase(string name, Action body)
        {
            int capacity = m_MeasureCycles + 2;
            m_Cases.Add(new Case
            {
                Name = name,
                Body = body,
                Marker = new ProfilerMarker(ProfilerCategory.Scripts, "E2S." + name),
                TimeNs = new List<double>(capacity),
                GcBytes = new List<long>(capacity),
                GcCount = new List<long>(capacity),
            });
        }

        /// <summary>측정 시작 전 프레임마다 OneShot을 하나씩 실행하고, 다음 프레임에 그 프레임의 GC 값을 기록한다. 끝났으면 false.</summary>
        private bool RunOneShotStage()
        {
            if (m_OneShotStage > m_OneShots.Length) return false;

            if (m_OneShotStage > 0 && m_GcBytesRecorder.Valid)
                m_OneShots[m_OneShotStage - 1].GcBytes = m_GcBytesRecorder.LastValue;

            if (m_OneShotStage < m_OneShots.Length)
            {
                var shot = m_OneShots[m_OneShotStage];
                m_Stopwatch.Restart();
                shot.Body();
                m_Stopwatch.Stop();
                shot.TimeNs = m_Stopwatch.Elapsed.TotalMilliseconds * 1e6;
            }

            m_OneShotStage++;
            return true;
        }

        private void Update()
        {
            if (RunOneShotStage()) return;

            // 이전 프레임에 실행한 케이스의 GC 기록을 수집한다 (Recorder의 LastValue는 직전 프레임 값)
            if (m_PrevCaseIndex >= 0 && m_PrevMeasured)
            {
                var prev = m_Cases[m_PrevCaseIndex];
                prev.TimeNs.Add(prev.LastTimeNs);
                if (m_GcBytesRecorder.Valid) prev.GcBytes.Add(m_GcBytesRecorder.LastValue);
                if (m_GcCountRecorder.Valid) prev.GcCount.Add(m_GcCountRecorder.LastValue);
            }

            int index = m_Frame % m_Cases.Count;
            if (index == 0)
            {
                if (m_Frame > 0) m_Cycle++;
                if (m_WarmupEndCycle < 0 && m_Cycle >= m_MinWarmupCycles && Time.realtimeSinceStartup - m_StartTime >= m_WarmupSeconds)
                    m_WarmupEndCycle = m_Cycle;
                if (m_WarmupEndCycle >= 0 && m_Cycle >= m_WarmupEndCycle + m_MeasureCycles)
                {
                    Finish();
                    return;
                }
            }

            var current = m_Cases[index];
            using (current.Marker.Auto())
            {
                m_Stopwatch.Restart();
                current.Body();
                m_Stopwatch.Stop();
            }
            current.LastTimeNs = m_Stopwatch.Elapsed.TotalMilliseconds * 1e6;

            m_PrevCaseIndex = index;
            m_PrevMeasured = m_WarmupEndCycle >= 0;
            m_Frame++;
        }

        private void Finish()
        {
            enabled = false;
            StopProfileLog();

            bool hasGc = m_GcBytesRecorder.Valid;
            var baseline = m_Cases[0];
            double baseNs = Percentile(baseline.TimeNs, 0.5);
            long baseBytes = hasGc ? (long)Percentile(baseline.GcBytes, 0.5) : 0;
            long baseCount = hasGc ? (long)Percentile(baseline.GcCount, 0.5) : 0;

            var sb = new StringBuilder();
            sb.AppendLine($"# runtime={RuntimeDescription()}, platform={Application.platform}, unity={Application.unityVersion}, development={Debug.isDebugBuild}, callsPerFrame={CallsPerFrame}, samples={m_MeasureCycles}, warmupCycles={m_WarmupEndCycle}");
            // 중앙값 · p95는 정렬 후 (int)(n * p) 위치 값 (짝수 표본이면 상위 중앙값). Baseline 값을 빼고 호출 수로 나눈다
            sb.AppendLine("case,median_ns_per_call,p95_ns_per_call,gc_bytes_per_call,gc_allocs_per_call");
            for (int i = 1; i < m_Cases.Count; i++)
            {
                var c = m_Cases[i];
                double median = Math.Max(0, Percentile(c.TimeNs, 0.5) - baseNs) / CallsPerFrame;
                double p95 = Math.Max(0, Percentile(c.TimeNs, 0.95) - baseNs) / CallsPerFrame;
                string bytes = hasGc ? (Math.Max(0, (long)Percentile(c.GcBytes, 0.5) - baseBytes) / (double)CallsPerFrame).ToString("F2") : "n/a";
                string count = hasGc ? (Math.Max(0, (long)Percentile(c.GcCount, 0.5) - baseCount) / (double)CallsPerFrame).ToString("F3") : "n/a";
                sb.AppendLine($"{c.Name},{median:F2},{p95:F2},{bytes},{count}");
            }

            // 첫 호출 1회 측정: 표본이 하나이고 초기화 비용이 섞이므로 p95 · 할당 횟수는 없다
            foreach (var shot in m_OneShots)
            {
                string bytes = shot.GcBytes >= 0 ? (Math.Max(0, shot.GcBytes - baseBytes) / (double)shot.Calls).ToString("F2") : "n/a";
                sb.AppendLine($"{shot.Name},{shot.TimeNs / shot.Calls:F2},,{bytes},");
            }

            string report = sb.ToString();
            string path = ResolvePathArg(OutArg) ?? DefaultOutputPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[E2S Benchmark] 결과 저장 실패: {e.Message}");
            }
            Debug.Log($"[E2S Benchmark] 결과 저장: {path}\n{report}");

            Completed?.Invoke(path);
            if (m_QuitWhenDone && !Application.isEditor)
                Application.Quit();
        }

        private void OnDestroy()
        {
            m_GcBytesRecorder.Dispose();
            m_GcCountRecorder.Dispose();
            StopProfileLog();
        }

        /// <summary>-e2sProfileLog가 있으면 Profiler 데이터를 .raw 파일로 기록한다 (Development 플레이어 전용). 에디터에서 Profiler 창으로 열어 확인한다.</summary>
        private static void StartProfileLogIfRequested()
        {
            string logPath = ResolvePathArg(ProfileLogArg);
            if (logPath == null || !Debug.isDebugBuild || Application.isEditor) return;

            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            Profiler.logFile = logPath;
            Profiler.enableBinaryLog = true;
            Profiler.maxUsedMemory = 256 * 1024 * 1024;
            Profiler.enabled = true;
        }

        private static void StopProfileLog()
        {
            if (!Profiler.enableBinaryLog) return;
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = string.Empty;
        }

        private static string ResolvePathArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return Path.GetFullPath(args[i + 1]);
            }
            return null;
        }

        private static string DefaultOutputPath()
        {
            string root = Application.isEditor ? Path.Combine(Application.dataPath, "..", "Temp") : Application.persistentDataPath;
            return Path.GetFullPath(Path.Combine(root, "E2StringProfilerBenchmark.csv"));
        }

        private static string RuntimeDescription()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#endif
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];
        }

        private static double Percentile(List<long> values, double p)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<long>(values);
            sorted.Sort();
            return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];
        }
    }
}
