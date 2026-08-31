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

        /// <summary>
        /// Custom Grid overload (SPEC 11): one full-pitch road base tile per real block (so
        /// adjacent tiles abut exactly, with no gap or double-cover), plus the outer band beyond
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
            }

            float half = CityGeneratorConstants.CellPitch / 2f;
            foreach (BandRect rect in EnumerateBand(cellSet, CanvasCentre, half, half + CityGeneratorConstants.RoadBaseMargin))
            {
                GameObject marginInstance = InstantiatePrefab(roadBasePrefab, roadsGroup, $"Road_Base_{index}_Margin");
                marginInstance.transform.localPosition = new Vector3(rect.centerX, CityGeneratorConstants.RoadBaseY, rect.centerZ);
                CityGeneratorBoundsUtility.ScaleToFootprint(marginInstance, rect.width, rect.depth);
                index++;
            }
        }

        /// <summary>
        /// The sidewalk band that closes the city at its outer contour, so a generated city always
        /// ends in sidewalk rather than in bare asphalt: a PerimeterSidewalkWidth strip laid on the
        /// far side of every perimeter street, following the shape's own contour (including the
        /// inner contour of a Custom Grid hole).
        /// </summary>
        public static void BuildPerimeterSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, int gridWidth, int gridHeight)
        {
            var cellSet = new HashSet<Vector2Int>();
            for (int gx = 0; gx < gridWidth; gx++)
            {
                for (int gy = 0; gy < gridHeight; gy++)
                {
                    cellSet.Add(new Vector2Int(gx, gy));
                }
            }

            BuildPerimeterSidewalks(sidewalkPrefab, sidewalksGroup, cellSet,
                cell => CityGeneratorGrid.GetBlockCenter(cell.x, cell.y, gridWidth, gridHeight));
        }

        /// <summary>Custom Grid overload of <see cref="BuildPerimeterSidewalks(GameObject, Transform, int, int)"/>.</summary>
        public static void BuildPerimeterSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, IReadOnlyCollection<Vector2Int> blockCells)
        {
            BuildPerimeterSidewalks(sidewalkPrefab, sidewalksGroup, new HashSet<Vector2Int>(blockCells), CanvasCentre);
        }

        private static void BuildPerimeterSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, HashSet<Vector2Int> cellSet, System.Func<Vector2Int, Vector3> centreOf)
        {
            float half = CityGeneratorConstants.CellPitch / 2f;
            float inner = half + CityGeneratorConstants.StreetWidth / 2f;
            float outer = half + CityGeneratorConstants.RoadBaseMargin;

            int index = 0;
            foreach (BandRect rect in EnumerateBand(cellSet, centreOf, inner, outer))
            {
                GameObject instance = InstantiatePrefab(sidewalkPrefab, sidewalksGroup, $"Sidewalk_Perimeter_{index}");
                instance.transform.localPosition = new Vector3(rect.centerX, CityGeneratorConstants.SidewalkY, rect.centerZ);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, rect.width, rect.depth);
                index++;
            }
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): fills every gap of the shape with the "empty block"
        /// ground prefab, so a custom city still reads as the plain rectangle its own bounding box
        /// describes instead of ending in holes of empty space.
        ///
        /// The filled region is that bounding rectangle grown by <see cref="CityGeneratorConstants.RoadBaseMargin"/>
        /// -- exactly the outer footprint a rectangular grid of the same block count would have,
        /// and the same rectangle the minimap snapshot and the validator's View Radius check
        /// already assume -- minus everything the road base and the perimeter sidewalk already
        /// cover, i.e. the shape dilated by CellPitch/2 + RoadBaseMargin. The fill therefore butts
        /// exactly against the outer edge of the perimeter sidewalk rather than hiding it, keeping
        /// "a generated city always ends in sidewalk" true.
        /// </summary>
        public static void BuildEmptyBlocks(GameObject emptyBlockPrefab, Transform emptyBlocksGroup, IReadOnlyCollection<Vector2Int> blockCells)
        {
            if (emptyBlockPrefab == null)
                return;

            var cellSet = new HashSet<Vector2Int>(blockCells);
            int index = 0;
            foreach (BandRect rect in EnumerateEmptyFill(cellSet, CanvasCentre))
            {
                GameObject instance = InstantiatePrefab(emptyBlockPrefab, emptyBlocksGroup, $"Empty_Block_{index}");
                // Same Y datum as a plaza lawn: the empty fill is ground cover, sitting flush with
                // the sidewalk surface it abuts.
                instance.transform.localPosition = new Vector3(rect.centerX, CityGeneratorConstants.GroundDatumY, rect.centerZ);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, rect.width, rect.depth);
                index++;
            }
        }

        /// <summary>
        /// Tiles the part of the shape's bounding rectangle (grown by RoadBaseMargin) that no road
        /// base or perimeter sidewalk covers. Same per-*missing*-cell approach as
        /// <see cref="EnumerateBand"/>, and for the same reason -- a per-real-cell tiling overlaps
        /// at concave corners and leaves an unpaved notch at convex ones.
        ///
        /// A missing cell's own 56 m square is cut by the two coordinates where the covered
        /// dilation's boundary can fall (+-(CellPitch - (CellPitch/2 + RoadBaseMargin))) into at
        /// most 3x3 sub-rectangles, each wholly covered or wholly uncovered, then clipped to the
        /// bounding rectangle and merged along X. The clip edge of the surrounding ring of cells
        /// falls on that same coordinate, so it needs no extra cut of its own.
        /// </summary>
        private static IEnumerable<BandRect> EnumerateEmptyFill(HashSet<Vector2Int> cells, System.Func<Vector2Int, Vector3> centreOf)
        {
            const float half = CityGeneratorConstants.CellPitch / 2f;
            const float epsilon = 0.001f;
            float coveredEdge = CityGeneratorConstants.CellPitch - (half + CityGeneratorConstants.RoadBaseMargin);
            float[] bounds = { -half, -coveredEdge, coveredEdge, half };

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Vector2Int cell in cells)
            {
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            if (minX > maxX)
                yield break;

            Vector3 minCentre = centreOf(new Vector2Int(minX, minY));
            Vector3 maxCentre = centreOf(new Vector2Int(maxX, maxY));
            float rectMinX = minCentre.x - half - CityGeneratorConstants.RoadBaseMargin;
            float rectMaxX = maxCentre.x + half + CityGeneratorConstants.RoadBaseMargin;
            float rectMinZ = minCentre.z - half - CityGeneratorConstants.RoadBaseMargin;
            float rectMaxZ = maxCentre.z + half + CityGeneratorConstants.RoadBaseMargin;

            for (int mx = minX - 1; mx <= maxX + 1; mx++)
            {
                for (int my = minY - 1; my <= maxY + 1; my++)
                {
                    var m = new Vector2Int(mx, my);
                    if (cells.Contains(m))
                        continue;

                    Vector3 c = centreOf(m);
                    float clipMinX = rectMinX - c.x;
                    float clipMaxX = rectMaxX - c.x;
                    float clipMinZ = rectMinZ - c.z;
                    float clipMaxZ = rectMaxZ - c.z;

                    for (int q = 0; q < 3; q++)
                    {
                        float z0 = Mathf.Max(bounds[q], clipMinZ);
                        float z1 = Mathf.Min(bounds[q + 1], clipMaxZ);
                        if (z1 - z0 <= epsilon)
                            continue;

                        float z = (z0 + z1) / 2f;
                        float runFrom = 0f;
                        float runTo = 0f;
                        bool inRun = false;

                        for (int p = 0; p < 3; p++)
                        {
                            float x0 = Mathf.Max(bounds[p], clipMinX);
                            float x1 = Mathf.Min(bounds[p + 1], clipMaxX);
                            bool fill = x1 - x0 > epsilon && !IsCovered(cells, m, (x0 + x1) / 2f, z, coveredEdge);

                            if (fill)
                            {
                                if (!inRun)
                                {
                                    runFrom = x0;
                                    inRun = true;
                                }

                                runTo = x1;
                                continue;
                            }

                            if (!inRun)
                                continue;

                            yield return new BandRect(c.x + (runFrom + runTo) / 2f, c.z + z, runTo - runFrom, z1 - z0);
                            inRun = false;
                        }

                        if (inRun)
                            yield return new BandRect(c.x + (runFrom + runTo) / 2f, c.z + z, runTo - runFrom, z1 - z0);
                    }
                }
            }
        }

        /// <summary>
        /// Whether the point at (<paramref name="x"/>, <paramref name="z"/>) local to
        /// <paramref name="missing"/>'s centre already lies under the road base or perimeter
        /// sidewalk of a neighbouring real cell. Only a neighbour within one cell in each axis can
        /// reach in: two cells away is 112 m, well past the 39 m the dilation extends.
        /// </summary>
        private static bool IsCovered(HashSet<Vector2Int> cells, Vector2Int missing, float x, float z, float coveredEdge)
        {
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0)
                        continue;
                    if (!cells.Contains(missing + new Vector2Int(i, j)))
                        continue;
                    if (Reaches(x, i, coveredEdge) && Reaches(z, j, coveredEdge))
                        return true;
                }
            }

            return false;
        }

        private static Vector3 CanvasCentre(Vector2Int cell)
        {
            int canvas = CityGeneratorConstants.MaxGridSize;
            return CityGeneratorGrid.GetBlockCenter(cell.x, cell.y, canvas, canvas);
        }

        /// <summary>One axis-aligned tile of the band that wraps the city's contour.</summary>
        private readonly struct BandRect
        {
            public readonly float centerX;
            public readonly float centerZ;
            public readonly float width;
            public readonly float depth;

            public BandRect(float centerX, float centerZ, float width, float depth)
            {
                this.centerX = centerX;
                this.centerZ = centerZ;
                this.width = width;
                this.depth = depth;
            }
        }

        /// <summary>
        /// Tiles the band around the contour of <paramref name="cells"/> that lies between
        /// <paramref name="innerRadius"/> and <paramref name="outerRadius"/> of a real cell's
        /// centre (Chebyshev distance, i.e. square rings): exactly the set difference between the
        /// shape dilated by each radius, which is what makes the result gap-free *and*
        /// overlap-free -- two tiles at the same Y that covered the same square would z-fight, and
        /// a convex corner needs an L-shaped piece rather than the square a per-edge tiling gives.
        ///
        /// Worked per *missing* cell, whose own 56 m square is cut by the six coordinates where
        /// either dilation's boundary can fall (+-CellPitch/2 and +-(CellPitch - radius) for each
        /// radius) into at most 5x5 sub-rectangles, each wholly in or wholly out of the band.
        /// Sub-rectangles are merged along X so a straight run of contour stays a single tile.
        /// </summary>
        private static IEnumerable<BandRect> EnumerateBand(HashSet<Vector2Int> cells, System.Func<Vector2Int, Vector3> centreOf, float innerRadius, float outerRadius)
        {
            const float half = CityGeneratorConstants.CellPitch / 2f;
            const float epsilon = 0.001f;
            float outerEdge = CityGeneratorConstants.CellPitch - outerRadius;
            float innerEdge = CityGeneratorConstants.CellPitch - innerRadius;
            float[] bounds = { -half, -innerEdge, -outerEdge, outerEdge, innerEdge, half };

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Vector2Int cell in cells)
            {
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            var neighbours = new List<Vector2Int>(8);

            for (int mx = minX - 1; mx <= maxX + 1; mx++)
            {
                for (int my = minY - 1; my <= maxY + 1; my++)
                {
                    var m = new Vector2Int(mx, my);
                    if (cells.Contains(m))
                        continue;

                    // Only a neighbour within one cell in each axis can reach into this cell's own
                    // square with either dilation, and this cell itself is not part of the shape.
                    neighbours.Clear();
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            if ((i != 0 || j != 0) && cells.Contains(m + new Vector2Int(i, j)))
                                neighbours.Add(new Vector2Int(i, j));
                        }
                    }

                    if (neighbours.Count == 0)
                        continue;

                    Vector3 c = centreOf(m);

                    for (int q = 0; q < 5; q++)
                    {
                        float depth = bounds[q + 1] - bounds[q];
                        if (depth <= epsilon)
                            continue;

                        float z = (bounds[q] + bounds[q + 1]) / 2f;
                        int runStart = -1;

                        for (int p = 0; p <= 5; p++)
                        {
                            bool inBand = p < 5
                                && bounds[p + 1] - bounds[p] > epsilon
                                && IsInBand(neighbours, (bounds[p] + bounds[p + 1]) / 2f, z, innerEdge, outerEdge);

                            if (inBand)
                            {
                                if (runStart < 0)
                                    runStart = p;
                                continue;
                            }

                            if (runStart < 0)
                                continue;

                            float from = bounds[runStart];
                            float to = bounds[p];
                            yield return new BandRect(c.x + (from + to) / 2f, c.z + z, to - from, depth);
                            runStart = -1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether the point at (<paramref name="x"/>, <paramref name="z"/>) local to a missing
        /// cell's centre is inside the outer dilation of <paramref name="neighbours"/> but outside
        /// the inner one -- the band itself.
        /// </summary>
        private static bool IsInBand(List<Vector2Int> neighbours, float x, float z, float innerEdge, float outerEdge)
        {
            bool inOuter = false;

            foreach (Vector2Int neighbour in neighbours)
            {
                if (Reaches(x, neighbour.x, innerEdge) && Reaches(z, neighbour.y, innerEdge))
                    return false;

                inOuter |= Reaches(x, neighbour.x, outerEdge) && Reaches(z, neighbour.y, outerEdge);
            }

            return inOuter;
        }

        // Whether a neighbour one cell away in direction `step` (0 = same row/column) covers this
        // local coordinate, given the dilation reaches to `edge` from the missing cell's centre.
        private static bool Reaches(float coordinate, int step, float edge)
        {
            if (step == 0)
                return true;

            return step > 0 ? coordinate >= edge : coordinate <= -edge;
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
