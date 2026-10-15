using System;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Build-time references to optional clips. Missing entries are intentionally valid.</summary>
    public sealed class LocalSoundPackManifest : ScriptableObject
    {
        [Serializable] public sealed class CueEntry
        {
            public SoundCue cue;
            public AudioClip[] clips = new AudioClip[0];
        }
        [SerializeField] private CueEntry[] cues = new CueEntry[0];
        [SerializeField] private AudioClip[] calm = new AudioClip[0];
        [SerializeField] private AudioClip[] battle = new AudioClip[0];
        [SerializeField] private AudioClip[] victory = new AudioClip[0];
        [SerializeField] private AudioClip[] defeat = new AudioClip[0];
        public CueEntry[] Cues { get { return cues; } set { cues = value ?? new CueEntry[0]; } }
        public AudioClip[] Calm { get { return calm; } set { calm = value ?? new AudioClip[0]; } }
        public AudioClip[] Battle { get { return battle; } set { battle = value ?? new AudioClip[0]; } }
        public AudioClip[] Victory { get { return victory; } set { victory = value ?? new AudioClip[0]; } }
        public AudioClip[] Defeat { get { return defeat; } set { defeat = value ?? new AudioClip[0]; } }
        public AudioClip[] Get(SoundCue cue)
        {
            if (cues == null) return Array.Empty<AudioClip>();
            for (int i = 0; i < cues.Length; i++) if (cues[i] != null && cues[i].cue == cue) return cues[i].clips ?? Array.Empty<AudioClip>();
            return Array.Empty<AudioClip>();
        }
    }
}
