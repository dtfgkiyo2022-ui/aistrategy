using System;

namespace Rts.Contracts
{
    /// <summary>Chapter 73: the two deterministic ward types.</summary>
    public enum WardKind : byte
    {
        Hidden = 1,
        Revealing = 2
    }

    /// <summary>Read-only ward data. Enemy hidden wards are included only while detected.</summary>
    public readonly struct WardView
    {
        public uint Id { get; }
        public uint FactionId { get; }
        public WardKind Kind { get; }
        public SimPoint Position { get; }
        public int Hp { get; }
        public long PlacedTick { get; }
        public long ExpiryTick { get; }
        public bool IsDetected { get; }

        public WardView(uint id, uint factionId, WardKind kind, SimPoint position, int hp,
            long placedTick, long expiryTick, bool isDetected = false)
        {
            Id = id; FactionId = factionId; Kind = kind; Position = position; Hp = hp;
            PlacedTick = placedTick; ExpiryTick = expiryTick; IsDetected = isDetected;
        }
    }
}
