using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 markets and siege (technical-design-v3 32 #9, #19). A finished own market trades TradeLot of food, wood or stone for
    /// TradeReturn of another, or GemsTradeReturn of Gems. Gems can only be received. The siege workshop (second age) trains rams: they fight as infantry, but strike buildings
    /// and cores with RamSiegeDamage. Maps with ages only.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int TradeRich = 600, TradePoor = 150, AutoRams = 2, AutoTradeVillagers = 2;
        private static readonly ResourceKind[] GoodsKinds =
        {
            ResourceKind.Steel, ResourceKind.BowGear, ResourceKind.Rations, ResourceKind.Bricks, ResourceKind.Tack,
            ResourceKind.Planks, ResourceKind.Books, ResourceKind.Incense, ResourceKind.Charms, ResourceKind.RepairKits, ResourceKind.Tools,
            ResourceKind.CutStone, ResourceKind.Frames, ResourceKind.ArmsKit, ResourceKind.Scholarly, ResourceKind.FineArmsKit, ResourceKind.SiegeParts, ResourceKind.Mechanism, ResourceKind.SiegeEngine, ResourceKind.EliteKit
        };
        // V3-9 #2 provisional caravan safety policy. The values are deliberately small and tick-based:
        // danger is only what the faction currently sees, and a route needs 20 clear ticks before restarting.
        private const int CaravanDangerMeters = 1, CaravanSafetyTicks = 20, CaravanGemsReward = 1;

        private static bool Tradable(ResourceKind kind) => kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone;

        private static bool TradeTakeable(ResourceKind kind) => Tradable(kind) || kind == ResourceKind.Gems;

        private bool GoodsTradeable(ResourceKind kind)
        {
            if ((kind == ResourceKind.CutStone || kind == ResourceKind.Frames) && !CraftTree1On) return false;
            if ((kind == ResourceKind.ArmsKit || kind == ResourceKind.Scholarly || kind == ResourceKind.FineArmsKit) && !CraftTree2On) return false;
            if ((kind == ResourceKind.SiegeParts || kind == ResourceKind.Mechanism) && !CraftTree3On) return false;
            if ((kind == ResourceKind.SiegeEngine || kind == ResourceKind.EliteKit) && !CraftTree4On) return false;
            foreach (var goods in GoodsKinds) if (goods == kind) return true;
            return false;
        }

        private static int StockOf(FactionEconomy e, ResourceKind kind)
            => kind == ResourceKind.Food ? e.Food : kind == ResourceKind.Wood ? e.Wood : kind == ResourceKind.Ore ? e.Ore
                : kind == ResourceKind.Metal ? e.Metal : kind == ResourceKind.Charcoal ? e.Charcoal : kind == ResourceKind.Gold ? e.Gold
                : kind == ResourceKind.Stone ? e.Stone : kind == ResourceKind.Gems ? e.Gems : kind == ResourceKind.Steel ? e.Steel
                : kind == ResourceKind.BowGear ? e.BowGear : kind == ResourceKind.Rations ? e.Rations : kind == ResourceKind.Bricks ? e.Bricks
                : kind == ResourceKind.Tack ? e.Tack : kind == ResourceKind.Planks ? e.Planks : kind == ResourceKind.Books ? e.Books
                : kind == ResourceKind.Incense ? e.Incense : kind == ResourceKind.Charms ? e.Charms
                : kind == ResourceKind.RepairKits ? e.RepairKits : kind == ResourceKind.Tools ? e.Tools
                : kind == ResourceKind.CutStone ? e.CutStone : kind == ResourceKind.Frames ? e.Frames
                 : kind == ResourceKind.ArmsKit ? e.ArmsKit : kind == ResourceKind.Scholarly ? e.Scholarly : kind == ResourceKind.FineArmsKit ? e.FineArmsKit
                 : kind == ResourceKind.SiegeParts ? e.SiegeParts : kind == ResourceKind.Mechanism ? e.Mechanism
                 : kind == ResourceKind.SiegeEngine ? e.SiegeEngine : kind == ResourceKind.EliteKit ? e.EliteKit
                 : kind == ResourceKind.Fittings ? e.Fittings : kind == ResourceKind.Rope ? e.Rope : 0;

        private int GoodsValueFor(uint faction, ResourceKind kind)
        {
            var rules = world.Config.Economy;
            int value = kind == ResourceKind.TradeGoods ? rules.CaravanTradeGoodsValue : GoodsValue(rules, kind);
            return value;
        }

        private static int GoodsValue(EconomyRules rules, ResourceKind kind)
            => kind == ResourceKind.Steel ? rules.GoodsValueSteel : kind == ResourceKind.BowGear ? rules.GoodsValueBowGear
                : kind == ResourceKind.Rations ? rules.GoodsValueRations : kind == ResourceKind.Bricks ? rules.GoodsValueBricks
                : kind == ResourceKind.Tack ? rules.GoodsValueTack : kind == ResourceKind.Planks ? rules.GoodsValuePlanks
                : kind == ResourceKind.Books ? rules.GoodsValueBooks : kind == ResourceKind.Incense ? rules.GoodsValueIncense
                : kind == ResourceKind.Charms ? rules.GoodsValueCharms : kind == ResourceKind.RepairKits ? rules.GoodsValueRepairKits
                : kind == ResourceKind.Tools ? rules.GoodsValueTools : kind == ResourceKind.CutStone ? 4
                : kind == ResourceKind.Frames ? 10 : kind == ResourceKind.ArmsKit ? rules.GoodsValueArmsKit
                 : kind == ResourceKind.Scholarly ? rules.GoodsValueScholarly : kind == ResourceKind.FineArmsKit ? rules.GoodsValueFineArmsKit
                 : kind == ResourceKind.SiegeParts ? rules.GoodsValueSiegeParts : kind == ResourceKind.Mechanism ? rules.GoodsValueMechanism
                 : kind == ResourceKind.SiegeEngine ? rules.GoodsValueSiegeEngine : kind == ResourceKind.EliteKit ? rules.GoodsValueEliteKit : 0;

        // The goods whose workshops stop at a stock limit. Selling at that limit lets the workshop run again.
        private static int GoodsCap(EconomyRules rules, ResourceKind kind)
            => kind == ResourceKind.Incense ? rules.IncenseStockLimit : kind == ResourceKind.Charms ? rules.CharmStockLimit
                : kind == ResourceKind.RepairKits ? rules.RepairKitStockLimit : kind == ResourceKind.Tools ? rules.ToolStockLimit : 0;

        private int MarketReturnFor(uint faction, int baseReturn)
        {
            var rules = world.Config.Economy;
            int returned = checked(baseReturn + (HasTech(faction, TechKind.Banking) ? rules.BankingTradeReturn : 0));
            if (AgeStrength(faction, CivKind.Caravan, 3))
                returned = checked((returned * rules.CaravanMarketReturnPermille + 999) / 1000);
            if (HasTech(faction, MetropolisTech.MarketFestivity))
            {
                int steps = Math.Min(rules.MetropolisMarketMaxBonusPermille / rules.MetropolisMarketBonusPermille,
                    LivingVillagers(faction) / rules.MetropolisMarketVillagerStep);
                int bonus = checked(steps * rules.MetropolisMarketBonusPermille);
                returned = checked(returned * (1000 + bonus) / 1000);
            }
            return returned;
        }

        private void TradeAtMarket(uint faction, ResourceKind give, ResourceKind take)
        {
            bool mountainOreTrade = MountainAllowed(faction) && give == ResourceKind.Ore
                && (take == ResourceKind.Food || take == ResourceKind.Wood);
            bool goodsTrade = GoodsMarketOn && GoodsTradeable(give) && Tradable(take);
            if (!AgesOn || give == take || (!mountainOreTrade && !goodsTrade && (!Tradable(give) || !TradeTakeable(take)))) return;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0 || !IsActualBuilding(world.Buildings[market]) || !world.Buildings[market].Complete) return;
            var rules = world.Config.Economy;
            int lot = goodsTrade ? rules.GoodsTradeLot : rules.TradeLot;
            if (StockOf(world.Economies[faction - 1], give) < lot) return;
            AddStock(faction, give, -lot);
                int returned = goodsTrade ? MarketReturnFor(faction, checked(GoodsValueFor(faction, give) * lot))
                : take == ResourceKind.Gems ? rules.GemsTradeReturn : MarketReturnFor(faction, rules.TradeReturn);
            if (goodsTrade)
            {
                ref var stats = ref world.Economies[faction - 1];
                int index = (int)give;
                if (stats.GoodsSoldCounts != null)
                {
                    stats.GoodsSoldCounts[index] = checked(stats.GoodsSoldCounts[index] + 1);
                    stats.GoodsSoldUnits[index] = checked(stats.GoodsSoldUnits[index] + lot);
                    if (take == ResourceKind.Food) stats.GoodsReceivedFood[index] = checked(stats.GoodsReceivedFood[index] + returned);
                    if (take == ResourceKind.Wood) stats.GoodsReceivedWood[index] = checked(stats.GoodsReceivedWood[index] + returned);
                }
            }
            AddStock(faction, take, returned);
        }

        private List<GoodsPriceView> GoodsPriceViews(uint faction)
        {
            var prices = new List<GoodsPriceView>(GoodsKinds.Length);
            var rules = world.Config.Economy;
            foreach (var goods in GoodsKinds)
            {
                if ((goods == ResourceKind.CutStone || goods == ResourceKind.Frames) && !CraftTree1On) continue;
                if ((goods == ResourceKind.ArmsKit || goods == ResourceKind.Scholarly || goods == ResourceKind.FineArmsKit) && !CraftTree2On) continue;
                if ((goods == ResourceKind.SiegeParts || goods == ResourceKind.Mechanism) && !CraftTree3On) continue;
                if ((goods == ResourceKind.SiegeEngine || goods == ResourceKind.EliteKit) && !CraftTree4On) continue;
                if ((goods == ResourceKind.ArmsKit || goods == ResourceKind.Scholarly || goods == ResourceKind.FineArmsKit) && !CraftTree2On) continue;
                prices.Add(new GoodsPriceView(goods, MarketReturnFor(faction, checked(GoodsValue(rules, goods) * rules.GoodsTradeLot))));
            }
            return prices;
        }

        private void DecideGoodsMarket(uint faction)
        {
            if (!GoodsMarketOn || world.Tick % world.Config.Economy.GoodsAutoSellIntervalTicks != 0) return;
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            ResourceKind selected = 0;
            foreach (var goods in GoodsKinds)
            {
                if ((goods == ResourceKind.CutStone || goods == ResourceKind.Frames) && !CraftTree1On) continue;
                int cap = GoodsCap(rules, goods);
                int threshold = cap > 0 ? Math.Min(rules.GoodsAutoSellAt, cap) : rules.GoodsAutoSellAt;
                int stock = StockOf(economy, goods);
                if (stock >= threshold && stock - rules.GoodsTradeLot >= rules.GoodsAutoKeep) { selected = goods; break; }
            }
            if (selected == 0) return;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0)
            {
                // Same door as DecideMarket: no new market while the faction saves for its next age.
                if (SavingToAdvance(faction) || economy.Wood < rules.MarketWoodCost + rules.GoodsMarketWoodReserve) return;
                int origin = FindSite(faction, rules.MarketSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Market, origin, Facing.North, 0);
                return;
            }
            if (!IsActualBuilding(world.Buildings[market]) || !world.Buildings[market].Complete || world.Buildings[market].Held) return;
            ResourceKind take = economy.Food < economy.Wood ? ResourceKind.Food : ResourceKind.Wood;
            TradeAtMarket(faction, selected, take);
        }

        private int OwnFinishedMarketIndex(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Market) return i;
            }
            return -1;
        }

        /// <summary>Assigns the living villagers in an economy command to the deterministic first finished own market.</summary>
        private void StartTradeRoute(uint faction, IReadOnlyList<uint> villagers)
        {
            if (!AgesOn) return;
            int market = OwnFinishedMarketIndex(faction);
            if (market < 0) return;
            uint marketId = world.Buildings[market].Id;
            foreach (uint id in villagers)
            {
                if (id == 0 || id > world.VillagerCount) continue;
                ref var v = ref world.Villagers[id - 1];
                if (!v.Alive || v.FactionId != faction) continue;
                ClearCaravan(ref v);
                v.NodeId = 0;
                v.HaulFrom = 0;
                v.HaulTo = 0;
                v.BuildingId = marketId;
                v.Task = VillagerTask.ToTradeMarket;
                ReleaseVillagerRoute(ref v);
                v.RouteCursor = 0;
                v.RouteGoal = v.Position;
                if (IndustryOn) v.Held = true;
            }
        }

        /// <summary>Returns true while both ends of a route remain a living own core and finished own market.</summary>
        private bool TradeRouteActive(VillagerState v)
        {
            if (!AgesOn || !v.Alive || v.BuildingId == 0 || v.BuildingId > world.BuildingCount) return false;
            var market = world.Buildings[v.BuildingId - 1];
            return IsActualBuilding(market) && market.Complete && market.FactionId == v.FactionId && market.Kind == BuildingKind.Market
                && OwnCore(v.FactionId).Hp > 0;
        }

        private static bool IsTradeRouteTask(VillagerTask task)
            => task == VillagerTask.ToTradeMarket || task == VillagerTask.ToTradeCore;

        private static bool IsCaravanTask(VillagerTask task)
            => task == VillagerTask.ToCaravanMarket || task == VillagerTask.ToCaravanserai || task == VillagerTask.ToCaravanCore;

        private static void ClearCaravan(ref VillagerState v)
        {
            v.CaravanMarketId = 0; v.CaravanseraiId = 0; v.CaravanOutpostId = 0; v.CaravanStage = 0;
            v.CaravanWood = 0; v.CaravanGems = 0; v.CaravanDangerStopped = false; v.CaravanSafeTicks = 0;
        }

        private void StopCaravan(ref VillagerState v)
        {
            ClearCaravan(ref v);
            v.Task = VillagerTask.Idle;
            ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position; v.MoveGoal = v.Position;
        }

        private bool CaravanMarketValid(VillagerState v)
        {
            if (!CaravanOn || v.CaravanMarketId == 0 || v.CaravanMarketId > world.BuildingCount) return false;
            var market = world.Buildings[v.CaravanMarketId - 1];
            return IsActualBuilding(market) && market.Complete && market.FactionId == v.FactionId && market.Kind == BuildingKind.Market;
        }

        private bool CaravanPairValid(VillagerState v)
        {
            if (!CaravanMarketValid(v) || v.CaravanseraiId == 0 || v.CaravanseraiId > world.BuildingCount) return false;
            var host = world.Buildings[v.CaravanseraiId - 1];
            if (!IsActualBuilding(host) || !host.Complete || host.FactionId != v.FactionId || host.Kind != BuildingKind.Caravanserai
                || host.CaravanMarketId != v.CaravanMarketId || host.CaravanOutpostId != v.CaravanOutpostId) return false;
            if (v.CaravanOutpostId == 0 || v.CaravanOutpostId > world.Outposts.Length) return false;
            return world.Outposts[v.CaravanOutpostId - 1].OwnerFactionId == v.FactionId;
        }

        private bool ObservedEnemyNear(uint faction, SimPoint point)
        {
            long limit = Fix64.FromInt(CaravanDangerMeters).Raw;
            BigInteger squaredLimit = new BigInteger(limit) * limit;
            var observation = world.Factions[faction - 1];
            foreach (int i in world.SoldierTraversal)
            {
                var enemy = world.Soldiers[i];
                if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                int cell = world.Map.Cell(enemy.Position);
                if (cell < 0 || cell >= observation.VisibleCells.Length || !observation.VisibleCells[cell]) continue;
                long dx = checked(enemy.Position.X.Raw - point.X.Raw), dz = checked(enemy.Position.Z.Raw - point.Z.Raw);
                if (new BigInteger(dx) * dx + new BigInteger(dz) * dz <= squaredLimit) return true;
            }
            return false;
        }

        /// <summary>Only visible enemy soldiers are considered. Unknown enemies never stop a caravan.</summary>
        private bool CaravanDangerous(VillagerState v)
        {
            if (!CaravanOn || v.CaravanMarketId == 0 || v.CaravanMarketId > world.BuildingCount
                || v.CaravanseraiId == 0 || v.CaravanseraiId > world.BuildingCount) return false;
            var market = world.Buildings[v.CaravanMarketId - 1];
            var host = world.Buildings[v.CaravanseraiId - 1];
            if (ObservedEnemyNear(v.FactionId, v.Position)) return true;
            if (IsActualBuilding(market) && ObservedEnemyNear(v.FactionId, world.Map.Center(market.WorkCell))) return true;
            return IsActualBuilding(host) && ObservedEnemyNear(v.FactionId, world.Map.Center(host.WorkCell));
        }

        private bool HoldCaravanAtMarket(ref VillagerState v)
        {
            if (v.Task != VillagerTask.ToCaravanMarket || (v.CaravanStage != 1 && v.CaravanStage != 3)
                || v.CaravanWood > 0 || v.CaravanGems > 0
                || !CaravanMarketValid(v)) return false;
            var market = world.Buildings[v.CaravanMarketId - 1];
            if (!InRange(v.Position, world.Map.Center(market.WorkCell), GatherReach)) return false;
            if (CaravanDangerous(v))
            {
                v.CaravanDangerStopped = true;
                v.CaravanSafeTicks = 0;
                ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                return true;
            }
            if (!v.CaravanDangerStopped) return false;
            v.CaravanSafeTicks = checked(v.CaravanSafeTicks + 1);
            if (v.CaravanSafeTicks < CaravanSafetyTicks)
            {
                ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                return true;
            }
            v.CaravanDangerStopped = false;
            v.CaravanSafeTicks = 0;
            return false;
        }

        private void StopCaravanOutboundForDanger(ref VillagerState v)
        {
            if (v.Task != VillagerTask.ToCaravanserai || !CaravanDangerous(v)) return;
            v.CaravanDangerStopped = true;
            v.CaravanSafeTicks = 0;
            // This is a voluntary retreat, not an ownership-loss stop: keep the fixed pair and return empty-handed.
            v.CaravanStage = 3; v.Task = VillagerTask.ToCaravanMarket;
            ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
        }

        private void EndCaravanAtCore(ref VillagerState v)
        {
            v.CaravanWood = 0; // The registered market was lost, so unbanked cargo is not recoverable.
            v.Task = VillagerTask.ToCaravanCore;
            ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
        }

        private void StartCaravanRoute(uint faction, uint caravanseraiId, IReadOnlyList<uint> villagers)
        {
            if (!CaravanAllowed(faction) || caravanseraiId == 0 || caravanseraiId > world.BuildingCount) return;
            var host = world.Buildings[caravanseraiId - 1];
            if (!IsActualBuilding(host) || !host.Complete || host.FactionId != faction || host.Kind != BuildingKind.Caravanserai) return;
            uint marketId = host.CaravanMarketId;
            uint outpostId = host.CaravanOutpostId;
            if (marketId == 0 || marketId > world.BuildingCount || outpostId == 0 || outpostId > world.Outposts.Length) return;
            var market = world.Buildings[marketId - 1];
            if (!IsActualBuilding(market) || !market.Complete || market.FactionId != faction || market.Kind != BuildingKind.Market
                || world.Outposts[outpostId - 1].OwnerFactionId != faction) return;
            foreach (uint id in villagers)
            {
                if (id == 0 || id > world.VillagerCount) continue;
                ref var v = ref world.Villagers[id - 1];
                if (!v.Alive || v.FactionId != faction) continue;
                // Re-sending the same (or another) caravan order never resets an active route. In particular, it
                // must not clear a dedicated load that is already on the return leg.
                if (IsCaravanTask(v.Task) || v.CaravanWood > 0) continue;
                v.NodeId = 0; v.HaulFrom = 0; v.HaulTo = 0; v.BuildingId = 0;
                ClearCaravan(ref v);
                v.CaravanMarketId = marketId; v.CaravanseraiId = caravanseraiId; v.CaravanOutpostId = outpostId; v.CaravanStage = 1;
                // A normal gathering load is delivered first. The pending caravan fields survive that delivery,
                // while the dedicated CaravanWood slot remains empty until the host is reached.
                if (v.Carry > 0) v.Task = VillagerTask.ToDropOff;
                else { v.Task = VillagerTask.ToCaravanMarket; ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position; }
                if (IndustryOn) v.Held = true;
            }
        }

        private bool TryCaravanseraiPlacement(uint faction, int origin, out uint outpostId, out uint marketId,
            out Fix64 distance, out int reward)
        {
            outpostId = 0; marketId = 0; distance = default; reward = 0;
            if (!CaravanAllowed(faction)) return false;
            int market = OwnFinishedMarketIndex(faction);
            if (market < 0) return false;
            var marketState = world.Buildings[market];
            var centre = FootprintCenter(origin, world.Config.Economy.CaravanseraiSizeCells);
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var post = world.Outposts[i];
                if (post.OwnerFactionId != faction || !InRange(centre, post.Definition.Position, Fix64.FromInt(world.Config.Economy.CaravanOutpostReach))) continue;
                bool occupied = false;
                for (int b = 0; b < world.BuildingCount; b++)
                {
                    var existing = world.Buildings[b];
                if (IsActualBuilding(existing) && existing.FactionId == faction && existing.Kind == BuildingKind.Caravanserai
                        && existing.CaravanOutpostId == post.Definition.Id) { occupied = true; break; }
                }
                if (occupied) continue;
                distance = FixedDistance(FootprintCenter(marketState.OriginCell, SizeOf(marketState.Kind)), centre);
                if (distance < Fix64.FromInt(world.Config.Economy.CaravanMinimumDistance)) continue;
                outpostId = post.Definition.Id; marketId = marketState.Id;
                long units = distance.Raw / Fix64.FromInt(world.Config.Economy.CaravanRewardDistanceStep).Raw;
                reward = (int)Math.Max(1, Math.Min(world.Config.Economy.CaravanRewardMaxWood, units));
                if (AgeStrength(faction, CivKind.Caravan, 4))
                    reward = checked((reward * world.Config.Economy.CaravanRewardPermille + 999) / 1000);
                return true;
            }
            return false;
        }

        /// <summary>Distance is integer fixed-point: sqrt(rawX²+rawZ²) is already a Q47.16 raw distance.
        /// BigInteger prevents the squared coordinates from overflowing; the integer square root floors fractional raw units.</summary>
        private static Fix64 FixedDistance(SimPoint a, SimPoint b)
        {
            long dx = checked(a.X.Raw - b.X.Raw), dz = checked(a.Z.Raw - b.Z.Raw);
            return Fix64.FromRaw((long)FixMath.IntegerSqrt(new BigInteger(dx) * dx + new BigInteger(dz) * dz));
        }

        private int FindCaravanseraiSite(uint faction, uint outpostId)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = world.Config.Economy.CaravanseraiSizeCells;
            var post = world.Outposts[outpostId - 1].Definition.Position;
            int centre = world.Map.Cell(post), cx = centre % width, cz = centre / width;
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)
                            || !KeepsMapConnected(faction, origin, size)) continue;
                        uint ignoredOutpost, ignoredMarket; Fix64 ignoredDistance; int ignoredReward;
                        if (TryCaravanseraiPlacement(faction, origin, out ignoredOutpost, out ignoredMarket, out ignoredDistance, out ignoredReward)
                            && ignoredOutpost == outpostId) return origin;
                    }
            return -1;
        }

        private void InitializeCaravanserai(uint faction, int index, uint outpostId, uint marketId, Fix64 distance, int reward)
        {
            ref var host = ref world.Buildings[index];
            host.CaravanOutpostId = outpostId; host.CaravanMarketId = marketId; host.CaravanDistance = distance; host.CaravanWoodReward = reward;
        }

        private void DecideCaravanserai(uint faction)
        {
            if (!CaravanAllowed(faction) || SavingToAdvance(faction)) return;
            if (OwnFinishedMarketIndex(faction) < 0) return;
            var e = world.Economies[faction - 1];
            if (e.Wood < WoodOf(BuildingKind.Caravanserai, faction)) return;
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var post = world.Outposts[i];
                if (post.OwnerFactionId != faction) continue;
                bool exists = false;
                for (int b = 0; b < world.BuildingCount; b++)
                if (IsActualBuilding(world.Buildings[b]) && world.Buildings[b].FactionId == faction && world.Buildings[b].Kind == BuildingKind.Caravanserai
                        && world.Buildings[b].CaravanOutpostId == post.Definition.Id) { exists = true; break; }
                if (exists) continue;
                int origin = FindCaravanseraiSite(faction, post.Definition.Id);
                if (origin < 0) continue;
                uint outpostId, marketId; Fix64 distance; int reward;
                if (!TryCaravanseraiPlacement(faction, origin, out outpostId, out marketId, out distance, out reward)) continue;
                PlaceBuildingAt(faction, BuildingKind.Caravanserai, origin, Facing.North, 0);
                InitializeCaravanserai(faction, world.BuildingCount - 1, outpostId, marketId, distance, reward);
                return;
            }
        }

        private void StopTradeRoute(ref VillagerState v)
        {
            v.Task = VillagerTask.Idle;
            v.NodeId = 0;
            v.BuildingId = 0;
            v.HaulFrom = 0;
            v.HaulTo = 0;
            ReleaseVillagerRoute(ref v);
            v.RouteCursor = 0;
            v.RouteGoal = v.Position;
            v.MoveGoal = v.Position;
        }

        /// <summary>Completes one end of the route. Wood is created only on arrival at the own core.</summary>
        private void AdvanceTradeRoute(ref VillagerState v)
        {
            if (!TradeRouteActive(v)) { StopTradeRoute(ref v); return; }
            var market = world.Buildings[v.BuildingId - 1];
            if (v.Task == VillagerTask.ToTradeMarket)
            {
                if (InRange(v.Position, world.Map.Center(market.WorkCell), GatherReach)) v.Task = VillagerTask.ToTradeCore;
                return;
            }
            if (v.Task != VillagerTask.ToTradeCore) return;
            if (!InRange(v.Position, OwnCore(v.FactionId).Definition.Position, world.Config.Rules.CoreRadius)) return;
            if (v.Carry > 0)
            {
                AddStock(v.FactionId, v.CarryKind, v.Carry);
                v.Carry = 0;
            }
            AddStock(v.FactionId, ResourceKind.Wood, world.Config.Economy.TradeRouteWood);
            v.Task = VillagerTask.ToTradeMarket;
            ReleaseVillagerRoute(ref v);
            v.RouteCursor = 0;
            v.RouteGoal = v.Position;
        }

        private void AdvanceCaravanRoute(ref VillagerState v)
        {
            if (v.Task == VillagerTask.ToCaravanCore)
            {
                if (InRange(v.Position, OwnCore(v.FactionId).Definition.Position, world.Config.Rules.CoreRadius)) StopCaravan(ref v);
                return;
            }
            if (!CaravanMarketValid(v)) { EndCaravanAtCore(ref v); return; }
            if (v.Task == VillagerTask.ToCaravanserai)
            {
                // Loss of the Outpost or host cancels only the new outbound leg. Any already loaded cargo is
                // never here: loading changes the task to the return leg in the same tick.
                if (!CaravanPairValid(v))
                {
                    v.CaravanStage = 3; v.Task = VillagerTask.ToCaravanMarket; ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                    return;
                }
                var host = world.Buildings[v.CaravanseraiId - 1];
                if (InRange(v.Position, world.Map.Center(host.WorkCell), GatherReach))
                {
                    // The dedicated slot is the idempotence guard: one host arrival can create one load only.
                    if (v.CaravanWood == 0 && v.CaravanGems == 0)
                    {
                        v.CaravanWood = host.CaravanWoodReward;
                        if (world.Economies[v.FactionId - 1].Age >= 2) v.CaravanGems = CaravanGemsReward;
                    }
                    v.CaravanStage = 3; v.Task = VillagerTask.ToCaravanMarket;
                    ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                }
                return;
            }
            if (v.Task != VillagerTask.ToCaravanMarket) return;
            var market = world.Buildings[v.CaravanMarketId - 1];
            if (!InRange(v.Position, world.Map.Center(market.WorkCell), GatherReach)) return;
            if (v.CaravanWood > 0)
            {
                AddStock(v.FactionId, ResourceKind.Wood, v.CaravanWood);
                v.CaravanWood = 0;
            }
            if (v.CaravanGems > 0)
            {
                AddStock(v.FactionId, ResourceKind.Gems, v.CaravanGems);
                v.CaravanGems = 0;
            }
            if (!CaravanPairValid(v)) { StopCaravan(ref v); return; }
            if (v.CaravanDangerStopped) return;
            v.CaravanStage = 2; v.Task = VillagerTask.ToCaravanserai;
            ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
        }

        /// <summary>Automatic economy: one idle villager is assigned until two active routes exist.</summary>
        private void DecideTradeRoute(uint faction)
        {
            if (!AgesOn || SavingToAdvance(faction)) return;
            int market = OwnFinishedMarketIndex(faction);
            if (market < 0 || world.Buildings[market].Held) return;
            int active = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && IsTradeRouteTask(v.Task)) active++;
            }
            if (active >= AutoTradeVillagers) return;
            uint marketId = world.Buildings[market].Id;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.Task != VillagerTask.Idle) continue;
                v.NodeId = 0;
                v.HaulFrom = 0;
                v.HaulTo = 0;
                v.BuildingId = marketId;
                v.Task = VillagerTask.ToTradeMarket;
                ReleaseVillagerRoute(ref v);
                v.RouteCursor = 0;
                v.RouteGoal = v.Position;
                return;
            }
        }

        private int ActiveCaravanRoutes(uint faction, uint hostId)
        {
            int active = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && IsCaravanTask(v.Task) && v.CaravanseraiId == hostId) active++;
            }
            return active;
        }

        private bool TryAssignCaravanVillager(uint faction, uint hostId)
        {
            // First use idle villagers. A deterministic villager-ID order makes ties stable.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != faction || v.Held || IsCaravanTask(v.Task)) continue;
                    bool idle = v.Task == VillagerTask.Idle;
                    if ((pass == 0) != idle) continue;
                    if (pass == 1 && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering && v.Task != VillagerTask.ToDropOff) continue;
                    // On the second pass this is an automatic gathering villager. StartCaravanRoute preserves
                    // an ordinary load by sending it through ToDropOff before the caravan leaves.
                    StartCaravanRoute(faction, hostId, new[] { v.Id });
                    return true;
                }
            return false;
        }

        /// <summary>Automatic economy: distribute one villager at a time across every finished fixed route.</summary>
        private void DecideCaravanRoute(uint faction)
        {
            if (!CaravanAllowed(faction)) return;
            int hostIndex = -1, least = int.MaxValue;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != faction || b.Kind != BuildingKind.Caravanserai || b.Held) continue;
                if (b.CaravanMarketId == 0 || b.CaravanMarketId > world.BuildingCount || b.CaravanOutpostId == 0
                    || b.CaravanOutpostId > world.Outposts.Length) continue;
                var market = world.Buildings[b.CaravanMarketId - 1];
                if (!IsActualBuilding(market) || !market.Complete || market.FactionId != faction || market.Kind != BuildingKind.Market
                    || world.Outposts[b.CaravanOutpostId - 1].OwnerFactionId != faction) continue;
                int active = ActiveCaravanRoutes(faction, b.Id);
                if (active < world.Config.Economy.CaravanAutoVillagers && active < least)
                { hostIndex = i; least = active; }
            }
            if (hostIndex < 0) return;
            TryAssignCaravanVillager(faction, world.Buildings[hostIndex].Id);
        }

        /// <summary>What a soldier deals to a building or a core: a ram its siege damage, everyone else their damage.</summary>
        private int SiegeDamage(SoldierState s)
        {
            int damage;
            if (s.Class != UnitKind.Ram && s.Class != UnitKind.Cannon) damage = s.Parameters.Damage;
            else
            {
                var rules = world.Config.Economy;
                damage = s.Class == UnitKind.Cannon ? rules.CannonSiegeDamage : rules.RamSiegeDamage;
                damage += HasTech(s.Initial.FactionId, TechKind.Siegecraft) ? rules.SiegecraftSiegeDamage : 0;
                if (AgeStrength(s.Initial.FactionId, CivKind.Bridge, 3))
                    damage = checked((damage * rules.BridgeRamSiegeDamagePermille + 999) / 1000);
                if (CraftTree3On && s.SiegePartsEnhanced)
                    damage = checked((damage * rules.SiegePartsDamagePermille + 999) / 1000);
            }
            var economy = world.Config.Economy;
            if (!economy.SiegeByAge || damage <= 0) return damage;
            int age = world.Economies[s.Initial.FactionId - 1].Age;
        int permille = age <= 0 ? economy.SiegeAge0Permille : age == 1 ? economy.SiegeAge1Permille
            : age == 2 ? economy.SiegeAge2Permille : age == 3 ? economy.SiegeAge3Permille : economy.SiegeAge4Permille;
            return Math.Max(1, checked(damage * permille / 1000));
        }

        /// <summary>
        /// AI phase, once the civilisation's line stands and a blacksmith too: a market; then, while one of food, wood and
        /// stone is TradeRich or more and another is under TradePoor, one lot from the richest to the poorest each cycle.
        /// </summary>
        private void DecideMarket(uint faction)
        {
            bool caravanNeedsBaseMarket = CaravanAllowed(faction) && OwnFinishedMarketIndex(faction) < 0;
            if (!AgesOn || (!CivLineStarted(faction) && !caravanNeedsBaseMarket)) return;
            var rules = world.Config.Economy;
            bool foodMarketCiv = CivUsesFoodMarket(faction) || FishingNeedsFoodMarket(faction);
            // Agriculture keeps its original market timing and rich/poor rule. The two civilizations without a
            // farm get a market before saving can close the door, so food remains available after wild food dries up.
            // Caravan's first market is the base condition for the civilisation, so allow that one building before
            // CivLineStarted becomes true. A caravanserai still waits for the finished market below.
            if (!foodMarketCiv && !caravanNeedsBaseMarket && SavingToAdvance(faction)) return;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0)
            {
                if (world.Economies[faction - 1].Wood < rules.MarketWoodCost + TradePoor) return;
                int origin = FindSite(faction, rules.MarketSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Market, origin, Facing.North, 0);
                return;
            }
            if (!world.Buildings[market].Complete || world.Buildings[market].Held) return;
            var e = world.Economies[faction - 1];
            if (foodMarketCiv)
            {
                if (e.Food >= rules.MarketFoodFloor) return;
                ResourceKind give = FoodTradeSource(e, rules);
                if (give != 0) TradeAtMarket(faction, give, ResourceKind.Food);
                return;
            }
            ResourceKind rich = 0, poor = 0;
            // Gems are a special final-research currency, not part of the three-resource balancing decision.
            foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone })
            {
                if (rich == 0 || StockOf(e, kind) > StockOf(e, rich)) rich = kind;
                if (poor == 0 || StockOf(e, kind) < StockOf(e, poor)) poor = kind;
            }
            if (rich != poor && StockOf(e, rich) >= TradeRich && StockOf(e, poor) < TradePoor) TradeAtMarket(faction, rich, poor);
        }

        /// <summary>Chooses a deterministic food trade source while preserving the next-age/building budget.</summary>
        private static ResourceKind FoodTradeSource(FactionEconomy e, EconomyRules rules)
        {
            bool wood = e.Wood >= rules.MarketWoodReserve + rules.TradeLot;
            bool stone = e.Stone >= rules.MarketStoneReserve + rules.TradeLot;
            if (!wood) return stone ? ResourceKind.Stone : (ResourceKind)0;
            if (!stone) return ResourceKind.Wood;
            int woodSurplus = e.Wood - rules.MarketWoodReserve;
            int stoneSurplus = e.Stone - rules.MarketStoneReserve;
            return woodSurplus >= stoneSurplus ? ResourceKind.Wood : ResourceKind.Stone;
        }

        /// <summary>Fishing keeps the registration row's original no-market policy until its covered fish is gone.</summary>
        private bool FishingNeedsFoodMarket(uint faction)
        {
            if (!FishingAllowed(faction)) return false;
            bool harbor = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Harbor) { harbor = true; break; }
            }
            if (!harbor) return false;
            foreach (var node in world.Nodes)
                if (node.Fishing && node.Remaining > 0 && HarborCoversFish(faction, node.Definition.Position)) return false;
            return true;
        }

        /// <summary>AI phase, in the second age: a siege workshop, then up to AutoRams rams at a time.</summary>
        private void DecideSiege(uint faction)
        {
            if (!AgesOn || world.Economies[faction - 1].Age < 2 || SavingToAdvance(faction)) return;
            var rules = world.Config.Economy;
            int workshop = OwnBuildingIndex(faction, BuildingKind.SiegeWorkshop);
            ref var economy = ref world.Economies[faction - 1];
            // AgeFourAi must not inherit the old "build only when the workshop branch is reached"
            // dead-end: once an age-four siege engine exists, the workshop is an explicit demand.
            // Keep this entirely inside the flag so AgeFourAi=false is byte-for-byte unchanged.
            if (AgeFourAiOn && economy.Age >= 4 && economy.SiegeEngine >= rules.CannonSiegeEngineCost && workshop < 0)
            {
                if (economy.Wood < rules.WorkshopWoodCost) return;
                int ageFourOrigin = FindSite(faction, rules.WorkshopSizeCells);
                if (ageFourOrigin >= 0) PlaceBuildingAt(faction, BuildingKind.SiegeWorkshop, ageFourOrigin, Facing.North, 0);
                return;
            }
            if (workshop < 0)
            {
                if (economy.Wood < rules.WorkshopWoodCost) return;
                int origin = FindSite(faction, rules.WorkshopSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.SiegeWorkshop, origin, Facing.North, 0);
                return;
            }
            ref var b = ref world.Buildings[workshop];
            if (!b.Complete || b.Held || b.Queued > 0) return;
            if (AgeFourAiOn && economy.Age >= 4 && CraftTree4Allowed(faction) && economy.SiegeEngine >= rules.CannonSiegeEngineCost
                && CountClass(faction, UnitKind.Cannon) < rules.CannonAutoLimit
                && AgeFourAiHasCannonRoom(faction) && CanPay(faction, UnitKind.Cannon, BuildingKind.SiegeWorkshop))
            {
                Enqueue(faction, ref b, UnitKind.Cannon);
                return;
            }
            // Do not spend the last food/wood on rams while an engine is waiting for a cannon.
            // CanPay above remains the single source of truth for the actual price check.
            if (AgeFourAiOn && economy.Age >= 4 && economy.SiegeEngine >= rules.CannonSiegeEngineCost) return;
            if (CraftTree4Allowed(faction) && economy.SiegeEngine >= rules.CannonSiegeEngineCost
                && CountClass(faction, UnitKind.Cannon) < rules.CannonAutoLimit
                && HasRoomFor(faction, UnitKind.Cannon) && CanPay(faction, UnitKind.Cannon, BuildingKind.SiegeWorkshop))
            {
                Enqueue(faction, ref b, UnitKind.Cannon);
                return;
            }
            if (CountClass(faction, UnitKind.Ram) >= AutoRamLimitFor(faction)) return;
            if (HasRoomFor(faction, UnitKind.Ram) && CanPay(faction, UnitKind.Ram)) Enqueue(faction, ref b, UnitKind.Ram);
        }

        private bool AgeFourAiHasCannonRoom(uint faction)
            => HasRoomFor(faction, UnitKind.Cannon)
                || EconomyScaleOn && world.Config.Economy.ArmyGrowth
                    && world.Factions[faction - 1].ArmyIds.Length < 12;
    }
}
