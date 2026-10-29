using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void AdvanceCatapults()
        {
            if (!BeltCraftOn) return;
            for (int i = 0; i < world.Catapults.Length; i++)
            {
                var state = world.Catapults[i];
                if (!state.Active) continue;
                AdvanceCatapultReceiver(ref state);
                LoadCatapult(ref state);
                AdvanceCatapultFlights(ref state);
                TryThrowCatapult(ref state);
                world.Catapults[i] = state;
            }
        }

        private void LoadCatapult(ref CatapultState state)
        {
            int rear = BeltPrevious(state.LauncherCell, world.Belts[state.LauncherCell].Facing);
            if (rear < 0 || rear >= world.Belts.Length) return;
            ref var belt = ref world.Belts[rear];
            if (belt.FactionId != state.FactionId || IsSpecialComponent(belt.Component) || belt.Item == 0) return;
            if (state.BufferedCount > 0 && state.BufferKind != belt.Item) return;
            int amount = System.Math.Min(world.Config.Economy.CatapultBuffer - state.BufferedCount, BeltItemCount(belt));
            if (amount <= 0) return;
            state.BufferKind = belt.Item;
            state.BufferedCount += amount;
            TakeFromBelt(ref belt, amount);
            if (state.FirstLoadTick < 0) state.FirstLoadTick = world.Tick;
        }

        private void TryThrowCatapult(ref CatapultState state)
        {
            if (state.BufferedCount <= 0 || state.FirstLoadTick < 0) return;
            var rules = world.Config.Economy;
            bool ready = state.BufferedCount >= rules.CatapultLoad || world.Tick - state.FirstLoadTick >= rules.CatapultMaxWaitTicks;
            if (!ready || world.Tick - state.LastThrowTick < rules.CatapultIntervalTicks) return;
            int inFlight = 0, slot = -1;
            for (int i = 0; i < state.Shots.Length; i++)
            {
                if (state.Shots[i].Active) inFlight += state.Shots[i].Count;
                else if (slot < 0) slot = i;
            }
            if (slot < 0 || state.ReceiverCount + inFlight + System.Math.Min(rules.CatapultLoad, state.BufferedCount) > rules.CatapultBuffer) return;
            int count = System.Math.Min(rules.CatapultLoad, state.BufferedCount);
            state.Shots[slot] = new CatapultShotState { Active = true, Item = state.BufferKind, Count = count, RemainingTicks = rules.CatapultFlightTicks };
            state.BufferedCount -= count;
            state.LastThrowTick = world.Tick;
            state.FirstLoadTick = state.BufferedCount == 0 ? -1 : world.Tick;
            if (state.BufferedCount == 0) state.BufferKind = 0;
        }

        private void AdvanceCatapultFlights(ref CatapultState state)
        {
            var rules = world.Config.Economy;
            for (int i = 0; i < state.Shots.Length; i++)
            {
                var shot = state.Shots[i];
                if (!shot.Active) continue;
                if (shot.RemainingTicks > 0) shot.RemainingTicks--;
                if (shot.RemainingTicks == 0)
                {
                    if (state.ReceiverCount == 0) state.ReceiverKind = shot.Item;
                    if (state.ReceiverKind == shot.Item && state.ReceiverCount + shot.Count <= rules.CatapultBuffer)
                    {
                        state.ReceiverCount += shot.Count;
                        state.Shots[i] = default(CatapultShotState);
                        continue;
                    }
                    else shot.RemainingTicks = 1; // reserved capacity makes this defensive branch unreachable; never lose cargo.
                }
                state.Shots[i] = shot;
            }
        }

        private void AdvanceCatapultReceiver(ref CatapultState state)
        {
            if (state.ReceiverCount <= 0) return;
            int front = BeltNext(state.ReceiverCell, world.Belts[state.ReceiverCell].Facing);
            if (front >= 0 && front < world.Belts.Length)
            {
                ref var belt = ref world.Belts[front];
                if (belt.FactionId == state.FactionId && BeltCanReceiveLoad(belt, state.ReceiverKind))
                {
                    int amount = System.Math.Min(state.ReceiverCount, BeltCapacity - BeltItemCount(belt));
                    PutOnBelt(ref belt, state.ReceiverKind, amount);
                    state.ReceiverCount -= amount;
                    RecordLargeBeltDelivery(state.FactionId, state.ReceiverKind, amount);
                    if (state.ReceiverCount == 0) state.ReceiverKind = 0;
                    return;
                }
                if (FeedsOwnCore(front, state.FactionId))
                {
                    int amount = state.ReceiverCount;
                    ResourceKind item = state.ReceiverKind;
                    AddStock(state.FactionId, item, amount);
                    state.ReceiverCount = 0; state.ReceiverKind = 0;
                    RecordLargeBeltDelivery(state.FactionId, item, amount);
                    return;
                }
            }
            if (TryFeedBuilding(front, state.FactionId, state.ReceiverKind))
            {
                state.ReceiverCount--; RecordLargeBeltDelivery(state.FactionId, state.ReceiverKind, 1);
                if (state.ReceiverCount == 0) state.ReceiverKind = 0;
                return;
            }
            if (BeltDropOffStockDestination(front, state.FactionId, state.ReceiverKind))
            {
                int amount = state.ReceiverCount;
                ResourceKind item = state.ReceiverKind;
                AddStock(state.FactionId, item, amount);
                state.ReceiverCount = 0; state.ReceiverKind = 0;
                RecordLargeBeltDelivery(state.FactionId, item, amount);
            }
        }

        private int CatapultShotCount(CatapultState state)
        {
            int count = 0;
            for (int i = 0; i < state.Shots.Length; i++) if (state.Shots[i].Active) count++;
            return count;
        }
    }
}
