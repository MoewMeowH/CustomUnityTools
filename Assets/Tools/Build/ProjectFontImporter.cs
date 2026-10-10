using UnityEngine;
using UnityEditor;

public class ProjectFontImporter : Editor
{
    private static string fontDirectoryPath = "Assets/TextMesh Pro/Fonts";

    public static void FontImportSetting(bool includeActive)
    {
        string[] fontGUIDs = AssetDatabase.FindAssets("t:font", new[] { fontDirectoryPath });

        string fontPath = string.Empty;
        for (int i = 0; i < fontGUIDs.Length; i++)
        {
            fontPath = AssetDatabase.GUIDToAssetPath(fontGUIDs[i]);

            TrueTypeFontImporter fontImporterSettingAsset = AssetImporter.GetAtPath(fontPath) as TrueTypeFontImporter;

            if (fontImporterSettingAsset == null)
            {
                Debug.LogError($"[ProjectFontImporter] Font ImporterData Load Error :: {fontPath}");
                continue;
            }

            fontImporterSettingAsset.includeFontData = includeActive;
            fontImporterSettingAsset.SaveAndReimport();
        }
    }
}
