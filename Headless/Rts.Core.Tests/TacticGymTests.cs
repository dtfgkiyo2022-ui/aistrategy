using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Headless.Cli;
using Rts.Simulation;

namespace Rts.Core.Tests
{

[TestFixture]
public sealed class TacticGymTests
{
    [Test]
    public void ResetAndStepAcceptsCommandsAndReportsInfo()
    {
        var session = new TacticGymSession(MapGenerator.Generate(1001, true), 1, "auto", 12000);
        var result = session.Step("{\"version\":1,\"commands\":[]}", 20);

        Assert.That(result.Tick, Is.EqualTo(20));
        Assert.That(result.ViewJson, Does.Contain("\"version\":1"));
        Assert.That(result.Info.ContainsKey("rejected"), Is.True);
        Assert.That(result.Info.ContainsKey("ownSoldiers"), Is.True);
    }

    [Test]
    public void MalformedCommandDoesNotDestroySession()
    {
        var session = new TacticGymSession(MapGenerator.Generate(1002, true), 1, "idle", 12000);
        Assert.Throws<FormatException>(() => session.Step("{\"version\":1}", 20));
        var result = session.Step("{\"version\":1,\"commands\":[]}", 20);
        Assert.That(result.Tick, Is.EqualTo(20));
    }

    [Test]
    public void TerminalStateContainsWinner()
    {
        var scenario = MapGenerator.Generate(1003, true);
        scenario.Cores[1].Hp = 0;
        var session = new TacticGymSession(scenario, 1, "auto", 12000);
        var result = session.Step("{\"version\":1,\"commands\":[]}", 1);

        Assert.That(result.Done, Is.True);
        Assert.That(result.Winner, Is.EqualTo(1));
        Assert.That(result.Reward, Is.EqualTo(1));
    }

    [Test]
    public void SameSeedAndCommandsProduceSameViewAndRewardSequence()
    {
        var left = new TacticGymSession(MapGenerator.Generate(1004, true), 1, "auto", 12000);
        var right = new TacticGymSession(MapGenerator.Generate(1004, true), 1, "auto", 12000);
        for (int i = 0; i < 5; i++)
        {
            var a = left.Step("{\"version\":1,\"commands\":[]}", 20);
            var b = right.Step("{\"version\":1,\"commands\":[]}", 20);
            Assert.That(a.ViewJson, Is.EqualTo(b.ViewJson));
            Assert.That(a.Reward, Is.EqualTo(b.Reward));
            Assert.That(a.Tick, Is.EqualTo(b.Tick));
        }
    }
}
}
