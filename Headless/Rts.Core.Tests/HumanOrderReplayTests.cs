using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Rts.Core.Tests
{
    public sealed class HumanOrderReplayTests
    {
        private const string Focus = "{\"tick\":20,\"kind\":\"Focus\",\"target\":{\"factionId\":1,\"kind\":\"Army\",\"id\":1},\"goal\":{\"kind\":\"Core\",\"id\":2,\"point\":{\"x\":0,\"z\":0}},\"priority\":100,\"allowedLossPermille\":1000,\"end\":{\"kind\":\"UntilReplaced\",\"tick\":0},\"reservePermille\":0,\"validUntilTick\":36000,\"maxObservationAgeTicks\":0,\"expire\":\"None\",\"sequence\":1}";
        private const string Missing = "{\"tick\":40,\"kind\":\"Focus\",\"target\":{\"factionId\":1,\"kind\":\"Army\",\"id\":99},\"goal\":{\"kind\":\"Core\",\"id\":2,\"point\":{\"x\":0,\"z\":0}},\"priority\":100,\"allowedLossPermille\":1000,\"end\":{\"kind\":\"UntilReplaced\",\"tick\":0},\"reservePermille\":0,\"validUntilTick\":36000,\"maxObservationAgeTicks\":0,\"expire\":\"None\",\"sequence\":2}";

        [Test]
        public void HumanOrdersReplayIntoTacticMatchAndComeBackOutOfThePack()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "human-replay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string orders = Path.Combine(root, "orders.jsonl");
                File.WriteAllText(orders, Focus + "\n" + Missing + "\n", new UTF8Encoding(false));
                string pack = Path.Combine(root, "pack");
                string log = Path.Combine(root, "log.jsonl");
                int exit = Rts.Headless.Cli.Program.Main(new[]
                {
                    "tactic-match", "--map-seed", "1", "--ticks", "200", "--west-tactic", "idle", "--east-tactic", "idle",
                    "--west-human", orders, "--pack-out", pack, "--log-out", log
                });
                Assert.That(exit, Is.EqualTo(0));
                string logText = File.ReadAllText(log);
                Assert.That(logText, Does.Contain("\"humanReplay\""));
                Assert.That(logText, Does.Contain("\"targetMissing\":1"));

                string extracted = Path.Combine(root, "extracted.jsonl");
                Assert.That(Rts.Headless.Cli.Program.Main(new[] { "human-orders", "--pack", pack, "--faction", "1", "--out", extracted }), Is.EqualTo(0));
                var lines = File.ReadAllLines(extracted).Where(l => l.Trim().Length != 0).ToArray();
                Assert.That(lines.Length, Is.EqualTo(1), "the order whose army does not exist is not sent, so only one human order is in the pack");
                Assert.That(lines[0], Does.Contain("\"kind\":\"Focus\""));
                Assert.That(lines[0], Does.Contain("\"tick\":20"));

                string pack2 = Path.Combine(root, "pack2");
                Assert.That(Rts.Headless.Cli.Program.Main(new[]
                {
                    "tactic-match", "--map-seed", "1", "--ticks", "200", "--west-tactic", "idle", "--east-tactic", "idle",
                    "--west-human", orders, "--pack-out", pack2
                }), Is.EqualTo(0));
                Assert.That(File.ReadAllBytes(Path.Combine(pack2, "replay.rpl")), Is.EqualTo(File.ReadAllBytes(Path.Combine(pack, "replay.rpl"))),
                    "the same human orders give the same match");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
