using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// The "empty block" ground that fills the gaps of a Custom Grid shape, so a custom city still
    /// reads as the plain rectangle of its own bounding box. The fill must tile that rectangle
    /// exactly: no gap, no overlapping (coplanar tiles at the same Y would z-fight), nothing on top
    /// of the road base / perimeter sidewalk it abuts, and nothing outside the rectangle.
    ///
    /// Follows <see cref="PerimeterSidewalkTests"/>'s pattern of offsetting each city root far away
    /// from this project's own open scene and from the other fixtures.
    /// </summary>
    internal class EmptyBlockFillTests
    {
        // Spelled out rather than read from CityGeneratorConstants so the expectations stay
        // independent of the builder's own arithmetic.
        private const float CellPitch = 56f;
        private const float BlockRadius = CellPitch / 2f;      // 28: half a cell pitch
        private const float GroundRadius = BlockRadius + 11f;  // 39: far edge of the paved ground
        private const float CanvasCentreIndex = 4.5f;          // MaxGridSize (10) / 2 - 0.5

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

        private static CityGeneratorSettings MakeCustomGridSettings(IReadOnlyList<Vector2Int> shape)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int>(shape);
            settings.general.plazaCells = new List<Vector2Int>();
            settings.general.useCustomSeed = true;
            settings.general.seed = 2024;
            return settings;
        }

        // Footprints in the city root's own local space, which is where every builder places its
        // content: Renderer.bounds is world-space and not reliably up to date for a root that was
        // moved before generating, while localPosition/size are exactly what was authored.
        private static List<Rect> EmptyBlockTiles(Transform root)
        {
            var tiles = new List<Rect>();
            foreach (Transform tile in root.Find("EmptyBlocks"))
            {
                Vector3 size = tile.GetComponentInChildren<Renderer>().bounds.size;
                Vector3 centre = tile.localPosition;
                tiles.Add(new Rect(centre.x - size.x / 2f, centre.z - size.z / 2f, size.x, size.z));
            }

            return tiles;
        }

        private static Vector3 CentreOf(Vector2Int cell)
        {
            return new Vector3((cell.x - CanvasCentreIndex) * CellPitch, 0f, (cell.y - CanvasCentreIndex) * CellPitch);
        }

        // Chebyshev distance from `point` to the nearest cell centre: the shape dilated by r is
        // exactly the set of points whose value here is <= r.
        private static float DistanceToShape(Vector2 point, IReadOnlyList<Vector2Int> shape)
        {
            float best = float.MaxValue;
            foreach (Vector2Int cell in shape)
            {
                Vector3 centre = CentreOf(cell);
                float distance = Mathf.Max(Mathf.Abs(point.x - centre.x), Mathf.Abs(point.y - centre.z));
                best = Mathf.Min(best, distance);
            }

            return best;
        }

        /// <summary>The bounding rectangle of <paramref name="shape"/>, grown by the ground margin — the outer footprint the city as a whole must end up with.</summary>
        private static Rect BoundingRect(IReadOnlyList<Vector2Int> shape)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Vector2Int cell in shape)
            {
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            Vector3 min = CentreOf(new Vector2Int(minX, minY));
            Vector3 max = CentreOf(new Vector2Int(maxX, maxY));
            return Rect.MinMaxRect(min.x - GroundRadius, min.z - GroundRadius, max.x + GroundRadius, max.z + GroundRadius);
        }

        private static void AssertFillIsExact(Transform root, IReadOnlyList<Vector2Int> shape)
        {
            List<Rect> tiles = EmptyBlockTiles(root);
            Rect bounds = BoundingRect(shape);

            for (int a = 0; a < tiles.Count; a++)
            {
                Assert.IsTrue(
                    tiles[a].xMin > bounds.xMin - 0.01f && tiles[a].xMax < bounds.xMax + 0.01f
                    && tiles[a].yMin > bounds.yMin - 0.01f && tiles[a].yMax < bounds.yMax + 0.01f,
                    $"Empty block tile {a} pokes outside the city's bounding rectangle.");

                for (int b = a + 1; b < tiles.Count; b++)
                {
                    bool overlaps = tiles[a].xMin < tiles[b].xMax - 0.01f && tiles[b].xMin < tiles[a].xMax - 0.01f
                        && tiles[a].yMin < tiles[b].yMax - 0.01f && tiles[b].yMin < tiles[a].yMax - 0.01f;
                    Assert.IsFalse(overlaps, $"Empty block tiles {a} and {b} overlap: coplanar tiles at the same Y would z-fight.");
                }
            }

            // Independent brute-force reconstruction: inside the bounding rectangle, every sampled
            // point the paved ground does not reach must be filled, and no point it does reach may
            // be — the fill butts against the outer edge of the perimeter sidewalk instead of
            // hiding it. Sampled off the tile boundaries, which are all multiples of a metre.
            for (float x = bounds.xMin + 0.37f; x < bounds.xMax; x += 1.13f)
            {
                for (float z = bounds.yMin + 0.53f; z < bounds.yMax; z += 1.13f)
                {
                    var point = new Vector2(x, z);

                    bool covered = false;
                    foreach (Rect tile in tiles)
                    {
                        if (tile.Contains(point))
                        {
                            covered = true;
                            break;
                        }
                    }

                    bool paved = DistanceToShape(point, shape) < GroundRadius;
                    if (paved)
                        Assert.IsFalse(covered, $"({x}, {z}) is road base or perimeter sidewalk, but an empty block covers it.");
                    else
                        Assert.IsTrue(covered, $"({x}, {z}) is a gap of the custom shape but no empty block fills it.");
                }
            }
        }

        [Test]
        public void Assemble_CustomGridLShape_EmptyBlocksFillTheBoundingRectangleExactly()
        {
            var shape = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            CityGeneratorSettings settings = MakeCustomGridSettings(shape);
            Transform root = CreateOffsetCityRoot("EmptyBlocks_LShape");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Assert.Greater(EmptyBlockTiles(root).Count, 0, "The missing corner of an L shape must be filled with empty-block ground.");
            AssertFillIsExact(root, shape);
        }

        [Test]
        public void Assemble_CustomGridWithHole_EmptyBlocksFillTheHole()
        {
            var shape = new List<Vector2Int>();
            for (int x = 4; x <= 6; x++)
            {
                for (int y = 4; y <= 6; y++)
                {
                    if (x != 5 || y != 5)
                        shape.Add(new Vector2Int(x, y));
                }
            }

            CityGeneratorSettings settings = MakeCustomGridSettings(shape);
            Transform root = CreateOffsetCityRoot("EmptyBlocks_Donut");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Assert.Greater(EmptyBlockTiles(root).Count, 0, "The inner hole of a donut shape must be filled with empty-block ground.");
            AssertFillIsExact(root, shape);
        }

        [Test]
        public void Assemble_CustomGridRectangle_NeedsNoEmptyBlocks()
        {
            var shape = new List<Vector2Int>();
            for (int x = 4; x <= 6; x++)
            {
                for (int y = 4; y <= 5; y++)
                    shape.Add(new Vector2Int(x, y));
            }

            CityGeneratorSettings settings = MakeCustomGridSettings(shape);
            Transform root = CreateOffsetCityRoot("EmptyBlocks_Rectangle");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Assert.AreEqual(0, EmptyBlockTiles(root).Count,
                "A custom shape that already is a full rectangle has no gap to fill, and must generate exactly what the rectangular grid path does.");
        }
    }
}
