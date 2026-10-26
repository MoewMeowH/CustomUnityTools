using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 계획에 필요한 Xcode 프로젝트 사실. 빌드 전 점검은 기본값(Unity 템플릿 구성 4개, GameAssembly 있음),
    /// 빌드 뒤 적용은 생성된 Xcode 프로젝트에서 읽은 값을 쓴다.
    /// </summary>
    public sealed class XcodePlanInput
    {
        public static readonly string[] DefaultConfigs = { "Debug", "Release", "ReleaseForProfiling", "ReleaseForRunning" };

        public EBuildType BuildType = EBuildType.DEV;
        public IReadOnlyList<string> Configs = DefaultConfigs;
        public bool HasGameAssembly = true;

        /// <summary> Files 원본 경로의 기준 (Unity 프로젝트 루트) </summary>
        public string ProjectRoot = string.Empty;

        /// <summary> HB_Build CustomData "ProvisionType" (aps-environment 경고용, 없으면 null) </summary>
        public string ProvisionType;
    }

    public enum EPlistMerge
    {
        /// <summary> 단일 값: 같으면 하나로, 다르면 충돌 </summary>
        Scalar,
        /// <summary> 문자열 배열: 합집합 (기존 값과도 합친다) </summary>
        StringList,
        /// <summary> SKAdNetworkItems: SKAdNetworkIdentifier 기준 합집합 </summary>
        SkAdNetworkList,
        /// <summary> 그 밖의 복합 값: 완전히 같아야 함 </summary>
        Exact,
    }

    public sealed class PlannedPlist
    {
        public string Key;
        public PlistNode Value;
        public EPlistMerge Merge;
        public List<string> Sources = new List<string>();
        /// <summary> 기존 값 덮어쓰기를 허용한 결정 행 (없으면 null) </summary>
        public XcodeDecision Overwrite;
    }

    public sealed class PlannedBuildSetting
    {
        /// <summary> App · UnityFramework · GameAssembly 중 하나 (All은 펼쳐 둠) </summary>
        public EXcodeTarget Target;
        public string Config;
        public string Key;
        public EBuildSettingOp Op;
        /// <summary> Set은 1개, Add는 더할 값 목록 </summary>
        public List<string> Values = new List<string>();
        public List<string> Sources = new List<string>();
        public XcodeDecision Overwrite;
    }

    public sealed class PlannedFramework
    {
        public EXcodeTarget Target;
        public string Name;
        public bool Optional;
        public List<string> Sources = new List<string>();
        /// <summary> 이미 다른 Required/Optional로 링크돼 있을 때 바꿔도 되는지 (결정 행) </summary>
        public XcodeDecision Overwrite;
    }

    public sealed class PlannedFile
    {
        public EXcodeTarget Target;
        public string SourceFullPath;
        /// <summary> Xcode 프로젝트 폴더 기준, '/' 구분 </summary>
        public string DestinationPath;
        public EXcodeFileMode Mode;
        public bool AddToResources;
        public string Hash;
        public List<string> Sources = new List<string>();
        public XcodeDecision Overwrite;
    }

    /// <summary> 공용 + 묶음을 합치고 검증한 최종 적용 계획. Errors가 있으면 적용하지 않는다 </summary>
    public sealed class XcodePlan
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public readonly List<PlannedPlist> Info = new List<PlannedPlist>();
        public readonly List<PlannedPlist> Entitlements = new List<PlannedPlist>();
        public readonly List<PlannedBuildSetting> BuildSettings = new List<PlannedBuildSetting>();
        public readonly List<PlannedFramework> Frameworks = new List<PlannedFramework>();
        public readonly List<PlannedFile> Files = new List<PlannedFile>();
        public readonly List<EXcodeCapability> Capabilities = new List<EXcodeCapability>();

        /// <summary> 기존 값 덮어쓰기만 하는 결정 행 (적용 때 실제로 쓰였는지 확인) </summary>
        public readonly List<XcodeDecision> OverwriteDecisions = new List<XcodeDecision>();

        public bool HasChanges =>
            Info.Count + Entitlements.Count + BuildSettings.Count + Frameworks.Count + Files.Count + Capabilities.Count > 0;

        /// <summary> 미리보기 · 로그용 요약 </summary>
        public List<string> Describe()
        {
            var lines = new List<string>();
            foreach (var c in Capabilities) lines.Add($"Capability  {c}");
            foreach (var p in Info) lines.Add($"Info  {p.Key} = {p.Value}  ← {string.Join(", ", p.Sources)}");
            foreach (var p in Entitlements) lines.Add($"Entitlement  {p.Key} = {p.Value}  ← {string.Join(", ", p.Sources)}");
            foreach (var b in BuildSettings)
                lines.Add($"BuildSetting  [{b.Target}/{b.Config}] {b.Key} {(b.Op == EBuildSettingOp.Set ? "=" : "+=")} {string.Join(" ", b.Values)}  ← {string.Join(", ", b.Sources)}");
            foreach (var f in Frameworks) lines.Add($"Framework  [{f.Target}] {f.Name}{(f.Optional ? " (Optional)" : string.Empty)}  ← {string.Join(", ", f.Sources)}");
            foreach (var f in Files) lines.Add($"File  {f.DestinationPath} ({f.Mode}, {f.Target}{(f.AddToResources ? ", Resources" : string.Empty)})  ← {string.Join(", ", f.Sources)}");
            return lines;
        }
    }

    /// <summary>
    /// 공용 HBXcodeSettings와 켜진 HBXcodeFragment를 합쳐 XcodePlan을 만듭니다 (iOS 모듈 없이 동작).
    /// 규칙: 값마다 주인은 하나(XcodeOwnership), 단일 값이 다르면 충돌 → 결정 행(Decisions)으로만 해결, 목록 값은 합집합.
    /// </summary>
    public static class XcodeSettingsPlanner
    {
        public const string SKADNETWORK_ITEMS = "SKAdNetworkItems";
        public const string SKADNETWORK_ID = "SKAdNetworkIdentifier";
        public const string LOCALIZATIONS_KEY = "CFBundleLocalizations";
        public const string APS_ENVIRONMENT = "aps-environment";

        private sealed class Contribution<T>
        {
            public XcodeSettingsSnapshot.SourceData Source;
            public string Where;
            public T Value;
        }

        private sealed class PlistValue
        {
            public PlistNode Node;
            public EPlistMerge Merge;
        }

        private sealed class BuildSettingValue
        {
            public EBuildSettingOp Op;
            public string Value;
        }

        private sealed class FileValue
        {
            public string FullPath;
            public string Hash;
            public EXcodeFileMode Mode;
            public EXcodeTarget Target;
            public bool AddToResources;
        }

        public static XcodePlan Plan(HBXcodeSettings settings, XcodePlanInput input) =>
            settings == null ? new XcodePlan() : Plan(XcodeSettingsSnapshot.Capture(settings), input);

        public static XcodePlan Plan(XcodeSettingsSnapshot snapshot, XcodePlanInput input)
        {
            var plan = new XcodePlan();
            if (snapshot == null) return plan;

            plan.Warnings.AddRange(snapshot.Warnings);
            // 켜진 묶음이 비었거나 Missing이면 그 SDK 설정이 통째로 빠진다: LIVE는 실패, DEV · QA는 경고
            foreach (var missing in snapshot.MissingFragments)
            {
                if (input.BuildType == EBuildType.LIVE)
                    plan.Errors.Add($"{missing} LIVE builds fail on this (the fragment's settings would be silently left out).");
                else
                    plan.Warnings.Add($"{missing} Its settings are left out (LIVE builds fail on this).");
            }
            var sources = snapshot.Sources;
            var decisions = snapshot.Decisions;
            ValidateDecisions(decisions, plan);
            var usedChoices = new HashSet<XcodeDecision>();

            var info = new Dictionary<string, List<Contribution<PlistValue>>>(StringComparer.Ordinal);
            var ents = new Dictionary<string, List<Contribution<PlistValue>>>(StringComparer.Ordinal);
            var capabilities = new List<EXcodeCapability>();

            foreach (var src in sources)
            {
                CollectCapabilities(src, input, info, ents, capabilities, plan);
                CollectPlist(src, src.Data.Info, "Info", input, info, plan, key => XcodeOwnership.TryGetInfoOwner(key, out var o) ? o : null);
                CollectPlist(src, src.Data.Entitlements, "Entitlements", input, ents, plan, key => XcodeOwnership.TryGetEntitlementOwner(key, out var o) ? o : null);
            }

            // 파일을 먼저 합쳐야 지역화 목록이 결정 행으로 탈락한 파일을 포함하지 않는다
            var fileContribs = new Dictionary<string, List<Contribution<FileValue>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in sources)
                CollectFiles(src, input, fileContribs, plan);
            var fileWinners = new Dictionary<string, Contribution<FileValue>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fileContribs)
                MergeFile(kv.Key, kv.Value, decisions, usedChoices, plan, fileWinners);
            CheckOverlappingFiles(plan);

            if (snapshot.FillLocalizationsFromLproj)
            {
                foreach (var f in plan.Files)
                {
                    string code = GetLprojCode(f.DestinationPath);
                    if (code == null || f.Target != EXcodeTarget.App || !f.AddToResources) continue;
                    var from = fileWinners[f.DestinationPath];
                    Add(info, LOCALIZATIONS_KEY, new Contribution<PlistValue>
                    {
                        Source = from.Source,
                        Where = from.Where + " (*.lproj → CFBundleLocalizations)",
                        Value = new PlistValue { Node = PlistNode.StringArray(new[] { code }), Merge = EPlistMerge.StringList },
                    });
                }
            }

            foreach (var kv in info)
                MergePlist(EXcodeChangeKind.Info, kv.Key, kv.Value, decisions, usedChoices, plan, plan.Info);
            foreach (var kv in ents)
                MergePlist(EXcodeChangeKind.Entitlement, kv.Key, kv.Value, decisions, usedChoices, plan, plan.Entitlements);

            var buildSettings = new Dictionary<(EXcodeTarget, string, string), List<Contribution<BuildSettingValue>>>();
            var frameworks = new Dictionary<(EXcodeTarget, string), List<Contribution<bool>>>();
            foreach (var src in sources)
            {
                CollectBuildSettings(src, input, buildSettings, plan);
                CollectFrameworks(src, input, frameworks, plan);
            }

            foreach (var kv in buildSettings)
                MergeBuildSetting(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value, decisions, usedChoices, plan);
            foreach (var kv in frameworks)
                MergeFramework(kv.Key.Item1, kv.Key.Item2, kv.Value, decisions, usedChoices, plan);

            plan.Capabilities.AddRange(capabilities.Distinct());
            CheckApsEnvironment(plan, input);
            CheckLiveSecurity(plan, input);

            foreach (var d in decisions)
            {
                // 덮어쓰기도 켠 행은 고른 소스가 안 쓰여도 기존 값 덮어쓰기에 필요하다
                if (d.HasChoice && !usedChoices.Contains(d) && !d.OverwriteExisting)
                    plan.Warnings.Add($"Decision {d} has no conflict to resolve. Remove it (left-over decisions hide future conflicts).");
                if (!d.HasChoice && !d.OverwriteExisting)
                    plan.Warnings.Add($"Decision {d} neither chooses a source nor allows overwriting. It does nothing.");
                if (d.OverwriteExisting)
                    plan.OverwriteDecisions.Add(d);
            }

            return plan;
        }

        // ───────────── 소스 ─────────────

        private static bool Applies(EXcodeBuildTypes types, EBuildType buildType, string where, XcodePlan plan)
        {
            if (types == 0)
            {
                plan.Warnings.Add($"{where} has no build type selected, so it is never applied.");
                return false;
            }
            var flag = buildType switch
            {
                EBuildType.DEV => EXcodeBuildTypes.DEV,
                EBuildType.QA => EXcodeBuildTypes.QA,
                EBuildType.LIVE => EXcodeBuildTypes.LIVE,
                _ => (EXcodeBuildTypes)0,
            };
            return (types & flag) != 0;
        }

        private static void Add<TKey, T>(Dictionary<TKey, List<Contribution<T>>> map, TKey key, Contribution<T> c)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<Contribution<T>>();
            list.Add(c);
        }

        // ───────────── Capabilities ─────────────

        private static void CollectCapabilities(XcodeSettingsSnapshot.SourceData src, XcodePlanInput input,
            Dictionary<string, List<Contribution<PlistValue>>> info,
            Dictionary<string, List<Contribution<PlistValue>>> ents,
            List<EXcodeCapability> capabilities, XcodePlan plan)
        {
            var rows = src.Data.Capabilities ?? new List<XcodeCapabilityEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.Enabled) continue;
                string where = $"{src.Name}.Capabilities[{i}] {row.Capability}";
                if (!Applies(row.BuildTypes, input.BuildType, where, plan)) continue;

                var values = Clean(row.Values);
                void Ent(string key, PlistNode node, EPlistMerge merge) =>
                    Add(ents, key, new Contribution<PlistValue> { Source = src, Where = where, Value = new PlistValue { Node = node, Merge = merge } });
                bool RequireValues()
                {
                    if (values.Count > 0) return true;
                    plan.Errors.Add($"{where} needs at least one value.");
                    return false;
                }

                switch (row.Capability)
                {
                    case EXcodeCapability.PushNotifications:
                        string aps = row.ApsEnvironment?.Trim() ?? string.Empty;
                        if (aps != "development" && aps != "production")
                        {
                            plan.Errors.Add($"{where}: ApsEnvironment must be 'development' or 'production' (was '{aps}').");
                            continue;
                        }
                        Ent(APS_ENVIRONMENT, PlistNode.String(aps), EPlistMerge.Scalar);
                        break;
                    case EXcodeCapability.SignInWithApple:
                        Ent("com.apple.developer.applesignin", PlistNode.StringArray(new[] { "Default" }), EPlistMerge.StringList);
                        break;
                    case EXcodeCapability.AssociatedDomains:
                        if (!RequireValues()) continue;
                        Ent("com.apple.developer.associated-domains", PlistNode.StringArray(values), EPlistMerge.StringList);
                        break;
                    case EXcodeCapability.BackgroundModes:
                        if (!RequireValues()) continue;
                        Add(info, "UIBackgroundModes", new Contribution<PlistValue>
                        {
                            Source = src, Where = where,
                            Value = new PlistValue { Node = PlistNode.StringArray(values), Merge = EPlistMerge.StringList },
                        });
                        break;
                    case EXcodeCapability.InAppPurchase:
                        break;
                    case EXcodeCapability.GameCenter:
                        Ent("com.apple.developer.game-center", PlistNode.Bool(true), EPlistMerge.Scalar);
                        break;
                    case EXcodeCapability.KeychainSharing:
                        if (!RequireValues()) continue;
                        Ent("keychain-access-groups", PlistNode.StringArray(values), EPlistMerge.StringList);
                        break;
                    case EXcodeCapability.AppGroups:
                        if (!RequireValues()) continue;
                        Ent("com.apple.security.application-groups", PlistNode.StringArray(values), EPlistMerge.StringList);
                        break;
                    default:
                        plan.Errors.Add($"{where}: unsupported capability.");
                        continue;
                }
                capabilities.Add(row.Capability);
            }
        }

        // ───────────── Info · Entitlements ─────────────

        private static void CollectPlist(XcodeSettingsSnapshot.SourceData src, List<XcodePlistEntry> rows, string tab, XcodePlanInput input,
            Dictionary<string, List<Contribution<PlistValue>>> map, XcodePlan plan, Func<string, string> ownerOf)
        {
            rows ??= new List<XcodePlistEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.Enabled) continue;
                string key = row.Key?.Trim() ?? string.Empty;
                string where = $"{src.Name}.{tab}[{i}] '{key}'";
                if (!Applies(row.BuildTypes, input.BuildType, where, plan)) continue;

                if (key.Length == 0)
                {
                    plan.Errors.Add($"{src.Name}.{tab}[{i}] has an empty key.");
                    continue;
                }
                string owner = ownerOf(key);
                if (owner != null)
                {
                    plan.Errors.Add($"{where}: this key is owned by {owner}. Set it there, not in Xcode settings.");
                    continue;
                }
                if (!TryBuildPlistValue(row, key, out var value, out string error))
                {
                    plan.Errors.Add($"{where}: {error}");
                    continue;
                }
                Add(map, key, new Contribution<PlistValue> { Source = src, Where = where, Value = value });
            }
        }

        private static bool TryBuildPlistValue(XcodePlistEntry row, string key, out PlistValue value, out string error)
        {
            value = null;
            error = null;
            string text = row.Value?.Trim() ?? string.Empty;
            switch (row.Type)
            {
                case EPlistValueType.String:
                    value = new PlistValue { Node = PlistNode.String(row.Value ?? string.Empty), Merge = EPlistMerge.Scalar };
                    return true;
                case EPlistValueType.Bool:
                    if (!bool.TryParse(text, out bool b))
                    {
                        error = $"Bool value must be true or false (was '{text}').";
                        return false;
                    }
                    value = new PlistValue { Node = PlistNode.Bool(b), Merge = EPlistMerge.Scalar };
                    return true;
                case EPlistValueType.Integer:
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    {
                        error = $"Integer value is invalid ('{text}').";
                        return false;
                    }
                    value = new PlistValue { Node = PlistNode.Integer(l), Merge = EPlistMerge.Scalar };
                    return true;
                case EPlistValueType.Real:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    {
                        error = $"Real value is invalid ('{text}', use '.' as the decimal point).";
                        return false;
                    }
                    if (!TryToPlistReal(d, out var real, out error))
                        return false;
                    value = new PlistValue { Node = real, Merge = EPlistMerge.Scalar };
                    return true;
                case EPlistValueType.StringArray:
                    var items = Clean(row.Values);
                    if (items.Count == 0)
                    {
                        error = "StringArray has no values.";
                        return false;
                    }
                    value = new PlistValue { Node = PlistNode.StringArray(items), Merge = EPlistMerge.StringList };
                    return true;
                case EPlistValueType.Raw:
                    PlistNode node;
                    try
                    {
                        node = PlistNode.ParseValueXml(row.RawXml);
                    }
                    catch (FormatException ex)
                    {
                        error = ex.Message;
                        return false;
                    }
                    node = NormalizeReals(node, out error);
                    if (node == null)
                        return false;
                    if (key == SKADNETWORK_ITEMS)
                    {
                        if (!IsSkAdNetworkList(node))
                        {
                            error = $"{SKADNETWORK_ITEMS} must be an <array> of <dict> each with a <string> {SKADNETWORK_ID}.";
                            return false;
                        }
                        value = new PlistValue { Node = node, Merge = EPlistMerge.SkAdNetworkList };
                        return true;
                    }
                    var merge = node.IsStringArray ? EPlistMerge.StringList : node.IsScalar ? EPlistMerge.Scalar : EPlistMerge.Exact;
                    value = new PlistValue { Node = node, Merge = merge };
                    return true;
                default:
                    error = "Unsupported value type.";
                    return false;
            }
        }

        /// <summary>
        /// Xcode plist API는 실수를 float로 쓴다. 계획 값을 float 정밀도로 맞춰 기존 값(float)과 같은 값이 충돌로 보이지 않게 하고,
        /// float로 나타낼 수 없는 값(무한대 · NaN · 범위 밖)은 거부한다.
        /// </summary>
        private static bool TryToPlistReal(double d, out PlistNode node, out string error)
        {
            node = null;
            error = null;
            float f = (float)d;
            if (double.IsNaN(d) || double.IsInfinity(d) || float.IsInfinity(f))
            {
                error = $"Real value {d.ToString(CultureInfo.InvariantCulture)} cannot be written by the Xcode plist API (float range).";
                return false;
            }
            node = PlistNode.Real(f);
            return true;
        }

        /// <summary> 값 안의 모든 실수를 float 정밀도로 맞춘다. 쓸 수 없는 실수가 있으면 null </summary>
        public static PlistNode NormalizeReals(PlistNode node, out string error)
        {
            error = null;
            switch (node.Kind)
            {
                case PlistNode.EKind.Real:
                    return TryToPlistReal(node.RealValue, out var real, out error) ? real : null;
                case PlistNode.EKind.Array:
                    var items = new List<PlistNode>();
                    foreach (var item in node.Items)
                    {
                        var n = NormalizeReals(item, out error);
                        if (n == null) return null;
                        items.Add(n);
                    }
                    return PlistNode.Array(items);
                case PlistNode.EKind.Dict:
                    var entries = new List<KeyValuePair<string, PlistNode>>();
                    foreach (var e in node.Entries)
                    {
                        var n = NormalizeReals(e.Value, out error);
                        if (n == null) return null;
                        entries.Add(new KeyValuePair<string, PlistNode>(e.Key, n));
                    }
                    return PlistNode.Dict(entries);
                default:
                    return node;
            }
        }

        public static bool IsSkAdNetworkList(PlistNode node) =>
            node.Kind == PlistNode.EKind.Array &&
            node.Items.All(i => i.Kind == PlistNode.EKind.Dict && i.Get(SKADNETWORK_ID)?.Kind == PlistNode.EKind.String);

        /// <summary> 목록 값 합치기 (계획 안의 합치기와 적용 때 기존 값과의 합치기에 같이 쓴다) </summary>
        public static PlistNode Union(EPlistMerge merge, PlistNode a, PlistNode b)
        {
            if (merge == EPlistMerge.StringList)
            {
                var result = new List<string>();
                foreach (var item in a.Items.Concat(b.Items))
                {
                    if (!result.Contains(item.Text, StringComparer.Ordinal))
                        result.Add(item.Text);
                }
                return PlistNode.StringArray(result);
            }
            if (merge == EPlistMerge.SkAdNetworkList)
            {
                var result = new List<PlistNode>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in a.Items.Concat(b.Items))
                {
                    if (ids.Add(item.Get(SKADNETWORK_ID).Text.Trim()))
                        result.Add(item);
                }
                return PlistNode.Array(result);
            }
            throw new InvalidOperationException($"{merge} is not a list merge.");
        }

        private static void MergePlist(EXcodeChangeKind kind, string key, List<Contribution<PlistValue>> contribs,
            List<XcodeDecision> decisions, HashSet<XcodeDecision> usedChoices, XcodePlan plan, List<PlannedPlist> output)
        {
            string conflict = TryMergePlist(contribs, out var merged);
            if (conflict != null)
            {
                var decision = FindDecision(decisions, kind, key, EXcodeTarget.All, string.Empty, needsChoice: true);
                if (decision == null)
                {
                    plan.Errors.Add($"{kind} '{key}': {conflict} — {DescribeContribs(contribs, c => c.Value.Node.ToString())}. " +
                                    "Add a decision (Decisions) that chooses one source.");
                    return;
                }
                usedChoices.Add(decision);
                var chosen = contribs.Where(c => c.Source.Index == decision.ChosenIndex).ToList();
                if (chosen.Count == 0)
                {
                    plan.Errors.Add($"{kind} '{key}': decision chooses '{decision.ChosenName}', which has no value for this key.");
                    return;
                }
                conflict = TryMergePlist(chosen, out merged);
                if (conflict != null)
                {
                    plan.Errors.Add($"{kind} '{key}': the chosen source '{decision.ChosenName}' itself has conflicting values ({conflict}).");
                    return;
                }
                contribs = chosen;
            }

            output.Add(new PlannedPlist
            {
                Key = key,
                Value = merged.Node,
                Merge = merged.Merge,
                Sources = contribs.Select(c => c.Source.Name).Distinct().ToList(),
                Overwrite = FindDecision(decisions, kind, key, EXcodeTarget.All, string.Empty, needsChoice: false, needsOverwrite: true),
            });
        }

        /// <summary> 합칠 수 없으면 이유, 합쳤으면 null </summary>
        private static string TryMergePlist(List<Contribution<PlistValue>> contribs, out PlistValue merged)
        {
            merged = contribs[0].Value;
            for (int i = 1; i < contribs.Count; i++)
            {
                var next = contribs[i].Value;
                if (merged.Merge != next.Merge || (merged.Merge == EPlistMerge.Scalar && merged.Node.Kind != next.Node.Kind))
                    return "value types differ";

                switch (merged.Merge)
                {
                    case EPlistMerge.StringList:
                    case EPlistMerge.SkAdNetworkList:
                        merged = new PlistValue { Node = Union(merged.Merge, merged.Node, next.Node), Merge = merged.Merge };
                        break;
                    default:
                        if (!merged.Node.ValueEquals(next.Node))
                            return "values differ";
                        break;
                }
            }
            return null;
        }

        // ───────────── Build Settings ─────────────

        private static IEnumerable<EXcodeTarget> ExpandTargets(EXcodeTarget target, XcodePlanInput input, string where, XcodePlan plan)
        {
            switch (target)
            {
                case EXcodeTarget.All:
                    yield return EXcodeTarget.App;
                    yield return EXcodeTarget.UnityFramework;
                    if (input.HasGameAssembly) yield return EXcodeTarget.GameAssembly;
                    break;
                case EXcodeTarget.GameAssembly when !input.HasGameAssembly:
                    plan.Errors.Add($"{where}: this Xcode project has no GameAssembly target.");
                    break;
                default:
                    yield return target;
                    break;
            }
        }

        private static void CollectBuildSettings(XcodeSettingsSnapshot.SourceData src, XcodePlanInput input,
            Dictionary<(EXcodeTarget, string, string), List<Contribution<BuildSettingValue>>> map, XcodePlan plan)
        {
            var rows = src.Data.BuildSettings ?? new List<XcodeBuildSettingEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.Enabled) continue;
                string key = row.Key?.Trim() ?? string.Empty;
                string where = $"{src.Name}.BuildSettings[{i}] '{key}'";
                if (!Applies(row.BuildTypes, input.BuildType, where, plan)) continue;

                if (key.Length == 0)
                {
                    plan.Errors.Add($"{src.Name}.BuildSettings[{i}] has an empty key.");
                    continue;
                }
                if (XcodeOwnership.TryGetBuildSettingOwner(key, out string owner))
                {
                    plan.Errors.Add($"{where}: this build setting is owned by {owner}. Set it there, not in Xcode settings.");
                    continue;
                }
                string value = row.Value?.Trim() ?? string.Empty;
                if (row.Op == EBuildSettingOp.Add && value.Length == 0)
                {
                    plan.Errors.Add($"{where}: Add needs a value.");
                    continue;
                }
                if (row.Op == EBuildSettingOp.Set &&
                    XcodeOwnership.ListBuildSettingKeys.Contains(XcodeOwnership.BaseBuildSettingKey(key)) &&
                    !value.Contains("$(inherited)"))
                {
                    plan.Warnings.Add($"{where}: Set on a list setting without $(inherited) hides values from CocoaPods xcconfig. Use Add, or include $(inherited).");
                }

                IEnumerable<string> configs;
                string config = row.Config?.Trim() ?? string.Empty;
                if (config.Length == 0)
                {
                    configs = input.Configs;
                }
                else if (input.Configs.Contains(config))
                {
                    configs = new[] { config };
                }
                else
                {
                    plan.Errors.Add($"{where}: config '{config}' does not exist (configs: {string.Join(", ", input.Configs)}).");
                    continue;
                }

                foreach (var t in ExpandTargets(row.Target, input, where, plan))
                {
                    foreach (var c in configs)
                    {
                        Add(map, (t, c, key), new Contribution<BuildSettingValue>
                        {
                            Source = src, Where = where, Value = new BuildSettingValue { Op = row.Op, Value = value },
                        });
                    }
                }
            }
        }

        private static void MergeBuildSetting(EXcodeTarget target, string config, string key, List<Contribution<BuildSettingValue>> contribs,
            List<XcodeDecision> decisions, HashSet<XcodeDecision> usedChoices, XcodePlan plan)
        {
            string conflict = TryMergeBuildSetting(contribs, out var op, out var values);
            if (conflict != null)
            {
                var decision = FindDecision(decisions, EXcodeChangeKind.BuildSetting, key, target, config, needsChoice: true);
                if (decision == null)
                {
                    plan.Errors.Add($"BuildSetting [{target}/{config}] '{key}': {conflict} — " +
                                    $"{DescribeContribs(contribs, c => $"{c.Value.Op} {c.Value.Value}")}. Add a decision (Decisions) that chooses one source.");
                    return;
                }
                usedChoices.Add(decision);
                var chosen = contribs.Where(c => c.Source.Index == decision.ChosenIndex).ToList();
                if (chosen.Count == 0)
                {
                    plan.Errors.Add($"BuildSetting [{target}/{config}] '{key}': decision chooses '{decision.ChosenName}', which has no value here.");
                    return;
                }
                conflict = TryMergeBuildSetting(chosen, out op, out values);
                if (conflict != null)
                {
                    plan.Errors.Add($"BuildSetting [{target}/{config}] '{key}': the chosen source '{decision.ChosenName}' itself has conflicting values ({conflict}).");
                    return;
                }
                contribs = chosen;
            }

            plan.BuildSettings.Add(new PlannedBuildSetting
            {
                Target = target,
                Config = config,
                Key = key,
                Op = op,
                Values = values,
                Sources = contribs.Select(c => c.Source.Name).Distinct().ToList(),
                Overwrite = FindDecision(decisions, EXcodeChangeKind.BuildSetting, key, target, config, needsChoice: false, needsOverwrite: true),
            });
        }

        private static string TryMergeBuildSetting(List<Contribution<BuildSettingValue>> contribs, out EBuildSettingOp op, out List<string> values)
        {
            op = contribs[0].Value.Op;
            values = new List<string>();
            if (contribs.Any(c => c.Value.Op != contribs[0].Value.Op))
                return "Set and Add are mixed (the result would depend on order)";

            foreach (var c in contribs)
            {
                if (!values.Contains(c.Value.Value, StringComparer.Ordinal))
                    values.Add(c.Value.Value);
            }
            if (op == EBuildSettingOp.Set && values.Count > 1)
                return "values differ";
            return null;
        }

        // ───────────── Frameworks ─────────────

        private static void CollectFrameworks(XcodeSettingsSnapshot.SourceData src, XcodePlanInput input,
            Dictionary<(EXcodeTarget, string), List<Contribution<bool>>> map, XcodePlan plan)
        {
            var rows = src.Data.Frameworks ?? new List<XcodeFrameworkEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.Enabled) continue;
                string name = row.Name?.Trim() ?? string.Empty;
                string where = $"{src.Name}.Frameworks[{i}] '{name}'";
                if (!Applies(row.BuildTypes, input.BuildType, where, plan)) continue;

                if (!name.EndsWith(".framework", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".tbd", StringComparison.OrdinalIgnoreCase))
                {
                    plan.Errors.Add($"{where}: name must end with .framework or .tbd (e.g. AdSupport.framework, libz.tbd).");
                    continue;
                }
                if (row.Optional && name.EndsWith(".tbd", StringComparison.OrdinalIgnoreCase))
                {
                    plan.Errors.Add($"{where}: Optional (weak) link is only supported for .framework.");
                    continue;
                }
                foreach (var t in ExpandTargets(row.Target, input, where, plan))
                    Add(map, (t, name), new Contribution<bool> { Source = src, Where = where, Value = row.Optional });
            }
        }

        private static void MergeFramework(EXcodeTarget target, string name, List<Contribution<bool>> contribs,
            List<XcodeDecision> decisions, HashSet<XcodeDecision> usedChoices, XcodePlan plan)
        {
            if (contribs.Any(c => c.Value != contribs[0].Value))
            {
                var decision = FindDecision(decisions, EXcodeChangeKind.Framework, name, target, string.Empty, needsChoice: true);
                if (decision == null)
                {
                    plan.Errors.Add($"Framework [{target}] '{name}': Required and Optional are mixed — " +
                                    $"{DescribeContribs(contribs, c => c.Value ? "Optional" : "Required")}. Add a decision (Decisions) that chooses one source.");
                    return;
                }
                usedChoices.Add(decision);
                var chosen = contribs.Where(c => c.Source.Index == decision.ChosenIndex).ToList();
                if (chosen.Count == 0 || chosen.Any(c => c.Value != chosen[0].Value))
                {
                    plan.Errors.Add($"Framework [{target}] '{name}': decision source '{decision.ChosenName}' has no single value here.");
                    return;
                }
                contribs = chosen;
            }

            plan.Frameworks.Add(new PlannedFramework
            {
                Target = target,
                Name = name,
                Optional = contribs[0].Value,
                Sources = contribs.Select(c => c.Source.Name).Distinct().ToList(),
                Overwrite = FindDecision(decisions, EXcodeChangeKind.Framework, name, target, string.Empty, needsChoice: false, needsOverwrite: true),
            });
        }

        // ───────────── Files ─────────────

        /// <summary> Xcode 프로젝트 안 상대 경로로 정리합니다. 쓸 수 없는 경로면 null과 이유 </summary>
        public static string NormalizeDestination(string path, out string error)
        {
            error = null;
            string p = (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (p.Length == 0)
            {
                error = "destination path is empty.";
                return null;
            }
            if (Path.IsPathRooted(p) || p.Contains(':') || (path ?? string.Empty).TrimStart().StartsWith("/"))
            {
                error = $"destination '{path}' must be relative to the Xcode project folder.";
                return null;
            }
            if (p.Split('/').Any(s => s.Length == 0 || s == "." || s == ".."))
            {
                error = $"destination '{path}' must not contain '.', '..' or empty segments.";
                return null;
            }
            foreach (var seg in p.Split('/'))
            {
                if (seg.EndsWith(".") || seg.EndsWith(" ") || seg.StartsWith(" "))
                {
                    error = $"destination '{path}' has a segment that starts or ends with a space or ends with a dot.";
                    return null;
                }
                string stem = seg.Split('.')[0].ToUpperInvariant();
                if (k_ReservedDeviceNames.Contains(stem))
                {
                    error = $"destination '{path}' uses a reserved device name ('{seg}').";
                    return null;
                }
            }
            return p;
        }

        private static readonly HashSet<string> k_ReservedDeviceNames = new HashSet<string>
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary> 앱 번들에 들어가면 안 되는 비밀 · 서명 파일 확장자 (폴더 복사 · 파일 복사 모두 실패) </summary>
        public static readonly HashSet<string> SensitiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".p8", ".p12", ".pfx", ".pem", ".key", ".cer", ".mobileprovision", ".provisionprofile", ".keystore", ".jks", ".env",
        };

        /// <summary> "ko.lproj" · "ko.lproj/InfoPlist.strings" → "ko" </summary>
        public static string GetLprojCode(string destination)
        {
            foreach (var seg in destination.Split('/'))
            {
                if (seg.EndsWith(".lproj", StringComparison.OrdinalIgnoreCase) && seg.Length > ".lproj".Length)
                    return seg.Substring(0, seg.Length - ".lproj".Length);
            }
            return null;
        }

        private static void CollectFiles(XcodeSettingsSnapshot.SourceData src, XcodePlanInput input,
            Dictionary<string, List<Contribution<FileValue>>> map, XcodePlan plan)
        {
            var rows = src.Data.Files ?? new List<XcodeFileEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || !row.Enabled) continue;
                string where = $"{src.Name}.Files[{i}] '{row.DestinationPath}'";
                if (!Applies(row.BuildTypes, input.BuildType, where, plan)) continue;

                string dest = NormalizeDestination(row.DestinationPath, out string destError);
                if (dest == null)
                {
                    plan.Errors.Add($"{where}: {destError}");
                    continue;
                }
                if (row.Target != EXcodeTarget.App && row.Target != EXcodeTarget.UnityFramework)
                {
                    plan.Errors.Add($"{where}: files can only be added to App or UnityFramework.");
                    continue;
                }

                string srcPath = (row.SourcePath ?? string.Empty).Trim();
                if (srcPath.Length == 0 || Path.IsPathRooted(srcPath))
                {
                    plan.Errors.Add($"{where}: source path must be relative to the Unity project (was '{srcPath}').");
                    continue;
                }
                string root = Path.GetFullPath(string.IsNullOrEmpty(input.ProjectRoot) ? "." : input.ProjectRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(root, srcPath));
                // 원본은 Unity 프로젝트 안만 (빌드 머신의 다른 파일이 산출물에 실리지 않게)
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Errors.Add($"{where}: source '{srcPath}' is outside the Unity project.");
                    continue;
                }
                if (XcodeFileHash.HasReparsePoint(full, root))
                {
                    plan.Errors.Add($"{where}: source '{srcPath}' is or is inside a symbolic link / junction. Use the real path.");
                    continue;
                }
                bool isDir = Directory.Exists(full);
                bool isFile = File.Exists(full);
                if (row.Mode == EXcodeFileMode.File && !isFile)
                {
                    plan.Errors.Add($"{where}: source file '{srcPath}' does not exist{(isDir ? " (it is a folder — use FolderReference)" : string.Empty)}.");
                    continue;
                }
                if (row.Mode == EXcodeFileMode.FolderReference && !isDir)
                {
                    plan.Errors.Add($"{where}: source folder '{srcPath}' does not exist{(isFile ? " (it is a file — use File)" : string.Empty)}.");
                    continue;
                }

                string problem = row.Mode == EXcodeFileMode.File
                    ? (SensitiveExtensions.Contains(Path.GetExtension(full)) ? Path.GetFileName(full) : null)
                    : XcodeFileHash.FindProblemInFolder(full);
                if (problem != null)
                {
                    plan.Errors.Add($"{where}: '{problem}' must not be copied into the app (key / certificate / env file, or a symbolic link). Move it out of the source.");
                    continue;
                }

                var c = new Contribution<FileValue>
                {
                    Source = src,
                    Where = where,
                    Value = new FileValue
                    {
                        FullPath = full,
                        Hash = XcodeFileHash.Compute(full),
                        Mode = row.Mode,
                        Target = row.Target,
                        AddToResources = row.AddToResources,
                    },
                };
                Add(map, dest, c);
            }
        }

        private static void MergeFile(string dest, List<Contribution<FileValue>> contribs,
            List<XcodeDecision> decisions, HashSet<XcodeDecision> usedChoices, XcodePlan plan, Dictionary<string, Contribution<FileValue>> winners)
        {
            bool Same(FileValue a, FileValue b) =>
                a.Hash == b.Hash && a.Mode == b.Mode && a.Target == b.Target && a.AddToResources == b.AddToResources;

            if (contribs.Any(c => !Same(c.Value, contribs[0].Value)))
            {
                var decision = FindDecision(decisions, EXcodeChangeKind.File, dest, EXcodeTarget.All, string.Empty, needsChoice: true);
                if (decision == null)
                {
                    plan.Errors.Add($"File '{dest}': different contents or settings for the same destination — " +
                                    $"{DescribeContribs(contribs, c => $"{c.Value.Mode}/{c.Value.Target}/{Path.GetFileName(c.Value.FullPath)}")}. Add a decision (Decisions) that chooses one source.");
                    return;
                }
                usedChoices.Add(decision);
                var chosen = contribs.Where(c => c.Source.Index == decision.ChosenIndex).ToList();
                if (chosen.Count == 0 || chosen.Any(c => !Same(c.Value, chosen[0].Value)))
                {
                    plan.Errors.Add($"File '{dest}': decision source '{decision.ChosenName}' has no single value here.");
                    return;
                }
                contribs = chosen;
            }

            var v = contribs[0].Value;
            winners[dest] = contribs[0];
            plan.Files.Add(new PlannedFile
            {
                Target = v.Target,
                SourceFullPath = v.FullPath,
                DestinationPath = dest,
                Mode = v.Mode,
                AddToResources = v.AddToResources,
                Hash = v.Hash,
                Sources = contribs.Select(c => c.Source.Name).Distinct().ToList(),
                Overwrite = FindDecision(decisions, EXcodeChangeKind.File, dest, EXcodeTarget.All, string.Empty, needsChoice: false, needsOverwrite: true),
            });
        }

        /// <summary> 한 대상이 다른 대상 폴더 안에 있으면 (예: ko.lproj 폴더 + ko.lproj/InfoPlist.strings 파일) 어느 쪽이 이길지 모호하다 </summary>
        private static void CheckOverlappingFiles(XcodePlan plan)
        {
            foreach (var a in plan.Files)
            {
                foreach (var b in plan.Files)
                {
                    if (a != b && b.DestinationPath.StartsWith(a.DestinationPath + "/", StringComparison.OrdinalIgnoreCase))
                        plan.Errors.Add($"File '{b.DestinationPath}' is inside another destination '{a.DestinationPath}'. Keep only one of them.");
                }
            }
        }

        // ───────────── 결정 · 기타 ─────────────

        private static void ValidateDecisions(List<XcodeDecision> decisions, XcodePlan plan)
        {
            for (int i = 0; i < decisions.Count; i++)
            {
                var d = decisions[i];
                if (string.IsNullOrWhiteSpace(d.Key))
                    plan.Errors.Add($"Decisions[{i}] has an empty key.");
                if (string.IsNullOrWhiteSpace(d.Memo))
                    plan.Errors.Add($"Decisions[{i}] {d} needs a memo (why this value).");
                if (!d.HasChoice) continue;
                if (!d.ChosenIsSettingsAsset)
                    plan.Errors.Add($"Decisions[{i}] {d}: chosen source must be an HBXcodeSettings or HBXcodeFragment asset.");
                else if (d.ChosenIndex < 0)
                    plan.Errors.Add($"Decisions[{i}] {d}: chosen source '{d.ChosenName}' is not this asset or an enabled fragment.");
            }
        }

        private static XcodeDecision FindDecision(List<XcodeDecision> decisions, EXcodeChangeKind kind, string key,
            EXcodeTarget target, string config, bool needsChoice, bool needsOverwrite = false)
        {
            foreach (var d in decisions)
            {
                if (d.Kind != kind) continue;
                // File 결정 키는 대상 경로와 같은 표기로 맞춘다 ('\' → '/', 앞뒤 '/' 제거)
                string dKey = kind == EXcodeChangeKind.File ? d.Key.Trim().Replace('\\', '/').Trim('/') : d.Key.Trim();
                if (!string.Equals(dKey, key, kind == EXcodeChangeKind.File ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    continue;
                if (kind is EXcodeChangeKind.BuildSetting or EXcodeChangeKind.Framework)
                {
                    if (d.Target != EXcodeTarget.All && d.Target != target) continue;
                }
                if (kind == EXcodeChangeKind.BuildSetting && !string.IsNullOrWhiteSpace(d.Config) && d.Config.Trim() != config)
                    continue;
                if (needsChoice && !d.HasChoice) continue;
                if (needsOverwrite && !d.OverwriteExisting) continue;
                return d;
            }
            return null;
        }

        private static void CheckApsEnvironment(XcodePlan plan, XcodePlanInput input)
        {
            var aps = plan.Entitlements.FirstOrDefault(e => e.Key == APS_ENVIRONMENT);
            if (aps == null || aps.Value.Kind != PlistNode.EKind.String || string.IsNullOrWhiteSpace(input.ProvisionType)) return;

            string expected = input.ProvisionType.Trim().ToLowerInvariant() switch
            {
                "development" => "development",
                "distribution" => "production",
                _ => null,
            };
            if (expected != null && aps.Value.Text != expected)
                plan.Warnings.Add($"Entitlement aps-environment is '{aps.Value.Text}' but the profile's ProvisionType is '{input.ProvisionType}' (expected '{expected}'). Push may not work with this signing.");
        }

        /// <summary> LIVE에서 보안을 약하게 하는 값 (충돌이 아니라 실패시키지 않고 경고 파일 · Jenkins UNSTABLE로 남긴다) </summary>
        private static void CheckLiveSecurity(XcodePlan plan, XcodePlanInput input)
        {
            if (input.BuildType != EBuildType.LIVE) return;

            var aps = plan.Entitlements.FirstOrDefault(e => e.Key == APS_ENVIRONMENT);
            if (aps != null && aps.Value.Kind == PlistNode.EKind.String && aps.Value.Text == "development")
                plan.Warnings.Add("LIVE build uses aps-environment 'development' (App Store builds need 'production').");

            var ats = plan.Info.FirstOrDefault(i => i.Key == "NSAppTransportSecurity")?.Value;
            if (ats == null || ats.Kind != PlistNode.EKind.Dict) return;
            if (ats.Get("NSAllowsArbitraryLoads")?.BoolValue == true)
                plan.Warnings.Add("LIVE build allows arbitrary HTTP loads (NSAppTransportSecurity/NSAllowsArbitraryLoads = true).");
            var domains = ats.Get("NSExceptionDomains");
            if (domains != null && domains.Kind == PlistNode.EKind.Dict)
            {
                foreach (var d in domains.Entries)
                {
                    if (d.Value.Kind == PlistNode.EKind.Dict && d.Value.Get("NSExceptionAllowsInsecureHTTPLoads")?.BoolValue == true)
                        plan.Warnings.Add($"LIVE build allows insecure HTTP to '{d.Key}' (NSExceptionAllowsInsecureHTTPLoads = true).");
                }
            }
        }

        private static string DescribeContribs<T>(List<Contribution<T>> contribs, Func<Contribution<T>, string> describe) =>
            string.Join("; ", contribs.Select(c => $"{c.Where}: {describe(c)}"));

        private static List<string> Clean(List<string> values) =>
            (values ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary> 파일 · 폴더 내용 해시 (폴더는 상대 경로 + 내용, .meta 제외) </summary>
    public static class XcodeFileHash
    {
        public static string Compute(string fullPath)
        {
            using var sha = SHA256.Create();
            if (File.Exists(fullPath))
                return ToHex(sha.ComputeHash(File.ReadAllBytes(fullPath)));

            var sb = new StringBuilder();
            foreach (var rel in ListFolderFiles(fullPath))
            {
                sb.Append(rel).Append('\n');
                sb.Append(ToHex(sha.ComputeHash(File.ReadAllBytes(Path.Combine(fullPath, rel))))).Append('\n');
            }
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>
        /// 폴더 안 파일의 상대 경로 ('/' 구분, 정렬). 번들에 들어가면 안 되는 Unity .meta와 숨김 파일 · 폴더('.'로 시작, .DS_Store · .git 등)는 뺀다
        /// </summary>
        public static List<string> ListFolderFiles(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                .Where(rel => !rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !rel.Split('/').Any(seg => seg.StartsWith(".")))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

        /// <summary> 경로 자신이나 root 아래의 상위 폴더 중 심볼릭 링크 · 정션이 있는지 </summary>
        public static bool HasReparsePoint(string fullPath, string root)
        {
            string current = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string stop = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (!string.IsNullOrEmpty(current) && current.Length > stop.Length)
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        /// <summary> 폴더 안에 복사하면 안 되는 것(비밀 · 서명 파일, 심볼릭 링크)이 있으면 그 상대 경로, 없으면 null </summary>
        public static string FindProblemInFolder(string folder)
        {
            foreach (var entry in Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(folder, entry).Replace('\\', '/');
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    return rel;
                if (File.Exists(entry) && XcodeSettingsPlanner.SensitiveExtensions.Contains(Path.GetExtension(entry)))
                    return rel;
            }
            return null;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
