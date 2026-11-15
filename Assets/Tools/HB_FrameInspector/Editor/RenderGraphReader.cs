using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// URP core 17.7+의 렌더 그래프 디버그 세션에서 카메라별 지표 · 합쳐지지 못한 패스 · 버려진 패스를 읽는다.
    /// 공개 API가 없어 internal RenderGraphDebugSessionManager를 리플렉션으로 부른다. Viewer 창이 없으면
    /// RenderGraphEditorLocalDebugSession을 직접 열었다 닫는다 (7000.0.0a8 실측: 창 없이 되고 게임은 멈추지 않음).
    /// 어떤 이유로든 실패하면 그 절만 비운다.
    /// </summary>
    internal static class RenderGraphReader
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string ManagerTypeName = "UnityEngine.Rendering.RenderGraphModule.RenderGraphDebugSessionManager";
        const string LiveSessionTypeName = "UnityEngine.Rendering.RenderGraphModule.LiveRenderGraphDebugSession";
        const string EditorSessionTypeName = "RenderGraphEditorLocalDebugSession";
        const int WarmUpFrames = 2;

        // 내부 출력 객체와 필드 이름이 같은 거울 클래스 (JsonUtility로 옮겨 읽기)
        [Serializable] class MetricsJson
        {
            public string executionName;
            public int totalPasses, culledPasses, unmergedPasses, totalResources, textureCount, bufferCount, importedResourceCount;
            public InsightJson[] insights;
        }
        [Serializable] class InsightJson { public string message; }
        [Serializable] class UnmergedJson { public RenderGraphUnmergedPass[] unmergedPasses; }
        [Serializable] class CulledJson { public CulledPassJson[] culledPasses; }
        [Serializable] class CulledPassJson { public string name; }

        public static async Task<RenderGraphReport> ReadAsync(Func<Task> nextFrame)
        {
            try
            {
                return await ReadCoreAsync(nextFrame);
            }
            catch (Exception e) when (!(e is CaptureInterruptedException))
            {
                Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                return new RenderGraphReport { unavailableReason = $"렌더 그래프 내부 API 호출 실패 ({cause.GetType().Name}: {cause.Message})" };
            }
        }

        static async Task<RenderGraphReport> ReadCoreAsync(Func<Task> nextFrame)
        {
            var report = new RenderGraphReport();
#if HB_FI_URP
            Type manager = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph).Assembly.GetType(ManagerTypeName);
#else
            Type manager = null;
