using System.Collections.Generic;
using HB.Cheat.Editor;
using NUnit.Framework;
using UnityEditor;

namespace HB.Cheat.Tests
{
    /// <summary>cheat_play 동안만 Game 뷰를 Play Unfocused로 바꾸고 되돌리는지 (Game 뷰 · SessionState는 가짜 호스트)</summary>
    public class CliPlayUnfocusedTests
    {
        private sealed class FakeHost : ICliPlayViewHost
        {
            public string Behavior;
            public bool Available = true;
            public readonly Dictionary<string, bool> Bools = new Dictionary<string, bool>();
            public readonly Dictionary<string, string> Strings = new Dictionary<string, string>();
            public readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
            public readonly List<string> Logs = new List<string>();
            public int Writes;

            public FakeHost(string behavior) { Behavior = behavior; }

            public bool IsPlayingOrWillChangePlaymode { get; set; }
            public bool ApiAvailable => Available;
            public string GetBehavior() => Behavior;
            public bool SetBehavior(string behavior) { Behavior = behavior; Writes++; return true; }
            public bool GetBool(string key) => Bools.TryGetValue(key, out bool v) && v;
            public void SetBool(string key, bool value) => Bools[key] = value;
            public void EraseBool(string key) => Bools.Remove(key);
            public string GetString(string key) => Strings.TryGetValue(key, out string v) ? v : null;
            public void SetString(string key, string value) => Strings[key] = value;
            public void EraseString(string key) => Strings.Remove(key);
            public int GetInt(string key) => Ints.TryGetValue(key, out int v) ? v : 0;
            public void SetInt(string key, int value) => Ints[key] = value;
            public void EraseInt(string key) => Ints.Remove(key);
            public void Log(string message) => Logs.Add(message);
            public string Recovery;
            public string ReadRecovery() => Recovery;
            public void WriteRecovery(string original) => Recovery = original;
            public void DeleteRecovery() => Recovery = null;

            /// <summary>에디터 재시작: SessionState는 사라지고 복구 기록 · 레이아웃 값은 남는다</summary>
            public void Restart()
            {
                Bools.Clear();
                Strings.Clear();
                Ints.Clear();
                IsPlayingOrWillChangePlaymode = false;
            }

            // "이번 실행에서 복구 기록을 확인함" 표시는 소유 상태가 아니다 (실행 동안 남는 게 의도)
            public bool HasNoState =>
                Bools.Count == (Bools.ContainsKey(CliPlayUnfocused.RecoveryCheckedKey) ? 1 : 0) && Strings.Count == 0 && Ints.Count == 0;
        }

        private const string Focused = CliPlayUnfocused.Focused;
        private const string Unfocused = CliPlayUnfocused.Unfocused;
        private const string Maximized = "PlayMaximized";

        private static (FakeHost Host, CliPlayUnfocused Controller) Create(string behavior)
        {
            var host = new FakeHost(behavior);
            return (host, new CliPlayUnfocused(host));
        }

        private static void EnterPlay(FakeHost host, CliPlayUnfocused c)
        {
            host.IsPlayingOrWillChangePlaymode = true;
            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
        }

        private static void ExitPlay(FakeHost host, CliPlayUnfocused c)
        {
            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            host.IsPlayingOrWillChangePlaymode = false;
            c.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        [Test]
        public void Focused_UnfocusedDuringPlay_RestoredInEditMode()
        {
            var (host, c) = Create(Focused);
            var d = c.Begin(true);

            Assert.IsTrue(d.Active);
            Assert.AreEqual(Focused, d.Original);
            Assert.IsNull(d.Reason);
            Assert.AreEqual(Unfocused, host.Behavior);

            EnterPlay(host, c);
            Assert.IsFalse(c.Pending, "EnteredPlayMode에서 대기 해제");
            Assert.AreEqual(Unfocused, host.Behavior);

            ExitPlay(host, c);
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsTrue(host.HasNoState);
            Assert.IsNull(host.Recovery, "정상 종료면 복구 기록도 지운다");
        }

        [Test]
        public void StaysUnfocusedUntilEditMode()
        {
            // Unity는 Play에서 나올 때도 이 값으로 포커스를 정한다
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            c.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            Assert.AreEqual(Unfocused, host.Behavior);
        }

        [Test]
        public void ClosedDuringPlay_RestoredOnNextEditorStart()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            c.OnQuitting();
            Assert.AreEqual(Focused, host.Recovery, "종료 때 되돌려도 복구 기록은 남긴다");

            // Unity가 Play 진입 때의 창 상태(Unfocused)로 레이아웃을 저장했다 → 재시작
            host.Behavior = Unfocused;
            host.Restart();
            new CliPlayUnfocused(host).OnLoad();
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsNull(host.Recovery);
        }

