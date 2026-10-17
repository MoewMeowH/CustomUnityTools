using System;
using System.IO;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// Pipeline 런타임 드라이버가 에디터 Play에서 생길지 설정 파일만 읽어 판단한다.
    /// cheat_play 시점에는 드라이버가 아직 없고(BeforeSceneLoad에 생김), Pipeline internal API에 기대지 않기 위해 파일만 본다.
    /// 형식은 com.unity.pipeline 0.8.0-exp.1 기준 (RuntimePipelineConfig: enableInBuilds 기본 false, autoStart 기본 true).
    /// </summary>
    internal static class PipelineRuntimeConfigProbe
    {
        public const string RelativePath = "ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json";

        public enum State
        {
            /// <summary>파일 없음 → Pipeline이 config를 null로 보고 드라이버를 만들지 않는다</summary>
            NoFile,
            /// <summary>enableInBuilds가 false → 드라이버 없음</summary>
            Disabled,
            /// <summary>enableInBuilds가 true → 드라이버가 생기고 runInBackground를 직접 켠다 (autoStart가 꺼져 있어도 코드 리로드 푸시에 켠다)</summary>
            DriverEnabled,
            /// <summary>파일은 있는데 읽거나 해석하지 못함</summary>
            Unreadable
        }

        public readonly struct Result
        {
            public readonly State State;
            public readonly bool? EnableInBuilds;
            public readonly bool? AutoStart;
            public readonly string Error;

            public Result(State state, bool? enableInBuilds = null, bool? autoStart = null, string error = null)
            {
                State = state;
                EnableInBuilds = enableInBuilds;
                AutoStart = autoStart;
                Error = error;
            }
        }

        // RuntimePipelineConfig의 두 필드와 같은 이름·기본값
        [Serializable]
        private sealed class Mirror
        {
            public bool enableInBuilds = false;
            public bool autoStart = true;
        }

        public static Result ProbeProject()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Probe(Path.Combine(projectRoot, RelativePath));
        }

        // RuntimePipelineConfig.Load와 같은 순서로 판정한다 (File.Exists → FromJsonOverwrite). 목적은 "드라이버가 생기는가"를 맞히는 것이라
        // Pipeline과 같게 둔다: 접근 권한이 없어 File.Exists가 false면 Pipeline도 config 없음, 빈 파일은 Pipeline도 기본값(enableInBuilds false)으로 드라이버를 만들지 않는다.
        public static Result Probe(string path)
        {
            if (!File.Exists(path)) return new Result(State.NoFile);

            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                return new Result(State.Unreadable, error: e.Message);
            }
        }

        /// <summary>JSON이 깨졌으면 예외 (호출부에서 Unreadable로 바꾼다)</summary>
        public static Result Parse(string json)
        {
            var mirror = new Mirror();
            JsonUtility.FromJsonOverwrite(json, mirror);
            return new Result(mirror.enableInBuilds ? State.DriverEnabled : State.Disabled, mirror.enableInBuilds, mirror.autoStart);
        }
    }
}
