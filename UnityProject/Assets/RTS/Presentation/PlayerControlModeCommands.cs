using System;

namespace Rts.Presentation
{
    /// <summary>One host-side action in the all-manual/automatic control transition.</summary>
    public enum PlayerControlCommandKind : byte
    {
        SetAutoEconomy,
        StopVillagers,
        ReturnEconomyToAuto,
        SwitchOwnDoctrine,
        StopArmies,
        SetAutoAssault
    }

    /// <summary>Unity-free description of the logged commands sent by a control-mode transition.</summary>
    public readonly struct PlayerControlCommand
    {
        public PlayerControlCommandKind Kind { get; }
        public bool Enabled { get; }
        public string Doctrine { get; }

        public PlayerControlCommand(PlayerControlCommandKind kind, bool enabled = false, string doctrine = null)
        {
            Kind = kind;
            Enabled = enabled;
            Doctrine = doctrine ?? "";
        }
    }

    public static class PlayerControlModeCommands
    {
        /// <summary>Returns the deterministic command order used for one mode change or match restart.</summary>
        public static PlayerControlCommand[] For(bool manual, string rememberedDoctrine)
        {
            if (manual)
                return new[]
                {
                    new PlayerControlCommand(PlayerControlCommandKind.SetAutoEconomy, false),
                    new PlayerControlCommand(PlayerControlCommandKind.StopVillagers),
                    new PlayerControlCommand(PlayerControlCommandKind.SwitchOwnDoctrine, doctrine: "none"),
                    new PlayerControlCommand(PlayerControlCommandKind.StopArmies),
                    new PlayerControlCommand(PlayerControlCommandKind.SetAutoAssault, false)
                };

            return new[]
            {
                new PlayerControlCommand(PlayerControlCommandKind.SetAutoEconomy, true),
                new PlayerControlCommand(PlayerControlCommandKind.ReturnEconomyToAuto),
                new PlayerControlCommand(PlayerControlCommandKind.SwitchOwnDoctrine, doctrine: rememberedDoctrine ?? "none"),
                new PlayerControlCommand(PlayerControlCommandKind.SetAutoAssault, true)
            };
        }
    }
}
