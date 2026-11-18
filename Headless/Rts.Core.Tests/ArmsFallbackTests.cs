using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmsFallbackTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool fallback)
        {
            var scenario = MapGenerator.GenerateTerrain(125125UL, false, true);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.ProcessingChain = true;
            e.CraftTree1 = true; e.CraftTree2 = true; e.CraftRequired = true;
            e.ArmsFallback = fallback; e.ArmsFallbackHpPermille = 850; e.ArmsFallbackDamagePermille = 850;
            e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.StartFood = 10000; e.StartWood = 10000; e.StartMetal = 10000;
            return scenario;
        }

        [Test]
        public void NoArmsPaysNormalWoodAndProducesWeakInfantry()
        {
            var source = Scenario(true);
            var simulation = new Battle(source);
            SetEconomy(simulation, "Age", 2); SetEconomy(simulation, "ArmsKit", 0); SetEconomy(simulation, "FineArmsKit", 0); SetEconomy(simulation, "EliteKit", 0);
            SetEconomy(simulation, "Food", 10000); SetEconomy(simulation, "Wood", 10000); SetEconomy(simulation, "Metal", 10000);
            var canPay = typeof(Battle).GetMethod("CanPay", Private, null, new[] { typeof(uint), typeof(UnitKind) }, null);
            var cost = typeof(Battle).GetMethod("CostOf", Private, null, new[] { typeof(uint), typeof(UnitKind) }, null);
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.True);
            Assert.That(Get(cost.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), "Item2"), Is.GreaterThan(0));

            var building = NewBuilding();
            var enqueueArgs = new object[] { 1u, building, UnitKind.Infantry };
            Invoke(simulation, "Enqueue", enqueueArgs);
            building = enqueueArgs[1];
            var queue = (Array)Get(building, "QueueArmsFallback", false);
            Assert.That(queue, Is.Not.Null);
            Assert.That(queue.Length, Is.EqualTo(1));
            Assert.That((bool)queue.GetValue(0), Is.True);

            var world = World(simulation);
            var spawn = typeof(Battle).GetMethods(Private).Single(m => m.Name == "Spawn" && m.GetParameters().Length == 6);
            var core = source.Cores[0];
            Assert.That((bool)spawn.Invoke(simulation, new object[] { 1u, GoalKind.Core, core.Id, core.Position, UnitKind.Infantry, UnitKind.Infantry }), Is.True);
            int index = (int)Get(world, "NextSoldierId") - 1;
            Invoke(simulation, "ApplyClass", index - 1, UnitKind.Infantry);
            var soldier = ((Array)Get(world, "Soldiers", false)).GetValue(index - 1);
            var parameters = GetObject(soldier, "Parameters");
            int baseHp = Get(parameters, "Hp");
            int baseDamage = Get(parameters, "Damage");
            Invoke(simulation, "ApplyArmsFallback", index - 1);
            soldier = ((Array)Get(world, "Soldiers", false)).GetValue(index - 1);
            parameters = GetObject(soldier, "Parameters");
            Assert.That(Get(parameters, "Hp"), Is.EqualTo((baseHp * 850 + 999) / 1000));
            Assert.That(Get(parameters, "Damage"), Is.EqualTo((baseDamage * 850 + 999) / 1000));
        }

        [Test]
        public void AvailableArmsArePreferredAndUseNoWood()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", 2); SetEconomy(simulation, "ArmsKit", 1); SetEconomy(simulation, "FineArmsKit", 0); SetEconomy(simulation, "EliteKit", 0);
            SetEconomy(simulation, "Food", 10000); SetEconomy(simulation, "Wood", 10000); SetEconomy(simulation, "Metal", 10000);
            int wood = GetEconomy(simulation, "Wood");
            var building = NewBuilding();
            var enqueueArgs = new object[] { 1u, building, UnitKind.Infantry };
            Invoke(simulation, "Enqueue", enqueueArgs);
            building = enqueueArgs[1];
            Assert.That(GetEconomy(simulation, "Wood"), Is.EqualTo(wood));
            Assert.That(GetEconomy(simulation, "ArmsKit"), Is.EqualTo(0));
            var queue = (Array)Get(building, "QueueArmsFallback", false);
            Assert.That((bool)queue.GetValue(0), Is.False);
        }

        [Test]
        public void CancellingFallbackRefundsItsWood()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", 2); SetEconomy(simulation, "ArmsKit", 0); SetEconomy(simulation, "FineArmsKit", 0); SetEconomy(simulation, "EliteKit", 0);
            SetEconomy(simulation, "Food", 10000); SetEconomy(simulation, "Wood", 10000); SetEconomy(simulation, "Metal", 10000);
            int wood = GetEconomy(simulation, "Wood");
            var building = NewBuilding();
            var enqueueArgs = new object[] { 1u, building, UnitKind.Infantry };
            Invoke(simulation, "Enqueue", enqueueArgs);
            building = enqueueArgs[1];
            Assert.That(GetEconomy(simulation, "Wood"), Is.LessThan(wood));
            var cancelArgs = new object[] { 1u, building };
            Invoke(simulation, "CancelLast", cancelArgs);
            building = cancelArgs[1];
            Assert.That(GetEconomy(simulation, "Wood"), Is.EqualTo(wood));
            Assert.That(Get(building, "Queued"), Is.EqualTo(0));
        }

        [Test]
        public void FlagOffStillRejectsTrainingWithoutArms()
        {
            var simulation = new Battle(Scenario(false));
            SetEconomy(simulation, "Age", 2); SetEconomy(simulation, "ArmsKit", 0); SetEconomy(simulation, "FineArmsKit", 0); SetEconomy(simulation, "EliteKit", 0);
            var canPay = typeof(Battle).GetMethod("CanPay", Private, null, new[] { typeof(uint), typeof(UnitKind) }, null);
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.False);
        }

        [Test]
        public void Extension125RoundTripsFallbackRules()
        {
            var source = Scenario(true);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 125);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.ArmsFallback, Is.True);
            Assert.That(decoded.Economy.ArmsFallbackHpPermille, Is.EqualTo(850));
            Assert.That(decoded.Economy.ArmsFallbackDamagePermille, Is.EqualTo(850));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false)));
            Assert.That(off.Economy.ArmsFallback, Is.False);
            Assert.That(off.Extensions.Any(x => x.Id == 125), Is.False);
        }

        private static object World(Battle simulation) => typeof(Battle).GetField("world", Private).GetValue(simulation);
        private static object GetObject(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
        private static int Get(object value, string name) => (int)Get(value, name, true);
        private static object Get(object value, string name, bool convert)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var result = field.GetValue(value);
            return convert ? (result is bool b ? (b ? 1 : 0) : Convert.ToInt32(result)) : result;
        }
        private static int GetEconomy(Battle simulation, string name)
        {
            var world = World(simulation);
            var economies = (Array)Get(world, "Economies", false);
            return Get(economies.GetValue(0), name);
        }
        private static void SetEconomy(Battle simulation, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", Private);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue(0);
            Set(economy, name, value);
            economies.SetValue(economy, 0);
        }
        private static void Set(object value, string name, object fieldValue)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fieldValue != null && !field.FieldType.IsInstanceOfType(fieldValue))
                fieldValue = field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, fieldValue) : Convert.ChangeType(fieldValue, field.FieldType);
            field.SetValue(value, fieldValue);
        }
        private static object NewBuilding()
        {
            var value = Activator.CreateInstance(typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState"));
            Set(value, "FactionId", 1u); Set(value, "Kind", BuildingKind.Barracks); Set(value, "Complete", true); Set(value, "Alive", true);
            return value;
        }
        private static object Invoke(Battle simulation, string method, params object[] args)
            => typeof(Battle).GetMethod(method, Private).Invoke(simulation, args);
    }
}
