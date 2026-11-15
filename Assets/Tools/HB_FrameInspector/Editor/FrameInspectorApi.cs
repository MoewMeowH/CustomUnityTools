using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// 창 밖에서 쓰는 입구 (AI 에이전트 · 스크립트). 캡처 → 저장 → 저장 폴더 경로.
    /// 예: <c>await FrameInspectorApi.CaptureAndSaveAsync()</c> 뒤 그 폴더의 report.md를 읽는다.
    /// </summary>
    public static class FrameInspectorApi
    {
        const string SaveRootPrefKey = "HB.FrameInspector.SaveRoot";

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string DefaultSaveRoot => Path.Combine(ProjectRoot, "FrameReports");

        /// <summary>리포트를 저장할 상위 폴더. 이 PC · 이 사용자 설정(EditorPrefs)이다.</summary>
        public static string SaveRoot
        {
            get
            {
                string v = EditorPrefs.GetString(SaveRootPrefKey, "");
                return string.IsNullOrEmpty(v) ? DefaultSaveRoot : v;
            }
            set
            {
                if (string.IsNullOrEmpty(value) || SamePath(value, DefaultSaveRoot)) EditorPrefs.DeleteKey(SaveRootPrefKey);
                else EditorPrefs.SetString(SaveRootPrefKey, value);
            }
        }

        public static async Task<string> CaptureAndSaveAsync(int frames = 60, bool drawList = true)
        {
            FrameCaptureResult result = await FrameCapture.CaptureAsync(new FrameCaptureOptions { Frames = frames, DrawList = drawList });
            return Save(result);
        }

        /// <summary>SaveRoot 아래 <c>yyyyMMdd_HHmmss_씬</c> 폴더에 report.md · report.json · png를 쓰고 그 폴더 경로를 돌려준다.</summary>
        public static string Save(FrameCaptureResult result, string saveRoot = null)
        {
            if (result?.Report == null) throw new ArgumentNullException(nameof(result));

            string root = string.IsNullOrEmpty(saveRoot) ? SaveRoot : saveRoot;
            string folder = UniqueFolder(root, FolderName(result.Report.meta));
            Directory.CreateDirectory(folder);

            bool hasOverdraw = result.OverdrawPng != null;
            bool hasScreen = result.ScreenPng != null;
            if (hasOverdraw) File.WriteAllBytes(Path.Combine(folder, ReportWriter.OverdrawFile), result.OverdrawPng);
            if (hasScreen) File.WriteAllBytes(Path.Combine(folder, ReportWriter.ScreenFile), result.ScreenPng);
            File.WriteAllText(Path.Combine(folder, ReportWriter.JsonFile), ReportWriter.ToJson(result.Report), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, ReportWriter.MarkdownFile), ReportWriter.ToMarkdown(result.Report, hasOverdraw, hasScreen), new UTF8Encoding(false));
            return folder;
        }

        /// <summary>
        /// 다른 AI(웹 채팅 등)에 붙여 넣을 글: report.md 본문 + 같이 첨부할 그림 경로.
        /// 경로만으로는 다른 PC · 웹 AI가 못 읽으므로 본문을 통째로 넣는다.
        /// </summary>
        public static string BuildAiClipboardText(string savedFolder)
        {
            string md = File.ReadAllText(Path.Combine(savedFolder, ReportWriter.MarkdownFile));
            string[] images = new[] { ReportWriter.OverdrawFile, ReportWriter.ScreenFile }
                .Select(f => Path.Combine(savedFolder, f))
                .Where(File.Exists)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("아래는 Unity 에디터에서 뽑은 프레임 리포트다. 병목과 개선 방향을 분석해 줘.");
            sb.AppendLine();
            sb.Append(md);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"리포트 폴더: {savedFolder}");
            if (images.Length > 0)
            {
                sb.AppendLine("본문의 그림은 아래 파일이다 (웹 AI에는 직접 첨부):");
                foreach (string img in images) sb.AppendLine($"- {img}");
            }
            return sb.ToString();
        }

        // 폴더 선택 창은 '/'를, Path.Combine은 '\'를 쓰고 Windows 경로는 대소문자를 가리지 않는다.
        static bool SamePath(string a, string b)
        {
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        static string FolderName(ReportMeta meta)
        {
            string scene = string.IsNullOrEmpty(meta.scene) ? "Untitled" : meta.scene;
            foreach (char c in Path.GetInvalidFileNameChars()) scene = scene.Replace(c, '_');
            DateTime t = DateTime.TryParseExact(meta.capturedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                ? parsed
                : DateTime.Now;
            return $"{t:yyyyMMdd_HHmmss}_{scene}";
        }

        static string UniqueFolder(string root, string name)
        {
            string path = Path.Combine(root, name);
            for (int i = 2; Directory.Exists(path); i++)
                path = Path.Combine(root, $"{name}_{i}");
            return path;
        }
    }
}
