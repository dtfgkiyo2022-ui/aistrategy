using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rts.Presentation
{
    /// <summary>
    /// Optional visuals from a purchased asset pack (Toony Tiny RTS Set). The pack lives under Assets/ThirdParty/, which
    /// is git-ignored (an Extension Asset needs one license per person holding the files). The editor reads the pack by
    /// path; a build reads the generated LocalVisualPackManifest. Both paths work without the pack: every method reports
    /// false when the relevant asset is missing, and the caller falls back to the placeholder models.
    /// </summary>
    public static class LocalVisualPack
    {
        public const string ManifestAssetPath = "Assets/RTS/Generated/Resources/LocalVisualPackManifest.asset";
        public const string ManifestResourceName = "LocalVisualPackManifest";
        private const string Root = "Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/";
        private const string Units = Root + "prefabs/";
        private const string Banners = Units + "banners/";
        private const string Buildings = Root + "models/buildings/";
        private const string AnimationRoot = Root + "animation/";
        private const string UnitMaterialRoot = Root + "models/materials/color/Units/TT_RTS_Units_";
        private const string BuildingMaterialRoot = Root + "models/materials/color/Buildings/TT_RTS_buildings_";
        private const float CoreWidth = 9f, OutpostWidth = 6f;
        private const string OutpostModel = "Tower_A.FBX";
        private static readonly string[] BannerFiles =
        {
            "TT_Banner_Black.prefab", "TT_Banner_Blue_A.prefab", "TT_Banner_Blue_B.prefab",
            "TT_Banner_Brown.prefab", "TT_Banner_Green_A.prefab", "TT_Banner_Green_B.prefab",
            "TT_Banner_Orange.prefab", "TT_Banner_Pink.prefab", "TT_Banner_Purple.prefab",
            "TT_Banner_Red.prefab", "TT_Banner_White.prefab", "TT_Banner_Yellow.prefab"
        };

        private sealed class UnitSpec
        {
            public readonly string File;
            public readonly float Height;
            public UnitSpec(string file, float height) { File = file; Height = height; }
        }

        private sealed class TieredUnitSpec
        {
            public readonly string File;
            public readonly float Height;
            public readonly AnimationSpec Animation;

            public TieredUnitSpec(string file, float height, AnimationSpec animation)
            {
                File = file;
                Height = height;
                Animation = animation;
            }
        }

        private sealed class BuildingSpec
        {
            public readonly string File;
            /// <summary>Finished height in metres; the placeholder box heights in EconomyLayer are the reference.</summary>
            public readonly float Height;
            /// <summary>Footprint relative to the building's simulated size, for a model reused at a different size.</summary>
            public readonly float WidthScale;
            /// <summary>Display-only tint, multiplied with the owner's material to distinguish reused models.</summary>
            public readonly Color Tint;
            public BuildingSpec(string file, float height, float widthScale = 1f, Color? tint = null)
            {
                File = file;
                Height = height;
                WidthScale = widthScale;
                Tint = tint ?? Color.white;
            }
        }

        public enum UnitMotion
        {
            Idle,
            Walk,
            Attack,
            Work,
            Death
        }

        private sealed class AnimationSpec
        {
            public readonly string Idle;
            public readonly string Walk;
            public readonly string Attack;
            public readonly string Work;
            public readonly string Death;

            public AnimationSpec(string idle, string walk, string attack)
            {
                Idle = AnimationRoot + idle;
                Walk = AnimationRoot + walk;
                Attack = AnimationRoot + attack;
                // The pack keeps the same numbered motion names in each family. Ram has no separate
                // work swing, so its normal attack remains the harmless fallback for that unused case.
                Work = AnimationRoot + attack.Replace("_attack_A.", "_attack_B.");
                Death = AnimationRoot + idle.Replace("_01_idle.", "_06_death_A.");
            }

            public string Path(UnitMotion motion)
            {
                switch (motion)
                {
                    case UnitMotion.Walk: return Walk;
                    case UnitMotion.Attack: return Attack;
                    case UnitMotion.Work: return Work;
                    case UnitMotion.Death: return Death;
                    default: return Idle;
                }
            }
        }

        private static AnimationSpec InfantryAnimation(string family, string name)
        {
            string lower = name.ToLowerInvariant();
            return new AnimationSpec(
                "animation_infantry/" + family + "/" + lower + "_01_idle.FBX",
                "animation_infantry/" + family + "/" + lower + "_02_walk.FBX",
                "animation_infantry/" + family + "/" + lower + "_04_attack_A.FBX");
        }

        private static TieredUnitSpec Infantry(string file, string family, string animationName, float height)
        {
            return new TieredUnitSpec(file, height, InfantryAnimation(family, animationName));
        }

        // Each civilisation keeps one weapon family across its three equipment stages; the comments explain the visual choice.
        private static readonly Dictionary<CivKind, TieredUnitSpec[]> InfantryTable = new Dictionary<CivKind, TieredUnitSpec[]>
        {
            // Primitive stays as the light infantry silhouette because it has no specialised military tradition.
            { CivKind.Primitive, new[] { Infantry("TT_Light_Infantry.prefab", "Infantry", "infantry", 2.4f), Infantry("TT_Light_Infantry.prefab", "Infantry", "infantry", 2.4f), Infantry("TT_Light_Infantry.prefab", "Infantry", "infantry", 2.4f) } },
            // Agrarian uses spear and polearm forms that read as tools turned into field weapons.
            { CivKind.Agrarian, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Metallurgy advances from a shielded sword line into the commander's heavier two-handed armour.
            { CivKind.Metallurgy, new[] { Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Forestry favours long reach, so its late stages move from spears to halberds.
            { CivKind.Forestry, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f) } },
            // Masonry is defensive and disciplined, making the broad halberdier silhouette its signature.
            { CivKind.Masonry, new[] { Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Caravan guards mix a practical sword with a polearm escort and a veteran commander.
            { CivKind.Caravan, new[] { Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Cavalry civilisation keeps a compact sword guard before adopting paladin-grade protection.
            { CivKind.Cavalry, new[] { Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } },
            // Bridge relies on polearms to protect construction crews and crossings.
            { CivKind.Bridge, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Academy trains a shielded officer corps, ending in the heavily armed paladin.
            { CivKind.Academy, new[] { Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } },
            // Cult favours spear-bearing devotees before its chosen champions take commander's arms.
            { CivKind.Cult, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } },
            // Fishing starts with spears and adopts polearms as coastal defence becomes organised.
            { CivKind.Fishing, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f) } },
            // Mountain uses the most recognisable reach weapon, then hardens it with commander and paladin armour.
            { CivKind.Mountain, new[] { Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } },
            // Tollgate's guards are halberdiers at every gate, with a commander at the final stage.
            { CivKind.Tollgate, new[] { Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Halberdier.prefab", "Polearm", "polearm", 2.5f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f) } },
            // Metropolis fields professional sword guards and promotes them to elite paladins.
            { CivKind.Metropolis, new[] { Infantry("TT_Swordman.prefab", "Shield", "shield", 2.4f), Infantry("TT_Commander.prefab", "TwoHanded", "twohanded", 2.6f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } },
            // Sanctuary follows the spear-bearing pilgrim with a visibly sacred paladin silhouette.
            { CivKind.Sanctuary, new[] { Infantry("TT_Spearman.prefab", "Spear", "spear", 2.4f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f), Infantry("TT_Paladin.prefab", "Shield", "shield", 2.7f) } }
        };

        private static readonly Dictionary<UnitKind, TieredUnitSpec[]> TieredUnitTable = new Dictionary<UnitKind, TieredUnitSpec[]>
        {
            { UnitKind.Cavalry, new[]
                {
                    new TieredUnitSpec("TT_Heavy_Cavalry.prefab", 3.0f, new AnimationSpec("animation_cavalry/cavalry/cavalry_01_idle.FBX", "animation_cavalry/cavalry/cavalry_02_walk.FBX", "animation_cavalry/cavalry/cavalry_04_attack.FBX")),
                    new TieredUnitSpec("TT_Mounted_Knight.prefab", 3.0f, new AnimationSpec("animation_cavalry/cavalry/cavalry_01_idle.FBX", "animation_cavalry/cavalry/cavalry_02_walk.FBX", "animation_cavalry/cavalry/cavalry_04_attack.FBX")),
                    new TieredUnitSpec("TT_Mounted_Paladin.prefab", 3.0f, new AnimationSpec("animation_cavalry/cavalry/cavalry_01_idle.FBX", "animation_cavalry/cavalry/cavalry_02_walk.FBX", "animation_cavalry/cavalry/cavalry_04_attack.FBX"))
                }
            },
            { UnitKind.Ram, new[]
                {
                    new TieredUnitSpec("machines/TT_Ram_lvl1.prefab", 3.4f, new AnimationSpec("animation_machines/Ram/ram_01_idle.FBX", "animation_machines/Ram/ram_02_move.FBX", "animation_machines/Ram/ram_03_attack.FBX")),
                    new TieredUnitSpec("machines/TT_Ram_lvl2.prefab", 3.4f, new AnimationSpec("animation_machines/Ram/ram_01_idle.FBX", "animation_machines/Ram/ram_02_move.FBX", "animation_machines/Ram/ram_03_attack.FBX")),
                    new TieredUnitSpec("machines/TT_Ram_lvl3.prefab", 3.4f, new AnimationSpec("animation_machines/Ram/ram_01_idle.FBX", "animation_machines/Ram/ram_02_move.FBX", "animation_machines/Ram/ram_03_attack.FBX"))
                }
            },
            { UnitKind.Monk, new[]
                {
                    new TieredUnitSpec("TT_Priest.prefab", 2.4f, new AnimationSpec("animation_infantry/Staff/staff_01_idle.FBX", "animation_infantry/Staff/staff_02_walk.FBX", "animation_infantry/Staff/staff_04_attack_A.FBX")),
                    new TieredUnitSpec("TT_HighPriest.prefab", 2.4f, new AnimationSpec("animation_infantry/Staff/staff_01_idle.FBX", "animation_infantry/Staff/staff_02_walk.FBX", "animation_infantry/Staff/staff_04_attack_A.FBX")),
                    new TieredUnitSpec("TT_HighPriest.prefab", 2.4f, new AnimationSpec("animation_infantry/Staff/staff_01_idle.FBX", "animation_infantry/Staff/staff_02_walk.FBX", "animation_infantry/Staff/staff_04_attack_A.FBX"))
                }
            }
        };

        // Unit kind -> pack prefab. Every kind has its own model, so the placeholder class marks are not needed with the pack.
        // Cavalry and light cavalry, and archers and skirmish archers, use different models so the pairs stay apart.
        private static readonly Dictionary<UnitKind, UnitSpec> UnitTable = new Dictionary<UnitKind, UnitSpec>
        {
            { UnitKind.Infantry, new UnitSpec("TT_Light_Infantry.prefab", 2.4f) },
            { UnitKind.Scout, new UnitSpec("TT_Scout.prefab", 2.4f) },
            { UnitKind.Villager, new UnitSpec("TT_Peasant.prefab", 2.2f) },
            { UnitKind.Archer, new UnitSpec("TT_Archer.prefab", 2.4f) },
            { UnitKind.Cavalry, new UnitSpec("TT_Heavy_Cavalry.prefab", 3.0f) },
            { UnitKind.LightCavalry, new UnitSpec("TT_Light_Cavalry.prefab", 2.8f) },
            { UnitKind.Ram, new UnitSpec("machines/TT_Ram_lvl1.prefab", 3.4f) },
            { UnitKind.Mercenary, new UnitSpec("TT_HeavySwordman.prefab", 2.6f) },
            { UnitKind.Monk, new UnitSpec("TT_Priest.prefab", 2.4f) },
            { UnitKind.HeavyInfantry, new UnitSpec("TT_Heavy_Infantry.prefab", 2.6f) },
            { UnitKind.SkirmishArcher, new UnitSpec("TT_Crossbowman.prefab", 2.4f) }
        };

        // Unit model -> animation family.
        // Infantry is used by scouts and villagers because their pack models share the same humanoid rig and proportions.
        // The weapon-specific families keep attack motions visually believable.
        private static readonly Dictionary<UnitKind, AnimationSpec> AnimationTable = new Dictionary<UnitKind, AnimationSpec>
        {
            { UnitKind.Infantry, new AnimationSpec("animation_infantry/Infantry/infantry_01_idle.FBX", "animation_infantry/Infantry/infantry_02_walk.FBX", "animation_infantry/Infantry/infantry_04_attack_A.FBX") },
            { UnitKind.Scout, new AnimationSpec("animation_infantry/Infantry/infantry_01_idle.FBX", "animation_infantry/Infantry/infantry_02_walk.FBX", "animation_infantry/Infantry/infantry_04_attack_A.FBX") },
            { UnitKind.Villager, new AnimationSpec("animation_infantry/Infantry/infantry_01_idle.FBX", "animation_infantry/Infantry/infantry_02_walk.FBX", "animation_infantry/Infantry/infantry_04_attack_A.FBX") },
            { UnitKind.Archer, new AnimationSpec("animation_infantry/Archer/archer_01_idle.FBX", "animation_infantry/Archer/archer_02_walk.FBX", "animation_infantry/Archer/archer_04_attack_A.FBX") },
            { UnitKind.SkirmishArcher, new AnimationSpec("animation_infantry/Crossbow/crossbow_01_idle.FBX", "animation_infantry/Crossbow/crossbow_02_walk.FBX", "animation_infantry/Crossbow/crossbow_04_attack_A.FBX") },
            { UnitKind.Monk, new AnimationSpec("animation_infantry/Staff/staff_01_idle.FBX", "animation_infantry/Staff/staff_02_walk.FBX", "animation_infantry/Staff/staff_04_attack_A.FBX") },
            { UnitKind.Mercenary, new AnimationSpec("animation_infantry/TwoHanded/twohanded_01_idle.FBX", "animation_infantry/TwoHanded/twohanded_02_walk.FBX", "animation_infantry/TwoHanded/twohanded_04_attack_A.FBX") },
            { UnitKind.HeavyInfantry, new AnimationSpec("animation_infantry/Shield/shield_01_idle.FBX", "animation_infantry/Shield/shield_02_walk.FBX", "animation_infantry/Shield/shield_04_attack_A.FBX") },
            { UnitKind.Cavalry, new AnimationSpec("animation_cavalry/cavalry/cavalry_01_idle.FBX", "animation_cavalry/cavalry/cavalry_02_walk.FBX", "animation_cavalry/cavalry/cavalry_04_attack.FBX") },
            { UnitKind.LightCavalry, new AnimationSpec("animation_cavalry/cavalry_spear_A/cav_spear_A_01_idle.FBX", "animation_cavalry/cavalry_spear_A/cav_spear_A_02_walk.FBX", "animation_cavalry/cavalry_spear_A/cav_spear_A_04_attack.FBX") },
            { UnitKind.Ram, new AnimationSpec("animation_machines/Ram/ram_01_idle.FBX", "animation_machines/Ram/ram_02_move.FBX", "animation_machines/Ram/ram_03_attack.FBX") }
        };

        private static readonly Dictionary<string, AnimationClip> animationClips = new Dictionary<string, AnimationClip>();
        private static readonly Dictionary<string, GameObject> modelAssets = new Dictionary<string, GameObject>();

        // Building kind -> pack model. The core uses Castle and outposts Tower_A, so the castle building takes Keep and
        // towers take Tower_B to stay distinguishable. Blacksmith stands in for the smelter, kiln and steelworks (told apart
        // Visual stand-ins: Granary reads as Mine/Quarry by its grey-blue/stone tint, Keep as MineShaft,
        // Market as the larger Caravanserai, Workshop as the olive EngineerCamp, and BeastLair as Harbor.
        // Wall_A_1x1 gives each Wall cell a separate wall piece; Wall_A_gate reads as Tollgate.
        // Bridge intentionally remains the placeholder: its terrain-spanning shape needs a dedicated model.
        private static readonly Dictionary<BuildingKind, BuildingSpec> BuildingTable = new Dictionary<BuildingKind, BuildingSpec>
        {
            { BuildingKind.Barracks, new BuildingSpec("Barracks.FBX", 3.0f) },
            { BuildingKind.Farm, new BuildingSpec("Farm.FBX", 1.6f) },
            { BuildingKind.House, new BuildingSpec("House.FBX", 1.4f) },
            { BuildingKind.DropSite, new BuildingSpec("Granary.FBX", 2.2f) }, // 1.0 squashed the tall granary flat (10-10)
            { BuildingKind.Tower, new BuildingSpec("Tower_B.FBX", 4.5f) },
            { BuildingKind.Blacksmith, new BuildingSpec("Blacksmith.FBX", 2.0f) },
            { BuildingKind.Market, new BuildingSpec("Market.FBX", 1.8f) },
            { BuildingKind.SiegeWorkshop, new BuildingSpec("Workshop.FBX", 2.6f) },
            { BuildingKind.ArcheryRange, new BuildingSpec("Archery.FBX", 2.2f) },
            { BuildingKind.Stable, new BuildingSpec("Stables.FBX", 2.4f) },
            { BuildingKind.Castle, new BuildingSpec("Keep.FBX", 6.0f) },
            { BuildingKind.Smelter, new BuildingSpec("Blacksmith.FBX", 2.4f) },
            { BuildingKind.CharcoalKiln, new BuildingSpec("Blacksmith.FBX", 3.0f) },
            { BuildingKind.Steelworks, new BuildingSpec("Blacksmith.FBX", 3.0f) },
            { BuildingKind.LumberCamp, new BuildingSpec("LumberMill.FBX", 3.0f) },
            { BuildingKind.Fletcher, new BuildingSpec("Archery.FBX", 2.2f, 0.7f) },
            { BuildingKind.Academy, new BuildingSpec("Library.FBX", 3.0f) },
            { BuildingKind.Monastery, new BuildingSpec("Temple.FBX", 3.0f) },
            { BuildingKind.Shrine, new BuildingSpec("MageTower.FBX", 3.0f) },
            { BuildingKind.Town, new BuildingSpec("TownHall.FBX", 3.0f) },
            { BuildingKind.GrandHouse, new BuildingSpec("House.FBX", 3.6f, 1.2f) },
            { BuildingKind.Wall, new BuildingSpec("Wall_A_1x1.FBX", 1.8f) },
            { BuildingKind.Tollgate, new BuildingSpec("Wall_A_gate.FBX", 2.6f) },
            { BuildingKind.Mine, new BuildingSpec("Granary.FBX", 2.0f, 1f, new Color(0.62f, 0.70f, 0.78f, 1f)) },
            { BuildingKind.MineShaft, new BuildingSpec("Keep.FBX", 3.8f) },
            { BuildingKind.Quarry, new BuildingSpec("Granary.FBX", 2.0f, 1f, new Color(0.76f, 0.72f, 0.64f, 1f)) },
            { BuildingKind.Caravanserai, new BuildingSpec("Market.FBX", 2.0f, 1.15f) },
            { BuildingKind.Harbor, new BuildingSpec("BeastLair.FBX", 2.8f) },
            { BuildingKind.EngineerCamp, new BuildingSpec("Workshop.FBX", 2.6f, 1f, new Color(0.62f, 0.72f, 0.50f, 1f)) },
            // V3-20 storage: a granary in wood brown, told apart from the mine and quarry granaries by colour.
            { BuildingKind.Storage, new BuildingSpec("Granary.FBX", 2.2f, 1f, new Color(0.78f, 0.62f, 0.44f, 1f)) }
        };

        /// <summary>Hides the pack so the placeholders are used; for measuring one against the other.</summary>
        public static bool Disabled;

        public static string UnitAssetPath(UnitKind kind) { return UnitTable.TryGetValue(kind, out var s) ? Units + s.File : null; }

        public static string BuildingAssetPath(BuildingKind kind) { return BuildingTable.TryGetValue(kind, out var s) ? Buildings + s.File : null; }

        public static float UnitHeight(UnitKind kind) { return UnitTable.TryGetValue(kind, out var s) ? s.Height : 2.4f; }

        public static string AnimationAssetPath(UnitKind kind, UnitMotion motion)
        {
            return AnimationTable.TryGetValue(kind, out var spec) ? spec.Path(motion) : null;
        }

        public static string InfantryAssetPath(CivKind civ, int stage)
        {
            var spec = GetInfantrySpec(civ, stage);
            return spec == null ? null : Units + spec.File;
        }

        public static string InfantryAnimationAssetPath(CivKind civ, int stage, UnitMotion motion)
        {
            var spec = GetInfantrySpec(civ, stage);
            return spec == null ? null : spec.Animation.Path(motion);
        }

        public static string TieredUnitAssetPath(UnitKind kind, int stage)
        {
            var spec = GetTieredUnitSpec(kind, stage);
            return spec == null ? null : Units + spec.File;
        }

        public static string TieredUnitAnimationAssetPath(UnitKind kind, int stage, UnitMotion motion)
        {
            var spec = GetTieredUnitSpec(kind, stage);
            return spec == null ? null : spec.Animation.Path(motion);
        }

        public static string BannerAssetPath(int index)
        {
            return index < 0 || index >= BannerFiles.Length ? null : Banners + BannerFiles[index];
        }

        public static int EquipmentTechCount(ulong techs)
        {
            int count = 0;
            if ((techs & (1UL << ((int)TechKind.Weapons - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.Armour - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.SteelWeapons - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.SteelArmour - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.GemArmor - 1))) != 0) count++;
            return count;
        }

        public static int EquipmentStage(ulong techs)
        {
            int count = EquipmentTechCount(techs);
            return count == 0 ? 1 : count == 1 ? 2 : 3;
        }

        public static int EquipmentStage(EconomyView economy)
        {
            return economy == null ? 1 : EquipmentStage(economy.Techs);
        }

        public static CivKind UnitVisualCiv(FactionFrame frame, RenderUnit unit)
        {
            return unit.Civ;
        }

        public static int UnitVisualStage(FactionFrame frame, RenderUnit unit)
        {
            return unit.EquipmentLevel == 0 ? 1 : unit.EquipmentLevel == 1 ? 2 : 3;
        }

        /// <summary>Every pack file the staged looks and banners use: models first, then clips. For the build manifest.</summary>
        public static void StagedAssetPaths(List<string> models, List<string> clips)
        {
            foreach (var row in InfantryTable.Values) AddStaged(row, models, clips);
            foreach (var row in TieredUnitTable.Values) AddStaged(row, models, clips);
            for (int i = 0; i < BannerFiles.Length; i++) models.Add(BannerAssetPath(i));
        }

        private static void AddStaged(TieredUnitSpec[] row, List<string> models, List<string> clips)
        {
            foreach (var spec in row)
            {
                if (!models.Contains(Units + spec.File)) models.Add(Units + spec.File);
                foreach (UnitMotion motion in System.Enum.GetValues(typeof(UnitMotion)))
                {
                    string path = spec.Animation.Path(motion);
                    if (path != null && !clips.Contains(path)) clips.Add(path);
                }
            }
        }

        public static bool IsTieredUnit(UnitKind kind)
        {
            return kind == UnitKind.Infantry || TieredUnitTable.ContainsKey(kind);
        }

        private static TieredUnitSpec GetInfantrySpec(CivKind civ, int stage)
        {
            if (stage < 1 || stage > 3) return null;
            return InfantryTable.TryGetValue(civ, out var row) ? row[stage - 1] : null;
        }

        private static TieredUnitSpec GetTieredUnitSpec(UnitKind kind, int stage)
        {
            if (stage < 1 || stage > 3) return null;
            return TieredUnitTable.TryGetValue(kind, out var row) ? row[stage - 1] : null;
        }

        public static string CoreAssetPath() { return Buildings + "Castle.FBX"; }

        public static string OutpostAssetPath() { return Buildings + OutpostModel; }

        public static string UnitMaterialAssetPath(bool own)
        {
            return UnitMaterialRoot + (own ? "blue" : "red") + ".mat";
        }

        public static string BuildingMaterialAssetPath(uint owner)
        {
            return BuildingMaterialRoot + (owner == 1 ? "blue" : owner == 2 ? "red" : "white") + ".mat";
        }

        public static string AnimationKey(UnitKind kind, UnitMotion motion)
        {
            return LocalVisualPackManifest.AnimationKey(kind, motion);
        }

        public sealed class AnimationHandle
        {
            private const float BlendSeconds = 0.15f;
            private readonly PlayableGraph graph;
            private readonly AnimationMixerPlayable mixer;
            private readonly AnimationClipPlayable idle;
            private readonly AnimationClipPlayable walk;
            // The attack clips are imported without looping; it is wrapped by hand so a long fight keeps swinging.
            private readonly AnimationClipPlayable attack;
            private readonly AnimationClipPlayable work;
            private readonly AnimationClipPlayable death;
            private readonly double attackLength;
            private readonly double workLength;
            private readonly bool hasWork;
            private readonly bool hasDeath;
            private int desired;
            private bool disposed;

            internal AnimationHandle(PlayableGraph graph, AnimationMixerPlayable mixer, AnimationClipPlayable idle,
                AnimationClipPlayable walk, AnimationClipPlayable attack, AnimationClipPlayable work,
                AnimationClipPlayable death, double attackLength, double workLength, bool hasWork, bool hasDeath)
            {
                this.graph = graph;
                this.mixer = mixer;
                this.idle = idle;
                this.walk = walk;
                this.attack = attack;
                this.work = work;
                this.death = death;
                this.attackLength = attackLength;
                this.workLength = workLength;
                this.hasWork = hasWork;
                this.hasDeath = hasDeath;
            }

            public void SetDesired(bool moving, bool attacking, bool working, bool dying)
            {
                desired = dying && hasDeath ? 4 : working && hasWork ? 3 : attacking ? 2 : moving ? 1 : 0;
            }

            /// <summary>Scales clip playback without changing simulation time or state.</summary>
            public void SetPlaybackRate(float walkRate, float matchRate)
            {
                if (disposed) return;
                idle.SetSpeed(matchRate);
                walk.SetSpeed(walkRate * matchRate);
                attack.SetSpeed(matchRate);
                if (hasWork) work.SetSpeed(matchRate);
                if (hasDeath) death.SetSpeed(matchRate);
            }

            public void RestartAttack()
            {
                if (!disposed) attack.SetTime(0d);
            }

            public void RestartWork()
            {
                if (!disposed && hasWork) work.SetTime(0d);
            }

            public void StartDeath()
            {
                if (!disposed && hasDeath) death.SetTime(0d);
            }

            public void Tick(float deltaTime, float matchRate = 1f)
            {
                if (disposed) return;
                if (matchRate <= 0f) return;
                if (attackLength > 0.0001 && attack.GetTime() > attackLength) attack.SetTime(attack.GetTime() % attackLength);
                if (hasWork && workLength > 0.0001 && work.GetTime() > workLength) work.SetTime(work.GetTime() % workLength);
                float step = BlendSeconds <= 0f ? 1f : deltaTime * matchRate / BlendSeconds;
                for (int i = 0; i < 5; i++)
                {
                    float target = i == desired ? 1f : 0f;
                    mixer.SetInputWeight(i, Mathf.MoveTowards(mixer.GetInputWeight(i), target, step));
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (graph.IsValid()) graph.Destroy();
            }
        }

        public static AnimationHandle TryCreateAnimation(GameObject instance, UnitKind kind, ulong id)
        {
            return TryCreateAnimation(instance, AnimationTable.TryGetValue(kind, out var spec) ? spec : null, kind, id);
        }

        private static AnimationHandle TryCreateAnimation(GameObject instance, AnimationSpec spec, UnitKind? manifestKind, ulong id)
        {
            if (Disabled || instance == null || spec == null) return null;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) return null;
            var idle = manifestKind.HasValue ? LoadAnimationClip(manifestKind.Value, UnitMotion.Idle) : LoadAnimationClip(spec, UnitMotion.Idle);
            var walk = manifestKind.HasValue ? LoadAnimationClip(manifestKind.Value, UnitMotion.Walk) : LoadAnimationClip(spec, UnitMotion.Walk);
            var attack = manifestKind.HasValue ? LoadAnimationClip(manifestKind.Value, UnitMotion.Attack) : LoadAnimationClip(spec, UnitMotion.Attack);
            if (idle == null || walk == null || attack == null) return null;
            var work = manifestKind.HasValue ? LoadAnimationClip(manifestKind.Value, UnitMotion.Work) : LoadAnimationClip(spec, UnitMotion.Work);
            var death = manifestKind.HasValue ? LoadAnimationClip(manifestKind.Value, UnitMotion.Death) : LoadAnimationClip(spec, UnitMotion.Death);
            // Off-screen soldiers are not animated at all; a crowd of 200 only pays for the ones in view.
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            var graph = PlayableGraph.Create("RTS Unit Animation");
            var mixer = AnimationMixerPlayable.Create(graph, 5);
            var idlePlayable = AnimationClipPlayable.Create(graph, idle);
            var walkPlayable = AnimationClipPlayable.Create(graph, walk);
            var attackPlayable = AnimationClipPlayable.Create(graph, attack);
            graph.Connect(idlePlayable, 0, mixer, 0);
            graph.Connect(walkPlayable, 0, mixer, 1);
            graph.Connect(attackPlayable, 0, mixer, 2);
            bool hasWork = work != null;
            bool hasDeath = death != null;
            var workPlayable = hasWork ? AnimationClipPlayable.Create(graph, work) : default(AnimationClipPlayable);
            var deathPlayable = hasDeath ? AnimationClipPlayable.Create(graph, death) : default(AnimationClipPlayable);
            if (hasWork) graph.Connect(workPlayable, 0, mixer, 3);
            if (hasDeath) graph.Connect(deathPlayable, 0, mixer, 4);
            mixer.SetInputWeight(0, 1f);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(mixer);
            // Each clip keeps its own time, so the offset goes on every clip: the soldiers do not step in unison.
            float offset = (id % 97u) / 97f;
            idlePlayable.SetTime(offset * idle.length);
            walkPlayable.SetTime(offset * walk.length);
            attackPlayable.SetTime(offset * attack.length);
            if (hasWork) workPlayable.SetTime(offset * work.length);
            if (hasDeath) deathPlayable.SetTime(0d);
            graph.Play();
            return new AnimationHandle(graph, mixer, idlePlayable, walkPlayable, attackPlayable, workPlayable,
                deathPlayable, attack.length, hasWork ? work.length : 0d, hasWork, hasDeath);
        }

#if UNITY_EDITOR
        private static AnimationClip LoadAnimationClip(string path)
        {
            if (path == null) return null;
            if (animationClips.ContainsKey(path)) return animationClips[path];
            AnimationClip clip = null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
                // An FBX also carries Unity's "__preview__" copy of each take; only the real clip is wanted.
                if (assets[i] is AnimationClip candidate && !candidate.name.StartsWith("__preview__")) { clip = candidate; break; }
            animationClips.Add(path, clip);
            return clip;
        }
#endif

        private static AnimationClip LoadAnimationClip(AnimationSpec spec, UnitMotion motion)
        {
#if UNITY_EDITOR
            return spec == null ? null : LoadAnimationClip(spec.Path(motion));
#else
            // A player build finds the staged looks' clips in the manifest, listed by pack file.
            var manifest = LoadManifest();
            return manifest == null || spec == null ? null : manifest.GetAnimationByFile(spec.Path(motion));
#endif
        }

        private static AnimationClip LoadAnimationClip(UnitKind kind, UnitMotion motion)
        {
#if UNITY_EDITOR
            return LoadAnimationClip(AnimationAssetPath(kind, motion));
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetAnimation(kind, motion);
#endif
        }

        /// <summary>Finished height of a building model, or 0 for kinds that keep the placeholder box.</summary>
        public static float BuildingHeight(BuildingKind kind) { return BuildingTable.TryGetValue(kind, out var s) ? s.Height : 0f; }

        public static float BuildingWidthScale(BuildingKind kind) { return BuildingTable.TryGetValue(kind, out var s) ? s.WidthScale : 1f; }

        private static Color BuildingTint(BuildingKind kind)
        {
            return BuildingTable.TryGetValue(kind, out var s) ? s.Tint : Color.white;
        }

        public static bool HasUnit(UnitKind kind) { return !Disabled && LoadUnitModel(kind) != null; }

        public static bool HasBuilding(BuildingKind kind) { return !Disabled && LoadBuildingModel(kind) != null; }

        public static bool HasCore() { return !Disabled && LoadCoreModel() != null; }

        public static bool HasOutpost() { return !Disabled && LoadOutpostModel() != null; }

        /// <summary>Creates an outpost tower under parent, white (neutral) until SetOutpostOwner colors it.</summary>
        public static bool TryCreateOutpost(Transform parent, out GameObject instance, out float height)
        {
            return TryCreate(LoadOutpostModel(), LoadMaterial(LocalVisualPackManifest.BuildingWhiteMaterialKey,
                BuildingMaterialAssetPath(0)), parent, OutpostWidth, true, out instance, out height);
        }

        /// <summary>Colors an outpost tower by its owner: 1 west blue, 2 east red, anything else neutral white.</summary>
        public static void SetOutpostOwner(GameObject instance, uint owner)
        {
            Recolor(instance, LoadMaterial(BuildingMaterialKey(owner), BuildingMaterialAssetPath(owner)));
        }

        /// <summary>Creates a unit of the pack under parent, feet on the parent's y, team blue (own) or red (enemy).</summary>
        public static bool TryCreateUnit(UnitKind kind, bool own, Transform parent, out GameObject instance)
        {
            return TryCreate(LoadUnitModel(kind), LoadMaterial(own ? LocalVisualPackManifest.UnitBlueMaterialKey : LocalVisualPackManifest.UnitRedMaterialKey,
                UnitMaterialAssetPath(own)),
                parent, UnitHeight(kind), false, out instance, out _);
        }

        public static bool TryCreateDisplayUnit(UnitKind kind, CivKind civ, int stage, bool own, Transform parent,
            out GameObject instance, out bool tiered)
        {
            tiered = false;
            if (kind == UnitKind.Infantry)
            {
                if (TryCreateTieredUnit(GetInfantrySpec(civ, stage), own, parent, out instance))
                {
                    tiered = true;
                    return true;
                }
            }
            else if (TryCreateTieredUnit(GetTieredUnitSpec(kind, stage), own, parent, out instance))
            {
                tiered = true;
                return true;
            }
            return TryCreateUnit(kind, own, parent, out instance);
        }

        public static AnimationHandle TryCreateDisplayAnimation(GameObject instance, UnitKind kind, CivKind civ,
            int stage, ulong id, bool tiered)
        {
            if (tiered)
            {
                TieredUnitSpec spec = kind == UnitKind.Infantry ? GetInfantrySpec(civ, stage) : GetTieredUnitSpec(kind, stage);
                var animation = TryCreateAnimation(instance, spec == null ? null : spec.Animation, null, id);
                if (animation != null) return animation;
            }
            return TryCreateAnimation(instance, kind, id);
        }

        private static bool TryCreateTieredUnit(TieredUnitSpec spec, bool own, Transform parent, out GameObject instance)
        {
            instance = null;
            if (spec == null) return false;
            return TryCreate(LoadTieredModel(spec.File), LoadMaterial(own ? LocalVisualPackManifest.UnitBlueMaterialKey : LocalVisualPackManifest.UnitRedMaterialKey,
                UnitMaterialAssetPath(own)), parent, spec.Height, false, out instance, out _);
        }

        public static bool HasBanner(int index)
        {
            return !Disabled && LoadBannerModel(index) != null;
        }

        public static bool TryCreateBanner(int index, Transform parent, out GameObject instance)
        {
            // Taller than a soldier (2.4 m), so the banner shows above the ranks.
            return TryCreate(LoadBannerModel(index), null, parent, 4.5f, false, out instance, out _);
        }

        /// <summary>
        /// Creates a building of the pack under parent: footprint width x width, the table's finished height, owner's
        /// color, feet on the parent's y. The holder's local y scale is 1 for the finished height, so
        /// <see cref="SetBuildingProgress"/> can raise it while under construction without measuring again.
        /// </summary>
        public static bool TryCreateBuilding(BuildingKind kind, uint owner, Transform parent, float width, out GameObject instance)
        {
            instance = null;
            float height = BuildingHeight(kind);
            if (height <= 0f) return false;
            return TryCreateStretched(LoadBuildingModel(kind), LoadMaterial(BuildingMaterialKey(owner), BuildingMaterialAssetPath(owner)),
                parent, width, height, kind, out instance);
        }

        /// <summary>Under construction a building rises from the ground: progress 0..1 scales the holder's height only.</summary>
        public static void SetBuildingProgress(GameObject instance, float progress)
        {
            if (instance == null) return;
            instance.transform.localScale = new Vector3(1f, Mathf.Clamp(progress, 0.15f, 1f), 1f);
        }

        /// <summary>Creates the core building under parent; height is the top of the model, for placing the HP bar.</summary>
        public static bool TryCreateCore(bool west, Transform parent, out GameObject instance, out float height)
        {
            uint owner = west ? 1u : 2u;
            return TryCreate(LoadCoreModel(), LoadMaterial(BuildingMaterialKey(owner), BuildingMaterialAssetPath(owner)),
                parent, CoreWidth, true, out instance, out height);
        }

        private static string BuildingMaterialKey(uint owner)
        {
            return owner == 1 ? LocalVisualPackManifest.BuildingBlueMaterialKey :
                owner == 2 ? LocalVisualPackManifest.BuildingRedMaterialKey : LocalVisualPackManifest.BuildingWhiteMaterialKey;
        }

        private static LocalVisualPackManifest LoadManifest()
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<LocalVisualPackManifest>(ManifestAssetPath);
#else
            return Resources.Load<LocalVisualPackManifest>(ManifestResourceName);
#endif
        }

        private static GameObject LoadModelAtPath(string path)
        {
            if (Disabled || path == null) return null;
            if (modelAssets.ContainsKey(path)) return modelAssets[path];
            GameObject model = null;
#if UNITY_EDITOR
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
#endif
            modelAssets.Add(path, model);
            return model;
        }

        private static GameObject LoadTieredModel(string file)
        {
            if (file == null) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(Units + file);
#else
            if (Disabled) return null;
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetModelByFile(Units + file);
#endif
        }

        private static GameObject LoadBannerModel(int index)
        {
            string path = BannerAssetPath(index);
            if (path == null) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(path);
#else
            if (Disabled) return null;
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetModelByFile(path);
#endif
        }

        private static GameObject LoadUnitModel(UnitKind kind)
        {
            if (Disabled) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(UnitAssetPath(kind));
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetUnit(kind);
#endif
        }

        private static GameObject LoadBuildingModel(BuildingKind kind)
        {
            if (Disabled) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(BuildingAssetPath(kind));
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetBuilding(kind);
#endif
        }

        private static GameObject LoadCoreModel()
        {
            if (Disabled) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(CoreAssetPath());
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetCore();
#endif
        }

        private static GameObject LoadOutpostModel()
        {
            if (Disabled) return null;
#if UNITY_EDITOR
            return LoadModelAtPath(OutpostAssetPath());
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetOutpost();
#endif
        }

        private static Material LoadMaterial(string key, string editorPath)
        {
            if (Disabled) return null;
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Material>(editorPath);
#else
            var manifest = LoadManifest();
            return manifest == null ? null : manifest.GetMaterial(key);
#endif
        }

        private static void Recolor(GameObject instance, Material material)
        {
            if (material == null || instance == null) return;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                // Every slot, not only the first: some pack models carry several materials on one renderer.
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        private static void ApplyBuildingTint(GameObject instance, Color tint)
        {
            if (instance == null || tint == Color.white) return;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;
                    if (material.HasProperty("_BaseColor")) block.SetColor("_BaseColor", material.GetColor("_BaseColor") * tint);
                    else if (material.HasProperty("_Color")) block.SetColor("_Color", material.GetColor("_Color") * tint);
                }
                renderer.SetPropertyBlock(block);
            }
        }

        private static bool TryCreate(GameObject model, Material material, Transform parent, float size, bool bySpan,
            out GameObject instance, out float height)
        {
            instance = null; height = 0f;
            if (Disabled || model == null || parent == null) return false;
            var holder = new GameObject("Pack");
            holder.transform.SetParent(parent, false);
            var body = Object.Instantiate(model, holder.transform);
            body.transform.localPosition = Vector3.zero;
            Recolor(body, material);
            var bounds = Measure(body);
            float measured = bySpan ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
            if (measured <= 0.0001f) { Discard(holder); return false; }
            holder.transform.localScale = Vector3.one * (size / measured);
            bounds = Measure(body);
            // Feet on the parent's ground level, whatever the model's own pivot is. Shifting the body (not the holder)
            // keeps the holder's pivot on the ground, so scaling the holder's height later grows it from the ground.
            body.transform.position += new Vector3(0f, parent.position.y - bounds.min.y, 0f);
            height = bounds.size.y;
            instance = holder;
            return true;
        }

        /// <summary>Like TryCreate, but stretches the body to an exact footprint width and height; the holder stays at scale 1.</summary>
        private static bool TryCreateStretched(GameObject model, Material material, Transform parent, float width, float height,
            BuildingKind kind,
            out GameObject instance)
        {
            instance = null;
            if (Disabled || model == null || parent == null) return false;
            var holder = new GameObject("Pack");
            holder.transform.SetParent(parent, false);
            var body = Object.Instantiate(model, holder.transform);
            body.transform.localPosition = Vector3.zero;
            Recolor(body, material);
            ApplyBuildingTint(body, BuildingTint(kind));
            var bounds = Measure(body);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span <= 0.0001f || bounds.size.y <= 0.0001f) { Discard(holder); return false; }
            body.transform.localScale = Vector3.Scale(body.transform.localScale, new Vector3(width / span, height / bounds.size.y, width / span));
            bounds = Measure(body);
            body.transform.position += new Vector3(0f, parent.position.y - bounds.min.y, 0f);
            instance = holder;
            return true;
        }

        private static Bounds Measure(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool first = true;
            foreach (var renderer in renderers)
            {
                if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static void Discard(Object target)
        {
#if UNITY_EDITOR
            if (UnityEngine.Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
#else
            Object.Destroy(target);
#endif
        }
    }
}
