using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 training queues (technical-design-v3 32). A barracks queue holds unit kinds in order; without ages the queue
    /// has no kinds and every entry is infantry, exactly as in V3-1 to V3-4. Costs, times and room are asked per kind
    /// here, so a new unit kind is one more case in these methods.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Food, wood and metal a unit of <paramref name="kind"/> costs this faction now, and its training ticks.</summary>
        private (int food, int wood, int metal, int gems, int gold, int ticks) CostOf(uint faction, UnitKind kind)
        {
            var e = world.Config.Economy;
            if (kind == UnitKind.Scout) return (e.ScoutFoodCost, TrainWoodCostFor(faction, kind, e.ScoutWoodCost), 0, 0, 0, e.ScoutTrainTicks);
            if (kind == UnitKind.Archer) return (e.ArcherFood, TrainWoodCostFor(faction, kind, e.ArcherWood), 0, 0, 0, e.ArcherTicks);
            if (kind == UnitKind.Cavalry) return (e.CavalryFood, TrainWoodCostFor(faction, kind, e.CavalryWood), e.CavalryMetal, 0, 0, CavalryTrainTicks(faction, e.CavalryTicks));
            if (kind == UnitKind.LightCavalry) return (e.LightCavalryFood, TrainWoodCostFor(faction, kind, e.LightCavalryWood), 0, 0, 0, CavalryTrainTicks(faction, e.LightCavalryTicks));
            if (kind == UnitKind.HeavyCavalry) return (e.HeavyCavalryFoodCost, TrainWoodCostFor(faction, kind, 0), 0, 0, 0, CavalryTrainTicks(faction, e.HeavyCavalryTrainTicks));
            if (kind == UnitKind.Ram) return (e.RamFood, TrainWoodCostFor(faction, kind, e.RamWood), 0, 0, 0, RamTicksFor(faction));
            if (kind == UnitKind.Cannon) return (e.CannonFoodCost, TrainWoodCostFor(faction, kind, e.CannonWoodCost), 0, 0, 0, e.RamTicks);
            if (kind == UnitKind.Mercenary) return (0, 0, 0, AgeStrength(faction, CivKind.Mountain, 3) ? e.MountainMercenaryGems : e.MercenaryGems, 0, e.MercenaryTicks);
            if (kind == UnitKind.Monk)
                return e.GoldEnabled ? (e.MonkFoodCost, 0, 0, 0, e.MonkGoldCost, e.MonkTrainTicks)
                    : (e.MonkFoodCost, 0, e.MonkGoldCost, 0, 0, e.MonkTrainTicks);
            // V3-6: heavy infantry pays steel in the metal slot (CanPay, Enqueue and the queue book it as steel).
            if (kind == UnitKind.HeavyInfantry) return (e.HeavyInfantryFoodCost, TrainWoodCostFor(faction, kind, e.HeavyInfantryWoodCost), e.HeavyInfantrySteelCost, 0, 0, e.HeavyInfantryTrainTicks);
            if (kind == UnitKind.SkirmishArcher) return (e.SkirmishArcherFoodCost, 0, e.SkirmishArcherBowGearCost, 0, 0, e.SkirmishArcherTrainTicks);
            return (InfantryFoodFor(faction), TrainWoodCostFor(faction, kind, InfantryWoodFor(faction)), InfantryMetalFor(faction), 0, 0, InfantryTicksFor(faction));
        }

        private (int food, int wood, int metal, int gems, int gold, int ticks) CostOf(uint faction, UnitKind kind, BuildingKind producer)
        {
            if (producer == BuildingKind.Barracks && kind == UnitKind.Mercenary && JewelsAllowed(faction)
                && world.Economies[faction - 1].Age >= 2)
            {
                var mountain = world.Config.Economy;
                return (mountain.JewelMercenaryFood, TrainWoodCostFor(faction, kind, 0), 0, AgeStrength(faction, CivKind.Mountain, 3) ? mountain.MountainMercenaryGems : mountain.JewelMercenaryGems, 0, mountain.MercenaryTicks);
            }
            if (producer == BuildingKind.Monastery && kind == UnitKind.Monk && CultAllowed(faction))
            {
                var e = world.Config.Economy;
                return (e.MonasteryMonkFoodCost, TrainWoodCostFor(faction, kind, e.MonasteryMonkWoodCost), 0, 0, 0, e.MonkTrainTicks);
            }
            return CostOf(faction, kind);
        }

        /// <summary>What a barracks can train: infantry always, scouts on a map with ages.</summary>
        private bool Trains(BuildingState b, UnitKind kind)
        {
            if (b.Kind == BuildingKind.Town) return false;
            if (b.Kind == BuildingKind.Monastery) return kind == UnitKind.Monk && CultAllowed(b.FactionId);
            // V3-5 (32 #9): the siege workshop trains rams, and only rams.
            if (b.Kind == BuildingKind.SiegeWorkshop) return (kind == UnitKind.Ram || kind == UnitKind.Cannon) && AgesOn && world.Economies[b.FactionId - 1].Age >= 2
                && (kind != UnitKind.Cannon || CraftTree4Allowed(b.FactionId));
            // V3-5 (32 #12): the range and the stable train their unit in either civilisation, from the second age.
            if (b.Kind == BuildingKind.ArcheryRange)
                return kind == UnitKind.Archer && AgesOn
                    && (world.Economies[b.FactionId - 1].Age >= 2 || EarlyArmsAllowed(b.FactionId) && EarlyArmsUnit(b.FactionId) == UnitKind.Archer);
            if (b.Kind == BuildingKind.Stable)
                return (kind == UnitKind.Cavalry && AgesOn
                        && (world.Economies[b.FactionId - 1].Age >= 2 || EarlyArmsAllowed(b.FactionId) && EarlyArmsUnit(b.FactionId) == UnitKind.Cavalry))
                    || (kind == UnitKind.LightCavalry && world.Economies[b.FactionId - 1].Age >= 1
                        && (CavalryAllowed(b.FactionId) || EarlyArmsAllowed(b.FactionId) && EarlyArmsUnit(b.FactionId) == UnitKind.LightCavalry))
                    || (kind == UnitKind.HeavyCavalry && TackAllowed(b.FactionId));
            // V3-5 (32 #17): a castle trains any of the three line units, whatever the civilisation.
            if (b.Kind == BuildingKind.Castle)
                return AgesOn && world.Economies[b.FactionId - 1].Age >= 3
                    && (kind == UnitKind.Infantry || kind == UnitKind.Archer || kind == UnitKind.Cavalry || kind == UnitKind.Mercenary);
            if (b.Kind != BuildingKind.Barracks) return false;
            if (kind == UnitKind.Infantry) return true;
            if (kind == UnitKind.Monk) return world.Config.Economy.MonksEnabled;
            if (!AgesOn) return false;
            var e = world.Economies[b.FactionId - 1];
            return kind == UnitKind.Scout
                || (kind == UnitKind.Archer && e.Civ == CivKind.Agrarian && e.Age >= 2)
                || (kind == UnitKind.Cavalry && e.Civ == CivKind.Metallurgy && e.Age >= 2)
                || (kind == UnitKind.Mercenary && JewelsAllowed(b.FactionId) && e.Age >= 2)
                || (kind == UnitKind.HeavyInfantry && ProcessingAvailable(b.FactionId))
                || (kind == UnitKind.SkirmishArcher && ForestryAllowed(b.FactionId) && e.Age >= 2);
        }

        /// <summary>The civilisation's own unit once it is in its second age (archers or cavalry), else 0.</summary>
        private UnitKind SpecialUnit(uint faction)
        {
            var e = world.Economies[faction - 1];
            if (!AgesOn || e.Age < 2) return 0;
            return e.Civ == CivKind.Agrarian ? UnitKind.Archer : e.Civ == CivKind.Metallurgy ? UnitKind.Cavalry : 0;
        }

        /// <summary>
        /// One cross-building unit for each civilisation in the first civilisation age: archers or light cavalry, about
        /// half each. The riders are light cavalry, not cavalry: cavalry costs metal, which most civilisations cannot make
        /// in their first age (measured 10-10: a stable stood idle all match). Cavalry proper still comes with the second age.
        /// </summary>
        private UnitKind EarlyArmsUnit(uint faction)
        {
            if (!EarlyArmsOn) return 0;
            switch (world.Economies[faction - 1].Civ)
            {
                case CivKind.Agrarian: return UnitKind.LightCavalry;
                case CivKind.Metallurgy: return UnitKind.Archer;
                case CivKind.Forestry: return UnitKind.Archer;
                case CivKind.Masonry: return UnitKind.LightCavalry;
                case CivKind.Caravan: return UnitKind.LightCavalry;
                case CivKind.Cavalry: return UnitKind.LightCavalry;
                case CivKind.Bridge: return UnitKind.Archer;
                case CivKind.Academy: return UnitKind.LightCavalry;
                case CivKind.Cult: return UnitKind.Archer;
                case CivKind.Fishing: return UnitKind.LightCavalry;
                case CivKind.Mountain: return UnitKind.Archer;
                case CivKind.Tollgate: return UnitKind.LightCavalry;
                case CivKind.Metropolis: return UnitKind.Archer;
                case CivKind.Sanctuary: return UnitKind.LightCavalry;
                default: return 0;
            }
        }

        private bool EarlyArmsAllowed(uint faction)
            => EarlyArmsOn && world.Economies[faction - 1].Age >= 1 && EarlyArmsUnit(faction) != 0;

        /// <summary>Archers and cavalry fight as infantry with their own numbers (32 #8); it is what they were trained as.</summary>
        private void ApplyClass(int index, UnitKind unit)
        {
            if (unit == UnitKind.Cannon)
            {
                var r = world.Config.Economy;
                ref var cannon = ref world.Soldiers[index];
                cannon.Class = unit;
                cannon.Parameters.Hp = r.CannonHp; cannon.Parameters.Damage = r.CannonDamage; cannon.Parameters.AttackIntervalTicks = r.CannonInterval;
                cannon.Parameters.Range = r.CannonRange; cannon.Parameters.Speed = r.CannonSpeed; cannon.Parameters.Vision = r.CannonVision;
                cannon.StepDistance = MovementMath.StepDistance(r.CannonSpeed, r);
                cannon.Hp = r.CannonHp; cannon.Initial.Hp = r.CannonHp;
                return;
            }
            if (unit == UnitKind.Ram)
            {
                var r = world.Config.Economy;
                ref var ram = ref world.Soldiers[index];
                ram.Class = unit;
                ram.Parameters.Hp = r.RamHp; ram.Parameters.Damage = r.RamDamage; ram.Parameters.AttackIntervalTicks = r.RamInterval;
                ram.Parameters.Range = r.RamRange; ram.Parameters.Speed = r.RamSpeed; ram.Parameters.Vision = r.RamVision;
                ram.StepDistance = MovementMath.StepDistance(ram.Parameters.Speed, r);
                ram.Hp = r.RamHp; ram.Initial.Hp = r.RamHp;
                return;
            }
            if (unit == UnitKind.Mercenary)
            {
                var r = world.Config.Economy;
                ref var mercenary = ref world.Soldiers[index];
                mercenary.Class = unit;
                mercenary.Parameters.Hp = r.MercenaryHp; mercenary.Parameters.Damage = r.MercenaryDamage;
                mercenary.Parameters.AttackIntervalTicks = r.MercenaryInterval;
                if (AgeStrength(mercenary.Initial.FactionId, CivKind.Mountain, 4)) mercenary.Parameters.Damage = checked(mercenary.Parameters.Damage + r.MountainMercenaryDamageBonus);
                mercenary.Hp = r.MercenaryHp; mercenary.Initial.Hp = r.MercenaryHp;
                return;
            }
            if (unit == UnitKind.HeavyInfantry)
            {
                var r = world.Config.Economy;
                ref var heavy = ref world.Soldiers[index];
                heavy.Class = unit;
                heavy.Parameters.Hp = r.HeavyInfantryHp; heavy.Parameters.Damage = r.HeavyInfantryDamage;
                heavy.Parameters.AttackIntervalTicks = r.HeavyInfantryAttackIntervalTicks;
                heavy.Parameters.Range = r.HeavyInfantryRange; heavy.Parameters.Speed = r.HeavyInfantrySpeed;
                heavy.Parameters.Vision = r.HeavyInfantryVision;
                heavy.StepDistance = MovementMath.StepDistance(heavy.Parameters.Speed, r);
                heavy.Hp = heavy.Parameters.Hp; heavy.Initial.Hp = heavy.Parameters.Hp;
                return;
            }
            if (unit == UnitKind.SkirmishArcher)
            {
                var r = world.Config.Economy;
                ref var skirmisher = ref world.Soldiers[index];
                skirmisher.Class = unit;
                skirmisher.Parameters.Hp = r.SkirmishArcherHp; skirmisher.Parameters.Damage = r.SkirmishArcherDamage;
                skirmisher.Parameters.AttackIntervalTicks = r.SkirmishArcherAttackIntervalTicks;
                skirmisher.Parameters.Range = r.SkirmishArcherRange; skirmisher.Parameters.Speed = r.SkirmishArcherSpeed;
                skirmisher.Parameters.Vision = r.SkirmishArcherVision;
                skirmisher.StepDistance = MovementMath.StepDistance(skirmisher.Parameters.Speed, r);
                skirmisher.Hp = skirmisher.Parameters.Hp; skirmisher.Initial.Hp = skirmisher.Parameters.Hp;
                return;
            }
            if (unit == UnitKind.LightCavalry)
            {
                var r = world.Config.Economy;
                ref var light = ref world.Soldiers[index];
                light.Class = unit;
                light.Parameters.Hp = r.LightCavalryHp; light.Parameters.Damage = r.LightCavalryDamage;
                light.Parameters.AttackIntervalTicks = r.LightCavalryInterval;
                light.Parameters.Range = r.LightCavalryRange; light.Parameters.Speed = r.LightCavalrySpeed;
                light.Parameters.Vision = r.LightCavalryVision;
                light.StepDistance = MovementMath.StepDistance(light.Parameters.Speed, r);
                light.Hp = light.Parameters.Hp; light.Initial.Hp = light.Parameters.Hp;
                return;
            }
            if (unit == UnitKind.HeavyCavalry)
            {
                var r = world.Config.Economy;
                ref var heavy = ref world.Soldiers[index];
                heavy.Class = unit;
                heavy.Parameters.Hp = r.HeavyCavalryHp; heavy.Parameters.Damage = r.HeavyCavalryDamage;
                heavy.Parameters.AttackIntervalTicks = r.HeavyCavalryAttackIntervalTicks;
                heavy.Parameters.Range = r.HeavyCavalryRange; heavy.Parameters.Speed = r.HeavyCavalrySpeed;
                heavy.Parameters.Vision = r.HeavyCavalryVision;
                if (AgeStrength(heavy.Initial.FactionId, CivKind.Cavalry, 3))
                    heavy.Parameters.Speed = Fix64.FromRaw(checked(heavy.Parameters.Speed.Raw * r.CavalrySpeedPermille / 1000));
                if (AgeStrength(heavy.Initial.FactionId, CivKind.Cavalry, 4))
                    ScaleSoldierHp(ref heavy, r.CavalryHeavyHpPermille);
                heavy.StepDistance = MovementMath.StepDistance(heavy.Parameters.Speed, r);
                heavy.Hp = heavy.Parameters.Hp; heavy.Initial.Hp = heavy.Parameters.Hp;
                return;
            }
            if (unit != UnitKind.Archer && unit != UnitKind.Cavalry) return;
            var e = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            bool archer = unit == UnitKind.Archer;
            s.Class = unit;
            s.Parameters.Hp = archer ? e.ArcherHp : e.CavalryHp;
            s.Parameters.Damage = archer ? e.ArcherDamage : e.CavalryDamage;
            s.Parameters.AttackIntervalTicks = archer ? e.ArcherInterval : e.CavalryInterval;
            s.Parameters.Range = archer ? e.ArcherRange : e.CavalryRange;
            s.Parameters.Speed = archer ? e.ArcherSpeed : e.CavalrySpeed;
            if (!archer && AgeStrength(s.Initial.FactionId, CivKind.Cavalry, 3))
                s.Parameters.Speed = Fix64.FromRaw(checked(s.Parameters.Speed.Raw * e.CavalrySpeedPermille / 1000));
            if (archer && AgeStrength(s.Initial.FactionId, CivKind.Forestry, 3))
                s.Parameters.Range = Fix64.FromRaw(checked(s.Parameters.Range.Raw + Fix64.FromInt(e.ForestryArcherRangeBonus).Raw));
            s.Parameters.Vision = archer ? e.ArcherVision : e.CavalryVision;
            s.StepDistance = MovementMath.StepDistance(s.Parameters.Speed, e);
            s.Hp = s.Parameters.Hp;
            s.Initial.Hp = s.Parameters.Hp;
        }

        private static UnitKind QueueAt(BuildingState b, int i) => b.QueueKinds == null || i >= b.QueueKinds.Length ? UnitKind.Infantry : b.QueueKinds[i];
        private static bool QueueRationsAt(BuildingState b, int i) => b.QueueRations != null && i >= 0 && i < b.QueueRations.Length && b.QueueRations[i];
        private static bool QueuePlanksAt(BuildingState b, int i) => b.QueuePlanks != null && i >= 0 && i < b.QueuePlanks.Length && b.QueuePlanks[i];
        private static bool QueueCharmsAt(BuildingState b, int i) => b.QueueCharms != null && i >= 0 && i < b.QueueCharms.Length && b.QueueCharms[i];
        private static bool QueueToolsAt(BuildingState b, int i) => b.QueueTools != null && i >= 0 && i < b.QueueTools.Length && b.QueueTools[i];
        private static bool QueueJerkyAt(BuildingState b, int i) => b.QueueJerky != null && i >= 0 && i < b.QueueJerky.Length && b.QueueJerky[i];
        private static bool QueueArmsAt(BuildingState b, int i) => b.QueueArmsKit != null && i >= 0 && i < b.QueueArmsKit.Length && b.QueueArmsKit[i];
        private static bool QueueFineArmsAt(BuildingState b, int i) => b.QueueFineArmsKit != null && i >= 0 && i < b.QueueFineArmsKit.Length && b.QueueFineArmsKit[i];
        private static bool QueueSiegePartsAt(BuildingState b, int i) => b.QueueSiegeParts != null && i >= 0 && i < b.QueueSiegeParts.Length && b.QueueSiegeParts[i];
        private static bool QueueEliteKitAt(BuildingState b, int i) => b.QueueEliteKit != null && i >= 0 && i < b.QueueEliteKit.Length && b.QueueEliteKit[i];
        private static bool QueueSiegeEngineAt(BuildingState b, int i) => b.QueueSiegeEngine != null && i >= 0 && i < b.QueueSiegeEngine.Length && b.QueueSiegeEngine[i];
        private static bool QueueToolsAt(FactionEconomy economy, int i)
            => economy.QueueTools != null && i >= 0 && i < economy.QueueTools.Length && economy.QueueTools[i];
        private static bool QueueStoneToolsAt(FactionEconomy economy, int i)
            => economy.QueueStoneTools != null && i >= 0 && i < economy.QueueStoneTools.Length && economy.QueueStoneTools[i];

        private void EnqueueCoreVillager(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            bool tool = CanUseToolForVillager(faction);
            bool stoneTool = !tool && CanUseStoneToolsForVillager(faction);
            int food = VillagerFoodCostFor(faction, tool);
            if (ArmyRecoveryWouldConsumeReserve(faction, ResourceKind.Food, food)) return;
            if (NoNegativeStockOn && economy.Food < food) return;
            economy.Food = checked(economy.Food - food);
            if (tool) { economy.Tools--; economy.ToolsConsumed++; economy.ToolsTrainedVillagers++; economy.ToolsSavedFood = checked(economy.ToolsSavedFood + VillagerFoodCostFor(faction, false) - food); }
            if (economy.Queued == 0) economy.TrainRemaining = VillagerTrainTicksFor(faction, tool);
            economy.Queued++;
            if (stoneTool) { economy.StoneTools--; economy.StoneToolsConsumed++; economy.StoneToolsTrainedVillagers++; }
            if (ToolsOn)
            {
                var tools = economy.QueueTools ?? Array.Empty<bool>();
                Array.Resize(ref tools, tools.Length + 1); tools[tools.Length - 1] = tool; economy.QueueTools = tools;
            }
            if (PrimitiveCraftOn)
            {
                var stoneTools = economy.QueueStoneTools ?? Array.Empty<bool>();
                Array.Resize(ref stoneTools, stoneTools.Length + 1); stoneTools[stoneTools.Length - 1] = stoneTool; economy.QueueStoneTools = stoneTools;
            }
        }

        private void EnqueueCoreVillagerBatch(uint faction)
        {
            for (int i = 0; i < world.Config.Economy.LargeVillagerBatch; i++) EnqueueCoreVillager(faction);
        }

        private void CancelCoreVillager(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Queued == 0) return;
            bool tool = QueueToolsAt(economy, economy.Queued - 1);
            bool stoneTool = QueueStoneToolsAt(economy, economy.Queued - 1);
            economy.Queued--;
            economy.Food = checked(economy.Food + VillagerFoodCostFor(faction, tool));
            if (tool) { economy.Tools++; economy.ToolsConsumed--; economy.ToolsTrainedVillagers--; economy.ToolsSavedFood = checked(economy.ToolsSavedFood - (VillagerFoodCostFor(faction, false) - VillagerFoodCostFor(faction, true))); }
            if (stoneTool) { economy.StoneTools++; economy.StoneToolsConsumed--; economy.StoneToolsTrainedVillagers--; }
            if (economy.QueueTools != null && economy.QueueTools.Length > 0) Array.Resize(ref economy.QueueTools, economy.QueueTools.Length - 1);
            if (economy.QueueStoneTools != null && economy.QueueStoneTools.Length > 0) Array.Resize(ref economy.QueueStoneTools, economy.QueueStoneTools.Length - 1);
            if (economy.Queued == 0) economy.TrainRemaining = 0;
        }
        private bool UsesRations(uint faction, UnitKind kind)
            => RationsAllowed(faction) && kind != UnitKind.Villager && kind != UnitKind.Scout;
        private bool UsesPlanks(uint faction, UnitKind kind)
            => PlanksAllowed(faction) && kind == UnitKind.Ram;
        private bool UsesCharms(uint faction, UnitKind kind)
            => CharmsAllowed(faction) && kind != UnitKind.Villager && kind != UnitKind.Monk;
        private bool UsesArmsKit(UnitKind kind)
            => kind != UnitKind.Villager && kind != UnitKind.Scout && kind != UnitKind.Monk;
        private bool TrainNeedsArms(uint faction, UnitKind kind)
            => CraftRequiredOn && world.Economies[faction - 1].Age >= 2 && UsesArmsKit(kind);
        private int TrainWoodCostFor(uint faction, UnitKind kind, int normalWoodCost)
            => TrainNeedsArms(faction, kind) ? 0 : normalWoodCost;
        private bool HasTrainingArms(uint faction, UnitKind kind)
        {
            if (!TrainNeedsArms(faction, kind)) return true;
            var e = world.Economies[faction - 1];
            bool available = (CraftTree4On && kind != UnitKind.Cannon && e.EliteKit >= 1)
                || (CraftTree2On && e.FineArmsKit >= 1)
                || (CraftTree2On && e.ArmsKit >= 1);
            if (!available) RecordTrainingArmsUnavailable(faction);
            return available;
        }
        private int TrainTicksFor(uint faction, UnitKind kind, int ticks, bool rationed)
        {
            return TrainTicksFor(faction, kind, ticks, rationed, false);
        }
        private int TrainTicksFor(uint faction, UnitKind kind, int ticks, bool rationed, bool planked)
        {
            if (rationed) ticks = Math.Max(1, checked(ticks * world.Config.Economy.RationsTrainPermille / 1000));
            if (planked) ticks = Math.Max(1, checked(ticks * world.Config.Economy.PlankRamTrainPermille / 1000));
            return ticks;
        }

        private int CavalryTrainTicks(uint faction, int ticks)
            => (AgeStrength(faction, CivKind.Cavalry, 2) && ticks > 0)
                ? Math.Max(1, checked((ticks * world.Config.Economy.CavalryStableTimePermille + 999) / 1000)) : ticks;

        private bool CanPay(uint faction, UnitKind kind) => CanPay(faction, kind, BuildingKind.Barracks);

        private bool CanPay(uint faction, UnitKind kind, BuildingKind producer)
        {
            if (kind == UnitKind.Monk && !world.Config.Economy.MonksEnabled) return false;
            var e = world.Economies[faction - 1];
            var c = CostOf(faction, kind, producer);
            if (!HasTrainingArms(faction, kind)) return false;
            int availableSteel = kind == UnitKind.HeavyInfantry ? e.Steel : kind == UnitKind.SkirmishArcher ? e.BowGear : e.Metal;
            int heldWood = automaticEconomyAction && BuildingReservationsOn ? ReservedConstructionWood(faction) : 0;
            // The reserve protects the purchase of the cannon itself; do not count it twice
            // when CanPay is checking that cannon.
            heldWood = checked(heldWood + (automaticEconomyAction && kind != UnitKind.Cannon ? AgeFourAiWoodReserve(faction) : 0));
            if (automaticEconomyAction && AiExpandTownPending(faction) && kind != UnitKind.Villager && kind != UnitKind.Monk
                && e.Wood - c.wood < TownWoodCostFor(faction)) return false;
            return e.Food >= c.food && e.Wood - heldWood >= c.wood && availableSteel >= c.metal && e.Gems >= c.gems && e.Gold >= c.gold
                && (kind != UnitKind.Cannon || e.SiegeEngine >= world.Config.Economy.CannonSiegeEngineCost)
                && (kind != UnitKind.HeavyCavalry || TackAllowed(faction) && e.Tack >= world.Config.Economy.HeavyCavalryTackCost);
        }

        /// <summary>Pays and puts one unit at the back of the queue. The caller checked room, cost and the queue limit.</summary>
        private void Enqueue(uint faction, ref BuildingState b, UnitKind kind)
        {
            var c = CostOf(faction, kind, b.Kind);
            ref var e = ref world.Economies[faction - 1];
            e.Food = checked(e.Food - c.food);
            e.Wood = checked(e.Wood - c.wood);
            RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Infantry, c.wood);
            if (kind == UnitKind.HeavyInfantry)
            {
                e.Steel = checked(e.Steel - c.metal);
                b.QueuedSteel = checked(b.QueuedSteel + c.metal);
            }
            else if (kind == UnitKind.SkirmishArcher)
            {
                e.BowGear = checked(e.BowGear - c.metal);
                b.QueuedBowGear = checked(b.QueuedBowGear + c.metal);
            }
            else
            {
                e.Metal = checked(e.Metal - c.metal);
                if (AiFactoryOn) e.MetalConsumed = checked(e.MetalConsumed + c.metal);
                b.QueuedMetal = checked(b.QueuedMetal + c.metal);
            }
            e.Gems = checked(e.Gems - c.gems);
            e.Gold = checked(e.Gold - c.gold);
            bool siegeEngine = CraftTree4On && kind == UnitKind.Cannon && e.SiegeEngine >= world.Config.Economy.CannonSiegeEngineCost;
            if (siegeEngine)
            {
                e.SiegeEngine = checked(e.SiegeEngine - world.Config.Economy.CannonSiegeEngineCost);
                e.SiegeEngineConsumed = checked(e.SiegeEngineConsumed + world.Config.Economy.CannonSiegeEngineCost);
                b.QueuedSiegeEngine = checked(b.QueuedSiegeEngine + world.Config.Economy.CannonSiegeEngineCost);
            }
            if (kind == UnitKind.HeavyCavalry)
            {
                e.Tack = checked(e.Tack - world.Config.Economy.HeavyCavalryTackCost);
                e.TackConsumed = checked(e.TackConsumed + world.Config.Economy.HeavyCavalryTackCost);
            }
            b.QueuedGems = checked(b.QueuedGems + c.gems);
            b.QueuedGold = checked(b.QueuedGold + c.gold);
            bool rationed = UsesRations(faction, kind) && e.Rations >= world.Config.Economy.RationsPerSoldier;
            if (rationed)
            {
                e.Rations = checked(e.Rations - world.Config.Economy.RationsPerSoldier);
                e.RationsConsumed = checked(e.RationsConsumed + world.Config.Economy.RationsPerSoldier);
            }
            if (JewelsAllowed(faction) && b.Kind == BuildingKind.Barracks && kind == UnitKind.Mercenary)
            {
                e.JewelsConsumed = checked(e.JewelsConsumed + c.gems);
                e.JewelsTrainedMercenaries++;
            }
            bool planked = UsesPlanks(faction, kind) && e.Planks >= world.Config.Economy.PlanksPerRam;
            if (planked)
            {
                e.Planks = checked(e.Planks - world.Config.Economy.PlanksPerRam);
                e.PlanksConsumed = checked(e.PlanksConsumed + world.Config.Economy.PlanksPerRam);
                e.PlanksConsumedRams = checked(e.PlanksConsumedRams + world.Config.Economy.PlanksPerRam);
                RecordCraftTracePlanks(faction, CraftTraceOther, world.Config.Economy.PlanksPerRam);
            }
            bool charmed = UsesCharms(faction, kind) && e.Charms >= 1;
                if (charmed)
                {
                e.Charms = checked(e.Charms - 1);
                e.CharmsConsumed = checked(e.CharmsConsumed + 1);
                }
            bool jerky = PrimitiveCraftOn && kind != UnitKind.Villager && kind != UnitKind.Scout && kind != UnitKind.Monk && e.Jerky > 0;
            if (jerky) { e.Jerky--; e.JerkyConsumed++; e.JerkyTrainedSoldiers++; }
            bool elite = CraftTree4On && kind != UnitKind.Cannon && UsesArmsKit(kind) && e.EliteKit >= 1;
            bool fineArms = CraftTree2On && !elite && UsesArmsKit(kind) && e.FineArmsKit >= 1;
            bool arms = CraftTree2On && !elite && UsesArmsKit(kind) && !fineArms && e.ArmsKit >= 1;
            bool siegeParts = CraftTree3On && kind == UnitKind.Ram && e.SiegeParts >= 1;
            if (fineArms) { e.FineArmsKit--; e.FineArmsKitConsumed++; }
            else if (arms) { e.ArmsKit--; e.ArmsKitConsumed++; }
            if (elite) { e.EliteKit--; e.EliteKitConsumed++; }
            if (siegeParts) { e.SiegeParts--; e.SiegePartsConsumed++; }
            if (b.Queued == 0) b.TrainRemaining = TrainTicksFor(faction, kind, c.ticks, rationed, planked);
            b.Queued++;
            if (AgesOn || kind == UnitKind.Monk)
            {
                var kinds = b.QueueKinds ?? Array.Empty<UnitKind>();
                Array.Resize(ref kinds, kinds.Length + 1);
                kinds[kinds.Length - 1] = kind;
                b.QueueKinds = kinds;
            }
            // Only under the flag: without it no queue entry is ever rationed, and the building keeps no extra state.
            if (RationsOn && (AgesOn || kind == UnitKind.Monk))
            {
                var rations = b.QueueRations ?? Array.Empty<bool>();
                Array.Resize(ref rations, rations.Length + 1);
                rations[rations.Length - 1] = rationed;
                b.QueueRations = rations;
            }
            if (PlanksOn && (AgesOn || kind == UnitKind.Monk))
            {
                var planks = b.QueuePlanks ?? Array.Empty<bool>();
                Array.Resize(ref planks, planks.Length + 1);
                planks[planks.Length - 1] = planked;
                b.QueuePlanks = planks;
            }
            if (CharmsOn && (AgesOn || kind == UnitKind.Monk))
            {
                var charms = b.QueueCharms ?? Array.Empty<bool>();
                Array.Resize(ref charms, charms.Length + 1);
                charms[charms.Length - 1] = charmed;
                b.QueueCharms = charms;
            }
            if (PrimitiveCraftOn)
            {
                var jerkyQueue = b.QueueJerky ?? Array.Empty<bool>();
                Array.Resize(ref jerkyQueue, jerkyQueue.Length + 1); jerkyQueue[jerkyQueue.Length - 1] = jerky; b.QueueJerky = jerkyQueue;
            }
            if (CraftTree2On)
            {
                var armsQueue = b.QueueArmsKit ?? Array.Empty<bool>();
                Array.Resize(ref armsQueue, armsQueue.Length + 1); armsQueue[armsQueue.Length - 1] = arms; b.QueueArmsKit = armsQueue;
                var fineQueue = b.QueueFineArmsKit ?? Array.Empty<bool>();
                Array.Resize(ref fineQueue, fineQueue.Length + 1); fineQueue[fineQueue.Length - 1] = fineArms; b.QueueFineArmsKit = fineQueue;
                var siegeQueue = b.QueueSiegeParts ?? Array.Empty<bool>();
                Array.Resize(ref siegeQueue, siegeQueue.Length + 1); siegeQueue[siegeQueue.Length - 1] = siegeParts; b.QueueSiegeParts = siegeQueue;
                var eliteQueue = b.QueueEliteKit ?? Array.Empty<bool>();
                Array.Resize(ref eliteQueue, eliteQueue.Length + 1); eliteQueue[eliteQueue.Length - 1] = elite; b.QueueEliteKit = eliteQueue;
                var engineQueue = b.QueueSiegeEngine ?? Array.Empty<bool>();
                Array.Resize(ref engineQueue, engineQueue.Length + 1); engineQueue[engineQueue.Length - 1] = siegeEngine; b.QueueSiegeEngine = engineQueue;
            }
        }

        private bool CanTrainTownVillager(uint faction, BuildingState b)
        => TownsAllowed(faction) && IsActualBuilding(b) && b.Complete && b.Kind == BuildingKind.Town
                && b.Queued < world.Config.Economy.QueueLimit
                && LivingVillagers(faction) + LivingSoldiers(faction) + world.Economies[faction - 1].Queued
                    + QueuedInfantry(faction) + QueuedTownVillagers(faction) < PopCapFor(faction)
                && world.Economies[faction - 1].Food >= VillagerFoodCostFor(faction, CanUseToolForVillager(faction));

        private void EnqueueTownVillager(uint faction, ref BuildingState b)
        {
            ref var economy = ref world.Economies[faction - 1];
            bool tool = CanUseToolForVillager(faction);
            bool stoneTool = !tool && CanUseStoneToolsForVillager(faction);
            int food = VillagerFoodCostFor(faction, tool);
            if (ArmyRecoveryWouldConsumeReserve(faction, ResourceKind.Food, food)) return;
            if (NoNegativeStockOn && economy.Food < food) return;
            economy.Food = checked(economy.Food - food);
            if (tool) { economy.Tools--; economy.ToolsConsumed++; economy.ToolsTrainedVillagers++; economy.ToolsSavedFood = checked(economy.ToolsSavedFood + VillagerFoodCostFor(faction, false) - food); }
            if (b.Queued == 0) b.TrainRemaining = TownVillagerTrainTicksFor(faction, tool);
            b.Queued++;
            if (stoneTool) { economy.StoneTools--; economy.StoneToolsConsumed++; economy.StoneToolsTrainedVillagers++; }
            var kinds = b.QueueKinds ?? Array.Empty<UnitKind>();
            Array.Resize(ref kinds, kinds.Length + 1);
            kinds[kinds.Length - 1] = UnitKind.Villager;
            b.QueueKinds = kinds;
            if (ToolsOn)
            {
                var tools = b.QueueTools ?? Array.Empty<bool>();
                Array.Resize(ref tools, tools.Length + 1); tools[tools.Length - 1] = tool; b.QueueTools = tools;
            }
            if (PrimitiveCraftOn)
            {
                var stoneTools = b.QueueStoneTools ?? Array.Empty<bool>();
                Array.Resize(ref stoneTools, stoneTools.Length + 1); stoneTools[stoneTools.Length - 1] = stoneTool; b.QueueStoneTools = stoneTools;
            }
        }

        private void CancelTownVillager(uint faction, ref BuildingState b)
        {
            if (b.Queued == 0) return;
            ref var economy = ref world.Economies[faction - 1];
            bool tool = QueueToolsAt(b, b.Queued - 1);
            bool stoneTool = b.QueueStoneTools != null && b.QueueStoneTools.Length > b.Queued - 1 && b.QueueStoneTools[b.Queued - 1];
            b.Queued--;
            economy.Food = checked(economy.Food + VillagerFoodCostFor(faction, tool));
            if (tool) { economy.Tools++; economy.ToolsConsumed--; economy.ToolsTrainedVillagers--; economy.ToolsSavedFood = checked(economy.ToolsSavedFood - (VillagerFoodCostFor(faction, false) - VillagerFoodCostFor(faction, true))); }
            if (stoneTool) { economy.StoneTools++; economy.StoneToolsConsumed--; economy.StoneToolsTrainedVillagers--; }
            if (b.QueueKinds != null && b.QueueKinds.Length > 0)
            {
                var kinds = new UnitKind[b.QueueKinds.Length - 1];
                Array.Copy(b.QueueKinds, 0, kinds, 0, kinds.Length);
                b.QueueKinds = kinds;
            }
            if (b.QueueTools != null && b.QueueTools.Length > 0) Array.Resize(ref b.QueueTools, b.QueueTools.Length - 1);
            if (b.QueueStoneTools != null && b.QueueStoneTools.Length > 0) Array.Resize(ref b.QueueStoneTools, b.QueueStoneTools.Length - 1);
            if (b.Queued == 0) b.TrainRemaining = 0;
        }

        /// <summary>
        /// Takes the last unit off the queue and returns today's price for it - metal only up to what the queue paid, so a
        /// cancel after advancing never gives metal that was not paid (26.1).
        /// </summary>
        private void CancelLast(uint faction, ref BuildingState b)
        {
            var kind = QueueAt(b, b.Queued - 1);
            bool rationed = QueueRationsAt(b, b.Queued - 1);
            bool planked = QueuePlanksAt(b, b.Queued - 1);
            bool charmed = QueueCharmsAt(b, b.Queued - 1);
            bool jerky = QueueJerkyAt(b, b.Queued - 1);
            bool armed = QueueArmsAt(b, b.Queued - 1);
            bool fineArmed = QueueFineArmsAt(b, b.Queued - 1);
            bool siegeParts = QueueSiegePartsAt(b, b.Queued - 1);
            bool elite = QueueEliteKitAt(b, b.Queued - 1);
            bool siegeEngine = QueueSiegeEngineAt(b, b.Queued - 1);
            var c = CostOf(faction, kind, b.Kind);
            ref var e = ref world.Economies[faction - 1];
            b.Queued--;
            if (b.QueueKinds != null && b.QueueKinds.Length > 0) Array.Resize(ref b.QueueKinds, b.QueueKinds.Length - 1);
            if (b.QueueRations != null && b.QueueRations.Length > 0) Array.Resize(ref b.QueueRations, b.QueueRations.Length - 1);
            if (b.QueuePlanks != null && b.QueuePlanks.Length > 0) Array.Resize(ref b.QueuePlanks, b.QueuePlanks.Length - 1);
            if (b.QueueCharms != null && b.QueueCharms.Length > 0) Array.Resize(ref b.QueueCharms, b.QueueCharms.Length - 1);
            if (b.QueueJerky != null && b.QueueJerky.Length > 0) Array.Resize(ref b.QueueJerky, b.QueueJerky.Length - 1);
            if (b.QueueArmsKit != null && b.QueueArmsKit.Length > 0) Array.Resize(ref b.QueueArmsKit, b.QueueArmsKit.Length - 1);
            if (b.QueueFineArmsKit != null && b.QueueFineArmsKit.Length > 0) Array.Resize(ref b.QueueFineArmsKit, b.QueueFineArmsKit.Length - 1);
            if (b.QueueSiegeParts != null && b.QueueSiegeParts.Length > 0) Array.Resize(ref b.QueueSiegeParts, b.QueueSiegeParts.Length - 1);
            if (b.QueueEliteKit != null && b.QueueEliteKit.Length > 0) Array.Resize(ref b.QueueEliteKit, b.QueueEliteKit.Length - 1);
            if (b.QueueSiegeEngine != null && b.QueueSiegeEngine.Length > 0) Array.Resize(ref b.QueueSiegeEngine, b.QueueSiegeEngine.Length - 1);
            e.Food = checked(e.Food + c.food);
            e.Wood = checked(e.Wood + c.wood);
            if (kind == UnitKind.HeavyInfantry)
            {
                int steelBack = Math.Min(c.metal, b.QueuedSteel);
                b.QueuedSteel -= steelBack;
                e.Steel = checked(e.Steel + steelBack);
            }
            else if (kind == UnitKind.HeavyCavalry)
            {
                e.Tack = checked(e.Tack + world.Config.Economy.HeavyCavalryTackCost);
                e.TackConsumed = checked(e.TackConsumed - world.Config.Economy.HeavyCavalryTackCost);
            }
            else if (kind == UnitKind.SkirmishArcher)
            {
                int bowGearBack = Math.Min(c.metal, b.QueuedBowGear);
                b.QueuedBowGear -= bowGearBack;
                e.BowGear = checked(e.BowGear + bowGearBack);
            }
            else
            {
                int metalBack = Math.Min(c.metal, b.QueuedMetal);
                b.QueuedMetal -= metalBack;
                e.Metal = checked(e.Metal + metalBack);
            }
            int gemsBack = Math.Min(c.gems, b.QueuedGems);
            b.QueuedGems -= gemsBack;
            e.Gems = checked(e.Gems + gemsBack);
            int goldBack = Math.Min(c.gold, b.QueuedGold);
            b.QueuedGold -= goldBack;
            e.Gold = checked(e.Gold + goldBack);
            if (rationed)
            {
                e.Rations = checked(e.Rations + world.Config.Economy.RationsPerSoldier);
                e.RationsConsumed = checked(e.RationsConsumed - world.Config.Economy.RationsPerSoldier);
            }
            if (JewelsAllowed(faction) && b.Kind == BuildingKind.Barracks && kind == UnitKind.Mercenary)
                e.JewelsConsumed = checked(e.JewelsConsumed - c.gems);
            if (planked)
            {
                e.Planks = checked(e.Planks + world.Config.Economy.PlanksPerRam);
                e.PlanksConsumed = checked(e.PlanksConsumed - world.Config.Economy.PlanksPerRam);
                e.PlanksConsumedRams = checked(e.PlanksConsumedRams - world.Config.Economy.PlanksPerRam);
                RecordCraftTracePlanks(faction, CraftTraceOther, -world.Config.Economy.PlanksPerRam);
            }
            if (charmed)
            {
                e.Charms = checked(e.Charms + 1);
                e.CharmsConsumed = checked(e.CharmsConsumed - 1);
            }
            if (jerky) { e.Jerky++; e.JerkyConsumed--; e.JerkyTrainedSoldiers--; }
            if (fineArmed) { e.FineArmsKit++; e.FineArmsKitConsumed--; }
            else if (armed) { e.ArmsKit++; e.ArmsKitConsumed--; }
            if (elite) { e.EliteKit++; e.EliteKitConsumed--; }
            if (siegeParts) { e.SiegeParts++; e.SiegePartsConsumed--; }
            if (siegeEngine) { e.SiegeEngine += world.Config.Economy.CannonSiegeEngineCost; e.SiegeEngineConsumed -= world.Config.Economy.CannonSiegeEngineCost; b.QueuedSiegeEngine -= world.Config.Economy.CannonSiegeEngineCost; }
            if (b.Queued == 0) b.TrainRemaining = 0;
        }

        /// <summary>The front unit left the building: the next one starts its clock.</summary>
        private void Dequeue(uint faction, ref BuildingState b, UnitKind done)
        {
            b.Queued--;
            if (b.QueueKinds != null && b.QueueKinds.Length > 0)
            {
                var rest = new UnitKind[b.QueueKinds.Length - 1];
                Array.Copy(b.QueueKinds, 1, rest, 0, rest.Length);
                b.QueueKinds = rest;
            }
            if (b.QueueRations != null && b.QueueRations.Length > 0)
            {
                var rest = new bool[b.QueueRations.Length - 1];
                Array.Copy(b.QueueRations, 1, rest, 0, rest.Length);
                b.QueueRations = rest;
            }
            if (b.QueuePlanks != null && b.QueuePlanks.Length > 0)
            {
                var rest = new bool[b.QueuePlanks.Length - 1];
                Array.Copy(b.QueuePlanks, 1, rest, 0, rest.Length);
                b.QueuePlanks = rest;
            }
            if (b.QueueCharms != null && b.QueueCharms.Length > 0)
            {
                var rest = new bool[b.QueueCharms.Length - 1];
                Array.Copy(b.QueueCharms, 1, rest, 0, rest.Length);
                b.QueueCharms = rest;
            }
            if (b.QueueJerky != null && b.QueueJerky.Length > 0)
            {
                var rest = new bool[b.QueueJerky.Length - 1];
                Array.Copy(b.QueueJerky, 1, rest, 0, rest.Length); b.QueueJerky = rest;
            }
            if (b.QueueArmsKit != null && b.QueueArmsKit.Length > 0)
            {
                var rest = new bool[b.QueueArmsKit.Length - 1];
                Array.Copy(b.QueueArmsKit, 1, rest, 0, rest.Length); b.QueueArmsKit = rest;
            }
            if (b.QueueFineArmsKit != null && b.QueueFineArmsKit.Length > 0)
            {
                var rest = new bool[b.QueueFineArmsKit.Length - 1];
                Array.Copy(b.QueueFineArmsKit, 1, rest, 0, rest.Length); b.QueueFineArmsKit = rest;
            }
            if (b.QueueSiegeParts != null && b.QueueSiegeParts.Length > 0)
            {
                var rest = new bool[b.QueueSiegeParts.Length - 1];
                Array.Copy(b.QueueSiegeParts, 1, rest, 0, rest.Length); b.QueueSiegeParts = rest;
            }
            if (b.QueueEliteKit != null && b.QueueEliteKit.Length > 0)
            {
                var rest = new bool[b.QueueEliteKit.Length - 1]; Array.Copy(b.QueueEliteKit, 1, rest, 0, rest.Length); b.QueueEliteKit = rest;
            }
            if (b.QueueSiegeEngine != null && b.QueueSiegeEngine.Length > 0)
            {
                var rest = new bool[b.QueueSiegeEngine.Length - 1]; Array.Copy(b.QueueSiegeEngine, 1, rest, 0, rest.Length); b.QueueSiegeEngine = rest;
            }
            var doneCost = CostOf(faction, done, b.Kind);
            int metal = doneCost.metal;
            int gems = doneCost.gems;
            int gold = doneCost.gold;
            if (done == UnitKind.HeavyInfantry) b.QueuedSteel = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedSteel - metal);
            else if (done == UnitKind.SkirmishArcher) b.QueuedBowGear = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedBowGear - metal);
            else if (done == UnitKind.Cannon) b.QueuedSiegeEngine = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedSiegeEngine - world.Config.Economy.CannonSiegeEngineCost);
            else if (metal > 0 || done == UnitKind.Infantry) b.QueuedMetal = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedMetal - metal);
            if (gems > 0) b.QueuedGems = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedGems - gems);
            if (world.Config.Economy.GoldEnabled && done == UnitKind.Monk)
                b.QueuedGold = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedGold - gold);
            b.TrainRemaining = b.Queued > 0 ? TrainTicksFor(faction, QueueAt(b, 0), CostOf(faction, QueueAt(b, 0), b.Kind).ticks, QueueRationsAt(b, 0), QueuePlanksAt(b, 0)) : 0;
        }

        /// <summary>Units of <paramref name="kind"/> queued in all the faction's buildings.</summary>
        private int QueuedOf(uint faction, UnitKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction || b.Kind == BuildingKind.Town) continue;
                for (int q = 0; q < b.Queued; q++) if (QueueAt(b, q) == kind) count++;
            }
            return count;
        }

        /// <summary>Room for one more of <paramref name="kind"/> in the armies it would join, counting the queues.</summary>
        private bool HasRoomFor(uint faction, UnitKind kind)
        {
            bool scout = kind == UnitKind.Scout;
            // Archers and cavalry join the infantry armies, so they share the infantry room.
            int queued = scout ? QueuedOf(faction, UnitKind.Scout)
                : QueuedOf(faction, UnitKind.Infantry) + QueuedOf(faction, UnitKind.Archer) + QueuedOf(faction, UnitKind.Cavalry)
                    + QueuedOf(faction, UnitKind.LightCavalry) + QueuedOf(faction, UnitKind.HeavyCavalry) + QueuedOf(faction, UnitKind.HeavyInfantry) + QueuedOf(faction, UnitKind.SkirmishArcher) + QueuedOf(faction, UnitKind.Ram) + QueuedOf(faction, UnitKind.Monk)
                    + QueuedOf(faction, UnitKind.Cannon)
                    + ((CaravanAllowed(faction) || JewelsAllowed(faction)) ? QueuedOf(faction, UnitKind.Mercenary) : 0);
            int free = 0;
            foreach (uint id in world.Factions[faction - 1].ArmyIds)
            {
                var a = world.Armies[id - 1];
                if ((a.Definition.Role == "scout") != scout) continue;
                int count = 0;
                foreach (uint soldier in a.SoldierIds) if (world.Soldiers[soldier - 1].Alive) count++;
                free += a.Definition.Capacity - count;
            }
            return free > queued;
        }

        /// <summary>
        /// The automatic economy keeps the scout armies full: one scout at a time when one is missing, never while saving
        /// to advance. Returns true when it queued one (one action per cycle).
        /// </summary>
        private bool DecideScout(uint faction, ref BuildingState barracks)
        {
            if (!AgesOn || SavingToAdvance(faction) || barracks.Queued >= world.Config.Economy.QueueLimit) return false;
            // LargeEconomy keeps a scout count of its own. The count check stays inside the flag: as a check for every map
            // (desired 1) it stopped the normal map from replacing a lost scout while another one lived (10-21 full test).
            if (world.Config.Economy.LargeEconomy
                && LivingClass(faction, UnitKind.Scout) + QueuedOf(faction, UnitKind.Scout) >= world.Config.Economy.LargeExploreScoutCount)
                return false;
            if (QueuedOf(faction, UnitKind.Scout) > 0
                || !HasRoomFor(faction, UnitKind.Scout) || !CanPay(faction, UnitKind.Scout)) return false;
            Enqueue(faction, ref barracks, UnitKind.Scout);
            return true;
        }

        /// <summary>Gold-enabled automatic economy keeps one monk planned, except while saving for the third age.</summary>
        private bool DecideMonk(uint faction, ref BuildingState barracks)
        {
            if (CultAllowed(faction)) return false;
            if (!world.Config.Economy.GoldEnabled || !world.Config.Economy.MonksEnabled || SavingToAdvance(faction)
                || barracks.Queued >= world.Config.Economy.QueueLimit || QueuedOf(faction, UnitKind.Monk) > 0
                || LivingClass(faction, UnitKind.Monk) > 0 || !HasRoomFor(faction, UnitKind.Monk) || !CanPay(faction, UnitKind.Monk)) return false;
            Enqueue(faction, ref barracks, UnitKind.Monk);
            return true;
        }
    }
}
