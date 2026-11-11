using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.PerfProbe.Tests
{
    /// <summary>에디터 Play에서 실제 소켓으로 명령을 보내 답 JSON과 캡처 폴더를 본다.</summary>
    public class PerfProbeSocketTests
    {
        string _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "HBProbePlay_" + Guid.NewGuid().ToString("N"));
            PerfProbe.Stop();
            yield return null;
            Assert.IsTrue(PerfProbe.Start(new PerfProbeOptions { Port = 0, CaptureRoot = _root, IntervalSeconds = 0.1f }));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PerfProbe.Stop();
            yield return null;
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        static string Token => PerfProbeRunner.s_Instance.Token;

        /// <summary>토큰을 붙여 보낸다.</summary>
        static Task<string> Send(int port, string line) => SendRaw(port, line + " token=" + Token);

        /// <summary>소켓 I/O는 다른 스레드에서 한다 — 서버가 메인 스레드 처리를 기다리므로 메인 스레드에서 막으면 멈춘다.</summary>
        static Task<string> SendRaw(int port, string line)
        {
            return Task.Run(() =>
            {
                using (var client = new TcpClient("127.0.0.1", port))
                {
                    client.ReceiveTimeout = 30000;
                    var stream = client.GetStream();
                    byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadLine();
                }
            });
        }

        static IEnumerator Wait(Task<string> task)
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(task.IsCompleted, "no reply in 30s");
        }

        [UnityTest]
        public IEnumerator StatusReportsLatestSummary()
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (PerfProbe.LatestSummaryJson == null && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(PerfProbe.LatestSummaryJson, "요약이 나오지 않았다");

            Task<string> task = Send(PerfProbe.Port, "status");
            yield return Wait(task);
            string reply = task.Result;
            StringAssert.StartsWith("{\"ok\":true,\"cmd\":\"status\"", reply);
            StringAssert.Contains("\"latest\":{\"seq\":", reply);
            StringAssert.EndsWith("}}", reply);
        }

        [UnityTest]
        public IEnumerator CaptureWritesFolderAndDropRemovesIt()
        {
            yield return new WaitForSeconds(0.3f);

            Task<string> task = Send(PerfProbe.Port, "capture reason=play_test");
            yield return Wait(task);
            string reply = task.Result;
            StringAssert.StartsWith("{\"ok\":true,\"cmd\":\"capture\",\"reason\":\"play_test\"", reply);

            string[] dirs = Directory.GetDirectories(_root);
            Assert.AreEqual(1, dirs.Length);
            string dir = dirs[0];
            StringAssert.EndsWith("_play_test", dir);
            foreach (string f in new[] { "meta.json", "frames.json", "summaries.json", "logs.txt" })
                Assert.IsTrue(File.Exists(Path.Combine(dir, f)), f);
            StringAssert.Contains("\"columns\":[\"t\",\"frameMs\",\"mainMs\",\"gcAlloc\",\"draw\",\"setPass\"]", File.ReadAllText(Path.Combine(dir, "frames.json")));

            string name = Path.GetFileName(dir);
            task = Send(PerfProbe.Port, "drop name=" + name);
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":true,\"cmd\":\"drop\"", task.Result);
            Assert.IsFalse(Directory.Exists(dir));
        }

        [UnityTest]
        public IEnumerator CensusWritesCensusJson()
        {
            Task<string> task = Send(PerfProbe.Port, "census");
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":true,\"cmd\":\"census\",\"reason\":\"census\"", task.Result);
            string dir = Directory.GetDirectories(_root)[0];
            StringAssert.StartsWith("{\"time\":", File.ReadAllText(Path.Combine(dir, "census.json")));
        }

        [UnityTest]
        public IEnumerator MarkAppearsInNextSummary()
        {
            Task<string> task = Send(PerfProbe.Port, "mark name=boss_start");
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":true,\"cmd\":\"mark\"", task.Result);

            float until = Time.realtimeSinceStartup + 5f;
            while ((PerfProbe.LatestSummaryJson == null || !PerfProbe.LatestSummaryJson.Contains("boss_start")) && Time.realtimeSinceStartup < until)
                yield return null;
            StringAssert.Contains("\"marks\":\"boss_start\"", PerfProbe.LatestSummaryJson);
        }

        [UnityTest]
        public IEnumerator MissingOrWrongTokenIsRejected()
        {
            Task<string> task = SendRaw(PerfProbe.Port, "status");
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":false,\"cmd\":\"status\",\"error\":\"unauthorized", task.Result);

            task = SendRaw(PerfProbe.Port, "capture token=" + new string('0', Token.Length));
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":false,\"cmd\":\"capture\",\"error\":\"unauthorized", task.Result);
            Assert.IsFalse(Directory.Exists(_root) && Directory.GetDirectories(_root).Length > 0);
        }

        [UnityTest]
        public IEnumerator IdleConnectionDoesNotBlockOthers()
        {
            using (var idle = new TcpClient("127.0.0.1", PerfProbe.Port))
            {
                Task<string> task = Send(PerfProbe.Port, "status");
                yield return Wait(task);
                StringAssert.StartsWith("{\"ok\":true", task.Result);
            }
        }

        [UnityTest]
        public IEnumerator StopThenStartInSameFrameKeepsRunning()
        {
            PerfProbe.Stop();
            Assert.IsFalse(PerfProbe.IsRunning);
            Assert.IsTrue(PerfProbe.Start(new PerfProbeOptions { Port = 0, CaptureRoot = _root, IntervalSeconds = 0.1f }));
            yield return null;
            yield return null;
            Assert.IsTrue(PerfProbe.IsRunning);
            Task<string> task = Send(PerfProbe.Port, "status");
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":true", task.Result);
        }

        [UnityTest]
        public IEnumerator PortInUseFailsCleanly()
        {
            PerfProbe.Stop();
            yield return null;
            var blocker = new TcpListener(System.Net.IPAddress.Loopback, 0);
            blocker.Start();
            try
            {
                int used = ((System.Net.IPEndPoint)blocker.LocalEndpoint).Port;
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"^\[HBProbe\] Failed to start"));
                Assert.IsFalse(PerfProbe.Start(new PerfProbeOptions { Port = used, CaptureRoot = _root }));
                Assert.IsFalse(PerfProbe.IsRunning);
                Assert.IsNull(GameObject.Find("HBPerfProbe"));
            }
            finally
            {
                blocker.Stop();
            }
        }

        [UnityTest]
        public IEnumerator BadCommandGetsError()
        {
            Task<string> task = Send(PerfProbe.Port, "capture reason=../x");
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":false", task.Result);
            task = Send(PerfProbe.Port, "capture reason=" + new string('r', ProbeCommand.MaxReasonLength + 1));
            yield return Wait(task);
            StringAssert.StartsWith("{\"ok\":false", task.Result);
            Assert.IsFalse(Directory.Exists(_root) && Directory.GetDirectories(_root).Length > 0);
        }
    }
}
