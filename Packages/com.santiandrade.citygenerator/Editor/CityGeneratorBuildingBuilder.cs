using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Places buildings on up to <c>buildingsPerBlock</c> fixed 22 m slots per non-plaza block,
    /// picking a random prefab per slot while guaranteeing every assigned prefab is used at
    /// least once across its pool -- the whole city outside districts for the base list, each
    /// district's own blocks for its list (SPEC 21) -- when there are enough slots for it.
    /// </summary>
    internal static class CityGeneratorBuildingBuilder
    {
        // Internal (not private): CityGeneratorCustomPlaceBuilder and the Custom Places grid
        // picker reuse this same 0-3 index convention as their single geometric source of truth.
        internal static readonly Vector2[] SlotOffsets =
        {
            new(-CityGeneratorConstants.BuildingSlotPitch / 2f, -CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(CityGeneratorConstants.BuildingSlotPitch / 2f, -CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(-CityGeneratorConstants.BuildingSlotPitch / 2f, CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(CityGeneratorConstants.BuildingSlotPitch / 2f, CityGeneratorConstants.BuildingSlotPitch / 2f),
        };

        /// <summary>
        /// SPEC 21: every block of a district draws from that district's own prefab pool (nulls
        /// filtered out) and Buildings Per Block (its override, or the global value); every other
        /// block draws from the base <paramref name="buildingPrefabs"/> list, untouched. The
        /// "every prefab at least once" guarantee holds per pool. The random sequence is
        /// deliberately the same as before districts existed when <paramref name="districts"/> is
        /// null or empty: blocks are walked in the same order, a block with nothing to build
        /// consumes no random number, pools are assigned in a fixed order (base first, then
        /// districts in list order) and the rotation roll stays in global slot order.
        /// </summary>
        public static List<GameObject> BuildBuildings(List<GameObject> buildingPrefabs, Transform buildingsGroup, IReadOnlyList<BlockCell> blocks, int buildingsPerBlock, System.Random random, IReadOnlyCollection<(int gridX, int gridY, int slot)> reservedSlots = null, IReadOnlyList<DistrictEntry> districts = null)
        {
            var placed = new List<GameObject>();

            // Pool 0 is the base list, as-is (its null entries are handled exactly as before
            // districts existed); pool d + 1 is districts[d], with its nulls filtered out.
            var pools = new List<List<GameObject>> { buildingPrefabs ?? new List<GameObject>() };
            var poolBuildingsPerBlock = new List<int> { Mathf.Clamp(buildingsPerBlock, 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock) };
            var poolByCell = new Dictionary<(int gridX, int gridY), int>();
            if (districts != null)
            {
                for (int d = 0; d < districts.Count; d++)
                {
                    DistrictEntry district = districts[d];
                    var pool = new List<GameObject>();
                    if (district.buildingPrefabs != null)
                        pool.AddRange(district.buildingPrefabs.Where(prefab => prefab != null));
                    pools.Add(pool);

                    int districtBuildingsPerBlock = district.overrideBuildingsPerBlock ? district.buildingsPerBlock : buildingsPerBlock;
                    poolBuildingsPerBlock.Add(Mathf.Clamp(districtBuildingsPerBlock, 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock));

                    if (district.cells == null)
                        continue;
                    // A block listed by two districts is a validation error; the first one wins
                    // here only so a forced generation stays deterministic.
                    foreach (Vector2Int cell in district.cells)
                        poolByCell.TryAdd((cell.x, cell.y), d + 1);
                }
            }

            var reserved = reservedSlots ?? System.Array.Empty<(int, int, int)>();

            var slots = new List<(BlockCell block, Vector2 offset, int pool)>();
            foreach (BlockCell block in blocks)
            {
                if (block.isPlaza)
                    continue;

                // A Custom Place occupying the whole block excludes every corner of that block
                // from the random building distribution.
                if (reserved.Contains((block.gridX, block.gridY, -1)))
                    continue;

                int poolIndex = poolByCell.TryGetValue((block.gridX, block.gridY), out int districtPool) ? districtPool : 0;
                int blockBuildings = poolBuildingsPerBlock[poolIndex];
                // Skipped before the shuffle, so a block with nothing to build consumes no random
                // number -- what keeps an empty base list / Buildings Per Block = 0 city on the
                // exact random sequence it had before districts.
                if (blockBuildings == 0 || pools[poolIndex].Count == 0)
                    continue;

                // Which corners get filled is randomised per block: with fewer than 4
                // buildings, always picking corners 0..buildingsPerBlock-1 made every partially
                // filled block look identical (same corners occupied, same corners empty).
                List<int> cornerOrder = Enumerable.Range(0, CityGeneratorConstants.MaxBuildingSlotsPerBlock)
                    .Where(corner => !reserved.Contains((block.gridX, block.gridY, corner)))
                    .ToList();
                CityGeneratorRandomUtility.Shuffle(cornerOrder, random);

                for (int s = 0; s < blockBuildings && s < cornerOrder.Count; s++)
                    slots.Add((block, SlotOffsets[cornerOrder[s]], poolIndex));
            }

            if (slots.Count == 0)
                return placed;

            var assignment = new GameObject[slots.Count];
            for (int poolIndex = 0; poolIndex < pools.Count; poolIndex++)
            {
                var poolSlots = new List<int>();
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].pool == poolIndex)
                        poolSlots.Add(i);
                }

                if (poolSlots.Count == 0)
                    continue;

                GameObject[] poolAssignment = AssignPrefabs(pools[poolIndex], poolSlots.Count, random);
                for (int i = 0; i < poolSlots.Count; i++)
                    assignment[poolSlots[i]] = poolAssignment[i];
            }

            var slotIndexPerBlock = new Dictionary<(int gridX, int gridY), int>();
            for (int i = 0; i < slots.Count; i++)
            {
                (BlockCell block, Vector2 offset, _) = slots[i];
                GameObject prefab = assignment[i];

                var blockKey = (block.gridX, block.gridY);
                slotIndexPerBlock.TryGetValue(blockKey, out int slotIndex);
                slotIndexPerBlock[blockKey] = slotIndex + 1;

                Transform blockGroup = GetOrCreateBlockGroup(buildingsGroup, block.gridX, block.gridY);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, blockGroup);
                instance.name = $"Building_{block.gridX}_{block.gridY}_{slotIndex}";
                instance.transform.localPosition = new Vector3(block.center.x + offset.x, CityGeneratorConstants.GroundDatumY, block.center.z + offset.y);
                instance.transform.localRotation = Quaternion.Euler(0f, 90f * random.Next(4), 0f);
                placed.Add(instance);
            }

            return placed;
        }

        // Every prefab is placed into one random slot first (guaranteeing at least one use when
        // there are enough slots), then the remaining slots are filled with independent random picks.
        private static GameObject[] AssignPrefabs(List<GameObject> buildingPrefabs, int slotCount, System.Random random)
        {
            var assignment = new GameObject[slotCount];
            List<int> slotOrder = Enumerable.Range(0, slotCount).ToList();
            CityGeneratorRandomUtility.Shuffle(slotOrder, random);

            int guaranteedCount = Mathf.Min(buildingPrefabs.Count, slotCount);
            for (int i = 0; i < guaranteedCount; i++)
                assignment[slotOrder[i]] = buildingPrefabs[i];

            for (int i = guaranteedCount; i < slotOrder.Count; i++)
                assignment[slotOrder[i]] = buildingPrefabs[random.Next(buildingPrefabs.Count)];

            return assignment;
        }

        private static Transform GetOrCreateBlockGroup(Transform parent, int gridX, int gridY)
        {
            string name = $"Block_{gridX}_{gridY}";
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            var group = new GameObject(name).transform;
            group.SetParent(parent);
            return group;
        }
    }
}
