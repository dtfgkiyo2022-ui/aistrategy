using NUnit.Framework;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class PlayerControlModeCommandsTests
    {
        [Test]
        public void ManualModeStopsEconomyDoctrineAndAssaultInOrder()
        {
            var commands = PlayerControlModeCommands.For(true, "maintain");

            Assert.That(commands.Length, Is.EqualTo(5));
            Assert.That(commands[0].Kind, Is.EqualTo(PlayerControlCommandKind.SetAutoEconomy));
            Assert.That(commands[0].Enabled, Is.False);
            Assert.That(commands[1].Kind, Is.EqualTo(PlayerControlCommandKind.StopVillagers));
            Assert.That(commands[2].Kind, Is.EqualTo(PlayerControlCommandKind.SwitchOwnDoctrine));
            Assert.That(commands[2].Doctrine, Is.EqualTo("none"));
            Assert.That(commands[3].Kind, Is.EqualTo(PlayerControlCommandKind.StopArmies));
            Assert.That(commands[4].Kind, Is.EqualTo(PlayerControlCommandKind.SetAutoAssault));
            Assert.That(commands[4].Enabled, Is.False);
        }

        [Test]
        public void AutomaticModeReturnsEconomyDoctrineAndAssaultInOrder()
        {
            var commands = PlayerControlModeCommands.For(false, "concentrate");

            Assert.That(commands.Length, Is.EqualTo(4));
            Assert.That(commands[0].Kind, Is.EqualTo(PlayerControlCommandKind.SetAutoEconomy));
            Assert.That(commands[0].Enabled, Is.True);
            Assert.That(commands[1].Kind, Is.EqualTo(PlayerControlCommandKind.ReturnEconomyToAuto));
            Assert.That(commands[2].Kind, Is.EqualTo(PlayerControlCommandKind.SwitchOwnDoctrine));
            Assert.That(commands[2].Doctrine, Is.EqualTo("concentrate"));
            Assert.That(commands[3].Kind, Is.EqualTo(PlayerControlCommandKind.SetAutoAssault));
            Assert.That(commands[3].Enabled, Is.True);
        }
    }
}
