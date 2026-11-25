using System.Collections.Generic;
using System.Linq;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 23: the district data CityGeneratorContentAssembler stores on CityGeneratorInfo and the
    /// position -> cell -> district lookup that reads it. The position -> cell step is pinned against
    /// <see cref="CityGeneratorGrid.GetBlockCenter"/> (it must be its exact inverse) on both grid
    /// modes, odd and even sizes, and a root moved away from the origin (SPEC 16).
    /// </summary>
    internal class DistrictInfoTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 60000f;

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
            root.transform.position = new Vector3(nextOffset, 3f, -nextOffset * 0.5f);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        private CityGeneratorInfo CreateInfo(string name, Vector2Int layoutSize)
        {
            CityGeneratorInfo info = CreateOffsetRoot(name).gameObject.AddComponent<CityGeneratorInfo>();
            info.cellPitch = CityGeneratorConstants.CellPitch;
            info.layoutSize = layoutSize;
            return info;
        }

        private static Vector3 WorldBlockCenter(CityGeneratorInfo info, int gx, int gy) =>
            info.transform.TransformPoint(CityGeneratorGrid.GetBlockCenter(gx, gy, info.layoutSize.x, info.layoutSize.y));

        private static DistrictEntry MakeDistrict(string name, List<GameObject> prefabs, params Vector2Int[] cells) => new()
        {
            name = name,
            cells = new List<Vector2Int>(cells),
            buildingPrefabs = prefabs,
        };

        private static CityGeneratorSettings MakeSettings(int seed)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.plazaCells.Clear();
            settings.customPlaces.Clear();
            settings.customPedestrians.Clear();
            settings.audio.plazaAudio.enabled = false;
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            return settings;
        }

        private static void AssertValid(CityGeneratorSettings settings)
        {
            Assert.IsTrue(CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues),
                string.Join("\n", issues.Where(issue => !issue.isWarning).Select(issue => issue.message)));
        }

        private static int DistrictAt(CityModule city, Vector3 worldPosition)
        {
            bool found = city.TryGetDistrictAt(worldPosition, out int index);
            Assert.AreEqual(found, index >= 0, "TryGetDistrictAt must return -1 exactly when it returns false.");
            return index;
        }

        // ---------------------------------------------------------------- position -> cell

        [TestCase(1, 1)]
        [TestCase(2, 3)]
        [TestCase(4, 4)]
        [TestCase(5, 2)]
        [TestCase(7, 6)]
        [TestCase(15, 15)] // the Custom Grid canvas (MaxGridSize x MaxGridSize)
        public void TryGetCell_IsTheInverseOfGetBlockCenter_OnEveryBlock(int width, int height)
        {
            CityGeneratorInfo info = CreateInfo($"Layout_{width}x{height}", new Vector2Int(width, height));
            float inside = CityGeneratorConstants.CellPitch / 2f - 1f;

            for (int gy = 0; gy < height; gy++)
            {
                for (int gx = 0; gx < width; gx++)
                {
                    Vector3 center = WorldBlockCenter(info, gx, gy);
                    var expected = new Vector2Int(gx, gy);
                    Assert.IsTrue(info.TryGetCell(center, out Vector2Int cell), $"Centre of {expected}");
                    Assert.AreEqual(expected, cell);

                    foreach (Vector3 offset in new[] { new Vector3(inside, 0f, inside), new Vector3(-inside, 0f, -inside), new Vector3(inside, 0f, -inside), new Vector3(-inside, 0f, inside) })
                    {
                        Assert.IsTrue(info.TryGetCell(center + offset, out cell), $"{expected} + {offset}");
                        Assert.AreEqual(expected, cell, $"A point up to 1 m short of the surrounding street axes still belongs to {expected}.");
                    }
                }
            }
        }

        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 5)]
        public void TryGetCell_StreetAxis_SplitsItsTwoBlocksOneMetreEachSide(int width, int height)
        {
            CityGeneratorInfo info = CreateInfo($"Streets_{width}x{height}", new Vector2Int(width, height));
            float rowZ = CityGeneratorGrid.GetBlockAxisPosition(height, 0);
            float columnX = CityGeneratorGrid.GetBlockAxisPosition(width, 0);

            for (int i = 1; i < width; i++)
            {
                float axisX = CityGeneratorGrid.GetStreetAxisPosition(width, i);
                Assert.IsTrue(info.TryGetCell(info.transform.TransformPoint(new Vector3(axisX - 1f, 0f, rowZ)), out Vector2Int west));
                Assert.IsTrue(info.TryGetCell(info.transform.TransformPoint(new Vector3(axisX + 1f, 0f, rowZ)), out Vector2Int east));
                Assert.AreEqual(new Vector2Int(i - 1, 0), west);
                Assert.AreEqual(new Vector2Int(i, 0), east);
            }

            for (int i = 1; i < height; i++)
            {
                float axisZ = CityGeneratorGrid.GetStreetAxisPosition(height, i);
                Assert.IsTrue(info.TryGetCell(info.transform.TransformPoint(new Vector3(columnX, 0f, axisZ - 1f)), out Vector2Int south));
                Assert.IsTrue(info.TryGetCell(info.transform.TransformPoint(new Vector3(columnX, 0f, axisZ + 1f)), out Vector2Int north));
                Assert.AreEqual(new Vector2Int(0, i - 1), south);
                Assert.AreEqual(new Vector2Int(0, i), north);
            }
        }

        [TestCase(1, 1)]
        [TestCase(3, 2)]
        [TestCase(15, 15)]
        public void TryGetCell_EndsAtTheOuterStreetAxes(int width, int height)
        {
            CityGeneratorInfo info = CreateInfo($"Edges_{width}x{height}", new Vector2Int(width, height));
            float westAxis = CityGeneratorGrid.GetStreetAxisPosition(width, 0);
            float eastAxis = CityGeneratorGrid.GetStreetAxisPosition(width, width);
            float southAxis = CityGeneratorGrid.GetStreetAxisPosition(height, 0);
            float northAxis = CityGeneratorGrid.GetStreetAxisPosition(height, height);
            float midZ = CityGeneratorGrid.GetBlockAxisPosition(height, 0);
            float midX = CityGeneratorGrid.GetBlockAxisPosition(width, 0);

            Vector3 World(float x, float z) => info.transform.TransformPoint(new Vector3(x, 0f, z));

            Assert.IsTrue(info.TryGetCell(World(westAxis + 1f, midZ), out Vector2Int cell));
            Assert.AreEqual(new Vector2Int(0, 0), cell);
            Assert.IsTrue(info.TryGetCell(World(eastAxis - 1f, midZ), out cell));
            Assert.AreEqual(new Vector2Int(width - 1, 0), cell);
            Assert.IsTrue(info.TryGetCell(World(midX, southAxis + 1f), out cell));
            Assert.AreEqual(new Vector2Int(0, 0), cell);
            Assert.IsTrue(info.TryGetCell(World(midX, northAxis - 1f), out cell));
            Assert.AreEqual(new Vector2Int(0, height - 1), cell);

            Assert.IsFalse(info.TryGetCell(World(westAxis - 1f, midZ), out _), "West of the canvas");
            Assert.IsFalse(info.TryGetCell(World(eastAxis + 1f, midZ), out _), "East of the canvas");
            Assert.IsFalse(info.TryGetCell(World(midX, southAxis - 1f), out _), "South of the canvas");
            Assert.IsFalse(info.TryGetCell(World(midX, northAxis + 1f), out _), "North of the canvas");
            Assert.IsFalse(info.TryGetCell(World(eastAxis + 10000f, northAxis + 10000f), out _), "Far away");
        }

        [Test]
        public void TryGetCell_IsRelativeToTheRoot_AndIgnoresHeight()
        {
            CityGeneratorInfo info = CreateInfo("MovedRoot", new Vector2Int(3, 3));
            Vector3 localCenter = CityGeneratorGrid.GetBlockCenter(2, 1, 3, 3);

            Assert.IsTrue(info.TryGetCell(info.transform.position + localCenter + Vector3.up * 25f, out Vector2Int cell));
            Assert.AreEqual(new Vector2Int(2, 1), cell);
            Assert.IsFalse(info.TryGetCell(localCenter, out _), "The same local position, unmoved, lies far outside the moved city.");
        }

        [Test]
        public void ACityBuiltBeforeSpec23_HasNoCellsAndNoDistricts()
        {
            CityGeneratorInfo info = CreateOffsetRoot("Legacy").gameObject.AddComponent<CityGeneratorInfo>();
            CityModule city = CityGeneratorAPI.For(info).Value.City;

            Assert.IsFalse(info.TryGetCell(info.transform.position, out _));
            Assert.AreEqual(0, city.DistrictCount);
            Assert.IsNull(city.GetDistrictName(0));
            Assert.AreEqual(-1, DistrictAt(city, info.transform.position));
        }

        [Test]
        public void TryGetDistrictIndex_ACellInTwoDistricts_ResolvesToTheFirst()
        {
            CityGeneratorInfo info = CreateInfo("Overlap", new Vector2Int(2, 1));
            info.districts.Add(new CityDistrict { name = "A", cells = new List<Vector2Int> { new(0, 0) } });
            info.districts.Add(new CityDistrict { name = "B", cells = new List<Vector2Int> { new(0, 0), new(1, 0) } });
            info.districts.Add(new CityDistrict { name = "C", cells = null });

            Assert.IsTrue(info.TryGetDistrictIndex(new Vector2Int(0, 0), out int index));
            Assert.AreEqual(0, index);
            Assert.IsTrue(info.TryGetDistrictIndex(new Vector2Int(1, 0), out index));
            Assert.AreEqual(1, index);
        }

        // ---------------------------------------------------------------- assembler

        [Test]
        public void Assembler_StoresDistrictsInSettingsOrder_TrimmedAndCopied()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 23);
            settings.general.gridWidth = 4;
            settings.general.gridHeight = 3;
            settings.general.plazaCells.Add(new Vector2Int(3, 2));
            List<GameObject> pool = settings.buildingPrefabs.GetRange(0, 1);
            settings.districts.Add(MakeDistrict("  Old Town ", pool, new Vector2Int(0, 0), new Vector2Int(1, 0)));
            settings.districts.Add(MakeDistrict("Docks", pool, new Vector2Int(2, 0), new Vector2Int(2, 1)));
            AssertValid(settings);

            Transform root = CreateOffsetRoot("RectDistricts");
            CityGeneratorContentAssembler.Assemble(settings, root);
            CityGeneratorInfo info = root.GetComponent<CityGeneratorInfo>();

            Assert.AreEqual(CityGeneratorConstants.CellPitch, info.cellPitch);
            Assert.AreEqual(new Vector2Int(4, 3), info.layoutSize);
            Assert.AreEqual(2, info.districts.Count);
            Assert.AreEqual("Old Town", info.districts[0].name);
            Assert.AreEqual("Docks", info.districts[1].name);
            CollectionAssert.AreEqual(settings.districts[0].cells, info.districts[0].cells);
            CollectionAssert.AreEqual(settings.districts[1].cells, info.districts[1].cells);
            Assert.AreNotSame(settings.districts[0].cells, info.districts[0].cells, "The city keeps its own copy, never the settings' list.");

            CityModule city = CityGeneratorAPI.For(info).Value.City;
            Assert.AreEqual(2, city.DistrictCount);
            Assert.AreEqual("Old Town", city.GetDistrictName(0));
            Assert.AreEqual("Docks", city.GetDistrictName(1));
            Assert.IsNull(city.GetDistrictName(2));
            Assert.IsNull(city.GetDistrictName(-1));

            for (int d = 0; d < settings.districts.Count; d++)
            {
                foreach (Vector2Int cell in settings.districts[d].cells)
                    Assert.AreEqual(d, DistrictAt(city, WorldBlockCenter(info, cell.x, cell.y)), $"Centre of {cell}");
            }

            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 0, 2)), "A block in no district");
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 3, 2)), "A plaza");
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 0, 0) + new Vector3(-200f, 0f, 0f)), "Outside the city");

            // The street between (1,0) of Old Town and (2,0) of Docks.
            float axisX = CityGeneratorGrid.GetStreetAxisPosition(4, 2);
            float rowZ = CityGeneratorGrid.GetBlockAxisPosition(3, 0);
            Assert.AreEqual(0, DistrictAt(city, root.TransformPoint(new Vector3(axisX - 1f, 0f, rowZ))));
            Assert.AreEqual(1, DistrictAt(city, root.TransformPoint(new Vector3(axisX + 1f, 0f, rowZ))));
        }

        [Test]
        public void Assembler_CustomGrid_StoresTheCanvasAsLayout_AndAnEmptyCellHasNoDistrict()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 11);
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            List<GameObject> pool = settings.buildingPrefabs.GetRange(0, 1);
            settings.districts.Add(MakeDistrict("Harbour", pool, new Vector2Int(6, 5)));
            AssertValid(settings);

            Transform root = CreateOffsetRoot("CustomDistricts");
            CityGeneratorContentAssembler.Assemble(settings, root);
            CityGeneratorInfo info = root.GetComponent<CityGeneratorInfo>();

            int canvas = CityGeneratorConstants.MaxGridSize;
            Assert.AreEqual(new Vector2Int(canvas, canvas), info.layoutSize);
            Assert.AreEqual(CityGeneratorConstants.CellPitch, info.cellPitch);

            CityModule city = CityGeneratorAPI.For(info).Value.City;
            Assert.AreEqual(0, DistrictAt(city, WorldBlockCenter(info, 6, 5)));
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 5, 5)), "A real block in no district");
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 6, 6)), "The empty cell that closes the L");
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 0, 0) + new Vector3(-100f, 0f, -100f)), "Outside the canvas");
        }

        [Test]
        public void Assembler_WithoutDistricts_StoresAnEmptyList()
        {
            CityGeneratorSettings settings = MakeSettings(seed: 5);
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            AssertValid(settings);

            Transform root = CreateOffsetRoot("NoDistricts");
            CityGeneratorContentAssembler.Assemble(settings, root);
            CityGeneratorInfo info = root.GetComponent<CityGeneratorInfo>();

            Assert.IsNotNull(info.districts);
            Assert.AreEqual(0, info.districts.Count);
            Assert.AreEqual(new Vector2Int(2, 2), info.layoutSize);
            CityModule city = CityGeneratorAPI.For(info).Value.City;
            Assert.AreEqual(0, city.DistrictCount);
            Assert.AreEqual(-1, DistrictAt(city, WorldBlockCenter(info, 0, 0)));
        }
    }
}
