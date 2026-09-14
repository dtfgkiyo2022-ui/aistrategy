using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public interface IPolicyProvider
    {
        void Request(PolicyRequest request);
        IReadOnlyList<PolicyReply> Poll(long tick);
    }

    public interface ITacticalDecision
    {
        ArmyIntent Decide(FactionObservation observation, OwnArmyView army, PolicyView policy);
    }

    public interface ISimulation
    {
        void Step(long tick, IReadOnlyList<ScheduledInput> inputs);
        FactionFrame Capture(uint factionId);
        DiagnosticState CaptureDiagnostic();
    }

    public interface ICommandPort
    {
        ulong Submit(UserPolicyIntent intent);
        void Cancel(ulong requestId);
    }

    public interface IFrameSource
    {
        FactionFrame Latest(uint factionId);
    }

}
