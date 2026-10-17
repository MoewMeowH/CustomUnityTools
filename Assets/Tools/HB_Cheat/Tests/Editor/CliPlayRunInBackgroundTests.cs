using System.Collections.Generic;
using System.IO;
using HB.Cheat.Editor;
using NUnit.Framework;
using UnityEditor;

namespace HB.Cheat.Tests
{
    /// <summary>cheat_play 동안만 runInBackground를 켜고 되돌리는지 (Application·SessionState는 가짜 호스트로 바꾼다)</summary>
    public class CliPlayRunInBackgroundTests
    {
        private sealed class FakeHost : ICliPlayHost
        {
            private bool m_RunInBackground;
            public readonly Dictionary<string, bool> Bools = new Dictionary<string, bool>();
            public readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
            public readonly List<string> Logs = new List<string>();
            public int Writes;

            public bool RunInBackground
            {
                get => m_RunInBackground;
                set { m_RunInBackground = value; Writes++; }
            }

            public bool IsPlayingOrWillChangePlaymode { get; set; }

            public FakeHost(bool runInBackground) { m_RunInBackground = runInBackground; }

            public bool GetBool(string key) => Bools.TryGetValue(key, out bool v) && v;
            public void SetBool(string key, bool value) => Bools[key] = value;
            public void EraseBool(string key) => Bools.Remove(key);
            public int GetInt(string key) => Ints.TryGetValue(key, out int v) ? v : 0;
            public void SetInt(string key, int value) => Ints[key] = value;
            public void EraseInt(string key) => Ints.Remove(key);
            public void Log(string message) => Logs.Add(message);

            public bool HasNoState => Bools.Count == 0 && Ints.Count == 0;
        }

        private static readonly PipelineRuntimeConfigProbe.Result NoPipeline = new PipelineRuntimeConfigProbe.Result(PipelineRuntimeConfigProbe.State.NoFile);

        private static (FakeHost Host, CliPlayRunInBackground Controller) Create(bool runInBackground)
        {
            var host = new FakeHost(runInBackground);
            return (host, new CliPlayRunInBackground(host));
        }

        // cheat_play → isPlaying = true 이후 Unity가 하는 상태 변화
        private static void EnterPlay(FakeHost host, CliPlayRunInBackground c)
        {
            host.IsPlayingOrWillChangePlaymode = true;
            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
        }

