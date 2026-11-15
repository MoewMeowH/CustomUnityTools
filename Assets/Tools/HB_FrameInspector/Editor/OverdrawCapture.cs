using UnityEngine;
using UnityEngine.Rendering;
#if HB_FI_URP
using UnityEngine.Rendering.Universal;
#endif

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// URP Rendering Debugger의 오버드로우 모드로 카메라를 RT에 그려 픽셀별 겹친 횟수를 얻는다.
    /// RT 출력이 원값인지 색 히트맵인지는 설정마다 달라 <see cref="OverdrawMath.Decode"/>가 둘 다 읽는다.
    /// </summary>
    internal static class OverdrawCapture
    {
        const int MaxWidth = 1280;

        internal sealed class Output
        {
            public OverdrawReport Report = new OverdrawReport();
            public byte[] OverdrawPng;
            public byte[] ScreenPng;
        }

        public static Output Capture(Camera camera, int screenWidth, int screenHeight)
        {
            var output = new Output();
            if (camera == null)
            {
                output.Report.unavailableReason = "카메라 없음 (Camera.main이 없고 지정한 카메라도 없음)";
                return output;
            }

            (int w, int h) = FitSize(screenWidth, screenHeight);
            output.ScreenPng = RenderScreen(camera, w, h);

#if HB_FI_URP
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                output.Report.unavailableReason = "URP가 아님 — 오버드로우는 URP Rendering Debugger로만 잰다";
                return output;
            }

            DebugDisplaySettingsRendering rs = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
            DebugOverdrawMode previous = rs.overdrawMode;
            int max = rs.maxOverdrawCount;
            try
            {
                int[] all = RenderCounts(camera, rs, DebugOverdrawMode.All, w, h, max, out bool colorized);

                OverdrawReport r = output.Report;
                r.available = true;
                r.maxDisplayedCount = max;
                r.width = w;
                r.height = h;
                r.decodedFrom = colorized ? "colorized" : "raw";
                r.excludedBottomRows = colorized ? Mathf.Min(OverdrawMath.LegendRows, h) : 0;
                r.all = OverdrawMath.Compute(all, max);
                output.OverdrawPng = ColorPng(all, w, h, max);
            }
            finally
            {
                rs.overdrawMode = previous;
            }
#else
            output.Report.unavailableReason = "URP 패키지 없음 — 오버드로우는 URP Rendering Debugger로만 잰다";
#endif
            return output;
        }

        static (int, int) FitSize(int w, int h)
        {
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            if (w <= MaxWidth) return (w, h);
            return (MaxWidth, Mathf.Max(1, Mathf.RoundToInt(h * (float)MaxWidth / w)));
        }

        static Texture2D RenderToTexture(Camera camera, int w, int h, RenderTextureFormat rtFormat, TextureFormat texFormat)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, rtFormat);
            RenderTexture prevTarget = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, texFormat, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            catch
            {
                if (tex != null) Object.DestroyImmediate(tex);
                throw;
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static byte[] RenderScreen(Camera camera, int w, int h)
        {
            Texture2D tex = RenderToTexture(camera, w, h, RenderTextureFormat.ARGB32, TextureFormat.RGBA32);
            try { return tex.EncodeToPNG(); }
            finally { Object.DestroyImmediate(tex); }
        }

#if HB_FI_URP
        static int[] RenderCounts(Camera camera, DebugDisplaySettingsRendering rs, DebugOverdrawMode mode, int w, int h, int max, out bool colorized)
        {
            rs.overdrawMode = mode;
            Texture2D tex = RenderToTexture(camera, w, h, RenderTextureFormat.ARGBHalf, TextureFormat.RGBAHalf);
            try
            {
                return OverdrawMath.Decode(tex.GetPixels(), w, max, out colorized);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
#endif

        static byte[] ColorPng(int[] counts, int w, int h, int max)
        {
            var px = new Color32[counts.Length];
            for (int i = 0; i < counts.Length; i++)
                px[i] = OverdrawMath.ToColor(counts[i], max);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
