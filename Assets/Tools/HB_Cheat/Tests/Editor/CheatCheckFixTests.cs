using NUnit.Framework;
using HB.Cheat.UI;
using UnityEngine;

namespace HB.Cheat.Tests
{
    /// <summary>2026-10-24 점검 수정: 단축키 해석, NaN 범위 검사, JSON 비유한수, raw 명령 본문</summary>
    public class CheatCheckFixTests : CheatTestFixture
    {
        private const string FloatRangeId = "hb.test.floatrange";

        [SetUp]
        public void RegisterFloatRange()
        {
            CheatRegistry.Register(new CheatDefinition
            {
                Id = FloatRangeId,
                Path = "Test/FloatRange",
                Target = CheatTarget.Local,
                Args = new[] { new CheatArgDefinition { Name = "x", ValueType = CheatValueType.Float, Editor = CheatEditorKind.Number, HasRange = true, Min = 0, Max = 1, DefaultValue = 0.5f } },
                LocalInvoker = _ => CheatResult.Ok()
            });
        }

        [TearDown]
        public void UnregisterFloatRange() => CheatRegistry.Unregister(FloatRangeId);

        [TestCase("F8", KeyCode.F8)]
        [TestCase(" f9 ", KeyCode.F9)]
        [TestCase("Alpha1", KeyCode.Alpha1)]
        public void ToggleKey_Name_Parses(string text, KeyCode expected)
        {
            Assert.IsTrue(CheatRunner.TryParseToggleKey(text, out KeyCode key));
            Assert.AreEqual(expected, key);
        }

        [TestCase("1")]
        [TestCase("999")]
        [TestCase("-1")]
        [TestCase("+8")]
        [TestCase("None")]
        [TestCase("none")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("NotAKey")]
        [TestCase("F8,F9")]
        [TestCase("F8, F9")]
        public void ToggleKey_NumberNoneOrUnknown_Rejected(string text)
        {
            Assert.IsFalse(CheatRunner.TryParseToggleKey(text, out KeyCode _));
        }

#if HB_CHEAT_INPUT_SYSTEM
        [TestCase("F8", UnityEngine.InputSystem.Key.F8)]
        [TestCase("digit1", UnityEngine.InputSystem.Key.Digit1)]
        [TestCase("Backquote", UnityEngine.InputSystem.Key.Backquote)]
        public void ToggleKey_InputSystemName_Parses(string text, UnityEngine.InputSystem.Key expected)
        {
            Assert.IsTrue(CheatRunner.TryParseToggleKey(text, out UnityEngine.InputSystem.Key key));
            Assert.AreEqual(expected, key);
        }

        [TestCase("1")]
        [TestCase("999")]
        [TestCase("None")]
        [TestCase("Space,Enter")]
        public void ToggleKey_InputSystemNumberNoneOrCombined_Rejected(string text)
        {
            Assert.IsFalse(CheatRunner.TryParseToggleKey(text, out UnityEngine.InputSystem.Key _));
        }
#endif

        [Test]
        public void Validate_NaN_IsOutOfRange()
        {
            Assert.IsFalse(CheatSystem.Validate(Def(FloatRangeId), new object[] { float.NaN }, out _, out string code));
            Assert.AreEqual(CheatErrorCode.OutOfRangeValue, code);
        }

        [Test]
        public void Run_NaN_RejectedWithoutExecution()
        {
            string json = CheatProtocol.Handle("run " + FloatRangeId + " x=NaN", CheatSource.Cli);
            StringAssert.StartsWith("{\"ok\":false,", json);
            StringAssert.Contains("\"code\":\"out_of_range_value\"", json);
            Assert.AreEqual(0, CheatSystem.History.Count, "실행을 만들지 않는다");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Json_NonFiniteNumber_IsNull(double value)
        {
            Assert.AreEqual("{\"v\":null}", new CheatJsonWriter().BeginObject().Prop("v", value).EndObject().ToString());
        }

        [Test]
        public void Json_FiniteNumber_Unchanged()
        {
            Assert.AreEqual("{\"v\":1.5}", new CheatJsonWriter().BeginObject().Prop("v", 1.5).EndObject().ToString());
        }

        [TestCase("raw item_add 1 1", "item_add 1 1")]
        [TestCase("  RAW   item_add 1 1  ", "item_add 1 1")]
        [TestCase("\"raw\" item_add 1 1", "item_add 1 1")]
        [TestCase("ra\"w\" item_add \"a b\"", "item_add \"a b\"")]
        public void HandleRaw_BodyAfterVerb(string line, string expected)
        {
            CheatSystem.Options.AllowRawFromAutomation = true;
            StringAssert.Contains("\"ok\":true", CheatProtocol.Handle(line, CheatSource.Cli));
            CollectionAssert.AreEqual(new[] { expected }, Transport.Sent);
        }
    }
}