        [Test]
        public void RecoveryRecord_ChangedSince_LeftAndCleared()
        {
            var (host, c) = Create(Maximized);
            host.Recovery = Focused;   // 지난 실행의 기록인데 지금은 사람이 Maximized로 바꿔 둠
            Assert.IsFalse(c.OnLoad());
            Assert.AreEqual(Maximized, host.Behavior);
            Assert.IsNull(host.Recovery);
        }

        [Test]
        public void RecoveryRecord_NoViewYet_KeptForLater()
        {
            var (host, c) = Create(null);
            host.Recovery = Focused;
            Assert.IsTrue(c.OnLoad(), "뷰가 없으면 다시 확인 요청");
            Assert.AreEqual(Focused, host.Recovery);
            host.Behavior = Unfocused;   // 창이 뜬 뒤 (훅이 update에서 다시 부름)
            Assert.IsFalse(c.OnLoad());
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsNull(host.Recovery);
        }

        [Test]
        public void RecoveryRecord_GiveUp_LaterUnfocusedChoiceKept()
        {
            var (host, c) = Create(null);
            host.Recovery = Focused;
            Assert.IsTrue(c.OnLoad());
            c.GiveUpRecovery();   // 끝내 Game 뷰가 없었다
            Assert.IsNull(host.Recovery);

            // 나중에 사람이 Game 뷰를 열고 Play Unfocused를 골랐다 → 리로드돼도 덮지 않는다
            host.Behavior = Unfocused;
            Assert.IsFalse(c.OnLoad());
            Assert.AreEqual(Unfocused, host.Behavior);
        }

        [Test]
        public void RecoveryRecord_CheckedOncePerEditorSession()
        {
            var (host, c) = Create(Focused);
            Assert.IsFalse(c.OnLoad());   // 시작 때 기록 없음 → 확인 끝
            host.Recovery = Focused;      // (있을 수 없는 늦은 기록)
            host.Behavior = Unfocused;
            c.OnLoad();
            Assert.AreEqual(Unfocused, host.Behavior);
        }

        [TestCase(Maximized)]
        [TestCase("2")]
        [TestCase("garbage")]
        public void RecoveryRecord_UnexpectedContent_Ignored(string content)
        {
            var (host, c) = Create(Unfocused);
            host.Recovery = content;
            c.OnLoad();
            Assert.AreEqual(Unfocused, host.Behavior);
            Assert.AreEqual(0, host.Writes);
            Assert.IsNull(host.Recovery);
        }

        [Test]
        public void Begin_AppliesPendingRecoveryFirst()
        {
            // 지난 실행이 Play 중에 닫혀 레이아웃에 Unfocused가 남았고, 시작 확인 전에 cheat_play가 왔다
            var (host, c) = Create(Unfocused);
            host.Recovery = Focused;
            var d = c.Begin(true);
            Assert.IsTrue(d.Active);
            Assert.AreEqual(Focused, d.Original);
            Assert.AreEqual(Unfocused, host.Behavior);
            Assert.AreEqual(Focused, host.Recovery, "이번 Play의 복구 기록");
        }

