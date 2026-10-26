using System;
using System.Collections.Generic;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HB.TestFrameworkHelper.Tests
{
    public class TestScopeTests
    {
        private static int s_Value;
        private static List<int> s_List;

        [Test]
        public void Dispose_RunsAllCleanupInReverseRegistrationOrder()
        {
            var order = new List<string>();
            var scope = new TestScope();
            scope.Defer(() => order.Add("defer1"));
            scope.Preserve(() => s_Value, _ => order.Add("preserve2"));
            scope.Own(new Recorder("own3", order));
            scope.Defer(() => order.Add("defer4"));

            scope.Dispose();

            CollectionAssert.AreEqual(new[] { "defer4", "own3", "preserve2", "defer1" }, order);
        }

        [Test]
        public void Dispose_OneCleanupThrows_RunsTheRestAndRethrowsIt()
        {
            var order = new List<string>();
            var scope = new TestScope();
            scope.Defer(() => order.Add("1"));
            scope.Defer(() => throw new InvalidOperationException("boom"));
            scope.Defer(() => order.Add("3"));

            var e = Assert.Throws<InvalidOperationException>(scope.Dispose);

            Assert.AreEqual("boom", e.Message);
            CollectionAssert.AreEqual(new[] { "3", "1" }, order);
        }

        [Test]
        public void Dispose_SeveralCleanupsThrow_ThrowsAggregate()
        {
            var scope = new TestScope();
            scope.Defer(() => throw new InvalidOperationException("a"));
            scope.Defer(() => throw new ArgumentException("b"));

            var e = Assert.Throws<AggregateException>(scope.Dispose);

            Assert.AreEqual(2, e.InnerExceptions.Count);
        }

        [Test]
        public void Dispose_IsIdempotent_AndRejectsLaterRegistration()
        {
            int count = 0;
            var scope = new TestScope();
            scope.Defer(() => count++);
            scope.Dispose();
            scope.Dispose();
            Assert.AreEqual(1, count);
            Assert.IsTrue(scope.IsDisposed);

            Assert.Throws<ObjectDisposedException>(() => scope.Defer(() => { }));
            Assert.Throws<ObjectDisposedException>(() => scope.Preserve(() => 0, _ => { }));
            Assert.Throws<ObjectDisposedException>(() => scope.NewGameObject("x"));
            Assert.Throws<ObjectDisposedException>(() => scope.NewInstance<TestScopeProbeAsset>());
            Assert.Throws<ObjectDisposedException>(() => scope.NewTempDirectory());

            var late = new Recorder("late", new List<string>());
            Assert.Throws<ObjectDisposedException>(() => scope.Own(late));
            Assert.IsTrue(late.Disposed, "Dispose된 Scope에 넘긴 자원은 바로 정리돼야 함");
        }

        [Test]
        public void NewGameObject_AddsComponents_AndDisposeDestroysIt()
        {
            var scope = new TestScope();
            GameObject go = scope.NewGameObject("HBTestScope_New", typeof(BoxCollider));
            Assert.IsNotNull(go.GetComponent<BoxCollider>());

            scope.Dispose();

            Assert.IsTrue(go == null);
        }

        [Test]
        public void NewGameObject_NonComponentType_ThrowsWithoutCreating()
        {
            string name = "HBTestScope_Invalid_" + Guid.NewGuid().ToString("N");
            var scope = new TestScope();
            try
            {
                Assert.Throws<ArgumentException>(() => scope.NewGameObject(name, typeof(string)));
                Assert.Throws<ArgumentException>(() => scope.NewGameObject(name, typeof(AbstractProbeComponent)), "C# 추상 Component");
                Assert.IsTrue(GameObject.Find(name) == null);
            }
            finally
            {
                scope.Dispose();
            }
        }

        [Test]
        public void Track_Component_DestroysOnlyThatComponent()
        {
            var owner = new TestScope();
            try
            {
                GameObject go = owner.NewGameObject("HBTestScope_Owner", typeof(BoxCollider), typeof(SphereCollider));
                var inner = new TestScope();
                inner.Track(go.GetComponent<BoxCollider>());

                inner.Dispose();

                Assert.IsTrue(go != null);
                Assert.IsTrue(go.GetComponent<BoxCollider>() == null);
                Assert.IsTrue(go.GetComponent<SphereCollider>() != null);
            }
            finally
            {
                owner.Dispose();
            }
        }

        [Test]
        public void Track_PersistentAsset_ThrowsAndKeepsTheAsset()
        {
            var owner = new TestScope();
            try
            {
                TempAssetFolder folder = owner.NewTempAssetFolder();
                var asset = ScriptableObject.CreateInstance<TestScopeProbeAsset>();
                string path = folder.AssetPath + "/Probe.asset";
                AssetDatabase.CreateAsset(asset, path);

                var scope = new TestScope();
                Assert.Throws<ArgumentException>(() => scope.Track(asset));
                scope.Dispose();

                Assert.IsTrue(asset != null);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TestScopeProbeAsset>(path));
            }
            finally
            {
                owner.Dispose();
            }
        }

        [Test]
        public void ObjectThatBecameAnAsset_IsNotDestroyedOnDispose()
        {
            var owner = new TestScope();
            try
            {
                TempAssetFolder folder = owner.NewTempAssetFolder();
                var scope = new TestScope();
                var so = scope.NewInstance<TestScopeProbeAsset>();
                string path = folder.AssetPath + "/Became.asset";
                AssetDatabase.CreateAsset(so, path);

                // 에셋을 지우려 하면 Unity가 에러 로그를 남겨 이 테스트가 실패한다
                scope.Dispose();

                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TestScopeProbeAsset>(path));
            }
            finally
            {
                owner.Dispose();
            }
        }

        [Test]
        public void Track_Transform_Throws()
        {
            var scope = new TestScope();
            try
            {
                GameObject go = scope.NewGameObject("HBTestScope_Transform");
                Assert.Throws<ArgumentException>(() => scope.Track(go.transform));
            }
            finally
            {
                scope.Dispose();
            }
        }

        [Test]
        public void Track_Null_Throws_AlreadyDestroyed_ReturnsWithoutRegistering()
        {
            var scope = new TestScope();
            Assert.Throws<ArgumentNullException>(() => scope.Track<Object>(null));

            var destroyed = new GameObject("HBTestScope_Destroyed");
            Object.DestroyImmediate(destroyed);
            Assert.AreSame(destroyed, scope.Track(destroyed));

            Assert.DoesNotThrow(scope.Dispose);
        }

        [Test]
        public void NewInstance_IsDestroyedOnDispose()
        {
            var scope = new TestScope();
            var so = scope.NewInstance<TestScopeProbeAsset>();

            scope.Dispose();

            Assert.IsTrue(so == null);
        }

        [Test]
        public void Preserve_PreservedTwice_RestoresTheFirstValue()
        {
            s_Value = 1;
            var scope = new TestScope();
            scope.Preserve(() => s_Value, v => s_Value = v);
            s_Value = 2;
            scope.Preserve(() => s_Value, v => s_Value = v);
            s_Value = 3;

            scope.Dispose();

            Assert.AreEqual(1, s_Value);
        }

        [Test]
        public void Preserve_IsShallow_InnerMutationIsNotRestored()
        {
            s_List = new List<int> { 1 };
            var scope = new TestScope();
            scope.Preserve(() => s_List, v => s_List = v);
            List<int> original = s_List;
            s_List.Add(2);

            scope.Dispose();

            Assert.AreSame(original, s_List);
            Assert.AreEqual(2, s_List.Count, "같은 객체의 내부 변경은 되돌리지 않는다 (문서화된 동작)");
        }

        private sealed class Recorder : IDisposable
        {
            private readonly string m_Name;
            private readonly List<string> m_Order;

            public bool Disposed { get; private set; }

            public Recorder(string name, List<string> order)
            {
                m_Name = name;
                m_Order = order;
            }

            public void Dispose()
            {
                Disposed = true;
                m_Order.Add(m_Name);
            }
        }
    }
}
