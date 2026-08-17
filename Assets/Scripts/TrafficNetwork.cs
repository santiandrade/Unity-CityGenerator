using System.Collections.Generic;
using UnityEngine;

namespace TestAI
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

        [Header("Trazado")]
        [Tooltip("Coordenadas de los ejes de calle, en orden ascendente. Valen para X y para Z.")]
        [SerializeField] private float[] axes = { -84f, -28f, 28f, 84f };

        [Tooltip("Separación del carril respecto al eje de la calle. Debe caber dentro de la calzada.")]
        [SerializeField] private float laneOffset = 2.6f;

        [Header("Circulación")]
        [Tooltip("Distancia por delante de la entrada del cruce donde se detiene un coche en rojo.")]
        [SerializeField] private float stopLineBack = 6.5f;

        [SerializeField] private float straightWeight = 2.5f;
        [SerializeField] private float turnWeight = 1f;

        [Tooltip("Multiplica el peso de las salidas que llevan hacia el interior de la retícula.")]
        [SerializeField] private float interiorBias = 1.8f;

        [Tooltip("Penaliza las salidas que mantienen al vehículo dando vueltas por las calles del perímetro.")]
        [SerializeField] private float borderPenalty = 0.35f;

        [Tooltip("Tiempo tras el que se libera la prioridad de un cruce sin semáforo si su dueño no ha pasado.")]
        [SerializeField] private float reservationTimeout = 4f;

        [Header("Depuración")]
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

        private void Awake()
        {
            Build();
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
            int n = axes.Length;
            nodes = new Node[n * n * 4 * 2];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        nodes[NodeIndex(i, j, k, true)] = new Node
                        {
                            Position = EntryPosition(i, j, k),
                            Direction = Dirs[k],
                            IsEntry = true,
                            Intersection = i * n + j
                        };
                        nodes[NodeIndex(i, j, k, false)] = new Node
                        {
                            Position = ExitPosition(i, j, k),
                            Direction = Dirs[k],
                            IsEntry = false,
                            Intersection = i * n + j
                        };
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
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

            AssignTrafficLights();

            reservationOwner = new int[n * n];
            reservationTime = new float[n * n];
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
            int n = axes.Length;
            TrafficLight[] lights = FindObjectsByType<TrafficLight>(FindObjectsSortMode.None);
            hasSignals = new bool[n * n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
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
                            hasSignals[i * n + j] = true;
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
            float centre = (axes.Length - 1) * 0.5f;
            return Mathf.Max(Mathf.Abs(i - centre), Mathf.Abs(j - centre));
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

        public Vector3 IntersectionCentre(int intersection)
        {
            EnsureBuilt();
            int n = axes.Length;
            return IntersectionPosition(intersection / n, intersection % n);
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
            int n = axes.Length;
            return (((i * n + j) * 4 + direction) * 2) + (entry ? 0 : 1);
        }

        private Vector3 IntersectionPosition(int i, int j) => new Vector3(axes[i], 0f, axes[j]);

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

            int n = axes.Length;
            return ni >= 0 && ni < n && nj >= 0 && nj < n;
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
            if (!drawGraph)
            {
                return;
            }

            EnsureBuilt();
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
