using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HB.FrameInspector.Editor
{
    /// <summary>
    /// Profiler를 N프레임 녹화해 .data 파일로 저장한다 (Unity Profiler의 Load · com.unity.pipeline diagnostics profiler_session으로 연다).
    /// SaveProfile은 Profiler 기록 전체를 쓰므로 녹화 전에 기존 기록을 지운다 (Pipeline profiler_record와 같은 방식).
    /// </summary>
    internal static class ProfilerCapture
    {
        // ProfilerConnection::kEditorGuid — Profiler가 이 에디터 자신에 붙어 있을 때의 connectedProfiler 값
        const int EditorConnection = -1;
        const string TempPrefix = "HBFrameInspector_";
        public const int MaxFrames = 300;

        /// <summary>녹화가 만든 임시 파일인지 (시스템 임시 폴더 바로 아래 · 이름 접두사). 저장 위치가 임시 폴더 아래여도 저장본과 헷갈리지 않게 이름으로 가린다.</summary>
        public static bool IsTempFile(string path) =>
            Path.GetFileName(path).StartsWith(TempPrefix, StringComparison.Ordinal)
            && string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        internal sealed class Output
        {
            public ProfilerReport Report = new ProfilerReport();
            public string TempFile;
        }

        public static async Task<Output> RecordAsync(int frames, Func<Task> nextFrame)
        {
            var output = new Output();
            if (ProfilerDriver.connectedProfiler != EditorConnection)
            {
                output.Report.unavailableReason = "Profiler가 이 에디터가 아닌 대상(연결된 기기 등)에 붙어 있어 녹화하지 않음";
                return output;
            }

            frames = Mathf.Clamp(frames, 1, MaxFrames);
            bool prevEnabled = ProfilerDriver.enabled;
            bool prevProfileEditor = ProfilerDriver.profileEditor;
            string path = Path.Combine(Path.GetTempPath(), $"{TempPrefix}{Guid.NewGuid():N}.data");
            bool completed = false;
            try
            {
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.enabled = true;
                for (int i = 0; i < frames; i++)
                    await nextFrame();
                completed = true;
            }
            finally
            {
                ProfilerDriver.enabled = false;
                bool saved = completed && ProfilerDriver.lastFrameIndex >= 0 && ProfilerDriver.SaveProfile(path) && File.Exists(path);
                // 이전 상태로 되돌리면 Profiler가 다시 프레임을 쌓을 수 있어 저장한 프레임 수는 그 전에 읽는다.
                int savedFrames = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;
                ProfilerDriver.enabled = prevEnabled;
                ProfilerDriver.profileEditor = prevProfileEditor;
                if (saved)
                {
                    output.TempFile = path;
                    output.Report.recorded = true;
                    output.Report.frames = savedFrames;
                }
            }

            if (!output.Report.recorded)
                output.Report.unavailableReason = "ProfilerDriver.SaveProfile 실패 (에디터 로그 확인)";
            return output;
        }
    }
}
