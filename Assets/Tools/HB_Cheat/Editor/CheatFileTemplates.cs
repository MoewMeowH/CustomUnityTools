using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 프로젝트(Assets) 쪽 치트 파일·asmdef 생성. 패키지는 읽기 전용이므로 치트 정의는 모두 여기서 만든다.
    /// </summary>
    public static class CheatFileTemplates
    {
        public const string RegistryFileName = "HBCheatRegistry.g.cs";
        public const string GeneratedMarker = "// HB_CHEAT_GENERATED_REGISTRY";

        /// <summary>폴더 안의 스크립트가 들어갈 어셈블리 이름</summary>
        public static string AssemblyOf(string folder)
        {
            string name = CompilationPipeline.GetAssemblyNameFromScriptPath(folder.TrimEnd('/') + "/__probe.cs") ?? "Assembly-CSharp";
            return name.EndsWith(".dll") ? name.Substring(0, name.Length - 4) : name;
        }

        public static bool IsAssemblyCSharp(string folder) => AssemblyOf(folder) == "Assembly-CSharp";

        /// <summary>
        /// 치트 카테고리 파일 생성. Assembly-CSharp 폴더면 파일 전체를 프로젝트 심볼로 감싼다 (asmdef면 asmdef의 defineConstraints가 막는다).
        /// </summary>
        public static string CreateCheatFile(string folder, string className)
        {
            var settings = CheatSettings.Load();
            Directory.CreateDirectory(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{className}.cs");
            className = Path.GetFileNameWithoutExtension(path);

            bool guard = IsAssemblyCSharp(folder);
            string category = className.EndsWith("Cheats") && className.Length > "Cheats".Length
                ? className.Substring(0, className.Length - "Cheats".Length)
                : className;
            string idPrefix = category.ToLowerInvariant();

            var sb = new StringBuilder();
            if (guard) sb.AppendLine($"#if {settings.UserDefine}");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using HB.Cheat;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"public static class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // Local: 클라에서 직접 실행. 반환형 void / bool / string(메시지) / CheatResult");
            sb.AppendLine($"    [Cheat(\"{idPrefix}.log\", \"{category}/Log\", Description = \"Sample local cheat\")]");
            sb.AppendLine("    public static void Log([CheatRange(1, 100)] int count = 1)");
            sb.AppendLine("    {");
            sb.AppendLine($"        Debug.Log($\"[{category}] count={{count}}\");");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    // Server: 반환한 문자열을 ICheatServerTransport로 보낸다");
            sb.AppendLine($"    [Cheat(\"{idPrefix}.add\", \"{category}/Add\", Target = CheatTarget.Server)]");
            sb.AppendLine("    public static string Add(");
            sb.AppendLine($"        [CheatArg(\"Id\"), CheatOptions(typeof({className}SampleProvider))] int id,");
            sb.AppendLine("        [CheatArg(\"Count\"), CheatRange(1, 9999)] int count = 1)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return CheatCommand.Join(\"{idPrefix}_add\", id, count);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("// 테이블을 읽어 후보를 만든다. Version이 바뀔 때만 다시 수집한다.");
            sb.AppendLine($"public sealed class {className}SampleProvider : ICheatOptionProvider");
            sb.AppendLine("{");
            sb.AppendLine("    public int Version => 0;");
            sb.AppendLine();
            sb.AppendLine("    public void Collect(CheatOptionContext context, List<CheatOption> results)");
            sb.AppendLine("    {");
            sb.AppendLine("        results.Add(new CheatOption(1001, \"Sample A\"));");
            sb.AppendLine("        results.Add(new CheatOption(1002, \"Sample B\"));");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            if (guard) sb.AppendLine("#endif");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>
        /// 치트 전용 asmdef 생성. HB_CHEAT와 프로젝트 심볼이 모두 켜져 있을 때만 컴파일된다.
        /// </summary>
        public static string CreateCheatAsmdef(string folder)
        {
            var settings = CheatSettings.Load();
            Directory.CreateDirectory(folder);
            string path = $"{folder}/{settings.CheatAsmdefName}.asmdef";
            if (File.Exists(path))
            {
                Debug.LogWarning($"[HB_Cheat] '{path}' already exists.");
                return path;
            }

            var references = new[] { "HB.Cheat" }.Concat(settings.CheatAsmdefReferences.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"    \"name\": \"{settings.CheatAsmdefName}\",");
            sb.AppendLine("    \"rootNamespace\": \"\",");
            sb.AppendLine("    \"references\": [");
            sb.AppendLine(string.Join(",\n", references.Select(r => $"        \"{r}\"")));
            sb.AppendLine("    ],");
            sb.AppendLine("    \"includePlatforms\": [],");
            sb.AppendLine("    \"excludePlatforms\": [],");
            sb.AppendLine("    \"allowUnsafeCode\": false,");
            sb.AppendLine("    \"overrideReferences\": false,");
            sb.AppendLine("    \"precompiledReferences\": [],");
            sb.AppendLine("    \"autoReferenced\": false,");
            sb.AppendLine("    \"defineConstraints\": [");
            sb.AppendLine($"        \"{CheatDefines.CoreDefine}\",");
            sb.AppendLine($"        \"{settings.UserDefine}\"");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"versionDefines\": [],");
            sb.AppendLine("    \"noEngineReferences\": false");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>프로젝트에 있는 생성 레지스트리 파일 (심볼이 꺼져 있어도 찾는다)</summary>
        public static string[] FindGeneratedRegistries()
        {
            return AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(RegistryFileName) + " t:MonoScript", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(RegistryFileName) && File.Exists(p) && File.ReadAllText(p).Contains(GeneratedMarker))
                .ToArray();
        }

        /// <summary>생성 파일을 모두 지운다 (치트를 지워 생성 파일이 컴파일 에러를 낼 때 복구용)</summary>
        public static int DeleteGeneratedRegistries()
        {
            var paths = FindGeneratedRegistries();
            foreach (var path in paths)
                AssetDatabase.DeleteAsset(path);
            return paths.Length;
        }
    }
}
