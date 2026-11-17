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
        private bool IsUnitAsset() { return assetPath.Contains("/Resources/UnitModels/") || IsAnimatedAsset(); }
        private bool IsAnimatedAsset() { return assetPath.Contains("/Resources/UnitModelsAnimated/"); }

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
            bool animated = IsAnimatedAsset();
            importer.animationType = animated ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = animated;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsAnimatedAsset()) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
                clips[i].loopTime = !clips[i].name.ToLowerInvariant().Contains("attack");
            importer.clipAnimations = clips;
        }
    }
}
