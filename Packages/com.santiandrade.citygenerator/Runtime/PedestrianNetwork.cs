using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    public enum PedestrianNodeKind { Ring, Curb, Crossing, Interior }

    /// <summary>An undirected node in the pedestrian graph.</summary>
    public struct PedestrianNode
    {
        public Vector3 Position;
        public PedestrianNodeKind Kind;

        /// <summary>Only set when Kind == Crossing.</summary>
        public TrafficLightIntersection Intersection;

        /// <summary>Only meaningful when Kind == Crossing: true if the traffic crossed here flows along X.</summary>
        public bool CrossingAxisIsX;

        public bool Blocked;
        public List<int> Neighbours;
    }

    /// <summary>
    /// The city's pedestrian network. Mirrors <see cref="TrafficNetwork"/> structurally (its own
    /// graph, built from the same street axes, matched against the scene's traffic lights) but is
    /// undirected: a pedestrian can walk either way along every edge.
    ///
    /// Every block gets an 8-node ring (4 corners + 4 side midpoints) sitting in the gap between
    /// building slots and street furniture. At every interior intersection (the same one
    /// TrafficLightIntersection/zebra crossings occupy), each of the 4 arms adds a curb -> crossing
    /// -> curb chain linking two ring corners across the street, with the crossing node aligned to
    /// the same TrafficLightIntersection matched by <see cref="TrafficNetwork"/> so
    /// <see cref="CanCross"/> reads the actual light state.
    ///
    /// Every non-plaza block without a full-block Custom Place also gets a 5-node Interior cross
    /// (centre + 4 arms) linking that block's own 4 Ring midpoints -- see
    /// <see cref="BuildInteriorCross"/>. Plaza blocks and blocks with a full-block Custom Place get
    /// neither: pedestrians stay confined to the ring around them.
    ///
    /// The city's outer contour ends in sidewalk, not asphalt, so it gets a walkway of its own --
    /// see <see cref="BuildBorderWalkway"/> -- reached from the blocks by the outward crosswalk at
    /// every border T-intersection.
    /// </summary>
    public class PedestrianNetwork : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Street axis coordinates along X, in ascending order. Shared with TrafficNetwork.")]
        [SerializeField] private float[] axesX = { -84f, -28f, 28f, 84f };

        [Tooltip("Street axis coordinates along Z, in ascending order. Shared with TrafficNetwork.")]
        [SerializeField] private float[] axesZ = { -84f, -28f, 28f, 84f };

        // Own copies of the layout geometry (not read from CityGeneratorConstants: that class is
        // Editor-only/internal, and every other Runtime script in the tool already keeps its own
        // copy of the numbers it needs rather than reaching into the Editor assembly).
        [SerializeField] private float streetHalfWidth = 5f;

        // BlockHalfSize (23) - PedestrianRingInset (3.5): the ring sits in the gap between the
        // building slot edge (~18 m) and street furniture (StreetEdgeInset ring at 21 m).
        [SerializeField] private float ringRadius = 19.5f;

        // Matches TrafficNetwork/CityGeneratorConstants' ZebraArmOffset (StreetWidth/2 + lane
        // offset) exactly, so the crossing node's fixed lateral position lines up with the
        // already-painted zebra stripe instead of the ring corner's own diagonal offset.
        [SerializeField] private float crossingArmOffset = 7.6f;

        [Header("Perimeter")]
        [Tooltip("Distance from an out-of-city cell's centre to the centreline of the perimeter sidewalk walkway between it and its real neighbour: CityGeneratorConstants' CellPitch/2 - RoadBaseMargin + PerimeterSidewalkWidth/2.")]
        [SerializeField] private float perimeterWalkOffset = 20f;

        [Tooltip("How far from a crosswalk's outward curb a perimeter walkway node still counts as the sidewalk that crosswalk lands on. Only ever has to reach the PerimeterSidewalkWidth/2 step between the curb line and the walkway centreline.")]
        [SerializeField] private float perimeterLinkRadius = 6f;

        [Header("Interior")]
        [Tooltip("Flattened [bi, bj] -> flag (index = bi * blocksZ + bj), set by CityGeneratorPedestrianBuilder.AddNetworkComponent from BlockCell.isPlaza. A plaza block gets no Interior cross, same as a full-block Custom Place -- Runtime-only bools: Build() must not know about BlockCell (an Editor-only type).")]
        [SerializeField] private bool[] blockIsPlaza;

        [Tooltip("Flattened [bi, bj] -> flag (index = bi * blocksZ + bj), set by CityGeneratorPedestrianBuilder.AddNetworkComponent from reservedSlots (slot == -1, i.e. a full-block Custom Place).")]
        [SerializeField] private bool[] blockIsFullyReserved;

        [Header("Node datums")]
        [Tooltip("Y of every node except a Crossing node's road midpoint (matches GroundDatumY).")]
        [SerializeField] private float sidewalkY = 0.18f;

        [Tooltip("Y of a Crossing node's road midpoint. The agent has no raycast, so it interpolates between this and sidewalkY with MoveTowards while walking a crosswalk arm.")]
        [SerializeField] private float roadY = 0f;

        [Header("References")]
        [Tooltip("Looked up in the scene if left empty, same fallback CarAgent uses for its own network reference.")]
        [SerializeField] private TrafficNetwork trafficNetwork;

        [Tooltip("This network's PedestrianManager, on the same GameObject. Set by CityGeneratorPedestrianBuilder.AddManagerComponent. PedestrianAgent resolves its manager through this reference instead of a global static Instance, so multiple cities/networks in the same scene never share (or fight over) a single manager.")]
        [SerializeField] private PedestrianManager manager;

        public PedestrianManager Manager => manager;

        [Tooltip("This network's PedestrianRoadProximityGrid, on the same GameObject as Manager. Set by CityGeneratorPedestrianBuilder.AddManagerComponent. CarAgent queries it for nearby pedestrians instead of SphereCasting once the pedestrian count justifies it.")]
        [SerializeField] private PedestrianRoadProximityGrid roadProximity;

        public PedestrianRoadProximityGrid RoadProximity => roadProximity;

        [Header("Obstacle pruning")]
        [Tooltip("Layers Physics.CheckSphere treats as obstacles. Set by CityGeneratorPedestrianBuilder to exclude the Pedestrian layer: without that, a pedestrian standing right on its own spawn node gets detected by this very check the moment PedestrianNetwork.Awake() rebuilds the graph in Play, wrongly marking that node (and any neighbour whose only route ran through it) Blocked.")]
        [SerializeField] private LayerMask obstacleMask = ~0;

        [Tooltip("Sample radius for the per-node Physics.CheckSphere. Small and point-like: just enough to catch an overlapping collider without two neighbouring ring nodes ever seeing each other's obstacle.")]
        [SerializeField] private float pruneCheckRadius = 0.3f;

        [Tooltip("Height above the node at which the obstacle sphere is sampled: above sidewalk level, so the ground itself never counts as an obstacle.")]
        [SerializeField] private float pruneCheckHeight = 1f;

        [Header("Debugging")]
        [SerializeField] private bool drawGraph = true;

        // Own copies of the geometry needed by BuildFromBlockCells, same reasoning as the comment
        // above (CityGeneratorConstants is Editor-only) and matching TrafficNetwork's equivalent copies.
        private const float CellPitch = 56f;
        private const int MaxGridSize = 10;

        // Set by BuildFromBlockCells; gates Build()'s block loop so the rectangular
        // SetAxes/Build() path (useCustomShape == false) is completely unaffected.
        // Must be [SerializeField] (with the cell sets mirrored into plain serializable lists):
        // a plain private field is wiped back to its default the moment a domain reload/scene
        // reload runs Awake() again, silently falling back to the rectangular rule -- see
        // TrafficNetwork's identical fix for the matching "cars driving over unbuilt ground" bug.
        [SerializeField] private bool useCustomShape;
        [SerializeField] private List<Vector2Int> customBlockCellsList = new List<Vector2Int>();
        [SerializeField] private List<Vector2Int> customPlazaCellsList = new List<Vector2Int>();
        [SerializeField] private List<Vector2Int> customFullyReservedCellsList = new List<Vector2Int>();
        private HashSet<Vector2Int> customBlockCells;
        private HashSet<Vector2Int> customPlazaCells;
        private HashSet<Vector2Int> customFullyReservedCells;

        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        private readonly List<PedestrianNode> nodes = new();

        // Ring nodes on the perimeter sidewalk band, rebuilt by BuildBorderWalkway on every
        // Build(). Kept apart from `nodes` only so BuildCrossings can resolve the one a border
        // crosswalk lands on without scanning the whole graph.
        private readonly List<int> borderNodes = new();

        // BFS scratch buffers: sized to nodes.Count once per Build()/AddNode() batch, then reused
        // by every FindPath call without allocating — Unity's single-threaded main loop makes one
        // shared set safe across every agent that calls FindPath in turn.
        private int[] bfsQueue;
        private bool[] bfsVisited;

        // Connected components (item 9): parallel to `nodes`, recomputed every Build() by a flood
        // fill over the just-built edges. Lets PlanNewDestination filter candidate destinations to
        // the origin's own component before ever attempting a route, instead of discovering
        // unreachability only after a failed FindPath -- the common case on a 1xN/Nx1 grid, whose
        // blocks have no interior intersections to link their rings together (see CLAUDE.md).
        private int[] nodeComponent;

        // Short-lived BFS route cache (item 9): keyed by origin node, invalidated on every Build().
        // Several pedestrians planning in the same short window of frames from nearby/identical
        // origins reuse one shared cameFrom tree instead of each re-running its own BFS.
        private readonly Dictionary<int, int[]> cameFromCache = new();

        public int NodeCount
        {
            get
            {
                EnsureBuilt();
                return nodes.Count;
            }
        }

        public PedestrianNode GetNode(int index)
        {
            EnsureBuilt();
            return nodes[index];
        }

        private void Awake()
        {
            // The HashSets are runtime-only (not themselves serializable); rebuild them from the
            // serialized lists before Build() reads them, since Awake() is exactly the point
            // where a domain reload/scene reload has just wiped them back to null.
            if (useCustomShape)
            {
                customBlockCells = new HashSet<Vector2Int>(customBlockCellsList);
                customPlazaCells = new HashSet<Vector2Int>(customPlazaCellsList);
                customFullyReservedCells = new HashSet<Vector2Int>(customFullyReservedCellsList);
            }

            Build();
        }

        /// <summary>
        /// Sets the street axes without rebuilding the graph, mirroring TrafficNetwork.SetAxes.
        /// </summary>
        public void SetAxes(float[] newAxesX, float[] newAxesZ)
        {
            axesX = newAxesX;
            axesZ = newAxesZ;
            useCustomShape = false;
            customBlockCellsList.Clear();
            customPlazaCellsList.Clear();
            customFullyReservedCellsList.Clear();
            customBlockCells = null;
            customPlazaCells = null;
            customFullyReservedCells = null;
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): builds the graph over the fixed MaxGridSize canvas, but
        /// only real blocks (<paramref name="blockCells"/>) get a ring/interior cross -- mirroring
        /// TrafficNetwork.BuildFromBlockCells. Crossings self-restrict already: BuildCrossings only
        /// adds a crosswalk arm where a TrafficLightIntersection actually exists nearby, and those
        /// are only placed at real decision points (3+ real arms: a 4-way or a T-intersection) for
        /// a custom shape.
        /// </summary>
        public void BuildFromBlockCells(IReadOnlyCollection<Vector2Int> blockCells, IReadOnlyCollection<Vector2Int> plazaCells, IReadOnlyCollection<Vector2Int> fullyReservedCells)
        {
            customBlockCells = new HashSet<Vector2Int>(blockCells);
            customPlazaCells = new HashSet<Vector2Int>(plazaCells);
            customFullyReservedCells = new HashSet<Vector2Int>(fullyReservedCells);
            customBlockCellsList = new List<Vector2Int>(blockCells);
            customPlazaCellsList = new List<Vector2Int>(plazaCells);
            customFullyReservedCellsList = new List<Vector2Int>(fullyReservedCells);
            useCustomShape = true;

            int axisCount = MaxGridSize + 1;
            var axes = new float[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                axes[i] = (i - MaxGridSize / 2f) * CellPitch;
            }

            axesX = axes;
            axesZ = axes;
            Build();
        }

        private void EnsureBuilt()
        {
            if (nodes.Count == 0)
            {
                Build();
            }
        }

        /// <summary>
        /// This city's own TrafficNetwork, resolved by hierarchy so two cities in the same scene
        /// never borrow each other's traffic state for CanCross. Falls back to a scene-wide search
        /// when this network has no CityGeneratorRoot ancestor, same as before this scoping existed.
        /// </summary>
        private TrafficNetwork FindTrafficNetworkInScope()
        {
            CityGeneratorRoot root = GetComponentInParent<CityGeneratorRoot>();
            return root != null
                ? root.GetComponentInChildren<TrafficNetwork>(true)
                : FindAnyObjectByType<TrafficNetwork>();
        }

        /// <summary>
        /// TrafficLightIntersection instances this network is allowed to match crossings against:
        /// only the ones under its own city, mirroring TrafficNetwork.FindLightsInScope. Falls back
        /// to a scene-wide search when this network has no CityGeneratorRoot ancestor.
        /// </summary>
        private TrafficLightIntersection[] FindIntersectionsInScope()
        {
            CityGeneratorRoot root = GetComponentInParent<CityGeneratorRoot>();
            return root != null
                ? root.GetComponentsInChildren<TrafficLightIntersection>(true)
                : FindObjectsByType<TrafficLightIntersection>(FindObjectsInactive.Exclude);
        }

        /// <summary>
        /// Rebuilds the whole graph from the street axes and re-matches crossings against the
        /// scene's TrafficLightIntersection instances. Doubles as the explicit re-bake: safe to
        /// call again at any time (e.g. after moving a building), since it always starts from a
        /// clean slate and prunes obstacles at the end.
        /// </summary>
        [ContextMenu("Rebuild Network")]
        public void Build()
        {
            nodes.Clear();
            cameFromCache.Clear();
            // Null (not just cleared) while Build() runs its own internal AddNode calls (ring,
            // crossing): AddNode only tries to keep nodeComponent in sync once it's non-null, so
            // those calls are left alone and ComputeConnectedComponents below computes the array
            // fresh, once, from the complete final node set.
            nodeComponent = null;

            if (trafficNetwork == null)
            {
                trafficNetwork = FindTrafficNetworkInScope();
            }

            int blocksX = axesX.Length - 1;
            int blocksZ = axesZ.Length - 1;
            if (blocksX <= 0 || blocksZ <= 0)
            {
                return;
            }

            // [bi, bj, corner] -> node index. Corner codes: 0 = SW (min X, min Z), 1 = SE (max X,
            // min Z), 2 = NE (max X, max Z), 3 = NW (min X, max Z).
            var cornerNode = new int[blocksX, blocksZ, 4];

            // [bi, bj, side] -> node index. Side codes: 0 = S, 1 = E, 2 = N, 3 = W.
            var midNode = new int[blocksX, blocksZ, 4];

            for (int bi = 0; bi < blocksX; bi++)
            {
                for (int bj = 0; bj < blocksZ; bj++)
                {
                    if (useCustomShape && !customBlockCells.Contains(new Vector2Int(bi, bj)))
                    {
                        continue;
                    }

                    BuildBlockRing(bi, bj, cornerNode, midNode);

                    // A full-block Custom Place already occupies the whole block, and a plaza
                    // block stays confined to its ring -- neither gets an Interior cross. Only a
                    // normal block does.
                    bool isFullyReserved = useCustomShape
                        ? customFullyReservedCells.Contains(new Vector2Int(bi, bj))
                        : GetBlockFlag(blockIsFullyReserved, bi, bj, blocksZ);
                    bool isPlaza = useCustomShape
                        ? customPlazaCells.Contains(new Vector2Int(bi, bj))
                        : GetBlockFlag(blockIsPlaza, bi, bj, blocksZ);

                    if (isFullyReserved || isPlaza)
                    {
                        continue;
                    }

                    BuildInteriorCross(bi, bj, midNode);
                }
            }

            // Before the crossings pass: BuildCrossings resolves the far side of a border
            // crosswalk against these nodes, so they have to exist by then.
            BuildBorderWalkway(blocksX, blocksZ);

            // Every intersection is a candidate now, including the grid's own border (a
            // T-intersection there needs a crossing on its 3 real arms just like an interior
            // 4-way) -- BuildCrossings itself skips any arm whose block is out of range or a
            // shape hole, and FindNearestIntersection already skips an intersection with no
            // matching TrafficLightIntersection (never placed at a perimeter corner, exactly 2
            // arms), so widening this loop can't add crossings where there's no real one.
            var intersections = FindIntersectionsInScope();
            for (int i = 0; i < axesX.Length; i++)
            {
                for (int j = 0; j < axesZ.Length; j++)
                {
                    BuildCrossings(i, j, blocksX, blocksZ, cornerNode, intersections);
                }
            }

            RebuildBfsBuffers();
            ComputeConnectedComponents();
            PrunePlacedObstacles();
        }

        /// <summary>
        /// Flood fill over the just-built edges, assigning every node an index identifying which
        /// connected component it belongs to. Independent of Blocked: components reflect graph
        /// topology (the same thing FindPath's reachability ultimately depends on), not runtime
        /// obstacle pruning.
        /// </summary>
        private void ComputeConnectedComponents()
        {
            nodeComponent = new int[nodes.Count];
            for (int i = 0; i < nodeComponent.Length; i++)
                nodeComponent[i] = -1;

            var stack = new Stack<int>();
            int currentComponent = 0;

            for (int start = 0; start < nodes.Count; start++)
            {
                if (nodeComponent[start] != -1)
                    continue;

                stack.Push(start);
                nodeComponent[start] = currentComponent;

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    List<int> neighbours = nodes[current].Neighbours;
                    for (int n = 0; n < neighbours.Count; n++)
                    {
                        int next = neighbours[n];
                        if (nodeComponent[next] == -1)
                        {
                            nodeComponent[next] = currentComponent;
                            stack.Push(next);
                        }
                    }
                }

                currentComponent++;
            }
        }

        /// <summary>Which connected component <paramref name="nodeIndex"/> belongs to -- two nodes only have any chance of a path between them if this matches.</summary>
        public int ComponentOf(int nodeIndex)
        {
            EnsureBuilt();
            return nodeComponent[nodeIndex];
        }

        private Vector3 BlockCentre(int bi, int bj)
            => transform.TransformPoint(new Vector3((axesX[bi] + axesX[bi + 1]) * 0.5f, sidewalkY, (axesZ[bj] + axesZ[bj + 1]) * 0.5f));

        // Same as BlockCentre, but also valid for the ring of cells just outside the axes arrays:
        // the perimeter walkway is tiled from the *missing* cells around the city, which for a
        // rectangular grid sit at index -1 / blocksX, past either end of the arrays.
        private Vector3 BlockCentreOutside(int bi, int bj)
            => transform.TransformPoint(new Vector3(axesX[0] + (bi + 0.5f) * CellPitch, sidewalkY, axesZ[0] + (bj + 0.5f) * CellPitch));

        // Whether block (bi, bj) actually has a ring built for it: in range, and (for a Custom
        // Grid shape) a real cell rather than a hole cornerNode was never populated for.
        private bool BlockExists(int bi, int bj, int blocksX, int blocksZ)
        {
            if (bi < 0 || bi >= blocksX || bj < 0 || bj >= blocksZ)
            {
                return false;
            }

            return !useCustomShape || customBlockCells.Contains(new Vector2Int(bi, bj));
        }

        private static bool GetBlockFlag(bool[] flags, int bi, int bj, int blocksZ)
        {
            if (flags == null)
            {
                return false;
            }

            int index = bi * blocksZ + bj;
            return index >= 0 && index < flags.Length && flags[index];
        }

        private void BuildBlockRing(int bi, int bj, int[,,] cornerNode, int[,,] midNode)
        {
            Vector3 c = BlockCentre(bi, bj);

            int sw = AddNode(new Vector3(c.x - ringRadius, c.y, c.z - ringRadius), PedestrianNodeKind.Ring);
            int sMid = AddNode(new Vector3(c.x, c.y, c.z - ringRadius), PedestrianNodeKind.Ring);
            int se = AddNode(new Vector3(c.x + ringRadius, c.y, c.z - ringRadius), PedestrianNodeKind.Ring);
            int eMid = AddNode(new Vector3(c.x + ringRadius, c.y, c.z), PedestrianNodeKind.Ring);
            int ne = AddNode(new Vector3(c.x + ringRadius, c.y, c.z + ringRadius), PedestrianNodeKind.Ring);
            int nMid = AddNode(new Vector3(c.x, c.y, c.z + ringRadius), PedestrianNodeKind.Ring);
            int nw = AddNode(new Vector3(c.x - ringRadius, c.y, c.z + ringRadius), PedestrianNodeKind.Ring);
            int wMid = AddNode(new Vector3(c.x - ringRadius, c.y, c.z), PedestrianNodeKind.Ring);

            Connect(sw, sMid);
            Connect(sMid, se);
            Connect(se, eMid);
            Connect(eMid, ne);
            Connect(ne, nMid);
            Connect(nMid, nw);
            Connect(nw, wMid);
            Connect(wMid, sw);

            cornerNode[bi, bj, 0] = sw;
            cornerNode[bi, bj, 1] = se;
            cornerNode[bi, bj, 2] = ne;
            cornerNode[bi, bj, 3] = nw;

            midNode[bi, bj, 0] = sMid;
            midNode[bi, bj, 1] = eMid;
            midNode[bi, bj, 2] = nMid;
            midNode[bi, bj, 3] = wMid;
        }

        /// <summary>
        /// A block's interior shortcut: 5 nodes (centre + 4 arm midpoints) forming a cross,
        /// connected to the block's own 4 Ring midpoint nodes (never the corners). Gives
        /// pedestrians a way to cut through a normal block's interior instead of only ever walking
        /// its perimeter ring.
        /// </summary>
        private void BuildInteriorCross(int bi, int bj, int[,,] midNode)
        {
            Vector3 c = BlockCentre(bi, bj);

            // Half the block's building-slot gap (CityGeneratorConstants.BuildingSlotPitch / 2):
            // derived directly from ringRadius rather than a new field, since exact placement
            // doesn't matter -- PrunePlacedObstacles blocks any node that ends up overlapping a
            // building's collider regardless of its precise offset.
            float armOffset = ringRadius * 0.5f;

            int centre = AddNode(c, PedestrianNodeKind.Interior);
            int armS = AddNode(new Vector3(c.x, c.y, c.z - armOffset), PedestrianNodeKind.Interior);
            int armE = AddNode(new Vector3(c.x + armOffset, c.y, c.z), PedestrianNodeKind.Interior);
            int armN = AddNode(new Vector3(c.x, c.y, c.z + armOffset), PedestrianNodeKind.Interior);
            int armW = AddNode(new Vector3(c.x - armOffset, c.y, c.z), PedestrianNodeKind.Interior);

            Connect(centre, armS);
            Connect(centre, armE);
            Connect(centre, armN);
            Connect(centre, armW);

            Connect(armS, midNode[bi, bj, 0]);
            Connect(armE, midNode[bi, bj, 1]);
            Connect(armN, midNode[bi, bj, 2]);
            Connect(armW, midNode[bi, bj, 3]);
        }

        /// <summary>
        /// The walkway along the perimeter sidewalk band -- the strip
        /// CityGeneratorGroundBuilder.BuildPerimeterSidewalks lays on the far side of every
        /// perimeter street so the city always ends in sidewalk rather than asphalt. Without it a
        /// pedestrian could only ever walk the blocks' own rings, and the outward crosswalk
        /// already painted at every border T-intersection led nowhere.
        ///
        /// Tiled from the *missing* cells around the city (the same decomposition the ground band
        /// uses): each one contributes a 3-node strip towards every real neighbour it has, and the
        /// strips are stitched into a single contour by position -- an inner corner shares one
        /// node between its two strips, an outer corner gets its own node joining the two strips
        /// that pass it. Nodes are Ring, so pedestrians spawn on and walk to the perimeter like
        /// any other sidewalk.
        /// </summary>
        private void BuildBorderWalkway(int blocksX, int blocksZ)
        {
            borderNodes.Clear();

            // Keyed by position rounded to a decimetre: a node two strips share is created once,
            // whichever of them reaches it first.
            var byPosition = new Dictionary<(int, int), int>();

            int GetOrAdd(Vector3 position)
            {
                var key = (Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.z * 10f));
                if (byPosition.TryGetValue(key, out int existing))
                {
                    return existing;
                }

                int added = AddNode(position, PedestrianNodeKind.Ring);
                byPosition.Add(key, added);
                borderNodes.Add(added);
                return added;
            }

            // A strip ends level with the crosswalk that lands on it, so the link BuildCrossings
            // makes from the outward curb is a single step across the sidewalk's own width.
            float endOffset = CellPitch / 2f - crossingArmOffset;

            for (int mi = -1; mi <= blocksX; mi++)
            {
                for (int mj = -1; mj <= blocksZ; mj++)
                {
                    if (BlockExists(mi, mj, blocksX, blocksZ))
                    {
                        continue;
                    }

                    Vector3 centre = BlockCentreOutside(mi, mj);

                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 u = Dirs[k];
                        int ui = Mathf.RoundToInt(u.x);
                        int uj = Mathf.RoundToInt(u.z);
                        if (!BlockExists(mi + ui, mj + uj, blocksX, blocksZ))
                        {
                            continue;
                        }

                        int ti = -uj;
                        int tj = ui;
                        Vector3 t = new(ti, 0f, tj);

                        Vector3 line = centre + u * perimeterWalkOffset;
                        int mid = GetOrAdd(line);

                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            int ni = mi + ti * sign;
                            int nj = mj + tj * sign;

                            // An inner corner: the walkway turns here, and this strip's end node is
                            // the same node the perpendicular strip ends on.
                            bool turnsHere = BlockExists(ni, nj, blocksX, blocksZ);
                            float tangential = turnsHere ? perimeterWalkOffset : endOffset;
                            int end = GetOrAdd(line + t * (sign * tangential));
                            Connect(mid, end);

                            // Otherwise the same contour edge simply carries on into the next
                            // missing cell, whose strip starts a cell pitch further along.
                            if (turnsHere || !BlockExists(ni + ui, nj + uj, blocksX, blocksZ))
                            {
                                continue;
                            }

                            Vector3 nextLine = BlockCentreOutside(ni, nj) + u * perimeterWalkOffset;
                            Connect(end, GetOrAdd(nextLine - t * (sign * endOffset)));
                        }
                    }

                    // An outer corner of the city: this missing cell touches it only diagonally,
                    // so it owns no strip of its own -- just the corner node joining the two
                    // strips that come round it.
                    for (int dx = -1; dx <= 1; dx += 2)
                    {
                        for (int dz = -1; dz <= 1; dz += 2)
                        {
                            if (!BlockExists(mi + dx, mj + dz, blocksX, blocksZ))
                            {
                                continue;
                            }

                            if (BlockExists(mi + dx, mj, blocksX, blocksZ) || BlockExists(mi, mj + dz, blocksX, blocksZ))
                            {
                                continue;
                            }

                            int corner = GetOrAdd(centre + new Vector3(dx * perimeterWalkOffset, 0f, dz * perimeterWalkOffset));

                            Vector3 alongX = BlockCentreOutside(mi, mj + dz) + new Vector3(dx * perimeterWalkOffset, 0f, -dz * endOffset);
                            Vector3 alongZ = BlockCentreOutside(mi + dx, mj) + new Vector3(-dx * endOffset, 0f, dz * perimeterWalkOffset);
                            Connect(corner, GetOrAdd(alongX));
                            Connect(corner, GetOrAdd(alongZ));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The perimeter walkway node a crosswalk facing out of the city lands on, or -1 if there
        /// is none within <see cref="perimeterLinkRadius"/> of <paramref name="position"/>.
        /// </summary>
        private int FindBorderNodeNear(Vector3 position)
        {
            int best = -1;
            float bestDistance = perimeterLinkRadius * perimeterLinkRadius;

            for (int i = 0; i < borderNodes.Count; i++)
            {
                float distance = (nodes[borderNodes[i]].Position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = borderNodes[i];
                }
            }

            return best;
        }

        /// <summary>
        /// Builds the 4 crosswalk arms of the interior intersection at axis indices (i, j): for
        /// each arm direction, a curb -> crossing -> curb chain linking the two ring corners it
        /// faces, with the crossing node's Intersection/CrossingAxisIsX set for CanCross.
        /// </summary>
        private void BuildCrossings(int i, int j, int blocksX, int blocksZ, int[,,] cornerNode, TrafficLightIntersection[] intersections)
        {
            // Transformed like BlockCentre/BlockCentreOutside: FindNearestIntersection matches
            // against TrafficLight world positions, and the arm offsets below (lateral, travel)
            // are pure vector arithmetic that stays correct once centre itself is in world space
            // (only translation of the root is supported -- see CLAUDE.md).
            Vector3 centre = transform.TransformPoint(new Vector3(axesX[i], sidewalkY, axesZ[j]));
            TrafficLightIntersection matched = FindNearestIntersection(centre, intersections);
            if (matched == null)
            {
                return;
            }

            for (int k = 0; k < 4; k++)
            {
                Vector3 dir = Dirs[k];
                bool axisIsX = Mathf.Abs(dir.x) > 0f;
                Vector3 travel = axisIsX ? Vector3.forward : Vector3.right;

                int blockAI = axisIsX ? (dir.x > 0f ? i : i - 1) : i;
                int blockAJ = axisIsX ? j : (dir.z > 0f ? j : j - 1);
                int blockBI = axisIsX ? blockAI : i - 1;
                int blockBJ = axisIsX ? j - 1 : blockAJ;

                bool blockAExists = BlockExists(blockAI, blockAJ, blocksX, blocksZ);
                bool blockBExists = BlockExists(blockBI, blockBJ, blocksX, blocksZ);

                // Both sides off the city (a hole's own far corner): nothing to link either way.
                if (!blockAExists && !blockBExists)
                {
                    continue;
                }

                Vector3 lateral = dir * crossingArmOffset;
                Vector3 curbNearPos = centre + lateral + travel * streetHalfWidth;
                Vector3 crossingPos = centre + lateral;
                Vector3 curbFarPos = centre + lateral - travel * streetHalfWidth;
                curbNearPos.y = sidewalkY;
                curbFarPos.y = sidewalkY;
                crossingPos.y = roadY;

                // A side with no block behind it is the city's own contour: the crosswalk lands
                // on the perimeter walkway instead of on a block's ring corner. That is exactly
                // where the zebra stripes at a border T-intersection were already painted.
                int sideA = blockAExists
                    ? cornerNode[blockAI, blockAJ, NearestCornerCode(blockAI, blockAJ, i, j)]
                    : FindBorderNodeNear(curbNearPos);
                int sideB = blockBExists
                    ? cornerNode[blockBI, blockBJ, NearestCornerCode(blockBI, blockBJ, i, j)]
                    : FindBorderNodeNear(curbFarPos);

                if (sideA < 0 || sideB < 0)
                {
                    continue;
                }

                int curbNear = AddNode(curbNearPos, PedestrianNodeKind.Curb);
                int crossing = AddNode(crossingPos, PedestrianNodeKind.Crossing);
                int curbFar = AddNode(curbFarPos, PedestrianNodeKind.Curb);

                SetCrossingInfo(crossing, matched, axisIsX);

                Connect(sideA, curbNear);
                Connect(curbNear, crossing);
                Connect(crossing, curbFar);
                Connect(curbFar, sideB);
            }
        }

        // Given block (bi, bj) is adjacent to intersection (i, j), returns which of its 4 corners
        // faces that intersection: the block's own min/max side matching whichever side of the
        // intersection it sits on.
        private static int NearestCornerCode(int bi, int bj, int i, int j)
        {
            bool minX = bi == i;
            bool minZ = bj == j;
            if (minX && minZ) return 0; // SW
            if (!minX && minZ) return 1; // SE
            if (!minX && !minZ) return 2; // NE
            return 3; // NW
        }

        private static TrafficLightIntersection FindNearestIntersection(Vector3 centre, TrafficLightIntersection[] intersections)
        {
            TrafficLightIntersection best = null;
            float bestDistance = float.MaxValue;

            foreach (TrafficLightIntersection candidate in intersections)
            {
                TrafficLight[] lights = candidate.GetComponentsInChildren<TrafficLight>();
                if (lights.Length == 0)
                {
                    continue;
                }

                Vector3 average = Vector3.zero;
                foreach (TrafficLight light in lights)
                {
                    average += light.transform.position;
                }
                average /= lights.Length;
                average.y = centre.y;

                float distance = Vector3.Distance(average, centre);
                if (distance < 14f && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private void SetCrossingInfo(int nodeIndex, TrafficLightIntersection intersection, bool axisIsX)
        {
            PedestrianNode node = nodes[nodeIndex];
            node.Intersection = intersection;
            node.CrossingAxisIsX = axisIsX;
            nodes[nodeIndex] = node;
        }

        /// <summary>Adds a node and returns its index.</summary>
        public int AddNode(Vector3 position, PedestrianNodeKind kind)
        {
            nodes.Add(new PedestrianNode
            {
                Position = position,
                Kind = kind,
                Neighbours = new List<int>()
            });
            int index = nodes.Count - 1;

            // Only once Build() has already computed the array once (nodeComponent is null while
            // Build() runs its own internal AddNode calls -- see Build()): keeps it in sync for a
            // node AddNode-ed afterwards, so PickRandomDestination never indexes past its end.
            if (nodeComponent != null)
            {
                System.Array.Resize(ref nodeComponent, nodes.Count);
                nodeComponent[index] = -1;
            }

            return index;
        }

        /// <summary>Adds an undirected edge between two existing nodes.</summary>
        public void Connect(int a, int b)
        {
            if (!nodes[a].Neighbours.Contains(b))
            {
                nodes[a].Neighbours.Add(b);
            }

            if (!nodes[b].Neighbours.Contains(a))
            {
                nodes[b].Neighbours.Add(a);
            }
        }

        /// <summary>
        /// Marks a node Blocked (or clears it) without touching its edges — used by the generator
        /// to prune nodes that land inside a user prefab's footprint (the obstacles list), the
        /// same "level 1" pruning every other placed category gets, ahead of PrunePlacedObstacles'
        /// own runtime Physics-based pass.
        /// </summary>
        public void SetBlocked(int nodeIndex, bool blocked)
        {
            PedestrianNode node = nodes[nodeIndex];
            node.Blocked = blocked;
            nodes[nodeIndex] = node;
        }

        /// <summary>Closest node of the given kind to a world position. Returns -1 if none exist.</summary>
        public int FindNearestNode(Vector3 position, PedestrianNodeKind kind)
        {
            EnsureBuilt();
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Kind != kind)
                {
                    continue;
                }

                float distance = (nodes[i].Position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Whether a pedestrian waiting at the given Crossing node may step onto the road: the traffic that crosses it must be red.</summary>
        public bool CanCross(int crossingNodeIndex)
        {
            EnsureBuilt();
            PedestrianNode node = nodes[crossingNodeIndex];
            if (node.Kind != PedestrianNodeKind.Crossing || node.Intersection == null || trafficNetwork == null)
            {
                return true;
            }

            // Amber still has cars moving/braking through the intersection: only Red is safe to
            // step onto, "not green" alone would let pedestrians start crossing on amber.
            return trafficNetwork.AxisState(node.Intersection, node.CrossingAxisIsX) == TrafficLightState.Red;
        }

        /// <summary>
        /// Picks a random non-blocked Ring/Interior node as a final destination -- Curb and
        /// Crossing are excluded (mid-crosswalk link nodes, never a place to actually walk to).
        /// SPEC 10: Interior is a valid destination, not just a waypoint a route might cross,
        /// since a same-block Ring-to-Ring path never actually routes through it (BFS ties
        /// between a block's ring and its interior always resolve to the ring, whose edges are
        /// built first) -- without this, the Interior node kind would exist in the graph but a
        /// pedestrian would never actually be observed walking into a block's interior.
        /// When <paramref name="requiredComponent"/> is non-negative (item 9), only considers nodes
        /// in that connected component: on a grid with isolated block rings (e.g. gridWidth == 1 or
        /// gridHeight == 1, see CLAUDE.md), this stops PlanNewDestination from repeatedly drawing
        /// candidates FindPath could never reach in the first place.
        /// When <paramref name="allowedNodes"/> is non-null (SPEC 12: Custom Pedestrians), only
        /// considers nodes in that subset -- used by a PedestrianAgent confined to a hand-traced
        /// node network instead of the whole city. A normal pedestrian passes null and behaves
        /// exactly as before.
        /// </summary>
        public int PickRandomDestination(int requiredComponent = -1, IReadOnlyList<int> allowedNodes = null)
        {
            EnsureBuilt();

            if (allowedNodes != null)
            {
                if (allowedNodes.Count == 0)
                    return -1;

                int restrictedAttempts = allowedNodes.Count * 2;
                for (int attempt = 0; attempt < restrictedAttempts; attempt++)
                {
                    int candidate = allowedNodes[Random.Range(0, allowedNodes.Count)];
                    if (candidate < 0 || candidate >= nodes.Count)
                        continue;

                    PedestrianNode candidateNode = nodes[candidate];
                    if (candidateNode.Blocked)
                        continue;
                    if (candidateNode.Kind == PedestrianNodeKind.Curb || candidateNode.Kind == PedestrianNodeKind.Crossing)
                        continue;

                    return candidate;
                }

                return -1;
            }

            int attempts = nodes.Count * 2;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int candidate = Random.Range(0, nodes.Count);
                PedestrianNode node = nodes[candidate];
                if (node.Blocked)
                    continue;
                if (node.Kind == PedestrianNodeKind.Curb || node.Kind == PedestrianNodeKind.Crossing)
                    continue;
                if (requiredComponent >= 0 && nodeComponent[candidate] != requiredComponent)
                    continue;

                return candidate;
            }

            return -1;
        }

        /// <summary>
        /// Breadth-first shortest path from `from` to `to` (fewest hops; every edge is unweighted).
        /// Writes node indices (from -> to inclusive) into the caller-supplied outPath buffer and
        /// returns how many were written, or 0 if unreachable.
        ///
        /// Item 9: the full single-source `cameFrom` tree from `from` is cached (see
        /// <see cref="cameFromCache"/>) rather than only walking until `to` is found, so several
        /// FindPath calls sharing the same `from` within one Build() window (PlanNewDestination
        /// tries up to 8 candidate destinations per call, always from the current node) reuse one
        /// BFS instead of re-running it per destination. The BFS itself is still zero-allocation
        /// per call; only the first call for a given, not-yet-cached `from` allocates its tree.
        /// </summary>
        /// When <paramref name="allowedNodes"/> is non-null (SPEC 12: Custom Pedestrians), the
        /// route is computed over that node subset only (both endpoints must be in it), bypassing
        /// <see cref="cameFromCache"/> since the cache is keyed by origin alone and shared across
        /// every agent -- a restricted route is never cached. A normal pedestrian passes null and
        /// gets the exact cached/unrestricted behaviour as before.
        public int FindPath(int from, int to, int[] outPath, IReadOnlyList<int> allowedNodes = null)
        {
            EnsureBuilt();
            if (from < 0 || to < 0 || from >= nodes.Count || to >= nodes.Count)
            {
                return 0;
            }

            if (allowedNodes != null && (!ContainsNode(allowedNodes, from) || !ContainsNode(allowedNodes, to)))
            {
                return 0;
            }

            if (from == to)
            {
                outPath[0] = from;
                return 1;
            }

            int[] cameFrom = allowedNodes != null ? ComputeRestrictedCameFrom(from, allowedNodes) : GetOrComputeCameFrom(from);

            // -2 = never visited by this origin's BFS (unreachable); -1 is reserved for `from`
            // itself, which can't be `to` here (handled above).
            if (cameFrom[to] == -2)
            {
                return 0;
            }

            int length = 0;
            int node = to;
            while (node != -1)
            {
                length++;
                node = cameFrom[node];
            }

            if (length > outPath.Length)
            {
                // Caller's buffer is too small for this path: refuse rather than overrun it.
                return 0;
            }

            int writeIndex = length - 1;
            node = to;
            while (node != -1 && writeIndex >= 0)
            {
                outPath[writeIndex--] = node;
                node = cameFrom[node];
            }

            return length;
        }

        private int[] GetOrComputeCameFrom(int from)
        {
            if (cameFromCache.TryGetValue(from, out int[] cached))
            {
                return cached;
            }

            // AddNode may have grown the graph (e.g. points of interest registered after Build())
            // since the scratch buffers were last sized.
            if (bfsQueue == null || bfsQueue.Length != nodes.Count)
            {
                RebuildBfsBuffers();
            }

            var cameFrom = new int[nodes.Count];
            System.Array.Fill(cameFrom, -2);

            System.Array.Clear(bfsVisited, 0, bfsVisited.Length);
            int head = 0, tail = 0;
            bfsQueue[tail++] = from;
            bfsVisited[from] = true;
            cameFrom[from] = -1;

            while (head < tail)
            {
                int current = bfsQueue[head++];
                List<int> neighbours = nodes[current].Neighbours;
                for (int n = 0; n < neighbours.Count; n++)
                {
                    int next = neighbours[n];
                    if (bfsVisited[next] || nodes[next].Blocked)
                    {
                        continue;
                    }

                    bfsVisited[next] = true;
                    cameFrom[next] = current;
                    bfsQueue[tail++] = next;
                }
            }

            cameFromCache[from] = cameFrom;
            return cameFrom;
        }

        private void RebuildBfsBuffers()
        {
            bfsQueue = new int[nodes.Count];
            bfsVisited = new bool[nodes.Count];
        }

        private static bool ContainsNode(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Same BFS as <see cref="GetOrComputeCameFrom"/>, but neighbour expansion is filtered to
        /// <paramref name="allowedNodes"/> -- never cached, since the subset differs per Custom
        /// Pedestrian entry rather than being shared by every agent in the scene.
        /// </summary>
        private int[] ComputeRestrictedCameFrom(int from, IReadOnlyList<int> allowedNodes)
        {
            if (bfsQueue == null || bfsQueue.Length != nodes.Count)
            {
                RebuildBfsBuffers();
            }

            var cameFrom = new int[nodes.Count];
            System.Array.Fill(cameFrom, -2);

            System.Array.Clear(bfsVisited, 0, bfsVisited.Length);
            int head = 0, tail = 0;
            bfsQueue[tail++] = from;
            bfsVisited[from] = true;
            cameFrom[from] = -1;

            while (head < tail)
            {
                int current = bfsQueue[head++];
                List<int> neighbours = nodes[current].Neighbours;
                for (int n = 0; n < neighbours.Count; n++)
                {
                    int next = neighbours[n];
                    if (bfsVisited[next] || nodes[next].Blocked || !ContainsNode(allowedNodes, next))
                    {
                        continue;
                    }

                    bfsVisited[next] = true;
                    cameFrom[next] = current;
                    bfsQueue[tail++] = next;
                }
            }

            return cameFrom;
        }

        /// <summary>
        /// Auto-repairs the graph against the scene as it stands right now: a Physics.CheckSphere
        /// slightly above sidewalk height (so the ground itself never counts) marks any node
        /// overlapping a moved/added obstacle as Blocked, plus a downward raycast to catch a node
        /// left without any ground under it. Blocked nodes aren't removed (their edges stay put,
        /// no rebuild needed) — FindPath simply refuses to route through them.
        /// </summary>
        [ContextMenu("Prune Placed Obstacles")]
        public void PrunePlacedObstacles()
        {
            // Generation creates every ground/building/prop collider in the same script execution
            // that then immediately calls this (via Awake() -> Build()): without forcing a sync,
            // Physics.Raycast/CheckSphere below can run before Unity's physics engine has indexed
            // those brand-new colliders, so the ground raycast finds nothing under a huge fraction
            // of nodes and !hasGround marks them all Blocked -- confirmed directly (one bad
            // generation had 531/715 nodes Blocked; calling this alone, with no other change,
            // dropped it to 0). Cheap relative to the CheckSphere/Raycast pass this method already
            // does per node.
            Physics.SyncTransforms();

            for (int i = 0; i < nodes.Count; i++)
            {
                PedestrianNode node = nodes[i];
                Vector3 samplePoint = node.Position + Vector3.up * pruneCheckHeight;
                bool overlapsObstacle = Physics.CheckSphere(samplePoint, pruneCheckRadius, obstacleMask);
                bool hasGround = Physics.Raycast(samplePoint, Vector3.down, pruneCheckHeight + 1f);
                node.Blocked = overlapsObstacle || !hasGround;
                nodes[i] = node;
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Deliberately does not EnsureBuilt(): selecting the object before Play (or before the
            // generator has built anything) would otherwise construct the whole graph just to draw
            // gizmos. Nothing to draw yet in that case.
            if (!drawGraph || nodes.Count == 0)
            {
                return;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                PedestrianNode node = nodes[i];
                Gizmos.color = node.Blocked ? new Color(1f, 0f, 0f, 0.6f) : KindColor(node.Kind);
                Gizmos.DrawSphere(node.Position + Vector3.up * 0.3f, 0.3f);

                Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                foreach (int neighbour in node.Neighbours)
                {
                    if (neighbour > i)
                    {
                        Gizmos.DrawLine(node.Position + Vector3.up * 0.3f, nodes[neighbour].Position + Vector3.up * 0.3f);
                    }
                }
            }
        }

        private static Color KindColor(PedestrianNodeKind kind) => kind switch
        {
            PedestrianNodeKind.Ring => new Color(0.2f, 0.9f, 0.3f),
            PedestrianNodeKind.Curb => new Color(0.9f, 0.8f, 0.1f),
            PedestrianNodeKind.Crossing => new Color(1f, 0.4f, 0.1f),
            PedestrianNodeKind.Interior => new Color(0.3f, 0.6f, 1f),
            _ => Color.white
        };
    }
}
