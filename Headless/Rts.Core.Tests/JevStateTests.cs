using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    /// <summary>
    /// What the external model is told. The rule that matters is CLAUDE.md 6: a faction may only be told what it
    /// observed. The rest is about saying it in terms the question can be answered in.
    /// </summary>
    public sealed class JevStateTests
    {
        private static KnownObjective Outpost(uint id, int z, bool ownerKnown, uint owner, long lastSeen = 0,
            uint capturing = 0, int captureTicks = 0, int captureDuration = 0) =>
            new KnownObjective(GoalKind.Outpost, id, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(z)), ownerKnown, owner,
                false, 0, lastSeen, capturing, captureTicks, captureDuration);

        private static KnownObjective Core(uint id, uint owner, int hp, bool hpKnown = true) =>
            new KnownObjective(GoalKind.Core, id, new SimPoint(Fix64.FromInt(16), Fix64.FromInt(64)), true, owner, hpKnown, hp, 0);

        private static EnemyContact Contact(int x, int z, bool visible, long lastSeen, int min, int max,
            bool strengthUnknown = false, bool absent = false, int assumed = 0) =>
            new EnemyContact(1, new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z)), lastSeen, min, max, visible,
                Array.Empty<uint>(), false, strengthUnknown, assumed, absent);

        private static FactionObservation Observation(long tick = 1200, IReadOnlyList<OwnArmyView> armies = null,
            IReadOnlyList<EnemyContact> contacts = null, IReadOnlyList<KnownObjective> objectives = null) =>
            new FactionObservation(1, tick,
                armies ?? new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, new PolicyGoal(GoalKind.Outpost, 1, default)) },
                Array.Empty<VisibleEnemy>(), contacts ?? Array.Empty<EnemyContact>(), objectives ?? Array.Empty<KnownObjective>());

        private static Dictionary<string, object> Parse(FactionObservation o) =>
            (Dictionary<string, object>)MiniJson.Parse(JevState.Build(o));

        [Test]
        public void TheStateIsValidJsonAndCarriesItsVersion()
        {
            var state = Parse(Observation());
            Assert.That(state["stateVersion"], Is.EqualTo(JevState.Version));
            Assert.That(JevState.Version, Is.EqualTo("s3"), "a change of content must change the version, or answers stop being comparable");
        }

        [Test]
        public void TheOutpostFurthestNorthIsTheOneCalledNorth()
        {
            // The question offers "north" and "south" but the model has no map, so the state has to say which is which.
            var state = Parse(Observation(objectives: new[] { Outpost(1, 96, true, 1), Outpost(2, 32, true, 0) }));
            var objectives = ((List<object>)state["objectives"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(objectives[0]["name"], Is.EqualTo("the north outpost"));
            Assert.That(objectives[1]["name"], Is.EqualTo("the south outpost"));
            // Listing them the other way round must not change which one is north.
            var flipped = Parse(Observation(objectives: new[] { Outpost(2, 32, true, 0), Outpost(1, 96, true, 1) }));
            var flippedObjectives = ((List<object>)flipped["objectives"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(flippedObjectives[0]["name"], Is.EqualTo("the south outpost"));
            Assert.That(flippedObjectives[1]["name"], Is.EqualTo("the north outpost"));
        }

        [TestCase(1u, "me")]
        [TestCase(2u, "the enemy")]
        [TestCase(0u, "nobody")]
        public void OwnershipIsSaidFromThisFactionsPointOfView(uint owner, string expected)
        {
            var state = Parse(Observation(objectives: new[] { Outpost(1, 96, true, owner), Outpost(2, 32, true, 0) }));
            var first = (Dictionary<string, object>)((List<object>)state["objectives"])[0];
            Assert.That(first["heldBy"], Is.EqualTo(expected));
        }

        [Test]
        public void AnOwnerThatWasNeverSeenIsSaidToBeUnknownRatherThanGuessed()
        {
            var state = Parse(Observation(objectives: new[] { Outpost(1, 96, false, 0), Outpost(2, 32, true, 1) }));
            var first = (Dictionary<string, object>)((List<object>)state["objectives"])[0];
            Assert.That(first["heldBy"], Is.EqualTo("unknown"));
        }

        [Test]
        public void EachCoreIsNamedByWhoseItIs()
        {
            var state = Parse(Observation(objectives: new[] { Core(1, 1, 3000), Core(2, 2, 2400) }));
            var objectives = ((List<object>)state["objectives"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(objectives[0]["name"], Is.EqualTo("my core"));
            Assert.That(objectives[0]["hp"], Is.EqualTo(3000d));
            Assert.That(objectives[1]["name"], Is.EqualTo("the enemy core"));
        }

        [Test]
        public void AnUnseenCoreHasNoHpFieldRatherThanAZero()
        {
            var state = Parse(Observation(objectives: new[] { Core(2, 2, 0, hpKnown: false) }));
            var core = (Dictionary<string, object>)((List<object>)state["objectives"])[0];
            Assert.That(core.ContainsKey("hp"), Is.False);
        }

        [Test]
        public void TheStrengthOfBothSidesIsGivenAsCountsAndRanges()
        {
            var state = Parse(Observation(
                armies: new[]
                {
                    new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, new PolicyGoal(GoalKind.Outpost, 1, default)),
                    new OwnArmyView(2, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(32)), 6, new PolicyGoal(GoalKind.Outpost, 2, default))
                },
                contacts: new[] { Contact(60, 96, true, 1200, 14, 18), Contact(60, 32, false, 1000, 2, 5) }));
            Assert.That(state["myTotalSoldiers"], Is.EqualTo(14d));
            var enemy = (Dictionary<string, object>)state["enemy"];
            Assert.That(enemy["knownSoldiersAtLeast"], Is.EqualTo(16d));
            Assert.That(enemy["knownSoldiersAtMost"], Is.EqualTo(23d));
            Assert.That(enemy["maxPossibleSoldiers"], Is.EqualTo(40d), "a public scenario rule, not a count of hidden soldiers");
        }

        [Test]
        public void AStaleSightingIsMarkedWithItsAgeNotPresentedAsCurrent()
        {
            var state = Parse(Observation(tick: 1200, contacts: new[] { Contact(60, 96, false, 600, -1, -1, strengthUnknown: true, absent: true, assumed: 8) }));
            var sighting = (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)state["enemy"])["sightings"])[0];
            Assert.That(sighting["visibleNow"], Is.EqualTo(false));
            Assert.That(sighting["sightingAgeSeconds"], Is.EqualTo(30d), "600 ticks at 20 Hz");
            Assert.That(sighting["strengthUnknown"], Is.EqualTo(true));
            Assert.That(sighting["goneFromHere"], Is.EqualTo(true));
            Assert.That(sighting["soldiersAtLeast"], Is.EqualTo(0d), "an unknown count must not read as a negative one");
            Assert.That(sighting["soldiersAtMost"], Is.EqualTo(8d), "the doctrine's conservative value, since nothing was counted");
        }

        [Test]
        public void ACaptureInProgressIsReportedWithWhoIsTakingItAndHowFarAlong()
        {
            var state = Parse(Observation(objectives: new[] { Outpost(1, 96, true, 0, capturing: 2, captureTicks: 80, captureDuration: 200), Outpost(2, 32, true, 1) }));
            var objectives = ((List<object>)state["objectives"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(objectives[0]["beingTakenBy"], Is.EqualTo("the enemy"));
            Assert.That(objectives[0]["capturePercent"], Is.EqualTo(40d));
            Assert.That(objectives[1].ContainsKey("beingTakenBy"), Is.False, "nothing is happening there");
        }

        [Test]
        public void EachArmyIsToldWhatItDefendsAndHowCloseTheNearestEnemyIs()
        {
            var state = Parse(Observation(
                contacts: new[] { Contact(24, 60, true, 1200, 4, 6), Contact(60, 96, true, 1200, 14, 18) },
                objectives: new[] { Outpost(1, 96, true, 1), Outpost(2, 32, true, 0) }));
            var army = (Dictionary<string, object>)((List<object>)state["myArmies"])[0];
            Assert.That(army["defends"], Is.EqualTo("the north outpost"));
            Assert.That(army["nearestEnemyMeters"], Is.EqualTo(36d), "the army at (24,96) is 36 m from (24,60), not 36 m from the other one");
        }

        [Test]
        public void WithNoSightingsThereIsNoNearestEnemyField()
        {
            var army = (Dictionary<string, object>)((List<object>)Parse(Observation())["myArmies"])[0];
            Assert.That(army.ContainsKey("nearestEnemyMeters"), Is.False);
        }

        [Test]
        public void MetresAreWrittenTheSameWayOnAnyMachine()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var armies = new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromRaw(1605632), Fix64.FromInt(96)), 8, default(PolicyGoal)) };
                Assert.That(JevState.Build(Observation(armies: armies)), Does.Contain("\"x\":24.5"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void NothingHiddenFromThisFactionCanReachTheModel()
        {
            // The only inputs are the observation's own lists, so this is really a check that no new source crept in.
            string state = JevState.Build(Observation(objectives: new[] { Outpost(1, 96, false, 0), Core(1, 1, 3000) }));
            Assert.That(state, Does.Not.Contain("VisibleEnemies").And.Not.Contain("factionId\":2"));
            Assert.That(state, Does.Contain("\"heldBy\":\"unknown\""), "an unseen owner stays unseen");
        }
    }
}
