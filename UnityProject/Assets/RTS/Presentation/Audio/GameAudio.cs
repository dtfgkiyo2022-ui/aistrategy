using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed class GameAudio : MonoBehaviour
    {
        public const string MasterKey = "rts.audio.master";
        public const string MusicKey = "rts.audio.music";
        public const string EffectsKey = "rts.audio.effects";
        public const string UiKey = "rts.audio.ui";
        public const int MaxSources = 16;
        private const float BattleFadeSeconds = 2f;
        private const float AttackNoticeSeconds = 10f;
        private const float BattleRecentSeconds = 8f;
        private static GameAudio instance;
        private readonly List<AudioSource> sources = new List<AudioSource>(MaxSources);
        private readonly Dictionary<SoundCue, float> lastCueAt = new Dictionary<SoundCue, float>();
        private readonly Dictionary<SoundCue, int> nextClip = new Dictionary<SoundCue, int>();
        private readonly List<FrameSoundEvent> frameEvents = new List<FrameSoundEvent>(64);
        private AudioSource calmSource, battleSource;
        private AudioClip[] calmClips = Array.Empty<AudioClip>(), battleClips = Array.Empty<AudioClip>();
        private int calmIndex, battleIndex;
        private float lastAttackNotice = -999f;
        private float battleSeenAt = -999f;
        private bool battleMode;
        private int activeBgm;
        private bool outcomePlayed;

        public static GameAudio Instance { get { return instance; } }
        public static float MasterVolume { get { return PlayerPrefs.GetFloat(MasterKey, 1f); } set { Save(MasterKey, value); } }
        public static float MusicVolume { get { return PlayerPrefs.GetFloat(MusicKey, 1f); } set { Save(MusicKey, value); } }
        public static float EffectsVolume { get { return PlayerPrefs.GetFloat(EffectsKey, 1f); } set { Save(EffectsKey, value); } }
        public static float UiVolume { get { return PlayerPrefs.GetFloat(UiKey, 1f); } set { Save(UiKey, value); } }
        private static void Save(string key, float value) { PlayerPrefs.SetFloat(key, Mathf.Clamp01(value)); PlayerPrefs.Save(); }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            for (int i = 0; i < MaxSources; i++) { var source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; sources.Add(source); }
            calmSource = AddBgmSource(); battleSource = AddBgmSource();
            var manifest = Resources.Load<LocalSoundPackManifest>(LocalSoundPack.ManifestResourceName);
            if (manifest != null) { calmClips = manifest.Calm; battleClips = manifest.Battle; }
        }
        private AudioSource AddBgmSource() { var source = gameObject.AddComponent<AudioSource>(); source.loop = false; source.playOnAwake = false; return source; }
        private static GameAudio EnsureInstance()
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            var go = new GameObject("GameAudio"); instance = go.AddComponent<GameAudio>(); DontDestroyOnLoad(go); return instance;
        }
        public static void Ui(SoundCue cue) { var audio = EnsureInstance(); if (audio != null) audio.Play(cue, 1f, true); }
        public static void Effect(SoundCue cue, float distanceScale = 1f) { var audio = EnsureInstance(); if (audio != null) audio.Play(cue, distanceScale, false); }
        public static void FeedFrames(Rts.Contracts.FactionFrame previous, Rts.Contracts.FactionFrame current, uint ownFactionId)
        {
            var audio = EnsureInstance(); if (audio != null) audio.CompareFrames(previous, current, ownFactionId);
        }
        public static void PlayOutcome(bool victory)
        {
            var audio = EnsureInstance(); if (audio == null || audio.outcomePlayed) return;
            var manifest = Resources.Load<LocalSoundPackManifest>(LocalSoundPack.ManifestResourceName);
            if (manifest == null) return;
            var clips = victory ? manifest.Victory : manifest.Defeat;
            if (clips == null || clips.Length == 0) return;
            audio.outcomePlayed = true;
            audio.battleSource.PlayOneShot(clips[0], MasterVolume * MusicVolume);
        }
        public void Compare(FrameSoundEvent[] unused) { }
        public void CompareFrames(Rts.Contracts.FactionFrame previous, Rts.Contracts.FactionFrame current, uint ownFactionId)
        {
            FrameSoundEvents.Compare(previous, current, ownFactionId, frameEvents);
            for (int i = 0; i < frameEvents.Count; i++)
            {
                var item = frameEvents[i];
                if (item.Cue == SoundCue.MeleeHit || item.Cue == SoundCue.ArrowHit || item.Cue == SoundCue.StructureHit || item.Cue == SoundCue.RamHit)
                    MarkCombat();
                if (item.Cue == SoundCue.UnderAttackCore || item.Cue == SoundCue.UnderAttackBuilding || item.Cue == SoundCue.UnderAttackVillager)
                { if (Time.unscaledTime - lastAttackNotice < AttackNoticeSeconds) continue; lastAttackNotice = Time.unscaledTime; }
                Effect(item.Cue, 1f);
            }
        }
        private void Play(SoundCue cue, float distanceScale, bool ui)
        {
            float now = Time.unscaledTime;
            if (Time.timeScale <= 0f && !ui) return;
            float interval = ui ? 0.05f : 0.12f;
            float last; if (lastCueAt.TryGetValue(cue, out last) && now - last < interval) return;
            int same = 0; for (int i = 0; i < sources.Count; i++) if (sources[i].isPlaying && sources[i].name == cue.ToString()) same++;
            if (same >= 3) return;
            AudioClip[] clips = LocalSoundPack.Load(cue); if (clips == null || clips.Length == 0) return;
            int index; nextClip.TryGetValue(cue, out index); index %= clips.Length;
            var source = FindFreeSource(); if (source == null) return;
            source.name = cue.ToString(); source.volume = MasterVolume * (ui ? UiVolume : EffectsVolume) * Mathf.Clamp01(distanceScale);
            source.clip = clips[index]; source.Play(); nextClip[cue] = index + 1; lastCueAt[cue] = now;
        }
        private AudioSource FindFreeSource() { for (int i = 0; i < sources.Count; i++) if (!sources[i].isPlaying) return sources[i]; return null; }
        public void MarkCombat() { battleSeenAt = Time.unscaledTime; }
        private void Update()
        {
            bool wanted = Time.unscaledTime - battleSeenAt < BattleRecentSeconds;
            if (wanted != battleMode) { battleMode = wanted; activeBgm = 0; }
            float target = MasterVolume * MusicVolume * (Time.timeScale <= 0f ? 0.2f : 1f);
            EnsureBgm(calmSource, calmClips, ref calmIndex, !battleMode);
            EnsureBgm(battleSource, battleClips, ref battleIndex, battleMode);
            calmSource.volume = Mathf.MoveTowards(calmSource.volume, battleMode ? 0f : target, Time.unscaledDeltaTime / BattleFadeSeconds);
            battleSource.volume = Mathf.MoveTowards(battleSource.volume, battleMode ? target : 0f, Time.unscaledDeltaTime / BattleFadeSeconds);
        }
        private static void EnsureBgm(AudioSource source, AudioClip[] clips, ref int index, bool wanted)
        {
            if (!wanted || clips == null || clips.Length == 0) return;
            if (source.isPlaying) return;
            source.clip = clips[index % clips.Length]; index++; source.Play();
        }
        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
