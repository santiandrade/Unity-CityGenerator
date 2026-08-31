using System.Collections.Generic;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 11: full-pipeline generation with `useCustomGrid` on and a small irregular (L-triomino)
    /// shape, mirroring <see cref="SeededGenerationTests"/>'s pattern and its reasoning for offsetting
    /// the city root away from this project's own currently-open scene.
    /// </summary>
    internal class CustomGridGenerationTests
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

        private static CityGeneratorSettings MakeCustomGridSettings(IReadOnlyList<Vector2Int> shape, int seed)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int>(shape);
            settings.general.plazaCells = new List<Vector2Int>();
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            return settings;
        }

        [Test]
        public void Assemble_LShape_CompletesWithoutExceptions_AndBuildsOnlyRealBlocks()
        {
            var shape = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            CityGeneratorSettings settings = MakeCustomGridSettings(shape, seed: 42);
            Transform root = CreateOffsetCityRoot("CustomGrid_LShape");

            CityBuildSummary summary = default;
            Assert.DoesNotThrow(() => summary = CityGeneratorContentAssembler.Assemble(settings, root));

            Assert.AreEqual(shape.Count, summary.blockCount, "Block count must match the custom shape's cell count, not a rectangle.");
            Assert.Greater(summary.buildingCount, 0, "At least one building must be placed.");

            var pedestrianNetwork = root.GetComponentInChildren<PedestrianNetwork>();
            Assert.IsNotNull(pedestrianNetwork, "PedestrianNetwork must always be generated.");
            Assert.Greater(pedestrianNetwork.NodeCount, 0);

            var trafficNetwork = root.GetComponentInChildren<TrafficNetwork>();
            Assert.IsNotNull(trafficNetwork, "TrafficNetwork must always be generated.");
            Assert.Greater(trafficNetwork.NodeCount, 0);

            Transform sidewalks = root.Find("Sidewalks");
            int blockSidewalks = 0;
            int perimeterSidewalks = 0;
            foreach (Transform sidewalk in sidewalks)
            {
                if (sidewalk.name.StartsWith("Sidewalk_Perimeter"))
                    perimeterSidewalks++;
                else
                    blockSidewalks++;
            }

            Assert.AreEqual(shape.Count, blockSidewalks, "Exactly one sidewalk per real block, none for holes.");
            Assert.Greater(perimeterSidewalks, 0, "The city's outer contour must end in sidewalk, not in bare asphalt.");

            var minimapData = root.GetComponent<MinimapData>();
            Assert.IsNotNull(minimapData, "Minimap is enabled by default and must still be built for a custom shape.");
        }

        [Test]
        public void Assemble_SingleBlock_ProducesTheSmallestValidShape()
        {
            var shape = new List<Vector2Int> { new(5, 5) };
            CityGeneratorSettings settings = MakeCustomGridSettings(shape, seed: 7);
            Transform root = CreateOffsetCityRoot("CustomGrid_SingleBlock");

            CityBuildSummary summary = default;
            Assert.DoesNotThrow(() => summary = CityGeneratorContentAssembler.Assemble(settings, root));

            Assert.AreEqual(1, summary.blockCount);
            Assert.Greater(summary.buildingCount, 0);
        }
    }
}
