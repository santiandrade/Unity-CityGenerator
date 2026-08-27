using System.Collections.Generic;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Places every valid <see cref="CustomPlaceEntry"/> at its fixed block/slot/orientation,
    /// mirroring <see cref="CityGeneratorBuildingBuilder"/> but with no randomisation: position
    /// and facing are exactly what the user picked. Runs before the building builder so its
    /// reserved slots can be excluded from the random building distribution.
    /// </summary>
    internal static class CityGeneratorCustomPlaceBuilder
    {
        /// <summary>
        /// Instantiates every entry with a title, prefab and an assigned position that resolves to
        /// a real, non-plaza block, in that block's exact centre (occupiesFullBlock) or corner slot
        /// (using the same 0-3 convention as <see cref="CityGeneratorBuildingBuilder.SlotOffsets"/>),
        /// at the fixed rotation given by facing. An entry that fails any of those checks (not yet
        /// placed, pointing at a plaza block, an out-of-range block) is silently skipped here — the
        /// same configuration is a blocking validation error in <see cref="CityGeneratorValidator"/>,
        /// which is what keeps the Build buttons disabled before this ever runs.
        /// Also projects every placed entry with <c>isPointOfInterest == true</c> into a
        /// <see cref="PointOfInterestEntry"/> (title + final world position), consumed by
        /// <see cref="CityGeneratorMinimapBuilder"/> — computed here, not re-derived later, since
        /// this is the one place that already resolves an entry's final world position.
        /// </summary>
        public static (List<GameObject> placed, HashSet<(int gridX, int gridY, int slot)> reservedSlots, List<PointOfInterestEntry> pointsOfInterest) BuildCustomPlaces(
            List<CustomPlaceEntry> customPlaces, IReadOnlyList<BlockCell> blocks, Transform customPlacesGroup)
        {
            var placed = new List<GameObject>();
            var reservedSlots = new HashSet<(int gridX, int gridY, int slot)>();
            var pointsOfInterest = new List<PointOfInterestEntry>();

            if (customPlaces == null || customPlaces.Count == 0)
                return (placed, reservedSlots, pointsOfInterest);

            var blockLookup = new Dictionary<(int gridX, int gridY), BlockCell>();
            foreach (BlockCell block in blocks)
                blockLookup[(block.gridX, block.gridY)] = block;

            foreach (CustomPlaceEntry entry in customPlaces)
            {
                if (!TryResolveEntry(entry, blockLookup, out BlockCell block))
                    continue;

                Vector3 position;
                if (entry.occupiesFullBlock)
                {
                    position = block.center;
                    reservedSlots.Add((block.gridX, block.gridY, -1));
                }
                else
                {
                    Vector2 offset = CityGeneratorBuildingBuilder.SlotOffsets[entry.cornerSlot];
                    position = new Vector3(block.center.x + offset.x, block.center.y, block.center.z + offset.y);
                    reservedSlots.Add((block.gridX, block.gridY, entry.cornerSlot));
                }
                position.y = CityGeneratorConstants.GroundDatumY;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, customPlacesGroup);
                instance.name = entry.title;
                instance.transform.localPosition = position;
                instance.transform.localRotation = Quaternion.Euler(0f, 90f * (int)entry.facing, 0f);
                placed.Add(instance);

                if (entry.isPointOfInterest)
                    pointsOfInterest.Add(new PointOfInterestEntry { title = entry.title, worldPosition = instance.transform.position });
            }

            return (placed, reservedSlots, pointsOfInterest);
        }

        private static bool TryResolveEntry(CustomPlaceEntry entry, Dictionary<(int gridX, int gridY), BlockCell> blockLookup, out BlockCell block)
        {
            block = default;

            if (string.IsNullOrEmpty(entry.title) || entry.prefab == null || !entry.positionAssigned)
                return false;

            if (!blockLookup.TryGetValue((entry.blockCell.x, entry.blockCell.y), out block))
                return false;

            if (block.isPlaza)
                return false;

            if (!entry.occupiesFullBlock && (entry.cornerSlot < 0 || entry.cornerSlot >= CityGeneratorBuildingBuilder.SlotOffsets.Length))
                return false;

            return true;
        }
    }
}
