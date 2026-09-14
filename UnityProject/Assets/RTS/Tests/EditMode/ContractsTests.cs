using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;

namespace Rts.Tests.EditMode
{
    public sealed class ContractsTests
    {
        [TestCase(typeof(ScopeKind), "All=1,Army=2,Outpost=3")]
        [TestCase(typeof(PolicyKind), "Focus=1,AllowAbandon=2,Retreat=3,MaintainReserve=4,Defend=5,Scout=6,ReturnToAuto=7")]
        [TestCase(typeof(CommandSource), "Human=1,Doctrine=2,Ai=3")]
        [TestCase(typeof(CommandStatus), "Interpreting=1,Pending=2,Executing=3,Completed=4,Cancelled=5,Expired=6,Impossible=7")]
        [TestCase(typeof(GoalKind), "None=0,Point=1,Outpost=2,Core=3")]
        [TestCase(typeof(EndKind), "UntilReplaced=1,Arrived=2,ObjectiveOwned=3,AtTick=4,LossReached=5")]
        [TestCase(typeof(ExpireFlags), "None=0,SubjectGone=1,OwnershipChanged=2,ObservationTooOld=4")]
        [TestCase(typeof(InputKind), "Reserve=1,Resolve=2,Cancel=3,Proposal=4")]
        [TestCase(typeof(UnitKind), "Infantry=1,Scout=2")]
        [TestCase(typeof(EventKind), "CommandChanged=1,MoveStarted=2,Attack=3,Death=4,Capture=5,Reinforcement=6,ContactChanged=7,MatchEnded=8,Fault=9")]
        [TestCase(typeof(ReasonCode), "None=0,Superseded=1,UserCancelled=2,Deadline=3,StaleVersion=4,InvalidPayload=5,SubjectGone=6,OwnershipChanged=7,NoPath=8,EmptyArmy=9,ObservationTooOld=10,LossLimit=11")]
        public void WireValuesAreFixed(Type type, string expected)
        {
            Assert.That(Enum.GetUnderlyingType(type), Is.EqualTo(typeof(byte)));
            var actual = Enum.GetNames(type).Select(name =>
                name + "=" + Convert.ToByte(Enum.Parse(type, name)).ToString(CultureInfo.InvariantCulture));
            Assert.That(actual, Is.EquivalentTo(expected.Split(',')));
        }

        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Fix64PreservesRawAndValueEquality(long raw)
        {
            var value = Fix64.FromRaw(raw);
            var same = new Fix64(raw);
            var different = Fix64.FromRaw(raw == 0 ? 1 : 0);
            Assert.That(value.Raw, Is.EqualTo(raw));
            Assert.That(value.Equals(same), Is.True);
            Assert.That(value.Equals((object)same), Is.True);
            Assert.That(value == same, Is.True);
            Assert.That(value != different, Is.True);
            Assert.That(value.Equals(different), Is.False);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals(raw), Is.False);
            Assert.That(value.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        }

        [Test]
        public void IntegerCreationIsChecked()
        {
            Assert.That(Fix64.FromInt(-2).Raw, Is.EqualTo(-131072L));
            Assert.That(Fix64.FromInt(3).Raw, Is.EqualTo(196608L));
            Assert.That(Fix64.FromInt(-140737488355328L).Raw, Is.EqualTo(long.MinValue));
            Assert.That(Fix64.FromInt(140737488355327L).Raw, Is.EqualTo(9223372036854710272L));
            Assert.Throws<OverflowException>(() => Fix64.FromInt(140737488355328L));
            Assert.Throws<OverflowException>(() => Fix64.FromInt(-140737488355329L));
        }

        [Test]
        public void FormattingIsExactAndIndependentOfCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(Fix64.FromRaw(1).ToString(), Is.EqualTo("0.0000152587890625"));
                Assert.That(Fix64.FromRaw(-98304).ToString(), Is.EqualTo("-1.5"));
                Assert.That(Fix64.FromRaw(long.MinValue).ToString(), Is.EqualTo("-140737488355328"));
                Assert.That(Fix64.FromRaw(long.MaxValue).ToString(), Is.EqualTo("140737488355327.9999847412109375"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        public static IEnumerable<TestCaseData> CollectionProperties()
        {
            foreach (var type in typeof(FactionFrame).Assembly.GetExportedTypes())
            foreach (var property in type.GetProperties())
                if (IsList(property.PropertyType))
                    yield return new TestCaseData(type, property.Name)
                        .SetName("DefensiveCopy_" + type.Name + "_" + property.Name);
        }

        [TestCaseSource(nameof(CollectionProperties))]
        public void CollectionsAreCopiedAndCannotBeWritten(Type type, string propertyName)
        {
            var constructor = type.GetConstructors().Single();
            var parameters = constructor.GetParameters();
            var arguments = parameters.Select(p => Sample(p.ParameterType)).ToArray();
            int index = Array.FindIndex(parameters,
                p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            var original = (Array)arguments[index];
            object expected = original.GetValue(0);
            object instance = constructor.Invoke(arguments);
            var exposed = (IList)type.GetProperty(propertyName).GetValue(instance);
            original.SetValue(null, 0);
            Assert.That(exposed[0], Is.EqualTo(expected), "Input array mutation must not affect the snapshot.");
            Assert.That(exposed, Is.Not.SameAs(original));
            Assert.That(exposed.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposed[0] = expected);

            // A mutable List<T> input must also be copied.
            var listType = typeof(List<>).MakeGenericType(original.GetType().GetElementType());
            var inputList = (IList)Activator.CreateInstance(listType);
            inputList.Add(expected);
            arguments[index] = inputList;
            instance = constructor.Invoke(arguments);
            inputList.Clear();
            exposed = (IList)type.GetProperty(propertyName).GetValue(instance);
            Assert.That(exposed.Count, Is.EqualTo(1));
            Assert.That(exposed[0], Is.EqualTo(expected));
        }

        private static bool IsList(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

        // Nonzero samples ensure replacing a source element with default is observable.
        private static object Sample(Type type)
        {
            if (IsList(type))
            {
                var element = type.GetGenericArguments()[0];
                var array = Array.CreateInstance(element, 1);
                array.SetValue(Sample(element), 0);
                return array;
            }
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            if (type == typeof(bool)) return true;
            if (type.IsPrimitive) return Convert.ChangeType(1, type, CultureInfo.InvariantCulture);
            var constructor = type.GetConstructors().Single();
            return constructor.Invoke(constructor.GetParameters().Select(p => Sample(p.ParameterType)).ToArray());
        }
    }
}
