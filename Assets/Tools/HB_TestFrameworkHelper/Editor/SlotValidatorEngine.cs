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
        MissingReference,    // 연결된 에셋/오브젝트가 삭제되어 Missing 상태임 (현재는 구분하지 않고 NullSlot으로 보고)
        MissingComponent,    // 스크립트 연결 자체가 끊어진 컴포넌트 (Missing Script)
        NullElement,         // [NeedSlotNullCheck] 배열 · List 슬롯의 원소가 비어있음 (경고)
    }

    public enum EValidationSeverity
    {
        Error,   // 테스트 실패 · LIVE 빌드 차단
        Warning, // 로그만
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
        public EValidationSeverity Severity = EValidationSeverity.Error;

        public override string ToString()
        {
            return $"[{Severity}][{IssueType}] {AssetPath} > {GameObjectPath} > {ComponentName}.{FieldName}" +
                   (string.IsNullOrEmpty(Description) ? "" : $" ({Description})");
        }
    }

    /// <summary>
    /// 프로젝트 내 프리팹의 [NeedSlotNullCheck] 인스펙터 슬롯 및 Missing 컴포넌트를 스캔하는 코어 엔진입니다.
    /// MonoBehaviour의 직렬화 필드를 따라 중첩 [Serializable] 클래스 · 구조체(배열 · List 원소 포함)까지 검사합니다.
    /// </summary>
    public static class SlotValidatorEngine
    {
        // Unity 직렬화 깊이 상한과 같다. 순환 참조 타입에서 무한히 내려가지 않도록 한다.
        private const int MaxDepth = 10;

        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// 지정한 폴더 내의 모든 프리팹을 스캔하여 슬롯 누락 이슈를 수집합니다.
        /// </summary>
        public static List<SlotValidationIssue> ScanPrefabs(string[] searchFolders = null)
        {
            return ScanPrefabs(searchFolders, false, out _);
        }

        /// <summary>
        /// 지정한 폴더 내의 모든 프리팹을 스캔합니다. showProgress면 취소 가능한 진행 표시를 띄우고, 취소하면 그때까지의 결과를 돌려줍니다.
        /// </summary>
        public static List<SlotValidationIssue> ScanPrefabs(string[] searchFolders, bool showProgress, out bool canceled)
        {
            canceled = false;
            if (searchFolders == null || searchFolders.Length == 0)
            {
                searchFolders = new[] { "Assets" };
            }

            var issues = new List<SlotValidationIssue>();
            var scan = new TypeScanCache();

            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (showProgress &&
                        EditorUtility.DisplayCancelableProgressBar("Slot Validator", $"{path} ({i + 1}/{guids.Length})", (float)i / guids.Length))
                    {
                        canceled = true;
                        break;
                    }

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    ScanMissingComponents(prefab, path, issues);
                    ScanSlots(prefab, path, scan, issues);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            return issues;
        }

        private static void ScanMissingComponents(GameObject prefab, string path, List<SlotValidationIssue> issues)
        {
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
        }

        private static void ScanSlots(GameObject prefab, string path, TypeScanCache scan, List<SlotValidationIssue> issues)
        {
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;

                var mbType = mb.GetType();
                if (!scan.HasTargetFields(mbType)) continue;

                using (var so = new SerializedObject(mb))
                {
                    var target = new ScanTarget
                    {
                        AssetPath = path,
                        Prefab = prefab,
                        GameObjectPath = GetHierarchyPath(mb.transform),
                        ComponentName = mbType.Name,
                        Issues = issues,
                    };
                    CheckFields(so, mbType, string.Empty, target, scan, 0);
                }
            }
        }

        private static void CheckFields(SerializedObject so, Type type, string prefix, ScanTarget target, TypeScanCache scan, int depth)
        {
            if (depth > MaxDepth) return;

            foreach (var field in scan.GetSerializedFields(type))
            {
                string propertyPath = prefix + field.Name;
                Type fieldType = field.FieldType;
                Type elementType = GetCollectionElementType(fieldType);

                var attr = field.GetCustomAttribute<NeedSlotNullCheckAttribute>();
                if (attr != null)
                {
                    CheckSlot(so, propertyPath, attr, elementType != null, target);
                    continue;
                }

                // 중첩 [Serializable] 타입: 배열 · List면 원소마다, 아니면 그대로 내려간다
                if (elementType != null)
                {
                    if (!IsNestedSerializableType(elementType) || !scan.HasTargetFields(elementType)) continue;

                    var listProp = so.FindProperty(propertyPath);
                    if (listProp == null || !listProp.isArray) continue;

                    for (int i = 0; i < listProp.arraySize; i++)
                    {
                        CheckFields(so, elementType, $"{propertyPath}.Array.data[{i}].", target, scan, depth + 1);
                    }
                }
                else if (IsNestedSerializableType(fieldType) && scan.HasTargetFields(fieldType))
                {
                    CheckFields(so, fieldType, propertyPath + ".", target, scan, depth + 1);
                }
            }
        }

        private static void CheckSlot(SerializedObject so, string propertyPath, NeedSlotNullCheckAttribute attr, bool isCollection, ScanTarget target)
        {
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return;

            if (isCollection && prop.isArray)
            {
                // 배열 · List: 빈 목록은 문제 아님, null 원소만 경고 (어트리뷰트에서 끌 수 있음)
                if (!attr.WarnNullElements) return;

                for (int i = 0; i < prop.arraySize; i++)
                {
                    var element = prop.GetArrayElementAtIndex(i);
                    if (element.propertyType == SerializedPropertyType.ObjectReference && element.objectReferenceValue == null)
                    {
                        target.Add($"{ToDisplayPath(propertyPath)}[{i}]", attr.Description, EValidationIssueType.NullElement, EValidationSeverity.Warning);
                    }
                }
                return;
            }

            if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null)
            {
                target.Add(ToDisplayPath(propertyPath), attr.Description, EValidationIssueType.NullSlot, EValidationSeverity.Error);
            }
        }

        /// <summary>T[] 또는 List&lt;T&gt;의 T. 그 밖은 null (Unity가 직렬화하는 컬렉션은 이 둘뿐).</summary>
        private static Type GetCollectionElementType(Type type)
        {
            if (type.IsArray) return type.GetArrayRank() == 1 ? type.GetElementType() : null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Unity가 필드 값으로 직렬화하는 사용자 정의 [Serializable] 클래스 · 구조체인지.</summary>
        private static bool IsNestedSerializableType(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsAbstract || type.IsGenericTypeDefinition) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (!type.IsDefined(typeof(SerializableAttribute), false)) return false;

            // 엔진 · BCL 타입(Vector3 등)에는 [NeedSlotNullCheck]가 있을 수 없으므로 내려가지 않는다.
            // 접두사만 보면 "SystemGameplay" 같은 사용자 namespace까지 빠지므로 '.' 경계까지 맞춘다.
            string ns = type.Namespace ?? string.Empty;
            return !(IsNamespaceOrChild(ns, "UnityEngine") || IsNamespaceOrChild(ns, "UnityEditor") || IsNamespaceOrChild(ns, "System"));
        }

        private static bool IsNamespaceOrChild(string ns, string root)
        {
            return ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);
        }

        /// <summary>"_inner.Array.data[0]._slot" → "_inner[0]._slot"</summary>
        private static string ToDisplayPath(string propertyPath)
        {
            return propertyPath.Replace(".Array.data[", "[");
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

        private sealed class ScanTarget
        {
            public string AssetPath;
            public GameObject Prefab;
            public string GameObjectPath;
            public string ComponentName;
            public List<SlotValidationIssue> Issues;

            public void Add(string fieldName, string description, EValidationIssueType type, EValidationSeverity severity)
            {
                Issues.Add(new SlotValidationIssue
                {
                    AssetPath = AssetPath,
                    PrefabAsset = Prefab,
                    GameObjectPath = GameObjectPath,
                    ComponentName = ComponentName,
                    FieldName = fieldName,
                    Description = description ?? string.Empty,
                    IssueType = type,
                    Severity = severity,
                });
            }
        }

        /// <summary>한 번의 스캔 동안 타입별 직렬화 필드와 "검사 대상 필드가 그래프 안에 있는가"를 캐시한다.</summary>
        private sealed class TypeScanCache
        {
            private readonly Dictionary<Type, List<FieldInfo>> m_SerializedFields = new Dictionary<Type, List<FieldInfo>>();
            private readonly Dictionary<Type, bool> m_HasTargetFields = new Dictionary<Type, bool>();

            /// <summary>Unity가 직렬화하는 인스턴스 필드 (상속 포함, 기반 클래스부터). [SerializeReference]는 경로가 달라 제외.</summary>
            public List<FieldInfo> GetSerializedFields(Type type)
            {
                if (m_SerializedFields.TryGetValue(type, out var cached)) return cached;

                var chain = new List<Type>();
                for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType)
                {
                    chain.Add(t);
                }
                chain.Reverse();

                var fields = new List<FieldInfo>();
                foreach (var t in chain)
                {
                    foreach (var field in t.GetFields(DeclaredInstanceFields))
                    {
                        if (field.IsInitOnly || field.IsLiteral || field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                        if (field.IsDefined(typeof(SerializeReference), false)) continue;
                        if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false)) continue;
                        fields.Add(field);
                    }
                }

                m_SerializedFields[type] = fields;
                return fields;
            }

            /// <summary>
            /// type에서 직렬화 필드를 따라 닿는 타입 중 [NeedSlotNullCheck] 필드를 직접 가진 타입이 있는지.
            /// 방문 집합으로 그래프 전체를 훑어 루트별로 확정된 결과만 캐시한다
            /// (탐색 중의 잠정 값을 캐시하면 순환 타입에서 실제 슬롯을 놓친다). 실제 내려가는 깊이는 CheckFields가 MaxDepth로 끊는다.
            /// </summary>
            public bool HasTargetFields(Type type)
            {
                if (m_HasTargetFields.TryGetValue(type, out bool cached)) return cached;

                bool result = false;
                var visited = new HashSet<Type> { type };
                var pending = new Stack<Type>();
                pending.Push(type);

                while (pending.Count > 0 && !result)
                {
                    foreach (var field in GetSerializedFields(pending.Pop()))
                    {
                        if (field.IsDefined(typeof(NeedSlotNullCheckAttribute), true))
                        {
                            result = true;
                            break;
                        }

                        Type nested = GetCollectionElementType(field.FieldType) ?? field.FieldType;
                        if (IsNestedSerializableType(nested) && visited.Add(nested))
                        {
                            pending.Push(nested);
                        }
                    }
                }

                m_HasTargetFields[type] = result;
                return result;
            }
        }
    }
}
