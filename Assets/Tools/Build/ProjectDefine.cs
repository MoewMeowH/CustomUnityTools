using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[System.Serializable, CreateAssetMenu(fileName = "DefineContainer", menuName = "Define/DefineContainer")]
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
}
