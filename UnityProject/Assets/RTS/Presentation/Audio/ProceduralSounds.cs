using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Stand-in battle sounds made in code while the sound pack is empty (11-01: no clip was ever assigned, so battles
    /// were silent). A purchased clip for the same cue always wins; these are only the fallback.
    /// </summary>
    public static class ProceduralSounds
    {
        private const int Rate = 22050;
        private static readonly Dictionary<SoundCue, AudioClip[]> Cache = new Dictionary<SoundCue, AudioClip[]>();
        private static readonly AudioClip[] None = new AudioClip[0];

        public static AudioClip[] For(SoundCue cue)
        {
            if (Cache.TryGetValue(cue, out var clips)) return clips;
            switch (cue)
            {
                case SoundCue.MeleeHit: clips = Variants(cue, 3, Hit); break;
                case SoundCue.ArrowHit: clips = Variants(cue, 2, ArrowThud); break;
                case SoundCue.ArrowRelease: clips = Variants(cue, 2, Twang); break;
                case SoundCue.UnitDeath: clips = Variants(cue, 2, Fall); break;
                case SoundCue.StructureHit: clips = Variants(cue, 2, Knock); break;
                case SoundCue.RamHit: clips = Variants(cue, 1, Boom); break;
                case SoundCue.BuildingDestroyed: clips = Variants(cue, 1, Collapse); break;
                default: clips = None; break;
            }
            Cache[cue] = clips;
            return clips;
        }

        private delegate float[] Maker(System.Random random, int variant);

        private static AudioClip[] Variants(SoundCue cue, int count, Maker maker)
        {
            var result = new AudioClip[count];
            for (int i = 0; i < count; i++)
            {
                // A fixed seed per cue and variant: the same sound every run.
                var samples = maker(new System.Random(((int)cue + 1) * 7919 + i * 104729), i);
                var clip = AudioClip.Create("Procedural_" + cue + "_" + i, samples.Length, 1, Rate, false);
                clip.SetData(samples, 0);
                result[i] = clip;
            }
            return result;
        }

        /// <summary>Blade on body: a bright noise crack over a short low thump.</summary>
        private static float[] Hit(System.Random random, int variant)
        {
            int length = Seconds(0.11f);
            var data = new float[length];
            float low = 0f, pitch = 140f + variant * 25f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.35f;
                float crack = (noise - low) * Mathf.Exp(-t * 70f);
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 30f);
                data[i] = 0.5f * crack + 0.55f * thump;
            }
            return data;
        }

        private static float[] ArrowThud(System.Random random, int variant)
        {
            int length = Seconds(0.07f);
            var data = new float[length];
            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.2f;
                data[i] = 0.6f * low * Mathf.Exp(-t * 60f) + 0.4f * Mathf.Sin(2f * Mathf.PI * (220f + variant * 30f) * t) * Mathf.Exp(-t * 80f);
            }
            return data;
        }

        private static float[] Twang(System.Random random, int variant)
        {
            int length = Seconds(0.12f);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float pitch = 520f + variant * 60f - t * 900f;
                data[i] = 0.25f * Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 35f);
            }
            return data;
        }

        /// <summary>A body falling: a pitch that drops, with a little dust noise.</summary>
        private static float[] Fall(System.Random random, int variant)
        {
            int length = Seconds(0.28f);
            var data = new float[length];
            float phase = 0f, low = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float pitch = Mathf.Lerp(150f + variant * 20f, 55f, t / 0.28f);
                phase += 2f * Mathf.PI * pitch / Rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.08f;
                data[i] = (0.6f * Mathf.Sin(phase) + 0.5f * low) * Mathf.Exp(-t * 12f);
            }
            return data;
        }

        private static float[] Knock(System.Random random, int variant)
        {
            int length = Seconds(0.16f);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(2f * Mathf.PI * (190f + variant * 30f) * t);
                float b = Mathf.Sin(2f * Mathf.PI * (430f + variant * 40f) * t);
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                data[i] = (0.5f * a + 0.3f * b) * Mathf.Exp(-t * 28f) + 0.2f * noise * Mathf.Exp(-t * 90f);
            }
            return data;
        }

        private static float[] Boom(System.Random random, int variant)
        {
            int length = Seconds(0.35f);
            var data = new float[length];
            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.05f;
                data[i] = (0.7f * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 40f, t / 0.35f) * t) + 0.8f * low) * Mathf.Exp(-t * 9f);
            }
            return data;
        }

        private static float[] Collapse(System.Random random, int variant)
        {
            int length = Seconds(0.7f);
            var data = new float[length];
            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.04f;
                float rumble = Mathf.Sin(2f * Mathf.PI * 48f * t);
                data[i] = (1.2f * low + 0.4f * rumble) * Mathf.Exp(-t * 4.5f);
            }
            return data;
        }

        private static int Seconds(float seconds) { return Mathf.Max(1, Mathf.RoundToInt(seconds * Rate)); }
    }
}
