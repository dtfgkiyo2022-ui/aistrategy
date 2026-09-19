using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.UnityHost
{
    /// <summary>Accepts commands without a simulation behind it, so the input UI can be checked on its own.</summary>
    public sealed class MockCommandPort : ICommandPort
    {
        private ulong nextId = 1;

        public List<UserPolicyIntent> Submitted { get; } = new List<UserPolicyIntent>();

        public ulong Submit(UserPolicyIntent intent)
        {
            Submitted.Add(intent);
            return nextId++;
        }

        public void Cancel(ulong requestId) { }

        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
        {
            return nextId++;
        }
    }
}
