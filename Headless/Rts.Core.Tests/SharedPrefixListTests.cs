using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;

namespace Rts.Core.Tests
{
    /// <summary>Frames share one growing array of command views (10-10); the frame-level check is in TacticJsTests.</summary>
    public sealed class SharedPrefixListTests
    {
        [Test]
        public void ReplacedItemsWinAndEnumerationMatchesTheIndexer()
        {
            var list = new SharedPrefixList<int>(new[] { 10, 11, 12, 13, 99 }, 4, new[] { 1, 3 }, new[] { 21, 23 });
            Assert.That(list.Count, Is.EqualTo(4));
            Assert.That(list.ToArray(), Is.EqualTo(new[] { 10, 21, 12, 23 }));
            Assert.That(Enumerable.Range(0, 4).Select(i => list[i]).ToArray(), Is.EqualTo(new[] { 10, 21, 12, 23 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => { var _ = list[4]; });
            Assert.Throws<ArgumentException>(() => new SharedPrefixList<int>(new[] { 1, 2 }, 2, new[] { 1, 0 }, new[] { 5, 6 }));
        }
    }
}
