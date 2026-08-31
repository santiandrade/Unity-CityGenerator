using System.Collections.Generic;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// The sidewalk band that closes a generated city at its outer contour: a city always ends in
    /// sidewalk, never in bare asphalt, and that sidewalk is walkable -- reachable from the blocks
    /// through the crosswalk already painted at every border T-intersection.
    ///
    /// Follows <see cref="SeededGenerationTests"/>'s pattern of offsetting each city root far away
    /// from this project's own open scene and from the other fixtures.
    /// </summary>
    internal class PerimeterSidewalkTests
    {
        // CityGeneratorConstants is Editor-internal but visible to this assembly; the derived
        // radii are spelled out here so the expectations stay independent of the builder's own
        // arithmetic.
        private const float CellPitch = 56f;
        private const float BlockRadius = CellPitch / 2f;                 // 28: half a cell pitch
        private const float StreetOuterRadius = BlockRadius + 5f;         // 33: far edge of the perimeter street
        private const float GroundRadius = BlockRadius + 11f;             // 39: far edge of the ground

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

        private static CityGeneratorSettings MakeSettings(int gridWidth, int gridHeight)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = gridWidth;
            settings.general.gridHeight = gridHeight;
            settings.general.useCustomSeed = true;
            settings.general.seed = 2024;
            return settings;
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
        private static List<Rect> PerimeterTiles(Transform root)
        {
            var tiles = new List<Rect>();
            foreach (Transform sidewalk in root.Find("Sidewalks"))
            {
                if (!sidewalk.name.StartsWith("Sidewalk_Perimeter"))
                    continue;

                Vector3 size = sidewalk.GetComponentInChildren<Renderer>().bounds.size;
                Vector3 centre = sidewalk.localPosition;
                tiles.Add(new Rect(centre.x - size.x / 2f, centre.z - size.z / 2f, size.x, size.z));
            }

            return tiles;
        }

        // Chebyshev distance from `point` to the nearest cell centre: the shape dilated by r is
        // exactly the set of points whose value here is <= r.
        private static float DistanceToShape(Vector2 point, IEnumerable<Vector3> cellCentres)
        {
            float best = float.MaxValue;
            foreach (Vector3 centre in cellCentres)
            {
                float distance = Mathf.Max(Mathf.Abs(point.x - centre.x), Mathf.Abs(point.y - centre.z));
                best = Mathf.Min(best, distance);
            }

            return best;
        }

        private static void AssertPerimeterBandIsExact(Transform root, List<Vector3> cellCentres)
        {
            List<Rect> tiles = PerimeterTiles(root);
            Assert.Greater(tiles.Count, 0, "The city's outer contour must end in sidewalk, not in bare asphalt.");

            for (int a = 0; a < tiles.Count; a++)
            {
                for (int b = a + 1; b < tiles.Count; b++)
                {
                    bool overlaps = tiles[a].xMin < tiles[b].xMax - 0.01f && tiles[b].xMin < tiles[a].xMax - 0.01f
                        && tiles[a].yMin < tiles[b].yMax - 0.01f && tiles[b].yMin < tiles[a].yMax - 0.01f;
                    Assert.IsFalse(overlaps, $"Perimeter sidewalk tiles {a} and {b} overlap: coplanar tiles at the same Y would z-fight.");
                }
            }

            // Independent brute-force reconstruction of the band -- every sampled point between the
            // street's far edge and the ground's far edge must be paved, and nothing inside the
            // street may be.
            float extent = 0f;
            foreach (Vector3 centre in cellCentres)
                extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(centre.x), Mathf.Abs(centre.z)));
            extent += GroundRadius + CellPitch;

            for (float x = -extent; x <= extent; x += 2.5f)
            {
                for (float z = -extent; z <= extent; z += 2.5f)
                {
                    var point = new Vector2(x, z);
                    float distance = DistanceToShape(point, cellCentres);

                    bool covered = false;
                    foreach (Rect tile in tiles)
                    {
                        if (tile.Contains(point))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (distance > StreetOuterRadius + 0.5f && distance < GroundRadius - 0.5f)
                        Assert.IsTrue(covered, $"({x}, {z}) is on the perimeter band but no sidewalk covers it.");
                    else if (distance < StreetOuterRadius - 0.5f || distance > GroundRadius + 0.5f)
                        Assert.IsFalse(covered, $"({x}, {z}) is street or outside the city, but a perimeter sidewalk covers it.");
                }
            }
        }

        [Test]
        public void Assemble_RectangularGrid_PerimeterBandPavesExactlyTheGroundBeyondTheOuterStreet()
        {
            CityGeneratorSettings settings = MakeSettings(3, 3);
            Transform root = CreateOffsetCityRoot("Perimeter_3x3");
            CityGeneratorContentAssembler.Assemble(settings, root);

            var centres = new List<Vector3>();
            for (int gx = 0; gx < 3; gx++)
            {
                for (int gy = 0; gy < 3; gy++)
                    centres.Add(new Vector3((gx - 1) * CellPitch, 0f, (gy - 1) * CellPitch));
            }

            AssertPerimeterBandIsExact(root, centres);
        }

        [Test]
        public void Assemble_CustomGridLShape_PerimeterBandFollowsTheShapesOwnContour()
        {
            var shape = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };
            CityGeneratorSettings settings = MakeCustomGridSettings(shape);
            Transform root = CreateOffsetCityRoot("Perimeter_LShape");
            CityGeneratorContentAssembler.Assemble(settings, root);

            var centres = new List<Vector3>();
            foreach (Vector2Int cell in shape)
                centres.Add(new Vector3((cell.x - 4.5f) * CellPitch, 0f, (cell.y - 4.5f) * CellPitch));

            AssertPerimeterBandIsExact(root, centres);
        }

        [Test]
        public void Assemble_RectangularGrid_PerimeterWalkwayIsReachableFromTheBlocks()
        {
            CityGeneratorSettings settings = MakeSettings(3, 3);
            Transform root = CreateOffsetCityRoot("PerimeterWalkway_3x3");
            CityGeneratorContentAssembler.Assemble(settings, root);

            var network = root.GetComponentInChildren<PedestrianNetwork>();
            Assert.IsNotNull(network);

            // The outermost Ring node can only be on the perimeter walkway: a block's own ring
            // stays within BlockRadius of its centre, a whole street closer in.
            int perimeter = -1;
            int blockRing = -1;
            float furthest = float.MinValue;
            float nearest = float.MaxValue;

            for (int i = 0; i < network.NodeCount; i++)
            {
                PedestrianNode node = network.GetNode(i);
                if (node.Kind != PedestrianNodeKind.Ring)
                    continue;

                // Node positions are authored in the city's own frame, independent of where the
                // root sits in the world.
                float distance = Mathf.Abs(node.Position.x) + Mathf.Abs(node.Position.z);
                if (distance > furthest)
                {
                    furthest = distance;
                    perimeter = i;
                }

                if (distance < nearest)
                {
                    nearest = distance;
                    blockRing = i;
                }
            }

            Assert.GreaterOrEqual(perimeter, 0);
            Assert.GreaterOrEqual(blockRing, 0);
            Assert.AreEqual(network.ComponentOf(blockRing), network.ComponentOf(perimeter),
                "A pedestrian must be able to walk from a block's ring out onto the perimeter sidewalk.");
        }
    }
}
