using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Read-only display of FactionFrame. Interpolates S(t-1) to S(t) over one tick; never writes simulation state.</summary>
    public sealed class BattlefieldView : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float Fix64Scale = 65536f;
        private const float UnitModelScale = 2.2f;
        private const float CoreModelScale = 1.2f;

        [SerializeField] private int coreMaxHp = 3000;
        [SerializeField] private GameObject infantryModel;
        [SerializeField] private GameObject scoutModel;
        [SerializeField] private GameObject coreModel;

        private sealed class Visual
        {
            public GameObject Object;
            public Vector3 From;
            public Vector3 To;
            public Transform HpFill;
            public int Hp;
        }

        private readonly Dictionary<uint, Visual> units = new Dictionary<uint, Visual>();
        private readonly Dictionary<uint, Visual> cores = new Dictionary<uint, Visual>();
        private readonly List<uint> scratch = new List<uint>();
        private readonly Dictionary<uint, Vector3> previousUnitPositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, Vector3> previousCorePositions = new Dictionary<uint, Vector3>();
        private float sinceUpdate;

        public void Push(FactionFrame frame)
        {
            sinceUpdate = 0f;
            SyncUnits(frame);
            SyncCores(frame);
            Apply(0f);
        }

        public void Apply(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            foreach (var visual in units.Values)
                visual.Object.transform.position = Vector3.Lerp(visual.From, visual.To, alpha);
            foreach (var visual in cores.Values)
                visual.Object.transform.position = Vector3.Lerp(visual.From, visual.To, alpha);
            var camera = Camera.main;
            if (camera == null) return;
            foreach (var visual in cores.Values)
                visual.HpFill.parent.rotation = camera.transform.rotation;
        }

        private void Update()
        {
            sinceUpdate += Time.deltaTime;
            Apply(sinceUpdate / TickSeconds);
        }

        private void SyncUnits(FactionFrame frame)
        {
            var present = new HashSet<uint>();
            foreach (var unit in frame.Units)
            {
                present.Add(unit.Id);
                var target = ToWorld(unit.Position, ModelFor(unit.Kind) != null ? 0f : 1.1f);
                if (!units.TryGetValue(unit.Id, out var visual))
                {
                    visual = new Visual { Object = CreateUnitObject(unit), From = target };
                    units.Add(unit.Id, visual);
                }
                else
                {
                    visual.From = visual.Object.transform.position;
                    if (previousUnitPositions.TryGetValue(unit.Id, out var previous)) visual.From = previous;
                }
                visual.To = target;
            }

            scratch.Clear();
            foreach (var pair in units)
                if (!present.Contains(pair.Key)) scratch.Add(pair.Key);
            foreach (var id in scratch)
            {
                Discard(units[id].Object);
                units.Remove(id);
            }

            previousUnitPositions.Clear();
            foreach (var pair in units) previousUnitPositions[pair.Key] = pair.Value.To;
        }

        private void SyncCores(FactionFrame frame)
        {
            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Core) continue;
                var target = ToWorld(objective.Position, coreModel != null ? 0f : 2f);
                if (!cores.TryGetValue(objective.Id, out var visual))
                {
                    visual = CreateCoreObject(objective);
                    visual.From = target;
                    cores.Add(objective.Id, visual);
                }
                else
                {
                    visual.From = previousCorePositions.TryGetValue(objective.Id, out var previous) ? previous : target;
                }
                visual.To = target;
                UpdateHpBar(visual, objective.IsHpKnown ? objective.Hp : coreMaxHp);
            }

            previousCorePositions.Clear();
            foreach (var pair in cores) previousCorePositions[pair.Key] = pair.Value.To;
        }

        private static void Discard(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static Vector3 ToWorld(SimPoint point, float height)
        {
            return new Vector3(point.X.Raw / Fix64Scale, height, point.Z.Raw / Fix64Scale);
        }

        private GameObject ModelFor(UnitKind kind)
        {
            return kind == UnitKind.Scout ? scoutModel : infantryModel;
        }

        private GameObject CreateUnitObject(RenderUnit unit)
        {
            var color = PresentationMaterials.Get(unit.IsOwn ? new Color(0.2f, 0.5f, 1f) : new Color(1f, 0.3f, 0.25f));
            var model = ModelFor(unit.Kind);
            GameObject go;
            if (model != null)
            {
                go = Instantiate(model, transform);
                go.transform.localScale = Vector3.one * UnitModelScale;
                Tint(go, color);
            }
            else
            {
                var type = unit.Kind == UnitKind.Scout ? PrimitiveType.Sphere : PrimitiveType.Capsule;
                go = GameObject.CreatePrimitive(type);
                go.transform.SetParent(transform, false);
                float size = unit.Kind == UnitKind.Scout ? 1.6f : 2.2f;
                go.transform.localScale = new Vector3(size, size, size);
                Discard(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = color;
            }
            go.name = (unit.IsOwn ? "Own_" : "Enemy_") + unit.Id;
            return go;
        }

        private static void Tint(GameObject go, Material material)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = material;
        }

        private Visual CreateCoreObject(KnownObjective objective)
        {
            var root = new GameObject("Core_" + objective.Id);
            root.transform.SetParent(transform, false);
            var color = PresentationMaterials.Get(
                objective.OwnerFactionId == 1 ? new Color(0.1f, 0.3f, 0.8f) : new Color(0.8f, 0.15f, 0.1f));
            float barHeight;
            if (coreModel != null)
            {
                var body = Instantiate(coreModel, root.transform);
                body.transform.localScale = Vector3.one * CoreModelScale;
                Tint(body, color);
                barHeight = 8f;
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(8f, 4f, 8f);
                Discard(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = color;
                barHeight = 4.6f;
            }

            var bar = new GameObject("HpBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, barHeight, 0f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            back.transform.SetParent(bar.transform, false);
            back.transform.localScale = new Vector3(8.4f, 1.9f, 1f);
            Discard(back.GetComponent<Collider>());
            back.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.08f, 0.08f, 0.08f));
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            Discard(fill.GetComponent<Collider>());
            return new Visual { Object = root, HpFill = fill.transform };
        }

        private void UpdateHpBar(Visual visual, int hp)
        {
            float ratio = Mathf.Clamp01(hp / (float)coreMaxHp);
            const float width = 8f;
            visual.HpFill.localScale = new Vector3(width * ratio, 1.5f, 1f);
            visual.HpFill.localPosition = new Vector3(-width * (1f - ratio) / 2f, 0f, -0.01f);
            visual.HpFill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(
                Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.2f), ratio));
            visual.Hp = hp;
        }
    }
}
