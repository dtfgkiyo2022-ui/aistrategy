using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>記録パックが部隊の現在の実効命令を帰属させるための公開診断値です。</summary>
    public readonly struct MatchPackArmyPolicy
    {
        public uint FactionId { get; }
        public uint ArmyId { get; }
        public ulong CommandId { get; }
        public bool HasCommand { get; }
        public CommandSource Source { get; }

        public MatchPackArmyPolicy(uint factionId, uint armyId, ulong commandId, bool hasCommand, CommandSource source)
        {
            FactionId = factionId;
            ArmyId = armyId;
            CommandId = commandId;
            HasCommand = hasCommand;
            Source = source;
        }
    }

    public sealed class MatchPackCreditSnapshot
    {
        public long Tick { get; }
        public IReadOnlyList<MatchPackArmyPolicy> Armies { get; }

        public MatchPackCreditSnapshot(long tick, IReadOnlyList<MatchPackArmyPolicy> armies)
        {
            Tick = tick;
            Armies = new List<MatchPackArmyPolicy>(armies ?? Array.Empty<MatchPackArmyPolicy>());
        }
    }

    public sealed partial class Simulation
    {
        /// <summary>ComposePolicies が選んだ命令だけを、記録パック用に返します。</summary>
        public MatchPackCreditSnapshot CaptureMatchPackCredit()
        {
            var result = new List<MatchPackArmyPolicy>(world.Armies.Length);
            foreach (var army in world.Armies.OrderBy(a => a.Definition.FactionId).ThenBy(a => a.Definition.Id))
            {
                var command = army.CommandId == 0
                    ? null
                    : CommandById(army.CommandId);
                result.Add(new MatchPackArmyPolicy(
                    army.Definition.FactionId,
                    army.Definition.Id,
                    army.CommandId,
                    command != null,
                    command == null ? CommandSource.Human : command.Order.Source));
            }
            return new MatchPackCreditSnapshot(world.Tick, result);
        }
    }
}
