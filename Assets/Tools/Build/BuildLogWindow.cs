using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class BuildConfirmWindow : EditorWindow
{
    private static string label = string.Empty;
    private static System.Action callback = null;

    public static void SetData(string lbl, System.Action evt)
    {
        label = lbl;
        callback = evt;
    }

    private void OnGUI()
    {
        //Table Parser Make
        GUILayout.Label(string.Format("{0} 하시겠습니까?", label));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("진행", GUILayout.Height(100)))
        {
            if (callback != null)
            {
                callback();
            }
        }

        //Table Parser Make
        if (GUILayout.Button("취소", GUILayout.Height(100)))
        {
            callback = null;
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

public class BuildLogWindow : EditorWindow
{
    private struct AssetInfo
    {
        public string type;
        public string path;
        public string outputFile;
        public int size;
        public Texture icon;
    }

    #region TextStyle

    private static GUIStyle s_OddStyle;
    private GUIStyle OddStyle
    {
        get
        {
            if (s_OddStyle == null)
            {
                s_OddStyle = new GUIStyle(GUIStyle.none);
                s_OddStyle.normal.background = MakeColorTexture(new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }
            return s_OddStyle;
        }
    }

    private static GUIStyle s_EvenStyle;
    private GUIStyle EvenStyle
    {
        get
        {
            if (s_EvenStyle == null)
            {
                s_EvenStyle = new GUIStyle(GUIStyle.none);
                s_EvenStyle.normal.background = MakeColorTexture(new Color(0.5f, 0.5f, 0.5f, 0.0f));
            }
            return s_EvenStyle;
        }
    }

    private static GUIStyle s_DataFileStyle;
    private GUIStyle DataFileStyle
    {
        get
        {
            if (s_DataFileStyle == null)
            {
                s_DataFileStyle = new GUIStyle(EditorStyles.foldout);
                s_DataFileStyle.fontStyle = FontStyle.Bold;
            }
            return s_DataFileStyle;
        }
    }

    private static GUIStyle s_SizeStyle;
    private GUIStyle sizeStyle
    {
        get
        {
            if (s_SizeStyle == null)
                s_SizeStyle = new GUIStyle(GUI.skin.label);
            s_SizeStyle.alignment = TextAnchor.MiddleRight;
            return s_SizeStyle;
        }
    }

    private static GUIStyle s_BuildInfoStyle;
    private GUIStyle BuildInfoStyle
    {
        get
        {
            if (s_BuildInfoStyle == null)
                s_BuildInfoStyle = new GUIStyle(GUI.skin.label);

            s_BuildInfoStyle.alignment = TextAnchor.MiddleLeft;
            s_BuildInfoStyle.fontSize = 15;

            return s_BuildInfoStyle;
        }
    }

    #endregion TextStyle

    private Vector2 scrollPosition;
    private const int kLineHeight = 20;

    private const string lastBuildInfoFilePath = "Library/LastBuild.buildreport";
    private const string buildInfoFileAssetPath = "Assets/BuildReport";

    private BuildReport buildReportSerilizeFile = null;
    private List<AssetInfo> buildInfoList = new List<AssetInfo>();
    private BuildSummary buildInfoData;
    private DateTime lastBuildTimeData;

    private bool CheckLastBuildFile
    {
        get
        {
            return File.Exists(lastBuildInfoFilePath);
        }
    }
    //private bool dataSettingBool = false;
    //private string buildTarget = string.Empty;


    [MenuItem("Project Tools/BuildLogCheck", priority = -10000)]
    public static void OpenWindow()
    {
        GetWindow<BuildLogWindow>("Build Log Window");
    }

    private void OnGUI()
    {
        if (CheckLastBuildFile == false)
        {
            EditorGUILayout.HelpBox("Build Report Not Find", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();

        buildReportSerilizeFile = (BuildReport)EditorGUILayout.ObjectField(buildReportSerilizeFile, typeof(BuildReport), true);

        if (GUILayout.Button("Load Report") == true)
        {
            SettingBuildInfo();
            //dataSettingBool = true;
        }

        EditorGUILayout.EndHorizontal();      

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        //Build Info Setting
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Build Target     : ", BuildInfoStyle);

            GUILayout.Label(buildInfoData.platform.ToString(), BuildInfoStyle);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Build Total Size     : ", BuildInfoStyle);

            GUILayout.Label(FormatSize((int)buildInfoData.totalSize), BuildInfoStyle);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Build End Time     : ", BuildInfoStyle);

            GUILayout.Label(lastBuildTimeData.ToString(), BuildInfoStyle);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Build Success Bool   : ", BuildInfoStyle);

            GUILayout.Label(buildInfoData.result == BuildResult.Succeeded ? "True" : "False", BuildInfoStyle);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Build Out Path  : ", BuildInfoStyle);

            GUILayout.Label(buildInfoData.outputPath, BuildInfoStyle);

            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(20);

        float vPos = -scrollPosition.y;
        ShowAssets(ref vPos);

        EditorGUILayout.EndScrollView();
    }

    private void SettingBuildInfo()
    {
        if (buildReportSerilizeFile == null)
        {
            if (Directory.Exists(buildInfoFileAssetPath) == false)
                Directory.CreateDirectory(buildInfoFileAssetPath);

            //마지막 빌드가 이뤄진 시간 데이터 셋팅
            lastBuildTimeData = File.GetLastWriteTime(lastBuildInfoFilePath);

            var assetPath = buildInfoFileAssetPath + "/Build_" + lastBuildTimeData.ToString("yyyy-dd-MMM-HH-mm-ss") + ".buildreport";
            File.Copy(lastBuildInfoFilePath, assetPath, true);

            AssetDatabase.ImportAsset(assetPath);

            Selection.objects = new[] { AssetDatabase.LoadAssetAtPath<BuildReport>(assetPath) };

            //Load Build Report File 
            buildReportSerilizeFile = AssetDatabase.LoadAssetAtPath<BuildReport>(assetPath);
        } 

        //빌드 정보들을 셋팅하자 !!
        buildInfoData = buildReportSerilizeFile.summary;

        //빌드에 포함된 에셋들을 정리함 
        buildInfoList.Clear();

        SerializedObject copySerializedObject = new SerializedObject(buildReportSerilizeFile);

        Dictionary<string, int> outputFiles = new Dictionary<string, int>();
        Dictionary<string, int> assetTypes = new Dictionary<string, int>();

        foreach (var packedAsset in buildReportSerilizeFile.packedAssets)
        {
            outputFiles[packedAsset.shortPath] = 0;
            ulong totalSizeProp = packedAsset.overhead;
            outputFiles[packedAsset.shortPath] = (int)totalSizeProp;

            foreach (PackedAssetInfo entry in packedAsset.contents)
            {
                AssetImporter asset = AssetImporter.GetAtPath(entry.sourceAssetPath);
                string type = asset != null ? asset.GetType().Name : "Unknown";
                ulong sizeProp = entry.packedSize;

                if (type.EndsWith("Importer"))
                    type = type.Substring(0, type.Length - 8);
                
                if (!assetTypes.ContainsKey(type))
                    assetTypes[type] = 0;  

                assetTypes[type] += (int)sizeProp;
                outputFiles[packedAsset.shortPath] += (int)sizeProp;

                buildInfoList.Add(new AssetInfo
                {
                    size = (int)sizeProp,
                    icon = AssetDatabase.GetCachedIcon(entry.sourceAssetPath),
                    outputFile = packedAsset.shortPath,
                    type = type,
                    path = entry.sourceAssetPath
                });
            }
        }

        buildInfoList = buildInfoList.OrderBy(p => -p.size).ToList();
        outputFiles = outputFiles.OrderBy(p => -p.Value).ToDictionary(x => x.Key, x => x.Value);
        assetTypes = assetTypes.OrderBy(p => -p.Value).ToDictionary(x => x.Key, x => x.Value);
    }

    private void ShowAssets(ref float vPos)
    {
        GUILayout.BeginVertical();
        var odd = false;
        foreach (var entry in buildInfoList)
        {
            if (vPos >= -kLineHeight && vPos <= Screen.height)
            {
                GUILayout.BeginHorizontal(odd ? OddStyle : EvenStyle);

                GUILayout.Label(entry.icon, GUILayout.MaxHeight(16), GUILayout.Width(20));

                if (GUILayout.Button(new GUIContent(Path.GetFileName(entry.path), entry.path), GUI.skin.label, GUILayout.Width(300))) //GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth - 110)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.path));                

                GUILayout.Label(FormatSize(entry.size), sizeStyle);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Space(kLineHeight);
            }
            vPos += kLineHeight;
            odd = !odd;
        }
        GUILayout.EndVertical();
    }

    private Texture2D MakeColorTexture(Color col)
    {
        Color[] pix = new Color[1];
        pix[0] = col;

        Texture2D result = new Texture2D(1, 1);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }

    private string FormatSize(int size)
    {
        if (size < 1024)
            return size + " B";
        if (size < 1024 * 1024)
            return (size / 1024.00).ToString("F2") + " KB";
        if (size < 1024 * 1024 * 1024)
            return (size / (1024.0 * 1024.0)).ToString("F2") + " MB";
        return (size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
    }
}