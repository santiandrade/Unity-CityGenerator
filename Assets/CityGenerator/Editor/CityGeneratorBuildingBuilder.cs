using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Places buildings on up to <c>buildingsPerBlock</c> fixed 22 m slots per non-plaza block,
    /// picking a random prefab per slot while guaranteeing every assigned prefab is used at
    /// least once across the whole city (when there are enough slots for it).
    /// </summary>
    internal static class CityGeneratorBuildingBuilder
    {
        private static readonly Vector2[] SlotOffsets =
        {
            new(-CityGeneratorConstants.BuildingSlotPitch / 2f, -CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(CityGeneratorConstants.BuildingSlotPitch / 2f, -CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(-CityGeneratorConstants.BuildingSlotPitch / 2f, CityGeneratorConstants.BuildingSlotPitch / 2f),
            new(CityGeneratorConstants.BuildingSlotPitch / 2f, CityGeneratorConstants.BuildingSlotPitch / 2f),
        };

        public static void BuildBuildings(List<GameObject> buildingPrefabs, Transform buildingsGroup, IReadOnlyList<BlockCell> blocks, int buildingsPerBlock, System.Random random)
        {
            if (buildingPrefabs.Count == 0)
                return;

            buildingsPerBlock = Mathf.Clamp(buildingsPerBlock, 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock);
            if (buildingsPerBlock == 0)
                return;

            var slots = new List<(BlockCell block, Vector2 offset)>();
            foreach (BlockCell block in blocks)
            {
                if (block.isPlaza)
                    continue;

                for (int s = 0; s < buildingsPerBlock; s++)
                    slots.Add((block, SlotOffsets[s]));
            }

            if (slots.Count == 0)
                return;

            GameObject[] assignment = AssignPrefabs(buildingPrefabs, slots.Count, random);

            var slotIndexPerBlock = new Dictionary<(int gridX, int gridY), int>();
            for (int i = 0; i < slots.Count; i++)
            {
                (BlockCell block, Vector2 offset) = slots[i];
                GameObject prefab = assignment[i];

                var blockKey = (block.gridX, block.gridY);
                slotIndexPerBlock.TryGetValue(blockKey, out int slotIndex);
                slotIndexPerBlock[blockKey] = slotIndex + 1;

                Transform blockGroup = GetOrCreateBlockGroup(buildingsGroup, block.gridX, block.gridY);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, blockGroup);
                instance.name = $"Building_{block.gridX}_{block.gridY}_{slotIndex}";
                instance.transform.localPosition = new Vector3(block.center.x + offset.x, CityGeneratorConstants.GroundDatumY, block.center.z + offset.y);
                instance.transform.localRotation = Quaternion.Euler(0f, 90f * random.Next(4), 0f);
            }
        }

        // Every prefab is placed into one random slot first (guaranteeing at least one use when
        // there are enough slots), then the remaining slots are filled with independent random picks.
        private static GameObject[] AssignPrefabs(List<GameObject> buildingPrefabs, int slotCount, System.Random random)
        {
            var assignment = new GameObject[slotCount];
            List<int> slotOrder = Enumerable.Range(0, slotCount).ToList();
            Shuffle(slotOrder, random);

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

        private static void Shuffle<T>(IList<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
