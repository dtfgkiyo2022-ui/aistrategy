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
        private const int CivOreReach = 44, CivFoodReach = 30, GuaranteedFoodPoints = 3, AdvanceVillagers = 8, Age2Villagers = 10;

        private bool AgesOn => world.Config.Economy.Enabled && world.Config.Economy.Ages;

        /// <summary>Mines and smelters: on an ages map only in the metallurgy civilisation.</summary>
        private bool MetalworkAllowed(uint faction) => !AgesOn || world.Economies[faction - 1].Civ == CivKind.Metallurgy;

        /// <summary>The metal an infantry costs this faction now.</summary>
        private int InfantryMetalFor(uint faction) => MetalworkAllowed(faction) ? world.Config.Economy.InfantryMetalCost : 0;

        /// <summary>Farms: the agrarian civilisation only.</summary>
        private bool FarmingAllowed(uint faction) => AgesOn && world.Economies[faction - 1].Civ == CivKind.Agrarian;

        private bool Agrarian(uint faction) => AgesOn && world.Economies[faction - 1].Civ == CivKind.Agrarian;

        private int InfantryFoodFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryFood : world.Config.Economy.InfantryFoodCost;
        private int InfantryWoodFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryWood : world.Config.Economy.InfantryWoodCost;
        private int InfantryTicksFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryTicks : world.Config.Economy.InfantryTrainTicks;

        /// <summary>Metallurgy (27): an infantry trained now is born forged - more HP and damage. Soldiers already out stay as they are.</summary>
        private void ForgeIfMetallurgy(uint faction, int index)
        {
            if (!AgesOn || world.Economies[faction - 1].Civ != CivKind.Metallurgy) return;
            var rules = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            s.Parameters.Hp = rules.ForgedInfantryHp;
            s.Parameters.Damage = rules.ForgedInfantryDamage;
            s.Hp = rules.ForgedInfantryHp;
            s.Initial.Hp = rules.ForgedInfantryHp;
        }

        /// <summary>
        /// Ticks per food of a farm on <paramref name="origin"/> (27): FarmStepTicks faster for each food point within
        /// FarmFoodReach of its centre and once more if river lies within FarmRiverReach, never under FarmMinTicks.
        /// </summary>
        private int FarmInterval(int origin)
        {
            var rules = world.Config.Economy;
            var centre = FootprintCenter(origin, rules.FarmSizeCells);
            int steps = 0;
            foreach (var node in world.Nodes)
                if (node.Definition.Kind == ResourceKind.Food && InRange(node.Definition.Position, centre, Fix64.FromInt(rules.FarmFoodReach))) steps++;
            // River: only the cells around the farm can be in reach, so only they are looked at.
            var terrain = world.Config.Map.Terrain;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, cellSize = world.Config.Map.CellSizeMeters;
            int reach = rules.FarmRiverReach / cellSize + rules.FarmSizeCells + 1, ox = origin % width, oz = origin / width;
            bool river = false;
            for (int z = System.Math.Max(0, oz - reach); z <= System.Math.Min(height - 1, oz + reach) && !river; z++)
                for (int x = System.Math.Max(0, ox - reach); x <= System.Math.Min(width - 1, ox + reach) && !river; x++)
                {
                    int cell = z * width + x;
                    if (terrain.Length != 0 && terrain[cell] == (byte)TerrainKind.River && InRange(world.Map.Center(cell), centre, Fix64.FromInt(rules.FarmRiverReach))) river = true;
                }
            if (river) steps++;
            return System.Math.Max(rules.FarmMinTicks, rules.FarmBaseTicks - rules.FarmStepTicks * steps);
        }

        /// <summary>
        /// Advancing into <paramref name="civ"/>: out of the primitive age into either civilisation, or (32 #7) into the
        /// second age of the civilisation already taken. Nothing else is ever possible.
        /// </summary>
        private bool CanAdvance(uint faction, CivKind civ)
        {
            if (!AgesOn) return false;
            var e = world.Economies[faction - 1];
            if (e.AdvanceRemaining != 0 || e.Queued != 0) return false;
            var (food, wood, _) = AdvancePrice(e);
            if (e.Food < food || e.Wood < wood) return false;
            if (e.Civ == CivKind.Primitive) return civ == CivKind.Agrarian || civ == CivKind.Metallurgy;
            return e.Age == 1 && civ == e.Civ;
        }

        private (int food, int wood, int ticks) AdvancePrice(FactionEconomy e)
        {
            var rules = world.Config.Economy;
            return e.Civ == CivKind.Primitive ? (rules.AdvanceFoodCost, rules.AdvanceWoodCost, rules.AdvanceTicks)
                : (rules.Age2FoodCost, rules.Age2WoodCost, rules.Age2Ticks);
        }

        private void StartAdvance(uint faction, CivKind civ)
        {
            ref var e = ref world.Economies[faction - 1];
            var (food, wood, ticks) = AdvancePrice(e);
            e.Food = checked(e.Food - food);
            e.Wood = checked(e.Wood - wood);
            e.AdvancingTo = civ;
            e.AdvanceRemaining = ticks;
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
                e.Age = e.Civ == CivKind.Primitive ? (byte)1 : (byte)2;
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
            var e = world.Economies[faction - 1];
            var civ = e.Civ == CivKind.Primitive ? ChooseCiv(faction) : e.Civ;
            if (e.CoreHeld || !SavingToAdvance(faction) || !CanAdvance(faction, civ)) return;
            StartAdvance(faction, civ);
        }

        /// <summary>True while the automatic economy keeps food and wood for advancing (no infantry is queued then).</summary>
        private bool SavingToAdvance(uint faction)
        {
            if (!AgesOn) return false;
            var e = world.Economies[faction - 1];
            if (e.AdvanceRemaining > 0 || e.Age >= 2) return false;
            // The second age waits for the civilisation's own line, and for the stock to be half way there (32.8).
            if (e.Civ != CivKind.Primitive)
            {
                var rules = world.Config.Economy;
                if (!CivLineStarted(faction) || 2 * (e.Food + e.Wood) < rules.Age2FoodCost + rules.Age2WoodCost) return false;
            }
            bool barracks = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Barracks) { barracks = true; break; }
            }
            return barracks && LivingVillagers(faction) >= (e.Civ == CivKind.Primitive ? AdvanceVillagers : Age2Villagers);
        }

        /// <summary>
        /// True while the automatic economy holds back its cheap groundwork too - houses, drop sites, research, markets.
        /// Only the first step out of the primitive age is worth that: saving for the second age lasts long, and stopping
        /// the groundwork for it cost the side its research and its defence (32.9, measured).
        /// </summary>
        private bool SavingHard(uint faction) => SavingToAdvance(faction) && world.Economies[faction - 1].Civ == CivKind.Primitive;

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
