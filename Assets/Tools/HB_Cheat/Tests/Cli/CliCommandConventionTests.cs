using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor;

namespace HB.Cheat.Tests
{
    /// <summary>
    /// HB.* 어셈블리의 Unity CLI 명령이 규칙(https://github.com/MoewMeowH/CustomUnityTools/blob/main/CLI_COMMANDS.md)을 지키는지 검사한다.
    /// 다른 프로젝트에서 쓰려면 이 파일을 복사해 AssemblyPrefix만 바꾼다.
    /// </summary>
    public class CliCommandConventionTests
    {
        private const string AssemblyPrefix = "HB.";

        // com.unity.pipeline 1.0.0이 쓰는 그룹 (creating-commands.md "Groups in the package") + global · fixture
        private static readonly HashSet<string> PipelineGroups = new HashSet<string>
        {
            "animation", "asset", "authoring", "build", "capture", "component", "console", "diagnostics", "editor",
            "gameobject", "job", "lighting", "material", "navmesh", "occlusion", "package", "pipeline", "prefab",
            "runtime", "scene", "script", "settings", "test", "global", "fixture"
        };

        // 실행기가 먼저 가져가는 옵션 이름 — 인자로 쓰면 값이 들어오지 않는다
        private static readonly HashSet<string> ReservedArgs = new HashSet<string> { "format", "json", "fields", "verbose", "omit-nulls" };

        private static readonly Regex KebabCase = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        [Test]
        public void EveryCommand_FollowsConventions()
        {
            var commands = TypeCache.GetMethodsWithAttribute<CliGroupCommandAttribute>()
                .Concat(TypeCache.GetMethodsWithAttribute<CliCommandAttribute>())
                .Where(m => m.DeclaringType.Assembly.GetName().Name.StartsWith(AssemblyPrefix))
                .Distinct()
                .ToList();
            Assume.That(commands, Is.Not.Empty, $"{AssemblyPrefix}* 어셈블리에 CLI 명령이 없음");

            var problems = new StringBuilder();
            foreach (var method in commands)
            {
                foreach (string problem in Check(method))
                    problems.AppendLine($"{method.DeclaringType.FullName}.{method.Name}: {problem}");
            }
            Assert.IsEmpty(problems.ToString(), "CLI_COMMANDS.md 규칙 위반:\n" + problems);
        }

        private static IEnumerable<string> Check(MethodInfo method)
        {
            if (method.GetCustomAttribute<CliCommandAttribute>() != null)
            {
                yield return "[CliCommand] 대신 [CliGroupCommand(Group, ...)]";
                yield break;
            }

            var attr = method.GetCustomAttribute<CliGroupCommandAttribute>();
            if (PipelineGroups.Contains(attr.Group.Split('.')[0]))
                yield return $"그룹 '{attr.Group}'는 Pipeline 그룹 — 도구 이름 그룹을 쓴다";
            if (attr.Effect == CommandEffect.Unspecified)
                yield return "Effect를 정한다 (Unspecified 금지)";
            if (string.IsNullOrWhiteSpace(attr.Description))
                yield return "설명이 비어 있음";

            if (!method.IsStatic)
                yield return "static 메서드여야 함 (인스턴스 메서드는 등록되지 않음)";
            if (method.ReturnType != typeof(ExitCode) && method.ReturnType != typeof(Task<ExitCode>))
                yield return "반환형은 ExitCode 또는 Task<ExitCode>";

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(CommandContext))
                yield return "첫 인자는 CommandContext";

            foreach (var parameter in parameters.Where(p => p.ParameterType != typeof(CommandContext)))
            {
                var arg = parameter.GetCustomAttribute<CliArgAttribute>();
                if (arg == null)
                {
                    yield return $"인자 '{parameter.Name}'에 [CliArg] 없음";
                    continue;
                }
                if (!KebabCase.IsMatch(arg.Name))
                    yield return $"인자 '{arg.Name}'는 kebab-case로";
                if (ReservedArgs.Contains(arg.Name))
                    yield return $"인자 '{arg.Name}'는 예약 이름";
                if (string.IsNullOrWhiteSpace(arg.Description))
                    yield return $"인자 '{arg.Name}' 설명이 비어 있음";
            }
        }
    }
}
