using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// 화면에 보이는 Renderer(+ Frame Debugger에 잡힌 Renderer)의 머티리얼에서 셰이더 정보를 모은다.
    /// Frame Debugger의 SRPBatch 이벤트는 오브젝트가 비어 있어 SRP Batcher로 묶인 Renderer의 드로우 수는 0으로 남는다.
    /// </summary>
    internal static class ShaderCollector
    {
        const int MaxShaders = 30;
        const int MaxKeywords = 20;

        sealed class Entry
        {
            public Shader Shader;
            public readonly HashSet<Material> Materials = new HashSet<Material>();
            public readonly HashSet<Renderer> Renderers = new HashSet<Renderer>();
            public readonly SortedSet<string> Keywords = new SortedSet<string>();
            public int DrawCount;
            public int MinQueue = int.MaxValue;
            public int MaxQueue = int.MinValue;
        }

        public static List<ShaderStat> Collect(IReadOnlyDictionary<Renderer, int> drawCounts)
        {
            var renderers = new HashSet<Renderer>(FindRenderers().Where(r => r.isVisible));
            if (drawCounts != null)
                renderers.UnionWith(drawCounts.Keys);

            var entries = new Dictionary<Shader, Entry>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                int draws = drawCounts != null && drawCounts.TryGetValue(renderer, out int d) ? d : 0;
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    if (!entries.TryGetValue(mat.shader, out Entry e))
                        entries[mat.shader] = e = new Entry { Shader = mat.shader };
                    e.Materials.Add(mat);
                    if (e.Renderers.Add(renderer)) e.DrawCount += draws;
                    e.MinQueue = Mathf.Min(e.MinQueue, mat.renderQueue);
                    e.MaxQueue = Mathf.Max(e.MaxQueue, mat.renderQueue);
                    foreach (string k in mat.shaderKeywords) e.Keywords.Add(k);
                }
            }

            return entries.Values
                .OrderByDescending(e => e.DrawCount)
                .ThenByDescending(e => e.Renderers.Count)
                .Take(MaxShaders)
                .Select(e => new ShaderStat
                {
                    shader = e.Shader.name,
                    passCount = ShaderUtil.GetShaderData(e.Shader)?.ActiveSubshader?.PassCount ?? 0,
                    materialCount = e.Materials.Count,
                    rendererCount = e.Renderers.Count,
                    drawCount = e.DrawCount,
                    transparent = e.MaxQueue > (int)RenderQueue.GeometryLast,
                    minRenderQueue = e.MinQueue,
                    maxRenderQueue = e.MaxQueue,
                    hasErrors = ShaderUtil.ShaderHasError(e.Shader),
                    keywords = e.Keywords.Take(MaxKeywords).ToList(),
                })
                .ToList();
        }

        // 7000에서 FindObjectsSortMode 오버로드가 obsolete, 6000.3에는 새 오버로드가 없다.
        static Renderer[] FindRenderers() =>
#if UNITY_7000_0_OR_NEWER
            Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
#else
            Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

        /// <summary>Frame Debugger 이벤트의 오브젝트를 Renderer별 드로우 수로 바꾼다.</summary>
        public static Dictionary<Renderer, int> DrawCountsFrom(FrameDebuggerReader.Result r)
        {
            var counts = new Dictionary<Renderer, int>();
            if (r == null || r.Error != null) return counts;
            foreach (FrameDebuggerReader.DrawEvent ev in r.Events)
            {
                Renderer renderer = ev.Obj switch
                {
                    Renderer rd => rd,
                    GameObject go => go.GetComponent<Renderer>(),
                    Component c => c.GetComponent<Renderer>(),
                    _ => null,
                };
                if (renderer == null) continue;
                counts.TryGetValue(renderer, out int n);
                counts[renderer] = n + 1;
            }
            return counts;
        }
    }
}
