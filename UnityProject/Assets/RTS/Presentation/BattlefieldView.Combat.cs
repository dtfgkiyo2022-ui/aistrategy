using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// 11-01 "hard to tell whether damage lands": a hit unit flashes its bar and flinches, sparks at the body, a number
    /// rises when the camera is close, and each fight gets a mark with what both sides lost. All of it is read from the
    /// HP drops between two frames; nothing is written back.
    /// </summary>
    public sealed partial class BattlefieldView
    {
        private const float HitReactSeconds = 0.22f;
        private const float HitBarFlashSeconds = 0.12f;
        private const float HitFlinchMeters = 0.35f;
        /// <summary>A unit that vanishes this long after its last hit counts as fallen in the fight's tally.</summary>
        private const float FallCountSeconds = 6f;
        private const float NumberCameraDistance = 110f;
        private const float SparkCameraDistance = 170f;
        private const float SparkSeconds = 0.25f;
        private const int MaxSparks = 64;

        private sealed class Spark
        {
            public GameObject Object;
            public float Remaining;
            public float Size;
        }

        private readonly CombatFeedback combatFeedback = new CombatFeedback();
        private readonly List<Spark> sparks = new List<Spark>();
        private int nextSpark;
        private float combatClock;
        private GUIStyle combatNumberStyle;
        private GUIStyle combatMarkStyle;
        private static readonly Color OwnHurtColor = new Color(1f, 0.38f, 0.22f, 1f);
        private static readonly Color EnemyHurtColor = new Color(1f, 0.93f, 0.55f, 1f);

        /// <summary>Called for every unit in a pushed frame once its target position is known.</summary>
        private void TrackUnitHp(ulong key, Visual visual, RenderUnit unit, int maxHp)
        {
            if (!unit.HasHp) return;
            visual.HpRatio = maxHp <= 0 ? 0f : Mathf.Clamp01(unit.Hp / (float)maxHp);
            int previous = visual.LastHp;
            visual.LastHp = unit.Hp;
            if (previous < 0 || unit.Hp >= previous) return;
            int damage = previous - unit.Hp;
            visual.HitSeconds = HitReactSeconds;
            visual.LastHitClock = combatClock;
            var body = visual.To + Vector3.up * 1.6f;
            combatFeedback.RegisterHit(key, body, damage, unit.IsOwn);
            SpawnSpark(body, unit.IsOwn, 0.9f);
        }

        /// <summary>A unit left the frame and is shown falling.</summary>
        private void TrackUnitFell(Visual visual)
        {
            if (visual.LastHitClock < 0f || combatClock - visual.LastHitClock > FallCountSeconds) return;
            var position = visual.Object.transform.position;
            combatFeedback.RegisterDeath(position, !visual.IsEnemy);
            SpawnSpark(position + Vector3.up * 0.8f, !visual.IsEnemy, 1.6f);
        }

        private void AdvanceCombatFeedback(float matchSeconds)
        {
            if (matchSeconds <= 0f) return;
            combatClock += matchSeconds;
            combatFeedback.Advance(matchSeconds);
            foreach (var visual in units.Values)
                if (visual.HitSeconds > 0f) visual.HitSeconds = Mathf.Max(0f, visual.HitSeconds - matchSeconds);
            var camera = Camera.main;
            foreach (var spark in sparks)
            {
                if (spark.Remaining <= 0f) continue;
                spark.Remaining -= matchSeconds;
                if (spark.Remaining <= 0f) { spark.Object.SetActive(false); continue; }
                float scale = spark.Size * Mathf.Sin(Mathf.PI * (1f - spark.Remaining / SparkSeconds));
                spark.Object.transform.localScale = new Vector3(scale, scale, scale);
                if (camera != null) spark.Object.transform.rotation = camera.transform.rotation;
            }
        }

        /// <summary>After the interpolated position is set: push the hit unit back a little and squash it.</summary>
        private void ApplyHitReaction(Visual visual)
        {
            if (visual.HitSeconds <= 0f && !visual.Flashing && !visual.Squashed) return;
            if (visual.IsDying) visual.HitSeconds = 0f;
            float k = visual.HitSeconds / HitReactSeconds;
            visual.Squashed = k > 0f;
            if (k > 0f)
            {
                var back = Quaternion.Euler(0f, visual.FacingY, 0f) * Vector3.back;
                visual.Object.transform.position += back * (HitFlinchMeters * k);
                visual.Object.transform.localScale = new Vector3(
                    visual.BaseScale.x * (1f + 0.10f * k), visual.BaseScale.y * (1f - 0.16f * k), visual.BaseScale.z * (1f + 0.10f * k));
            }
            else
            {
                visual.Object.transform.localScale = visual.BaseScale;
            }
            bool flash = visual.HitSeconds > HitReactSeconds - HitBarFlashSeconds;
            if (visual.HealthBar != null && visual.HealthBar.Fill != null && flash != visual.Flashing)
            {
                WorldHealthBarVisuals.Flash(visual.HealthBar, flash, visual.HpRatio);
            }
            visual.Flashing = flash;
        }

        private void SpawnSpark(Vector3 position, bool ownVictim, float size)
        {
            var camera = Camera.main;
            if (camera == null || (camera.transform.position - position).sqrMagnitude > SparkCameraDistance * SparkCameraDistance) return;
            Spark spark = null;
            if (sparks.Count < MaxSparks)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "HitSpark";
                quad.transform.SetParent(transform, false);
                Discard(quad.GetComponent<Collider>());
                spark = new Spark { Object = quad };
                sparks.Add(spark);
            }
            else
            {
                // The pool is full: reuse the oldest in turn, so a big battle never grows it.
                spark = sparks[nextSpark];
                nextSpark = (nextSpark + 1) % MaxSparks;
            }
            spark.Object.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(ownVictim ? OwnHurtColor : EnemyHurtColor);
            spark.Object.transform.position = position;
            spark.Object.transform.rotation = camera.transform.rotation;
            spark.Object.transform.localScale = Vector3.zero;
            spark.Object.SetActive(true);
            spark.Remaining = SparkSeconds;
            spark.Size = size;
        }

        private void ClearCombatFeedback()
        {
            combatFeedback.Clear();
            foreach (var spark in sparks) { spark.Remaining = 0f; if (spark.Object != null) spark.Object.SetActive(false); }
        }

        private void DrawCombatGui(Camera camera)
        {
            if (combatNumberStyle == null)
            {
                combatNumberStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                combatMarkStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            }
            var saved = GUI.color;
            foreach (var number in combatFeedback.Numbers)
            {
                if ((camera.transform.position - number.Position).sqrMagnitude > NumberCameraDistance * NumberCameraDistance) continue;
                var world = number.Position + Vector3.up * (1.4f + number.Age * 2.2f);
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;
                var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
                if (UiHitAreas.Shared.ContainsGui(guiPoint)) continue;
                float alpha = 1f - Mathf.Clamp01((number.Age - CombatFeedback.NumberSeconds * 0.5f) / (CombatFeedback.NumberSeconds * 0.5f));
                var rect = new Rect(guiPoint.x - 30f, guiPoint.y - 10f, 60f, 20f);
                string text = "-" + number.Amount;
                GUI.color = new Color(0f, 0f, 0f, 0.8f * alpha);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, combatNumberStyle);
                var color = number.OwnVictim ? OwnHurtColor : EnemyHurtColor;
                GUI.color = new Color(color.r, color.g, color.b, alpha);
                GUI.Label(rect, text, combatNumberStyle);
            }
            GUI.color = saved;
            foreach (var battle in combatFeedback.MarkedBattles())
            {
                var screen = camera.WorldToScreenPoint(battle.Center + Vector3.up * 5f);
                string text = UiText.CombatMark(battle.EnemyLost, battle.OwnLost);
                var size = combatMarkStyle.CalcSize(new GUIContent(text));
                bool onScreen = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
                Vector2 point;
                if (onScreen)
                {
                    point = new Vector2(screen.x, Screen.height - screen.y);
                }
                else
                {
                    // Off screen: keep the mark on the edge in the fight's direction, so a fight elsewhere is not missed.
                    var direction = new Vector2(screen.x - Screen.width * 0.5f, screen.y - Screen.height * 0.5f);
                    if (screen.z <= 0f) direction = -direction;
                    if (direction.sqrMagnitude < 1f) direction = Vector2.up;
                    float halfW = Screen.width * 0.5f - size.x * 0.5f - 12f, halfH = Screen.height * 0.5f - size.y - 60f;
                    float scale = Mathf.Min(halfW / Mathf.Max(1f, Mathf.Abs(direction.x)), halfH / Mathf.Max(1f, Mathf.Abs(direction.y)));
                    var edge = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + direction * scale;
                    point = new Vector2(edge.x, Screen.height - edge.y);
                }
                var rect = new Rect(point.x - size.x * 0.5f - 4f, point.y - size.y * 0.5f, size.x + 8f, size.y + 2f);
                if (UiHitAreas.Shared.ContainsGui(rect.center)) continue;
                GUI.Box(rect, text, combatMarkStyle);
            }
        }
    }
}
