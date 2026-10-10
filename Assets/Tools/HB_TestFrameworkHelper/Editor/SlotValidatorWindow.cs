using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// [NeedSlotNullCheck] 인스펙터 슬롯 누락 및 Missing Script를 시각적으로 확인하고
    /// 해당 프리팹으로 즉시 점프할 수 있는 에디터 윈도우입니다.
    /// </summary>
    public class SlotValidatorWindow : EditorWindow
    {
        private List<SlotValidationIssue> m_Issues = new List<SlotValidationIssue>();
        private Vector2 m_ScrollPosition;
        private string m_SearchFolder = "Assets";
        private bool m_HasScanned = false;

        [MenuItem("Tools/HB/Test Framework Helper")]
        public static void ShowWindow()
        {
            var window = GetWindow<SlotValidatorWindow>("HB Test Helper");
            window.minSize = new Vector2(500, 450);
            window.Show();
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
            GUILayout.Label("🛠️ HB Test Framework Helper - Slot Validator", EditorStyles.boldLabel);
            GUILayout.Label("프리팹의 [NeedSlotNullCheck] 필수 슬롯 누락 및 Missing 스크립트를 전수 검사합니다.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            EditorGUILayout.LabelField("검사 폴더:", GUILayout.Width(65));
            m_SearchFolder = EditorGUILayout.TextField(m_SearchFolder, EditorStyles.toolbarTextField);

            if (GUILayout.Button("폴더 선택", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFolderPanel("검사할 폴더 선택", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        m_SearchFolder = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("알림", "Assets 폴더 내부의 경로만 지정할 수 있습니다.", "확인");
                    }
                }
            }

            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            if (GUILayout.Button("🚀 전수 검사 실행", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                ExecuteScan();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void ExecuteScan()
        {
            m_Issues = SlotValidatorEngine.ScanPrefabs(new[] { m_SearchFolder });
            m_HasScanned = true;
            Repaint();
        }

        private void DrawResultsList()
        {
            if (!m_HasScanned)
            {
                EditorGUILayout.HelpBox("상단의 '전수 검사 실행' 버튼을 눌러 프로젝트 프리팹을 검사하세요.", MessageType.Info);
                return;
            }

            if (m_Issues.Count == 0)
            {
                GUI.color = Color.green;
                EditorGUILayout.HelpBox("🎉 완벽합니다! 누락된 [NeedSlotNullCheck] 슬롯이나 Missing 스크립트가 없습니다.", MessageType.Info);
                GUI.color = Color.white;
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUI.color = new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"⚠️ 총 {m_Issues.Count}개의 슬롯/컴포넌트 누락이 발견되었습니다!", EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            for (int i = 0; i < m_Issues.Count; i++)
            {
                var issue = m_Issues[i];

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();

                string typeLabel = issue.IssueType switch
                {
                    EValidationIssueType.NullSlot => "[빈 슬롯 (Null)]",
                    EValidationIssueType.MissingReference => "[Missing 참조]",
                    EValidationIssueType.MissingComponent => "[Missing 스크립트]",
                    _ => "[이슈]"
                };

                GUI.color = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label(typeLabel, EditorStyles.boldLabel, GUILayout.Width(120));
                GUI.color = Color.white;

                GUILayout.Label($"{issue.ComponentName} > {issue.FieldName}", EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("에셋 열기 (Ping)", EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    if (issue.PrefabAsset != null)
                    {
                        EditorGUIUtility.PingObject(issue.PrefabAsset);
                        Selection.activeObject = issue.PrefabAsset;
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"경로: {issue.AssetPath} ({issue.GameObjectPath})", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(issue.Description))
                {
                    EditorGUILayout.LabelField($"설명: {issue.Description}", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
