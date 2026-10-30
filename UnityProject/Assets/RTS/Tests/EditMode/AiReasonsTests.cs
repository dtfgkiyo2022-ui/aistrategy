using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class AiReasonsTests
    {
        [Test]
        public void EveryAiReasonHasJapaneseText()
        {
            foreach (AiReason reason in System.Enum.GetValues(typeof(AiReason)))
            {
                string text = UiText.AiReasonJapanese(reason, 3);
                Assert.That(text, Is.Not.Null.And.Not.Empty, reason.ToString());
                if (reason != AiReason.None) Assert.That(text.Length, Is.LessThanOrEqualTo(25), reason.ToString());
            }
        }
    }
}
