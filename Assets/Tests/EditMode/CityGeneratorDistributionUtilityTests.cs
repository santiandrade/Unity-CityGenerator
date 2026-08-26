using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;

namespace CityGenerator.Tests.EditMode
{
    internal class CityGeneratorDistributionUtilityTests
    {
        private readonly struct Entry
        {
            public readonly float percentage;
            public Entry(float percentage) => this.percentage = percentage;
        }

        [Test]
        public void DistributePercentages_ExactDivision_MatchesFloorShares()
        {
            var entries = new List<Entry> { new(50f), new(30f), new(20f) };
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 10, e => e.percentage);

            CollectionAssert.AreEqual(new[] { 5, 3, 2 }, counts);
        }

        [Test]
        public void DistributePercentages_TotalAlwaysMatchesRequestedCount()
        {
            var entries = new List<Entry> { new(33.34f), new(33.33f), new(33.33f) };
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 10, e => e.percentage);

            int sum = 0;
            foreach (int c in counts) sum += c;
            Assert.AreEqual(10, sum);
        }

        [Test]
        public void DistributePercentages_LeftoverGoesToLargestRemainderFirst()
        {
            // Exact shares: 3.334, 3.333, 3.333 -> floors 3,3,3 (assigned 9), 1 leftover.
            // Entry 0 has the largest fractional remainder, so it gets the extra unit.
            var entries = new List<Entry> { new(33.34f), new(33.33f), new(33.33f) };
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 10, e => e.percentage);

            Assert.AreEqual(4, counts[0]);
            Assert.AreEqual(3, counts[1]);
            Assert.AreEqual(3, counts[2]);
        }

        [Test]
        public void DistributePercentages_ZeroTotalCount_ReturnsAllZeros()
        {
            var entries = new List<Entry> { new(50f), new(50f) };
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 0, e => e.percentage);

            CollectionAssert.AreEqual(new[] { 0, 0 }, counts);
        }

        [Test]
        public void DistributePercentages_SingleEntryAt100Percent_GetsEverything()
        {
            var entries = new List<Entry> { new(100f) };
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 37, e => e.percentage);

            CollectionAssert.AreEqual(new[] { 37 }, counts);
        }

        [Test]
        public void DistributePercentages_EmptyEntriesAndZeroTotal_ReturnsEmptyArray()
        {
            var entries = new List<Entry>();
            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(entries, 0, e => e.percentage);

            Assert.AreEqual(0, counts.Length);
        }
    }
}
