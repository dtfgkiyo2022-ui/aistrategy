using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-4 ages (technical-design-v3 26, 29). Everyone starts in the primitive age; advancing at the core costs food and
    /// wood, takes AdvanceTicks, stops villager training meanwhile, and ends in the chosen civilisation for good. Mines,
    /// smelters and the metal cost of infantry belong to the metallurgy civilisation. Without Ages nothing here applies.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int CivOreReach = 44, CivFoodReach = 30, GuaranteedFoodPoints = 3, AdvanceVillagers = 8;

        private bool AgesOn => world.Config.Economy.Enabled && world.Config.Economy.Ages;

        /// <summary>Mines and smelters: on an ages map only in the metallurgy civilisation.</summary>
        private bool MetalworkAllowed(uint faction) => !AgesOn || world.Economies[faction - 1].Civ == CivKind.Metallurgy;

        /// <summary>The metal an infantry costs this faction now.</summary>
        private int InfantryMetalFor(uint faction) => MetalworkAllowed(faction) ? world.Config.Economy.InfantryMetalCost : 0;

        private bool CanAdvance(uint faction)
        {
            if (!AgesOn) return false;
            var rules = world.Config.Economy;
            var e = world.Economies[faction - 1];
            return e.Civ == CivKind.Primitive && e.AdvanceRemaining == 0 && e.Queued == 0
                && e.Food >= rules.AdvanceFoodCost && e.Wood >= rules.AdvanceWoodCost;
        }

        private void StartAdvance(uint faction, CivKind civ)
        {
            var rules = world.Config.Economy;
            ref var e = ref world.Economies[faction - 1];
            e.Food = checked(e.Food - rules.AdvanceFoodCost);
            e.Wood = checked(e.Wood - rules.AdvanceWoodCost);
            e.AdvancingTo = civ;
            e.AdvanceRemaining = rules.AdvanceTicks;
        }

        /// <summary>Economy step: the advancing clock; at zero the civilisation is taken.</summary>
        private void AdvanceAges()
        {
            if (!AgesOn) return;
            for (int f = 0; f < 2; f++)
            {
                ref var e = ref world.Economies[f];
                if (e.AdvanceRemaining == 0) continue;
                if (--e.AdvanceRemaining > 0) continue;
                e.Civ = e.AdvancingTo;
                e.AdvancingTo = CivKind.Primitive;
            }
        }

        /// <summary>
        /// The automatic economy advances once the primitive base stands - a finished barracks and AdvanceVillagers
        /// villagers - and saves for it meanwhile (SavingToAdvance). The civilisation follows the ground (29).
        /// </summary>
        private void DecideAdvance(uint faction)
        {
            // The core the player runs by hand (V3-3, 19) is theirs to advance too.
            if (world.Economies[faction - 1].CoreHeld || !SavingToAdvance(faction) || !CanAdvance(faction)) return;
            StartAdvance(faction, ChooseCiv(faction));
        }

        /// <summary>True while the automatic economy keeps food and wood for advancing (no infantry is queued then).</summary>
        private bool SavingToAdvance(uint faction)
        {
            if (!AgesOn) return false;
            var e = world.Economies[faction - 1];
            if (e.Civ != CivKind.Primitive || e.AdvanceRemaining > 0) return false;
            bool barracks = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Barracks) { barracks = true; break; }
            }
            return barracks && LivingVillagers(faction) >= AdvanceVillagers;
        }

        private CivKind ChooseCiv(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int ore = 0, food = 0;
            // The ground, not what is left of it: points gathered empty in the primitive age still count.
            foreach (var node in world.Nodes)
            {
                if (node.Definition.Kind == ResourceKind.Ore && InRange(node.Definition.Position, core, Fix64.FromInt(CivOreReach))) ore++;
                else if (node.Definition.Kind == ResourceKind.Food && InRange(node.Definition.Position, core, Fix64.FromInt(CivFoodReach))) food++;
            }
            return EconomyDecision.ChooseCiv(ore, food, GuaranteedFoodPoints);
        }
    }
}
