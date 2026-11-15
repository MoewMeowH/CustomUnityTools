using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.FrameInspector.Editor
{
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public sealed class FrameInspectorWindow : EditorWindow
    {
        static readonly string[] Modes = { "캡처 리포트", "전후 비교" };
        static readonly string[] Tabs = { "요약", "오버드로우", "GPU 패스", "드로우 · 셰이더", "CPU", "렌더 그래프" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Worse = new Color(0.95f, 0.45f, 0.4f);
        static readonly Color Better = new Color(0.45f, 0.85f, 0.5f);

        [SerializeField] int _mode;
        [SerializeField] int _frames = 60;
        [SerializeField] bool _drawList = true;
        [SerializeField] bool _recordProfiler;
        [SerializeField] int _tab;
        [SerializeField] string _baselineFolder;
        [SerializeField] string _currentFolder;

        ComparisonReport _comparison;
        List<string> _violations;
        string _compareFolder;
        Vector2 _compareScroll;

        Camera _camera;
        FrameCaptureResult _result;
        string _savedFolder;
        string _status = "";
        bool _busy;
        Texture2D _overdrawTex;
        Texture2D _screenTex;
        Vector2 _scroll;

        [MenuItem("Tools/HB/Frame Inspector")]
        public static void Open() => GetWindow<FrameInspectorWindow>("Frame Inspector");

        void OnDisable()
        {
            ReleaseTextures();
            DiscardUnsavedProfilerFile();
        }

        // 저장하지 않은 Profiler 녹화 임시 파일(수십 MB)은 결과를 바꾸거나 창을 닫을 때 지운다.
        void DiscardUnsavedProfilerFile()
        {
            string path = _result?.ProfilerTempFile;
            if (path != null && ProfilerCapture.IsTempFile(path) && System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        void OnGUI()
        {
            _mode = GUILayout.Toolbar(_mode, Modes);
            if (_mode == 1)
            {
                DrawCompare();
                return;
            }

            DrawToolbar();
            DrawStatus();
            if (_result == null) return;

            _tab = GUILayout.Toolbar(_tab, Tabs);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(labelWidth, 230);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawSummary(_result.Report); break;
                case 1: DrawOverdraw(_result.Report.overdraw); break;
                case 2: DrawGpu(_result.Report); break;
                case 3: DrawDrawsAndShaders(_result.Report); break;
                case 4: DrawCpu(_result.Report); break;
                case 5: DrawRenderGraph(_result.Report.renderGraph); break;
            }
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = labelWidth;
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(_busy || !EditorApplication.isPlaying))
                {
                    if (GUILayout.Button(_busy ? "캡처 중…" : "캡처", GUILayout.Width(90), GUILayout.Height(28)))
                        Capture();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _frames = Mathf.Clamp(EditorGUILayout.IntField("프레임 수", _frames), 1, 600);
                        _drawList = EditorGUILayout.ToggleLeft(new GUIContent("드로우 목록", "Frame Debugger로 드로우 목록을 읽는다. 읽는 동안 Play가 잠깐 일시정지되고 Frame Debugger 창이 열렸다 닫힌다."), _drawList, GUILayout.Width(100));
                        _recordProfiler = EditorGUILayout.ToggleLeft(new GUIContent("Profiler 녹화", "측정이 끝난 뒤 Profiler를 녹화해 profiler.data로 저장한다 (최대 300프레임). Profiler 창의 기존 기록이 지워진다."), _recordProfiler, GUILayout.Width(110));
                    }
                    _camera = (Camera)EditorGUILayout.ObjectField(new GUIContent("카메라", "비우면 Camera.main"), _camera, typeof(Camera), true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_result == null || _busy))
                {
                    if (GUILayout.Button("저장")) SaveCurrent();
                    if (GUILayout.Button(new GUIContent("AI용 복사", "저장한 뒤 report.md 본문과 그림 경로를 클립보드에 넣는다"))) CopyForAi();
                }
                if (GUILayout.Button("폴더 열기"))
                    EditorUtility.RevealInFinder(_savedFolder ?? FrameInspectorApi.SaveRoot);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("저장 위치", FrameInspectorApi.SaveRoot, EditorStyles.miniLabel);
                if (GUILayout.Button("변경", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    string picked = EditorUtility.OpenFolderPanel("리포트 저장 위치", FrameInspectorApi.SaveRoot, "");
                    if (!string.IsNullOrEmpty(picked)) FrameInspectorApi.SaveRoot = picked;
                    // 모달 창 뒤에 같은 이벤트로 레이아웃을 이어 가지 않는다
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("기본", EditorStyles.miniButton, GUILayout.Width(40)))
                    FrameInspectorApi.SaveRoot = null;
            }
        }

        void DrawStatus()
        {
            if (!EditorApplication.isPlaying && !_busy)
                EditorGUILayout.HelpBox("Play 모드에서 캡처한다. 게임을 실행한 뒤 보고 싶은 장면에서 [캡처]를 누른다.", MessageType.Info);
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        async void Capture()
        {
            _busy = true;
            _status = "캡처 시작";
            try
            {
                // Progress<T>는 메시지를 나중에 전달해 완료 문구를 덮어쓰므로 바로 반영하는 구현을 쓴다.
                var progress = new ImmediateProgress(s => { _status = s; if (this != null) Repaint(); });
                FrameCaptureResult result = await FrameCapture.CaptureAsync(
                    new FrameCaptureOptions { Frames = _frames, DrawList = _drawList, Camera = _camera, RecordProfiler = _recordProfiler }, progress);
                // 캡처 중에 창을 닫았으면 텍스처를 만들지 않는다 (OnDisable이 이미 지나가 해제할 곳이 없음).
                if (this == null) return;
                DiscardUnsavedProfilerFile();
                _result = result;
                _savedFolder = null;
                LoadTextures();
                _status = $"캡처 끝 ({_result.Report.meta.capturedAt}). 저장하려면 [저장].";
            }
            catch (Exception e)
            {
                _status = "캡처 실패: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                if (this != null) Repaint();
            }
        }

        sealed class ImmediateProgress : IProgress<string>
        {
            readonly Action<string> _onReport;
            public ImmediateProgress(Action<string> onReport) => _onReport = onReport;
            public void Report(string value) => _onReport(value);
        }

        void SaveCurrent()
        {
            _savedFolder = FrameInspectorApi.Save(_result);
            _status = "저장함: " + _savedFolder;
        }

        void CopyForAi()
        {
            if (_savedFolder == null) SaveCurrent();
            EditorGUIUtility.systemCopyBuffer = FrameInspectorApi.BuildAiClipboardText(_savedFolder);
            _status = "AI용 글을 클립보드에 넣음 (그림은 따로 첨부): " + _savedFolder;
        }

        void LoadTextures()
        {
            ReleaseTextures();
            _overdrawTex = ToTexture(_result.OverdrawPng);
            _screenTex = ToTexture(_result.ScreenPng);
        }

        static Texture2D ToTexture(byte[] png)
        {
            if (png == null) return null;
            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            tex.LoadImage(png);
            return tex;
        }

        void ReleaseTextures()
        {
            if (_overdrawTex != null) DestroyImmediate(_overdrawTex);
            if (_screenTex != null) DestroyImmediate(_screenTex);
            _overdrawTex = _screenTex = null;
        }

        // ---- 탭 ----

        static void DrawSummary(FrameReport r)
        {
            ReportMeta m = r.meta;
            Header("병목 힌트");
            EditorGUILayout.LabelField(r.bottleneck.side, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(r.bottleneck.reason, EditorStyles.wordWrappedLabel);

            Header("프레임 시간 (ms)  평균 / 최대 / 최소");
            StatLine("CPU 프레임", r.timing.cpuFrameMs);
            StatLine("CPU 메인 스레드", r.timing.cpuMainThreadMs);
            StatLine("CPU 렌더 스레드", r.timing.cpuRenderThreadMs);
            StatLine("GPU 프레임", r.timing.gpuFrameMs);
            EditorGUILayout.LabelField("GPU 출처: " + r.timing.gpuSource, EditorStyles.miniLabel);

            Header("렌더링 · 메모리 카운터  평균 / 최대");
            foreach (ValueStat c in r.counters)
                Row(c.name, $"{ReportMath.FormatCounter(c.avg, c.unit)} / {ReportMath.FormatCounter(c.max, c.unit)}");

            Header("Profiler 녹화");
            Row("profiler.data", r.profiler.recorded ? $"{r.profiler.frames}프레임 ({(string.IsNullOrEmpty(r.profiler.file) ? "저장하면 리포트 폴더에 생김" : r.profiler.file)})" : r.profiler.unavailableReason);

            Header("측정 조건");
            Row("Unity · 파이프라인", $"{m.unityVersion} · {m.renderPipeline}");
            Row("그래픽", $"{m.graphicsApi} · {m.gpuName}");
            Row("씬 · 카메라", $"{(string.IsNullOrEmpty(m.scene) ? "(저장 안 된 씬)" : m.scene)} · {m.camera}");
            Row("해상도 · 프레임", $"{m.screenWidth}×{m.screenHeight} · {m.sampledFrames}프레임");
            if (m.batchMode) Row("배치 모드", "예 — GPU · 렌더링 카운터 · 드로우 목록 측정 안 됨");

            Header("참고 · 한계");
            foreach (string n in r.notes) EditorGUILayout.LabelField("• " + n, EditorStyles.wordWrappedMiniLabel);
        }

        void DrawOverdraw(OverdrawReport o)
        {
            if (!o.available)
            {
                EditorGUILayout.HelpBox("오버드로우 없음: " + o.unavailableReason, MessageType.Warning);
                DrawImage(_screenTex, "화면");
                return;
            }

            Header($"오버드로우 ({o.width}×{o.height}, {o.maxDisplayedCount}회 이상은 {o.maxDisplayedCount}회)");
            if (o.excludedBottomRows > 0)
                EditorGUILayout.LabelField($"색 히트맵에서 되돌림 — 범례 자리 아래쪽 {o.excludedBottomRows}px는 통계에서 뺌 (그림에서 회색)", EditorStyles.wordWrappedMiniLabel);
            Row("", "픽셀당 평균 · 덮인 픽셀당 평균 · 덮인 % · 3회+ % · 5회+ % · 최대+ %");
            OverdrawLine("불투명 + 반투명", o.all);

            Header("히스토그램 (겹친 횟수별 픽셀 %)");
            float maxPct = Mathf.Max(0.0001f, o.all.histogramPercent.DefaultIfEmpty(0f).Max());
            for (int i = 0; i < o.all.histogramPercent.Count; i++)
            {
                float pct = o.all.histogramPercent[i];
                Bar($"{i}{(i == o.maxDisplayedCount ? "+" : "")}", pct / maxPct, pct.ToString("F1", Inv) + " %", OverdrawMath.ToColor(i, o.maxDisplayedCount));
            }

            DrawImage(_overdrawTex, "오버드로우 (검정 0 → 파랑 1 → 빨강 → 하양 최대)");
            DrawImage(_screenTex, "같은 카메라 일반 화면");
        }

        static void DrawGpu(FrameReport r)
        {
            Header("렌더 패스 GPU 시간 (ms, 중첩됨 — 더하지 말 것)");
            DrawBars(r.gpuPasses, 4);
            if (r.editorGpu.Count > 0)
            {
                Header("에디터 창 그리기 GPU 시간 (게임과 무관)");
                DrawBars(r.editorGpu, 4);
            }
        }

        static void DrawDrawsAndShaders(FrameReport r)
        {
            DrawListReport d = r.drawList;
            if (!d.available)
            {
                EditorGUILayout.HelpBox("드로우 목록 없음: " + d.unavailableReason, MessageType.Warning);
            }
            else
            {
                Header($"드로우 이벤트 {d.eventCount}개 — 패스별");
                foreach (CountEntry e in d.byPass) Row(e.name, e.count.ToString(Inv));
                Header("종류별");
                foreach (CountEntry e in d.byType) Row(e.name, e.count.ToString(Inv));
                if (d.byObject.Count > 0)
                {
                    Header("오브젝트별 (상위 30)");
                    foreach (CountEntry e in d.byObject) Row(e.name, e.count.ToString(Inv));
                }
            }

            Header("셰이더  드로우 · Renderer · 패스 · 반투명 · 큐 · 오류");
            foreach (ShaderStat s in r.shaders)
            {
                string queue = s.minRenderQueue == s.maxRenderQueue ? s.minRenderQueue.ToString(Inv) : $"{s.minRenderQueue}~{s.maxRenderQueue}";
                Row(s.shader, $"{s.drawCount} · {s.rendererCount} · {s.passCount} · {(s.transparent ? "반투명" : "불투명")} · {queue}{(s.hasErrors ? " · 오류" : "")}");
                if (s.keywords.Count > 0)
                    EditorGUILayout.LabelField("    " + string.Join(" ", s.keywords), EditorStyles.wordWrappedMiniLabel);
            }
        }

        static void DrawCpu(FrameReport r)
        {
            Header("CPU 주요 마커 (ms) — 함수 단위는 Profiler 녹화로");
            DrawBars(r.cpuMarkers, 3);
        }

        static void DrawRenderGraph(RenderGraphReport g)
        {
            if (!g.available)
            {
                EditorGUILayout.HelpBox("렌더 그래프 지표 없음: " + g.unavailableReason, MessageType.Warning);
                return;
            }

            foreach (RenderGraphExecution e in g.executions)
            {
                Header($"{e.graphName} / {e.executionName}");
                Row("패스 · 버려진 · 합쳐지지 못한", $"{e.totalPasses} · {e.culledPasses} · {e.unmergedPasses}");
                Row("리소스 · 텍스처 · 버퍼", $"{e.totalResources} · {e.textureCount} · {e.bufferCount}");
                foreach (string insight in e.insights)
                    EditorGUILayout.LabelField("• " + insight, EditorStyles.wordWrappedMiniLabel);
                foreach (RenderGraphUnmergedPass u in e.unmerged)
                    EditorGUILayout.LabelField($"{u.name} → {u.nextPassName}: {u.breakReason}", EditorStyles.wordWrappedMiniLabel);
                if (e.culled.Count > 0)
                    EditorGUILayout.LabelField("버려진 패스: " + string.Join(", ", e.culled), EditorStyles.wordWrappedMiniLabel);
            }
        }

        // ---- 전후 비교 ----

        void DrawCompare()
        {
            EditorGUILayout.HelpBox("저장된 리포트 두 개(이전 · 이후)를 지표별로 비교한다. 모든 지표는 낮을수록 좋다.", MessageType.None);
            FolderField("이전 (기준)", ref _baselineFolder);
            FolderField("이후", ref _currentFolder);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("최근 저장 두 개"))
                {
                    string[] recent = FrameInspectorApi.ListReportFolders();
                    if (recent.Length >= 2)
                    {
                        _currentFolder = recent[0];
                        _baselineFolder = recent[1];
                    }
                    else _status = "저장 위치에 리포트가 두 개 이상 없다: " + FrameInspectorApi.SaveRoot;
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_baselineFolder) || string.IsNullOrEmpty(_currentFolder)))
                {
                    if (GUILayout.Button("비교")) RunCompare();
                }
                using (new EditorGUI.DisabledScope(_comparison == null))
                {
                    if (GUILayout.Button("저장")) SaveCompare();
                    if (GUILayout.Button(new GUIContent("AI용 복사", "저장한 뒤 compare.md 본문을 클립보드에 넣는다")))
                    {
                        if (_compareFolder == null) SaveCompare();
                        EditorGUIUtility.systemCopyBuffer = FrameInspectorApi.BuildAiClipboardTextForComparison(_compareFolder);
                        _status = "AI용 비교 글을 클립보드에 넣음: " + _compareFolder;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);
            if (_comparison == null) return;

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(labelWidth, 230);
            _compareScroll = EditorGUILayout.BeginScrollView(_compareScroll);
            if (_comparison.conditionDiffs.Count > 0)
                EditorGUILayout.HelpBox("측정 조건이 다름:\n" + string.Join("\n", _comparison.conditionDiffs), MessageType.Warning);

            Header(_violations.Count == 0 ? "허용치 검사: 넘은 지표 없음" : $"허용치 검사: {_violations.Count}개 넘음");
            foreach (string v in _violations)
                EditorGUILayout.LabelField("• " + v, EditorStyles.wordWrappedMiniLabel);

            foreach (IGrouping<string, MetricDelta> group in _comparison.metrics.GroupBy(m => m.group))
            {
                Header(group.Key);
                foreach (MetricDelta d in group)
                {
                    if (!d.comparable)
                    {
                        Row(d.name, "비교 불가 — " + d.reason);
                        continue;
                    }

                    Color prev = GUI.contentColor;
                    if (d.direction > 0) GUI.contentColor = Worse;
                    else if (d.direction < 0) GUI.contentColor = Better;
                    string unit = d.unit == "bytes" ? "" : " " + ReportMath.UnitLabel(d.unit);
                    Row(d.name, $"{ReportMath.FormatCounter(d.baseline, d.unit, 3)} → {ReportMath.FormatCounter(d.current, d.unit, 3)}{unit}  ({(d.deltaPercent >= 0 ? "+" : "")}{d.deltaPercent.ToString("F1", Inv)}%)");
                    GUI.contentColor = prev;
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = labelWidth;
        }

        static void FolderField(string label, ref string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, string.IsNullOrEmpty(value) ? "(고르지 않음)" : System.IO.Path.GetFileName(value));
                if (GUILayout.Button("고르기", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    string picked = EditorUtility.OpenFolderPanel(label + " 리포트 폴더", string.IsNullOrEmpty(value) ? FrameInspectorApi.SaveRoot : value, "");
                    if (!string.IsNullOrEmpty(picked)) value = picked;
                    // 모달 창 뒤에 같은 이벤트로 레이아웃을 이어 가지 않는다
                    GUIUtility.ExitGUI();
                }
            }
        }

        void RunCompare()
        {
            try
            {
                _comparison = FrameReportComparer.Compare(
                    FrameInspectorApi.LoadReport(_baselineFolder), FrameInspectorApi.LoadReport(_currentFolder),
                    System.IO.Path.GetFileName(_baselineFolder), System.IO.Path.GetFileName(_currentFolder));
                _violations = FrameReportComparer.Check(_comparison);
                _compareFolder = null;
                _status = $"비교 끝: {_comparison.metrics.Count(m => m.direction > 0)}개 나빠짐 · {_comparison.metrics.Count(m => m.direction < 0)}개 좋아짐";
            }
            catch (Exception e)
            {
                _comparison = null;
                _status = "비교 실패: " + e.Message;
            }
        }

        void SaveCompare()
        {
            _compareFolder = FrameInspectorApi.SaveComparison(_comparison);
            _status = "저장함: " + _compareFolder;
        }

        // ---- 그리기 도우미 ----

        static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        static void Row(string label, string value) => EditorGUILayout.LabelField(label, value);

        static void StatLine(string label, Stat s) =>
            Row(label, s.available ? $"{s.avg.ToString("F2", Inv)} / {s.max.ToString("F2", Inv)} / {s.min.ToString("F2", Inv)}" : "없음");

        static void OverdrawLine(string label, OverdrawStats s) =>
            Row(label, string.Join(" · ", new[] { s.avgPerPixel, s.avgPerCoveredPixel, s.coveredPercent, s.atLeast3Percent, s.atLeast5Percent, s.atMaxPercent }.Select(v => v.ToString("F2", Inv))));

        static void DrawBars(List<ValueStat> stats, int digits)
        {
            if (stats.Count == 0)
            {
                EditorGUILayout.LabelField("(없음 — 요약 탭의 참고 · 한계 참고)");
                return;
            }
            float max = Mathf.Max(0.0001f, stats.Max(s => s.avg));
            foreach (ValueStat s in stats)
                Bar(s.name, s.avg / max, $"{s.avg.ToString("F" + digits, Inv)} (최대 {s.max.ToString("F" + digits, Inv)})", new Color(0.35f, 0.6f, 0.95f));
        }

        static void Bar(string label, float fraction, string value, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(rect.x, rect.y, Mathf.Min(320, rect.width * 0.45f), rect.height);
            Rect barArea = new Rect(labelRect.xMax + 4, rect.y + 2, rect.width - labelRect.width - 4, rect.height - 4);
            EditorGUI.LabelField(labelRect, new GUIContent(label, label));
            EditorGUI.DrawRect(new Rect(barArea.x, barArea.y, barArea.width * Mathf.Clamp01(fraction), barArea.height), color);
            EditorGUI.LabelField(barArea, value, EditorStyles.miniLabel);
        }

        void DrawImage(Texture2D tex, string caption)
        {
            if (tex == null) return;
            Header(caption);
            float w = Mathf.Min(position.width - 24, tex.width);
            Rect rect = GUILayoutUtility.GetRect(w, w * tex.height / tex.width, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }
    }
}
