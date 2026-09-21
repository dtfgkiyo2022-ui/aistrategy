using System.Collections.Generic;
using NUnit.Framework;
using Rts.Headless.Cli;

namespace Rts.Tests.Headless
{
    /// <summary>
    /// Issue 4-1 (#28): the message `compare` prints for chapter 13.3, "which phase of the tick first differs".
    /// This runs when a determinism check has already failed, so every branch must produce a usable sentence
    /// instead of an exception.
    /// </summary>
    public sealed class PhaseDiffTests
    {
        private static List<KeyValuePair<string, string>> Phases(params string[] pairs)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < pairs.Length; i += 2) list.Add(new KeyValuePair<string, string>(pairs[i], pairs[i + 1]));
            return list;
        }

        private const string HashA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string HashB = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        [Test]
        public void TheFirstDifferingPhaseIsReportedByItsPositionAndName()
        {
            var left = Phases("Commands", HashA, "AI", HashA, "Commands", HashA);
            var right = Phases("Commands", HashA, "AI", HashB, "Commands", HashA);
            var message = Program.FirstDifferentPhase(7, left, right);
            Assert.That(message, Does.Contain("tick=7").And.Contain("#2").And.Contain("AI"));
            Assert.That(message, Does.Contain(HashA[..16]).And.Contain(HashB[..16]));
        }

        [Test]
        public void APhaseNameMismatchIsCalledOutSeparately()
        {
            // Different phase order means the two builds do not even run the same tick, which is worse than a
            // differing value and must not be reported as one.
            var message = Program.FirstDifferentPhase(3, Phases("Movement", HashA), Phases("Visibility", HashA));
            Assert.That(message, Does.Contain("フェーズ名が食い違います").And.Contain("Movement").And.Contain("Visibility"));
        }

        [Test]
        public void ShortOrMissingHashesDoNotCrashTheDiagnostic()
        {
            // The hashes are 64-character hex in practice; truncating blindly used to throw on anything shorter,
            // losing the diagnosis exactly when the data was already odd.
            var message = Program.FirstDifferentPhase(1, Phases("Commands", "abc"), Phases("Commands", null));
            Assert.That(message, Does.Contain("#1").And.Contain("abc").And.Contain("(なし)"));
        }

        [Test]
        public void ATickThatEndsEarlyOnOneSideIsReportedAsSuch()
        {
            var message = Program.FirstDifferentPhase(9, Phases("Commands", HashA, "AI", HashA), Phases("Commands", HashA));
            Assert.That(message, Does.Contain("#2").And.Contain("片方のtickがここで終わっています"));
            Assert.That(message, Does.Contain("left=2").And.Contain("right=1"));
        }

        [Test]
        public void MatchingPhasesSayTheDifferenceDidNotReproduceHere()
        {
            var message = Program.FirstDifferentPhase(4, Phases("Commands", HashA), Phases("Commands", HashA));
            Assert.That(message, Does.Contain("なし").And.Contain("記録元の環境"));
        }

        [Test]
        public void ASideThatCouldNotBeRerunIsNamed()
        {
            Assert.That(Program.FirstDifferentPhase(2, null, Phases("Commands", HashA)), Does.Contain("left"));
            Assert.That(Program.FirstDifferentPhase(2, Phases("Commands", HashA), null), Does.Contain("right"));
        }
    }
}
