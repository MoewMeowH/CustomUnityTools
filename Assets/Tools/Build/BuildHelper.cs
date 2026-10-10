using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public readonly struct AndroidBuildParam
{
    public readonly string BundleVersion;
    public readonly int VersionCode;
    public readonly bool IsAAB;
    public readonly EBuildType BuildType;

    public AndroidBuildParam(string bundleVersion, int versionCode, bool isAab, EBuildType buildType)
    {
        BundleVersion = bundleVersion;
        VersionCode = versionCode;
        IsAAB = isAab;
        BuildType = buildType;
    }
}

public readonly struct IOSBuildParam
{
    public readonly string BundleVersion;
    public readonly string VersionCode;
    public readonly string ProvisionUUID;
    public readonly EBuildType BuildType;

    public IOSBuildParam(string bundleVersion, EBuildType buildType, string provisionUuid, string versionCode)
    {
        BundleVersion = bundleVersion;
        BuildType = buildType;
        ProvisionUUID = provisionUuid;
        VersionCode = versionCode;
    }
}

public readonly struct PCBuildParam
{
    public readonly string BundleVersion;
    public readonly bool EnablePDB;
    public readonly EBuildType BuildType;

    public PCBuildParam(string bundleVersion, bool enablePdb, EBuildType buildType)
    {
        BundleVersion = bundleVersion;
        EnablePDB = enablePdb;
        BuildType = buildType;
    }
}

public class JenkinsBuildException : BuildFailedException
{
    public JenkinsBuildException(string message) : base(message) { }
    public JenkinsBuildException(Exception innerException) : base(innerException) { }
}

public enum EBuildType
{
    DEV,
    QA,
    LIVE,
}

public enum EPlatformType
{
    NONE,
    PC,
    MOBILE,
}

public static class BuildHelper
{
    public const string JENKINS_BUILD_NUM = "BUILD_NUMBER";
    public const string JENKINS_HOME = "JENKINS_HOME";
    public const string JOB_NAME = "JOB_NAME";

    public const string BUILD_TARGET = "buildTarget";
    public const string BUILD_TYPE = "buildType";
    public const string BUILD_VERSION = "buildVersion";
    public const string VERSION_CODE = "versionCode";
    public const string IS_AAB = "isAab";
    public const string SCENE_LIST = "sceneList";
    public const string ENABLE_PDB = "enablePdb";
    public const string DEV_BUILD = "devBuild";
    public const string AUTO_PROF = "autoProfile";
    public const string DEEP_PROF = "deepProfile";
    public const string CLEAN_BUILD = "cleanBuild";
    public const string OUTPUT_PATH = "outputPath";
    public const string PROVISION_UUID = "provisionUuid";
    public const string ZIP_PACKAGE = "zipPackage";

    public const string USER_BUILD_DATA_DIR = "Assets/Editor/HBBuild";
    public const string USER_DEFINE_CONTAINER_PATH = "Assets/Editor/HBBuild/DefineContainer.asset";
    public const string USER_SCENE_DEFINE_PATH = "Assets/Editor/HBBuild/SceneDefine.asset";
    public const string NONE_SCENE_LIST = "NONE";

    private static List<string> s_Args = new List<string>();
    private const string LOG_HEAD = "[HB_Build]";

    public static bool IsDevBuild = false;
    public static bool IsDeepProfile = false;
    public static bool IsAutoProfile = false;
    public static bool IsCleanBuild = false;
    public static string ProjectPath = string.Empty;

    public static bool IsNullOrEmptyCollection<T>(this ICollection<T> collection)
    {
        return collection == null || collection.Count == 0;
    }

    public static void SetUpAllArgs()
    {
        s_Args = Environment.GetCommandLineArgs().ToList();
        Log($"Command Line Args Count: {s_Args.Count}");
    }

    public static void ReadDevBuildState()
    {
        var isDevBuildStr = GetArg(DEV_BUILD);
        if (!string.IsNullOrEmpty(isDevBuildStr))
            bool.TryParse(isDevBuildStr, out IsDevBuild);

        var isProfStr = GetArg(AUTO_PROF);
        if (!string.IsNullOrEmpty(isProfStr))
            bool.TryParse(isProfStr, out IsAutoProfile);

        var isDeepStr = GetArg(DEEP_PROF);
        if (!string.IsNullOrEmpty(isDeepStr))
            bool.TryParse(isDeepStr, out IsDeepProfile);

        var isCleanBuildStr = GetArg(CLEAN_BUILD);
        if (!string.IsNullOrEmpty(isCleanBuildStr))
            bool.TryParse(isCleanBuildStr, out IsCleanBuild);

        if (!IsDevBuild && IsDeepProfile)
            IsDeepProfile = false;
    }

    public static void ReadEnvironment()
    {
        var projectPath = GetArg("-projectPath");
        if (string.IsNullOrEmpty(projectPath))
        {
            ProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
        else
        {
            ProjectPath = Path.GetFullPath(projectPath);
        }

        Log($"Project Root: {ProjectPath}");
    }

    public static string GetArg(string argName)
    {
        if (string.IsNullOrEmpty(argName) || s_Args == null || s_Args.Count == 0)
            return string.Empty;

        for (int i = 0; i < s_Args.Count - 1; i++)
        {
            string current = s_Args[i].TrimStart('-');
            if (current.Equals(argName.TrimStart('-'), StringComparison.OrdinalIgnoreCase))
            {
                return s_Args[i + 1].Trim('\"', '\'');
            }
        }

        return string.Empty;
    }

    public static string GetDefaultOutputPath(BuildTarget target)
    {
        string root = string.IsNullOrEmpty(ProjectPath) 
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..")) 
            : ProjectPath;

        return Path.Combine(root, "BuildOutput", target.ToString());
    }

    public static void Log(string str)
    {
        Console.WriteLine($"{LOG_HEAD} {str}");
        Debug.Log($"{LOG_HEAD} {str}");
    }

    public static JenkinsBuildException ThrowError(string msg) => new JenkinsBuildException($"{LOG_HEAD} :: {msg}");

    public static BuildTargetGroup GetTargetGroup(BuildTarget buildTarget) => buildTarget switch
    {
        BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => BuildTargetGroup.Standalone,
        BuildTarget.iOS => BuildTargetGroup.iOS,
        BuildTarget.Android => BuildTargetGroup.Android,
        _ => BuildTargetGroup.Standalone,
    };

    public static string[] GetDefaultBuildScenes()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length > 0)
            return scenes;

        var foundGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        if (foundGuids.Length > 0)
        {
            return new[] { AssetDatabase.GUIDToAssetPath(foundGuids[0]) };
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Assets/Editor/HBBuild 폴더를 확인하고 없으면 자동 생성합니다.
    /// </summary>
    public static void EnsureUserBuildDataDirExists()
    {
        if (!Directory.Exists(USER_BUILD_DATA_DIR))
        {
            Directory.CreateDirectory(USER_BUILD_DATA_DIR);
            AssetDatabase.Refresh();
        }
    }
}
