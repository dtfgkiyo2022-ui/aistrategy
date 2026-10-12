using System;
using NUnit.Framework;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class StaffChatEditModeTests
    {
        [Test]
        public void OneExchangeIsTwoBubblesAndThinkingKeepsTheStaffBubble()
        {
            var thinking = StaffChatText.Build(new[]
            {
                new StaffInstructionView("12:00", "luna", "北を守って", StaffInstructionView.Interpreting,
                    "", "", Array.Empty<string>(), Array.Empty<string>(), 0.1m, 0m)
            });
            var answered = StaffChatText.Build(new[]
            {
                new StaffInstructionView("12:00", "luna", "北を守って", StaffInstructionView.Completed,
                    "了解です。北を守ります。", "", new[] { "防衛" }, Array.Empty<string>(), 0.1m, 0.2m)
            });
            Assert.That(thinking, Has.Count.EqualTo(2));
            Assert.That(answered, Has.Count.EqualTo(2));
            Assert.That(thinking[1].Body, Does.Contain("考え中…"));
            Assert.That(answered[1].Body, Does.Contain("了解です。北を守ります。"));
        }
    }
}