#endif
            Type liveSessionBase = manager?.Assembly.GetType(LiveSessionTypeName);
            Type editorSession = liveSessionBase == null
                ? null
                : TypeCache.GetTypesDerivedFrom(liveSessionBase).FirstOrDefault(t => t.Name == EditorSessionTypeName);
            if (manager == null || editorSession == null)
            {
                report.unavailableReason = "이 URP 버전에 렌더 그래프 디버그 세션이 없음 (URP core 17.7 이상 필요)";
                return report;
            }

            PropertyInfo hasActive = manager.GetProperty("hasActiveDebugSession", Static);
            bool startedByUs = !(bool)hasActive.GetValue(null);
            if (!startedByUs)
            {
                // Render Graph Viewer가 기기(원격 플레이어)에 붙인 세션이면 에디터 캡처가 아니라 읽지 않는다.
                object current = manager.GetProperty("currentDebugSession", Static)?.GetValue(null);
                if (current == null || current.GetType() != editorSession)
                {
                    report.unavailableReason = $"Render Graph Viewer가 이 에디터가 아닌 대상에 연결돼 있어 읽지 않음 ({current?.GetType().Name})";
                    return report;
                }
            }
            else
            {
                // BeginSession<T>() · BeginSession(Type) · BeginSession(IRenderGraphDebugSession) 중 세션 객체를 받는 것
                MethodInfo begin = manager.GetMethods(Static).First(m =>
                    m.Name == "BeginSession" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType != typeof(Type)
                    && m.GetParameters()[0].ParameterType.IsAssignableFrom(editorSession));
                begin.Invoke(null, new[] { Activator.CreateInstance(editorSession, true) });
            }

            try
            {
                for (int i = 0; i < WarmUpFrames; i++)
                    await nextFrame();

                int unreadable = 0;
                var graphs = (IList<string>)manager.GetMethod("GetRegisteredGraphs", Static).Invoke(null, null);
                using (var scope = (IDisposable)manager.GetMethod("CreateQueryScope", Static).Invoke(null, null))
                {
                    foreach (string graph in graphs)
                    {
                        var executions = (IList)manager.GetMethod("GetExecutions", Static).Invoke(null, new object[] { graph });
                        object graphQuery = scope.GetType().GetMethod("Graph", Instance).Invoke(scope, new object[] { graph });
                        foreach (object execution in executions)
                        {
                            object query = graphQuery.GetType().GetMethod("Execution", Instance).Invoke(graphQuery, new[] { execution });
                            RenderGraphExecution read = ReadExecution(graph, query);
                            if (read != null) report.executions.Add(read);
                            else unreadable++;
                        }
                    }
                }

                if (report.executions.Count == 0 && unreadable > 0)
                {
                    report.unavailableReason = $"렌더 그래프 지표를 읽지 못함 — 내부 출력 형식이 바뀐 것으로 보임 (카메라 {unreadable}개)";
                    return report;
                }
                if (unreadable > 0)
                    report.unavailableReason = $"카메라 {unreadable}개는 지표를 읽지 못해 뺐음";
            }
            finally
            {
                if (startedByUs)
                    manager.GetMethod("EndSession", Static).Invoke(null, null);
            }

            report.available = report.executions.Count > 0;
            if (!report.available)
                report.unavailableReason = "렌더 그래프 실행 기록이 없음 (렌더 그래프를 쓰지 않는 파이프라인이거나 그려진 카메라 없음)";
            return report;
        }

        /// <summary>지표를 못 읽었거나(쿼리 실패) 내부 출력 필드 이름이 바뀌어 0만 남을 상황이면 null — 0을 실측처럼 남기지 않는다.</summary>
        static RenderGraphExecution ReadExecution(string graph, object query)
        {
            string metricsJson = QueryJson(query, "GetGraphMetrics");
            if (metricsJson == null || !metricsJson.Contains("\"totalPasses\"") || !metricsJson.Contains("\"unmergedPasses\""))
                return null;

            var metrics = JsonUtility.FromJson<MetricsJson>(metricsJson);
            var unmerged = FromJson<UnmergedJson>(QueryJson(query, "GetUnmergedPasses"));
            var culled = FromJson<CulledJson>(QueryJson(query, "GetCulledPasses"));
            return new RenderGraphExecution
            {
                graphName = graph,
                executionName = metrics.executionName,
                totalPasses = metrics.totalPasses,
                culledPasses = metrics.culledPasses,
                unmergedPasses = metrics.unmergedPasses,
                totalResources = metrics.totalResources,
                textureCount = metrics.textureCount,
                bufferCount = metrics.bufferCount,
                importedResourceCount = metrics.importedResourceCount,
                insights = metrics.insights?.Select(i => i.message).ToList() ?? new List<string>(),
                unmerged = unmerged?.unmergedPasses?.ToList() ?? new List<RenderGraphUnmergedPass>(),
                culled = culled?.culledPasses?.Select(c => c.name).ToList() ?? new List<string>(),
            };
        }

        static T FromJson<T>(string json) where T : class => json == null ? null : JsonUtility.FromJson<T>(json);

        // QueryResult<T>.TryGetValue(out T)로 꺼내 JSON으로 돌려준다. 실패하면 null.
        static string QueryJson(object query, string method)
        {
            object result = query.GetType().GetMethod(method, Instance).Invoke(query, null);
            var args = new object[] { null };
            bool ok = (bool)result.GetType().GetMethod("TryGetValue", Instance).Invoke(result, args);
            return ok && args[0] != null ? JsonUtility.ToJson(args[0]) : null;
        }
    }
}
