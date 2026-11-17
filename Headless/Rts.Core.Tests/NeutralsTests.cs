using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class NeutralsTests
    {
        [Test]
        public void LargeLiveRulesEnableDeterministicSymmetricNeutralCamps()
        {
            var left = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var right = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var a = left.Capture(1).NeutralCamps;
            var b = right.Capture(1).NeutralCamps;
            Assert.That(a.Count, Is.EqualTo(18));
            Assert.That(b.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw)),
                Is.EqualTo(a.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw))));
            for (int i = 0; i < a.Count; i += 2)
            {
                Assert.That(a[i].Kind, Is.EqualTo(a[i + 1].Kind));
                Assert.That(a[i].Position.X.Raw + a[i + 1].Position.X.Raw,
                    Is.EqualTo(a[0].Position.X.Raw + a[1].Position.X.Raw));
                Assert.That(a[i].Position.Z.Raw, Is.EqualTo(a[i + 1].Position.Z.Raw));
            }
        }

        [Test]
        public void NeutralsOffPublishesNoCampsAndDoesNotWriteExtension()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.Neutrals = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)).Economy.Neutrals, Is.False);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)).Extensions.Any(x => x.Id == 122), Is.False);
            var simulation = new Battle(scenario);
            Assert.That(simulation.Capture(1).NeutralCamps, Is.Empty);
            Assert.That(simulation.Capture(1).VisibleNeutrals, Is.Empty);
        }

        [Test]
        public void NeutralsExtensionRoundTripsByteForByte()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Neutrals, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 122), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }
    }
}
