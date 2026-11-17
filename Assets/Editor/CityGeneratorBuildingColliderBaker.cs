using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development-repo-only tooling: rebuilds the collider of every demo building prefab
/// (Packages/com.santiandrade.citygenerator/DefaultAssets/Prefabs/Buildings) as one BoxCollider
/// for the main body plus one for each pronounced protrusion (a balcony, a planter, a
/// canopy, a taller part of the building), all on the prefab root, instead of one box sized to the
/// whole model's bounds — which stopped a car well before it reached the wall wherever an upper
/// floor, a balcony or a cornice overhung the street.
///
/// How it works, all in the root's axes and in metres:
/// 1. Every triangle of the model is sampled densely into a voxel grid (cellSize horizontally,
///    sliceHeight vertically): a voxel is "surface" when the model passes through it.
/// 2. Each horizontal slice is flood-filled from outside; whatever the outside cannot reach is
///    solid. A wall closes the building's interior, and a balcony's floor or railing against the
///    facade closes the balcony.
/// 3. Each slice's solid footprint is split greedily into rectangles at least minFeatureGround
///    (street band) or minFeatureUpper (above it) wide on both axes: largest uncovered rectangle
///    first, then grown over any row or column at least growFill solid whose cells are mostly not
///    covered yet. That swallows chamfered corners without letting a balcony's rectangle spread
///    over the body, and leaves window frames and trims above the street without boxes.
/// 4. Rectangles of consecutive slices whose edges are within matchTolerance are chained into one
///    box, across gaps of up to bridgeHeight (a cornice band, a hollow floor slab). Below
///    groundBandTop (the street level a vehicle can touch) a chain takes the union of its
///    rectangles and only exactly contained boxes are dropped, so plinths, steps, planters, door
///    frames and pillars are described exactly; above it the most frequent rectangle wins and boxes
///    another one contains within the tolerance are dropped, so a cornice band doesn't fatten a
///    whole floor's box. The street band is never chained with the floors above it.
/// 5. Select keeps the main body (a full-height box) plus one box for each pronounced protrusion,
///    leaving small ones out; see its comment and Protrudes for the rules. The detailed pieces of
///    steps 3-4 are only its candidates.
/// 6. Each box is shrunk to the sampled points it actually contains, which removes the voxel
///    quantisation.
///
/// Lives outside Packages/com.santiandrade.citygenerator/ on purpose, like the other tools in
/// this folder: it rewrites the package's own demo prefabs, which only makes sense in this
/// development repo.
/// </summary>
internal static class CityGeneratorBuildingColliderBaker
{
    private const string BuildingsFolder = "Packages/com.santiandrade.citygenerator/DefaultAssets/Prefabs/Buildings";

    internal struct Settings
    {
        public float cellSize;
        public float sliceHeight;
        public float groundBandTop;
        public float minFeatureGround;
        public float minFeatureUpper;
        public float growFill;
        public float matchTolerance;
        public float bridgeHeight;
        public int maxRectsPerSlice;
        public float minProtrusion;
        public float minExtraVolume;
        public float minProfileArea;
        public float groundEmptyWeight;
        public float upperEmptyWeight;
    }

    internal static Settings DefaultSettings => new Settings
    {
        cellSize = 0.1f,
        sliceHeight = 0.1f,
        groundBandTop = 2.5f,
        minFeatureGround = 0.25f,
        minFeatureUpper = 0.5f,
        growFill = 0.85f,
        matchTolerance = 0.4f,
        bridgeHeight = 1.0f,
        maxRectsPerSlice = 12,
        minProtrusion = 0.6f,
        minExtraVolume = 0.5f,
        minProfileArea = 0.7f,
        groundEmptyWeight = 3f,
        upperEmptyWeight = 2f,
    };

