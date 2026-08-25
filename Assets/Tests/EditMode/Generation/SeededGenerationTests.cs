using System.Collections.Generic;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// Full-pipeline generation with a fixed seed, on the grid sizes this spec's baseline
    /// measurements use (1x3, 5x5, 10x10). Every builder places content via `transform.localPosition`
    /// under the city root, so offsetting the root's own world position moves the whole generated
    /// city with it -- used here to keep each fixture far away from this project's own currently-open
    /// scene (City.unity, with a real generated city sitting at/near world origin) and from each
    /// other, so PedestrianNetwork.Build()'s Physics-based obstacle pruning never sees unrelated
    /// real geometry.
    /// </summary>
    internal class SeededGenerationTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset;

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

        private Transform CreateOffsetCityRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(nextOffset, 0f, nextOffset);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        private static CityGeneratorSettings MakeSettings(int gridWidth, int gridHeight, int seed)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = gridWidth;
            settings.general.gridHeight = gridHeight;
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            return settings;
        }

        [TestCase(1, 3)]
        [TestCase(5, 5)]
        [TestCase(10, 10)]
        public void Assemble_FixedSeed_CompletesWithoutExceptions_AndProducesExpectedInvariants(int gridWidth, int gridHeight)
        {
            CityGeneratorSettings settings = MakeSettings(gridWidth, gridHeight, seed: 12345);
            Transform root = CreateOffsetCityRoot($"City_{gridWidth}x{gridHeight}");

            CityBuildSummary summary = default;
            Assert.DoesNotThrow(() => summary = CityGeneratorContentAssembler.Assemble(settings, root));

            Assert.AreEqual(gridWidth * gridHeight, summary.blockCount, "Block count must match grid dimensions.");
            Assert.Greater(summary.buildingCount, 0, "At least one building must be placed.");

            var pedestrianNetwork = root.GetComponentInChildren<PedestrianNetwork>();
            Assert.IsNotNull(pedestrianNetwork, "PedestrianNetwork must always be generated, regardless of Include Pedestrians.");
            Assert.Greater(pedestrianNetwork.NodeCount, 0);

            var trafficNetwork = root.GetComponentInChildren<TrafficNetwork>();
            Assert.IsNotNull(trafficNetwork, "TrafficNetwork must always be generated, regardless of Include Traffic.");
            Assert.Greater(trafficNetwork.NodeCount, 0);
        }

        [Test]
        public void Assemble_SameSeed_ProducesIdenticalBuildingLayout()
        {
            CityGeneratorSettings settingsA = MakeSettings(5, 5, seed: 777);
            CityGeneratorSettings settingsB = MakeSettings(5, 5, seed: 777);

            Transform rootA = CreateOffsetCityRoot("CityA");
            Transform rootB = CreateOffsetCityRoot("CityB");

            CityGeneratorContentAssembler.Assemble(settingsA, rootA);
            CityGeneratorContentAssembler.Assemble(settingsB, rootB);

            List<Vector3> positionsA = CollectBuildingLocalPositions(rootA);
            List<Vector3> positionsB = CollectBuildingLocalPositions(rootB);
            List<string> prefabsA = CollectBuildingPrefabNames(rootA);
            List<string> prefabsB = CollectBuildingPrefabNames(rootB);

            Assert.Greater(positionsA.Count, 0);
            Assert.AreEqual(positionsA.Count, positionsB.Count, "Same seed must place the same number of buildings.");
            CollectionAssert.AreEqual(prefabsA, prefabsB, "Same seed must pick the same prefab for each building slot, in the same order.");
            for (int i = 0; i < positionsA.Count; i++)
                Assert.AreEqual(positionsA[i], positionsB[i], $"Building {i} diverged between two runs with the same seed.");
        }

        [Test]
        public void Assemble_DifferentSeeds_ProduceDifferentBuildingLayout()
        {
            CityGeneratorSettings settingsA = MakeSettings(5, 5, seed: 1);
            CityGeneratorSettings settingsB = MakeSettings(5, 5, seed: 2);

            Transform rootA = CreateOffsetCityRoot("CityA");
            Transform rootB = CreateOffsetCityRoot("CityB");

            CityGeneratorContentAssembler.Assemble(settingsA, rootA);
            CityGeneratorContentAssembler.Assemble(settingsB, rootB);

            List<string> prefabsA = CollectBuildingPrefabNames(rootA);
            List<string> prefabsB = CollectBuildingPrefabNames(rootB);

            CollectionAssert.AreNotEqual(prefabsA, prefabsB);
        }

        private static List<Vector3> CollectBuildingLocalPositions(Transform root)
        {
            Transform buildings = root.Find("Buildings");
            var positions = new List<Vector3>();
            foreach (Transform blockGroup in buildings)
            {
                foreach (Transform building in blockGroup)
                    positions.Add(building.localPosition);
            }
            return positions;
        }

        /// <summary>
        /// Building instances are renamed to a fixed "Building_x_y_slot" scheme regardless of
        /// which prefab was chosen, so their GameObject name can't distinguish prefab choices
        /// between two runs -- the underlying prefab asset name (via the instance's corresponding
        /// source object) is what actually reflects the seeded random pick.
        /// </summary>
        private static List<string> CollectBuildingPrefabNames(Transform root)
        {
            Transform buildings = root.Find("Buildings");
            var names = new List<string>();
            foreach (Transform blockGroup in buildings)
            {
                foreach (Transform building in blockGroup)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(building.gameObject);
                    names.Add(source != null ? source.name : building.name);
                }
            }
            return names;
        }
    }
}