        private static void ExitPlay(FakeHost host, CliPlayRunInBackground c)
        {
            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            host.IsPlayingOrWillChangePlaymode = false;
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        [Test]
        public void OriginalFalse_OnDuringPlay_RestoredInEditMode()
        {
            var (host, c) = Create(false);
            var d = c.Begin(true, NoPipeline);

            Assert.IsTrue(d.Active);
            Assert.AreEqual(false, d.Original);
            Assert.IsNull(d.Reason);
            Assert.IsTrue(host.RunInBackground);

            EnterPlay(host, c);
            Assert.IsTrue(host.RunInBackground);
            Assert.IsFalse(c.Pending, "EnteredPlayMode에서 대기 해제");

            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            Assert.IsFalse(host.RunInBackground, "종료 전환 중 원래 값 재적용");
            Assert.IsTrue(c.Owned, "소유권은 에디트 모드까지 유지");

            host.IsPlayingOrWillChangePlaymode = false;
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
            Assert.IsFalse(host.RunInBackground);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void OriginalTrue_DoesNothing()
        {
            var (host, c) = Create(true);
            var d = c.Begin(true, NoPipeline);

            Assert.IsFalse(d.Active);
            Assert.AreEqual(true, d.Original);
            Assert.AreEqual(RunInBackgroundDecision.AlreadyTrue, d.Reason);

            EnterPlay(host, c);
            ExitPlay(host, c);
            Assert.AreEqual(0, host.Writes, "덮어쓴 것도 되돌린 것도 없어야 한다");
            Assert.IsTrue(host.RunInBackground);
            Assert.IsEmpty(host.Logs);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void Disabled_DoesNothing()
        {
            var (host, c) = Create(false);
            var d = c.Begin(false, NoPipeline);

            Assert.IsFalse(d.Active);
            Assert.IsNull(d.Original);
            Assert.AreEqual(RunInBackgroundDecision.Disabled, d.Reason);
            EnterPlay(host, c);
            ExitPlay(host, c);
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void ManualPlay_WithoutBegin_IsNotTouched()
        {
            var (host, c) = Create(false);
            EnterPlay(host, c);
            ExitPlay(host, c);
            c.OnLoad();
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void LaterWriteDuringExit_IsOverriddenInEditMode()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            EnterPlay(host, c);

            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            host.RunInBackground = true; // 종료 중 다른 코드가 다시 켬
            host.IsPlayingOrWillChangePlaymode = false;
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
            Assert.IsFalse(host.RunInBackground);
        }

        [Test]
        public void OnLoad_InEditModeWithOwnedState_Restores()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            EnterPlay(host, c);

            // Play 중 재컴파일로 종료 이벤트를 놓치고 에디트 모드에서 도메인이 다시 올라옴
            host.IsPlayingOrWillChangePlaymode = false;
            var reloaded = new CliPlayRunInBackground(host);
            reloaded.OnLoad();
            Assert.IsFalse(host.RunInBackground);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void OnLoad_DuringPlay_KeepsOwnership()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            EnterPlay(host, c);

            var reloaded = new CliPlayRunInBackground(host);
            reloaded.OnLoad();
            Assert.IsTrue(host.RunInBackground);
            Assert.IsTrue(reloaded.Owned);

            ExitPlay(host, reloaded);
            Assert.IsFalse(host.RunInBackground);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void PlayNotEntered_RestoredAfterGraceTicks()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            host.IsPlayingOrWillChangePlaymode = false; // 컴파일 에러 등으로 진입 취소

            int ticks = 0;
            while (c.Tick()) ticks++;
            Assert.AreEqual(CliPlayRunInBackground.PendingGraceTicks - 1, ticks);
            Assert.IsFalse(host.RunInBackground);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void Pending_WhileEnteringPlay_KeepsWaiting()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            host.IsPlayingOrWillChangePlaymode = true;

            for (int i = 0; i < CliPlayRunInBackground.PendingGraceTicks * 3; i++)
                Assert.IsTrue(c.Tick());
            Assert.IsTrue(host.RunInBackground);

            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            Assert.IsFalse(c.Tick());
            Assert.IsTrue(host.RunInBackground, "Play에 들어갔으면 대기만 끝나고 값은 유지");
        }

        [Test]
        public void Quitting_Restores()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            EnterPlay(host, c);
            c.OnQuitting();
            Assert.IsFalse(host.RunInBackground);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void StaleOwnedState_RestoredBeforeNextBegin()
        {
            var (host, c) = Create(false);
            c.Begin(true, NoPipeline);
            host.IsPlayingOrWillChangePlaymode = false; // 종료 경로를 놓친 채 에디트 모드

            var d = c.Begin(true, NoPipeline);
            Assert.IsTrue(d.Active);
            Assert.AreEqual(false, d.Original, "남은 상태를 되돌린 뒤 읽은 값이 원래 값");
        }

        [Test]
        public void PipelineDriverEnabled_DoesNothing()
        {
            var (host, c) = Create(false);
            var pipeline = new PipelineRuntimeConfigProbe.Result(PipelineRuntimeConfigProbe.State.DriverEnabled, true, false);
            var d = c.Begin(true, pipeline);

            Assert.IsFalse(d.Active);
            Assert.AreEqual(RunInBackgroundDecision.PipelineDriver, d.Reason);
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void PipelineConfigUnreadable_DoesNothing()
        {
            var (host, c) = Create(false);
            var pipeline = new PipelineRuntimeConfigProbe.Result(PipelineRuntimeConfigProbe.State.Unreadable, error: "bad");
            var d = c.Begin(true, pipeline);

            Assert.IsFalse(d.Active);
            Assert.AreEqual(RunInBackgroundDecision.PipelineConfigUnreadable, d.Reason);
            Assert.AreEqual(0, host.Writes);
        }

        [Test]
        public void PipelineConfigDisabled_Proceeds()
        {
            var (host, c) = Create(false);
            var pipeline = new PipelineRuntimeConfigProbe.Result(PipelineRuntimeConfigProbe.State.Disabled, false, true);
            Assert.IsTrue(c.Begin(true, pipeline).Active);
        }

        #region PipelineRuntimeConfigProbe

        private static string TempConfig(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "hbcheat_pipeline_" + System.Guid.NewGuid().ToString("N") + ".json");
            if (content != null) File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void Probe_NoFile()
        {
            var r = PipelineRuntimeConfigProbe.Probe(TempConfig(null));
            Assert.AreEqual(PipelineRuntimeConfigProbe.State.NoFile, r.State);
        }

        [TestCase("{\"enableInBuilds\":true,\"autoStart\":true}", "DriverEnabled", true, true)]
        [TestCase("{\"enableInBuilds\":true,\"autoStart\":false}", "DriverEnabled", true, false)]
        [TestCase("{\"enableInBuilds\":false,\"autoStart\":true}", "Disabled", false, true)]
        [TestCase("{\"port\":0}", "Disabled", false, true)]
        [TestCase("", "Disabled", false, true)] // Pipeline도 같은 FromJsonOverwrite → 기본값 → 드라이버 없음
        // enum은 internal이라 public 테스트 인자로 쓸 수 없어 이름으로 받는다
        public void Probe_ReadsFields(string json, string expected, bool enable, bool autoStart)
        {
            string path = TempConfig(json);
            try
            {
                var r = PipelineRuntimeConfigProbe.Probe(path);
                Assert.AreEqual(expected, r.State.ToString());
                Assert.AreEqual(enable, r.EnableInBuilds);
                Assert.AreEqual(autoStart, r.AutoStart);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Probe_BrokenJson_IsUnreadable()
        {
            string path = TempConfig("{ not json");
            try
            {
                var r = PipelineRuntimeConfigProbe.Probe(path);
                Assert.AreEqual(PipelineRuntimeConfigProbe.State.Unreadable, r.State);
                Assert.IsNotNull(r.Error);
            }
            finally
            {
                File.Delete(path);
            }
        }

        #endregion

        [Test]
        public void PlayEnteringJson_Shape()
        {
            string json = CheatProtocol.PlayEntering(true, false, null, "none", null, null);
            Assert.AreEqual("{\"ok\":true,\"playMode\":\"entering\",\"runInBackgroundOverride\":{\"active\":true,\"original\":false,\"reason\":null,\"pipeline\":{\"config\":\"none\",\"enableInBuilds\":null,\"autoStart\":null}}}", json);

            json = CheatProtocol.PlayEntering(false, null, "pipeline_driver", "enabled", true, false);
            StringAssert.Contains("\"active\":false,\"original\":null,\"reason\":\"pipeline_driver\"", json);
            StringAssert.Contains("\"enableInBuilds\":true,\"autoStart\":false", json);
        }
    }
}
