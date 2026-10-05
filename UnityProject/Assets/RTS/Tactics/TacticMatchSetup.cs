using System;
using Rts.Contracts;

namespace Rts.Tactics
{
    /// <summary>One side's tactic selection and the objects needed to run it.</summary>
    public sealed class TacticMatchSide
    {
        internal TacticMatchSide(string selection, TacticHost host, string preset)
        {
            Selection = selection ?? "";
            Host = host;
            Preset = preset;
        }

        public string Selection { get; }
        public TacticHost Host { get; }
        /// <summary>The policy preset to use for this side; a tactic owns the side, so it is none.</summary>
        public string Preset { get; }
        public bool HasTactic => Host != null;
    }

    /// <summary>Unity-independent construction shared by the headless and Unity match hosts.</summary>
    public static class TacticMatchSetup
    {
        public const string None = "";

        public static TacticMatchSide Create(
            uint factionId,
            string selection,
            Func<string, ITacticRuntime> loadRuntime,
            IFrameSource frames,
            ICommandPort commandPort,
            IEconomyPort economyPort,
            ITacticGlobalPolicyPort globalPolicyPort = null)
        {
            if (string.IsNullOrEmpty(selection)) return new TacticMatchSide(None, null, null);
            if (loadRuntime == null) throw new ArgumentNullException(nameof(loadRuntime));
            var runtime = loadRuntime(selection) ?? throw new InvalidOperationException("戦術ランタイムを作成できません。");
            var host = new TacticHost(factionId, frames, commandPort, economyPort, runtime, globalPolicyPort);
            return new TacticMatchSide(selection, host, "none");
        }
    }
}
