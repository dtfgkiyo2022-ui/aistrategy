using NUnit.Framework;
using UnityEngine;
using Rts.Presentation;
using Rts.UnityHost;

namespace Rts.Tests.EditMode
{
    public sealed class FineJudgementEditModeTests
    {
        private bool japanese;

        [SetUp]
        public void SetUp()
        {
            japanese = UiText.Japanese;
            UiText.Japanese = true;
        }

        [TearDown]
        public void TearDown()
        {
            UiText.Japanese = japanese;
        }

        [Test]
        public void FineJudgementHasThreeChoicesAndForwardsSelection()
        {
            var gameObject = new GameObject("fine-judgement-test");
            try
            {
                var panel = gameObject.AddComponent<CommandPanel>();
                var fake = new FakeFineJudgement();
                panel.FineJudgement = fake;

                Assert.That(fake.Options, Is.EqualTo(new[] { FineJudgementOption.Off, FineJudgementOption.Jev, FineJudgementOption.LocalLlm }));
                Assert.That(panel.TrySelectFineJudgement(FineJudgementOption.LocalLlm), Is.True);
                Assert.That(fake.Mode, Is.EqualTo(FineJudgementOption.LocalLlm));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void JevChoiceIsRejectedWhenItsKeyIsMissing()
        {
            var gameObject = new GameObject("fine-judgement-key-test");
            try
            {
                var panel = gameObject.AddComponent<CommandPanel>();
                var fake = new FakeFineJudgement { JevKeyAvailable = false };
                panel.FineJudgement = fake;

                Assert.That(panel.TrySelectFineJudgement(FineJudgementOption.Jev), Is.False);
                Assert.That(fake.Mode, Is.EqualTo(FineJudgementOption.Off));
                Assert.That(CommandPanel.FineJudgementLabel(FineJudgementOption.Jev, false), Is.EqualTo("Jev（キー未設定）"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HostKeyReaderCanBeReplacedWithoutReadingTheEnvironment()
        {
            var previous = LiveMatchHost.FineJudgementKeyReader;
            var gameObject = new GameObject("fine-judgement-host-key-test");
            try
            {
                LiveMatchHost.FineJudgementKeyReader = () => null;
                var host = gameObject.AddComponent<LiveMatchHost>();
                Assert.That(host.JevKeyAvailable, Is.False);
                host.Mode = FineJudgementOption.Jev;
                Assert.That(host.Mode, Is.EqualTo(FineJudgementOption.Off));
            }
            finally
            {
                LiveMatchHost.FineJudgementKeyReader = previous;
                Object.DestroyImmediate(gameObject);
            }
        }

        private sealed class FakeFineJudgement : IFineJudgementControl
        {
            public string[] Options { get; } = { FineJudgementOption.Off, FineJudgementOption.Jev, FineJudgementOption.LocalLlm };
            public string Mode { get; set; } = FineJudgementOption.Off;
            public bool JevKeyAvailable { get; set; } = true;
            public string Description { get; } = "説明";
        }
    }
}
