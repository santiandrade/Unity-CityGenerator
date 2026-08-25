using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    public enum PedestrianNodeKind { Ring, Curb, Crossing, PointOfInterest }

    /// <summary>Serialized so a point of interest (bench/fountain stop) survives the Awake -> Build()
    /// cycle: nodes.Clear() wipes the runtime graph every Build(), so POIs must be re-added from
    /// something serialized, not just left in the in-memory node list.</summary>
    [System.Serializable]
    public struct PointOfInterestDescriptor
    {
        public Vector3 position;
        public Vector3 lookAt;
        // Positions of every node this POI was connected to at registration time (usually one Ring
        // corner, but a plaza centerpiece loop also connects POI-to-POI). Node indices are not
        // stable across Build() calls (the node list is rebuilt from scratch), so every connection
        // is re-resolved by exact node position — deterministic, since node geometry only depends
        // on settings/grid, not on random.
        public List<Vector3> connectedPositions;
    }

    /// <summary>An undirected node in the pedestrian graph.</summary>
    public struct PedestrianNode
    {
        public Vector3 Position;
        public PedestrianNodeKind Kind;

        /// <summary>Only set when Kind == Crossing.</summary>
        public TrafficLightIntersection Intersection;

        /// <summary>Only meaningful when Kind == Crossing: true if the traffic crossed here flows along X.</summary>
        public bool CrossingAxisIsX;

        /// <summary>Only set when Kind == PointOfInterest: direction an agent should face while stopped here.</summary>
        public Vector3? LookAt;

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

        [Header("Obstacle pruning")]
        [Tooltip("Layers Physics.CheckSphere treats as obstacles. Set by CityGeneratorPedestrianBuilder to exclude the Pedestrian layer: without that, a pedestrian standing right on its own spawn node gets detected by this very check the moment PedestrianNetwork.Awake() rebuilds the graph in Play, wrongly marking that node (and any neighbour whose only route ran through it) Blocked.")]
        [SerializeField] private LayerMask obstacleMask = ~0;

        [Tooltip("Sample radius for the per-node Physics.CheckSphere. Small and point-like: just enough to catch an overlapping collider without two neighbouring ring nodes ever seeing each other's obstacle.")]
        [SerializeField] private float pruneCheckRadius = 0.3f;

        [Tooltip("Height above the node at which the obstacle sphere is sampled: above sidewalk level, so the ground itself never counts as an obstacle.")]
        [SerializeField] private float pruneCheckHeight = 1f;

        [Header("Debugging")]
        [SerializeField] private bool drawGraph = true;

        [Header("Points of interest")]
        [Tooltip("Serialized POI registrations (bench/fountain stops), so they survive the Awake -> Build() cycle in Play. Populated by RegisterPointOfInterest, called by CityGeneratorPedestrianBuilder.RegisterPointsOfInterest.")]
        [SerializeField] private List<PointOfInterestDescriptor> pointsOfInterest = new();

        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        private readonly List<PedestrianNode> nodes = new();

        // Node index -> index into pointsOfInterest, for every currently-live PointOfInterest node.
        // Rebuilt from scratch every Build(); lets ConnectPointOfInterest find which descriptor (if
        // any) to extend when a new edge touches a POI node.
        private readonly Dictionary<int, int> poiDescriptorByNodeIndex = new();

        // BFS scratch buffers: sized to nodes.Count once per Build()/AddNode() batch, then reused
        // by every FindPath call without allocating — Unity's single-threaded main loop makes one
        // shared set safe across every agent that calls FindPath in turn.
        private int[] bfsQueue;
        private int[] bfsParent;
        private bool[] bfsVisited;

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
            poiDescriptorByNodeIndex.Clear();

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

            ReinsertPointsOfInterest();

            RebuildBfsBuffers();
            PrunePlacedObstacles();
        }

        /// <summary>
        /// Re-adds every previously-registered point of interest (bench/fountain stop) from
        /// <see cref="pointsOfInterest"/>, in two passes: first every POI node is re-created (so
        /// every position a connection might target — including another POI's — exists), then
        /// every recorded connection is re-resolved by exact node position and re-applied via
        /// <see cref="ConnectPointOfInterest"/>, which also re-populates <see cref="pointsOfInterest"/>
        /// itself for the next Build() call.
        /// </summary>
        private void ReinsertPointsOfInterest()
        {
            if (pointsOfInterest.Count == 0)
            {
                return;
            }

            List<PointOfInterestDescriptor> descriptors = new(pointsOfInterest);
            pointsOfInterest.Clear();

            var reinsertedNodeIndex = new int[descriptors.Count];
            for (int i = 0; i < descriptors.Count; i++)
            {
                reinsertedNodeIndex[i] = RegisterPointOfInterest(descriptors[i].position, descriptors[i].lookAt);
            }

            for (int i = 0; i < descriptors.Count; i++)
            {
                foreach (Vector3 connectedPosition in descriptors[i].connectedPositions)
                {
                    int targetIndex = FindNearestNodeAnyKind(connectedPosition);
                    if (targetIndex >= 0)
                    {
                        ConnectPointOfInterest(reinsertedNodeIndex[i], targetIndex);
                    }
                }
            }
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

        /// <summary>Adds a node and returns its index. Public so the pedestrian builder can wire in points of interest after Build().</summary>
        public int AddNode(Vector3 position, PedestrianNodeKind kind, Vector3? lookAt = null)
        {
            nodes.Add(new PedestrianNode
            {
                Position = position,
                Kind = kind,
                LookAt = lookAt,
                Neighbours = new List<int>()
            });
            return nodes.Count - 1;
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
        /// Adds a PointOfInterest node and connects it to zero or more already-existing nodes,
        /// persisting a <see cref="PointOfInterestDescriptor"/> so it (and its connections) survive
        /// a future <see cref="Build"/> call (Play mode, or an explicit re-bake). Called by
        /// <c>CityGeneratorPedestrianBuilder.RegisterPointsOfInterest</c>.
        /// </summary>
        public int RegisterPointOfInterest(Vector3 position, Vector3 lookAt, params int[] connectedNodeIndices)
        {
            int nodeIndex = AddNode(position, PedestrianNodeKind.PointOfInterest, lookAt);
            pointsOfInterest.Add(new PointOfInterestDescriptor
            {
                position = position,
                lookAt = lookAt,
                connectedPositions = new List<Vector3>()
            });
            poiDescriptorByNodeIndex[nodeIndex] = pointsOfInterest.Count - 1;

            foreach (int connectedNodeIndex in connectedNodeIndices)
            {
                ConnectPointOfInterest(nodeIndex, connectedNodeIndex);
            }

            return nodeIndex;
        }

        /// <summary>
        /// Same as <see cref="Connect"/>, but also extends either endpoint's
        /// <see cref="PointOfInterestDescriptor"/> (if it is a registered POI) with the other
        /// endpoint's position, so this edge is replayed by <see cref="ReinsertPointsOfInterest"/>
        /// on the next Build(). Use this (not plain <see cref="Connect"/>) for any edge touching a
        /// POI node, including POI-to-POI edges like a plaza centerpiece's loop.
        /// </summary>
        public void ConnectPointOfInterest(int a, int b)
        {
            Connect(a, b);
            AppendPoiConnection(a, b);
            AppendPoiConnection(b, a);
        }

        private void AppendPoiConnection(int nodeIndex, int otherIndex)
        {
            if (!poiDescriptorByNodeIndex.TryGetValue(nodeIndex, out int descriptorIndex))
            {
                return;
            }

            Vector3 otherPosition = nodes[otherIndex].Position;
            PointOfInterestDescriptor descriptor = pointsOfInterest[descriptorIndex];
            if (!descriptor.connectedPositions.Contains(otherPosition))
            {
                descriptor.connectedPositions.Add(otherPosition);
            }
            pointsOfInterest[descriptorIndex] = descriptor;
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

        /// <summary>Closest node of any kind to a world position, used by <see cref="ReinsertPointsOfInterest"/> to re-resolve a persisted connection (which may target a Ring corner or another POI) by exact position instead of index. Returns -1 if the graph is empty.</summary>
        private int FindNearestNodeAnyKind(Vector3 position)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < nodes.Count; i++)
            {
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

        /// <summary>Picks a random non-blocked Ring or PointOfInterest node — the only kinds valid as a final destination.</summary>
        public int PickRandomDestination()
        {
            EnsureBuilt();
            int attempts = nodes.Count * 2;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int candidate = Random.Range(0, nodes.Count);
                PedestrianNodeKind kind = nodes[candidate].Kind;
                if (!nodes[candidate].Blocked && (kind == PedestrianNodeKind.Ring || kind == PedestrianNodeKind.PointOfInterest))
                {
                    return candidate;
                }
            }

            return -1;
        }

        /// <summary>
        /// Breadth-first shortest path from `from` to `to` (fewest hops; every edge is unweighted).
        /// Writes node indices (from -> to inclusive) into the caller-supplied outPath buffer and
        /// returns how many were written, or 0 if unreachable. Uses only the pre-allocated BFS
        /// scratch buffers — no allocation per call.
        /// </summary>
        public int FindPath(int from, int to, int[] outPath)
        {
            EnsureBuilt();
            if (from < 0 || to < 0 || from >= nodes.Count || to >= nodes.Count)
            {
                return 0;
            }

            // AddNode may have grown the graph (e.g. points of interest registered after Build())
            // since the buffers were last sized.
            if (bfsQueue == null || bfsQueue.Length != nodes.Count)
            {
                RebuildBfsBuffers();
            }

            if (from == to)
            {
                outPath[0] = from;
                return 1;
            }

            System.Array.Clear(bfsVisited, 0, bfsVisited.Length);
            int head = 0, tail = 0;
            bfsQueue[tail++] = from;
            bfsVisited[from] = true;
            bfsParent[from] = -1;

            bool found = false;
            while (head < tail)
            {
                int current = bfsQueue[head++];
                if (current == to)
                {
                    found = true;
                    break;
                }

                List<int> neighbours = nodes[current].Neighbours;
                for (int n = 0; n < neighbours.Count; n++)
                {
                    int next = neighbours[n];
                    if (bfsVisited[next] || nodes[next].Blocked)
                    {
                        continue;
                    }

                    bfsVisited[next] = true;
                    bfsParent[next] = current;
                    bfsQueue[tail++] = next;
                }
            }

            if (!found)
            {
                return 0;
            }

            int length = 0;
            int node = to;
            while (node != -1)
            {
                length++;
                node = bfsParent[node];
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
                node = bfsParent[node];
            }

            return length;
        }

        private void RebuildBfsBuffers()
        {
            bfsQueue = new int[nodes.Count];
            bfsParent = new int[nodes.Count];
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
            PedestrianNodeKind.PointOfInterest => new Color(0.2f, 0.6f, 1f),
            _ => Color.white
        };
    }
}
