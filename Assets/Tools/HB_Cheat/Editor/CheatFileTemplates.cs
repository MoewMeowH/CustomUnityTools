using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 프로젝트(Assets) 쪽 치트 파일·asmdef 생성. 패키지는 읽기 전용이므로 치트 정의는 모두 여기서 만든다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
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
        /// 클래스 이름이 C# 식별자가 아니면 ArgumentException. 같은 파일이나 같은 이름의 전역 타입이 있으면 뒤에 번호를 붙인다(ItemCheats2 …).
        /// </summary>
        public static string CreateCheatFile(string folder, string className)
        {
            string error = ValidateClassName(className);
            if (error != null) throw new ArgumentException(error);

            var settings = CheatSettings.Load();
            Directory.CreateDirectory(folder);
            string requested = className;
            className = UniqueClassName(folder, requested);
            string path = $"{folder}/{className}.cs";

            bool guard = IsAssemblyCSharp(folder);
            // 분류는 요청한 이름에서 Cheats를 뗀 값 + 붙은 번호 (ItemCheats → Item, 번호가 붙은 ItemCheats2 → Item2 — id가 겹치지 않게)
            string number = className.Substring(requested.Length);
            string category = (requested.EndsWith("Cheats") && requested.Length > "Cheats".Length
                ? requested.Substring(0, requested.Length - "Cheats".Length)
                : requested) + number;
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
            sb.AppendLine("    // Server: 반환한 문자열을 ICheatServerTransport로 보낸다. 패널 미리보기가 값이 바뀔 때마다 부르므로 문자열만 만들고 부작용이 없어야 한다");
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

        private static readonly HashSet<string> s_CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while"
        };

        // 생성 템플릿이 쓰는 이름 (전역 클래스로 만들면 using한 타입 · 네임스페이스 · 어트리뷰트를 가려 그 파일이 컴파일되지 않는다)
        private static readonly HashSet<string> s_TemplateNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System", "UnityEngine", "HB", "Debug", "List",
            "Cheat", "CheatArg", "CheatRange", "CheatOptions", "CheatTarget", "CheatCommand", "CheatOption", "CheatOptionContext",
            "ICheatOptionProvider", "CheatAttribute", "CheatArgAttribute", "CheatRangeAttribute", "CheatOptionsAttribute"
        };

        /// <summary>
        /// 클래스 이름으로 쓸 수 없으면 이유, 쓸 수 있으면 null
        /// (글자 · 숫자 · '_', 숫자로 시작하지 않음, C# 키워드 아님, 생성 템플릿이 쓰는 이름 아님)
        /// </summary>
        internal static string ValidateClassName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Class name is empty.";
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return $"'{name}' must start with a letter or '_'.";
            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_')) return $"'{name}' can contain only letters, digits and '_'.";
            }
            if (s_CSharpKeywords.Contains(name)) return $"'{name}' is a C# keyword.";
            if (s_TemplateNames.Contains(name)) return $"'{name}' is used by the generated code (System, UnityEngine, HB, Debug, List, Cheat* types) and would hide it.";
            return null;
        }

        /// <summary>
        /// 폴더에 같은 파일이 없고, 이 클래스와 함께 만드는 {이름}SampleProvider까지 같은 이름이 없는 이름.
        /// 이름은 로드된 전역 타입과 프로젝트의 같은 이름 스크립트 파일(심볼이 꺼져 컴파일되지 않은 치트 파일 · 아직 컴파일 전 파일)로 확인한다.
        /// GenerateUniqueAssetPath는 "ItemCheats 1"처럼 식별자가 아닌 번호를 붙일 수 있어 직접 번호를 붙인다.
        /// </summary>
        internal static string UniqueClassName(string folder, string className)
        {
            string candidate = className;
            for (int i = 2; IsNameTaken(folder, candidate); i++)
                candidate = className + i;
            return candidate;
        }

        private static bool IsNameTaken(string folder, string name)
        {
            return File.Exists($"{folder}/{name}.cs")
                   || GlobalTypeExists(name) || GlobalTypeExists(name + "SampleProvider")
                   || ScriptFileExists(name) || ScriptFileExists(name + "SampleProvider");
        }

        private static bool ScriptFileExists(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:MonoScript"))
            {
                if (Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == name) return true;
            }
            return false;
        }

        private static bool GlobalTypeExists(string name)
        {
#if UNITY_6000_4_OR_NEWER
            var assemblies = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
            for (int i = 0; i < assemblies.Count; i++)
            {
                if (assemblies[i].GetType(name, false) != null) return true;
            }
#else
#pragma warning disable UAC0005
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType(name, false) != null) return true;
            }
#pragma warning restore UAC0005
#endif
            return false;
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
