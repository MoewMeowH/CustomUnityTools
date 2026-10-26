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
        MissingReference,    // 연결된 에셋/오브젝트가 삭제되어 Missing 상태임 (단일 슬롯 · 배열 원소 모두 에러)
        MissingComponent,    // 스크립트 연결 자체가 끊어진 컴포넌트 (Missing Script)
        NullElement,         // [NeedSlotNullCheck] 배열 · List 슬롯의 원소가 비어있음 (경고)
        MissingShader,       // 머티리얼의 셰이더가 없음 (삭제됨)
        ErrorShader,         // 머티리얼이 에러 셰이더(Hidden/InternalErrorShader)를 씀
        ShaderCompileError,  // 머티리얼의 셰이더에 컴파일 에러가 있음 (경고)
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

        /// <summary>낸 검사 (<see cref="AssetCheckIds"/>)</summary>
        public string CheckId;

        /// <summary>Ping 대상 에셋: 프리팹 루트 · ScriptableObject · 머티리얼 · 씬 에셋(SceneAsset). 씬 안의 오브젝트는 담지 않는다.</summary>
        public UnityEngine.Object Asset;

        /// <summary>같은 파일 안의 서브에셋이면 "이름 (타입)", 아니면 null</summary>
        public string SubAssetName;

        /// <summary>저장하지 않은 변경이 있는 열린 씬을 그 상태 그대로 검사한 결과인지</summary>
        public bool IsUnsavedScene;

        public override string ToString()
        {
            // Missing 스크립트는 필드가 없으므로 "컴포넌트."로 끝나지 않게 한다
            string member = string.IsNullOrEmpty(FieldName) ? ComponentName : $"{ComponentName}.{FieldName}";
            return $"[{Severity}][{IssueType}] {AssetPath} > {GameObjectPath} > {member}" +
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

        // 불러온 프리팹은 스캔이 끝날 때까지 메모리에 남는다 → 이만큼마다 쓰지 않는 에셋을 내린다 (결과가 잡고 있는 프리팹은 남음)
        private const int UnloadInterval = 100;

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

            // 중첩 · 변형 프리팹이 원본에서 그대로 물려받은 값은 원본이 이번 스캔에 있으면 거기서 보고되므로 건너뛴다
            var scannedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                scannedPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

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

                    ScanMissingComponents(prefab, path, scannedPaths, issues);
                    ScanSlots(prefab, path, scannedPaths, scan, issues);

                    if ((i + 1) % UnloadInterval == 0)
                    {
                        EditorUtility.UnloadUnusedAssetsImmediate();
                    }
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            return issues;
        }

        /// <summary>새 엔진(AssetHealthEngine)이 Missing 스크립트 검사를 프리팹 밖(씬)에서 쓸 때 넘기는 정보. null이면 기존 프리팹 동작.</summary>
        internal sealed class MissingScriptHooks
        {
            public UnityEngine.Object Asset;
            public string CheckId;
            public bool IsUnsavedScene;
            /// <summary>씬 오브젝트: 원본 프리팹 체인이 이번 검사에서 이미 보고한 Missing 스크립트 개수의 합 (빼고 남은 것만 보고)</summary>
            public Func<GameObject, int> ReportedBySource;
            /// <summary>보고할 때마다 (오브젝트, 보고한 개수)</summary>
            public Action<GameObject, int> OnReported;
        }

        internal static void ScanMissingComponents(GameObject root, string path, HashSet<string> scannedPaths, List<SlotValidationIssue> issues, MissingScriptHooks hooks = null)
        {
            // GetComponentsInChildren의 null 항목은 소속 오브젝트를 알 수 없으므로, 오브젝트 단위로 Missing 개수를 센다
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                if (missingCount == 0) continue;

                // 원본(중첩 · 변형) 프리팹이 이번 스캔에 있으면 원본에 있던 개수는 거기서 보고된다 → 이 프리팹에서 늘어난 것만
                if (scannedPaths != null && TryGetScannedSource(transform.gameObject, scannedPaths, out var sourceObject))
                {
                    missingCount -= GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(sourceObject);
                    if (missingCount <= 0) continue;
                }
                else if (hooks?.ReportedBySource != null)
                {
                    missingCount -= hooks.ReportedBySource(transform.gameObject);
                    if (missingCount <= 0) continue;
                }

                bool unsaved = hooks != null && hooks.IsUnsavedScene;
                issues.Add(new SlotValidationIssue
                {
                    AssetPath = path,
                    PrefabAsset = hooks == null ? root : hooks.Asset as GameObject,
                    GameObjectPath = GetHierarchyPath(transform),
                    ComponentName = "Missing (MonoBehaviour)",
                    FieldName = string.Empty,
                    Description = $"스크립트 연결이 끊어진 컴포넌트가 {missingCount}개 존재합니다." + UnsavedSuffix(unsaved),
                    IssueType = EValidationIssueType.MissingComponent,
                    CheckId = hooks == null ? AssetCheckIds.PrefabSlots : hooks.CheckId,
                    Asset = hooks == null ? root : hooks.Asset,
                    IsUnsavedScene = unsaved,
                });
                hooks?.OnReported?.Invoke(transform.gameObject, missingCount);
            }
        }

        private static void ScanSlots(GameObject prefab, string path, HashSet<string> scannedPaths, TypeScanCache scan, List<SlotValidationIssue> issues)
        {
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;

                var mbType = mb.GetType();
                if (!scan.HasTargetFields(mbType)) continue;

                var target = new ScanTarget
                {
                    AssetPath = path,
                    Prefab = prefab,
                    Asset = prefab,
                    CheckId = AssetCheckIds.PrefabSlots,
                    GameObjectPath = GetHierarchyPath(mb.transform),
                    ComponentName = mbType.Name,
                    Issues = issues,
                    CurrentObject = mb,
                };
                ScanObjectSlots(mb, mbType, target, scannedPaths, scan);
            }
        }

        /// <summary>
        /// obj(MonoBehaviour · ScriptableObject)의 [NeedSlotNullCheck] 슬롯을 검사한다.
        /// obj가 이번 스캔 대상인 원본 프리팹에서 왔으면 물려받은 값은 건너뛴다 (scannedPaths가 null이면 원본 확인 안 함).
        /// </summary>
        internal static void ScanObjectSlots(UnityEngine.Object obj, Type type, ScanTarget target, HashSet<string> scannedPaths, TypeScanCache scan)
        {
            using (var so = new SerializedObject(obj))
            using (var sourceSo = CreateScannedSourceObject(obj, scannedPaths))
            {
                target.SourceObject = sourceSo;
                CheckFields(so, type, string.Empty, target, scan, 0);
                target.SourceObject = null;
            }
        }

        /// <summary>obj의 원본(중첩 · 변형)이 이번 스캔 대상이면 그 SerializedObject (부른 쪽이 Dispose), 아니면 null</summary>
        internal static SerializedObject CreateScannedSourceObject(UnityEngine.Object obj, HashSet<string> scannedPaths)
        {
            if (scannedPaths == null || !TryGetScannedSource(obj, scannedPaths, out var source)) return null;
            return new SerializedObject(source);
        }

        private static void CheckFields(SerializedObject so, Type type, string prefix, ScanTarget target, TypeScanCache scan, int depth)
        {
            if (depth > MaxDepth) return;

            foreach (var field in scan.GetSerializedFields(type))
            {
                string propertyPath = prefix + field.Name;
                Type fieldType = field.FieldType;
                Type elementType = GetCollectionElementType(fieldType);

                var attr = scan.GetSlotAttribute(field);
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

            // 이 칸은 슬롯 검사가 맡는다 (일반 Missing 검사가 같은 칸을 다시 보고하지 않게)
            target.OwnedPaths?.Add(propertyPath);

            if (isCollection && prop.isArray)
            {
                // 배열 · List: 빈 목록은 문제 아님. Missing 원소는 에러, 빈 원소는 경고 (빈 원소 경고만 어트리뷰트에서 끌 수 있음)
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var element = prop.GetArrayElementAtIndex(i);
                    target.OwnedPaths?.Add(element.propertyPath);
                    if (element.propertyType != SerializedPropertyType.ObjectReference || element.objectReferenceValue != null) continue;

                    bool missing = IsMissingReference(element);
                    if (!missing && !attr.WarnNullElements) continue;

                    var type = missing ? EValidationIssueType.MissingReference : EValidationIssueType.NullElement;
                    var severity = missing ? EValidationSeverity.Error : EValidationSeverity.Warning;
                    if (target.ShouldSkip(element, type, severity)) continue;

                    string elementName = $"{ToDisplayPath(propertyPath)}[{i}]";
                    target.Add(element.propertyPath, elementName, attr.Description, type, severity);
                }
                return;
            }

            if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null)
            {
                var type = IsMissingReference(prop) ? EValidationIssueType.MissingReference : EValidationIssueType.NullSlot;
                if (target.ShouldSkip(prop, type, EValidationSeverity.Error)) return;
                target.Add(propertyPath, ToDisplayPath(propertyPath), attr.Description, type, EValidationSeverity.Error);
            }
        }

        internal static string UnsavedSuffix(bool unsaved) => unsaved ? " [저장 안 된 변경 포함]" : string.Empty;

        /// <summary>값은 비었는데 ID가 남아 있으면 삭제된 에셋을 가리키는 참조(Missing)다. 빈 칸은 0(None)이다.</summary>
        internal static bool IsMissingReference(SerializedProperty prop)
        {
            // 6000.3에는 objectReferenceEntityIdValue가 없고, Unity 7에서는 instanceID 쪽이 컴파일 에러(CS0619)다.
#if UNITY_7000_0_OR_NEWER
            return prop.objectReferenceValue == null && prop.objectReferenceEntityIdValue != EntityId.None;
#else
            return prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0;
#endif
        }

        /// <summary>obj가 다른 프리팹(중첩 · 변형의 원본)에서 왔고 그 원본이 이번 스캔 대상인지. 아니면 source는 null.</summary>
        internal static bool TryGetScannedSource<T>(T obj, HashSet<string> scannedPaths, out T source) where T : UnityEngine.Object
        {
            source = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            if (source != null && scannedPaths.Contains(AssetDatabase.GetAssetPath(source))) return true;

            source = null;
            return false;
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
        internal static string ToDisplayPath(string propertyPath)
        {
            return propertyPath.Replace(".Array.data[", "[");
        }

        internal static string GetHierarchyPath(Transform t)
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

        internal sealed class ScanTarget
        {
            public string AssetPath;
            public GameObject Prefab;
            public string GameObjectPath;
            public string ComponentName;
            public List<SlotValidationIssue> Issues;
            /// <summary>스캔 대상인 원본 프리팹의 같은 컴포넌트. 원본이 없거나 스캔 대상 밖이면 null.</summary>
            public SerializedObject SourceObject;

            // ── 새 엔진(AssetHealthEngine)용. 기존 프리팹 스캔은 기본값 그대로 ──
            public UnityEngine.Object Asset;
            public string CheckId;
            public string SubAssetName;
            public bool IsUnsavedScene;
            /// <summary>지금 검사 중인 컴포넌트 · ScriptableObject (보고 기록용)</summary>
            public UnityEngine.Object CurrentObject;
            /// <summary>슬롯 검사가 맡은 속성 경로를 모은다 (null이면 모으지 않음)</summary>
            public HashSet<string> OwnedPaths;
            /// <summary>추가 건너뛰기 판정 (씬 안 프리팹 인스턴스: 원본이 같은 심각도 이상으로 이미 보고한 문제)</summary>
            public Func<SerializedProperty, EValidationIssueType, EValidationSeverity, bool> SkipReportedBySource;
            /// <summary>보고할 때마다 (오브젝트, 원래 속성 경로, 종류, 심각도)</summary>
            public Action<UnityEngine.Object, string, EValidationIssueType, EValidationSeverity> OnReported;

            public bool ShouldSkip(SerializedProperty prop, EValidationIssueType type, EValidationSeverity severity)
            {
                return IsInheritedValue(prop) || (SkipReportedBySource != null && SkipReportedBySource(prop, type, severity));
            }

            /// <summary>
            /// 스캔 대상인 원본 프리팹에서 덮어쓰지 않고 물려받은 값인지 (원본에서 보고되므로 여기서는 건너뛴다).
            /// 이 프리팹에서 늘린 배열 원소는 prefabOverride가 false여도 원본에 그 경로가 없으므로 물려받은 값이 아니다.
            /// 원본도 같은 상태(빈 칸 / Missing)일 때만 원본에서 보고되므로, 원본 값이 있으면 여기서 보고한다 (원본만 정상인 경우를 놓치지 않게).
            /// </summary>
            public bool IsInheritedValue(SerializedProperty prop)
            {
                if (SourceObject == null || prop.prefabOverride) return false;

                var sourceProp = SourceObject.FindProperty(prop.propertyPath);
                return sourceProp != null && sourceProp.objectReferenceValue == null && IsMissingReference(sourceProp) == IsMissingReference(prop);
            }

            public void Add(string propertyPath, string fieldName, string description, EValidationIssueType type, EValidationSeverity severity)
            {
                Issues.Add(new SlotValidationIssue
                {
                    AssetPath = AssetPath,
                    PrefabAsset = Prefab,
                    GameObjectPath = GameObjectPath,
                    ComponentName = ComponentName,
                    FieldName = fieldName,
                    Description = (description ?? string.Empty) + UnsavedSuffix(IsUnsavedScene),
                    IssueType = type,
                    Severity = severity,
                    CheckId = CheckId,
                    Asset = Asset,
                    SubAssetName = SubAssetName,
                    IsUnsavedScene = IsUnsavedScene,
                });
                OnReported?.Invoke(CurrentObject, propertyPath, type, severity);
            }
        }

        /// <summary>한 번의 스캔 동안 타입별 직렬화 필드와 "검사 대상 필드가 그래프 안에 있는가"를 캐시한다.</summary>
        internal sealed class TypeScanCache
        {
            private readonly Dictionary<Type, List<FieldInfo>> m_SerializedFields = new Dictionary<Type, List<FieldInfo>>();
            private readonly Dictionary<Type, bool> m_HasTargetFields = new Dictionary<Type, bool>();
            private readonly Dictionary<FieldInfo, NeedSlotNullCheckAttribute> m_SlotAttributes = new Dictionary<FieldInfo, NeedSlotNullCheckAttribute>();

            /// <summary>필드의 [NeedSlotNullCheck] (없으면 null). 같은 타입을 여러 번 검사해도 리플렉션은 한 번.</summary>
            public NeedSlotNullCheckAttribute GetSlotAttribute(FieldInfo field)
            {
                if (!m_SlotAttributes.TryGetValue(field, out var attr))
                {
                    attr = field.GetCustomAttribute<NeedSlotNullCheckAttribute>();
                    m_SlotAttributes[field] = attr;
                }
                return attr;
            }

            /// <summary>Unity가 직렬화하는 인스턴스 필드 (상속 포함, 기반 클래스부터). [SerializeReference]는 경로가 달라 제외.</summary>
            public List<FieldInfo> GetSerializedFields(Type type)
            {
                if (m_SerializedFields.TryGetValue(type, out var cached)) return cached;

                var chain = new List<Type>();
                for (var t = type;
                     t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object) && t != typeof(object);
                     t = t.BaseType)
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
