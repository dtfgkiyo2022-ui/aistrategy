using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class RationsTests
    {
        [Test]
        public void RationsExtensionRoundTripsAsId20AndOffOmitsIt()
        {
            var scenario = MapGenerator.GenerateTerrain(7301UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.ProcessingChain = true;
            var offBytes = ScenarioBinary.Encode(scenario);
            scenario.Economy.Rations = true;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 20);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.Rations, Is.True);
            Assert.That(decoded.Economy.MillTicks, Is.EqualTo(scenario.Economy.MillTicks));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            scenario.Economy.Rations = false;
            Assert.That(ScenarioBinary.Encode(scenario), Is.EqualTo(offBytes));
            Assert.That(ScenarioBinary.Decode(offBytes).Extensions.Any(x => x.Id == 20), Is.False);
        }
    }
}
