using System;
using System.Collections.Generic;
namespace Rts.Contracts
{
    // Own live soldiers, in soldier-ID order. No enemy data or world callbacks.
    public sealed class OffenseArmyInput
    {
        public uint ArmyId { get; }
        public IReadOnlyList<SimPoint> Soldiers { get; }
        public long StepRaw { get; }
        public bool LossReached { get; }
        public bool HomeArrived { get; }
        public int BaselineCount { get; }
        public int LostCount { get; }
        public OffenseArmyInput(uint armyId, IReadOnlyList<SimPoint> soldiers, long stepRaw, bool lossReached, bool homeArrived, int baselineCount = 0, int lostCount = 0)
        { ArmyId = armyId; Soldiers = ContractList.Copy(soldiers); StepRaw = stepRaw; LossReached = lossReached; HomeArrived = homeArrived; BaselineCount = baselineCount; LostCount = lostCount; }
    }
    public sealed class OffenseRouteInput
    {
        public PolicyGoal Goal { get; }
        public SimPoint Rally { get; }
        public long HomeDistanceRaw { get; }
        public IReadOnlyList<SimPoint> ToTarget { get; }
        public IReadOnlyList<ObjectiveRoute> ToRally { get; }
        // ToRally route Goal.Id identifies an own army, not an objective.
        public OffenseRouteInput(PolicyGoal goal, SimPoint rally, IReadOnlyList<SimPoint> toTarget, IReadOnlyList<ObjectiveRoute> toRally, long homeDistanceRaw = 0)
        { Goal = goal; Rally = rally; HomeDistanceRaw = homeDistanceRaw; ToTarget = ContractList.Copy(toTarget); ToRally = ContractList.Copy(toRally); }
    }
}
