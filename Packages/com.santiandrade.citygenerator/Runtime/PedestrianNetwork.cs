using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    public enum PedestrianNodeKind { Ring, Curb, Crossing }

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

        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        private readonly List<PedestrianNode> nodes = new();

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
            Build();
        }

        /// <summary>
        /// Sets the street axes without rebuilding the graph, mirroring TrafficNetwork.SetAxes.
        /// </summary>
        public void SetAxes(float[] newAxesX, float[] newAxesZ)
        {
            axesX = newAxesX;
            axesZ = newAxesZ;
        }

        private void EnsureBuilt()
        {
            if (nodes.Count == 0)
            {
                Build();
            }
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
                trafficNetwork = FindAnyObjectByType<TrafficNetwork>();
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

            for (int bi = 0; bi < blocksX; bi++)
            {
                for (int bj = 0; bj < blocksZ; bj++)
                {
                    BuildBlockRing(bi, bj, cornerNode);
                }
            }

            var intersections = FindObjectsByType<TrafficLightIntersection>(FindObjectsInactive.Exclude);
            for (int i = 1; i < axesX.Length - 1; i++)
            {
                for (int j = 1; j < axesZ.Length - 1; j++)
                {
                    BuildCrossings(i, j, cornerNode, intersections);
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
            => new((axesX[bi] + axesX[bi + 1]) * 0.5f, sidewalkY, (axesZ[bj] + axesZ[bj + 1]) * 0.5f);

        private void BuildBlockRing(int bi, int bj, int[,,] cornerNode)
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
        }

        /// <summary>
        /// Builds the 4 crosswalk arms of the interior intersection at axis indices (i, j): for
        /// each arm direction, a curb -> crossing -> curb chain linking the two ring corners it
        /// faces, with the crossing node's Intersection/CrossingAxisIsX set for CanCross.
        /// </summary>
        private void BuildCrossings(int i, int j, int[,,] cornerNode, TrafficLightIntersection[] intersections)
        {
            Vector3 centre = new(axesX[i], sidewalkY, axesZ[j]);
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

                int cornerA = cornerNode[blockAI, blockAJ, NearestCornerCode(blockAI, blockAJ, i, j)];
                int cornerB = cornerNode[blockBI, blockBJ, NearestCornerCode(blockBI, blockBJ, i, j)];

                Vector3 lateral = dir * crossingArmOffset;
                Vector3 curbNearPos = centre + lateral + travel * streetHalfWidth;
                Vector3 crossingPos = centre + lateral;
                Vector3 curbFarPos = centre + lateral - travel * streetHalfWidth;
                curbNearPos.y = sidewalkY;
                curbFarPos.y = sidewalkY;
                crossingPos.y = roadY;

                int curbNear = AddNode(curbNearPos, PedestrianNodeKind.Curb);
                int crossing = AddNode(crossingPos, PedestrianNodeKind.Crossing);
                int curbFar = AddNode(curbFarPos, PedestrianNodeKind.Curb);

                SetCrossingInfo(crossing, matched, axisIsX);

                Connect(cornerA, curbNear);
                Connect(curbNear, crossing);
                Connect(crossing, curbFar);
                Connect(curbFar, cornerB);
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
        /// Picks a random non-blocked Ring node — the only kind valid as a
        /// final destination. When <paramref name="requiredComponent"/> is non-negative (item 9),
        /// only considers nodes in that connected component: on a grid with isolated block rings
        /// (e.g. gridWidth == 1 or gridHeight == 1, see CLAUDE.md), this stops PlanNewDestination
        /// from repeatedly drawing candidates FindPath could never reach in the first place.
        /// </summary>
        public int PickRandomDestination(int requiredComponent = -1)
        {
            EnsureBuilt();
            int attempts = nodes.Count * 2;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int candidate = Random.Range(0, nodes.Count);
                PedestrianNode node = nodes[candidate];
                if (node.Blocked)
                    continue;
                if (node.Kind != PedestrianNodeKind.Ring)
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
        public int FindPath(int from, int to, int[] outPath)
        {
            EnsureBuilt();
            if (from < 0 || to < 0 || from >= nodes.Count || to >= nodes.Count)
            {
                return 0;
            }

            if (from == to)
            {
                outPath[0] = from;
                return 1;
            }

            int[] cameFrom = GetOrComputeCameFrom(from);

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
            _ => Color.white
        };
    }
}
