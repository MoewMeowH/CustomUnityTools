using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    public class SlotValidatorWindow : EditorWindow
    {
        private readonly Dictionary<string, bool> m_Checks = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> m_Foldouts = new Dictionary<string, bool>();
        private AssetHealthScanResult m_Result;
        // 결과가 바뀔 때만 다시 계산 (OnGUI마다 전체를 세지 않음)
        private List<Group> m_Groups = new List<Group>();
        private string m_Summary;
        private string m_FailureText;
        private Vector2 m_ScrollPosition;

        private sealed class Group
        {
            public string CheckId;
            public int Errors;
            public int Warnings;
            public readonly List<SlotValidationIssue> Issues = new List<SlotValidationIssue>();
        }

        [MenuItem("Tools/HB/Test Framework Helper")]
        public static void ShowWindow()
        {
            var window = GetWindow<SlotValidatorWindow>("HB Test Helper");
            window.minSize = new Vector2(500, 450);
            window.Show();
        }

        private void OnEnable()
        {
            // 처음 열 때는 프로젝트 설정에서 켜진 검사를 고른 상태로
            foreach (string id in AssetCheckIds.All)
            {
                m_Checks[id] = AssetHealthSettings.IsCheckEnabled(id);
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(6);

            DrawToolbar();
            EditorGUILayout.Space(8);

            DrawResultsList();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("HB Test Framework Helper - Asset Health", EditorStyles.boldLabel);
            GUILayout.Label("[NeedSlotNullCheck] 슬롯 · Missing 스크립트 · 머티리얼 · Missing 참조를 검사합니다. 폴더 · 씬 대상은 Project Settings에서 정합니다.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (string id in AssetCheckIds.All)
            {
                m_Checks[id] = EditorGUILayout.ToggleLeft(AssetHealthSettingsProvider.Describe(id), m_Checks[id]);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Project Settings 열기", EditorStyles.toolbarButton, GUILayout.Width(140)))
            {
                SettingsService.OpenProjectSettings(AssetHealthSettings.SettingsPath);
            }
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            if (GUILayout.Button("검사 실행", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                GUI.backgroundColor = Color.white;
                ExecuteScan();
                // 같은 이벤트에서 결과 목록을 그리면 Layout 단계와 컨트롤 수가 달라진다 → 다음 프레임에 그림
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void ExecuteScan()
        {
            // 이전 결과가 잡고 있는 에셋 참조를 먼저 놓는다
            m_Result = null;
            m_Groups = new List<Group>();

            var options = AssetHealthSettings.CreateScanOptions();
            options.EnabledChecks = new List<string>();
            foreach (var pair in m_Checks)
            {
                if (pair.Value) options.EnabledChecks.Add(pair.Key);
            }

            m_Result = AssetHealthEngine.Scan(options, showProgress: true);
            m_Groups = BuildGroups(m_Result);
            m_Summary = $"문제 {m_Result.Issues.Count}개 (에러 {m_Result.ErrorCount} · 경고 {m_Result.WarningCount}) · 검사한 파일 · 씬 {m_Result.ScannedTargetCount}개";
            m_FailureText = m_Result.IsComplete ? null : BuildFailureText(m_Result);
            Repaint();
        }

        private static string BuildFailureText(AssetHealthScanResult result)
        {
            var sb = new System.Text.StringBuilder(result.Canceled ? "검사를 취소했습니다. " : string.Empty);
            sb.Append($"검사를 끝까지 하지 못했습니다 — 아래 결과에 빠진 대상이 있습니다 (실패 {result.Failures.Count}개).");
            int shown = 0;
            foreach (var failure in result.Failures)
            {
                if (++shown > 20)
                {
                    sb.Append($"\n… 외 {result.Failures.Count - 20}개");
                    break;
                }
                sb.Append("\n• ").Append(failure);
            }
            return sb.ToString();
        }

        private static List<Group> BuildGroups(AssetHealthScanResult result)
        {
            var groups = new List<Group>();
            var byId = new Dictionary<string, Group>();
            foreach (var issue in result.Issues)
            {
                string id = issue.CheckId ?? "(검사 없음)";
                if (!byId.TryGetValue(id, out var group))
                {
                    group = new Group { CheckId = id };
                    byId[id] = group;
                    groups.Add(group);
                }
                group.Issues.Add(issue);
                if (issue.Severity == EValidationSeverity.Error) group.Errors++;
                else group.Warnings++;
            }
            return groups;
        }

        private void DrawResultsList()
        {
            if (m_Result == null)
            {
                EditorGUILayout.HelpBox("검사할 항목을 고르고 '검사 실행'을 누르세요.", MessageType.Info);
                return;
            }

            if (m_FailureText != null)
            {
                EditorGUILayout.HelpBox(m_FailureText, MessageType.Warning);
            }

            if (m_Result.Issues.Count == 0)
            {
                if (m_Result.IsComplete)
                {
                    EditorGUILayout.HelpBox($"문제가 없습니다 (검사한 파일 · 씬 {m_Result.ScannedTargetCount}개).", MessageType.Info);
                }
                return;
            }

            GUILayout.Label(m_Summary, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            foreach (var group in m_Groups)
            {
                if (!m_Foldouts.TryGetValue(group.CheckId, out bool open)) open = true;
                open = EditorGUILayout.Foldout(open, $"{group.CheckId} — 에러 {group.Errors} · 경고 {group.Warnings}", true);
                m_Foldouts[group.CheckId] = open;
                if (!open) continue;

                foreach (var issue in group.Issues)
                {
                    DrawIssue(issue);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawIssue(SlotValidationIssue issue)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            string typeLabel = issue.IssueType switch
            {
                EValidationIssueType.NullSlot => "[빈 슬롯 (Null)]",
                EValidationIssueType.MissingReference => "[Missing 참조]",
                EValidationIssueType.MissingComponent => "[Missing 스크립트]",
                EValidationIssueType.NullElement => "[빈 원소 (경고)]",
                EValidationIssueType.MissingShader => "[셰이더 없음]",
                EValidationIssueType.ErrorShader => "[에러 셰이더]",
                EValidationIssueType.ShaderCompileError => "[셰이더 컴파일 에러]",
                _ => "[이슈]"
            };

            GUI.color = issue.Severity == EValidationSeverity.Warning ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.5f);
            GUILayout.Label(typeLabel, EditorStyles.boldLabel, GUILayout.Width(120));
            GUI.color = Color.white;

            // Missing 스크립트 · 셰이더는 필드가 없을 수 있다
            string member = string.IsNullOrEmpty(issue.FieldName) ? issue.ComponentName : $"{issue.ComponentName} > {issue.FieldName}";
            GUILayout.Label(member, EditorStyles.boldLabel);
            if (issue.IsUnsavedScene) GUILayout.Label("[저장 안 됨]", EditorStyles.miniBoldLabel, GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("에셋 열기 (Ping)", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Object target = issue.Asset != null ? issue.Asset : issue.PrefabAsset;
                if (target != null)
                {
                    EditorGUIUtility.PingObject(target);
                    Selection.activeObject = target;
                }
            }

            EditorGUILayout.EndHorizontal();

            string sub = string.IsNullOrEmpty(issue.SubAssetName) ? string.Empty : $" · 서브에셋 {issue.SubAssetName}";
            EditorGUILayout.LabelField($"경로: {issue.AssetPath} ({issue.GameObjectPath}){sub}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(issue.Description))
            {
                EditorGUILayout.LabelField($"설명: {issue.Description}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }
    }
}
