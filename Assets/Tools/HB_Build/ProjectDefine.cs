using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 환경별(DEV, QA, LIVE) 및 플랫폼별 Scripting Define Symbols(전처리기 심볼)을 정의하는 ScriptableObject입니다.
/// 선택 사항(Optional)이며, 설정되지 않은 경우 기존 PlayerSettings 설정을 유지하고 건너뜁니다.
/// </summary>
[System.Serializable, CreateAssetMenu(fileName = "DefineContainer", menuName = "HB_Build/DefineContainer")]
public class ProjectDefine : ScriptableObject
{
    [System.Serializable]
    public class Container
    {
        public EBuildType BuildType;
        public EPlatformType PlatformType;
        public string[] DefineArr;
    }

    [SerializeField] private List<Container> m_DefineContainer = new List<Container>();

    public string[] GetDefineArray(EBuildType buildType, BuildTarget buildTarget)
    {
        var platformType = buildTarget switch
        {
            BuildTarget.iOS or BuildTarget.Android => EPlatformType.MOBILE,
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => EPlatformType.PC,
            _ => EPlatformType.NONE,
        };

        if (platformType is EPlatformType.NONE)
        {
            Debug.LogError("[ProjectDefine] This Target Is Not Supported");
            return Array.Empty<string>();
        }

        if (m_DefineContainer.IsNullOrEmptyCollection())
        {
            return Array.Empty<string>();
        }

        var target = m_DefineContainer.Find(x => x != null && x.BuildType == buildType && x.PlatformType == platformType);
        if (target == null)
        {
            return Array.Empty<string>();
        }

        return target.DefineArr;
    }

    public bool SetDefineArray(EBuildType _buildType, EPlatformType _PlatformType, string[] defines)
    {
        int idx = m_DefineContainer.FindIndex(x => x?.BuildType == _buildType && x?.PlatformType == _PlatformType);
        if (idx >= 0)
        {
            m_DefineContainer[idx].DefineArr = defines;
            return true;
        }
        else
        {
            m_DefineContainer.Add(new Container
            {
                BuildType = _buildType,
                PlatformType = _PlatformType,
                DefineArr = defines
            });
            return true;
        }
    }

    /// <summary>
    /// Assets/Editor/HBBuild/DefineContainer.asset 위치에 에셋이 존재하면 로드합니다.
    /// </summary>
    public static ProjectDefine LoadIfExists()
    {
        if (File.Exists(BuildHelper.USER_DEFINE_CONTAINER_PATH))
        {
            return AssetDatabase.LoadAssetAtPath<ProjectDefine>(BuildHelper.USER_DEFINE_CONTAINER_PATH);
        }

        // 전체 프로젝트에서 검색
        var guids = AssetDatabase.FindAssets("t:ProjectDefine");
        if (guids.Length > 0)
        {
            return AssetDatabase.LoadAssetAtPath<ProjectDefine>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        return null;
    }

    /// <summary>
    /// Assets/Editor/HBBuild/DefineContainer.asset 에 새 ScriptableObject 에셋을 생성합니다.
    /// </summary>
    public static ProjectDefine CreateAssetInUserFolder()
    {
        BuildHelper.EnsureUserBuildDataDirExists();

        var instance = CreateInstance<ProjectDefine>();
        AssetDatabase.CreateAsset(instance, BuildHelper.USER_DEFINE_CONTAINER_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ProjectDefine] Created user asset at: {BuildHelper.USER_DEFINE_CONTAINER_PATH}");
        return instance;
    }
}
