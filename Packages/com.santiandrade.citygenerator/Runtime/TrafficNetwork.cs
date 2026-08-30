using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// The city's traffic network. From the grid layout (the street axes) it
    /// generates a lane graph that <see cref="CarAgent"/> instances follow, always
    /// staying on the road and driving on the right.
    ///
    /// For each intersection and each of the four directions there are two nodes:
    /// an <em>entry</em> (the inner corner of the crossing, where the turn is decided and
    /// where the traffic light applies) and an <em>exit</em> (the point where the crossing is left).
    /// Turning right needs no intermediate node because one direction's entry
    /// coincides geometrically with the exit of the direction to its right.
    /// </summary>
    public class TrafficNetwork : MonoBehaviour
    {
        /// <summary>An exit from a node, with the weight it has when picking a random route.</summary>
        public struct Exit
        {
            public int Node;
            public float Weight;
        }

        public class Node
        {
            public Vector3 Position;
            /// <summary>Direction of travel this node is crossed with.</summary>
            public Vector3 Direction;
            /// <summary>Entries are the points where the traffic light or crossing priority applies.</summary>
            public bool IsEntry;
            public int Intersection;
            public TrafficLight Light;
            public readonly List<Exit> Exits = new List<Exit>();
        }

        [Header("Layout")]
        [Tooltip("Street axis coordinates along X, in ascending order.")]
        [SerializeField] private float[] axesX = { -84f, -28f, 28f, 84f };

        [Tooltip("Street axis coordinates along Z, in ascending order.")]
        [SerializeField] private float[] axesZ = { -84f, -28f, 28f, 84f };

        // Own copies of the layout geometry needed by BuildFromBlockCells (not read from
        // CityGeneratorConstants: that class is Editor-only/internal, and every other Runtime
        // script in the tool already keeps its own copy of the numbers it needs -- see
        // PedestrianNetwork's equivalent comment).
        private const float CellPitch = 56f;
        private const int MaxGridSize = 10;

        // Set by BuildFromBlockCells; gates Neighbour()'s extra shape-adjacency check so the
        // rectangular SetAxes/Build() path (useCustomShape == false) is completely unaffected.
        private bool useCustomShape;
        private HashSet<Vector2Int> customBlockCells;

        [Tooltip("Lane offset from the street axis. Must fit within the roadway.")]
        [SerializeField] private float laneOffset = 2.6f;

        [Header("Traffic flow")]
        [Tooltip("Distance ahead of the crossing entry where a car stops on red.")]
        [SerializeField] private float stopLineBack = 6.5f;

        [SerializeField] private float straightWeight = 2.5f;
        [SerializeField] private float turnWeight = 1f;

        [Tooltip("Multiplies the weight of exits that lead towards the interior of the grid.")]
        [SerializeField] private float interiorBias = 1.8f;

        [Tooltip("Penalizes exits that keep the vehicle circling the perimeter streets.")]
        [SerializeField] private float borderPenalty = 0.35f;

        [Header("References")]
        [Tooltip("This network's TrafficManager, on the same GameObject. Set by CityGeneratorTrafficBuilder.AddManagerComponent. CarAgent resolves its manager through this reference instead of a global static Instance, so multiple cities/networks in the same scene never share (or fight over) a single manager.")]
        [SerializeField] private TrafficManager manager;

        public TrafficManager Manager => manager;

        [Tooltip("This network's TrafficLaneOccupancy, on the same GameObject as Manager. Set by CityGeneratorTrafficBuilder.AddManagerComponent. CarAgent consults it first for the 'car ahead in the same lane segment' case before falling back to its SphereCast sensor.")]
        [SerializeField] private TrafficLaneOccupancy laneOccupancy;

        public TrafficLaneOccupancy LaneOccupancy => laneOccupancy;

        [Tooltip("Time after which an unsignalled crossing's priority is released if its owner hasn't passed.")]
        [SerializeField] private float reservationTimeout = 4f;

        [Header("Debugging")]
        [SerializeField] private bool drawGraph = true;

        // Directions of travel and, for each one, the index of the direction to its right and left.
        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly int[] RightOf = { 3, 2, 0, 1 };
        private static readonly int[] LeftOf = { 2, 3, 1, 0 };

        private Node[] nodes;
        private int[] reservationOwner;
        private float[] reservationTime;
        private bool[] hasSignals;

        public float StopLineBack => stopLineBack;

        public int NodeCount
        {
            get
            {
                EnsureBuilt();
                return nodes.Length;
            }
        }

        public Node GetNode(int index)
        {
            EnsureBuilt();
            return nodes[index];
        }

        /// <summary>
        /// Number of nodes valid as vehicle spawn points for a network with the given axis
        /// counts, without building the graph. Mirrors the node-validity rule used when spawning
        /// vehicles (every entry qualifies; an exit only if it isn't the outer edge of the grid,
        /// i.e. it has an outgoing street segment): every intersection contributes 4 entries, plus
        /// 4 exits minus the ones that fall on the grid's outer boundary in that direction.
        /// </summary>
        public static int EstimateValidSpawnNodeCount(int axesXCount, int axesZCount)
        {
            return 8 * axesXCount * axesZCount - 2 * axesXCount - 2 * axesZCount;
        }

        private void Awake()
        {
            Build();
        }

        /// <summary>
        /// Sets the street axes without rebuilding the graph. Used by the city generator, which
        /// must place all traffic lights before calling <see cref="Build"/> so
        /// <see cref="AssignTrafficLights"/> can find them.
        /// </summary>
        public void SetAxes(float[] newAxesX, float[] newAxesZ)
        {
            axesX = newAxesX;
            axesZ = newAxesZ;
            useCustomShape = false;
            customBlockCells = null;
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): builds the graph over the fixed MaxGridSize canvas,
        /// but a street segment between two intersections only exists (i.e. <see cref="Neighbour"/>
        /// allows it) when it is adjacent to at least one real block in <paramref name="blockCells"/>.
        /// A street ending where a block has no neighbour in that direction is a dead end, handled
        /// exactly like today's outer-edge intersections (no outgoing exit that way). An
        /// intersection touching no real block at all gets no usable entry, so it is never offered
        /// as a vehicle spawn point.
        /// </summary>
        public void BuildFromBlockCells(IReadOnlyCollection<Vector2Int> blockCells)
        {
            customBlockCells = new HashSet<Vector2Int>(blockCells);
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

        private bool IsUsedIntersection(int i, int j)
        {
            return customBlockCells.Contains(new Vector2Int(i - 1, j - 1))
                || customBlockCells.Contains(new Vector2Int(i, j - 1))
                || customBlockCells.Contains(new Vector2Int(i - 1, j))
                || customBlockCells.Contains(new Vector2Int(i, j));
        }

        // Whether the street segment leading out of intersection (i, j) in direction k is
        // adjacent to at least one real block -- mirrors CityGeneratorGroundBuilder's dash/zebra
        // adjacency rule exactly, so the drawn markings and the drivable graph always agree.
        private bool IsStreetSegmentReal(int i, int j, int k)
        {
            switch (k)
            {
                case 0: return customBlockCells.Contains(new Vector2Int(i, j - 1)) || customBlockCells.Contains(new Vector2Int(i, j));
                case 1: return customBlockCells.Contains(new Vector2Int(i - 1, j - 1)) || customBlockCells.Contains(new Vector2Int(i - 1, j));
                case 2: return customBlockCells.Contains(new Vector2Int(i - 1, j)) || customBlockCells.Contains(new Vector2Int(i, j));
                default: return customBlockCells.Contains(new Vector2Int(i - 1, j - 1)) || customBlockCells.Contains(new Vector2Int(i, j - 1));
            }
        }

        private void EnsureBuilt()
        {
            if (nodes == null)
            {
                Build();
            }
        }

        /// <summary>Rebuilds the graph and re-matches the traffic lights in the scene.</summary>
        public void Build()
        {
            int nx = axesX.Length;
            int nz = axesZ.Length;
            nodes = new Node[nx * nz * 4 * 2];

            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        nodes[NodeIndex(i, j, k, true)] = new Node
                        {
                            Position = EntryPosition(i, j, k),
                            Direction = Dirs[k],
                            IsEntry = true,
                            Intersection = i * nz + j
                        };
                        nodes[NodeIndex(i, j, k, false)] = new Node
                        {
                            Position = ExitPosition(i, j, k),
                            Direction = Dirs[k],
                            IsEntry = false,
                            Intersection = i * nz + j
                        };
                    }
                }
            }

            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        Node entry = nodes[NodeIndex(i, j, k, true)];
                        Node exit = nodes[NodeIndex(i, j, k, false)];

                        // Street segment: from this crossing's exit to the next crossing's entry.
                        if (Neighbour(i, j, k, out int si, out int sj))
                        {
                            exit.Exits.Add(new Exit { Node = NodeIndex(si, sj, k, true), Weight = 1f });
                            entry.Exits.Add(new Exit
                            {
                                Node = NodeIndex(i, j, k, false),
                                Weight = RouteWeight(straightWeight, i, j, si, sj)
                            });
                        }

                        // Right turn: the entry is already on the exit lane.
                        int kr = RightOf[k];
                        if (Neighbour(i, j, kr, out int ri, out int rj))
                        {
                            entry.Exits.Add(new Exit
                            {
                                Node = NodeIndex(ri, rj, kr, true),
                                Weight = RouteWeight(turnWeight, i, j, ri, rj)
                            });
                        }

                        // Left turn: the crossing is traversed to the exit of the opposite lane.
                        int kl = LeftOf[k];
                        if (Neighbour(i, j, kl, out int li, out int lj))
                        {
                            entry.Exits.Add(new Exit
                            {
                                Node = NodeIndex(i, j, kl, false),
                                Weight = RouteWeight(turnWeight, i, j, li, lj)
                            });
                        }
                    }
                }
            }

            if (useCustomShape)
            {
                // An intersection touching no real block at all (fully outside the shape) must
                // never be offered as a vehicle spawn point -- BuildVehicles treats any IsEntry
                // node as spawn-safe regardless of its exit count.
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        if (IsUsedIntersection(i, j))
                            continue;

                        for (int k = 0; k < 4; k++)
                            nodes[NodeIndex(i, j, k, true)].IsEntry = false;
                    }
                }
            }

            AssignTrafficLights();

            reservationOwner = new int[nx * nz];
            reservationTime = new float[nx * nz];
            for (int i = 0; i < reservationOwner.Length; i++)
            {
                reservationOwner[i] = 0;
            }
        }

        /// <summary>
        /// Matches each crossing entry with the traffic light that regulates it: the one facing
        /// head-on the driver arriving with that direction of travel.
        /// </summary>
        private void AssignTrafficLights()
        {
            int nx = axesX.Length;
            int nz = axesZ.Length;
            TrafficLight[] lights = FindObjectsByType<TrafficLight>(FindObjectsInactive.Exclude);
            hasSignals = new bool[nx * nz];

            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    Vector3 centre = IntersectionPosition(i, j);
                    for (int k = 0; k < 4; k++)
                    {
                        TrafficLight best = null;
                        float bestDistance = float.MaxValue;

                        foreach (TrafficLight light in lights)
                        {
                            Vector3 facing = light.transform.forward;
                            if (Vector3.Dot(facing, Dirs[k]) > -0.9f)
                            {
                                continue;
                            }

                            float distance = Vector3.Distance(light.transform.position, centre);
                            if (distance < 14f && distance < bestDistance)
                            {
                                bestDistance = distance;
                                best = light;
                            }
                        }

                        nodes[NodeIndex(i, j, k, true)].Light = best;
                        if (best != null)
                        {
                            hasSignals[i * nz + j] = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Distance to the outermost ring of the grid: 0.5 for interior
        /// intersections and 1.5 for perimeter ones (with four axes per side).
        /// </summary>
        private float Ring(int i, int j)
        {
            float centreX = (axesX.Length - 1) * 0.5f;
            float centreZ = (axesZ.Length - 1) * 0.5f;
            return Mathf.Max(Mathf.Abs(i - centreX), Mathf.Abs(j - centreZ));
        }

        /// <summary>
        /// Corrects an exit's weight based on where it leads. Without this, traffic
        /// piles up on the perimeter: there, going straight is the only non-turning option,
        /// and it would end up circling the border instead of spreading through the city.
        /// </summary>
        private float RouteWeight(float baseWeight, int fromI, int fromJ, int toI, int toJ)
        {
            float from = Ring(fromI, fromJ);
            float to = Ring(toI, toJ);

            if (to < from)
            {
                return baseWeight * interiorBias;
            }

            if (to > from)
            {
                return baseWeight;
            }

            // Stays on the same ring: only penalized if that ring is the perimeter.
            return from > 1f ? baseWeight * borderPenalty : baseWeight;
        }

        /// <summary>Randomly picks the next node to travel to, weighting the exits.</summary>
        public int PickNextNode(int nodeIndex)
        {
            EnsureBuilt();
            List<Exit> exits = nodes[nodeIndex].Exits;
            if (exits.Count == 0)
            {
                return -1;
            }

            float total = 0f;
            for (int i = 0; i < exits.Count; i++)
            {
                total += exits[i].Weight;
            }

            float pick = Random.value * total;
            for (int i = 0; i < exits.Count; i++)
            {
                pick -= exits[i].Weight;
                if (pick <= 0f)
                {
                    return exits[i].Node;
                }
            }

            return exits[exits.Count - 1].Node;
        }

        /// <summary>Closest node ahead that travels in the same direction as the vehicle.</summary>
        public int FindNodeAhead(Vector3 position, Vector3 forward)
        {
            EnsureBuilt();
            forward.y = 0f;
            forward.Normalize();

            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (Vector3.Dot(nodes[i].Direction, forward) < 0.7f)
                {
                    continue;
                }

                Vector3 to = nodes[i].Position - position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance < 0.01f || Vector3.Dot(to / distance, forward) < 0.5f)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        public bool HasGreen(int nodeIndex)
        {
            EnsureBuilt();
            TrafficLight light = nodes[nodeIndex].Light;
            return light == null || light.State == TrafficLightState.Green;
        }

        public TrafficLightState? LightState(int nodeIndex)
        {
            EnsureBuilt();
            TrafficLight light = nodes[nodeIndex].Light;
            return light == null ? (TrafficLightState?)null : light.State;
        }

        public bool HasSignals(int intersection)
        {
            EnsureBuilt();
            return hasSignals[intersection];
        }

        /// <summary>
        /// Whether the given intersection's light is green for the requested axis of travel
        /// (true for X/east-west, false for Z/north-south). Lets PedestrianNetwork read a
        /// crossing's light without re-scanning the scene for TrafficLight instances itself.
        /// </summary>
        public bool IsAxisGreen(TrafficLightIntersection intersection, bool axisIsX)
        {
            return AxisState(intersection, axisIsX) == TrafficLightState.Green;
        }

        /// <summary>
        /// Raw light state for the given intersection/axis. PedestrianNetwork.CanCross needs this
        /// rather than just IsAxisGreen: a pedestrian may only step onto the crossing once traffic
        /// is fully stopped (Red) — Amber still has cars moving/braking through the intersection,
        /// so "not green" alone is not safe to cross on.
        /// </summary>
        public TrafficLightState AxisState(TrafficLightIntersection intersection, bool axisIsX)
        {
            return axisIsX ? intersection.EastWestState : intersection.NorthSouthState;
        }

        public Vector3 IntersectionCentre(int intersection)
        {
            EnsureBuilt();
            int nz = axesZ.Length;
            return IntersectionPosition(intersection / nz, intersection % nz);
        }

        /// <summary>
        /// Priority at unsignalled crossings: only one vehicle can cross at a time.
        /// Returns true if the requester has right of way.
        ///
        /// The owner does not refresh its timestamp when re-claiming it: the timer
        /// runs from when it was granted. Otherwise a vehicle that gets stuck
        /// next to the crossing would hold it forever — it just had to keep asking every
        /// frame — causing a mutual deadlock with the car it was itself blocking.
        /// </summary>
        public bool TryReserve(int intersection, int carId)
        {
            EnsureBuilt();
            int owner = reservationOwner[intersection];
            if (owner == carId)
            {
                return true;
            }

            if (owner == 0 || Time.time - reservationTime[intersection] > reservationTimeout)
            {
                reservationOwner[intersection] = carId;
                reservationTime[intersection] = Time.time;
                return true;
            }

            return false;
        }

        /// <summary>Current owner of a crossing's priority, or 0 if free. For debugging.</summary>
        public int ReservationOwner(int intersection)
        {
            EnsureBuilt();
            return reservationOwner[intersection];
        }

        public void Release(int intersection, int carId)
        {
            EnsureBuilt();
            if (reservationOwner[intersection] == carId)
            {
                reservationOwner[intersection] = 0;
            }
        }

        private int NodeIndex(int i, int j, int direction, bool entry)
        {
            int nz = axesZ.Length;
            return (((i * nz + j) * 4 + direction) * 2) + (entry ? 0 : 1);
        }

        private Vector3 IntersectionPosition(int i, int j) => new Vector3(axesX[i], 0f, axesZ[j]);

        // Unit vector to the right of the direction of travel (cross(up, dir)).
        private static Vector3 RightOfDir(int k) => new Vector3(Dirs[k].z, 0f, -Dirs[k].x);

        private Vector3 EntryPosition(int i, int j, int k)
            => IntersectionPosition(i, j) + RightOfDir(k) * laneOffset - Dirs[k] * laneOffset;

        private Vector3 ExitPosition(int i, int j, int k)
            => IntersectionPosition(i, j) + RightOfDir(k) * laneOffset + Dirs[k] * laneOffset;

        private bool Neighbour(int i, int j, int k, out int ni, out int nj)
        {
            ni = i;
            nj = j;
            switch (k)
            {
                case 0: ni = i + 1; break;
                case 1: ni = i - 1; break;
                case 2: nj = j + 1; break;
                default: nj = j - 1; break;
            }

            if (ni < 0 || ni >= axesX.Length || nj < 0 || nj >= axesZ.Length)
            {
                return false;
            }

            return !useCustomShape || IsStreetSegmentReal(i, j, k);
        }

        /// <summary>Point where a vehicle arriving at this crossing entry stops.</summary>
        public Vector3 StopLinePosition(int nodeIndex)
        {
            EnsureBuilt();
            Node node = nodes[nodeIndex];
            return node.Position - node.Direction * stopLineBack;
        }

        private void OnDrawGizmosSelected()
        {
            // Deliberately does not EnsureBuilt(): selecting the object in the Editor before
            // Play (or before the generator has built anything) would otherwise construct the
            // whole graph just to draw gizmos. Nothing to draw yet in that case.
            if (!drawGraph || nodes == null)
            {
                return;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                Node node = nodes[i];
                Gizmos.color = node.IsEntry ? new Color(1f, 0.6f, 0.1f) : new Color(0.2f, 0.8f, 1f);
                Gizmos.DrawSphere(node.Position + Vector3.up * 0.3f, 0.35f);

                Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
                foreach (Exit exit in node.Exits)
                {
                    Gizmos.DrawLine(node.Position + Vector3.up * 0.3f,
                        nodes[exit.Node].Position + Vector3.up * 0.3f);
                }
            }
        }
    }
}
