using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>에셋 건강 검사 ID</summary>
    public static class AssetCheckIds
    {
        /// <summary>프리팹 [NeedSlotNullCheck] 빈 슬롯 · Missing 참조 + 프리팹 Missing 스크립트</summary>
        public const string PrefabSlots = "PrefabSlots";
        /// <summary>씬 [NeedSlotNullCheck] 빈 슬롯 · Missing 참조 + 씬 Missing 스크립트</summary>
        public const string Scenes = "Scenes";
        /// <summary>ScriptableObject [NeedSlotNullCheck] 빈 슬롯 · Missing 참조</summary>
        public const string ScriptableObjects = "ScriptableObjects";
        /// <summary>머티리얼 셰이더 없음 · 에러 셰이더(에러) · 현재 셰이더 텍스처 칸 Missing(경고)</summary>
        public const string Materials = "Materials";
        /// <summary>어트리뷰트 없는 직렬화 참조의 Missing (프리팹 · ScriptableObject · 씬, 내장 컴포넌트 포함, 경고)</summary>
        public const string MissingReferences = "MissingReferences";

        public static readonly IReadOnlyList<string> All = new[] { PrefabSlots, Scenes, ScriptableObjects, Materials, MissingReferences };

        /// <summary>기존 테스트(Validate_AllPrefabs…)가 맡는 PrefabSlots를 뺀 나머지</summary>
        public static readonly IReadOnlyList<string> AdditionalChecks = new[] { Scenes, ScriptableObjects, Materials, MissingReferences };

        public static bool IsValid(string checkId)
        {
            if (string.IsNullOrEmpty(checkId)) return false;
            foreach (string id in All)
            {
                if (id == checkId) return true;
            }
            return false;
        }
    }

    /// <summary>씬 검사 대상. 여럿을 함께 켤 수 있고, 합쳐서 같은 씬은 한 번만 검사한다.</summary>
    [Flags]
    public enum ESceneTargetSources
    {
        None = 0,
        /// <summary>Build Settings에서 켜진 씬</summary>
        BuildSettings = 1 << 0,
        /// <summary>지정 폴더 안의 씬 (폴더 목록이 비면 검사 폴더)</summary>
        Folders = 1 << 1,
        /// <summary>직접 고른 씬 (제외 폴더보다 우선)</summary>
        ExplicitScenes = 1 << 2,
    }

    /// <summary><see cref="AssetHealthEngine.Scan"/>에 넘기는 검사 설정. 프로젝트 설정은 <see cref="AssetHealthSettings.CreateScanOptions"/>로 만든다.</summary>
    [Serializable]
    public sealed class AssetHealthScanOptions
    {
        /// <summary>프리팹 · ScriptableObject · 머티리얼을 찾을 폴더 (Assets 안). 비어 있으면 대상 없음 — Assets 전체로 넓히지 않는다.</summary>
        public List<string> SearchFolders = new List<string> { "Assets" };
        /// <summary>제외 폴더 (직접 고른 씬 · OverrideScenePaths는 제외하지 않음)</summary>
        public List<string> ExcludeFolders = new List<string>();
        public List<string> EnabledChecks = new List<string> { AssetCheckIds.PrefabSlots };
        public ESceneTargetSources SceneSources = ESceneTargetSources.BuildSettings;
        /// <summary>ESceneTargetSources.Folders에서 찾을 폴더. 비면 SearchFolders</summary>
        public List<string> SceneFolders = new List<string>();
        public List<string> ExplicitScenePaths = new List<string>();
        /// <summary>null이 아니면 씬 대상은 이 목록만 쓴다 (빌드 스텝: 실제 빌드 씬). 비어 있으면 씬 대상 없음.</summary>
        public List<string> OverrideScenePaths;

        public AssetHealthScanOptions Clone()
        {
            return new AssetHealthScanOptions
            {
                SearchFolders = Copy(SearchFolders),
                ExcludeFolders = Copy(ExcludeFolders),
                EnabledChecks = Copy(EnabledChecks),
                SceneSources = SceneSources,
                SceneFolders = Copy(SceneFolders),
                ExplicitScenePaths = Copy(ExplicitScenePaths),
                OverrideScenePaths = OverrideScenePaths == null ? null : new List<string>(OverrideScenePaths),
            };
        }

        private static List<string> Copy(List<string> list) => list == null ? new List<string>() : new List<string>(list);
    }

    /// <summary>검사를 끝까지 하지 못한 이유 하나 (설정 오류 · 경로 없음 · 로드 실패 · PlayMode · 정리 실패 등)</summary>
    [Serializable]
    public sealed class AssetScanFailure
    {
        public string CheckId;
        public string AssetPath;
        public string Reason;

        public override string ToString()
        {
            string where = string.IsNullOrEmpty(AssetPath) ? string.Empty : $" {AssetPath}:";
            return $"[{CheckId ?? "공통"}]{where} {Reason}";
        }
    }

    public sealed class AssetHealthScanResult
    {
        public readonly List<SlotValidationIssue> Issues = new List<SlotValidationIssue>();
        public readonly List<AssetScanFailure> Failures = new List<AssetScanFailure>();
        public bool Canceled;
        /// <summary>실제로 연 파일 · 씬 수</summary>
        public int ScannedTargetCount;

        /// <summary>취소되지 않았고 실패가 하나도 없을 때만 true. false면 결과에 빠진 대상이 있다 — 테스트 · LIVE 빌드는 통과로 보지 않는다.</summary>
        public bool IsComplete => !Canceled && Failures.Count == 0;

        public int ErrorCount => Count(EValidationSeverity.Error);
        public int WarningCount => Count(EValidationSeverity.Warning);

        private int Count(EValidationSeverity severity)
        {
            int n = 0;
            foreach (var issue in Issues)
            {
                if (issue.Severity == severity) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// 에셋 건강 검사. 대상(프리팹 · ScriptableObject · 머티리얼 · 씬)을 먼저 확정한 뒤, 대상마다 한 번 열어 켜진 검사를 함께 돈다.
    /// <para>메인 스레드 전용, 검사 중 다시 부를 수 없다 (InvalidOperationException). 저장 · 임포트 때 자동으로 돌지 않는다.</para>
    /// <para>씬: 이미 열린 씬은 열린 상태 그대로 검사하고(저장 안 한 변경 포함) 닫거나 다시 불러오지 않는다. 검사기가 연 씬만 닫는다.
    /// 씬을 열면 그 씬의 [ExecuteAlways] · OnValidate 스크립트가 돈다.</para>
    /// </summary>
    public static class AssetHealthEngine
    {
        private static int s_MainThreadId = -1;
        private static bool s_Scanning;

        [InitializeOnLoadMethod]
        private static void RecordMainThread() => s_MainThreadId = Thread.CurrentThread.ManagedThreadId;

        public static AssetHealthScanResult Scan(AssetHealthScanOptions options, bool showProgress = false)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options), "프로젝트 설정으로 검사하려면 AssetHealthSettings.CreateScanOptions()를 넘기세요.");
            if (s_MainThreadId != -1 && Thread.CurrentThread.ManagedThreadId != s_MainThreadId)
                throw new InvalidOperationException("AssetHealthEngine.Scan은 메인 스레드에서만 부를 수 있습니다.");
            if (s_Scanning)
                throw new InvalidOperationException("AssetHealthEngine.Scan이 이미 실행 중입니다 (다시 부를 수 없음).");

            s_Scanning = true;
            var result = new AssetHealthScanResult();
            bool progress = showProgress && !Application.isBatchMode;
            try
            {
                new ScanRun(options.Clone(), result, progress).Execute();
            }
            finally
            {
                s_Scanning = false;
                if (progress) EditorUtility.ClearProgressBar();
            }
            return result;
        }

        /// <summary>한 번의 Scan 호출. 대상 · 원본 보고 기록은 이 호출 안에서만 쓴다.</summary>
        private sealed class ScanRun
        {
            private const int UnloadInterval = 100;
            private const int SceneUnloadInterval = 10; // 씬은 의존 에셋이 많아 더 자주 비운다
            private const int MaxSourceDepth = 10;
            private const string ErrorShaderName = "Hidden/InternalErrorShader";

            // Unity가 직접 저장하는 파일만 (모델 · 임포터가 만든 서브에셋은 고칠 수 없어 제외)
            private static readonly HashSet<string> NativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".prefab", ".asset", ".mat" };

            private readonly AssetHealthScanOptions m_Options;
            private readonly AssetHealthScanResult m_Result;
            private readonly bool m_Progress;
            private readonly HashSet<string> m_Checks = new HashSet<string>(StringComparer.Ordinal);
            private readonly SlotValidatorEngine.TypeScanCache m_Scan = new SlotValidatorEngine.TypeScanCache();

            // 프리팹 원본이 실제로 보고한 문제 (씬 안 프리팹 인스턴스에서 같은 문제를 다시 보고하지 않게)
            // 키 → 원본이 보고한 가장 높은 심각도 (원본의 경고가 씬의 에러를 가리지 않게)
            private readonly Dictionary<string, EValidationSeverity> m_ReportedBySource = new Dictionary<string, EValidationSeverity>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> m_ReportedMissingScripts = new Dictionary<string, int>(StringComparer.Ordinal);

            private HashSet<string> m_PrefabPaths = new HashSet<string>(StringComparer.Ordinal);
            private int m_LoadedSinceUnload;
            // 씬 인스턴스 대조에 쓸 때만 원본 보고를 기록한다 (씬을 검사하지 않으면 기록 비용을 쓰지 않음)
            private bool m_RecordSources;

            // 이번 호출 안에서만 쓰는 캐시. 에셋 참조를 잡고 있으면 언로드를 막으므로 언로드 전에 비운다
            private readonly Dictionary<Object, string> m_IdCache = new Dictionary<Object, string>();
            private readonly Dictionary<Shader, HashSet<string>> m_TextureNames = new Dictionary<Shader, HashSet<string>>();

            public ScanRun(AssetHealthScanOptions options, AssetHealthScanResult result, bool progress)
            {
                m_Options = options;
                m_Result = result;
                m_Progress = progress;
            }

            private bool On(string checkId) => m_Checks.Contains(checkId);

            public void Execute()
            {
                foreach (string id in m_Options.EnabledChecks)
                {
                    if (AssetCheckIds.IsValid(id)) m_Checks.Add(id);
                    else Fail(null, null, $"알 수 없는 검사 ID입니다: '{id ?? "(null)"}'");
                }
                if (m_Result.Failures.Count > 0 || m_Checks.Count == 0) return;

                var exclude = NormalizeFolders(m_Options.ExcludeFolders, validate: false);
                var search = NormalizeFolders(m_Options.SearchFolders, validate: true);

                var files = CollectFiles(search, exclude);
                var scenes = (On(AssetCheckIds.Scenes) || On(AssetCheckIds.MissingReferences)) ? CollectScenes(search, exclude) : new List<string>();

                // 원본 보고를 먼저 쌓도록 프리팹부터
                files.Sort((a, b) =>
                {
                    int pa = IsPrefab(a) ? 0 : 1, pb = IsPrefab(b) ? 0 : 1;
                    return pa != pb ? pa - pb : string.CompareOrdinal(a, b);
                });

                m_RecordSources = scenes.Count > 0;
                int total = files.Count + scenes.Count;
                for (int i = 0; i < files.Count; i++)
                {
                    if (Cancel($"{files[i]} ({i + 1}/{total})", i, total)) return;
                    ScanFile(files[i]);
                    UnloadPeriodically(UnloadInterval);
                }

                ScanScenes(scenes, files.Count, total);
            }

            // ───────── 대상 확정 ─────────

            private List<string> NormalizeFolders(List<string> folders, bool validate)
            {
                var result = new List<string>();
                foreach (string raw in folders)
                {
                    string folder = (raw ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
                    if (folder.Length == 0) continue;
                    if (validate && !AssetDatabase.IsValidFolder(folder))
                    {
                        Fail(null, folder, "검사 폴더가 프로젝트에 없습니다.");
                        continue;
                    }
                    if (!result.Contains(folder)) result.Add(folder);
                }
                return result;
            }

            private static bool IsUnder(string path, List<string> folders)
            {
                foreach (string folder in folders)
                {
                    if (path.Length > folder.Length && path[folder.Length] == '/' && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }

            private static bool IsPrefab(string path) => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

            private List<string> CollectFiles(List<string> search, List<string> exclude)
            {
                var paths = new HashSet<string>(StringComparer.Ordinal);
                if (search.Count == 0) return new List<string>();

                var filters = new List<string>();
                if (On(AssetCheckIds.PrefabSlots) || On(AssetCheckIds.MissingReferences)) filters.Add("t:Prefab");
                if (On(AssetCheckIds.ScriptableObjects) || On(AssetCheckIds.MissingReferences)) filters.Add("t:ScriptableObject");
                if (On(AssetCheckIds.Materials)) filters.Add("t:Material");

                string[] folders = search.ToArray();
                foreach (string filter in filters)
                {
                    foreach (string guid in AssetDatabase.FindAssets(filter, folders))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(path) || IsUnder(path, exclude)) continue;
                        if (!NativeExtensions.Contains(Path.GetExtension(path))) continue;
                        paths.Add(path);
                    }
                }

                // 중첩 · 변형 프리팹의 원본 판정은 제외까지 적용한 최종 프리팹 집합으로 한다
                foreach (string path in paths)
                {
                    if (IsPrefab(path)) m_PrefabPaths.Add(path);
                }
                return new List<string>(paths);
            }

            private List<string> CollectScenes(List<string> search, List<string> exclude)
            {
                var scenes = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                if (m_Options.OverrideScenePaths != null)
                {
                    // 실제 빌드 씬 목록: 제외 폴더를 적용하지 않는다
                    foreach (string path in m_Options.OverrideScenePaths) AddScene(path, scenes, seen);
                    return scenes;
                }

                var sources = m_Options.SceneSources;
                if ((sources & ESceneTargetSources.BuildSettings) != 0)
                {
                    foreach (var s in EditorBuildSettings.scenes)
                    {
                        if (s.enabled && !string.IsNullOrEmpty(s.path) && !IsUnder(s.path, exclude)) AddScene(s.path, scenes, seen);
                    }
                }
                if ((sources & ESceneTargetSources.Folders) != 0)
                {
                    var folders = m_Options.SceneFolders.Count > 0 ? NormalizeFolders(m_Options.SceneFolders, validate: true) : search;
                    if (folders.Count > 0)
                    {
                        foreach (string guid in AssetDatabase.FindAssets("t:Scene", folders.ToArray()))
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guid);
                            if (!IsUnder(path, exclude)) AddScene(path, scenes, seen);
                        }
                    }
                }
                if ((sources & ESceneTargetSources.ExplicitScenes) != 0)
                {
                    // 직접 고른 씬은 제외 폴더보다 우선
                    foreach (string path in m_Options.ExplicitScenePaths) AddScene(path, scenes, seen);
                }
                return scenes;
            }

            private void AddScene(string raw, List<string> scenes, HashSet<string> seen)
            {
                string path = (raw ?? string.Empty).Trim().Replace('\\', '/');
                if (path.Length == 0) return;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Fail(AssetCheckIds.Scenes, path, "씬 파일이 없습니다.");
                    return;
                }
                if (seen.Add(path)) scenes.Add(path);
            }

            // ───────── 파일 (프리팹 · ScriptableObject · 머티리얼) ─────────

            private void ScanFile(string path)
            {
                try
                {
                    Object main = AssetDatabase.LoadMainAssetAtPath(path);
                    // 서브에셋(SO · 머티리얼)을 보는 검사가 꺼져 있으면 파일 안 객체를 전부 불러오지 않는다
                    bool needSubAssets = On(AssetCheckIds.ScriptableObjects) || On(AssetCheckIds.Materials) || On(AssetCheckIds.MissingReferences);
                    Object[] objects = needSubAssets ? AssetDatabase.LoadAllAssetsAtPath(path) : new[] { main };
                    if (main == null || objects == null || objects.Length == 0)
                    {
                        Fail(null, path, "에셋을 불러오지 못했습니다.");
                        return;
                    }
                    m_Result.ScannedTargetCount++;

                    if (main is GameObject prefab && IsPrefab(path)) ScanPrefab(path, prefab);

                    foreach (Object obj in objects)
                    {
                        if (obj == null) continue;
                        bool isSub = obj != main;
                        if (obj is ScriptableObject so) ScanScriptableObject(path, so, isSub);
                        else if (obj is Material material && On(AssetCheckIds.Materials)) ScanMaterial(path, material, isSub);
                    }
                }
                catch (Exception e)
                {
                    Fail(null, path, $"검사 중 예외: {e.Message}");
                }
            }

            private void ScanPrefab(string path, GameObject prefab)
            {
                bool slots = On(AssetCheckIds.PrefabSlots);
                bool general = On(AssetCheckIds.MissingReferences);

                if (slots)
                {
                    SlotValidatorEngine.ScanMissingComponents(prefab, path, m_PrefabPaths, m_Result.Issues, new SlotValidatorEngine.MissingScriptHooks
                    {
                        Asset = prefab,
                        CheckId = AssetCheckIds.PrefabSlots,
                        OnReported = m_RecordSources ? new Action<GameObject, int>((go, count) => m_ReportedMissingScripts[Key(go)] = count) : null,
                    });
                }

                // 일반 Missing이 꺼져 있으면 슬롯 검사 대상(MonoBehaviour)만 모은다
                foreach (var component in CollectComponents(prefab, general))
                {
                    if (component == null) continue; // Missing 스크립트 (위에서 셈)

                    var type = component.GetType();
                    HashSet<string> owned = null;
                    if (slots && component is MonoBehaviour && m_Scan.HasTargetFields(type))
                    {
                        // 슬롯이 맡은 칸 목록은 일반 Missing이 같은 칸을 빼는 데만 쓴다
                        owned = general ? new HashSet<string>(StringComparer.Ordinal) : null;
                        var target = NewTarget(path, prefab, prefab, AssetCheckIds.PrefabSlots, SlotValidatorEngine.GetHierarchyPath(component.transform), type.Name, component, owned);
                        SlotValidatorEngine.ScanObjectSlots(component, type, target, m_PrefabPaths, m_Scan);
                    }

                    if (general)
                    {
                        var target = NewTarget(path, prefab, prefab, AssetCheckIds.MissingReferences, SlotValidatorEngine.GetHierarchyPath(component.transform), type.Name, component, null);
                        using (var source = SlotValidatorEngine.CreateScannedSourceObject(component, m_PrefabPaths))
                        {
                            target.SourceObject = source;
                            ScanGeneralMissing(component, target, owned);
                        }
                    }
                }
            }

            private void ScanScriptableObject(string path, ScriptableObject so, bool isSub)
            {
                var type = so.GetType();
                HashSet<string> owned = null;
                string subName = isSub ? $"{so.name} ({type.Name})" : null;

                if (On(AssetCheckIds.ScriptableObjects) && m_Scan.HasTargetFields(type))
                {
                    owned = On(AssetCheckIds.MissingReferences) ? new HashSet<string>(StringComparer.Ordinal) : null;
                    var target = NewTarget(path, null, so, AssetCheckIds.ScriptableObjects, so.name, type.Name, so, owned);
                    target.SubAssetName = subName;
                    SlotValidatorEngine.ScanObjectSlots(so, type, target, null, m_Scan);
                }

                if (On(AssetCheckIds.MissingReferences))
                {
                    var target = NewTarget(path, null, so, AssetCheckIds.MissingReferences, so.name, type.Name, so, null);
                    target.SubAssetName = subName;
                    ScanGeneralMissing(so, target, owned);
                }
            }

            private void ScanMaterial(string path, Material material, bool isSub)
            {
                string subName = isSub ? $"{material.name} (Material)" : null;

                using (var so = new SerializedObject(material))
                {
                    var shaderProp = so.FindProperty("m_Shader");
                    Shader shader = material.shader;
                    if ((shaderProp != null && SlotValidatorEngine.IsMissingReference(shaderProp)) || shader == null)
                    {
                        AddMaterialIssue(path, material, subName, "m_Shader", "셰이더가 없습니다 (삭제됨). 텍스처 칸은 검사하지 않았습니다.",
                            EValidationIssueType.MissingShader, EValidationSeverity.Error);
                        return;
                    }
                    if (shader.name == ErrorShaderName)
                    {
                        AddMaterialIssue(path, material, subName, "m_Shader", $"에러 셰이더({ErrorShaderName})를 씁니다.",
                            EValidationIssueType.ErrorShader, EValidationSeverity.Error);
                        return;
                    }
                    if (ShaderUtil.ShaderHasError(shader))
                    {
                        // 컴파일 에러는 플랫폼 · 그래픽스 API에 따라 다를 수 있어 경고만 (LIVE를 막지 않음)
                        AddMaterialIssue(path, material, subName, "m_Shader", $"셰이더({shader.name})에 컴파일 에러가 있습니다.",
                            EValidationIssueType.ShaderCompileError, EValidationSeverity.Warning);
                    }

                    // 현재 셰이더의 텍스처 속성만 (셰이더를 바꾸기 전에 남은 칸은 무시)
                    var textureNames = GetTextureNames(shader);
                    if (textureNames.Count == 0) return;

                    var texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                    if (texEnvs == null || !texEnvs.isArray)
                    {
                        Fail(AssetCheckIds.Materials, path, "머티리얼 텍스처 목록(m_SavedProperties.m_TexEnvs)을 읽지 못했습니다.");
                        return;
                    }
                    for (int i = 0; i < texEnvs.arraySize; i++)
                    {
                        var entry = texEnvs.GetArrayElementAtIndex(i);
                        var name = entry.FindPropertyRelative("first");
                        var texture = entry.FindPropertyRelative("second.m_Texture");
                        if (name == null || texture == null || !textureNames.Contains(name.stringValue)) continue;
                        if (SlotValidatorEngine.IsMissingReference(texture))
                        {
                            AddMaterialIssue(path, material, subName, name.stringValue, "연결된 텍스처가 삭제되었습니다 (Missing).",
                                EValidationIssueType.MissingReference, EValidationSeverity.Warning);
                        }
                    }
                }
            }

            private HashSet<string> GetTextureNames(Shader shader)
            {
                if (m_TextureNames.TryGetValue(shader, out var names)) return names;

                names = new HashSet<string>(StringComparer.Ordinal);
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (shader.GetPropertyType(i) == ShaderPropertyType.Texture) names.Add(shader.GetPropertyName(i));
                }
                m_TextureNames[shader] = names;
                return names;
            }

            private void AddMaterialIssue(string path, Material material, string subName, string field, string description, EValidationIssueType type, EValidationSeverity severity)
            {
                m_Result.Issues.Add(new SlotValidationIssue
                {
                    AssetPath = path,
                    GameObjectPath = material.name,
                    ComponentName = "Material",
                    FieldName = field,
                    Description = description,
                    IssueType = type,
                    Severity = severity,
                    CheckId = AssetCheckIds.Materials,
                    Asset = material,
                    SubAssetName = subName,
                });
            }

            // ───────── 씬 ─────────

            private void ScanScenes(List<string> scenes, int done, int total)
            {
                if (scenes.Count == 0) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Fail(AssetCheckIds.Scenes, null, "PlayMode(또는 전환 중)에는 씬을 검사하지 않습니다.");
                    return;
                }

                Scene initialActive = SceneManager.GetActiveScene();
                try
                {
                    for (int i = 0; i < scenes.Count; i++)
                    {
                        string path = scenes[i];
                        if (Cancel($"{path} ({done + i + 1}/{total})", done + i, total)) return;
                        ScanOneScene(path);
                        UnloadPeriodically(SceneUnloadInterval);
                    }
                }
                finally
                {
                    try
                    {
                        if (initialActive.IsValid() && initialActive.isLoaded && SceneManager.GetActiveScene() != initialActive)
                            SceneManager.SetActiveScene(initialActive);
                    }
                    catch (Exception e)
                    {
                        Fail(AssetCheckIds.Scenes, initialActive.path, $"활성 씬을 되돌리지 못했습니다: {e.Message}");
                    }
                }
            }

            private void ScanOneScene(string path)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool alreadyOpen = scene.IsValid();
                if (alreadyOpen && !scene.isLoaded)
                {
                    // 열린 씬은 다시 불러오지 않는다 → 검사하지 못함
                    Fail(AssetCheckIds.Scenes, path, "Hierarchy에 열려 있지만 로드되지 않은 씬이라 검사하지 않았습니다 (로드한 뒤 다시 검사).");
                    return;
                }

                bool openedByScanner = false;
                try
                {
                    if (!alreadyOpen)
                    {
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        if (!scene.IsValid() || !scene.isLoaded)
                        {
                            Fail(AssetCheckIds.Scenes, path, "씬을 열지 못했습니다.");
                            return;
                        }
                        openedByScanner = true;
                    }

                    m_Result.ScannedTargetCount++;
                    var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                    bool unsaved = alreadyOpen && scene.isDirty;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        ScanSceneRoot(path, sceneAsset, root, unsaved);
                    }
                }
                catch (Exception e)
                {
                    Fail(AssetCheckIds.Scenes, path, $"씬 검사 중 예외: {e.Message}");
                }
                finally
                {
                    // 검사기가 연 씬만 닫는다 (원래 열려 있던 씬은 그대로)
                    if (openedByScanner)
                    {
                        try
                        {
                            if (!EditorSceneManager.CloseScene(scene, true)) Fail(AssetCheckIds.Scenes, path, "검사하려고 연 씬을 닫지 못했습니다.");
                        }
                        catch (Exception e)
                        {
                            Fail(AssetCheckIds.Scenes, path, $"검사하려고 연 씬을 닫지 못했습니다: {e.Message}");
                        }
                    }
                }
            }

            private void ScanSceneRoot(string path, SceneAsset sceneAsset, GameObject root, bool unsaved)
            {
                bool slots = On(AssetCheckIds.Scenes);
                bool general = On(AssetCheckIds.MissingReferences);

                if (slots)
                {
                    SlotValidatorEngine.ScanMissingComponents(root, path, null, m_Result.Issues, new SlotValidatorEngine.MissingScriptHooks
                    {
                        Asset = sceneAsset,
                        CheckId = AssetCheckIds.Scenes,
                        IsUnsavedScene = unsaved,
                        ReportedBySource = ReportedMissingScriptsBySource,
                    });
                }

                foreach (var component in CollectComponents(root, general))
                {
                    if (component == null) continue;

                    var type = component.GetType();
                    var skip = SkipIfReportedBySource(component);
                    HashSet<string> owned = null;
                    if (slots && component is MonoBehaviour && m_Scan.HasTargetFields(type))
                    {
                        owned = general ? new HashSet<string>(StringComparer.Ordinal) : null;
                        var target = NewTarget(path, null, sceneAsset, AssetCheckIds.Scenes, SlotValidatorEngine.GetHierarchyPath(component.transform), type.Name, component, owned);
                        target.IsUnsavedScene = unsaved;
                        target.SkipReportedBySource = skip;
                        SlotValidatorEngine.ScanObjectSlots(component, type, target, null, m_Scan);
                    }

                    if (general)
                    {
                        var target = NewTarget(path, null, sceneAsset, AssetCheckIds.MissingReferences, SlotValidatorEngine.GetHierarchyPath(component.transform), type.Name, component, null);
                        target.IsUnsavedScene = unsaved;
                        target.SkipReportedBySource = skip;
                        ScanGeneralMissing(component, target, owned);
                    }
                }
            }

            // ───────── 일반 Missing 참조 ─────────

            /// <summary>
            /// 모든 직렬화 참조(숨김 포함)에서 Missing을 찾는다. [SerializeReference] 하위 · m_Script(Missing 스크립트 검사가 맡음) ·
            /// 슬롯 검사가 맡은 칸(owned)은 건너뛴다.
            /// </summary>
            private static void ScanGeneralMissing(Object obj, SlotValidatorEngine.ScanTarget target, HashSet<string> owned)
            {
                using (var so = new SerializedObject(obj))
                {
                    var it = so.GetIterator();
                    bool enterChildren = true;
                    while (it.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (it.propertyType == SerializedPropertyType.ManagedReference || !MayHoldReference(it))
                        {
                            enterChildren = false;
                            continue;
                        }
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (it.propertyPath == "m_Script") continue;
                        if (owned != null && owned.Contains(it.propertyPath)) continue;
                        if (!SlotValidatorEngine.IsMissingReference(it)) continue;
                        if (target.ShouldSkip(it, EValidationIssueType.MissingReference, EValidationSeverity.Warning)) continue;

                        target.Add(it.propertyPath, SlotValidatorEngine.ToDisplayPath(it.propertyPath), "연결된 에셋이 삭제되었습니다 (Missing 참조).",
                            EValidationIssueType.MissingReference, EValidationSeverity.Warning);
                    }
                }
            }

            /// <summary>
            /// 하위에 오브젝트 참조가 있을 수 있는지. 문자열 · 기본형 원소 배열(큰 데이터 테이블 · 타일 · 커브 키)은
            /// 원소마다 들어가면 비용만 크므로 건너뛴다.
            /// </summary>
            private static bool MayHoldReference(SerializedProperty prop)
            {
                if (prop.propertyType == SerializedPropertyType.String) return false;
                if (!prop.isArray || prop.arraySize == 0) return true;

                switch (prop.GetArrayElementAtIndex(0).propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                    case SerializedPropertyType.ExposedReference:
                    case SerializedPropertyType.Generic:
                        return true;
                    default:
                        return false;
                }
            }

            // ───────── 씬 안 프리팹 인스턴스: 원본이 실제로 보고한 문제는 건너뜀 ─────────

            private SlotValidatorEngine.ScanTarget NewTarget(string path, GameObject prefab, Object asset, string checkId, string objectPath, string componentName, Object current, HashSet<string> owned)
            {
                var target = new SlotValidatorEngine.ScanTarget
                {
                    AssetPath = path,
                    Prefab = prefab,
                    Asset = asset,
                    CheckId = checkId,
                    GameObjectPath = objectPath,
                    ComponentName = componentName,
                    Issues = m_Result.Issues,
                    CurrentObject = current,
                    OwnedPaths = owned,
                };
                // 프리팹에서 보고한 것만 기록 (씬 인스턴스가 대조)
                if (prefab != null && m_RecordSources) target.OnReported = RecordReportedBySource;
                return target;
            }

            private void RecordReportedBySource(Object obj, string propertyPath, EValidationIssueType type, EValidationSeverity severity)
            {
                string key = Key(obj, propertyPath, type);
                // Error(0)가 Warning(1)보다 높다
                if (!m_ReportedBySource.TryGetValue(key, out var reported) || severity < reported) m_ReportedBySource[key] = severity;
            }

            private Func<SerializedProperty, EValidationIssueType, EValidationSeverity, bool> SkipIfReportedBySource(Component component)
            {
                if (m_ReportedBySource.Count == 0 || !PrefabUtility.IsPartOfPrefabInstance(component)) return null;

                return (prop, type, severity) =>
                {
                    if (prop.prefabOverride) return false;
                    Object source = PrefabUtility.GetCorrespondingObjectFromSource(component);
                    for (int depth = 0; source != null && depth < MaxSourceDepth; depth++)
                    {
                        // 원본이 같은 심각도 이상으로 보고했을 때만 건너뜀 (원본은 경고 · 씬은 에러면 씬에서 보고)
                        if (m_ReportedBySource.TryGetValue(Key(source, prop.propertyPath, type), out var reported) && reported <= severity) return true;
                        Object next = PrefabUtility.GetCorrespondingObjectFromSource(source);
                        if (next == null || next == source) break;
                        source = next;
                    }
                    return false;
                };
            }

            // 변형 · 중첩 프리팹은 원본에서 늘어난 개수만 보고하므로 체인 전체를 더한다
            private int ReportedMissingScriptsBySource(GameObject go)
            {
                if (m_ReportedMissingScripts.Count == 0 || !PrefabUtility.IsPartOfPrefabInstance(go)) return 0;
                int total = 0;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                for (int depth = 0; source != null && depth < MaxSourceDepth; depth++)
                {
                    if (m_ReportedMissingScripts.TryGetValue(Key(source), out int count)) total += count;
                    GameObject next = PrefabUtility.GetCorrespondingObjectFromSource(source);
                    if (next == null || next == source) break;
                    source = next;
                }
                return total;
            }

            private string Key(Object obj)
            {
                if (!m_IdCache.TryGetValue(obj, out string id))
                {
                    id = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
                    m_IdCache[obj] = id;
                }
                return id;
            }

            private string Key(Object obj, string propertyPath, EValidationIssueType type) => $"{Key(obj)}|{propertyPath}|{type}";

            private static IEnumerable<Component> CollectComponents(GameObject root, bool all)
            {
                return all ? root.GetComponentsInChildren<Component>(true) : root.GetComponentsInChildren<MonoBehaviour>(true);
            }

            private void UnloadPeriodically(int interval)
            {
                if (++m_LoadedSinceUnload < interval) return;
                // 캐시가 에셋 참조를 잡고 있으면 언로드되지 않는다
                m_IdCache.Clear();
                m_TextureNames.Clear();
                EditorUtility.UnloadUnusedAssetsImmediate();
                m_LoadedSinceUnload = 0;
            }

            // ───────── 공통 ─────────

            private bool Cancel(string info, int index, int total)
            {
                if (!m_Progress) return false;
                if (!EditorUtility.DisplayCancelableProgressBar("Asset Health", info, total == 0 ? 1f : (float)index / total)) return false;
                m_Result.Canceled = true;
                Fail(null, null, "사용자가 검사를 취소했습니다.");
                return true;
            }

            private void Fail(string checkId, string path, string reason)
            {
                m_Result.Failures.Add(new AssetScanFailure { CheckId = checkId, AssetPath = path, Reason = reason });
            }
        }
    }
}
