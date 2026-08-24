using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Distributes a total count across a list of percentage-weighted entries. Shared by
    /// CityGeneratorTrafficBuilder (vehicles) and CityGeneratorPedestrianBuilder (pedestrians) —
    /// both follow the same percentage-list convention (VehicleEntry/PedestrianEntry), just with
    /// different entry types, hence the generic selector instead of a common interface.
    /// </summary>
    internal static class CityGeneratorDistributionUtility
    {
        /// <summary>
        /// Splits <paramref name="totalCount"/> across <paramref name="entries"/> by their
        /// configured percentage: each gets the floor of its exact share, and the leftover units
        /// (from rounding down) go to the entries with the largest fractional remainder first.
        /// </summary>
        public static int[] DistributePercentages<T>(IReadOnlyList<T> entries, int totalCount, Func<T, float> percentageSelector)
        {
            var counts = new int[entries.Count];
            var remainders = new float[entries.Count];
            int assigned = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                float exact = totalCount * (percentageSelector(entries[i]) / 100f);
                counts[i] = Mathf.FloorToInt(exact);
                remainders[i] = exact - counts[i];
                assigned += counts[i];
            }

            List<int> byRemainder = Enumerable.Range(0, entries.Count).OrderByDescending(i => remainders[i]).ToList();
            int remaining = totalCount - assigned;
            for (int i = 0; i < remaining; i++)
                counts[byRemainder[i % byRemainder.Count]]++;

            return counts;
        }
    }
}
