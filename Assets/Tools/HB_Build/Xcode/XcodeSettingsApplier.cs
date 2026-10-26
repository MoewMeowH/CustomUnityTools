#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor.iOS.Xcode;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// XcodePlan을 Unity가 만든 Xcode 프로젝트에 적용합니다 (iOS Build Support 모듈 필요 — UNITY_IOS일 때만 컴파일).
    /// 모든 검사(기존 값 충돌 포함)를 먼저 끝내고, 통과했을 때만 파일을 씁니다.
    /// HB_Build iOS 빌드는 항상 Replace라 새로 생성된 프로젝트에 한 번 적용하는 것을 기준으로 합니다.
    /// </summary>
    public static class XcodeSettingsApplier
    {
        public const string ENTITLEMENTS_FILE_NAME = "HBXcode.entitlements";

        /// <summary>
        /// project.pbxproj 경로. Unity API를 먼저 쓰고, 없으면 폴더의 유일한 *.xcodeproj를 쓴다
        /// (Unity 7 Swift 형식은 프로젝트 이름이 제품 이름이라 API 결과가 다를 수 있음).
        /// </summary>
        public static string FindProjectPath(string xcodeRoot)
        {
            string path = PBXProject.GetPBXProjectPath(xcodeRoot);
            if (File.Exists(path)) return path;

            var candidates = Directory.GetDirectories(xcodeRoot, "*.xcodeproj")
                .Where(d => !Path.GetFileName(d).Equals("Pods.xcodeproj", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 1)
                return Path.Combine(candidates[0], "project.pbxproj");
            throw new InvalidOperationException($"Cannot find the Xcode project in '{xcodeRoot}' ({candidates.Count} *.xcodeproj found).");
        }

        /// <summary> 계획에 필요한 프로젝트 사실 (구성 목록, GameAssembly 유무) </summary>
        public static void ReadFacts(string xcodeRoot, XcodePlanInput input)
        {
            var proj = new PBXProject();
            proj.ReadFromFile(FindProjectPath(xcodeRoot));
            input.Configs = proj.BuildConfigNames().ToList();
            input.HasGameAssembly = !string.IsNullOrEmpty(proj.TargetGuidByName("GameAssembly"));
        }

        /// <summary> 적용하고 바꾼 내용을 줄 단위로 돌려줍니다. 검사에 걸리면 아무것도 쓰지 않고 InvalidOperationException </summary>
        public static List<string> Apply(XcodePlan plan, string xcodeRoot, List<string> warnings)
        {
            if (plan.Errors.Count > 0)
                throw new InvalidOperationException("Plan has errors:\n" + string.Join("\n", plan.Errors));

            string projPath = FindProjectPath(xcodeRoot);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            var targets = new Dictionary<EXcodeTarget, string>
            {
                { EXcodeTarget.App, proj.GetUnityMainTargetGuid() },
                { EXcodeTarget.UnityFramework, proj.GetUnityFrameworkTargetGuid() },
            };
            string gameAssembly = proj.TargetGuidByName("GameAssembly");
            if (!string.IsNullOrEmpty(gameAssembly))
                targets[EXcodeTarget.GameAssembly] = gameAssembly;

            var errors = new List<string>();
            var log = new List<string>();
            var usedOverwrites = new HashSet<XcodeDecision>();
            string appGuid = targets[EXcodeTarget.App];
            var appConfigs = proj.BuildConfigNames().Select(n => proj.BuildConfigByName(appGuid, n)).Where(g => !string.IsNullOrEmpty(g)).ToList();

            // ── 경로 해석 (다른 후처리기가 바꾼 값일 수 있으므로 프로젝트 폴더 안 상대 경로인지 확인) ──
            string infoRel = SingleConfigValue(proj, appConfigs, "INFOPLIST_FILE", errors);
            if (infoRel != null && !IsPlainRelative(infoRel))
            {
                errors.Add($"INFOPLIST_FILE of the app target is '{infoRel}'. Only a plain path inside the Xcode project is supported (no absolute path, '..' or $(VARIABLE)).");
                infoRel = null;
            }
            string infoPath = infoRel != null ? Path.Combine(xcodeRoot, infoRel) : null;
            if (infoPath != null && !File.Exists(infoPath))
                errors.Add($"Info.plist not found at '{infoRel}' (INFOPLIST_FILE of the app target).");

            string existingEntRel = SingleConfigValue(proj, appConfigs, "CODE_SIGN_ENTITLEMENTS", errors, allowEmpty: true);
            if (!string.IsNullOrEmpty(existingEntRel) && !IsPlainRelative(existingEntRel))
            {
                errors.Add($"CODE_SIGN_ENTITLEMENTS of the app target is '{existingEntRel}'. Only a plain path inside the Xcode project is supported (no absolute path, '..' or $(VARIABLE)).");
                existingEntRel = null;
            }

            bool needsEntitlements = plan.Entitlements.Count > 0 || plan.Capabilities.Count > 0;
            string entRel = existingEntRel;
            bool createEntitlements = false;
            if (needsEntitlements)
            {
                if (string.IsNullOrEmpty(entRel))
                {
                    string infoDir = Path.GetDirectoryName(infoRel ?? string.Empty)?.Replace('\\', '/') ?? string.Empty;
                    entRel = string.IsNullOrEmpty(infoDir) ? ENTITLEMENTS_FILE_NAME : infoDir + "/" + ENTITLEMENTS_FILE_NAME;
                }
                // 빌드 설정이 가리키는데 파일이 없을 때도 새로 만든다
                createEntitlements = !File.Exists(Path.Combine(xcodeRoot, entRel));
            }
            if (errors.Count > 0) throw Fail(errors);

            // Files가 덮으면 안 되는 경로: 이 적용기가 직접 쓰는 plist와 Xcode 프로젝트 파일
            var reserved = new List<string> { infoRel, Path.GetFileName(Path.GetDirectoryName(projPath)) };
            if (!string.IsNullOrEmpty(entRel)) reserved.Add(entRel);

            // ── 검사 (쓰기 전) ──
            var info = new PlistDocument();
            if (plan.Info.Count > 0) info.ReadFromFile(infoPath);
            var infoWrites = CheckPlist("Info", info.root, plan.Info, errors, usedOverwrites);

            var ent = new PlistDocument();
            if (needsEntitlements && !createEntitlements) ent.ReadFromFile(Path.Combine(xcodeRoot, entRel));
            var entWrites = CheckPlist("Entitlement", ent.root, plan.Entitlements, errors, usedOverwrites);

            var settingWrites = new List<(string ConfigGuid, PlannedBuildSetting Setting, List<string> Values)>();
            foreach (var b in plan.BuildSettings)
            {
                if (!targets.TryGetValue(b.Target, out string targetGuid))
                {
                    errors.Add($"BuildSetting [{b.Target}/{b.Config}] '{b.Key}': target not found in the Xcode project.");
                    continue;
                }
                string configGuid = proj.BuildConfigByName(targetGuid, b.Config);
                if (string.IsNullOrEmpty(configGuid))
                {
                    errors.Add($"BuildSetting [{b.Target}/{b.Config}] '{b.Key}': config not found on this target.");
                    continue;
                }
                string existing = proj.GetBuildPropertyForConfig(configGuid, b.Key);
                if (b.Op == EBuildSettingOp.Set)
                {
                    string value = b.Values[0];
                    if (existing == value) continue;
                    if (!string.IsNullOrEmpty(existing) && !AllowOverwrite(b.Overwrite, usedOverwrites))
                    {
                        errors.Add($"BuildSetting [{b.Target}/{b.Config}] '{b.Key}': the Xcode project already has '{existing}' (wanted '{value}'). " +
                                   "Add a decision with OverwriteExisting if this should replace it.");
                        continue;
                    }
                    settingWrites.Add((configGuid, b, b.Values));
                }
                else
                {
                    var missing = b.Values.Where(v => !ContainsToken(existing, v)).ToList();
                    if (missing.Count > 0) settingWrites.Add((configGuid, b, missing));
                }
            }

            // 프레임워크: 이미 링크돼 있으면 Required/Optional까지 비교한다 (API가 weak 여부를 주지 않아 pbxproj 텍스트로 읽음)
            string pbxText = proj.WriteToString();
            var frameworkWrites = new List<(PlannedFramework Framework, bool Relink)>();
            foreach (var fw in plan.Frameworks)
            {
                if (!targets.TryGetValue(fw.Target, out string targetGuid))
                {
                    errors.Add($"Framework [{fw.Target}] '{fw.Name}': target not found in the Xcode project.");
                    continue;
                }
                string phase = proj.GetFrameworksBuildPhaseByTarget(targetGuid);
                if (string.IsNullOrEmpty(phase))
                {
                    errors.Add($"Framework [{fw.Target}] '{fw.Name}': target has no Link Binary With Libraries phase.");
                    continue;
                }
                if (!PbxprojText.TryFindLinked(pbxText, phase, fw.Name, out bool weak))
                {
                    frameworkWrites.Add((fw, false));
                }
                else if (weak != fw.Optional)
                {
                    if (AllowOverwrite(fw.Overwrite, usedOverwrites))
                    {
                        frameworkWrites.Add((fw, true));
                    }
                    else
                    {
                        errors.Add($"Framework [{fw.Target}] '{fw.Name}': already linked as {(weak ? "Optional" : "Required")} (wanted {(fw.Optional ? "Optional" : "Required")}). " +
                                   "Add a Framework decision with OverwriteExisting if this should change it.");
                    }
                }
            }

            var fileWrites = new List<(PlannedFile File, bool Copy)>();
            foreach (var f in plan.Files)
            {
                if (!targets.TryGetValue(f.Target, out string targetGuid))
                {
                    errors.Add($"File '{f.DestinationPath}': target {f.Target} not found.");
                    continue;
                }
                string clash = reserved.FirstOrDefault(r => SamePathOrNested(f.DestinationPath, r));
                if (clash != null)
                {
                    errors.Add($"File '{f.DestinationPath}': overlaps '{clash}', which Xcode settings edits itself (Info.plist / entitlements / Xcode project). Use the Info or Entitlements tab instead.");
                    continue;
                }
                string dest = Path.Combine(xcodeRoot, f.DestinationPath);
                if (XcodeFileHash.HasReparsePoint(dest, xcodeRoot))
                {
                    errors.Add($"File '{f.DestinationPath}': the destination or one of its folders is a symbolic link in the Xcode project.");
                    continue;
                }
                if (f.AddToResources && string.IsNullOrEmpty(proj.GetResourcesBuildPhaseByTarget(targetGuid)))
                {
                    errors.Add($"File '{f.DestinationPath}': target {f.Target} has no Copy Bundle Resources phase.");
                    continue;
                }
                bool exists = File.Exists(dest) || Directory.Exists(dest);
                bool same = exists && XcodeFileHash.Compute(dest) == f.Hash;
                if (exists && !same && !AllowOverwrite(f.Overwrite, usedOverwrites))
                {
                    errors.Add($"File '{f.DestinationPath}': a different file already exists in the Xcode project (Unity or another SDK post-processor wrote it). " +
                               "Add a decision with OverwriteExisting if this should replace it.");
                    continue;
                }
                fileWrites.Add((f, !same));
            }

            if (errors.Count > 0) throw Fail(errors);

            // ── 쓰기 (파일 복사를 먼저: IO 오류가 나면 plist · pbxproj는 아직 그대로) ──
            foreach (var (f, copy) in fileWrites)
            {
                if (!copy)
                {
                    log.Add($"File  {f.DestinationPath} already has the same content");
                    continue;
                }
                var copied = CopyToProject(f, xcodeRoot);
                log.Add($"File  {f.DestinationPath} ({f.Mode}, {f.Target}{(f.AddToResources ? ", Resources" : string.Empty)}): {string.Join(", ", copied)}");
            }

            foreach (var (key, element, note) in infoWrites)
            {
                info.root[key] = element;
                log.Add($"Info  {key} {note}");
            }
            if (infoWrites.Count > 0) info.WriteToFile(infoPath);

            if (needsEntitlements)
            {
                foreach (var (key, element, note) in entWrites)
                {
                    ent.root[key] = element;
                    log.Add($"Entitlement  {key} {note}");
                }
                if (createEntitlements || entWrites.Count > 0)
                    ent.WriteToFile(Path.Combine(xcodeRoot, entRel));
                if (createEntitlements && string.IsNullOrEmpty(proj.FindFileGuidByProjectPath(entRel)))
                {
                    proj.AddFile(entRel, entRel);
                    log.Add($"Entitlements file created: {entRel}");
                }
                foreach (var cfg in appConfigs)
                    proj.SetBuildPropertyForConfig(cfg, "CODE_SIGN_ENTITLEMENTS", entRel);
            }

            foreach (var cap in plan.Capabilities)
            {
                proj.AddCapability(appGuid, ToPbx(cap), entRel);
                log.Add($"Capability  {cap}");
            }

            foreach (var (configGuid, b, values) in settingWrites)
            {
                foreach (var v in values)
                {
                    if (b.Op == EBuildSettingOp.Set)
                        proj.SetBuildPropertyForConfig(configGuid, b.Key, v);
                    else
                        proj.AddBuildPropertyForConfig(configGuid, b.Key, v);
                }
                log.Add($"BuildSetting  [{b.Target}/{b.Config}] {b.Key} {(b.Op == EBuildSettingOp.Set ? "=" : "+=")} {string.Join(" ", values)}");
            }

            foreach (var (fw, relink) in frameworkWrites)
            {
                string targetGuid = targets[fw.Target];
                if (relink)
                    proj.RemoveFrameworkFromProject(targetGuid, fw.Name);
                LinkFramework(proj, targetGuid, fw);
                log.Add($"Framework  [{fw.Target}] {fw.Name}{(fw.Optional ? " (Optional)" : string.Empty)}{(relink ? " (relinked by decision)" : string.Empty)}");
            }

            foreach (var (f, _) in fileWrites)
            {
                string targetGuid = targets[f.Target];
                string fileGuid = proj.FindFileGuidByProjectPath(f.DestinationPath);
                if (string.IsNullOrEmpty(fileGuid))
                {
                    fileGuid = f.Mode == EXcodeFileMode.FolderReference
                        ? proj.AddFolderReference(f.DestinationPath, f.DestinationPath)
                        : proj.AddFile(f.DestinationPath, f.DestinationPath);
                }
                // 다른 후처리기가 참조만 만들어 둔 경우에도 Resources에 들어가게 한다
                if (f.AddToResources)
                {
                    string phase = proj.GetResourcesBuildPhaseByTarget(targetGuid);
                    if (!PbxprojText.PhaseContainsFileRef(proj.WriteToString(), phase, fileGuid))
                        proj.AddFileToBuildSection(targetGuid, phase, fileGuid);
                }
            }

            proj.WriteToFile(projPath);

            foreach (var d in plan.OverwriteDecisions)
            {
                if (!usedOverwrites.Contains(d))
                    warnings.Add($"[Xcode] Decision {d} allows overwriting, but nothing different existed in the Xcode project. Remove it if no longer needed.");
            }
            return log;
        }

        /// <summary> .framework는 SDK 프레임워크 폴더, .tbd는 usr/lib에서 링크한다 (AddFrameworkToProject는 .tbd도 System/Library/Frameworks로 만든다) </summary>
        private static void LinkFramework(PBXProject proj, string targetGuid, PlannedFramework fw)
        {
            if (!fw.Name.EndsWith(".tbd", StringComparison.OrdinalIgnoreCase))
            {
                proj.AddFrameworkToProject(targetGuid, fw.Name, fw.Optional);
                return;
            }
            string projectPath = "Frameworks/" + fw.Name;
            string fileGuid = proj.FindFileGuidByProjectPath(projectPath);
            if (string.IsNullOrEmpty(fileGuid))
                fileGuid = proj.AddFile("usr/lib/" + fw.Name, projectPath, PBXSourceTree.Sdk);
            proj.AddFileToBuildSection(targetGuid, proj.GetFrameworksBuildPhaseByTarget(targetGuid), fileGuid);
        }

        /// <summary> 빌드 설정 경로 값이 Xcode 프로젝트 폴더 안 일반 상대 경로인지 (절대 경로 · '..' · $(변수) 아님) </summary>
        private static bool IsPlainRelative(string path) =>
            !Path.IsPathRooted(path) && !path.StartsWith("/") && !path.Contains("$(") && !path.Contains("${") &&
            !path.Replace('\\', '/').Split('/').Any(s => s == "..");

        private static bool SamePathOrNested(string a, string b)
        {
            string x = a.Replace('\\', '/').Trim('/');
            string y = b.Replace('\\', '/').Trim('/');
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase) ||
                   y.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase) ||
                   x.StartsWith(y + "/", StringComparison.OrdinalIgnoreCase);
        }

        // ───────────── 내부 ─────────────

        private static InvalidOperationException Fail(List<string> errors) =>
            new InvalidOperationException("Xcode settings cannot be applied:\n  - " + string.Join("\n  - ", errors));

        private static bool AllowOverwrite(XcodeDecision decision, HashSet<XcodeDecision> used)
        {
            if (decision == null) return false;
            used.Add(decision);
            return true;
        }

        /// <summary> 앱 타깃의 모든 구성에서 같은 값이어야 하는 빌드 설정 (구성마다 다르면 어느 파일인지 정할 수 없다) </summary>
        private static string SingleConfigValue(PBXProject proj, List<string> configGuids, string key, List<string> errors, bool allowEmpty = false)
        {
            var values = configGuids.Select(g => proj.GetBuildPropertyForConfig(g, key) ?? string.Empty).Distinct().ToList();
            if (values.Count == 1 && (allowEmpty || values[0].Length > 0))
                return values[0];
            if (values.Count == 0 && allowEmpty)
                return string.Empty;
            errors.Add($"{key} of the app target is {(values.Count > 1 ? "different per config (" + string.Join(", ", values) + ")" : "empty")}; cannot decide which file to edit.");
            return null;
        }

        private static bool ContainsToken(string existing, string value) =>
            !string.IsNullOrEmpty(existing) &&
            (existing == value || existing.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.Ordinal));

        /// <summary> 기존 값과 비교해 쓸 값을 정합니다. 같으면 건너뛰고, 목록은 합치고, 다르면(덮어쓰기 결정이 없을 때) 오류 </summary>
        private static List<(string Key, PlistElement Element, string Note)> CheckPlist(string kind, PlistElementDict root, List<PlannedPlist> planned,
            List<string> errors, HashSet<XcodeDecision> usedOverwrites)
        {
            var writes = new List<(string, PlistElement, string)>();
            foreach (var p in planned)
            {
                PlistNode existing = null;
                if (root.values.TryGetValue(p.Key, out var existingElement))
                {
                    try
                    {
                        existing = ToNode(existingElement);
                    }
                    catch (FormatException ex)
                    {
                        errors.Add($"{kind} '{p.Key}': cannot read the existing value ({ex.Message}).");
                        continue;
                    }
                }

                PlistNode result = p.Value;
                string note = $"= {p.Value}";
                if (existing != null)
                {
                    bool listMerge = p.Merge is EPlistMerge.StringList or EPlistMerge.SkAdNetworkList;
                    bool compatible = p.Merge switch
                    {
                        EPlistMerge.StringList => existing.IsStringArray,
                        EPlistMerge.SkAdNetworkList => XcodeSettingsPlanner.IsSkAdNetworkList(existing),
                        _ => true,
                    };

                    if (listMerge && compatible)
                    {
                        result = XcodeSettingsPlanner.Union(p.Merge, existing, p.Value);
                        if (result.ValueEquals(existing)) continue;
                        note = $"{existing} → {result} (merged)";
                    }
                    else if (existing.ValueEquals(p.Value))
                    {
                        continue;
                    }
                    else if (AllowOverwrite(p.Overwrite, usedOverwrites))
                    {
                        note = $"{existing} → {p.Value} (overwritten by decision)";
                    }
                    else
                    {
                        errors.Add($"{kind} '{p.Key}': the Xcode project already has {existing} (wanted {p.Value}). " +
                                   "Add a decision with OverwriteExisting if this should replace it.");
                        continue;
                    }
                }

                try
                {
                    writes.Add((p.Key, ToElement(result), note));
                }
                catch (FormatException ex)
                {
                    errors.Add($"{kind} '{p.Key}': {ex.Message}");
                }
            }
            return writes;
        }

        private static PlistNode ToNode(PlistElement e)
        {
            switch (e)
            {
                case PlistElementString s:
                    return PlistNode.String(s.value);
                case PlistElementBoolean b:
                    return PlistNode.Bool(b.value);
                case PlistElementInteger i:
                    return PlistNode.Integer((long)i.value);
                case PlistElementReal r:
                    return PlistNode.Real((double)r.value);
                case PlistElementDate d:
                    return PlistNode.Date(d.value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                case PlistElementArray a:
                    return PlistNode.Array(a.values.Select(ToNode));
                case PlistElementDict d:
                    return PlistNode.Dict(d.values.Select(kv => new KeyValuePair<string, PlistNode>(kv.Key, ToNode(kv.Value))));
                default:
                    throw new FormatException($"unsupported plist element {e?.GetType().Name}");
            }
        }

        private static PlistElement ToElement(PlistNode n)
        {
            switch (n.Kind)
            {
                case PlistNode.EKind.String:
                    return new PlistElementString(n.Text);
                case PlistNode.EKind.Bool:
                    return new PlistElementBoolean(n.BoolValue);
                case PlistNode.EKind.Integer:
                    long l = n.IntegerValue;
                    if (l < int.MinValue || l > int.MaxValue)
                        throw new FormatException($"integer {l} is out of range for the Xcode plist API.");
                    return new PlistElementInteger((int)l);
                case PlistNode.EKind.Real:
                    return new PlistElementReal((float)n.RealValue);
                case PlistNode.EKind.Date:
                    if (!DateTime.TryParse(n.Text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                        throw new FormatException($"invalid date '{n.Text}'.");
                    return new PlistElementDate(date);
                case PlistNode.EKind.Array:
                    var array = new PlistElementArray();
                    foreach (var item in n.Items) array.values.Add(ToElement(item));
                    return array;
                case PlistNode.EKind.Dict:
                    var dict = new PlistElementDict();
                    foreach (var e in n.Entries) dict[e.Key] = ToElement(e.Value);
                    return dict;
                default:
                    throw new FormatException($"unsupported value kind {n.Kind}.");
            }
        }

        private static PBXCapabilityType ToPbx(EXcodeCapability cap) => cap switch
        {
            EXcodeCapability.PushNotifications => PBXCapabilityType.PushNotifications,
            EXcodeCapability.SignInWithApple => PBXCapabilityType.SignInWithApple,
            EXcodeCapability.AssociatedDomains => PBXCapabilityType.AssociatedDomains,
            EXcodeCapability.BackgroundModes => PBXCapabilityType.BackgroundModes,
            EXcodeCapability.InAppPurchase => PBXCapabilityType.InAppPurchase,
            EXcodeCapability.GameCenter => PBXCapabilityType.GameCenter,
            EXcodeCapability.KeychainSharing => PBXCapabilityType.KeychainSharing,
            EXcodeCapability.AppGroups => PBXCapabilityType.AppGroups,
            _ => throw new ArgumentOutOfRangeException(nameof(cap), cap, null),
        };

        /// <summary> 복사하고 복사한 파일의 상대 경로를 돌려준다 (로그용) </summary>
        private static List<string> CopyToProject(PlannedFile f, string xcodeRoot)
        {
            string dest = Path.Combine(xcodeRoot, f.DestinationPath);
            if (File.Exists(dest)) File.Delete(dest);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);

            if (f.Mode == EXcodeFileMode.File)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f.SourceFullPath, dest);
                return new List<string> { Path.GetFileName(dest) };
            }

            // 폴더는 .meta · 숨김 파일을 빼고 복사한다 (Unity가 만든 .meta · .DS_Store 등이 앱 번들에 들어가지 않게)
            var files = XcodeFileHash.ListFolderFiles(f.SourceFullPath);
            foreach (var rel in files)
            {
                string to = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(Path.Combine(f.SourceFullPath, rel), to);
            }
            Directory.CreateDirectory(dest);
            return files;
        }
    }
}
#endif
