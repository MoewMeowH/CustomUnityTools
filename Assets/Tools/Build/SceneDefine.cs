using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable, CreateAssetMenu(fileName = "IgnoreSceneContainer", menuName = "Define/SceneDefine")]
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

    public Data Get(EBuildType type) => m_Defines.Find(x => x.Type == type);
}
