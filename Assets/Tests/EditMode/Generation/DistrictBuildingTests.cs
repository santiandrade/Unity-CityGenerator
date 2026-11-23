using System.Collections.Generic;
using System.Linq;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 21: <see cref="CityGeneratorBuildingBuilder.BuildBuildings"/> with Districts. The
    /// regression cases compare the builder against <see cref="LegacyAssignment"/>, a frozen copy
    /// of the algorithm as it was before districts existed, so "no districts = same city for the
    /// same seed" is pinned slot by slot (prefab, position and rotation), not just by count.
    /// </summary>
    internal class DistrictBuildingTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 40000f;
        private CityGeneratorSettings defaults;

        [SetUp]
        public void SetUp()
        {
            defaults = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(defaults);
            Assume.That(defaults.buildingPrefabs.Count, Is.GreaterThanOrEqualTo(8), "These tests need the package's demo building prefabs.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in spawnedRoots)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
            spawnedRoots.Clear();
        }

        private Transform CreateOffsetRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(nextOffset, 0f, nextOffset);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        private readonly struct PlacedSlot
        {
            public readonly int gridX;
            public readonly int gridY;
            public readonly Vector3 localPosition;
            public readonly GameObject prefab;
            public readonly int rotationSteps;

            public PlacedSlot(int gridX, int gridY, Vector3 localPosition, GameObject prefab, int rotationSteps)
            {
                this.gridX = gridX;
                this.gridY = gridY;
                this.localPosition = localPosition;
                this.prefab = prefab;
                this.rotationSteps = rotationSteps;
            }
        }

        /// <summary>
        /// Verbatim copy of BuildBuildings' random-number consumption before SPEC 21 (no
        /// instantiation): which corners, which prefab and which rotation each slot got. Never
        /// update this to match the builder -- it is the reference the builder must reproduce.
        /// </summary>
        private static List<PlacedSlot> LegacyAssignment(List<GameObject> buildingPrefabs, IReadOnlyList<BlockCell> blocks, int buildingsPerBlock, System.Random random, IReadOnlyCollection<(int gridX, int gridY, int slot)> reservedSlots)
        {
            var placed = new List<PlacedSlot>();
            if (buildingPrefabs.Count == 0)
                return placed;

            buildingsPerBlock = Mathf.Clamp(buildingsPerBlock, 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock);
            if (buildingsPerBlock == 0)
                return placed;

            var reserved = reservedSlots ?? System.Array.Empty<(int, int, int)>();
            var slots = new List<(BlockCell block, Vector2 offset)>();
            foreach (BlockCell block in blocks)
            {
                if (block.isPlaza)
                    continue;
                if (reserved.Contains((block.gridX, block.gridY, -1)))
                    continue;

                List<int> cornerOrder = Enumerable.Range(0, CityGeneratorConstants.MaxBuildingSlotsPerBlock)
                    .Where(corner => !reserved.Contains((block.gridX, block.gridY, corner)))
                    .ToList();
                CityGeneratorRandomUtility.Shuffle(cornerOrder, random);

                for (int s = 0; s < buildingsPerBlock && s < cornerOrder.Count; s++)
                    slots.Add((block, CityGeneratorBuildingBuilder.SlotOffsets[cornerOrder[s]]));
            }

            if (slots.Count == 0)
                return placed;

            var assignment = new GameObject[slots.Count];
            List<int> slotOrder = Enumerable.Range(0, slots.Count).ToList();
            CityGeneratorRandomUtility.Shuffle(slotOrder, random);
            int guaranteedCount = Mathf.Min(buildingPrefabs.Count, slots.Count);
            for (int i = 0; i < guaranteedCount; i++)
                assignment[slotOrder[i]] = buildingPrefabs[i];
            for (int i = guaranteedCount; i < slotOrder.Count; i++)
                assignment[slotOrder[i]] = buildingPrefabs[random.Next(buildingPrefabs.Count)];

            for (int i = 0; i < slots.Count; i++)
            {
                (BlockCell block, Vector2 offset) = slots[i];
                var position = new Vector3(block.center.x + offset.x, CityGeneratorConstants.GroundDatumY, block.center.z + offset.y);
                placed.Add(new PlacedSlot(block.gridX, block.gridY, position, assignment[i], random.Next(4)));
            }

            return placed;
        }

        private static List<PlacedSlot> ReadPlaced(List<GameObject> instances)
        {
            var result = new List<PlacedSlot>();
            foreach (GameObject instance in instances)
            {
                string[] parts = instance.name.Split('_'); // Building_x_y_n
                GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);
                int steps = Mathf.RoundToInt(instance.transform.localEulerAngles.y / 90f) % 4;
                result.Add(new PlacedSlot(int.Parse(parts[1]), int.Parse(parts[2]), instance.transform.localPosition, prefab, steps));
            }
            return result;
        }

        private static void AssertSameCity(List<PlacedSlot> expected, List<PlacedSlot> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "Slot count differs from the pre-SPEC 21 builder.");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].gridX, actual[i].gridX, $"Slot {i}: block X differs.");
                Assert.AreEqual(expected[i].gridY, actual[i].gridY, $"Slot {i}: block Y differs.");
                Assert.That(Vector3.Distance(expected[i].localPosition, actual[i].localPosition), Is.LessThan(0.001f), $"Slot {i}: position differs.");
                Assert.AreSame(expected[i].prefab, actual[i].prefab, $"Slot {i}: prefab differs.");
                Assert.AreEqual(expected[i].rotationSteps, actual[i].rotationSteps, $"Slot {i}: rotation differs.");
            }
        }

        private static DistrictEntry MakeDistrict(string name, List<GameObject> prefabs, params Vector2Int[] cells) => new()
        {
            name = name,
            cells = new List<Vector2Int>(cells),
            buildingPrefabs = prefabs,
            overrideBuildingsPerBlock = false,
            buildingsPerBlock = 0,
        };

        private static HashSet<GameObject> PrefabsIn(List<GameObject> instances, int gridX, int gridY)
        {
            var result = new HashSet<GameObject>();
            foreach (GameObject instance in instances)
            {
                if (instance.name.StartsWith($"Building_{gridX}_{gridY}_"))
                    result.Add(PrefabUtility.GetCorrespondingObjectFromSource(instance));
            }
            return result;
        }

        private static int CountIn(List<GameObject> instances, int gridX, int gridY) =>
            instances.Count(instance => instance.name.StartsWith($"Building_{gridX}_{gridY}_"));

        [TestCase(1, 4, false)]
        [TestCase(7, 4, true)]
        [TestCase(42, 3, false)]
        [TestCase(42, 2, true)]
        [TestCase(1234, 1, true)]
        [TestCase(99, 0, false)]
        public void NoDistricts_ProducesTheSameCityAsBeforeDistricts(int seed, int buildingsPerBlock, bool withPlazasAndReservations)
        {
            var plazas = withPlazasAndReservations ? new List<Vector2Int> { new(1, 1), new(3, 0) } : new List<Vector2Int>();
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(5, 4, plazas);
            var reserved = withPlazasAndReservations
                ? new HashSet<(int, int, int)> { (0, 0, -1), (2, 2, 1), (4, 3, 3) }
                : new HashSet<(int, int, int)>();

            List<PlacedSlot> expected = LegacyAssignment(defaults.buildingPrefabs, blocks, buildingsPerBlock, new System.Random(seed), reserved);

            List<GameObject> withNull = CityGeneratorBuildingBuilder.BuildBuildings(defaults.buildingPrefabs, CreateOffsetRoot("NullDistricts"), blocks, buildingsPerBlock, new System.Random(seed), reserved);
            AssertSameCity(expected, ReadPlaced(withNull));

            List<GameObject> withEmpty = CityGeneratorBuildingBuilder.BuildBuildings(defaults.buildingPrefabs, CreateOffsetRoot("EmptyDistricts"), blocks, buildingsPerBlock, new System.Random(seed), reserved, new List<DistrictEntry>());
            AssertSameCity(expected, ReadPlaced(withEmpty));
        }

        [Test]
        public void NoDistricts_EmptyBaseList_ConsumesNoRandomNumbers()
        {
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(3, 3, new List<Vector2Int>());
            var random = new System.Random(5);
            List<GameObject> placed = CityGeneratorBuildingBuilder.BuildBuildings(new List<GameObject>(), CreateOffsetRoot("EmptyBase"), blocks, 4, random, null, new List<DistrictEntry>());

            Assert.AreEqual(0, placed.Count);
            Assert.AreEqual(new System.Random(5).Next(), random.Next(), "An empty base list must leave the random sequence untouched, as before districts.");
        }

        [Test]
        public void DistrictBlock_OnlyGetsItsOwnPrefabs_AndOtherBlocksOnlyGetTheBase()
        {
            List<GameObject> basePool = defaults.buildingPrefabs.GetRange(0, 4);
            List<GameObject> districtPool = defaults.buildingPrefabs.GetRange(4, 3);
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(4, 4, new List<Vector2Int>());
            var districts = new List<DistrictEntry> { MakeDistrict("Downtown", districtPool, new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 3)) };
            var districtCells = new HashSet<Vector2Int>(districts[0].cells);

            List<GameObject> placed = CityGeneratorBuildingBuilder.BuildBuildings(basePool, CreateOffsetRoot("Pools"), blocks, 4, new System.Random(11), null, districts);

            foreach (BlockCell block in blocks)
            {
                HashSet<GameObject> prefabs = PrefabsIn(placed, block.gridX, block.gridY);
                List<GameObject> allowed = districtCells.Contains(new Vector2Int(block.gridX, block.gridY)) ? districtPool : basePool;
                Assert.IsTrue(prefabs.IsSubsetOf(allowed), $"Block ({block.gridX}, {block.gridY}) got a prefab outside its pool.");
                Assert.AreEqual(4, CountIn(placed, block.gridX, block.gridY));
            }
        }

        [Test]
        public void EveryPool_UsesEachOfItsPrefabsAtLeastOnce()
        {
            List<GameObject> basePool = defaults.buildingPrefabs.GetRange(0, 6);
            List<GameObject> poolA = defaults.buildingPrefabs.GetRange(6, 2);
            // A null entry is filtered from a district's pool rather than placed or counted.
            var poolB = new List<GameObject> { defaults.buildingPrefabs[0], null, defaults.buildingPrefabs[7] };
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(4, 3, new List<Vector2Int>());
            var districts = new List<DistrictEntry>
            {
                MakeDistrict("A", poolA, new Vector2Int(0, 0)),
                MakeDistrict("B", poolB, new Vector2Int(3, 2)),
            };

            for (int seed = 0; seed < 20; seed++)
            {
                foreach (GameObject root in spawnedRoots)
                    Object.DestroyImmediate(root);
                spawnedRoots.Clear();

                List<GameObject> placed = CityGeneratorBuildingBuilder.BuildBuildings(basePool, CreateOffsetRoot("Guarantee"), blocks, 2, new System.Random(seed), null, districts);

                var inA = PrefabsIn(placed, 0, 0);
                var inB = PrefabsIn(placed, 3, 2);
                var inBase = new HashSet<GameObject>();
                foreach (BlockCell block in blocks)
                {
                    if ((block.gridX, block.gridY) is (0, 0) or (3, 2))
                        continue;
                    inBase.UnionWith(PrefabsIn(placed, block.gridX, block.gridY));
                }

                CollectionAssert.AreEquivalent(poolA, inA, $"Seed {seed}: district A must use each of its 2 prefabs in its 2 slots.");
                CollectionAssert.AreEquivalent(new[] { defaults.buildingPrefabs[0], defaults.buildingPrefabs[7] }, inB, $"Seed {seed}: district B must use each non-null prefab once.");
                CollectionAssert.IsSubsetOf(basePool, inBase);
                Assert.AreEqual(basePool.Count, inBase.Count, $"Seed {seed}: every base prefab must appear outside the districts.");
            }
        }

        [Test]
        public void OverrideBuildingsPerBlock_FillsExactlyThatManySlots_AndOffFollowsTheGlobal()
        {
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(3, 2, new List<Vector2Int>());
            DistrictEntry overridden = MakeDistrict("Sparse", defaults.buildingPrefabs.GetRange(0, 2), new Vector2Int(0, 0), new Vector2Int(1, 0));
            overridden.overrideBuildingsPerBlock = true;
            overridden.buildingsPerBlock = 1;
            DistrictEntry empty = MakeDistrict("Park", defaults.buildingPrefabs.GetRange(0, 2), new Vector2Int(2, 0));
            empty.overrideBuildingsPerBlock = true;
            empty.buildingsPerBlock = 0;
            DistrictEntry following = MakeDistrict("Follower", defaults.buildingPrefabs.GetRange(2, 2), new Vector2Int(0, 1));
            following.buildingsPerBlock = 4; // ignored: override is off

            List<GameObject> placed = CityGeneratorBuildingBuilder.BuildBuildings(defaults.buildingPrefabs, CreateOffsetRoot("Override"), blocks, 3, new System.Random(3), null,
                new List<DistrictEntry> { overridden, empty, following });

            Assert.AreEqual(1, CountIn(placed, 0, 0));
            Assert.AreEqual(1, CountIn(placed, 1, 0));
            Assert.AreEqual(0, CountIn(placed, 2, 0), "An override of 0 leaves the district's blocks empty.");
            Assert.AreEqual(3, CountIn(placed, 0, 1), "With override off a district follows the global Buildings Per Block.");
            Assert.AreEqual(3, CountIn(placed, 1, 1));
            Assert.AreEqual(3, CountIn(placed, 2, 1));
        }

        [Test]
        public void CustomPlaces_InsideADistrict_KeepTheirSlots()
        {
            List<GameObject> districtPool = defaults.buildingPrefabs.GetRange(4, 3);
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(3, 1, new List<Vector2Int>());
            var districts = new List<DistrictEntry> { MakeDistrict("Old Town", districtPool, new Vector2Int(0, 0), new Vector2Int(1, 0)) };
            var reserved = new HashSet<(int, int, int)> { (0, 0, 2), (1, 0, -1) };

            List<GameObject> placed = CityGeneratorBuildingBuilder.BuildBuildings(defaults.buildingPrefabs, CreateOffsetRoot("Reserved"), blocks, 4, new System.Random(8), reserved, districts);

            Assert.AreEqual(3, CountIn(placed, 0, 0), "A quarter-block Custom Place keeps its corner inside a district.");
            Vector2 reservedOffset = CityGeneratorBuildingBuilder.SlotOffsets[2];
            Vector3 reservedPosition = new(blocks[0].center.x + reservedOffset.x, CityGeneratorConstants.GroundDatumY, blocks[0].center.z + reservedOffset.y);
            foreach (GameObject instance in placed)
                Assert.That(Vector3.Distance(instance.transform.localPosition, reservedPosition), Is.GreaterThan(0.01f), "No building may land on the reserved corner.");
            Assert.IsTrue(PrefabsIn(placed, 0, 0).IsSubsetOf(districtPool));

            Assert.AreEqual(0, CountIn(placed, 1, 0), "A full-block Custom Place leaves that district block without buildings.");
            Assert.AreEqual(4, CountIn(placed, 2, 0));
        }

        [Test]
        public void Assembler_PassesDistrictsToTheBuilder()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 3;
            settings.general.gridHeight = 3;
            settings.general.plazaCells.Clear();
            settings.customPlaces.Clear();
            settings.customPedestrians.Clear();
            settings.audio.plazaAudio.enabled = false;
            settings.general.useCustomSeed = true;
            settings.general.seed = 21;
            settings.general.buildingsPerBlock = 4;
            List<GameObject> districtPool = settings.buildingPrefabs.GetRange(0, 1);
            settings.districts.Add(MakeDistrict("Mono", districtPool, new Vector2Int(1, 1)));

            Assert.IsTrue(CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues),
                string.Join("\n", issues.Where(issue => !issue.isWarning).Select(issue => issue.message)));

            Transform root = CreateOffsetRoot("DistrictCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Transform block = root.Find("Buildings/Block_1_1");
            Assert.IsNotNull(block);
            Assert.AreEqual(4, block.childCount);
            foreach (Transform building in block)
                Assert.AreSame(districtPool[0], PrefabUtility.GetCorrespondingObjectFromSource(building.gameObject));
        }
    }
}
