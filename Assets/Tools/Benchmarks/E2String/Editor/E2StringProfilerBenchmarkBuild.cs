using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HB.E2String.Benchmarks.Editor
{
    /// <summary>
    /// E2String 벤치마크 실행/빌드 진입점. 벤치마크 어셈블리는 E2S_BENCHMARK define이 있는 빌드에만 포함된다.
    /// - 메뉴: Tools/HB/E2String Benchmark/*
    /// - 배치: -executeMethod HB.E2String.Benchmarks.Editor.E2StringProfilerBenchmarkBuild.{BuildIl2CppDevelopment|BuildIl2CppRelease|BuildCoreClrDevelopment|BuildCoreClrRelease|MeasureIl2CppSizeVariants}
    /// </summary>
    public static class E2StringProfilerBenchmarkBuild
    {
        private const string MENU_ROOT = "Tools/HB/E2String Benchmark/";
        private const string SCENE_PATH = "Assets/Tools/Benchmarks/E2String/E2StringBenchmark.unity";
        private const string BUILD_ROOT = "Builds/E2StringBenchmark";
        private const string EXE_NAME = "E2StringBenchmark.exe";
        private const string BENCHMARK_DEFINE = "E2S_BENCHMARK";
        private const string SIZE_RESULT_PATH = "Temp/E2StringIl2CppSize.csv";
        private const string SIZE_LOW_RESULT_PATH = "Temp/E2StringIl2CppSize_StrippingLow.csv";
        private const string SIZE_MEDIUM_RESULT_PATH = "Temp/E2StringIl2CppSize_StrippingMedium.csv";
        private const string SIZE_HIGH_RESULT_PATH = "Temp/E2StringIl2CppSize_StrippingHigh.csv";

        private const string SPAWN_ON_PLAY_KEY = "HB.E2String.Benchmark.SpawnOnPlay";

        #region 에디터 실행

        /// <summary>현재 씬을 그대로 둔 채 Play 모드에 들어가 벤치마크 오브젝트만 추가한다. 끝나면 Play 모드를 자동으로 종료한다.</summary>
        [MenuItem(MENU_ROOT + "Run in Editor (Play Mode)")]
        private static void RunInEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 도메인 리로드 후에도 유지되도록 SessionState에 요청을 남긴다
            SessionState.SetBool(SPAWN_ON_PLAY_KEY, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SPAWN_ON_PLAY_KEY, false)) return;

            SessionState.SetBool(SPAWN_ON_PLAY_KEY, false);
            E2StringProfilerBenchmark.Completed += OnBenchmarkCompleted;
            E2StringProfilerBenchmark.Spawn();
        }

        private static void OnBenchmarkCompleted(string resultPath)
        {
            E2StringProfilerBenchmark.Completed -= OnBenchmarkCompleted;
            EditorApplication.ExitPlaymode();
        }

        #endregion

        #region 성능 측정 빌드

        [MenuItem(MENU_ROOT + "Build IL2CPP Development Player")]
        public static void BuildIl2CppDevelopment() => BuildAndExit(ScriptingImplementation.IL2CPP, true);

        [MenuItem(MENU_ROOT + "Build IL2CPP Release Player")]
        public static void BuildIl2CppRelease() => BuildAndExit(ScriptingImplementation.IL2CPP, false);

        [MenuItem(MENU_ROOT + "Build CoreCLR Development Player")]
        public static void BuildCoreClrDevelopment() => BuildAndExit(ScriptingImplementation.CoreCLR, true);

        [MenuItem(MENU_ROOT + "Build CoreCLR Release Player")]
        public static void BuildCoreClrRelease() => BuildAndExit(ScriptingImplementation.CoreCLR, false);

        private static void BuildAndExit(ScriptingImplementation backend, bool development)
        {
            string output = $"{BUILD_ROOT}_{backend}_{(development ? "Dev" : "Release")}/{EXE_NAME}";
            var report = WithBackend(backend, () => BuildPlayer(output, development, new[] { BENCHMARK_DEFINE }, false));
            ExitIfBatchMode(report.summary.result == BuildResult.Succeeded);
        }

        #endregion

        #region IL2CPP 크기 측정

        private struct SizeVariant
        {
            public string Name;
            public string[] Defines;
        }

        private static readonly SizeVariant[] s_SizeVariants =
        {
            new SizeVariant { Name = "None", Defines = new string[0] },
            new SizeVariant { Name = "Default_Unused", Defines = new[] { "E2S_SIZE_DEFAULT" } },
            new SizeVariant { Name = "All_Unused", Defines = new[] { "E2S_SIZE_ALL" } },
            new SizeVariant { Name = "Default_Used", Defines = new[] { "E2S_SIZE_DEFAULT", "E2S_SIZE_USED" } },
            new SizeVariant { Name = "All_Used", Defines = new[] { "E2S_SIZE_ALL", "E2S_SIZE_USED" } },
        };

        /// <summary>
        /// 100 멤버 enum 50개(SizeProbe)를 E2String 없음 / 기본 / All, 미사용 / 사용으로 나눠 IL2CPP Release 클린 빌드하고
        /// GameAssembly.dll · global-metadata.dat 크기와 빌드 시간을 기록한다. 빌드 5회라 오래 걸린다.
        /// 스트리핑은 프로젝트 설정을 그대로 쓴다.
        /// </summary>
        [MenuItem(MENU_ROOT + "Measure IL2CPP Size Variants (5 clean builds)")]
        public static void MeasureIl2CppSizeVariants()
        {
            MeasureSizeVariants(null, s_SizeVariants, SIZE_RESULT_PATH);
        }

        /// <summary>스트리핑 Low에서 쓰지 않는 생성 메서드가 빠지는지 확인한다 (없음 / 기본 미사용 / 기본 사용, 빌드 3회).</summary>
        [MenuItem(MENU_ROOT + "Measure IL2CPP Size Variants (Stripping Low, 3 clean builds)")]
        public static void MeasureIl2CppSizeVariantsStrippingLow()
        {
            var variants = s_SizeVariants.Where(v => v.Name == "None" || v.Name.StartsWith("Default")).ToArray();
            MeasureSizeVariants(ManagedStrippingLevel.Low, variants, SIZE_LOW_RESULT_PATH);
        }

        /// <summary>
        /// 스트리핑 Medium에서 쓰지 않는 생성 메서드가 빠지는지와, 사용 시 E2String만의 비용(Default_Used − None_Used)을 본다
        /// (없음 / 없음·enum 사용 / 기본 미사용 / 기본 사용, 빌드 4회).
        /// </summary>
        [MenuItem(MENU_ROOT + "Measure IL2CPP Size Variants (Stripping Medium, 4 clean builds)")]
        public static void MeasureIl2CppSizeVariantsStrippingMedium()
        {
            MeasureSizeVariants(ManagedStrippingLevel.Medium, StrippingCompareVariants(), SIZE_MEDIUM_RESULT_PATH);
        }

        /// <summary>스트리핑 High에서 Medium과 같은 4개 변형을 빌드한다.</summary>
        [MenuItem(MENU_ROOT + "Measure IL2CPP Size Variants (Stripping High, 4 clean builds)")]
        public static void MeasureIl2CppSizeVariantsStrippingHigh()
        {
            MeasureSizeVariants(ManagedStrippingLevel.High, StrippingCompareVariants(), SIZE_HIGH_RESULT_PATH);
        }

        private static SizeVariant[] StrippingCompareVariants()
        {
            var variants = s_SizeVariants.Where(v => v.Name == "None" || v.Name.StartsWith("Default")).ToList();
            // E2String 없이 같은 값을 기존 API로 쓰는 변형 (enum 자체 + 호출 코드 몫을 빼기 위함, SizeProbe Touch() 참고)
            variants.Insert(1, new SizeVariant { Name = "None_Used", Defines = new[] { "E2S_SIZE_USED" } });
            return variants.ToArray();
        }

        private static void MeasureSizeVariants(ManagedStrippingLevel? stripping, SizeVariant[] variants, string resultPath)
        {
            var target = NamedBuildTarget.Standalone;
            var previousStripping = PlayerSettings.GetManagedStrippingLevel(target);
            var effectiveStripping = stripping ?? previousStripping;

            var sb = new StringBuilder();
            sb.AppendLine($"# unity={Application.unityVersion}, stripping={effectiveStripping}, config=Release, enums=50x100");
            sb.AppendLine("variant,result,game_assembly_bytes,global_metadata_bytes,build_seconds");

            bool allSucceeded = true;
            bool switchStripping = effectiveStripping != previousStripping;
            try
            {
                if (switchStripping)
                    PlayerSettings.SetManagedStrippingLevel(target, effectiveStripping);
                WithBackend(ScriptingImplementation.IL2CPP, () =>
                {
                    foreach (var variant in variants)
                    {
                        string dir = $"{BUILD_ROOT}_Size_{effectiveStripping}/{variant.Name}";
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);

                        var defines = new[] { BENCHMARK_DEFINE }.Concat(variant.Defines).ToArray();
                        var report = BuildPlayer($"{dir}/{EXE_NAME}", false, defines, true);
                        allSucceeded &= report.summary.result == BuildResult.Succeeded;

                        long gameAssembly = FileSize($"{dir}/GameAssembly.dll");
                        long metadata = FileSize($"{dir}/E2StringBenchmark_Data/il2cpp_data/Metadata/global-metadata.dat");
                        sb.AppendLine($"{variant.Name},{report.summary.result},{gameAssembly},{metadata},{report.summary.totalTime.TotalSeconds:F1}");
                    }
                    return null;
                });
            }
            finally
            {
                if (switchStripping)
                    PlayerSettings.SetManagedStrippingLevel(target, previousStripping);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, sb.ToString());
            Debug.Log($"[E2S Benchmark] IL2CPP 크기 측정 결과: {Path.GetFullPath(resultPath)}\n{sb}");
            ExitIfBatchMode(allSucceeded);
        }

        private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;

        #endregion

        #region 공통

        private static BuildReport BuildPlayer(string outputPath, bool development, string[] defines, bool cleanBuild)
        {
            EnsureScene();

            var options = development ? BuildOptions.Development : BuildOptions.None;
            if (cleanBuild) options |= BuildOptions.CleanBuildCache;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SCENE_PATH },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = outputPath,
                options = options,
                extraScriptingDefines = defines,
            });
            Debug.Log($"[E2S Benchmark] 빌드 결과: {report.summary.result}, 출력: {Path.GetFullPath(outputPath)}, 정의: {string.Join(";", defines)}");
            return report;
        }

        /// <summary>
        /// 스크립팅 백엔드를 잠시 바꿔 빌드하고 되돌린다. 이미 같은 백엔드면 설정을 건드리지 않는다 (ProjectSettings 재저장 방지).
        /// </summary>
        private static BuildReport WithBackend(ScriptingImplementation backend, Func<BuildReport> build)
        {
            var target = NamedBuildTarget.Standalone;
            var previous = PlayerSettings.GetScriptingBackend(target);
            bool switchBackend = previous != backend;
            try
            {
                if (switchBackend)
                    PlayerSettings.SetScriptingBackend(target, backend);
                return build();
            }
            finally
            {
                if (switchBackend)
                    PlayerSettings.SetScriptingBackend(target, previous);
            }
        }

        private static void ExitIfBatchMode(bool succeeded)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(succeeded ? 0 : 1);
        }

        /// <summary>
        /// 벤치마크 컴포넌트 하나만 있는 빌드용 씬을 만든다. 이미 있으면 그대로 둔다.
        /// 열려 있는 씬을 건드리지 않도록 Additive로 만들고 저장 후 바로 닫는다.
        /// </summary>
        private static void EnsureScene()
        {
            if (File.Exists(SCENE_PATH)) return;

            var previousActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // 새 씬을 활성화한 상태에서 만들어야 기존 씬이 dirty 표시되지 않는다
            SceneManager.SetActiveScene(scene);
            new GameObject(nameof(E2StringProfilerBenchmark), typeof(E2StringProfilerBenchmark));
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH) ?? throw new InvalidOperationException(SCENE_PATH));
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            EditorSceneManager.CloseScene(scene, true);
        }

        #endregion
    }
}
