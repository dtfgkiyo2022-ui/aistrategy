using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmsSupplyTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void LargeLiveRulesUseTheNewClockAndEnableArmsSupply()
        {
            var enabled = LiveGameRules.Create(1, true, true, false, false).Economy;
            Assert.That(enabled.ArmsSupply, Is.True);
            Assert.That(new[] { enabled.AgeClockTicks1, enabled.AgeClockTicks2, enabled.AgeClockTicks3, enabled.AgeClockTicks4 },
                Is.EqualTo(new[] { 3600, 7200, 10800, 14400 }));
            Assert.That(LiveGameRules.Create(1, true, true, false, false, armsSupplyOff: true).Economy.ArmsSupply, Is.False);
        }

        [Test]
        public void Extension121RoundTripsAndOffProfileOmitsIt()
        {
            var enabled = LiveGameRules.Create(2, true, true, false, false);
            var bytes = ScenarioBinary.Encode(enabled);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.ArmsSupply, Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 121).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var disabled = LiveGameRules.Create(2, true, true, false, false, armsSupplyOff: true);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(disabled)).Extensions.Any(x => x.Id == 121), Is.False);
        }

        [Test]
        public void FlagOffRoundTripKeepsCanonicalHashesIdentical()
        {
            var source = LiveGameRules.Create(3, true, true, false, false, armsSupplyOff: true);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ArmsSupplyRestrictsAutomaticConstructionToTheWeaponLineAndPlankFreeBuildings()
        {
            var scenario = LiveGameRules.Create(41001, true, true, false, false);
            scenario.Economy.ArmsFallback = false;
            var simulation = new Battle(scenario);
            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);

            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.Assembler), Is.True);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.Sawmill), Is.True);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.CharcoalKiln), Is.True);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.House), Is.True);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.Barracks), Is.True);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.Joinery), Is.False);
            Assert.That((bool)Invoke(simulation, "ArmsSupplyAllowsBuilding", 1u, BuildingKind.Scriptorium), Is.False);
        }

        [Test]
        public void ArmsSupplyRaisesAssemblerTargetToTwoAtAgeOneAndThreeAtAgeTwo()
        {
            var simulation = new Battle(LiveGameRules.Create(41002, true, true, false, false));
            SetEconomy(simulation, 0, "Age", 1);
            Assert.That((int)Invoke(simulation, "CraftRequiredAiAssemblerTarget", 1u), Is.EqualTo(2));
            SetEconomy(simulation, 0, "Age", 2);
            Assert.That((int)Invoke(simulation, "CraftRequiredAiAssemblerTarget", 1u), Is.EqualTo(3));
            SetEconomy(simulation, 0, "Age", 3);
            Assert.That((int)Invoke(simulation, "CraftRequiredAiAssemblerTarget", 1u), Is.EqualTo(4));
        }

        [Test]
        public void ArmsSupplyChoosesArmsRecipeDuringAgeOneBeforeTheTrainingGate()
        {
            var simulation = new Battle(LiveGameRules.Create(41003, true, true, false, false));
            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            Assert.That((RecipeKind)Invoke(simulation, "PreferredAssemblerRecipe", 1u), Is.EqualTo(RecipeKind.ArmsKit));

            var ageThreeProbe = LiveGameRules.Create(41004, true, true, false, false);
            ageThreeProbe.Economy.CraftRequiredAge = 3;
            var ageThree = new Battle(ageThreeProbe);
            SetEconomy(ageThree, 0, "Age", 2);
            SetEconomy(ageThree, 0, "ArmsKit", 0);
            SetEconomy(ageThree, 0, "FineArmsKit", 0);
            var canPay = typeof(Battle).GetMethod("CanPay", Private, null, new[] { typeof(uint), typeof(UnitKind) }, null);
            Assert.That((bool)canPay.Invoke(ageThree, new object[] { 1u, UnitKind.Infantry }), Is.True,
                "時代3指定では時代2の武具必須を発生させない");
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = World(simulation);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            var economy = economies.GetValue(index);
            var member = economy.GetType().GetField(name, Private);
            member.SetValue(economy, Convert.ChangeType(value, member.FieldType));
            economies.SetValue(economy, index);
        }

        private static object Invoke(Battle simulation, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Private).Invoke(simulation, args);
    }
}
