using System.Collections.Generic;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 결정 행의 스냅샷 (Unity 객체 참조 없이 소스 순번으로 가리킨다)
    /// </summary>
    public sealed class XcodeDecision
    {
        public EXcodeChangeKind Kind;
        public EXcodeTarget Target;
        public string Config = string.Empty;
        public string Key = string.Empty;
        public bool OverwriteExisting;
        public string Memo = string.Empty;

        /// <summary> ChosenSource가 지정됐는지 </summary>
        public bool HasChoice;
        /// <summary> 고른 소스의 순번 (XcodeSettingsSnapshot.Sources). 공용 · 켜진 묶음이 아니면 -1 </summary>
        public int ChosenIndex = -1;
        public string ChosenName = string.Empty;
        public bool ChosenIsSettingsAsset = true;

        public override string ToString()
        {
            string scope = Kind is EXcodeChangeKind.BuildSetting or EXcodeChangeKind.Framework
                ? $"[{Target}{(string.IsNullOrWhiteSpace(Config) ? string.Empty : "/" + Config.Trim())}] "
                : string.Empty;
            return $"{Kind} {scope}'{Key}'";
        }
    }

    /// <summary>
    /// 빌드 전 점검 시점의 설정 내용 (순수 C# 복사본).
    /// BuildPlayer가 쓰지 않는 에셋을 정리하면 넘겨받은 Unity 객체 참조가 null이 될 수 있어(디스크 에셋 · 메모리 객체 모두 확인함),
    /// 빌드 뒤에는 이 스냅샷만 적용한다 — 점검한 내용과 적용하는 내용이 같다.
    /// </summary>
    public sealed class XcodeSettingsSnapshot
    {
        public sealed class SourceData
        {
            public int Index;
            public string Name;
            public XcodeSettingsData Data;
        }

        public string Name = string.Empty;
        public readonly List<SourceData> Sources = new List<SourceData>();
        public readonly List<XcodeDecision> Decisions = new List<XcodeDecision>();
        public bool FillLocalizationsFromLproj = true;
        public readonly List<string> Warnings = new List<string>();

        /// <summary> 켜졌는데 비었거나 Missing인 묶음 칸 (계획기가 LIVE는 오류, DEV · QA는 경고로 처리) </summary>
        public readonly List<string> MissingFragments = new List<string>();

        public static XcodeSettingsSnapshot Capture(HBXcodeSettings settings)
        {
            var snap = new XcodeSettingsSnapshot { Name = settings.name, FillLocalizationsFromLproj = settings.FillLocalizationsFromLproj };
            var assets = new List<Object> { settings };
            snap.Sources.Add(new SourceData { Index = 0, Name = settings.name, Data = Copy(settings.Data) });

            var refs = settings.Fragments ?? new List<XcodeFragmentRef>();
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (r == null || !r.Enabled) continue;
                // UnityEngine.Object는 == null로 판정한다. 참조는 있는데 null이면 에셋 삭제 · Missing (빈 칸과 구분해 알린다)
                if (r.Fragment == null)
                {
                    snap.MissingFragments.Add(ReferenceEquals(r.Fragment, null)
                        ? $"{settings.name}.Fragments[{i}] is enabled but empty."
                        : $"{settings.name}.Fragments[{i}] references a missing fragment asset (deleted or broken reference).");
                    continue;
                }
                if (assets.Contains(r.Fragment))
                {
                    snap.Warnings.Add($"Fragment '{r.Fragment.name}' is listed more than once. Using it once.");
                    continue;
                }
                assets.Add(r.Fragment);
                snap.Sources.Add(new SourceData { Index = snap.Sources.Count, Name = r.Fragment.name, Data = Copy(r.Fragment.Data) });
            }

            foreach (var d in settings.Decisions ?? new List<XcodeDecisionEntry>())
            {
                if (d == null) continue;
                var decision = new XcodeDecision
                {
                    Kind = d.Kind,
                    Target = d.Target,
                    Config = d.Config ?? string.Empty,
                    Key = d.Key ?? string.Empty,
                    OverwriteExisting = d.OverwriteExisting,
                    Memo = d.Memo ?? string.Empty,
                    HasChoice = d.ChosenSource != null,
                };
                if (decision.HasChoice)
                {
                    decision.ChosenName = d.ChosenSource.name;
                    decision.ChosenIsSettingsAsset = d.ChosenSource is HBXcodeSettings or HBXcodeFragment;
                    decision.ChosenIndex = assets.IndexOf(d.ChosenSource);
                }
                snap.Decisions.Add(decision);
            }
            return snap;
        }

        private static XcodeSettingsData Copy(XcodeSettingsData data) =>
            data == null ? new XcodeSettingsData() : JsonUtility.FromJson<XcodeSettingsData>(JsonUtility.ToJson(data));
    }
}
