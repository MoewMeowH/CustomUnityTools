using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    public enum EValidationIssueType
    {
        NullSlot,            // [NeedSlotNullCheck] 필드가 비어있음
        MissingReference,    // 연결된 에셋/오브젝트가 삭제되어 Missing 상태임
        MissingComponent,    // 스크립트 연결 자체가 끊어진 컴포넌트 (Missing Script)
    }

    [Serializable]
    public class SlotValidationIssue
    {
        public string AssetPath;
        public GameObject PrefabAsset;
        public string GameObjectPath;
        public string ComponentName;
        public string FieldName;
        public string Description;
        public EValidationIssueType IssueType;

        public override string ToString()
        {
            return $"[{IssueType}] {AssetPath} > {GameObjectPath} > {ComponentName}.{FieldName}" +
                   (string.IsNullOrEmpty(Description) ? "" : $" ({Description})");
        }
    }

    /// <summary>
    /// 프로젝트 내 프리팹의 [NeedSlotNullCheck] 인스펙터 슬롯 및 Missing 컴포넌트를 스캔하는 코어 엔진입니다.
    /// </summary>
    public static class SlotValidatorEngine
    {
        /// <summary>
        /// 지정한 폴더 내의 모든 프리팹을 스캔하여 슬롯 누락 이슈를 수집합니다.
        /// </summary>
        public static List<SlotValidationIssue> ScanPrefabs(string[] searchFolders = null)
        {
            if (searchFolders == null || searchFolders.Length == 0)
            {
                searchFolders = new[] { "Assets" };
            }

            var issues = new List<SlotValidationIssue>();

            // 1. [NeedSlotNullCheck] 어트리뷰트를 가진 필드 목록 수집
            var targetFields = TypeCache.GetFieldsWithAttribute<NeedSlotNullCheckAttribute>();
            var fieldLookup = new Dictionary<Type, List<FieldInfo>>();

            foreach (var field in targetFields)
            {
                var declaringType = field.DeclaringType;
                if (declaringType == null) continue;

                if (!fieldLookup.TryGetValue(declaringType, out var list))
                {
                    list = new List<FieldInfo>();
                    fieldLookup[declaringType] = list;
                }
                list.Add(field);
            }

            // 2. 프로젝트의 모든 프리팹 검색
            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // 2-1. Missing Component 스크립트 검사
                // GetComponentsInChildren의 null 항목은 소속 오브젝트를 알 수 없으므로, 오브젝트 단위로 Missing 개수를 센다
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    if (missingCount == 0) continue;

                    issues.Add(new SlotValidationIssue
                    {
                        AssetPath = path,
                        PrefabAsset = prefab,
                        GameObjectPath = GetHierarchyPath(transform),
                        ComponentName = "Missing (MonoBehaviour)",
                        FieldName = string.Empty,
                        Description = $"스크립트 연결이 끊어진 컴포넌트가 {missingCount}개 존재합니다.",
                        IssueType = EValidationIssueType.MissingComponent
                    });
                }

                // 2-2. [NeedSlotNullCheck] 필드 누락 검사
                var monoBehaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var mb in monoBehaviours)
                {
                    if (mb == null) continue;

                    var mbType = mb.GetType();

                    // 상속 계층 순회하여 등록된 필드 검색
                    foreach (var pair in fieldLookup)
                    {
                        if (!pair.Key.IsAssignableFrom(mbType))
                            continue;

                        var so = new SerializedObject(mb);

                        foreach (var field in pair.Value)
                        {
                            var attr = field.GetCustomAttribute<NeedSlotNullCheckAttribute>();
                            string desc = attr != null ? attr.Description : string.Empty;

                            var prop = so.FindProperty(field.Name);
                            if (prop != null)
                            {
                                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                                {
                                    if (prop.objectReferenceValue == null)
                                    {
                                        issues.Add(new SlotValidationIssue
                                        {
                                            AssetPath = path,
                                            PrefabAsset = prefab,
                                            GameObjectPath = GetHierarchyPath(mb.transform),
                                            ComponentName = mbType.Name,
                                            FieldName = field.Name,
                                            Description = desc,
                                            IssueType = EValidationIssueType.NullSlot
                                        });
                                    }
                                }
                            }
                            else
                            {
                                object val = field.GetValue(mb);
                                if (val == null || (val is UnityEngine.Object uObj && uObj == null))
                                {
                                    issues.Add(new SlotValidationIssue
                                    {
                                        AssetPath = path,
                                        PrefabAsset = prefab,
                                        GameObjectPath = GetHierarchyPath(mb.transform),
                                        ComponentName = mbType.Name,
                                        FieldName = field.Name,
                                        Description = desc,
                                        IssueType = EValidationIssueType.NullSlot
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return issues;
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return string.Empty;
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
