using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// 11-11: dust and sparks. Marching soldiers kick up dust at their feet, a hit throws a few glowing flecks that fall,
    /// a falling soldier raises a little cloud, and a building that is hit or lost throws dust and flecks. One pool of
    /// camera-facing quads, reused oldest first, so a large battle never grows it; only within the camera's reach.
    /// Display only: read from the same frame changes as the hit marks, nothing is written back.
    /// </summary>
    public sealed partial class BattlefieldView
    {
        private const int MaxParticles = 260;
        private const float DustCameraDistance = 140f;
        private const float WalkDustInterval = 0.75f;
        private const int MaxWalkDustPerFrame = 10;

        private sealed class Particle
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Velocity;
            public float Life, MaxLife, StartSize, EndSize, Gravity, Drag;
            public Color Color;
            public bool Glint;
        }

        private readonly List<Particle> particles = new List<Particle>();
        private int nextParticle;
        private static Material dustMaterial, glintMaterial;
        private static bool particleMaterialsLooked;
        private static Mesh particleQuad;
        private MaterialPropertyBlock particleBlock;
        private readonly System.Random particleRandom = new System.Random(11);

        private static readonly Color WalkDustColor = new Color(0.66f, 0.58f, 0.45f, 0.55f);
        private static readonly Color FallDustColor = new Color(0.62f, 0.55f, 0.44f, 0.7f);
        private static readonly Color RubbleDustColor = new Color(0.60f, 0.57f, 0.52f, 0.75f);
        private static readonly Color HitGlintColor = new Color(1f, 0.55f, 0.18f, 1f);
        private static readonly Color OwnHitGlintColor = new Color(1f, 0.32f, 0.14f, 1f);

        private float Range01() => (float)particleRandom.NextDouble();
        private float Range(float a, float b) => a + (b - a) * Range01();

        private bool ParticlesReady()
        {
            if (!particleMaterialsLooked)
            {
                particleMaterialsLooked = true;
                var puff = Shader.Find("Rts/SoftPuff");
                var glint = Shader.Find("Rts/SparkGlint");
                if (puff != null) dustMaterial = new Material(puff) { name = "Battle dust" };
                if (glint != null) glintMaterial = new Material(glint) { name = "Battle glint" };
            }
            if (particleBlock == null) particleBlock = new MaterialPropertyBlock();
            return dustMaterial != null && glintMaterial != null;
        }

        private static Mesh ParticleQuad()
        {
            if (particleQuad != null) return particleQuad;
            particleQuad = new Mesh { name = "Particle quad" };
            particleQuad.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            });
            particleQuad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            particleQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            particleQuad.RecalculateBounds();
            return particleQuad;
        }

        private Particle NextParticle()
        {
            if (particles.Count < MaxParticles)
            {
                var go = new GameObject("BattleParticle");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = ParticleQuad();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var made = new Particle { Transform = go.transform, Renderer = renderer };
                particles.Add(made);
                return made;
            }
            var reused = particles[nextParticle];
            nextParticle = (nextParticle + 1) % MaxParticles;
            return reused;
        }

        private void EmitParticle(Vector3 position, Vector3 velocity, float life, float startSize, float endSize,
            Color color, bool glint, float gravity, float drag)
        {
            // Farther from the camera, bigger, so a fight seen from the usual height still shows (1x up to 60 m).
            var camera = Camera.main;
            if (camera != null)
            {
                float scale = Mathf.Clamp(Vector3.Distance(camera.transform.position, position) / 60f, 1f, 2.6f);
                startSize *= scale; endSize *= scale;
            }
            var p = NextParticle();
            p.Transform.position = position;
            p.Velocity = velocity;
            p.Life = life; p.MaxLife = life;
            p.StartSize = startSize; p.EndSize = endSize;
            p.Color = color; p.Glint = glint;
            p.Gravity = gravity; p.Drag = drag;
            p.Renderer.sharedMaterial = glint ? glintMaterial : dustMaterial;
            p.Transform.localScale = Vector3.zero;
            p.Transform.gameObject.SetActive(true);
        }

        private static bool NearCamera(Camera camera, Vector3 position, float distance)
            => camera != null && (camera.transform.position - position).sqrMagnitude <= distance * distance;

        /// <summary>A puff or a few of dust low on the ground, drifting out and up.</summary>
        private void EmitDust(Vector3 ground, int count, float size, Color color, float spread, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var at = ground + outward * Range(0f, spread * 0.4f) + Vector3.up * Range(0.15f, 0.45f);
                var velocity = outward * Range(0.3f, 1.0f) * spread + Vector3.up * Range(0.4f, 1.0f);
                EmitParticle(at, velocity, life * Range(0.8f, 1.2f), size * Range(0.5f, 0.7f), size * Range(1.2f, 1.6f),
                    color, false, -0.15f, 1.6f);
            }
        }

        /// <summary>Glowing flecks thrown out from a hit, falling as they fade.</summary>
        private void EmitGlints(Vector3 at, int count, Color color, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                var velocity = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Range(0.4f, 1f) * speed
                    + Vector3.up * Range(0.6f, 1.2f) * speed;
                float size = Range(0.45f, 0.75f);
                EmitParticle(at, velocity, Range(0.4f, 0.6f), size, size * 0.35f, color, true, 16f, 0.6f);
            }
        }

        private void HitParticles(Vector3 body, bool ownVictim)
        {
            if (!ParticlesReady() || !NearCamera(Camera.main, body, SparkCameraDistance)) return;
            EmitGlints(body, 6, ownVictim ? OwnHitGlintColor : HitGlintColor, 4.5f);
        }

        private void FallParticles(Vector3 ground)
        {
            if (!ParticlesReady() || !NearCamera(Camera.main, ground, DustCameraDistance)) return;
            EmitDust(new Vector3(ground.x, 0f, ground.z), 4, 2.6f, FallDustColor, 1.4f, 1.3f);
        }

        private void StructureHitParticles(Vector3 at, bool ownVictim)
        {
            if (!ParticlesReady() || !NearCamera(Camera.main, at, SparkCameraDistance)) return;
            EmitGlints(at, 8, ownVictim ? OwnHitGlintColor : HitGlintColor, 5.5f);
            EmitDust(new Vector3(at.x, Mathf.Max(0f, at.y - 2f), at.z), 3, 3.2f, RubbleDustColor, 1.8f, 1.6f);
        }

        private void StructureLostParticles(Vector3 at)
        {
            if (!ParticlesReady() || !NearCamera(Camera.main, at, DustCameraDistance + 40f)) return;
            EmitDust(new Vector3(at.x, 0f, at.z), 12, 6.5f, RubbleDustColor, 4.0f, 2.8f);
            EmitGlints(at + Vector3.up * 1.5f, 14, HitGlintColor, 6.5f);
        }

        /// <summary>Marching soldiers near the camera kick up a little dust now and then.</summary>
        private void EmitWalkDust(float matchSeconds)
        {
            if (matchSeconds <= 0f || !ParticlesReady()) return;
            var camera = Camera.main;
            if (camera == null) return;
            int budget = MaxWalkDustPerFrame;
            foreach (var visual in units.Values)
            {
                if (!visual.IsWalking || visual.IsDying || visual.Object == null) continue;
                visual.DustSeconds -= matchSeconds;
                if (visual.DustSeconds > 0f) continue;
                visual.DustSeconds = WalkDustInterval * Range(0.7f, 1.4f);
                if (budget <= 0) continue;
                var feet = visual.Object.transform.position;
                if (!NearCamera(camera, feet, DustCameraDistance)) continue;
                budget--;
                EmitDust(new Vector3(feet.x, 0f, feet.z), 1, 1.8f, WalkDustColor, 0.6f, 1.0f);
            }
        }

        private void AdvanceParticles(float matchSeconds)
        {
            if (particles.Count == 0) return;
            var camera = Camera.main;
            var facing = camera != null ? camera.transform.rotation : Quaternion.identity;
            foreach (var p in particles)
            {
                if (p.Life <= 0f) continue;
                if (matchSeconds > 0f)
                {
                    p.Life -= matchSeconds;
                    if (p.Life <= 0f) { p.Transform.gameObject.SetActive(false); continue; }
                    p.Velocity += Vector3.down * (p.Gravity * matchSeconds);
                    p.Velocity *= Mathf.Max(0f, 1f - p.Drag * matchSeconds);
                    var position = p.Transform.position + p.Velocity * matchSeconds;
                    if (p.Glint && position.y < 0.1f) { position.y = 0.1f; p.Velocity = new Vector3(p.Velocity.x * 0.4f, 0f, p.Velocity.z * 0.4f); }
                    p.Transform.position = position;
                }
                float t = 1f - p.Life / p.MaxLife;
                float size = Mathf.Lerp(p.StartSize, p.EndSize, p.Glint ? t : Mathf.Sqrt(t));
                p.Transform.localScale = new Vector3(size, size, size);
                p.Transform.rotation = facing;
                // Dust swells in, then thins out; a fleck is brightest at first.
                float fade = p.Glint ? 1f - t * t : Mathf.Clamp01(t * 8f + 0.25f) * Mathf.Sqrt(1f - t);
                particleBlock.SetColor("_Color", p.Color);
                particleBlock.SetFloat("_Fade", fade);
                p.Renderer.SetPropertyBlock(particleBlock);
            }
        }

        /// <summary>For the photographs only (-rts-settings-shots): every kind of dust and spark around one point.</summary>
        public void ShowEffectsForShots(Vector3 at)
        {
            if (!ParticlesReady()) return;
            StructureLostParticles(at);
            for (int i = 0; i < 6; i++)
            {
                var spot = at + new Vector3(-12f + i * 2.2f, 0f, 8f);
                EmitDust(spot, 1, 1.8f, WalkDustColor, 0.6f, 1.0f);
                HitParticles(spot + Vector3.up * 1.6f, i % 2 == 0);
            }
            FallParticles(at + new Vector3(8f, 0f, 6f));
            StructureHitParticles(at + new Vector3(-6f, 4f, -4f), false);
        }

        private void ClearParticles()
        {
            foreach (var p in particles) { p.Life = 0f; if (p.Transform != null) p.Transform.gameObject.SetActive(false); }
        }
    }
}
