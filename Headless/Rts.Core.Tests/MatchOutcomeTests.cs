using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Core.Tests
{
    public sealed class MatchOutcomeTests
    {
        private static MatchResult Ended(uint winner, bool draw = false, bool fault = false, bool undecided = false) =>
            new MatchResult(true, winner, draw, fault, undecided);

        [Test]
        public void ARunningMatchHasNoOutcome()
        {
            Assert.That(MatchOutcome.Describe(new MatchResult(false, 0, false, false, false), 1, 100), Is.Null);
        }

        [Test]
        public void TheWinnerSeesVictoryAndTheLoserSeesDefeat()
        {
            Assert.That(MatchOutcome.Describe(Ended(1), 1, 5428).Value.Headline, Is.EqualTo("Victory"));
            Assert.That(MatchOutcome.Describe(Ended(1), 2, 5428).Value.Headline, Is.EqualTo("Defeat"));
            Assert.That(MatchOutcome.Describe(Ended(2), 1, 5428).Value.Headline, Is.EqualTo("Defeat"));
        }

        [Test]
        public void DrawFaultAndUndecidedAreNeverShownAsAWin()
        {
            Assert.That(MatchOutcome.Describe(Ended(0, draw: true), 1, 10).Value.Headline, Is.EqualTo("Draw"));
            Assert.That(MatchOutcome.Describe(Ended(0, fault: true), 1, 10).Value.Headline, Is.EqualTo("Match aborted"));
            Assert.That(MatchOutcome.Describe(Ended(0, undecided: true), 1, 10).Value.Headline, Is.EqualTo("No winner"));
            // A fault carries winner 0, but a stray winner must not turn an aborted match into a victory.
            Assert.That(MatchOutcome.Describe(Ended(1, fault: true), 1, 10).Value.Headline, Is.EqualTo("Match aborted"));
        }

        [Test]
        public void TheClockIsMinutesAndSecondsOfTheScenarioTickRate()
        {
            Assert.That(MatchOutcome.Clock(5428), Is.EqualTo("4:31"));
            Assert.That(MatchOutcome.Clock(0), Is.EqualTo("0:00"));
            Assert.That(MatchOutcome.Clock(1200), Is.EqualTo("1:00"));
            Assert.That(MatchOutcome.Clock(-5), Is.EqualTo("0:00"));
            Assert.That(MatchOutcome.Describe(Ended(1), 1, 5428).Value.Detail, Does.Contain("4:31").And.Contain("t=5428"));
        }
    }
}
