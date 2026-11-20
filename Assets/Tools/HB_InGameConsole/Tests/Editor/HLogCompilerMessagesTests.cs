#if USE_HLOG
using System;
using System.Collections.Generic;
using InGameConsoleSystem.Editor;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace InGameConsoleSystem.Tests
{
    /// <summary>컴파일 메시지를 어셈블리별로 바꿔 두는 규칙, 바깥 기록 넣기</summary>
    public class HLogCompilerMessagesTests
    {
        private const string AsmA = "Library/ScriptAssemblies/A.dll";
        private const string AsmB = "Library/ScriptAssemblies/B.dll";

        private static CompilerMessage Message(CompilerMessageType type, string text) =>
            new CompilerMessage { type = type, message = text };

        private static HLogCompilerMessages.Item Item(string assembly, string text) =>
            new HLogCompilerMessages.Item { assembly = assembly, message = text, type = LogType.Warning };

        [Test]
        public void Replace_KeepsWarningsAndErrors_SkipsInfo()
        {
            var result = HLogCompilerMessages.Replace(new List<HLogCompilerMessages.Item>(), AsmA, new[]
            {
                Message(CompilerMessageType.Warning, "w"),
                Message(CompilerMessageType.Error, "e"),
                Message(CompilerMessageType.Info, "i")
            });

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(LogType.Warning, result[0].type);
            Assert.AreEqual("w", result[0].message);
            Assert.AreEqual(LogType.Error, result[1].type);
            Assert.AreEqual(AsmA, result[1].assembly);
        }

        [Test]
        public void Replace_SameAssembly_DropsOldMessages_KeepsOtherAssemblies()
        {
            var current = new List<HLogCompilerMessages.Item> { Item(AsmA, "old a"), Item(AsmB, "b") };

            var result = HLogCompilerMessages.Replace(current, AsmA, new[] { Message(CompilerMessageType.Warning, "new a") });

            CollectionAssert.AreEqual(new[] { "b", "new a" }, result.ConvertAll(i => i.message));
        }

        [Test]
        public void Replace_CleanCompile_RemovesThatAssembly()
        {
            var current = new List<HLogCompilerMessages.Item> { Item(AsmA, "a"), Item(AsmB, "b") };

            var result = HLogCompilerMessages.Replace(current, AsmA, new CompilerMessage[0]);

            CollectionAssert.AreEqual(new[] { "b" }, result.ConvertAll(i => i.message));
        }

        [Test]
        public void KeepExisting_DropsAssembliesNoLongerCompiled()
        {
            var current = new List<HLogCompilerMessages.Item> { Item(AsmA, "a"), Item(AsmB, "b") };

            var result = HLogCompilerMessages.KeepExisting(current, new HashSet<string> { AsmB });

            CollectionAssert.AreEqual(new[] { "b" }, result.ConvertAll(i => i.message));
        }

        [Test]
        public void AddExternalEntry_RaisesLogAddedWithType()
        {
            // 사용자 HLog 상태(꺼짐 · 링 가득 참)에 기대지 않도록 개수 대신 이벤트로 받은 항목을 본다
            const string text = "HLogCompilerMessagesTests: test external entry";
            bool wasEnabled = HLogCore.IsLogEnabled;
            HLogCore.LogEntry received = null;
            Action<HLogCore.LogEntry> handler = e => { if (e.RawMessage == text) received = e; };
            HLogCore.OnLogAdded += handler;
            try
            {
                HLogCore.IsLogEnabled = true;
                HLogCore.AddExternalEntry(text, LogType.Warning);
            }
            finally
            {
                HLogCore.OnLogAdded -= handler;
                HLogCore.IsLogEnabled = wasEnabled;
            }

            Assert.IsNotNull(received);
            Assert.AreEqual(LogType.Warning, received.LogType);
        }
    }
}
#endif
