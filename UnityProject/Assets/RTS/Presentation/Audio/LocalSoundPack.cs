using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rts.Presentation
{
    /// <summary>
    /// Candidate paths for the purchased sound pack. Keep the paths empty until the owner chooses a pack.
    /// Fill the first/second/etc. string in CuePaths below with an Assets/ThirdParty/... path.
    /// </summary>
    public static class LocalSoundPack
    {
        public const string ManifestAssetPath = "Assets/RTS/Generated/Resources/LocalSoundPackManifest.asset";
        public const string ManifestResourceName = "LocalSoundPackManifest";

        private static readonly Dictionary<SoundCue, string[]> CuePaths = new Dictionary<SoundCue, string[]>
        {
            // Owner: replace "" entries with AssetDatabase paths under Assets/ThirdParty/.
            { SoundCue.MeleeHit, new[] { "" } }, { SoundCue.ArrowRelease, new[] { "" } },
            { SoundCue.ArrowHit, new[] { "" } }, { SoundCue.StructureHit, new[] { "" } },
            { SoundCue.UnitDeath, new[] { "" } }, { SoundCue.RamHit, new[] { "" } },
            { SoundCue.ConstructionStarted, new[] { "" } }, { SoundCue.ConstructionCompleted, new[] { "" } },
            { SoundCue.VillagerProduced, new[] { "" } }, { SoundCue.UnitTrained, new[] { "" } },
            { SoundCue.ResourceDrop, new[] { "" } }, { SoundCue.ProcessingOutput, new[] { "" } },
            { SoundCue.BeltPlaced, new[] { "" } }, { SoundCue.BuildingDestroyed, new[] { "" } },
            { SoundCue.AgeAdvancedOwn, new[] { "" } }, { SoundCue.AgeAdvancedEnemy, new[] { "" } },
            { SoundCue.UnderAttackCore, new[] { "" } }, { SoundCue.UnderAttackBuilding, new[] { "" } },
            { SoundCue.UnderAttackVillager, new[] { "" } }, { SoundCue.CivilizationChosen, new[] { "" } },
            { SoundCue.ResearchCompleted, new[] { "" } }, { SoundCue.ResourceShortage, new[] { "" } },
            { SoundCue.Click, new[] { "" } }, { SoundCue.CommandMove, new[] { "" } },
            { SoundCue.CommandAttack, new[] { "" } }, { SoundCue.SelectionChanged, new[] { "" } },
            { SoundCue.PlacementConfirmed, new[] { "" } }, { SoundCue.PlacementRejected, new[] { "" } }
        };

        /// <summary>Generated sound effects (MOSS-SoundEffect, #555): files named "&lt;Cue&gt;_&lt;n&gt;.wav" in this folder.</summary>
        public const string GeneratedFolder = "Assets/RTS/Audio/Sfx";

        public static IReadOnlyList<string> Paths(SoundCue cue)
        {
            string[] paths;
            var result = new List<string>(CuePaths.TryGetValue(cue, out paths) ? paths : Array.Empty<string>());
#if UNITY_EDITOR
            result.AddRange(GeneratedPaths(cue));
#endif
            return result;
        }

#if UNITY_EDITOR
        public static List<string> GeneratedPaths(SoundCue cue)
        {
            var found = new List<string>();
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) return found;
            string prefix = cue + "_";
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { GeneratedFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.Ordinal)) found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }
#endif

        public static AudioClip[] Load(SoundCue cue)
        {
            var manifest = Resources.Load<LocalSoundPackManifest>(ManifestResourceName);
            if (manifest != null) return manifest.Get(cue);
#if UNITY_EDITOR
            var result = new List<AudioClip>();
            foreach (var path in Paths(cue))
                if (!string.IsNullOrEmpty(path))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null) result.Add(clip);
                }
            return result.ToArray();
#else
            return Array.Empty<AudioClip>();
#endif
        }

        public static IEnumerable<string> AllPaths()
        {
            foreach (var pair in CuePaths)
                foreach (var path in pair.Value)
                    if (!string.IsNullOrEmpty(path)) yield return path;
        }
    }
}
