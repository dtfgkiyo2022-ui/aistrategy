using UnityEditor;

namespace Rts.Editor
{
    /// <summary>
    /// Import settings for the neutral beasts' models (Resources/NeutralModels, 11-18), so they never depend on hand-set
    /// .meta values: a generic rig with its clips, idle and walk looping, the attack not; normal maps are normal maps.
    /// The material comes from the _BaseColor/_Normal pictures at run time (NeutralModelArt), not from the file.
    /// </summary>
    public sealed class NeutralModelImport : AssetPostprocessor
    {
        private bool IsNeutralAsset() { return assetPath.Contains("/Resources/NeutralModels/"); }

        private void OnPreprocessTexture()
        {
            if (!IsNeutralAsset()) return;
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
            if (!IsNeutralAsset()) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsNeutralAsset()) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
                clips[i].loopTime = !clips[i].name.ToLowerInvariant().Contains("attack");
            importer.clipAnimations = clips;
        }
    }
}
