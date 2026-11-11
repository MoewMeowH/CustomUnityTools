using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace HB.PerfProbe.Tests
{
    public class ProbeCommandTests
    {
        [Test]
        public void Parse_NameAndArgs()
        {
            Assert.IsTrue(ProbeCommand.TryParse("  CAPTURE   reason=leak_1  ", out ProbeCommand cmd, out string error), error);
            Assert.AreEqual("capture", cmd.Name);
            Assert.AreEqual("leak_1", cmd.Get("reason"));
            Assert.IsNull(cmd.Get("missing"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("explode")]
        [TestCase("capture reason")]
        [TestCase("capture reason=")]
        [TestCase("capture =x")]
        [TestCase("capture reason=../up")]
        [TestCase("capture reason=a/b")]
        [TestCase("capture reason=..")]
        [TestCase("drop name=.")]
        [TestCase("drop name=...")]
        [TestCase("capture reason=a.")]
        [TestCase("capture reason=.a")]
        [TestCase("capture reason=한글")]
        public void Parse_Rejects(string line)
        {
            Assert.IsFalse(ProbeCommand.TryParse(line, out ProbeCommand cmd, out string error));
            Assert.IsNull(cmd);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Parse_RejectsLongValueAndLine()
        {
            Assert.IsFalse(ProbeCommand.TryParse("capture reason=" + new string('a', ProbeCommand.MaxValueLength + 1), out _, out _));
            Assert.IsTrue(ProbeCommand.TryParse("capture reason=" + new string('a', ProbeCommand.MaxValueLength), out _, out _));
            Assert.IsFalse(ProbeCommand.TryParse("status " + new string(' ', ProbeCommand.MaxLineLength), out _, out _));
        }
    }

    public class RingTests
    {
        [Test]
        public void KeepsNewestInOrder()
        {
            var ring = new Ring<int>(3);
            for (int i = 1; i <= 5; i++) ring.Add(i);
            Assert.AreEqual(3, ring.Count);
            Assert.AreEqual(3, ring[0]);
            Assert.AreEqual(4, ring[1]);
            Assert.AreEqual(5, ring[2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => { int _ = ring[3]; });
        }

        [Test]
        public void PartialAndClear()
        {
            var ring = new Ring<int>(4);
            ring.Add(7);
            ring.Add(8);
            Assert.AreEqual(2, ring.Count);
            Assert.AreEqual(7, ring[0]);
            ring.Clear();
            Assert.AreEqual(0, ring.Count);
            ring.Add(9);
            Assert.AreEqual(9, ring[0]);
        }
    }

    public class IntervalAggregatorTests
    {
        static FrameSample Frame(float ms, float main = 5, long alloc = 100, int draw = 10, int setPass = 2)
        {
            return new FrameSample { FrameMs = ms, MainThreadMs = main, GcAllocBytes = alloc, DrawCalls = draw, SetPassCalls = setPass };
        }

        [Test]
        public void SummarizesAndResets()
        {
            var agg = new IntervalAggregator();
            agg.Add(Frame(10, main: 4, alloc: 100, draw: 10));
            agg.Add(Frame(30, main: 8, alloc: 300, draw: 20));

            Assert.IsTrue(agg.Flush(1f, new MemorySample { TotalUsed = 1234 }, "a,b", out IntervalSummary s));
            Assert.AreEqual(1, s.Seq);
            Assert.AreEqual(2, s.Frames);
            Assert.AreEqual(20.0, s.FrameAvgMs, 1e-9);
            Assert.AreEqual(30.0, s.FrameMaxMs, 1e-9);
            Assert.AreEqual(50.0, s.Fps, 1e-9);
            Assert.AreEqual(6.0, s.MainAvgMs, 1e-9);
            Assert.AreEqual(8.0, s.MainMaxMs, 1e-9);
            Assert.AreEqual(400, s.GcAllocBytes);
            Assert.AreEqual(15.0, s.DrawCallsAvg, 1e-9);
            Assert.AreEqual(1234, s.Memory.TotalUsed);
            Assert.AreEqual("a,b", s.Marks);

            Assert.IsFalse(agg.Flush(2f, default, null, out _), "비운 뒤에는 프레임이 없다");
            agg.Add(Frame(16));
            Assert.IsTrue(agg.Flush(3f, default, null, out IntervalSummary s2));
            Assert.AreEqual(2, s2.Seq);
            Assert.AreEqual(1, s2.Frames);
            Assert.AreEqual(16.0, s2.FrameMaxMs, 1e-9);
        }

        [Test]
        public void MissingCounterMarksWholeInterval()
        {
            var agg = new IntervalAggregator();
            agg.Add(Frame(10, draw: 10));
            agg.Add(Frame(10, draw: -1, alloc: -1));
            Assert.IsTrue(agg.Flush(1f, default, null, out IntervalSummary s));
            Assert.AreEqual(-1.0, s.DrawCallsAvg);
            Assert.AreEqual(-1, s.GcAllocBytes);
            Assert.AreEqual(2.0, s.SetPassAvg, 1e-9);
        }

        [Test]
        public void SummaryJsonHasFields()
        {
            var s = new IntervalSummary { Seq = 3, Frames = 60, Fps = 59.5, FrameAvgMs = 16.8, Memory = new MemorySample { TotalUsed = 10, GcUsed = -1 }, Marks = "boss" };
            var w = new ProbeJson();
            s.WriteJson(w);
            string json = w.ToString();
            StringAssert.StartsWith("{\"seq\":3,", json);
            StringAssert.Contains("\"fps\":59.5", json);
            StringAssert.Contains("\"totalUsed\":10", json);
            StringAssert.Contains("\"gcUsed\":-1", json);
            StringAssert.EndsWith(",\"marks\":\"boss\"}", json);
        }
    }

    public class ProbeJsonTests
    {
        [Test]
        public void WritesNestedAndEscapes()
        {
            var w = new ProbeJson();
            w.BeginObject().Prop("s", "a\"b\\c\n\u0001").Prop("n", 1.5).Prop("nan", double.NaN).Key("arr").BeginArray().Value(1L).Value(true).Value((string)null).EndArray().EndObject();
            Assert.AreEqual("{\"s\":\"a\\\"b\\\\c\\n\\u0001\",\"n\":1.5,\"nan\":null,\"arr\":[1,true,null]}", w.ToString());
        }

        [Test]
        public void NumbersIgnoreCurrentCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.AreEqual("[0.25]", new ProbeJson().BeginArray().Value(0.25).EndArray().ToString());
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = old;
            }
        }
    }

    public class CensusTests
    {
        [Test]
        public void GroupsAndSortsByBytes()
        {
            var c = new Census();
            c.Add("Texture2D", 100);
            c.Add("Mesh", 500);
            c.Add("Texture2D", 450);
            c.Add("Material", 10);
            Assert.AreEqual(4, c.TotalObjects);
            Assert.AreEqual(1060, c.TotalBytes);

            string json = c.ToJson("T");
            int tex = json.IndexOf("\"Texture2D\"", StringComparison.Ordinal);
            int mesh = json.IndexOf("\"Mesh\"", StringComparison.Ordinal);
            int mat = json.IndexOf("\"Material\"", StringComparison.Ordinal);
            Assert.That(tex, Is.LessThan(mesh));
            Assert.That(mesh, Is.LessThan(mat));
            StringAssert.Contains("{\"type\":\"Texture2D\",\"count\":2,\"bytes\":550}", json);
            StringAssert.StartsWith("{\"time\":\"T\",\"totalObjects\":4,\"totalBytes\":1060,\"types\":[", json);
        }

        [Test]
        public void TakeFindsLoadedObjects()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                Census c = Census.Take();
                Assert.That(c.TotalObjects, Is.GreaterThan(0));
                bool found = false;
                foreach (Census.Entry e in c.Entries) found |= e.Type == typeof(Texture2D).FullName;
                Assert.IsTrue(found);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }

    public class CaptureStoreTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "HBProbeTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void CreateNamesByTimeAndAvoidsCollision()
        {
            var store = new CaptureStore(_root, 10);
            var t = new DateTime(2026, 11, 11, 9, 8, 7, 6);
            var a = store.Create("leak", t);
            var b = store.Create("leak", t);
            Assert.AreEqual("20261111_090807_006_leak", a.name);
            Assert.AreEqual("20261111_090807_006_leak_2", b.name);
            Assert.IsTrue(Directory.Exists(a.path));
            Assert.IsTrue(Directory.Exists(b.path));
        }

        [Test]
        public void PrunesOldestOverLimit()
        {
            var store = new CaptureStore(_root, 2);
            var t = new DateTime(2026, 1, 1, 0, 0, 0);
            var first = store.Create("a", t);
            var second = store.Create("b", t.AddSeconds(1));
            var third = store.Create("c", t.AddSeconds(2));
            Assert.IsFalse(Directory.Exists(first.path));
            Assert.IsTrue(Directory.Exists(second.path));
            Assert.IsTrue(Directory.Exists(third.path));
        }

        [Test]
        public void DropOnlyCaptureFolders()
        {
            var store = new CaptureStore(_root, 5);
            var made = store.Create("x", DateTime.Now);
            Directory.CreateDirectory(Path.Combine(_root, "Saves"));
            Assert.IsFalse(store.Drop("..", out _));
            Assert.IsFalse(store.Drop("...", out _));
            Assert.IsFalse(store.Drop("Saves", out string invalid));
            Assert.AreEqual("invalid capture name", invalid);
            Assert.IsTrue(Directory.Exists(Path.Combine(_root, "Saves")));
            Assert.IsFalse(store.Drop("20260101_000000_000_gone", out string notFound));
            Assert.AreEqual("not found", notFound);
            Assert.IsTrue(store.Drop(made.name, out _));
            Assert.IsFalse(Directory.Exists(made.path));
        }

        [Test]
        public void PruneLeavesOtherFolders()
        {
            Directory.CreateDirectory(Path.Combine(_root, "00_Saves"));
            Directory.CreateDirectory(Path.Combine(_root, ".cache"));
            var store = new CaptureStore(_root, 1);
            var t = new DateTime(2026, 1, 1, 0, 0, 0);
            var first = store.Create("a", t);
            var second = store.Create("b", t.AddSeconds(1));
            Assert.IsFalse(Directory.Exists(first.path));
            Assert.IsTrue(Directory.Exists(second.path));
            Assert.IsTrue(Directory.Exists(Path.Combine(_root, "00_Saves")));
            Assert.IsTrue(Directory.Exists(Path.Combine(_root, ".cache")));
        }

        [TestCase("20261111_090807_006_leak", true)]
        [TestCase("20261111_090807_006_leak_2", true)]
        [TestCase("20261111_090807_006_", false)]
        [TestCase("20261111_090807_006", false)]
        [TestCase("2026111a_090807_006_x", false)]
        [TestCase("20261111-090807_006_x", false)]
        [TestCase("Saves", false)]
        public void IsCaptureName(string name, bool expected)
        {
            Assert.AreEqual(expected, CaptureStore.IsCaptureName(name));
        }
    }

    public class LogRingTests
    {
        [Test]
        public void KeepsLastLinesWithStackForErrors()
        {
            string path = Path.GetTempFileName();
            try
            {
                var ring = new LogRing(2);
                var t = new DateTime(2026, 1, 1, 1, 2, 3, 4);
                ring.Add(t, "first", "stack0", LogType.Log);
                ring.Add(t, "second", "stack1", LogType.Log);
                ring.Add(t, "boom", "at Foo()\n", LogType.Exception);
                ring.WriteTo(path);
                string text = File.ReadAllText(path);
                StringAssert.DoesNotContain("first", text);
                StringAssert.Contains("01:02:03.004 [Log] second\n", text.Replace("\r\n", "\n"));
                StringAssert.DoesNotContain("stack1", text);
                StringAssert.Contains("[Exception] boom", text);
                StringAssert.Contains("at Foo()", text);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
