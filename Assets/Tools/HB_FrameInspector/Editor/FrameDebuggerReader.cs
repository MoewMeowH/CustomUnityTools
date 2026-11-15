using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// Frame Debugger의 드로우 이벤트 목록을 읽는다. 공개 API가 없어 internal 타입을 리플렉션으로 부른다.
    /// FrameDebuggerUtility.SetEnabled만으로는 이벤트가 0개라(7000.0.0a8 실측) 창의 EnableFrameDebugger()를 쓴다.
    /// 켜는 동안 Play가 일시정지되므로 끝나면 창 · 일시정지 상태를 되돌린다.
    /// </summary>
    internal static class FrameDebuggerReader
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string WindowTypeName = "UnityEditor.FrameDebuggerWindow, UnityEditor.CoreModule";
        const string UtilityTypeName = "UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility, UnityEditor.CoreModule";
        const int TimeoutMs = 3000;

        internal struct DrawEvent
        {
            public string Path;
            public string Type;
            public Object Obj;
        }

        internal sealed class Result
        {
            public readonly List<DrawEvent> Events = new List<DrawEvent>();
            public string Error;
        }

        /// <summary>내부 API가 바뀌어 리플렉션이 어떤 식으로 실패해도 드로우 목록만 비우고 캡처는 계속한다.</summary>
        public static async Task<Result> ReadAsync()
        {
            try
            {
                return await ReadCoreAsync();
            }
            catch (Exception e)
            {
                Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                return new Result { Error = $"Frame Debugger 내부 API 호출 실패 ({cause.GetType().Name}: {cause.Message})" };
            }
        }

        static async Task<Result> ReadCoreAsync()
        {
            var result = new Result();
            Type windowType = Type.GetType(WindowTypeName);
            Type utilType = Type.GetType(UtilityTypeName);
            MethodInfo enable = windowType?.GetMethod("EnableFrameDebugger", Instance);
            MethodInfo disable = windowType?.GetMethod("DisableFrameDebugger", Instance);
            PropertyInfo countProp = utilType?.GetProperty("count", Static);
            MethodInfo getEvents = utilType?.GetMethod("GetFrameEvents", Static);
            MethodInfo getName = utilType?.GetMethod("GetFrameEventInfoName", Static);
            MethodInfo getObj = utilType?.GetMethod("GetFrameEventObject", Static);
            if (enable == null || disable == null || countProp == null || getEvents == null || getName == null)
            {
                result.Error = "이 Unity 버전에서 Frame Debugger 내부 API를 찾지 못함";
                return result;
            }

            bool wasEnabled = FrameDebugger.enabled;
            bool pausedBefore = EditorApplication.isPaused;
            Object[] existing = Resources.FindObjectsOfTypeAll(windowType);
            bool openedByUs = existing.Length == 0;
            var window = existing.Length > 0 ? (EditorWindow)existing[0] : EditorWindow.GetWindow(windowType);

            try
            {
                if (!wasEnabled)
                    enable.Invoke(window, null);

                int count = 0;
                for (int waited = 0; waited < TimeoutMs; waited += 50)
                {
                    count = (int)countProp.GetValue(null);
                    if (count > 0) break;
                    await Task.Delay(50);
                }

                if (count == 0)
                {
                    result.Error = $"Frame Debugger가 {TimeoutMs}ms 안에 이벤트를 받지 못함";
                    return result;
                }

                var events = (Array)getEvents.Invoke(null, null);
                FieldInfo typeField = events.Length > 0 ? events.GetValue(0).GetType().GetField("m_Type", Instance) : null;
                for (int i = 0; i < events.Length; i++)
                {
                    object ev = events.GetValue(i);
                    result.Events.Add(new DrawEvent
                    {
                        Path = (string)getName.Invoke(null, new object[] { i }),
                        Type = typeField?.GetValue(ev)?.ToString() ?? "?",
                        Obj = getObj?.Invoke(null, new object[] { i }) as Object,
                    });
                }
            }
            finally
            {
                if (!wasEnabled)
                    disable.Invoke(window, null);
                if (openedByUs && window != null)
                    window.Close();
                if (EditorApplication.isPlaying)
                    EditorApplication.isPaused = pausedBefore;
            }

            return result;
        }

        public static DrawListReport ToReport(Result r)
        {
            var report = new DrawListReport();
            if (r.Error != null)
            {
                report.unavailableReason = r.Error;
                return report;
            }

            report.available = true;
            report.eventCount = r.Events.Count;
            report.byPass = ReportMath.CountBy(r.Events.Select(e => ReportMath.PassNameFromEventPath(e.Path)));
            report.byType = ReportMath.CountBy(r.Events.Select(e => e.Type));
            report.byObject = ReportMath.CountBy(r.Events.Where(e => e.Obj != null).Select(e => e.Obj.name), 30);
            return report;
        }
    }
}