    [MenuItem("Tools/City Generator/Bake Building Colliders")]
    private static void BakeAllMenuItem()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "City Generator - Bake Building Colliders",
            "This replaces the BoxColliders on every prefab in " + BuildingsFolder + " with one box for the main body plus one for each pronounced protrusion.",
            "Bake",
            "Cancel");
        if (confirmed)
            BakeAll();
    }

    /// <summary>Entry point for batch mode (-executeMethod CityGeneratorBuildingColliderBaker.BakeAll).</summary>
    internal static void BakeAll()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { BuildingsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int count = BakePrefab(path);
            Debug.Log($"[CityGeneratorBuildingColliderBaker] {path}: {count} boxes");
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>Replaces the root BoxColliders of one prefab, reusing the existing components (and so
    /// their file IDs) first. Returns the number of boxes written.</summary>
    internal static int BakePrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            List<Bounds> boxes = ComputeLocalBoxes(root, DefaultSettings);
            BoxCollider[] existing = root.GetComponents<BoxCollider>();
            for (int i = 0; i < boxes.Count; i++)
            {
                BoxCollider box = i < existing.Length ? existing[i] : root.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.center = boxes[i].center;
                box.size = boxes[i].size;
            }

            for (int i = boxes.Count; i < existing.Length; i++)
                Object.DestroyImmediate(existing[i]);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return boxes.Count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Computes the boxes for a building root, as BoxCollider centre/size in the root's
    /// local space (the root's scale must be uniform and positive).</summary>
    internal static List<Bounds> ComputeLocalBoxes(GameObject root, Settings settings)
    {
        List<Bounds> metricBoxes = ComputeMetricBoxes(root, settings);
        float scale = root.transform.lossyScale.x;
        var local = new List<Bounds>(metricBoxes.Count);
        foreach (Bounds box in metricBoxes)
            local.Add(new Bounds(box.center / scale, box.size / scale));
        return local;
    }

    /// <summary>Same boxes in metres, in the root's axes with its pivot at the origin.</summary>
    internal static List<Bounds> ComputeMetricBoxes(GameObject root, Settings settings)
    {
        List<Vector3[]> triangles = CollectTriangles(root);
        if (triangles.Count == 0)
            return new List<Bounds>();

        var grid = new VoxelGrid(triangles, settings);
        grid.Rasterize(triangles);

        int minCellsGround = Mathf.Max(1, Mathf.RoundToInt(settings.minFeatureGround / settings.cellSize));
        int minCellsUpper = Mathf.Max(1, Mathf.RoundToInt(settings.minFeatureUpper / settings.cellSize));
        int toleranceCells = Mathf.Max(0, Mathf.RoundToInt(settings.matchTolerance / settings.cellSize));
        int bridgeSlices = Mathf.Max(0, Mathf.RoundToInt(settings.bridgeHeight / settings.sliceHeight));

        var solids = new bool[grid.ny][];
        int groundSlices = 0;
        var open = new List<Chain>();
        var closed = new List<Chain>();
        bool previousGround = true;
        for (int y = 0; y < grid.ny; y++)
        {
            float sliceCentre = grid.origin.y + (y + 0.5f) * settings.sliceHeight;
            bool ground = sliceCentre < settings.groundBandTop;
            if (ground != previousGround)
            {
                closed.AddRange(open);
                open.Clear();
                previousGround = ground;
            }

            bool[] solid = grid.SolidSlice(y);
            solids[y] = solid;
            if (ground)
                groundSlices = y + 1;
            int minCells = ground ? minCellsGround : minCellsUpper;
            List<RectInt> rects = Decompose(solid, grid.nx, grid.nz, minCells, settings.growFill, settings.maxRectsPerSlice);

            var extended = new HashSet<Chain>();
            foreach (RectInt rect in rects)
            {
                Chain best = null;
                int bestDiff = int.MaxValue;
                foreach (Chain chain in open)
                {
                    if (extended.Contains(chain))
                        continue;
                    int diff = MaxEdgeDifference(chain.last, rect);
                    if (diff <= toleranceCells && diff < bestDiff)
                    {
                        best = chain;
                        bestDiff = diff;
                    }
                }

                if (best == null)
                {
                    best = new Chain { y0 = y, ground = ground };
                    open.Add(best);
                }

                best.y1 = y;
                best.last = rect;
                best.rects.Add(rect);
                extended.Add(best);
            }

            for (int i = open.Count - 1; i >= 0; i--)
            {
                // A chain survives a few slices without a match, so a cornice band or a row of
                // window frames doesn't cut a floor's box in two.
                if (!extended.Contains(open[i]) && y - open[i].y1 > bridgeSlices)
                {
                    closed.Add(open[i]);
                    open.RemoveAt(i);
                }
            }
        }

        closed.AddRange(open);
        foreach (Chain chain in closed)
            chain.rect = chain.ground ? Union(chain.rects) : Mode(chain.rects);

        var pieces = new List<Piece>(closed.Count);
        foreach (Chain chain in closed)
        {
            // Above the street, a slab thinner than half a feature is kept only when it's wide (a
            // canopy), never as a lone trim band.
            int height = chain.y1 - chain.y0 + 1;
            if (!chain.ground && height < Mathf.Max(1, minCellsUpper / 2) &&
                Mathf.Min(chain.rect.width, chain.rect.height) < 4 * minCellsUpper)
                continue;
            pieces.Add(new Piece { ground = chain.ground, rect = chain.rect, y0 = chain.y0, y1 = chain.y1 });
        }

        MergeAndPrune(pieces, toleranceCells, bridgeSlices);

        var occupancy = new Occupancy(solids, grid.nx, grid.nz, groundSlices, settings);
        List<Piece> selected = Select(pieces, occupancy, grid.ny, settings);

        var boxes = new List<Bounds>(selected.Count);
        foreach (Piece piece in selected)
            boxes.Add(grid.ToBounds(piece.rect, piece.y0, piece.y1));

        grid.ShrinkToContents(boxes, triangles);
        boxes.Sort((a, b) => (b.size.x * b.size.y * b.size.z).CompareTo(a.size.x * a.size.y * a.size.z));
        return boxes;
    }

    private sealed class Chain
    {
        public int y0;
        public int y1;
        public bool ground;
        public RectInt last;
        public RectInt rect;
        public readonly List<RectInt> rects = new List<RectInt>();
    }

    private static List<Vector3[]> CollectTriangles(GameObject root)
    {
        // Metres in the root's axes: undo the root's own rotation/translation, keep its scale.
        Matrix4x4 rootFrame = Matrix4x4.TRS(root.transform.position, root.transform.rotation, Vector3.one).inverse;
        var triangles = new List<Vector3[]>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
                continue;

            Matrix4x4 toRoot = rootFrame * filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                triangles.Add(new[]
                {
                    toRoot.MultiplyPoint3x4(vertices[indices[i]]),
                    toRoot.MultiplyPoint3x4(vertices[indices[i + 1]]),
                    toRoot.MultiplyPoint3x4(vertices[indices[i + 2]]),
                });
            }
        }

        return triangles;
    }

    private static int MaxEdgeDifference(RectInt a, RectInt b)
    {
        return Mathf.Max(
            Mathf.Max(Mathf.Abs(a.xMin - b.xMin), Mathf.Abs(a.xMax - b.xMax)),
            Mathf.Max(Mathf.Abs(a.yMin - b.yMin), Mathf.Abs(a.yMax - b.yMax)));
    }

    private static RectInt Union(List<RectInt> rects)
    {
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        foreach (RectInt r in rects)
        {
            xMin = Mathf.Min(xMin, r.xMin);
            yMin = Mathf.Min(yMin, r.yMin);
            xMax = Mathf.Max(xMax, r.xMax);
            yMax = Mathf.Max(yMax, r.yMax);
        }

        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    private static RectInt Mode(List<RectInt> rects)
    {
        var counts = new Dictionary<RectInt, int>();
        foreach (RectInt r in rects)
            counts[r] = counts.TryGetValue(r, out int c) ? c + 1 : 1;

        RectInt best = rects[0];
        int bestCount = 0;
        foreach (KeyValuePair<RectInt, int> pair in counts)
        {
            int area = pair.Key.width * pair.Key.height;
            if (pair.Value > bestCount || (pair.Value == bestCount && area > best.width * best.height))
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }

        return best;
    }

    /// <summary>Keeps one box for the main body plus one for each pronounced protrusion, at any
    /// height and however many there are (ten balconies stacked up a facade get ten boxes). The main
    /// body is the full-height candidate (every piece's footprint, from the ground up to every
    /// piece's top) that covers the most solid volume minus the empty volume it would add, empty
    /// space at street level weighing groundEmptyWeight and above it upperEmptyWeight. Every other
    /// piece is a candidate as is and extended down to the ground (a canopy on pillars); the extras
    /// are picked greedily, best score on top of the boxes already chosen first, as long as the
    /// candidate is a pronounced protrusion (see Protrudes) and adds minExtraVolume of solid volume:
    /// small protrusions (drain pipes, window sills, thin pillars, trims, cornices, the ledges of a
    /// skyscraper facade) are not worth a box.</summary>
    private static List<Piece> Select(List<Piece> pieces, Occupancy occupancy, int ny, Settings settings)
    {
        var selected = new List<Piece>();
        if (pieces.Count == 0)
            return selected;

        var tops = new HashSet<int> { ny - 1 };
        foreach (Piece piece in pieces)
            tops.Add(piece.y1);

        Piece main = null;
        double mainScore = double.MinValue;
        foreach (Piece piece in pieces)
        {
            foreach (int top in tops)
            {
                double score = occupancy.Score(piece.rect, 0, top);
                if (score > mainScore)
                {
                    mainScore = score;
                    main = new Piece { ground = false, rect = piece.rect, y0 = 0, y1 = top };
                }
            }
        }

        main.rect = occupancy.Refine(main.rect, main.y0, main.y1);
        selected.Add(main);

        var candidates = new List<Piece>();
        foreach (Piece piece in pieces)
        {
            candidates.Add(piece);
            if (piece.y0 > 0)
                candidates.Add(new Piece { ground = piece.ground, rect = piece.rect, y0 = 0, y1 = piece.y1 });
        }

        long minExtraVoxels = (long)(settings.minExtraVolume / (settings.cellSize * settings.cellSize * settings.sliceHeight));
        while (true)
        {
            Piece best = null;
            double bestScore = 0;
            foreach (Piece candidate in candidates)
            {
                if (selected.Contains(candidate) || !Protrudes(main, candidate, settings))
                    continue;
                double score = occupancy.MarginalScore(candidate, selected, out long newSolid);
                if (newSolid >= minExtraVoxels && score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null)
                break;
            selected.Add(best);
        }

        return selected;
    }

    /// <summary>Whether a candidate is a pronounced protrusion of the main body: its footprint is at
    /// least minProtrusion across, it sticks out by minProtrusion and its profile (how far it sticks out times its thickness) is at least
    /// minProfileArea. Sideways the thickness is the candidate's height (a balcony 1.4 m deep and
    /// 1.5 m tall: 2.1 m²; a ledge 0.8 m deep and 0.5 m tall: 0.4 m²); upwards (a taller part of the
    /// building, a rooftop structure) it is the candidate's narrower horizontal side. Where it sits
    /// doesn't matter, only its own size.</summary>
    private static bool Protrudes(Piece main, Piece candidate, Settings settings)
    {
        // Thinner than minProtrusion across: a drain pipe, a pillar, a post.
        float narrower = Mathf.Min(candidate.rect.width, candidate.rect.height) * settings.cellSize;
        if (narrower < settings.minProtrusion)
            return false;

        float sideways = Protrusion(main.rect, candidate.rect) * settings.cellSize;
        float height = (candidate.y1 - candidate.y0 + 1) * settings.sliceHeight;
        if (sideways >= settings.minProtrusion && sideways * height >= settings.minProfileArea)
            return true;

        float upwards = (candidate.y1 - main.y1) * settings.sliceHeight;
        return upwards >= settings.minProtrusion && upwards * narrower >= settings.minProfileArea;
    }

    /// <summary>How far, in cells, a rectangle sticks out of the main body's footprint.</summary>
    private static int Protrusion(RectInt main, RectInt rect)
    {
        return Mathf.Max(
            Mathf.Max(main.xMin - rect.xMin, rect.xMax - main.xMax),
            Mathf.Max(main.yMin - rect.yMin, rect.yMax - main.yMax));
    }

    /// <summary>Solid voxels per slice with 2D prefix sums, to score candidate boxes.</summary>
    private sealed class Occupancy
    {
        private readonly bool[][] solids;
        private readonly int[][] prefix;
        private readonly int nx;
        private readonly int stride;
        private readonly int groundSlices;
        private readonly float groundEmptyWeight;
        private readonly float upperEmptyWeight;

        public Occupancy(bool[][] solids, int nx, int nz, int groundSlices, Settings settings)
        {
            this.solids = solids;
            this.nx = nx;
            this.groundSlices = groundSlices;
            groundEmptyWeight = settings.groundEmptyWeight;
            upperEmptyWeight = settings.upperEmptyWeight;
            stride = nx + 1;
            prefix = new int[solids.Length][];
            for (int y = 0; y < solids.Length; y++)
            {
                var p = new int[(nx + 1) * (nz + 1)];
                for (int z = 0; z < nz; z++)
                {
                    int row = 0;
                    for (int x = 0; x < nx; x++)
                    {
                        if (solids[y][z * nx + x])
                            row++;
                        p[(z + 1) * stride + x + 1] = p[z * stride + x + 1] + row;
                    }
                }

                prefix[y] = p;
            }
        }

        private float EmptyWeight(int y) => y < groundSlices ? groundEmptyWeight : upperEmptyWeight;

        /// <summary>Moves each edge of the rectangle one cell at a time while that improves the score.</summary>
        public RectInt Refine(RectInt rect, int y0, int y1)
        {
            double best = Score(rect, y0, y1);
            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int edge = 0; edge < 4; edge++)
                {
                    for (int step = -1; step <= 1; step += 2)
                    {
                        RectInt moved = rect;
                        switch (edge)
                        {
                            case 0: moved.xMin += step; break;
                            case 1: moved.xMax += step; break;
                            case 2: moved.yMin += step; break;
                            default: moved.yMax += step; break;
                        }

                        if (moved.width < 1 || moved.height < 1 || moved.xMin < 0 || moved.yMin < 0 ||
                            moved.xMax > nx || moved.yMax > (prefix[0].Length / stride) - 1)
                            continue;
                        double score = Score(moved, y0, y1);
                        if (score > best)
                        {
                            best = score;
                            rect = moved;
                            improved = true;
                        }
                    }
                }
            }

            return rect;
        }

        public double Score(RectInt rect, int y0, int y1)
        {
            double score = 0;
            int area = rect.width * rect.height;
            for (int y = y0; y <= y1; y++)
            {
                int[] p = prefix[y];
                int solid = p[rect.yMax * stride + rect.xMax] - p[rect.yMin * stride + rect.xMax]
                            - p[rect.yMax * stride + rect.xMin] + p[rect.yMin * stride + rect.xMin];
                score += solid - EmptyWeight(y) * (area - solid);
            }

            return score;
        }

        public double MarginalScore(Piece candidate, List<Piece> chosen, out long newSolid)
        {
            double score = 0;
            newSolid = 0;
            for (int y = candidate.y0; y <= candidate.y1; y++)
            {
                bool[] slice = solids[y];
                float weight = EmptyWeight(y);
                for (int z = candidate.rect.yMin; z < candidate.rect.yMax; z++)
                {
                    for (int x = candidate.rect.xMin; x < candidate.rect.xMax; x++)
                    {
                        if (Covered(chosen, x, y, z))
                            continue;
                        if (slice[z * nx + x])
                        {
                            newSolid++;
                            score += 1;
                        }
                        else
                        {
                            score -= weight;
                        }
                    }
                }
            }

            return score;
        }

        private static bool Covered(List<Piece> chosen, int x, int y, int z)
        {
            foreach (Piece piece in chosen)
            {
                if (y >= piece.y0 && y <= piece.y1 && x >= piece.rect.xMin && x < piece.rect.xMax &&
                    z >= piece.rect.yMin && z < piece.rect.yMax)
                    return true;
            }

            return false;
        }
    }

    private sealed class Piece
    {
        public bool ground;
        public RectInt rect;
        public int y0;
        public int y1;

        public long Volume => (long)rect.width * rect.height * (y1 - y0 + 1);
    }

    /// <summary>Repeatedly merges vertically stacked pieces of the same band whose footprints match
    /// within the tolerance (street band: union; above: the taller piece's footprint), and drops any
    /// piece another one already contains (above the street band, within the tolerance).</summary>
    private static void MergeAndPrune(List<Piece> pieces, int toleranceCells, int bridgeSlices)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < pieces.Count && !changed; i++)
            {
                for (int j = 0; j < pieces.Count && !changed; j++)
                {
                    if (i == j)
                        continue;
                    Piece a = pieces[i];
                    Piece b = pieces[j];
                    if (a.ground != b.ground)
                        continue;

                    // In the street band only exact containment prunes: a door frame or a drain pipe
                    // sticking out of the wall keeps its own box there.
                    if (Contains(a, b, a.ground ? 0 : toleranceCells))
                    {
                        pieces.RemoveAt(j);
                        changed = true;
                        continue;
                    }

                    bool stacked = b.y0 <= a.y1 + bridgeSlices + 1 && b.y1 >= a.y0 - bridgeSlices - 1;
                    if (stacked && MaxEdgeDifference(a.rect, b.rect) <= toleranceCells)
                    {
                        RectInt rect = a.ground
                            ? Union(new List<RectInt> { a.rect, b.rect })
                            : (a.y1 - a.y0 >= b.y1 - b.y0 ? a.rect : b.rect);
                        pieces[i] = new Piece { ground = a.ground, rect = rect, y0 = Mathf.Min(a.y0, b.y0), y1 = Mathf.Max(a.y1, b.y1) };
                        pieces.RemoveAt(j);
                        changed = true;
                    }
                }
            }
        }
    }

    private static bool Contains(Piece outer, Piece inner, int toleranceCells)
    {
        if (outer.Volume < inner.Volume)
            return false;
        return outer.rect.xMin - toleranceCells <= inner.rect.xMin && outer.rect.xMax + toleranceCells >= inner.rect.xMax &&
               outer.rect.yMin - toleranceCells <= inner.rect.yMin && outer.rect.yMax + toleranceCells >= inner.rect.yMax &&
               outer.y0 - toleranceCells <= inner.y0 && outer.y1 + toleranceCells >= inner.y1;
    }

    /// <summary>Greedy rectangle cover of a solid footprint: the largest uncovered rectangle at least
    /// minCells on both sides, grown over any row or column at least growFill solid (which swallows
    /// chamfered corners), until none is left.</summary>
    private static List<RectInt> Decompose(bool[] solid, int nx, int nz, int minCells, float growFill, int maxRects)
    {
        var rects = new List<RectInt>();
        var uncovered = (bool[])solid.Clone();
        for (int iteration = 0; iteration < maxRects; iteration++)
        {
            RectInt best = LargestRect(uncovered, nx, nz, minCells);
            if (best.width == 0)
                break;

            RectInt grown = Grow(best, solid, uncovered, nx, nz, growFill);
            for (int z = grown.yMin; z < grown.yMax; z++)
                for (int x = grown.xMin; x < grown.xMax; x++)
                    uncovered[z * nx + x] = false;
            rects.Add(grown);
        }

        return rects;
    }

    private static RectInt LargestRect(bool[] cells, int nx, int nz, int minCells)
    {
        var heights = new int[nx + 1];
        var stack = new Stack<int>();
        RectInt best = default;
        int bestArea = 0;
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
                heights[x] = cells[z * nx + x] ? heights[x] + 1 : 0;
            heights[nx] = 0;

            stack.Clear();
            for (int x = 0; x <= nx; x++)
            {
                while (stack.Count > 0 && heights[stack.Peek()] >= heights[x])
                {
                    int h = heights[stack.Pop()];
                    int left = stack.Count > 0 ? stack.Peek() + 1 : 0;
                    int w = x - left;
                    if (h >= minCells && w >= minCells && h * w > bestArea)
                    {
                        bestArea = h * w;
                        best = new RectInt(left, z - h + 1, w, h);
                    }
                }

                stack.Push(x);
            }
        }

        return best;
    }

    private static RectInt Grow(RectInt rect, bool[] solid, bool[] uncovered, int nx, int nz, float growFill)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            if (rect.xMin > 0 && CanTake(ColumnCells(nx, rect.xMin - 1, rect.yMin, rect.yMax), solid, uncovered, growFill))
            {
                rect.xMin--;
                changed = true;
            }

            if (rect.xMax < nx && CanTake(ColumnCells(nx, rect.xMax, rect.yMin, rect.yMax), solid, uncovered, growFill))
            {
                rect.xMax++;
                changed = true;
            }

            if (rect.yMin > 0 && CanTake(RowCells(nx, rect.yMin - 1, rect.xMin, rect.xMax), solid, uncovered, growFill))
            {
                rect.yMin--;
                changed = true;
            }

            if (rect.yMax < nz && CanTake(RowCells(nx, rect.yMax, rect.xMin, rect.xMax), solid, uncovered, growFill))
            {
                rect.yMax++;
                changed = true;
            }
        }

        return rect;
    }

    /// <summary>A strip joins the rectangle when at least growFill of it is solid and most of its
    /// solid cells aren't covered yet, so a rectangle never spreads back over a part of the
    /// footprint another one already covers (a balcony's box stays on the balcony).</summary>
    private static bool CanTake(IEnumerable<int> cells, bool[] solid, bool[] uncovered, float growFill)
    {
        int total = 0, solidCount = 0, uncoveredCount = 0;
        foreach (int i in cells)
        {
            total++;
            if (!solid[i])
                continue;
            solidCount++;
            if (uncovered[i])
                uncoveredCount++;
        }

        return total > 0 && solidCount >= growFill * total && uncoveredCount * 2 > solidCount;
    }

    private static IEnumerable<int> ColumnCells(int nx, int x, int zMin, int zMax)
    {
        for (int z = zMin; z < zMax; z++)
            yield return z * nx + x;
    }

    private static IEnumerable<int> RowCells(int nx, int z, int xMin, int xMax)
    {
        for (int x = xMin; x < xMax; x++)
            yield return z * nx + x;
    }

    private sealed class VoxelGrid
    {
        private const int Padding = 2;

        public readonly Vector3 origin;
        public readonly int nx;
        public readonly int ny;
        public readonly int nz;
        private readonly float cell;
        private readonly float slice;
        private readonly float meshMinY;
        private readonly float meshMaxY;
        private readonly bool[][] surface;

        public VoxelGrid(List<Vector3[]> triangles, Settings settings)
        {
            cell = settings.cellSize;
            slice = settings.sliceHeight;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (Vector3[] t in triangles)
            {
                foreach (Vector3 p in t)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }

            meshMinY = min.y;
            meshMaxY = max.y;
            origin = new Vector3(min.x - Padding * cell, min.y, min.z - Padding * cell);
            nx = Mathf.CeilToInt((max.x - min.x) / cell) + 2 * Padding + 1;
            nz = Mathf.CeilToInt((max.z - min.z) / cell) + 2 * Padding + 1;
            ny = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / slice));
            surface = new bool[ny][];
            for (int y = 0; y < ny; y++)
                surface[y] = new bool[nx * nz];
        }

        public void Rasterize(List<Vector3[]> triangles)
        {
            foreach (Vector3[] t in triangles)
                Sample(t, p =>
                {
                    int x = Mathf.FloorToInt((p.x - origin.x) / cell);
                    int z = Mathf.FloorToInt((p.z - origin.z) / cell);
                    int y = Mathf.Clamp(Mathf.FloorToInt((p.y - origin.y) / slice), 0, ny - 1);
                    if (x >= 0 && x < nx && z >= 0 && z < nz)
                        surface[y][z * nx + x] = true;
                });
        }

        private void Sample(Vector3[] t, System.Action<Vector3> visit)
        {
            float longest = Mathf.Max((t[1] - t[0]).magnitude, Mathf.Max((t[2] - t[1]).magnitude, (t[0] - t[2]).magnitude));
            float spacing = 0.5f * Mathf.Min(cell, slice);
            int n = Mathf.Max(1, Mathf.CeilToInt(longest / spacing));
            Vector3 ab = t[1] - t[0];
            Vector3 ac = t[2] - t[0];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                    visit(t[0] + ab * (i / (float)n) + ac * (j / (float)n));
        }

        /// <summary>Cells of slice y the outside can't reach through empty cells (4-connected).</summary>
        public bool[] SolidSlice(int y)
        {
            bool[] walls = surface[y];
            var outside = new bool[nx * nz];
            var queue = new Queue<int>();
            outside[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % nx;
                int z = index / nx;
                TryVisit(x - 1, z);
                TryVisit(x + 1, z);
                TryVisit(x, z - 1);
                TryVisit(x, z + 1);
            }

            var solid = new bool[nx * nz];
            for (int i = 0; i < solid.Length; i++)
                solid[i] = !outside[i];
            return solid;

            void TryVisit(int x, int z)
            {
                if (x < 0 || z < 0 || x >= nx || z >= nz)
                    return;
                int i = z * nx + x;
                if (outside[i] || walls[i])
                    return;
                outside[i] = true;
                queue.Enqueue(i);
            }
        }

        public Bounds ToBounds(RectInt rect, int y0, int y1)
        {
            var min = new Vector3(origin.x + rect.xMin * cell, Mathf.Max(meshMinY, origin.y + y0 * slice), origin.z + rect.yMin * cell);
            var max = new Vector3(origin.x + rect.xMax * cell, Mathf.Min(meshMaxY, origin.y + (y1 + 1) * slice), origin.z + rect.yMax * cell);
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Shrinks each box horizontally to the sampled surface points inside it.</summary>
        public void ShrinkToContents(List<Bounds> boxes, List<Vector3[]> triangles)
        {
            var mins = new Vector3[boxes.Count];
            var maxs = new Vector3[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                mins[i] = Vector3.positiveInfinity;
                maxs[i] = Vector3.negativeInfinity;
            }

            foreach (Vector3[] t in triangles)
                Sample(t, p =>
                {
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        Bounds b = boxes[i];
                        if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y || p.z < b.min.z || p.z > b.max.z)
                            continue;
                        mins[i] = Vector3.Min(mins[i], p);
                        maxs[i] = Vector3.Max(maxs[i], p);
                    }
                });

            for (int i = 0; i < boxes.Count; i++)
            {
                if (mins[i].x > maxs[i].x)
                    continue;
                Bounds b = boxes[i];
                var min = new Vector3(Mathf.Max(b.min.x, mins[i].x), b.min.y, Mathf.Max(b.min.z, mins[i].z));
                var max = new Vector3(Mathf.Min(b.max.x, maxs[i].x), b.max.y, Mathf.Min(b.max.z, maxs[i].z));
                b.SetMinMax(min, max);
                boxes[i] = b;
            }
        }
    }
}
