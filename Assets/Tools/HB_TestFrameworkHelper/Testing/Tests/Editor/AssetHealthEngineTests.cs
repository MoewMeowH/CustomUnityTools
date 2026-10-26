using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>AssetHealthEngine: 임시 폴더에 깨진 에셋을 만들어 검사한다. 씬 테스트는 TempScene으로 지금 열린 씬 구성을 보호한다.</summary>
    public class AssetHealthEngineTests : HBTestFixture
    {
        private TempAssetFolder m_Folder;

        [SetUp]
        public void CreateFolder()
        {
            m_Folder = Scope.NewTempAssetFolder();
        }

        // ───────── 프리팹 ─────────

        [Test]
        public void PrefabSlots_NullAndMissingSlot_AreErrors()
        {
            SavePrefab("Null", go => go.AddComponent<SlotProbeComponent>());
            var texture = NewTexture("Gone.asset");
            SavePrefab("Missing", go => go.AddComponent<SlotProbeComponent>().Slot = texture);
            AssetDatabase.DeleteAsset(P("Gone.asset"));

            var result = AssetHealthEngine.Scan(Options(AssetCheckIds.PrefabSlots));

            Assert.IsTrue(result.IsComplete, Failures(result));
            var issues = Of(result, AssetCheckIds.PrefabSlots);
            Assert.AreEqual(2, issues.Count, Describe(result));
            Assert.IsTrue(issues.Any(i => i.IssueType == EValidationIssueType.NullSlot && i.AssetPath == P("Null.prefab")));
            Assert.IsTrue(issues.Any(i => i.IssueType == EValidationIssueType.MissingReference && i.AssetPath == P("Missing.prefab")));
            Assert.IsTrue(issues.All(i => i.Severity == EValidationSeverity.Error && i.Asset is GameObject));
        }

        [Test]
        public void ExcludedBasePrefab_VariantStillReportsInheritedSlot()
        {
            string excluded = CreateSubFolder("Excluded");
            GameObject basePrefab = SavePrefabAt(excluded + "/Base.prefab", go => go.AddComponent<SlotProbeComponent>());
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, P("Variant.prefab"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var options = Options(AssetCheckIds.PrefabSlots);
            options.ExcludeFolders.Add(excluded);
            var result = AssetHealthEngine.Scan(options);

            var issues = Of(result, AssetCheckIds.PrefabSlots);
            Assert.AreEqual(1, issues.Count, Describe(result));
            Assert.AreEqual(P("Variant.prefab"), issues[0].AssetPath, "원본이 제외되면 변형에서 보고해야 함 (원본 판정은 최종 대상 기준)");
        }

        [Test]
        public void LegacyScanPrefabs_KeepsResultsAndFormat()
        {
            SavePrefab("Legacy", go => go.AddComponent<SlotProbeComponent>());

            var issues = SlotValidatorEngine.ScanPrefabs(new[] { m_Folder.AssetPath });

            Assert.AreEqual(1, issues.Count);
            StringAssert.StartsWith($"[Error][NullSlot] {P("Legacy.prefab")} > Legacy > SlotProbeComponent.Slot", issues[0].ToString());
            Assert.AreEqual(AssetCheckIds.PrefabSlots, issues[0].CheckId);
        }

        // ───────── 대상 · 실패 ─────────

        [Test]
        public void EmptySearchFolders_DoesNotExpandToAssets()
        {
            var options = Options(AssetCheckIds.PrefabSlots);
            options.SearchFolders.Clear();

            var result = AssetHealthEngine.Scan(options);

            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(0, result.ScannedTargetCount);
        }

        [Test]
        public void MissingFolder_And_UnknownCheck_AreIncomplete()
        {
            var options = Options(AssetCheckIds.PrefabSlots);
            options.SearchFolders.Add(m_Folder.AssetPath + "/NoSuchFolder");
            Assert.IsFalse(AssetHealthEngine.Scan(options).IsComplete);

            var unknown = Options("NoSuchCheck");
            var result = AssetHealthEngine.Scan(unknown);
            Assert.IsFalse(result.IsComplete);
            StringAssert.Contains("NoSuchCheck", result.Failures[0].Reason);
        }

        [Test]
        public void MissingExplicitScene_IsIncomplete()
        {
            var options = Options(AssetCheckIds.Scenes);
            options.SceneSources = ESceneTargetSources.ExplicitScenes;
            options.ExplicitScenePaths.Add(P("NoSuch.unity"));

            Assert.IsFalse(AssetHealthEngine.Scan(options).IsComplete);
        }

        [Test]
        public void OverrideScenePaths_Empty_ScansNoScenes()
        {
            var options = Options(AssetCheckIds.Scenes);
            options.SceneSources = ESceneTargetSources.BuildSettings;
            options.OverrideScenePaths = new List<string>();

            var result = AssetHealthEngine.Scan(options);

            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(0, result.ScannedTargetCount);
        }

        // ───────── ScriptableObject ─────────

        [Test]
        public void ScriptableObject_MainAndSubAsset_BothReported()
        {
            var main = ScriptableObject.CreateInstance<SlotProbeAsset>();
            main.name = "Main";
            AssetDatabase.CreateAsset(main, P("Probe.asset"));
            var sub = ScriptableObject.CreateInstance<SlotProbeAsset>();
            sub.name = "Sub";
            AssetDatabase.AddObjectToAsset(sub, P("Probe.asset"));
            AssetDatabase.SaveAssets();

            var result = AssetHealthEngine.Scan(Options(AssetCheckIds.ScriptableObjects));

            var issues = Of(result, AssetCheckIds.ScriptableObjects);
            Assert.AreEqual(2, issues.Count, Describe(result));
            // 메인 에셋 이름은 임포트 때 파일 이름으로 바뀐다 (Unity 동작)
            Assert.IsTrue(issues.Any(i => i.SubAssetName == null && i.GameObjectPath == "Probe"), Describe(result));
            Assert.IsTrue(issues.Any(i => i.SubAssetName != null && i.GameObjectPath == "Sub"), Describe(result));
        }

        // ───────── 머티리얼 ─────────

        [Test]
        public void Material_ErrorShader_IsError()
        {
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.CreateAsset(material, P("Error.mat"));

            var issues = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.Materials)), AssetCheckIds.Materials);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(EValidationIssueType.ErrorShader, issues[0].IssueType);
            Assert.AreEqual(EValidationSeverity.Error, issues[0].Severity);
        }

        [Test]
        public void Material_DeletedShader_IsError()
        {
            File.WriteAllText(P("Probe.shader"), "Shader \"HBTest/Probe\" { Properties { _MainTex (\"Tex\", 2D) = \"white\" {} } SubShader { Pass { Color (1,1,1,1) } } }");
            AssetDatabase.ImportAsset(P("Probe.shader"));
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(P("Probe.shader")));
            AssetDatabase.CreateAsset(material, P("NoShader.mat"));
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(P("Probe.shader"));

            var issues = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.Materials)), AssetCheckIds.Materials);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(EValidationSeverity.Error, issues[0].Severity);
            Assert.That(issues[0].IssueType, Is.EqualTo(EValidationIssueType.MissingShader).Or.EqualTo(EValidationIssueType.ErrorShader));
            Debug.Log($"[실측] 셰이더를 지운 머티리얼의 판정: {issues[0].IssueType}");
        }

        [Test]
        public void Material_MissingTexture_OnlyForCurrentShaderProperties()
        {
            var texture = NewTexture("Tex.asset");
            var material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = texture };
            AssetDatabase.CreateAsset(material, P("Tex.mat"));
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(P("Tex.asset"));

            var missing = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.Materials)), AssetCheckIds.Materials);
            Assert.AreEqual(1, missing.Count, string.Join("\n", missing));
            Assert.AreEqual(EValidationIssueType.MissingReference, missing[0].IssueType);
            Assert.AreEqual(EValidationSeverity.Warning, missing[0].Severity);
            Assert.AreEqual("_MainTex", missing[0].FieldName);

            // 텍스처 속성이 없는 셰이더로 바꾸면 남은 칸은 무시
            material = AssetDatabase.LoadAssetAtPath<Material>(P("Tex.mat"));
            material.shader = Shader.Find("Unlit/Color");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(0, Of(AssetHealthEngine.Scan(Options(AssetCheckIds.Materials)), AssetCheckIds.Materials).Count);
        }

        [Test]
        public void Material_ShaderCompileError_IsWarning()
        {
            // 컴파일 에러 셰이더를 임포트하면 에러 로그가 나온다 (의도한 것)
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            File.WriteAllText(P("Broken.shader"),
                "Shader \"HBTest/Broken\" { SubShader { Pass { CGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n" +
                "float4 vert(float4 v : POSITION) : SV_POSITION { return hb_undefined_symbol; }\n" +
                "float4 frag() : SV_Target { return 1; }\nENDCG } } }");
            AssetDatabase.ImportAsset(P("Broken.shader"));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(P("Broken.shader"));
            Assert.IsTrue(ShaderUtil.ShaderHasError(shader), "전제: 임포트한 셰이더에 컴파일 에러가 있어야 함");
            AssetDatabase.CreateAsset(new Material(shader), P("Broken.mat"));

            var issues = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.Materials)), AssetCheckIds.Materials);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(EValidationIssueType.ShaderCompileError, issues[0].IssueType);
            Assert.AreEqual(EValidationSeverity.Warning, issues[0].Severity);
        }

        // ───────── 일반 Missing 참조 ─────────

        [Test]
        public void GeneralMissing_SkipsDataArrays_StillFindsLaterReference()
        {
            var texture = NewTexture("Gone.asset");
            var asset = ScriptableObject.CreateInstance<SlotProbeAsset>();
            asset.Numbers = Enumerable.Range(0, 1000).ToArray();
            asset.Plain = texture;
            AssetDatabase.CreateAsset(asset, P("Data.asset"));
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(P("Gone.asset"));

            var issues = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.MissingReferences)), AssetCheckIds.MissingReferences);

            // 기본형 배열 · 문자열(Plain 앞)을 건너뛰어도 그 뒤의 참조는 찾아야 함
            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual("Plain", issues[0].FieldName);
        }

        [Test]
        public void GeneralMissing_BuiltInComponent_AndSlotOwnership()
        {
            var mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, P("Mesh.asset"));
            var texture = NewTexture("Gone.asset");
            SavePrefab("Mixed", go =>
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var probe = go.AddComponent<SlotProbeComponent>();
                probe.Slot = texture;
                probe.Plain = texture;
            });
            AssetDatabase.DeleteAsset(P("Mesh.asset"));
            AssetDatabase.DeleteAsset(P("Gone.asset"));

            // 슬롯 검사가 켜져 있으면 Slot 칸은 슬롯 검사(에러)만, 나머지는 일반 Missing(경고)
            var both = AssetHealthEngine.Scan(Options(AssetCheckIds.PrefabSlots, AssetCheckIds.MissingReferences));
            Assert.IsTrue(both.IsComplete, Failures(both));
            var slot = Of(both, AssetCheckIds.PrefabSlots);
            var general = Of(both, AssetCheckIds.MissingReferences);
            Assert.AreEqual(1, slot.Count, Describe(both));
            Assert.AreEqual("Slot", slot[0].FieldName);
            Assert.IsTrue(general.Any(i => i.ComponentName == nameof(MeshFilter)), Describe(both));
            Assert.IsTrue(general.Any(i => i.FieldName == "Plain"), Describe(both));
            Assert.IsFalse(general.Any(i => i.FieldName == "Slot"), "슬롯 검사가 맡은 칸을 다시 보고하면 안 됨");
            Assert.IsTrue(general.All(i => i.Severity == EValidationSeverity.Warning));

            // 슬롯 검사가 꺼져 있으면 Slot 칸도 일반 Missing이 경고로 보고
            var only = Of(AssetHealthEngine.Scan(Options(AssetCheckIds.MissingReferences)), AssetCheckIds.MissingReferences);
            Assert.IsTrue(only.Any(i => i.FieldName == "Slot"));
        }

        // ───────── 씬 ─────────

        [Test]
        public void Scene_ClosedScene_IsScannedThenClosed_ActiveSceneKept()
        {
            Scene active = PrepareSceneHost();
            string path = SaveClosedScene("Closed.unity", scene => NewProbe(scene, "Holder"));

            var result = AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.Scenes));

            Assert.IsTrue(result.IsComplete, Failures(result));
            var issues = Of(result, AssetCheckIds.Scenes);
            Assert.AreEqual(1, issues.Count, Describe(result));
            Assert.IsInstanceOf<SceneAsset>(issues[0].Asset);
            Assert.IsFalse(issues[0].IsUnsavedScene);
            Assert.IsFalse(SceneManager.GetSceneByPath(path).IsValid(), "검사하려고 연 씬은 닫혀야 함");
            Assert.AreEqual(active, SceneManager.GetActiveScene());
        }

        [Test]
        public void Scene_OpenDirtyScene_IsScannedAsIs_AndStaysOpenAndDirty()
        {
            PrepareSceneHost();
            string path = SaveClosedScene("Open.unity", _ => { });
            Scene open = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            NewProbe(open, "Unsaved");
            EditorSceneManager.MarkSceneDirty(open);

            var issues = Of(AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.Scenes)), AssetCheckIds.Scenes);

            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].IsUnsavedScene);
            Scene after = SceneManager.GetSceneByPath(path);
            Assert.IsTrue(after.IsValid() && after.isLoaded, "열려 있던 씬은 닫지 않음");
            Assert.IsTrue(after.isDirty, "저장하지 않은 변경은 그대로");
        }

        [Test]
        public void Scene_PrefabInstance_SkippedOnlyWhenSourceReported()
        {
            PrepareSceneHost();
            GameObject prefab = SavePrefab("Placed", go => go.AddComponent<SlotProbeComponent>());
            string path = SaveClosedScene("Placed.unity", scene => PrefabUtility.InstantiatePrefab(prefab, scene));

            var withPrefab = AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.PrefabSlots, AssetCheckIds.Scenes));
            Assert.AreEqual(1, Of(withPrefab, AssetCheckIds.PrefabSlots).Count, Describe(withPrefab));
            Assert.AreEqual(0, Of(withPrefab, AssetCheckIds.Scenes).Count, "원본이 보고한 문제는 씬에서 건너뜀");

            var sceneOnly = AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.Scenes));
            Assert.AreEqual(1, Of(sceneOnly, AssetCheckIds.Scenes).Count, "원본을 검사하지 않았으면 씬에서 보고");
        }

        [Test]
        public void Scene_PrefabWarning_DoesNotHideSceneError()
        {
            PrepareSceneHost();
            var texture = NewTexture("Gone.asset");
            GameObject prefab = SavePrefab("Warned", go => go.AddComponent<SlotProbeComponent>().Slot = texture);
            string path = SaveClosedScene("Warned.unity", scene => PrefabUtility.InstantiatePrefab(prefab, scene));
            AssetDatabase.DeleteAsset(P("Gone.asset"));

            // 프리팹 슬롯 검사가 꺼져 있으면 원본은 Slot 칸을 일반 Missing(경고)으로만 보고한다 → 씬의 슬롯 에러를 가리면 안 됨
            var result = AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.Scenes, AssetCheckIds.MissingReferences));

            Assert.IsTrue(result.IsComplete, Failures(result));
            Assert.IsTrue(Of(result, AssetCheckIds.MissingReferences).Any(i => i.AssetPath == P("Warned.prefab") && i.FieldName == "Slot"), Describe(result));
            var scene = Of(result, AssetCheckIds.Scenes);
            Assert.AreEqual(1, scene.Count, Describe(result));
            Assert.AreEqual(EValidationIssueType.MissingReference, scene[0].IssueType);
            Assert.AreEqual(EValidationSeverity.Error, scene[0].Severity);
        }

        [Test]
        public void Scene_VariantInstance_MissingScriptsReportedByChain_AreSkipped()
        {
            PrepareSceneHost();
            GameObject basePrefab = SavePrefab("MsBase", go => go.AddComponent<SlotProbeComponent>());
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            GameObject variant;
            try
            {
                instance.AddComponent<SlotProbeComponent>();
                variant = PrefabUtility.SaveAsPrefabAsset(instance, P("MsVariant.prefab"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            string path = SaveClosedScene("Ms.unity", scene => PrefabUtility.InstantiatePrefab(variant, scene));

            // 스크립트 GUID를 없는 값으로 바꿔 Missing 스크립트를 만든다: 원본 1개 · 변형 2개(물려받은 1 + 추가 1)
            string scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(FindScript<SlotProbeComponent>()));
            foreach (string file in new[] { P("MsBase.prefab"), P("MsVariant.prefab"), path })
            {
                File.WriteAllText(file, File.ReadAllText(file).Replace(scriptGuid, "0123456789abcdef0123456789abcdef"));
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
            }

            var result = AssetHealthEngine.Scan(SceneOptions(path, AssetCheckIds.PrefabSlots, AssetCheckIds.Scenes));

            Assert.IsTrue(result.IsComplete, Failures(result));
            var prefabs = Of(result, AssetCheckIds.PrefabSlots).Where(i => i.IssueType == EValidationIssueType.MissingComponent).ToList();
            Assert.AreEqual(2, prefabs.Count, Describe(result));
            Assert.AreEqual(0, Of(result, AssetCheckIds.Scenes).Count, "원본 · 변형이 나눠 보고한 Missing 스크립트(합 2개)를 씬에서 다시 보고하면 안 됨\n" + Describe(result));
        }

        // ───────── 도우미 ─────────

        private string P(string name) => m_Folder.AssetPath + "/" + name;

        private AssetHealthScanOptions Options(params string[] checks)
        {
            return new AssetHealthScanOptions
            {
                SearchFolders = new List<string> { m_Folder.AssetPath },
                EnabledChecks = new List<string>(checks),
                SceneSources = ESceneTargetSources.None,
            };
        }

        private AssetHealthScanOptions SceneOptions(string scenePath, params string[] checks)
        {
            var options = Options(checks);
            options.SceneSources = ESceneTargetSources.ExplicitScenes;
            options.ExplicitScenePaths.Add(scenePath);
            return options;
        }

        private string CreateSubFolder(string name)
        {
            AssetDatabase.CreateFolder(m_Folder.AssetPath, name);
            return P(name);
        }

        private Texture2D NewTexture(string name)
        {
            var texture = new Texture2D(2, 2);
            AssetDatabase.CreateAsset(texture, P(name));
            return texture;
        }

        private GameObject SavePrefab(string name, Action<GameObject> build) => SavePrefabAt(P(name + ".prefab"), build, name);

        private static GameObject SavePrefabAt(string path, Action<GameObject> build, string name = null)
        {
            var go = new GameObject(name ?? Path.GetFileNameWithoutExtension(path));
            try
            {
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>지금 열린 씬 구성을 보호하고, 추가 씬을 열 수 있게 임시 씬에 경로를 준다. 그 씬을 돌려준다.</summary>
        private Scene PrepareSceneHost()
        {
            TempScene host = Scope.NewTempScene();
            EditorSceneManager.SaveScene(host.Scene, P("Host.unity"));
            return SceneManager.GetActiveScene();
        }

        private string SaveClosedScene(string name, Action<Scene> build)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            build(scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, P(name)));
            Assert.IsTrue(EditorSceneManager.CloseScene(scene, true));
            return P(name);
        }

        private static void NewProbe(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<SlotProbeComponent>();
        }

        private static MonoScript FindScript<T>() where T : MonoBehaviour
        {
            var go = new GameObject("ScriptProbe");
            try
            {
                return MonoScript.FromMonoBehaviour(go.AddComponent<T>());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static List<SlotValidationIssue> Of(AssetHealthScanResult result, string checkId) => result.Issues.Where(i => i.CheckId == checkId).ToList();

        private static string Failures(AssetHealthScanResult result) => string.Join("\n", result.Failures);

        private static string Describe(AssetHealthScanResult result) => string.Join("\n", result.Issues) + "\n" + Failures(result);
    }
}