        [Test]
        public void Disabled_DoesNothing()
        {
            var (host, c) = Create(Focused);
            var d = c.Begin(false);
            Assert.IsFalse(d.Active);
            Assert.AreEqual(PlayUnfocusedDecision.Disabled, d.Reason);
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void ApiUnavailable_DoesNothing()
        {
            var (host, c) = Create(Focused);
            host.Available = false;
            var d = c.Begin(true);
            Assert.AreEqual(PlayUnfocusedDecision.ApiUnavailable, d.Reason);
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void NoPlayModeView_DoesNothing()
        {
            var (host, c) = Create(null);
            var d = c.Begin(true);
            Assert.AreEqual(PlayUnfocusedDecision.NoPlayModeView, d.Reason);
            Assert.AreEqual(0, host.Writes);
        }

        [TestCase(Maximized)]
        [TestCase(Unfocused)]
        public void NotFocused_LeftAsIs(string original)
        {
            var (host, c) = Create(original);
            var d = c.Begin(true);
            Assert.IsFalse(d.Active);
            Assert.AreEqual(original, d.Original);
            Assert.AreEqual(PlayUnfocusedDecision.NotFocused, d.Reason);
            Assert.AreEqual(0, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void PlayNotEntered_RestoredAfterGraceTicks()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            for (int i = 0; i < CliPlayRunInBackground.PendingGraceTicks - 1; i++)
                Assert.IsTrue(c.Tick(), "아직 기다림");
            Assert.IsFalse(c.Tick());
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void ReloadInEditMode_Restored()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            host.IsPlayingOrWillChangePlaymode = false;   // 종료 이벤트를 놓친 채 리로드
            c.OnLoad();
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void ReloadDuringPlay_KeepsOverride()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            c.OnLoad();
            Assert.AreEqual(Unfocused, host.Behavior);
            Assert.IsTrue(c.Owned);
        }

        [Test]
        public void Quitting_Restored()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            c.OnQuitting();
            Assert.AreEqual(Focused, host.Behavior);
            Assert.IsTrue(host.HasNoState);
            Assert.AreEqual(Focused, host.Recovery);
        }

        [Test]
        public void ChangedBySomeoneDuringPlay_NotOverwritten()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            EnterPlay(host, c);
            host.Behavior = Maximized;   // 사람이 Play 중 드롭다운을 바꿈
            int writes = host.Writes;
            ExitPlay(host, c);
            Assert.AreEqual(Maximized, host.Behavior);
            Assert.AreEqual(writes, host.Writes);
            Assert.IsTrue(host.HasNoState);
        }

        [Test]
        public void StaleOwnership_RestoredBeforeNextPlay()
        {
            var (host, c) = Create(Focused);
            c.Begin(true);
            host.IsPlayingOrWillChangePlaymode = false;
            // 종료 경로를 모두 놓쳤다 → 다음 cheat_play가 먼저 되돌린 뒤 다시 판정
            var d = c.Begin(true);
            Assert.IsTrue(d.Active);
            Assert.AreEqual(Focused, d.Original);
            Assert.AreEqual(Unfocused, host.Behavior);
        }

        [Test]
        public void PlayEnteringJson_IncludesUnfocusedOverride()
        {
            string json = CheatProtocol.PlayEntering(true, false, null, "none", null, null, true, Focused, null);
            StringAssert.Contains("\"playUnfocusedOverride\":{\"active\":true,\"original\":\"PlayFocused\",\"reason\":null}", json);
            StringAssert.EndsWith("}", json);
        }

        [Test]
        public void PlayEnteringJson_OldOverloadUnchanged()
        {
            string json = CheatProtocol.PlayEntering(true, false, null, "none", null, null);
            Assert.AreEqual("{\"ok\":true,\"playMode\":\"entering\",\"runInBackgroundOverride\":{\"active\":true,\"original\":false,\"reason\":null,\"pipeline\":{\"config\":\"none\",\"enableInBuilds\":null,\"autoStart\":null}}}", json);
        }
    }
}
