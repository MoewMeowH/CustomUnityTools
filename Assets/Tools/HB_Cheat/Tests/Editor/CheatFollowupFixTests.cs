using HB.Cheat.Editor;
using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>2026-10-24 후속: long 스테퍼 경계, 생성 코드 문자열 이스케이프 · float 기본값 리터럴</summary>
    public class CheatFollowupFixTests
    {
        private static readonly CheatArgDefinition s_Long = new CheatArgDefinition { Name = "n", ValueType = CheatValueType.Long, Editor = CheatEditorKind.Number };

        [TestCase(9.3e18)]
        [TestCase(9223372036854775807d)]   // double로는 2^63 (long 범위 밖)
        public void FromDouble_LongAboveMax_ClampsToMax(double value)
        {
            Assert.AreEqual(long.MaxValue, CheatValueParser.FromDouble(s_Long, value));
        }

        [TestCase(-9.3e18)]
        [TestCase(-9223372036854775808d)]
        public void FromDouble_LongBelowMin_ClampsToMin(double value)
        {
            Assert.AreEqual(long.MinValue, CheatValueParser.FromDouble(s_Long, value));
        }

        [Test]
        public void FromDouble_LongInRange_Rounds()
        {
            Assert.AreEqual(13L, CheatValueParser.FromDouble(s_Long, 12.6));
            Assert.AreEqual(-3L, CheatValueParser.FromDouble(s_Long, -3.2));
        }

        [TestCase("a\u2028b", "\"a\\u2028b\"")]
        [TestCase("a\u2029b", "\"a\\u2029b\"")]
        [TestCase("a\u0085b", "\"a\\u0085b\"")]
        [TestCase("a\0b", "\"a\\u0000b\"")]
        [TestCase("line\nnext\t\"q\"\\", "\"line\\nnext\\t\\\"q\\\"\\\\\"")]
        [TestCase("한글 OK", "\"한글 OK\"")]
        public void Str_EscapesCharactersInvalidInLiterals(string input, string expected)
        {
            Assert.AreEqual(expected, CheatCodeGenerator.Str(input));
        }

        [TestCase("DEV_CHEAT", "DEV_CHEAT")]
        [TestCase("  QA_CHEAT2  ", "QA_CHEAT2")]
        [TestCase("_cheat", "_cheat")]
        [TestCase("DEV CHEAT", "DEV_CHEAT")]
        [TestCase("DEV-CHEAT", "DEV_CHEAT")]
        [TestCase("1ST_CHEAT", "DEV_CHEAT")]
        [TestCase("A;B", "DEV_CHEAT")]
        [TestCase("", "DEV_CHEAT")]
        public void UserDefine_InvalidNameFallsBackToDefault(string value, string expected)
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<CheatSettings>();
            try
            {
                var so = new UnityEditor.SerializedObject(settings);
                so.FindProperty("userDefine").stringValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(expected, settings.UserDefine);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ServerTimeout_NonFinite_FallsBackToDefault(float value)
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<CheatSettings>();
            try
            {
                var so = new UnityEditor.SerializedObject(settings);
                so.FindProperty("serverTimeoutSeconds").floatValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(10f, settings.ServerTimeoutSeconds);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [TestCase(float.NaN, "float.NaN")]
        [TestCase(float.PositiveInfinity, "float.PositiveInfinity")]
        [TestCase(float.NegativeInfinity, "float.NegativeInfinity")]
        [TestCase(1.5f, "1.5f")]
        public void Flt_NonFiniteUsesConstants(float value, string expected)
        {
            Assert.AreEqual(expected, CheatCodeGenerator.Flt(value));
        }
    }
}
