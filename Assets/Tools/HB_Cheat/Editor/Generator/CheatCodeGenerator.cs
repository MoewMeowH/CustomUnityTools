#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HB.Cheat.Editor
{
    internal sealed class CheatScanIssue
    {
        public bool IsError;
        public string Message;
        public MethodInfo Method;
    }

    internal sealed class ScannedArg
    {
        public ParameterInfo Parameter;
        public CheatValueType ValueType;
        public CheatEditorKind Editor;
        public CheatRangeAttribute Range;
        public CheatOptionsAttribute Options;
        public string Label;
        public int[] DependsOn = Array.Empty<int>();
    }

    internal sealed class ScannedCheat
    {
        public MethodInfo Method;
        public CheatAttribute Attribute;
        public string AssemblyName;
        public readonly List<ScannedArg> Args = new List<ScannedArg>();
    }

    /// <summary>
    /// [Cheat] 메서드를 찾아 어셈블리마다 레지스트리 코드(HBCheatRegistry.g.cs)를 생성한다.
    /// 리플렉션을 런타임에 쓰지 않기 위해 (IL2CPP 속도·스트리핑) 호출 코드를 미리 만든다.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatCodeGenerator
    {
        private static readonly Dictionary<Type, CheatValueType> s_ValueTypes = new Dictionary<Type, CheatValueType>
        {
            { typeof(int), CheatValueType.Int },
            { typeof(long), CheatValueType.Long },
            { typeof(float), CheatValueType.Float },
            { typeof(double), CheatValueType.Double },
            { typeof(bool), CheatValueType.Bool },
            { typeof(string), CheatValueType.String }
        };

        public static List<ScannedCheat> LastCheats { get; private set; } = new List<ScannedCheat>();
        public static List<CheatScanIssue> LastIssues { get; private set; } = new List<CheatScanIssue>();

        static CheatCodeGenerator()
        {
            CheatEditorHooks.Generate = Generate;
            CheatEditorHooks.DrawCheatsTab = CheatsTabGUI.Draw;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (CheatSettings.Load().AutoGenerate) Generate(true);
                else Scan(out _, out _);
            };
        }

        #region Scan

        public static void Scan(out List<ScannedCheat> cheats, out List<CheatScanIssue> issues)
        {
            cheats = new List<ScannedCheat>();
            issues = new List<CheatScanIssue>();

            var playerAssemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name),
                StringComparer.Ordinal);

            foreach (var method in TypeCache.GetMethodsWithAttribute<CheatAttribute>())
            {
                var attribute = method.GetCustomAttribute<CheatAttribute>();
                var cheat = new ScannedCheat
                {
                    Method = method,
                    Attribute = attribute,
                    AssemblyName = method.DeclaringType.Assembly.GetName().Name
                };

                int errorsBefore = issues.Count(i => i.IsError);
                ValidateMethod(cheat, playerAssemblies, issues);
                ScanArgs(cheat, issues);
                if (issues.Count(i => i.IsError) == errorsBefore)
                    cheats.Add(cheat);
            }

            foreach (var group in cheats.GroupBy(c => c.Attribute.Id).Where(g => g.Count() > 1))
            {
                foreach (var cheat in group)
                    Error(issues, cheat.Method, $"Duplicate id '{group.Key}'");
            }
            var duplicateIds = new HashSet<string>(cheats.GroupBy(c => c.Attribute.Id).Where(g => g.Count() > 1).Select(g => g.Key));
            cheats.RemoveAll(c => duplicateIds.Contains(c.Attribute.Id));

            foreach (var group in cheats.GroupBy(c => c.Attribute.Path).Where(g => g.Count() > 1))
                Warn(issues, group.First().Method, $"Several cheats share the path '{group.Key}'");

            cheats.Sort((a, b) => string.CompareOrdinal(a.Attribute.Id, b.Attribute.Id));
            LastCheats = cheats;
            LastIssues = issues;
        }

        private static void ValidateMethod(ScannedCheat cheat, HashSet<string> playerAssemblies, List<CheatScanIssue> issues)
        {
            var method = cheat.Method;
            var attribute = cheat.Attribute;

            if (string.IsNullOrWhiteSpace(attribute.Id)) Error(issues, method, "Id is empty");
            if (string.IsNullOrWhiteSpace(attribute.Path)) Error(issues, method, "Path is empty");
            if (!method.IsStatic) Error(issues, method, "Cheat method must be static");
            if (method.IsGenericMethodDefinition || method.DeclaringType.ContainsGenericParameters) Error(issues, method, "Generic methods/types are not supported");
            if (!(method.IsPublic || method.IsAssembly || method.IsFamilyOrAssembly)) Error(issues, method, "Cheat method must be public or internal");
            if (!IsAccessibleInAssembly(method.DeclaringType)) Error(issues, method, "Declaring type must be public or internal (not private nested)");
            if (!playerAssemblies.Contains(cheat.AssemblyName)) Error(issues, method, $"'{cheat.AssemblyName}' is not a player (runtime) assembly");

            var returnType = method.ReturnType;
            if (attribute.Target == CheatTarget.Server)
            {
                if (returnType != typeof(string)) Error(issues, method, "Server cheat must return string (the command sent to the server)");
            }
            else if (returnType != typeof(void) && returnType != typeof(bool) && returnType != typeof(string) && returnType != typeof(CheatResult))
            {
                Error(issues, method, "Local cheat must return void, bool, string or CheatResult");
            }
        }

        private static void ScanArgs(ScannedCheat cheat, List<CheatScanIssue> issues)
        {
            var parameters = cheat.Method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var arg = new ScannedArg
                {
                    Parameter = p,
                    Range = p.GetCustomAttribute<CheatRangeAttribute>(),
                    Options = p.GetCustomAttribute<CheatOptionsAttribute>(),
                    Label = p.GetCustomAttribute<CheatArgAttribute>()?.Label
                };
                cheat.Args.Add(arg);

                // CheatProtocol이 run / options에서 건너뛰는 이름 (README "예약 인자 이름")
                if (p.Name == "query" || p.Name == "limit")
                    Warn(issues, cheat.Method, $"'{p.Name}' is reserved by the automation protocol; automation cannot set this argument (rename the parameter)");

                if (p.IsOut || p.ParameterType.IsByRef || p.IsDefined(typeof(ParamArrayAttribute)))
                {
                    Error(issues, cheat.Method, $"'{p.Name}': ref/out/params are not supported");
                    continue;
                }

                var type = p.ParameterType;
                if (type.IsEnum)
                {
                    arg.ValueType = CheatValueType.Enum;
                    if (!IsAccessibleInAssembly(type) || (type.Assembly != cheat.Method.DeclaringType.Assembly && !type.IsVisible))
                        Error(issues, cheat.Method, $"'{p.Name}': enum {type.Name} is not accessible from generated code");
                }
                else if (!s_ValueTypes.TryGetValue(type, out arg.ValueType))
                {
                    Error(issues, cheat.Method, $"'{p.Name}': type {type.Name} is not supported (int, long, float, double, bool, string, enum)");
                    continue;
                }

                if (arg.Options != null) arg.Editor = CheatEditorKind.Options;
                else if (arg.ValueType == CheatValueType.Enum) arg.Editor = CheatEditorKind.Options;
                else if (arg.ValueType == CheatValueType.Bool) arg.Editor = CheatEditorKind.Toggle;
                else if (arg.ValueType == CheatValueType.String) arg.Editor = CheatEditorKind.Text;
                else arg.Editor = CheatEditorKind.Number;

                if (arg.Range != null && arg.Editor != CheatEditorKind.Number)
                    Warn(issues, cheat.Method, $"'{p.Name}': [CheatRange] only applies to number arguments");
                if (arg.Range != null && arg.Range.Min > arg.Range.Max)
                    Error(issues, cheat.Method, $"'{p.Name}': [CheatRange] min > max");

                if (arg.Options != null) ValidateProvider(cheat, arg, i, parameters, issues);
            }
        }

        private static void ValidateProvider(ScannedCheat cheat, ScannedArg arg, int index, ParameterInfo[] parameters, List<CheatScanIssue> issues)
        {
            var provider = arg.Options.Provider;
            string name = arg.Parameter.Name;

            if (provider == null || !typeof(ICheatOptionProvider).IsAssignableFrom(provider))
                Error(issues, cheat.Method, $"'{name}': provider must implement ICheatOptionProvider");
            else if (provider.IsAbstract || provider.ContainsGenericParameters)
                Error(issues, cheat.Method, $"'{name}': provider {provider.Name} must be a concrete non-generic class");
            else if (provider.GetConstructor(Type.EmptyTypes) == null)
                Error(issues, cheat.Method, $"'{name}': provider {provider.Name} needs a public parameterless constructor");
            else if (!IsAccessibleInAssembly(provider) || (provider.Assembly != cheat.Method.DeclaringType.Assembly && !provider.IsVisible))
                Error(issues, cheat.Method, $"'{name}': provider {provider.Name} is not accessible from generated code");

            var dependsOn = arg.Options.DependsOn ?? Array.Empty<string>();
            var indices = new List<int>();
            foreach (var dependency in dependsOn)
            {
                int depIndex = Array.FindIndex(parameters, p => p.Name == dependency);
                if (depIndex < 0) Error(issues, cheat.Method, $"'{name}': DependsOn '{dependency}' is not a parameter");
                else if (depIndex >= index) Error(issues, cheat.Method, $"'{name}': DependsOn '{dependency}' must be an earlier parameter");
                else indices.Add(depIndex);
            }
            arg.DependsOn = indices.ToArray();
        }

        /// <summary>같은 어셈블리의 생성 코드에서 접근 가능한지 (private/protected 중첩 타입 제외)</summary>
        private static bool IsAccessibleInAssembly(Type type)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (t.IsNestedPrivate || t.IsNestedFamily || t.IsNestedFamANDAssem) return false;
            }
            return true;
        }

        private static void Error(List<CheatScanIssue> issues, MethodInfo method, string message) =>
            issues.Add(new CheatScanIssue { IsError = true, Method = method, Message = message });

        private static void Warn(List<CheatScanIssue> issues, MethodInfo method, string message) =>
            issues.Add(new CheatScanIssue { IsError = false, Method = method, Message = message });

        public static string Describe(MethodInfo method) => method == null ? string.Empty : $"{method.DeclaringType.FullName}.{method.Name}";

        #endregion

        #region Generate

        public static bool Generate(bool silent)
        {
            Scan(out var cheats, out var issues);

            foreach (var issue in issues)
            {
                string text = $"[HB_Cheat] {Describe(issue.Method)}: {issue.Message}";
                if (issue.IsError) Debug.LogError(text);
                else if (!silent) Debug.LogWarning(text);
            }

            var settings = CheatSettings.Load();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            bool ok = true;

            foreach (var group in cheats.GroupBy(c => c.AssemblyName).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (!TryResolveOutputPath(group.Key, settings, out string path, out string error))
                {
                    Debug.LogError($"[HB_Cheat] {error}");
                    ok = false;
                    continue;
                }

                string code = BuildRegistry(group.Key, group.ToList(), settings);
                written.Add(path);
                if (File.Exists(path) && File.ReadAllText(path) == code) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, code, new UTF8Encoding(false));
                changed = true;
                Debug.Log($"[HB_Cheat] Generated {path} ({group.Count()} cheats)");
            }

            // 치트가 없어진 어셈블리, 출력 폴더가 바뀐 경우의 이전 파일
            foreach (var stale in CheatFileTemplates.FindGeneratedRegistries())
            {
                if (written.Contains(stale)) continue;
                AssetDatabase.DeleteAsset(stale);
                changed = true;
                Debug.Log($"[HB_Cheat] Removed stale registry {stale}");
            }

            if (changed) AssetDatabase.Refresh();
            else if (!silent) Debug.Log($"[HB_Cheat] Registry is up to date ({cheats.Count} cheats).");

            return ok && issues.All(i => !i.IsError);
        }

        private static bool TryResolveOutputPath(string assemblyName, CheatSettings settings, out string path, out string error)
        {
            string folder;
            string asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            if (asmdef != null)
            {
                if (!asmdef.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    path = null;
                    error = $"'{assemblyName}' is outside Assets ({asmdef}); generated code cannot be written there. Move the cheats into Assets.";
                    return false;
                }
                folder = Path.GetDirectoryName(asmdef).Replace('\\', '/') + "/Generated";
            }
            else if (assemblyName == "Assembly-CSharp")
            {
                folder = settings.AssemblyCSharpOutputFolder;
            }
            else
            {
                path = null;
                error = $"Cheats in '{assemblyName}' are not supported (use Assembly-CSharp or an asmdef under Assets).";
                return false;
            }

            path = $"{folder}/{CheatFileTemplates.RegistryFileName}";
            string actual = CheatFileTemplates.AssemblyOf(folder);
            if (actual != assemblyName)
            {
                error = $"Output '{path}' would compile into '{actual}', not '{assemblyName}'. Change the output folder in Tools > HB > Cheat.";
                path = null;
                return false;
            }

            error = null;
            return true;
        }

        private static string BuildRegistry(string assemblyName, List<ScannedCheat> cheats, CheatSettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine(CheatFileTemplates.GeneratedMarker);
            sb.AppendLine("// Generated by HB_Cheat (Tools > HB > Cheat). Do not edit.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine($"#if {CheatDefines.CoreDefine} && {settings.UserDefine}");
            sb.AppendLine("namespace HB.Cheat.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    internal static class HBCheatRegistry_{Sanitize(assemblyName)}");
            sb.AppendLine("    {");
            sb.AppendLine("        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("        private static void Register()");
            sb.AppendLine("        {");

            // 런타임 옵션 (어셈블리마다 같은 값을 넣는다)
            sb.AppendLine("            var options = global::HB.Cheat.CheatSystem.Options;");
            sb.AppendLine($"            options.InitMode = global::HB.Cheat.CheatInitMode.{(settings.InitMode == CheatInitModeSetting.Manual ? "Manual" : "AutoOnGameStart")};");
            sb.AppendLine($"            options.PreferConsoleTab = {Bool(settings.PreferConsoleTab)};");
            sb.AppendLine($"            options.ShowFloatingButton = {Bool(settings.ShowFloatingButton)};");
            sb.AppendLine($"            options.ToggleKey = {Str(settings.ToggleKey)};");
            sb.AppendLine($"            options.ServerTimeoutSeconds = {Flt(settings.ServerTimeoutSeconds)};");
            sb.AppendLine($"            options.HistoryCapacity = {settings.HistoryCapacity};");
            sb.AppendLine($"            options.DontDestroyOnLoad = {Bool(settings.DontDestroyOnLoad)};");
            sb.AppendLine($"            options.AllowRawFromAutomation = {Bool(settings.AllowRawFromAutomation)};");
            sb.AppendLine();

            // Provider는 타입마다 하나만 만든다
            var providers = cheats.SelectMany(c => c.Args).Where(a => a.Options != null).Select(a => a.Options.Provider).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            for (int i = 0; i < providers.Count; i++)
                sb.AppendLine($"            var provider{i} = new {TypeName(providers[i])}();");
            if (providers.Count > 0) sb.AppendLine();

            foreach (var cheat in cheats)
                AppendCheat(sb, cheat, providers);

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            return sb.ToString().Replace("\r\n", "\n");
        }

        private static void AppendCheat(StringBuilder sb, ScannedCheat cheat, List<Type> providers)
        {
            var a = cheat.Attribute;
            const string ns = "global::HB.Cheat.";
            string indent = "                ";

            sb.AppendLine($"            global::HB.Cheat.CheatRegistry.Register(new {ns}CheatDefinition");
            sb.AppendLine("            {");
            sb.AppendLine($"{indent}Id = {Str(a.Id)},");
            sb.AppendLine($"{indent}Path = {Str(a.Path)},");
            sb.AppendLine($"{indent}Target = {ns}CheatTarget.{a.Target},");
            if (!string.IsNullOrEmpty(a.Description)) sb.AppendLine($"{indent}Description = {Str(a.Description)},");
            if (a.Order != 0) sb.AppendLine($"{indent}Order = {a.Order},");

            if (cheat.Args.Count > 0)
            {
                sb.AppendLine($"{indent}Args = new {ns}CheatArgDefinition[]");
                sb.AppendLine($"{indent}{{");
                foreach (var arg in cheat.Args)
                    AppendArg(sb, arg, providers, indent + "    ");
                sb.AppendLine($"{indent}}},");
            }

            string call = $"{TypeName(cheat.Method.DeclaringType)}.{cheat.Method.Name}({string.Join(", ", cheat.Args.Select((arg, i) => $"a.Get<{TypeName(arg.Parameter.ParameterType)}>({i})"))})";
            if (a.Target == CheatTarget.Server)
            {
                sb.AppendLine($"{indent}ServerCommandBuilder = a => {call},");
            }
            else
            {
                var returnType = cheat.Method.ReturnType;
                string body;
                if (returnType == typeof(void)) body = $"{{ {call}; return {ns}CheatResult.Ok(); }}";
                else if (returnType == typeof(bool)) body = $"{call} ? {ns}CheatResult.Ok() : {ns}CheatResult.Fail(\"returned false\")";
                else if (returnType == typeof(string)) body = $"{ns}CheatResult.Ok({call})";
                else body = call;
                sb.AppendLine($"{indent}LocalInvoker = a => {body},");
            }
            sb.AppendLine("            });");
        }

        private static void AppendArg(StringBuilder sb, ScannedArg arg, List<Type> providers, string indent)
        {
            const string ns = "global::HB.Cheat.";
            var p = arg.Parameter;
            var parts = new List<string>
            {
                $"Name = {Str(p.Name)}",
                $"ValueType = {ns}CheatValueType.{arg.ValueType}",
                $"Editor = {ns}CheatEditorKind.{arg.Editor}"
            };
            if (!string.IsNullOrEmpty(arg.Label)) parts.Add($"Label = {Str(arg.Label)}");

            if (arg.ValueType == CheatValueType.Enum)
            {
                var enumType = p.ParameterType;
                parts.Add($"EnumType = typeof({TypeName(enumType)})");
                var options = Enum.GetNames(enumType).Select(n => $"new {ns}CheatOption({TypeName(enumType)}.@{n}, {Str(n)})");
                parts.Add($"FixedOptions = new {ns}CheatOption[] {{ {string.Join(", ", options)} }}");
            }

            if (arg.Range != null && arg.Editor == CheatEditorKind.Number)
            {
                parts.Add("HasRange = true");
                parts.Add($"Min = {Dbl(arg.Range.Min)}");
                parts.Add($"Max = {Dbl(arg.Range.Max)}");
            }
            double step = arg.Range != null && arg.Range.Step > 0 ? arg.Range.Step : 1;
            if (arg.Editor == CheatEditorKind.Number && step != 1) parts.Add($"Step = {Dbl(step)}");

            if (arg.Options != null)
            {
                parts.Add($"Provider = provider{providers.IndexOf(arg.Options.Provider)}");
                if (arg.DependsOn.Length > 0) parts.Add($"DependsOn = new int[] {{ {string.Join(", ", arg.DependsOn)} }}");
            }

            if (p.HasDefaultValue && p.DefaultValue != null)
                parts.Add($"DefaultValue = {Literal(p.DefaultValue, p.ParameterType)}");

            sb.AppendLine($"{indent}new {ns}CheatArgDefinition {{ {string.Join(", ", parts)} }},");
        }

        #endregion

        #region Code Helpers

        private static string TypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return "global::" + type.FullName.Replace('+', '.');
        }

        private static string Literal(object value, Type type)
        {
            if (type.IsEnum) return $"({TypeName(type)}){Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}";
            switch (value)
            {
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case long l: return l.ToString(CultureInfo.InvariantCulture) + "L";
                case float f: return Flt(f);
                case double d: return Dbl(d);
                case bool b: return Bool(b);
                case string s: return Str(s);
                default: return "null";
            }
        }

        private static string Dbl(double d)
        {
            if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
            if (double.IsNaN(d)) return "double.NaN";
            return d.ToString("R", CultureInfo.InvariantCulture) + "d";
        }

        internal static string Flt(float f)
        {
            if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
            if (float.IsNaN(f)) return "float.NaN";
            return f.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        private static string Bool(bool b) => b ? "true" : "false";

        internal static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // C# 문자열 리터럴에 들어갈 수 없는 줄바꿈 문자(U+0085 · U+2028 · U+2029)와 제어 문자
                        if (c < 0x20 || c == '\u0085' || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        #endregion
    }
}
