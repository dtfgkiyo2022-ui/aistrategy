using Rts.Contracts;
using System;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Runtime references to the optional local visual pack.
    ///
    /// This asset is generated on a machine that owns the pack. It contains references, not copies
    /// of the purchased files, so it must not be checked into the repository.
    /// </summary>
    public sealed class LocalVisualPackManifest : ScriptableObject
    {
        public const string CoreKey = "Core";
        public const string OutpostKey = "Outpost";
        public const string UnitBlueMaterialKey = "UnitBlue";
        public const string UnitRedMaterialKey = "UnitRed";
        public const string BuildingBlueMaterialKey = "BuildingBlue";
        public const string BuildingRedMaterialKey = "BuildingRed";
        public const string BuildingWhiteMaterialKey = "BuildingWhite";

        [Serializable]
        public sealed class ModelEntry
        {
            public string key;
            public GameObject model;
        }

        [Serializable]
        public sealed class AnimationEntry
        {
            public string key;
            public AnimationClip clip;
        }

        [Serializable]
        public sealed class MaterialEntry
        {
            public string key;
            public Material material;
        }

        [SerializeField] private ModelEntry[] models = new ModelEntry[0];
        [SerializeField] private AnimationEntry[] animations = new AnimationEntry[0];
        [SerializeField] private MaterialEntry[] materials = new MaterialEntry[0];

        public ModelEntry[] Models
        {
            get { return models; }
            set { models = value ?? new ModelEntry[0]; }
        }

        public AnimationEntry[] Animations
        {
            get { return animations; }
            set { animations = value ?? new AnimationEntry[0]; }
        }

        public MaterialEntry[] Materials
        {
            get { return materials; }
            set { materials = value ?? new MaterialEntry[0]; }
        }

        public GameObject GetUnit(UnitKind kind)
        {
            return GetModel(kind.ToString());
        }

        public GameObject GetBuilding(BuildingKind kind)
        {
            return GetModel(kind.ToString());
        }

        public GameObject GetCore()
        {
            return GetModel(CoreKey);
        }

        public GameObject GetOutpost()
        {
            return GetModel(OutpostKey);
        }

        public AnimationClip GetAnimation(UnitKind kind, LocalVisualPack.UnitMotion motion)
        {
            if (animations == null) return null;
            string wanted = AnimationKey(kind, motion);
            for (int i = 0; i < animations.Length; i++)
                if (animations[i] != null && animations[i].key == wanted) return animations[i].clip;
            return null;
        }

        public Material GetMaterial(string key)
        {
            if (key == null) return null;
            if (materials == null) return null;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].key == key) return materials[i].material;
            return null;
        }

        public static string AnimationKey(UnitKind kind, LocalVisualPack.UnitMotion motion)
        {
            return kind + ":" + motion;
        }

        /// <summary>Key of a model listed by its pack file (the staged looks and the banners), not by unit kind.</summary>
        public static string FileKey(string packPath)
        {
            return "file:" + packPath;
        }

        public GameObject GetModelByFile(string packPath)
        {
            return packPath == null ? null : GetModel(FileKey(packPath));
        }

        public AnimationClip GetAnimationByFile(string packPath)
        {
            if (animations == null || packPath == null) return null;
            string wanted = FileKey(packPath);
            for (int i = 0; i < animations.Length; i++)
                if (animations[i] != null && animations[i].key == wanted) return animations[i].clip;
            return null;
        }

        private GameObject GetModel(string key)
        {
            if (key == null) return null;
            if (models == null) return null;
            for (int i = 0; i < models.Length; i++)
                if (models[i] != null && models[i].key == key) return models[i].model;
            return null;
        }
    }
}
