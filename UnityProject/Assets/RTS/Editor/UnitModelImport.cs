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
        private bool IsUnitAsset() { return assetPath.Contains("/Resources/UnitModels/") || IsAnimatedAsset() || IsTierAsset(); }

        /// <summary>Medium/High/Ultra models and their 2048 pictures (graphics setting, 11-18): &lt;name&gt;_M/_H/_U.</summary>
        private bool IsTierAsset() { return assetPath.Contains("/Resources/UnitModelsHD/"); }

        /// <summary>Riders and machines carry their own clips; in the tier folder they are told apart by name.</summary>
        private bool IsAnimatedAsset()
        {
            if (assetPath.Contains("/Resources/UnitModelsAnimated/")) return true;
            if (!IsTierAsset()) return false;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            foreach (string name in AnimatedNames)
                if (file.StartsWith(name + "_")) return true;
            return false;
        }

        private static readonly string[] AnimatedNames = { "Cavalry", "LightCavalry", "HeavyCavalry", "Cannon", "Ram" };

        private void OnPreprocessTexture()
        {
            if (!IsUnitAsset()) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png") || assetPath.EndsWith("_Normal2k.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = IsTierAsset() ? 2048 : 1024;
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
