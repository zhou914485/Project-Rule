using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.RuleWheel
{
    public static class RuleWheelArtSetup
    {
        [MenuItem("RuleGame/Rule Wheel/Configure Paper Artwork")]
        public static void Configure()
        {
            const string folder = "Assets/_Project/UI/Art/RuleWheel";
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 512;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
        }
    }
}
