using UnityEditor;

namespace Rts.Editor
{
    /// <summary>
    /// Import settings for the game's own soldier models (Resources/UnitModels, 11-18): a Humanoid avatar made from the
    /// model's own skeleton (Mixamo bone names map automatically), so the purchased pack's Humanoid clips play on it.
    /// No clips or materials come from the file; the pictures are applied at run time (CustomUnitArt).
    /// </summary>
    public sealed class UnitModelImport : AssetPostprocessor
    {
        private bool IsUnitAsset() { return assetPath.Contains("/Resources/UnitModels/"); }

        private void OnPreprocessTexture()
        {
            if (!IsUnitAsset()) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        private void OnPreprocessModel()
        {
            if (!IsUnitAsset()) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
        }
    }
}
