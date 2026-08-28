using System.Collections.Generic;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// Full-pipeline generation exercising Custom Places (SPEC 06): a quarter-block corner entry
    /// and a whole-block entry, verifying the reserved slots are excluded from the random building
    /// distribution and that both entries land at their exact configured position/orientation.
    /// Mirrors <see cref="SeededGenerationTests"/>'s own offset-root convention so this fixture's
    /// synthetic city never collides with the currently-open scene's real geometry.
    /// </summary>
    internal class CustomPlaceBuilderTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 20000f;

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
            settings.general.plazaCells.Clear();
            settings.customPlaces.Clear();
            settings.audio.plazaAudio.enabled = false; // no plazas in this fixture; avoid the unrelated "no clip entries" issue
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            return settings;
        }

        [Test]
        public void QuarterBlockEntry_OccupiesExactSlot_AndOtherThreeCornersStillGetBuildings()
        {
            CityGeneratorSettings settings = MakeSettings(3, 3, seed: 42);
            settings.general.buildingsPerBlock = 4;
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Corner Kiosk",
                prefab = settings.buildingPrefabs[0],
                occupiesFullBlock = false,
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 1,
                facing = CustomPlaceFacing.East,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);
            AssertNoBlockingIssues(valid, issues);

            Transform root = CreateOffsetCityRoot("QuarterBlockCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Transform customPlaces = root.Find("CustomPlaces");
            Assert.AreEqual(1, customPlaces.childCount);
            Transform placed = customPlaces.GetChild(0);
            Assert.AreEqual("Corner Kiosk", placed.name);
            Assert.AreEqual(90f, placed.localEulerAngles.y, 0.01f, "East facing must be a 90-degree yaw.");

            Transform block00 = root.Find("Buildings/Block_0_0");
            Assert.IsNotNull(block00, "The other 3 corners of block (0,0) must still get random buildings.");
            Assert.AreEqual(3, block00.childCount, "Exactly 3 corners (not 4) must be filled: the reserved one is excluded.");

            foreach (Transform building in block00)
                Assert.AreNotEqual(placed.localPosition, building.localPosition, "No random building may land on the reserved slot.");
        }

        [Test]
        public void FullBlockEntry_OccupiesBlockCentre_AndNoBuildingsAreCreatedForThatBlock()
        {
            CityGeneratorSettings settings = MakeSettings(3, 3, seed: 42);
            settings.general.buildingsPerBlock = 4;
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Landmark Tower",
                prefab = settings.buildingPrefabs[1],
                occupiesFullBlock = true,
                blockCell = new Vector2Int(2, 0),
                facing = CustomPlaceFacing.South,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);
            AssertNoBlockingIssues(valid, issues);

            Transform root = CreateOffsetCityRoot("FullBlockCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Transform customPlaces = root.Find("CustomPlaces");
            Assert.AreEqual(1, customPlaces.childCount);
            Transform placed = customPlaces.GetChild(0);
            Assert.AreEqual("Landmark Tower", placed.name);
            Assert.AreEqual(180f, placed.localEulerAngles.y, 0.01f, "South facing must be a 180-degree yaw.");

            Transform block20 = root.Find("Buildings/Block_2_0");
            Assert.IsNull(block20, "No building slot may be filled in a block a full-block Custom Place reserves.");
        }

        [Test]
        public void CustomPlace_IsAddedToObstacles_SoNearbyPropsDoNotOverlapIt()
        {
            CityGeneratorSettings settings = MakeSettings(3, 3, seed: 42);
            settings.props.lampDensity = 1f;
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Corner Kiosk",
                prefab = settings.buildingPrefabs[0],
                occupiesFullBlock = false,
                blockCell = new Vector2Int(1, 1),
                cornerSlot = 0,
                facing = CustomPlaceFacing.North,
                positionAssigned = true,
            });

            Transform root = CreateOffsetCityRoot("ObstacleCity");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Transform customPlaces = root.Find("CustomPlaces");
            Transform placed = customPlaces.GetChild(0);
            Rect placedRect = ToXZRect(CityGeneratorBoundsUtility.GetWorldBounds(placed.gameObject));

            Transform streetLights = root.Find("StreetLights");
            foreach (Transform lamp in streetLights.GetComponentsInChildren<Transform>())
            {
                if (lamp == streetLights)
                    continue;
                Rect lampRect = ToXZRect(CityGeneratorBoundsUtility.GetWorldBounds(lamp.gameObject));
                Assert.IsFalse(placedRect.Overlaps(lampRect), $"Lamp '{lamp.name}' overlaps the Custom Place '{placed.name}'.");
            }
        }

        private static Rect ToXZRect(Bounds bounds) => new(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);

        private static void AssertNoBlockingIssues(bool valid, List<CityGeneratorValidationIssue> issues)
        {
            if (valid)
                return;

            var messages = new List<string>();
            foreach (CityGeneratorValidationIssue issue in issues)
                if (!issue.isWarning)
                    messages.Add(issue.message);
            Assert.Fail("Expected no blocking validation issues:\n" + string.Join("\n", messages));
        }
    }
}
