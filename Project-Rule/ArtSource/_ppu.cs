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
        imp.spritePixelsPerUnit = 140f;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.maxTextureSize = 1024;
        EditorUtility.SetDirty(imp);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        n++;
    }
}
finally { AssetDatabase.StopAssetEditing(); }
AssetDatabase.Refresh();
return "设了 " + n + " 张 PPU=140 / 不压缩";
