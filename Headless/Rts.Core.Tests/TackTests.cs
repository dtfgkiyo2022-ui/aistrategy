using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class TackTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
            internal void Step(int count) { for (int i = 0; i < count; i++) Gateway.Step(); }
        }

        private static ScenarioDefinition Scenario(bool tack)
        {
            var s = MapGenerator.GenerateTerrain(9137UL, false, true);
            s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Cavalry = true; s.Economy.Tack = tack; s.Economy.StartFood = 1000; s.Economy.StartWood = 5000;
            s.Economy.SaddleryWoodCost = 1; s.Economy.SaddleryWork = 1; s.Economy.SaddleryHp = 10;
            s.Economy.TackTicks = 3; s.Economy.TackFoodInput = 2; s.Economy.TackWoodInput = 2;
            s.Economy.HeavyCavalryTackCost = 2; s.Economy.HeavyCavalryFoodCost = 70; s.Economy.HeavyCavalryTrainTicks = 5;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.StableWoodCost = 1; s.Economy.StableWork = 1; s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static bool PlaceSaddlery(Fixture f)
        {
            int before = int.Parse(Fields(f.Simulation)["Buildings.Count"], CultureInfo.InvariantCulture);
            int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Saddlery, cell, Facing.North));
                f.Step(1);
                if (int.Parse(Fields(f.Simulation)["Buildings.Count"], CultureInfo.InvariantCulture) > before) return true;
            }
            return false;
        }

        [Test]
        public void TackExtensionRoundTripsIdTwentyTwoAndChangedValues()
        {
            var s = Scenario(true);
            s.Economy.SaddleryWoodCost = 97; s.Economy.TackTicks = 111; s.Economy.HeavyCavalrySpeed = Fix64.FromInt(5);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(s));
            Assert.That(decoded.Economy.Tack, Is.True);
            Assert.That(decoded.Economy.SaddleryWoodCost, Is.EqualTo(97));
            Assert.That(decoded.Economy.TackTicks, Is.EqualTo(111));
            Assert.That(decoded.Economy.HeavyCavalrySpeed, Is.EqualTo(Fix64.FromInt(5)));
        }

        [Test]
        public void TackOffRejectsSaddleryAndTackLineWithoutCreatingStock()
        {
            var f = new Fixture(Scenario(false));
            f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Saddlery, 0, Facing.North));
            f.Send(EconomyCommand.RequestLineAt(1, ++f.Sequence, ProcessingLineKind.Tack, 0));
            f.Step(1);
            var fields = Fields(f.Simulation);
            Assert.That(fields.ContainsKey("Economy[1].Tack"), Is.False);
            Assert.That(fields["Buildings.Count"], Is.EqualTo("0"));
        }

        [Test]
        public void TackOnAllowsCavalryCivToUseSaddleryButNotOtherCiv()
        {
            var cavalry = new Fixture(Scenario(true));
            cavalry.Send(EconomyCommand.Advance(1, ++cavalry.Sequence, CivKind.Cavalry));
            cavalry.Step(2);
            Assert.That(PlaceSaddlery(cavalry), Is.True);

            var other = new Fixture(Scenario(true));
            other.Send(EconomyCommand.Advance(1, ++other.Sequence, CivKind.Agrarian));
            other.Step(2);
            other.Send(EconomyCommand.Place(1, ++other.Sequence, BuildingKind.Saddlery, 100, Facing.North));
            other.Step(1);
            Assert.That(Fields(other.Simulation)["Buildings.Count"], Is.EqualTo("0"));
        }
    }
}
