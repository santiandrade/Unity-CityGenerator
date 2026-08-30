using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds the ground layer of a generated city: the single road base slab, one sidewalk
    /// per block, and the dashed lane lines / zebra crossings that reproduce the reference
    /// city's marking pattern scaled to the grid size.
    /// </summary>
    internal static class CityGeneratorGroundBuilder
    {
        public static void BuildRoadBase(GameObject roadBasePrefab, Transform roadsGroup, int gridWidth, int gridHeight)
        {
            float width = gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;

            GameObject instance = InstantiatePrefab(roadBasePrefab, roadsGroup, "Road_Base");
            instance.transform.localPosition = new Vector3(0f, CityGeneratorConstants.RoadBaseY, 0f);
            CityGeneratorBoundsUtility.ScaleToFootprint(instance, width, depth);
        }

        private static readonly Vector2Int[] Neighbors4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        /// <summary>
        /// Custom Grid overload (SPEC 11): one full-pitch road base tile per real block (so
        /// adjacent tiles abut exactly, with no gap or double-cover), plus a margin strip beyond
        /// any edge that has no neighboring block -- reproducing the rectangular path's outer
        /// RoadBaseMargin at the shape's own contour instead of a fixed rectangle.
        /// </summary>
        public static void BuildRoadBase(GameObject roadBasePrefab, Transform roadsGroup, IReadOnlyCollection<Vector2Int> blockCells)
        {
            var cellSet = new HashSet<Vector2Int>(blockCells);
            int canvas = CityGeneratorConstants.MaxGridSize;
            int index = 0;

            foreach (Vector2Int cell in cellSet)
            {
                Vector3 center = CityGeneratorGrid.GetBlockCenter(cell.x, cell.y, canvas, canvas);

                GameObject instance = InstantiatePrefab(roadBasePrefab, roadsGroup, $"Road_Base_{index}");
                instance.transform.localPosition = new Vector3(center.x, CityGeneratorConstants.RoadBaseY, center.z);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, CityGeneratorConstants.CellPitch, CityGeneratorConstants.CellPitch);
                index++;

                foreach (Vector2Int dir in Neighbors4)
                {
                    if (cellSet.Contains(cell + dir))
                        continue;

                    bool horizontal = dir.x != 0;
                    Vector3 edgeOffset = new Vector3(dir.x, 0f, dir.y) * (CityGeneratorConstants.CellPitch / 2f + CityGeneratorConstants.RoadBaseMargin / 2f);
                    Vector3 marginCenter = center + edgeOffset;

                    GameObject marginInstance = InstantiatePrefab(roadBasePrefab, roadsGroup, $"Road_Base_{index}_Margin");
                    marginInstance.transform.localPosition = new Vector3(marginCenter.x, CityGeneratorConstants.RoadBaseY, marginCenter.z);
                    float w = horizontal ? CityGeneratorConstants.RoadBaseMargin : CityGeneratorConstants.CellPitch;
                    float d = horizontal ? CityGeneratorConstants.CellPitch : CityGeneratorConstants.RoadBaseMargin;
                    CityGeneratorBoundsUtility.ScaleToFootprint(marginInstance, w, d);
                    index++;
                }
            }
        }

        public static void BuildSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, IReadOnlyList<BlockCell> blocks)
        {
            foreach (BlockCell block in blocks)
            {
                string name = $"Sidewalk_{block.gridX}_{block.gridY}";
                GameObject instance = InstantiatePrefab(sidewalkPrefab, sidewalksGroup, name);
                instance.transform.localPosition = new Vector3(block.center.x, CityGeneratorConstants.SidewalkY, block.center.z);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, CityGeneratorConstants.BlockSize, CityGeneratorConstants.BlockSize);
            }
        }

        public static void BuildRoadMarkings(GameObject dashPrefab, GameObject zebraPrefab, Transform markingsGroup, int gridWidth, int gridHeight)
        {
            BuildDashes(dashPrefab, markingsGroup, gridWidth, gridHeight);
            BuildZebraCrossings(zebraPrefab, markingsGroup, gridWidth, gridHeight);
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): dashes are drawn only on street segments adjacent to at
        /// least one real block; zebra crossings only at intersections with all 4 surrounding
        /// cells real (same rule as the traffic light criterion).
        /// </summary>
        public static void BuildRoadMarkings(GameObject dashPrefab, GameObject zebraPrefab, Transform markingsGroup, IReadOnlyCollection<Vector2Int> blockCells)
        {
            var cellSet = new HashSet<Vector2Int>(blockCells);
            BuildDashesCustom(dashPrefab, markingsGroup, cellSet);
            BuildZebraCrossingsCustom(zebraPrefab, markingsGroup, cellSet);
        }

        private static bool IsFourWayIntersection(HashSet<Vector2Int> cells, int i, int j)
        {
            return cells.Contains(new Vector2Int(i - 1, j - 1))
                && cells.Contains(new Vector2Int(i, j - 1))
                && cells.Contains(new Vector2Int(i - 1, j))
                && cells.Contains(new Vector2Int(i, j));
        }

        private static void BuildDashesCustom(GameObject dashPrefab, Transform group, HashSet<Vector2Int> cells)
        {
            int canvas = CityGeneratorConstants.MaxGridSize;
            int dashIndex = 0;

            for (int j = 0; j <= canvas; j++)
            {
                float z = CityGeneratorGrid.GetStreetAxisPosition(canvas, j);
                for (int k = 0; k < canvas; k++)
                {
                    bool south = cells.Contains(new Vector2Int(k, j - 1));
                    bool north = cells.Contains(new Vector2Int(k, j));
                    if (!south && !north)
                        continue;

                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(canvas, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(canvas, k + 1);
                    bool excludeStart = IsFourWayIntersection(cells, k, j);
                    bool excludeEnd = IsFourWayIntersection(cells, k + 1, j);
                    PlaceDashSegment(dashPrefab, group, segmentStart, z, isVertical: false,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }

            for (int i = 0; i <= canvas; i++)
            {
                float x = CityGeneratorGrid.GetStreetAxisPosition(canvas, i);
                for (int k = 0; k < canvas; k++)
                {
                    bool west = cells.Contains(new Vector2Int(i - 1, k));
                    bool east = cells.Contains(new Vector2Int(i, k));
                    if (!west && !east)
                        continue;

                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(canvas, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(canvas, k + 1);
                    bool excludeStart = IsFourWayIntersection(cells, i, k);
                    bool excludeEnd = IsFourWayIntersection(cells, i, k + 1);
                    PlaceDashSegment(dashPrefab, group, segmentStart, x, isVertical: true,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }
        }

        private static void BuildZebraCrossingsCustom(GameObject zebraPrefab, Transform group, HashSet<Vector2Int> cells)
        {
            int canvas = CityGeneratorConstants.MaxGridSize;
            int zebraIndex = 0;
            for (int i = 1; i < canvas; i++)
            {
                for (int j = 1; j < canvas; j++)
                {
                    if (!IsFourWayIntersection(cells, i, j))
                        continue;

                    float x = CityGeneratorGrid.GetStreetAxisPosition(canvas, i);
                    float z = CityGeneratorGrid.GetStreetAxisPosition(canvas, j);
                    Vector3 intersection = new Vector3(x, CityGeneratorConstants.MarkingY, z);

                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.right, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.left, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.forward, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.back, ref zebraIndex);
                }
            }
        }

        // Horizontal streets (constant Z, running along X): gridHeight + 1 axis lines, each split
        // into gridWidth segments of one cell pitch. Vertical streets: the transposed case.
        private static void BuildDashes(GameObject dashPrefab, Transform group, int gridWidth, int gridHeight)
        {
            int dashIndex = 0;

            for (int j = 0; j <= gridHeight; j++)
            {
                bool rowHasCrossings = j >= 1 && j <= gridHeight - 1;
                float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                for (int k = 0; k < gridWidth; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, k + 1);
                    bool excludeStart = rowHasCrossings && k >= 1;
                    bool excludeEnd = rowHasCrossings && k <= gridWidth - 2;
                    PlaceDashSegment(dashPrefab, group, segmentStart, z, isVertical: false,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }

            for (int i = 0; i <= gridWidth; i++)
            {
                bool columnHasCrossings = i >= 1 && i <= gridWidth - 1;
                float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                for (int k = 0; k < gridHeight; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, k + 1);
                    bool excludeStart = columnHasCrossings && k >= 1;
                    bool excludeEnd = columnHasCrossings && k <= gridHeight - 2;
                    PlaceDashSegment(dashPrefab, group, segmentStart, x, isVertical: true,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }
        }

        // A dash is skipped if it falls within a crosswalk's exclusion radius of an intersection
        // that has zebra crossings on it — otherwise the dashed centreline runs straight through
        // the crosswalk stripes.
        private static void PlaceDashSegment(
            GameObject dashPrefab, Transform group, float segmentStart, float crossAxisPosition, bool isVertical,
            bool excludeNearStart, float startAxisPosition, bool excludeNearEnd, float endAxisPosition, ref int dashIndex)
        {
            float spacing = CityGeneratorConstants.CellPitch / CityGeneratorConstants.DashesPerSegment;
            Quaternion rotation = isVertical ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

            for (int d = 0; d < CityGeneratorConstants.DashesPerSegment; d++)
            {
                float alongAxis = segmentStart + spacing * (d + 0.5f);

                if (excludeNearStart && Mathf.Abs(alongAxis - startAxisPosition) < CityGeneratorConstants.DashZebraExclusionRadius)
                    continue;
                if (excludeNearEnd && Mathf.Abs(alongAxis - endAxisPosition) < CityGeneratorConstants.DashZebraExclusionRadius)
                    continue;

                Vector3 position = isVertical
                    ? new Vector3(crossAxisPosition, CityGeneratorConstants.MarkingY, alongAxis)
                    : new Vector3(alongAxis, CityGeneratorConstants.MarkingY, crossAxisPosition);

                string name = (isVertical ? "Dash_V_" : "Dash_H_") + dashIndex;
                GameObject instance = InstantiatePrefab(dashPrefab, group, name);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                dashIndex++;
            }
        }

        // Zebra crossings only mark intersections fully surrounded by blocks (both axis indices
        // strictly interior), matching the reference city's 4 signalled inner crossings.
        private static void BuildZebraCrossings(GameObject zebraPrefab, Transform group, int gridWidth, int gridHeight)
        {
            int zebraIndex = 0;
            for (int i = 1; i < gridWidth; i++)
            {
                for (int j = 1; j < gridHeight; j++)
                {
                    float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                    float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                    Vector3 intersection = new Vector3(x, CityGeneratorConstants.MarkingY, z);

                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.right, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.left, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.forward, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.back, ref zebraIndex);
                }
            }
        }

        private static void PlaceZebraArm(GameObject zebraPrefab, Transform group, Vector3 intersection, Vector3 direction, ref int zebraIndex)
        {
            // The arm sits at a single fixed offset from the intersection centre (the stop
            // line), and its stripes are spread sideways across the crossing — perpendicular
            // to the arm direction, not strung out along it.
            bool isEastWestArm = Mathf.Abs(direction.x) > 0f;
            Quaternion rotation = isEastWestArm ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            Vector3 spreadAxis = isEastWestArm ? Vector3.forward : Vector3.right;
            Vector3 armCentre = intersection + direction * CityGeneratorConstants.ZebraArmOffset;

            float half = (CityGeneratorConstants.ZebraStripesPerArm - 1) / 2f;
            for (int s = 0; s < CityGeneratorConstants.ZebraStripesPerArm; s++)
            {
                float offset = (s - half) * CityGeneratorConstants.ZebraStripeSpacing;
                Vector3 position = armCentre + spreadAxis * offset;
                position.y = CityGeneratorConstants.MarkingY;

                string name = "Zebra_" + zebraIndex;
                GameObject instance = InstantiatePrefab(zebraPrefab, group, name);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                zebraIndex++;
            }
        }

        private static GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            return instance;
        }
    }
}
