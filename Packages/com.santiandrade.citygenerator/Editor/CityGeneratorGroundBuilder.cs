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

                bool northOpen = !cellSet.Contains(cell + new Vector2Int(0, 1));
                bool southOpen = !cellSet.Contains(cell + new Vector2Int(0, -1));
                bool eastOpen = !cellSet.Contains(cell + new Vector2Int(1, 0));
                bool westOpen = !cellSet.Contains(cell + new Vector2Int(-1, 0));

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

                // A convex corner (two perpendicular open edges) leaves a RoadBaseMargin-square
                // gap diagonally beyond the cell that neither edge strip above covers.
                foreach (Vector2Int corner in new[] { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) })
                {
                    bool xOpen = corner.x > 0 ? eastOpen : westOpen;
                    bool zOpen = corner.y > 0 ? northOpen : southOpen;
                    if (!xOpen || !zOpen)
                        continue;

                    Vector3 cornerOffset = new Vector3(corner.x, 0f, corner.y) * (CityGeneratorConstants.CellPitch / 2f + CityGeneratorConstants.RoadBaseMargin / 2f);
                    Vector3 cornerCenter = center + cornerOffset;

                    GameObject cornerInstance = InstantiatePrefab(roadBasePrefab, roadsGroup, $"Road_Base_{index}_Corner");
                    cornerInstance.transform.localPosition = new Vector3(cornerCenter.x, CityGeneratorConstants.RoadBaseY, cornerCenter.z);
                    CityGeneratorBoundsUtility.ScaleToFootprint(cornerInstance, CityGeneratorConstants.RoadBaseMargin, CityGeneratorConstants.RoadBaseMargin);
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
        /// least one real block; zebra crossings only at intersections with at least 3 real arms
        /// (same rule as the traffic light criterion).
        /// </summary>
        public static void BuildRoadMarkings(GameObject dashPrefab, GameObject zebraPrefab, Transform markingsGroup, IReadOnlyCollection<Vector2Int> blockCells)
        {
            var cellSet = new HashSet<Vector2Int>(blockCells);
            BuildDashesCustom(dashPrefab, markingsGroup, cellSet);
            BuildZebraCrossingsCustom(zebraPrefab, markingsGroup, cellSet);
        }

        // An intersection needs a crossing/light only when it's a real decision point (at least 3
        // real arms: a full 4-way or a T-intersection) -- a plain straight-through point (2
        // opposite arms) or a perpendicular L-corner (exactly 2 arms, a street simply bending 90
        // degrees with only one possible way through) never has crossing traffic. Mirrors
        // CityGeneratorTrafficBuilder's identical rule for placing traffic lights.
        private static bool HasCrossTraffic(HashSet<Vector2Int> cells, int i, int j)
        {
            bool east = cells.Contains(new Vector2Int(i, j - 1)) || cells.Contains(new Vector2Int(i, j));
            bool west = cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i - 1, j));
            bool north = cells.Contains(new Vector2Int(i - 1, j)) || cells.Contains(new Vector2Int(i, j));
            bool south = cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i, j - 1));
            int realArmCount = (east ? 1 : 0) + (west ? 1 : 0) + (north ? 1 : 0) + (south ? 1 : 0);
            return realArmCount >= 3;
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
                    bool excludeStart = HasCrossTraffic(cells, k, j);
                    bool excludeEnd = HasCrossTraffic(cells, k + 1, j);
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
                    bool excludeStart = HasCrossTraffic(cells, i, k);
                    bool excludeEnd = HasCrossTraffic(cells, i, k + 1);
                    PlaceDashSegment(dashPrefab, group, segmentStart, x, isVertical: true,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }
        }

        private static void BuildZebraCrossingsCustom(GameObject zebraPrefab, Transform group, HashSet<Vector2Int> cells)
        {
            int canvas = CityGeneratorConstants.MaxGridSize;
            int zebraIndex = 0;
            for (int i = 0; i <= canvas; i++)
            {
                for (int j = 0; j <= canvas; j++)
                {
                    bool east = cells.Contains(new Vector2Int(i, j - 1)) || cells.Contains(new Vector2Int(i, j));
                    bool west = cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i - 1, j));
                    bool north = cells.Contains(new Vector2Int(i - 1, j)) || cells.Contains(new Vector2Int(i, j));
                    bool south = cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i, j - 1));
                    int realArmCount = (east ? 1 : 0) + (west ? 1 : 0) + (north ? 1 : 0) + (south ? 1 : 0);
                    if (realArmCount < 3)
                        continue;

                    float x = CityGeneratorGrid.GetStreetAxisPosition(canvas, i);
                    float z = CityGeneratorGrid.GetStreetAxisPosition(canvas, j);
                    Vector3 intersection = new Vector3(x, CityGeneratorConstants.MarkingY, z);

                    // Only the arms that actually have a street get a crossing stripe -- a T-intersection
                    // has no crosswalk on the side with no road to cross into.
                    if (east) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.right, ref zebraIndex);
                    if (west) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.left, ref zebraIndex);
                    if (north) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.forward, ref zebraIndex);
                    if (south) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.back, ref zebraIndex);
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
                float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                for (int k = 0; k < gridWidth; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, k + 1);
                    bool excludeStart = HasCrossTrafficRect(k, j, gridWidth, gridHeight);
                    bool excludeEnd = HasCrossTrafficRect(k + 1, j, gridWidth, gridHeight);
                    PlaceDashSegment(dashPrefab, group, segmentStart, z, isVertical: false,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }

            for (int i = 0; i <= gridWidth; i++)
            {
                float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                for (int k = 0; k < gridHeight; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, k);
                    float segmentEnd = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, k + 1);
                    bool excludeStart = HasCrossTrafficRect(i, k, gridWidth, gridHeight);
                    bool excludeEnd = HasCrossTrafficRect(i, k + 1, gridWidth, gridHeight);
                    PlaceDashSegment(dashPrefab, group, segmentStart, x, isVertical: true,
                        excludeStart, segmentStart, excludeEnd, segmentEnd, ref dashIndex);
                }
            }
        }

        // Rectangular-grid counterpart of HasCrossTraffic: an intersection needs a crossing/light
        // only when it has at least 3 real arms (bounded by the grid edge) -- a perimeter corner
        // (exactly 2 perpendicular arms) never has crossing traffic.
        private static bool HasCrossTrafficRect(int i, int j, int gridWidth, int gridHeight)
        {
            int realArmCount = (i < gridWidth ? 1 : 0) + (i > 0 ? 1 : 0) + (j < gridHeight ? 1 : 0) + (j > 0 ? 1 : 0);
            return realArmCount >= 3;
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

        // Zebra crossings mark every intersection with at least 3 real arms (a full 4-way, or a
        // T-intersection along the grid's own border) -- a perimeter corner (exactly 2
        // perpendicular arms) never has crossing traffic. Mirrors the traffic light criterion.
        private static void BuildZebraCrossings(GameObject zebraPrefab, Transform group, int gridWidth, int gridHeight)
        {
            int zebraIndex = 0;
            for (int i = 0; i <= gridWidth; i++)
            {
                for (int j = 0; j <= gridHeight; j++)
                {
                    bool east = i < gridWidth;
                    bool west = i > 0;
                    bool north = j < gridHeight;
                    bool south = j > 0;
                    int realArmCount = (east ? 1 : 0) + (west ? 1 : 0) + (north ? 1 : 0) + (south ? 1 : 0);
                    if (realArmCount < 3)
                        continue;

                    float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                    float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                    Vector3 intersection = new Vector3(x, CityGeneratorConstants.MarkingY, z);

                    // Only the arms that actually have a street get a crossing stripe -- a
                    // T-intersection has no crosswalk on the side with no road to cross into.
                    if (east) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.right, ref zebraIndex);
                    if (west) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.left, ref zebraIndex);
                    if (north) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.forward, ref zebraIndex);
                    if (south) PlaceZebraArm(zebraPrefab, group, intersection, Vector3.back, ref zebraIndex);
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
