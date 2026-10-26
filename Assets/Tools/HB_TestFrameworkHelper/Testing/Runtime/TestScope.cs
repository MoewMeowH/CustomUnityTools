using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HB.TestFrameworkHelper
{
    /// <summary>
    /// 테스트 중 만든 객체 · 바꾼 값 · 임시 자원을 한 스택에 모아 등록 역순으로 정리합니다.
    /// <para>EditMode는 <c>DestroyImmediate</c>, PlayMode는 <c>Destroy</c>를 씁니다. PlayMode의 Dispose는 파괴 "예약"까지만 하며 실제 파괴는 프레임 끝입니다
    /// (<see cref="HBTestFixture"/>는 한 프레임 기다려 줍니다).</para>
    /// <para>정리 중 예외가 나도 나머지를 끝까지 정리한 뒤 던집니다 (1개면 그 예외, 여러 개면 <see cref="AggregateException"/>).
    /// 메인 스레드 전용입니다.</para>
    /// </summary>
    public sealed class TestScope : IDisposable
    {
        private readonly Stack<Action> m_Cleanup = new Stack<Action>();

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// GameObject를 만들고 소유합니다. components는 추상 · 열린 제네릭이 아닌 Component 타입만 받습니다.
        /// <para>C#에서 추상이 아니지만 엔진이 추상으로 다루는 타입(Collider · Renderer 등)은 미리 알 수 없어,
        /// AddComponent가 실패(Unity 에러 로그)한 뒤 InvalidOperationException을 던집니다.</para>
        /// </summary>
        public GameObject NewGameObject(string name = "TestObject", params Type[] components)
        {
            ThrowIfDisposed();
            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    Type type = components[i];
                    // AddComponent가 실패하면서 Unity 에러 로그를 남기기 전에 거른다
                    if (type == null || !typeof(Component).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
                        throw new ArgumentException($"components[{i}]는 추상 · 열린 제네릭이 아닌 Component 타입이어야 합니다: {type}", nameof(components));
                }
            }

            var go = new GameObject(name ?? "TestObject");
            // 컴포넌트 추가가 실패해도 Dispose에서 지워지도록 먼저 등록한다
            PushDestroy(go);
            if (components != null)
            {
                foreach (Type type in components)
                {
                    if (go.AddComponent(type) == null)
                        throw new InvalidOperationException($"{type.Name}을(를) {go.name}에 추가하지 못했습니다.");
                }
            }
            return go;
        }

        /// <summary>ScriptableObject를 만들고 소유합니다.</summary>
        public T NewInstance<T>() where T : ScriptableObject
        {
            ThrowIfDisposed();
            var instance = ScriptableObject.CreateInstance<T>();
            PushDestroy(instance);
            return instance;
        }

        /// <summary>
        /// 넘긴 객체의 소유권을 받아 Dispose에서 지웁니다. 넘긴 것만 지웁니다 (Component면 그 Component만, GameObject는 남음).
        /// <para>에셋(디스크에 저장된 객체)은 에디터에서 거절합니다. 플레이어에서는 검사하지 않습니다 — 지워도 메모리에서 내려갈 뿐 디스크 에셋은 바뀌지 않습니다.</para>
        /// <para>이미 파괴된 객체는 등록하지 않고 그대로 돌려줍니다. Transform은 따로 지울 수 없어 거절합니다 (GameObject를 Track하세요).
        /// 다른 Component가 [RequireComponent]로 의존하는 Component는 Unity가 지우지 않고 에러 로그를 남기므로, 그런 경우는 GameObject를 Track하세요.</para>
        /// <para>등록한 뒤 에셋이 된 객체(AssetDatabase.CreateAsset 등)는 Dispose에서 지우지 않습니다 — 에셋은 그 파일을 가진 쪽(TempAssetFolder 등)이 정리합니다.</para>
        /// </summary>
        public T Track<T>(T obj) where T : Object
        {
            ThrowIfDisposed();
            if (ReferenceEquals(obj, null)) throw new ArgumentNullException(nameof(obj));
            if (obj == null) return obj;
            if (obj is Transform) throw new ArgumentException("Transform은 따로 지울 수 없습니다 — GameObject를 Track하세요.", nameof(obj));
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(obj))
                throw new ArgumentException($"에셋은 Track할 수 없습니다 (디스크의 에셋이 지워집니다): {obj.name}", nameof(obj));
#endif
            PushDestroy(obj);
            return obj;
        }

        /// <summary>
        /// 지금 값을 읽어 두었다가 Dispose에서 setter로 다시 넣습니다.
        /// 얕은 복원입니다 — 값 · 참조를 다시 넣을 뿐, 같은 객체의 내부를 바꾼 것은 되돌리지 않습니다 (설정 객체는 새 인스턴스로 바꿔 쓰세요).
        /// </summary>
        public void Preserve<T>(Func<T> getter, Action<T> setter)
        {
            ThrowIfDisposed();
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            T original = getter();
            m_Cleanup.Push(() => setter(original));
        }

        /// <summary>Dispose에서 부를 정리 동작을 등록합니다.</summary>
        public void Defer(Action cleanup)
        {
            ThrowIfDisposed();
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            m_Cleanup.Push(cleanup);
        }

        /// <summary>임시 자원(IDisposable)을 소유합니다. 이미 Dispose된 Scope면 자원을 바로 정리하고 예외를 던집니다.</summary>
        public T Own<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            if (IsDisposed)
            {
                disposable.Dispose();
                throw new ObjectDisposedException(nameof(TestScope));
            }
            m_Cleanup.Push(disposable.Dispose);
            return disposable;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            List<Exception> errors = null;
            while (m_Cleanup.Count > 0)
            {
                Action cleanup = m_Cleanup.Pop();
                try
                {
                    cleanup();
                }
                catch (Exception e)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(e);
                }
            }

            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("TestScope 정리 중 예외가 여러 개 났습니다.", errors);
        }

        private void PushDestroy(Object obj) => m_Cleanup.Push(() => DestroyNowOrLater(obj));

        private static void DestroyNowOrLater(Object obj)
        {
            if (obj == null) return;
#if UNITY_EDITOR
            // 등록 뒤 에셋이 된 객체는 지우지 않는다 (Unity가 거절하며 에러 로그를 남기고, 에셋은 파일 쪽이 정리)
            if (UnityEditor.EditorUtility.IsPersistent(obj)) return;
#endif
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TestScope));
        }
    }
}
