using System;
using System.Collections.Generic;
using System.IO;
using Rts.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>Writes optional sound references into Resources for a player build.</summary>
    public sealed class LocalSoundPackManifestBuilder : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 1; } }
        [MenuItem("RTS/音素材パックの一覧を作り直す")]
        public static void RebuildFromMenu() { RebuildManifest(); }
        public void OnPreprocessBuild(BuildReport report) { RebuildManifest(); }
        public static void RebuildForProductBuild() { RebuildManifest(); }

        private static void RebuildManifest()
        {
            EnsureFolder("Assets/RTS/Generated"); EnsureFolder("Assets/RTS/Generated/Resources");
            if (AssetDatabase.LoadAssetAtPath<LocalSoundPackManifest>(LocalSoundPack.ManifestAssetPath) != null)
                AssetDatabase.DeleteAsset(LocalSoundPack.ManifestAssetPath);
            var manifest = ScriptableObject.CreateInstance<LocalSoundPackManifest>();
            var entries = new List<LocalSoundPackManifest.CueEntry>();
            foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue)))
            {
                var clips = new List<AudioClip>();
                foreach (var path in LocalSoundPack.Paths(cue))
                    if (!string.IsNullOrEmpty(path)) { var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path); if (clip != null) clips.Add(clip); }
                entries.Add(new LocalSoundPackManifest.CueEntry { cue = cue, clips = clips.ToArray() });
            }
            manifest.Cues = entries.ToArray();
            var calm = new List<AudioClip>(); var battle = new List<AudioClip>(); var victory = new List<AudioClip>(); var defeat = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/ThirdParty/Music" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path); if (clip == null) continue;
                if (name.StartsWith("calm_", StringComparison.Ordinal)) calm.Add(clip);
                else if (name.StartsWith("battle_", StringComparison.Ordinal)) battle.Add(clip);
                else if (name.StartsWith("victory_", StringComparison.Ordinal)) victory.Add(clip);
                else if (name.StartsWith("defeat_", StringComparison.Ordinal)) defeat.Add(clip);
            }
            manifest.Calm = calm.ToArray(); manifest.Battle = battle.ToArray(); manifest.Victory = victory.ToArray(); manifest.Defeat = defeat.ToArray();
            AssetDatabase.CreateAsset(manifest, LocalSoundPack.ManifestAssetPath); EditorUtility.SetDirty(manifest); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("音素材パックの一覧を作成しました: " + LocalSoundPack.ManifestAssetPath);
        }
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
        }
    }
}
