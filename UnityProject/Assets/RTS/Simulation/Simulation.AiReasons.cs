using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // This is an ephemeral hand-off from an AI decision to the placement boundary. It is
        // deliberately outside WorldState: it must never affect the simulation when AiReasons is off.
        private AiReason pendingAiReason;
        private int pendingAiReasonValue;

        private void SetAiReason(AiReason reason, int value)
        {
            if (!AiReasonsOn) return;
            pendingAiReason = reason;
            pendingAiReasonValue = value;
        }

        private (AiReason reason, int value) TakeAiReason(bool automatic)
        {
            if (!AiReasonsOn)
            {
                pendingAiReason = AiReason.None;
                pendingAiReasonValue = 0;
                return (AiReason.None, 0);
            }
            var result = automatic
                ? (pendingAiReason == AiReason.None ? AiReason.Unspecified : pendingAiReason, pendingAiReasonValue)
                : (AiReason.None, 0);
            pendingAiReason = AiReason.None;
            pendingAiReasonValue = 0;
            return result;
        }

        private int MetalLineNumber(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId == faction && (line.Kind == ProcessingLineKind.CoreMetal || line.Kind == ProcessingLineKind.Steel)
                    && IsAutoLine(i)) count++;
            }
            return System.Math.Max(1, count);
        }
    }
}
