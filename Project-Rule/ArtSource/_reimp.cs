var guids = AssetDatabase.FindAssets("t:Texture2D", new string[] { "Assets/_Project/Art/Sprites/Hero" });
int n = 0;
AssetDatabase.StartAssetEditing();
try
{
    foreach (var g in guids)
    {
        var path = AssetDatabase.GUIDToAssetPath(g);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) continue;
        EditorUtility.SetDirty(imp);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        n++;
    }
}
finally { AssetDatabase.StopAssetEditing(); }
AssetDatabase.Refresh();
return "重导入 " + n + " 张";
