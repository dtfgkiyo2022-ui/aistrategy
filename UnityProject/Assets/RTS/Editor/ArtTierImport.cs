using UnityEditor;

namespace Rts.Editor
{
    /// <summary>
    /// Import settings for the Medium/High/Ultra model files (docs/settings.md 2.1), so they never depend on hand-set
    /// .meta values: textures keep their full size (2048, or 4096 for the Ultra colour), normal maps are normal maps.
    /// The Low files in BuildingModels/ and ResourceModels/ are not touched.
    /// </summary>
    public sealed class ArtTierImport : AssetPostprocessor
    {
        private static bool IsTierAsset(string path)
        {
            return path.Contains("/Resources/BuildingModelsHD/") || path.Contains("/Resources/ResourceModelsHD/");
        }

        private void OnPreprocessTexture()
        {
            if (!IsTierAsset(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal2k.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = assetPath.EndsWith("_BaseColor4k.jpg") ? 4096 : 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
        }

        private void OnPreprocessModel()
        {
            if (!IsTierAsset(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
        }
    }
}
