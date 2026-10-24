using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 환경별(DEV, QA, LIVE) 빌드에서 제외할 씬(IgnoreSceneList)을 정의하는 ScriptableObject입니다.
    /// 선택 사항(Optional)이며, 설정되지 않은 경우 빌드 시 자동으로 건너뜁니다.
    /// </summary>
    [System.Serializable, CreateAssetMenu(fileName = "SceneDefine", menuName = "HB_Build/SceneDefine")]
    public class SceneDefine : ScriptableObject
    {
        [System.Serializable]
        public class Data
        {
            [SerializeField] public EBuildType Type;
            [SerializeField] public List<string> IgnoreSceneList = new List<string>();
        }

        [SerializeField] private List<Data> m_Defines = new List<Data>();

        public bool IsEmpty(EBuildType type) =>
            Get(type)?.IgnoreSceneList?.IsNullOrEmptyCollection() ?? true;

        public Data Get(EBuildType type) => m_Defines.Find(x => x != null && x.Type == type);

        /// <summary>
        /// 지정된 빌드 타입에서 제외되지 않은 활성 씬 목록만 필터링하여 반환합니다.
        /// </summary>
        public List<string> FilterScenes(EBuildType type, IEnumerable<string> scenes)
        {
            if (scenes == null) return new List<string>();
            var data = Get(type);
            if (data == null || data.IgnoreSceneList.IsNullOrEmptyCollection())
                return scenes.ToList();

            var ignoreSet = new HashSet<string>(data.IgnoreSceneList, StringComparer.OrdinalIgnoreCase);
            return scenes.Where(s => !ignoreSet.Contains(s)).ToList();
        }

        /// <summary>
        /// Assets/Editor/HBBuild/SceneDefine.asset 위치에 에셋이 존재하면 로드합니다. 없으면 프로젝트에 하나뿐일 때 그것, 여러 개면 null (창의 자동 연결용)
        /// </summary>
        public static SceneDefine LoadIfExists() =>
            BuildHelper.LoadSettingsAsset<SceneDefine>(BuildHelper.USER_SCENE_DEFINE_PATH);

        /// <summary>
        /// 빌드용: 기본 경로 우선(읽지 못하면 실패). 기본 경로에 없고 여러 개면 LIVE는 실패, DEV · QA는 경고 후 경로 순 첫 번째
        /// </summary>
        public static SceneDefine LoadIfExists(BuildContext context) =>
            BuildHelper.LoadSettingsAssetForBuild<SceneDefine>(BuildHelper.USER_SCENE_DEFINE_PATH, context);

        /// <summary>
        /// Assets/Editor/HBBuild/SceneDefine.asset 에 새 ScriptableObject 에셋을 생성합니다.
        /// 이미 있으면 새로 만들지 않고 기존 에셋을 반환합니다. (CreateAsset은 같은 경로의 기존 에셋을 지우고 만든다)
        /// </summary>
        public static SceneDefine CreateAssetInUserFolder()
        {
            string path = BuildHelper.USER_SCENE_DEFINE_PATH;
            var existing = AssetDatabase.LoadAssetAtPath<SceneDefine>(path);
            if (existing == null && File.Exists(path))
            {
                // 파일은 있는데 아직 임포트 전일 수 있다 (자동 Refresh 꺼짐, git pull 직후 등)
                AssetDatabase.ImportAsset(path);
                existing = AssetDatabase.LoadAssetAtPath<SceneDefine>(path);
            }

            if (existing != null)
            {
                Debug.Log($"[SceneDefine] Asset already exists. Using: {path}");
                return existing;
            }

            if (File.Exists(path))
                throw new InvalidOperationException($"[SceneDefine] A file exists at {path} but cannot be loaded as SceneDefine (different type, import error, or missing script). Check the file before creating a new one; it was not overwritten.");

            BuildHelper.EnsureUserBuildDataDirExists();

            var instance = CreateInstance<SceneDefine>();

            // 기본 3가지 환경 데이터 초기화
            instance.m_Defines.Add(new Data { Type = EBuildType.DEV });
            instance.m_Defines.Add(new Data { Type = EBuildType.QA });
            instance.m_Defines.Add(new Data { Type = EBuildType.LIVE });

            AssetDatabase.CreateAsset(instance, BuildHelper.USER_SCENE_DEFINE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SceneDefine] Created user asset at: {BuildHelper.USER_SCENE_DEFINE_PATH}");
            return instance;
        }
    }
}
