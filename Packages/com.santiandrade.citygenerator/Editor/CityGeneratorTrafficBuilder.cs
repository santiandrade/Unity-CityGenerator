using System.Collections.Generic;
using System.Linq;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds the traffic network graph, the traffic lights at every fully-interior
    /// intersection, and the vehicle instances distributed across the network's nodes.
    /// </summary>
    internal static class CityGeneratorTrafficBuilder
    {
        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        /// <summary>
        /// Adds the <see cref="TrafficNetwork"/> component to the "TrafficNetwork" group and sets
        /// its axes. Does not build the graph yet: call <see cref="TrafficNetwork.Build"/> only
        /// after all traffic lights have been instantiated, since it looks them up by scanning
        /// the scene.
        /// </summary>
        public static TrafficNetwork AddNetworkComponent(Transform trafficNetworkGroup, int gridWidth, int gridHeight)
        {
            var network = trafficNetworkGroup.gameObject.AddComponent<TrafficNetwork>();
            network.SetAxes(BuildAxes(gridWidth), BuildAxes(gridHeight));
            return network;
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): adds the component without setting axes yet -- the
        /// caller must call <see cref="TrafficNetwork.BuildFromBlockCells"/> directly (instead of
        /// <see cref="TrafficNetwork.Build"/>) once all traffic lights exist, since that method
        /// both sets the axes and builds the graph in one call.
        /// </summary>
        public static TrafficNetwork AddNetworkComponent(Transform trafficNetworkGroup)
        {
            return trafficNetworkGroup.gameObject.AddComponent<TrafficNetwork>();
        }

        private static float[] BuildAxes(int gridCount)
        {
            var axes = new float[gridCount + 1];
            for (int i = 0; i <= gridCount; i++)
                axes[i] = CityGeneratorGrid.GetStreetAxisPosition(gridCount, i);
            return axes;
        }

        /// <summary>
        /// Adds the <see cref="TrafficManager"/> that ticks every generated <see cref="CarAgent"/>
        /// from one central Update instead of each car's own (see the technical review, A.7), and
        /// wires it into <paramref name="network"/> (same GameObject) so CarAgent can resolve it
        /// via <see cref="TrafficNetwork.Manager"/> instead of a global static Instance. Only
        /// called when traffic is actually generated.
        /// </summary>
        public static void AddManagerComponent(Transform trafficNetworkGroup, TrafficNetwork network)
        {
            var manager = trafficNetworkGroup.gameObject.AddComponent<TrafficManager>();
            var laneOccupancy = trafficNetworkGroup.gameObject.AddComponent<TrafficLaneOccupancy>();

            var serialized = new SerializedObject(network);
            serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.FindProperty("laneOccupancy").objectReferenceValue = laneOccupancy;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Minimum number of real arms that makes an intersection a signalled one: a full 4-way,
        /// or a T-intersection along the shape's own border, is a real decision point where one
        /// flow must yield to another. A perimeter corner (exactly 2 perpendicular arms, a street
        /// simply bending 90 degrees with only one possible way through) never needs a light.
        /// </summary>
        internal const int SignalledIntersectionMinArms = 3;

        /// <summary>
        /// Fills <paramref name="armReal"/> (length 4, indexed like <see cref="Dirs"/>) for the
        /// intersection at grid coordinates (<paramref name="i"/>, <paramref name="j"/>) of a
        /// rectangular grid, and returns how many of its arms are real.
        /// </summary>
        internal static int CountRealArms(int gridWidth, int gridHeight, int i, int j, bool[] armReal)
        {
            armReal[0] = i < gridWidth;
            armReal[1] = i > 0;
            armReal[2] = j < gridHeight;
            armReal[3] = j > 0;
            return (armReal[0] ? 1 : 0) + (armReal[1] ? 1 : 0) + (armReal[2] ? 1 : 0) + (armReal[3] ? 1 : 0);
        }

        /// <summary>Custom Grid counterpart of <see cref="CountRealArms(int, int, int, int, bool[])"/>.</summary>
        internal static int CountRealArms(HashSet<Vector2Int> cellSet, int i, int j, bool[] armReal)
        {
            int realArmCount = 0;
            for (int k = 0; k < 4; k++)
            {
                armReal[k] = IsStreetSegmentReal(cellSet, i, j, k);
                if (armReal[k])
                    realArmCount++;
            }
            return realArmCount;
        }

        /// <summary>
        /// True when the rectangular grid contains at least one intersection that
        /// <see cref="BuildTrafficLights(GameObject, Transform, int, int, System.Random)"/> would
        /// signal, i.e. one with at least <see cref="SignalledIntersectionMinArms"/> real arms.
        /// This is the single predicate CityGeneratorValidator shares with the builder: the two
        /// once disagreed (the validator asked for a Traffic Light prefab only on a grid larger
        /// than 1x1, while the builder already signalled the T-intersections of a 1xN/Nx1 grid),
        /// so a 1x2 city with no prefab passed validation and then instantiated a null prefab.
        /// </summary>
        internal static bool HasSignalledIntersection(int gridWidth, int gridHeight)
        {
            var armReal = new bool[4];
            for (int i = 0; i <= gridWidth; i++)
            {
                for (int j = 0; j <= gridHeight; j++)
                {
                    if (CountRealArms(gridWidth, gridHeight, i, j, armReal) >= SignalledIntersectionMinArms)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Custom Grid counterpart of <see cref="HasSignalledIntersection(int, int)"/>.</summary>
        internal static bool HasSignalledIntersection(IReadOnlyCollection<Vector2Int> blockCells)
        {
            if (blockCells == null || blockCells.Count == 0)
                return false;

            var cellSet = new HashSet<Vector2Int>(blockCells);
            var armReal = new bool[4];
            int canvas = CityGeneratorConstants.MaxGridSize;
            for (int i = 0; i <= canvas; i++)
            {
                for (int j = 0; j <= canvas; j++)
                {
                    if (CountRealArms(cellSet, i, j, armReal) >= SignalledIntersectionMinArms)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Places lights at every intersection with at least 3 real arms (a full 4-way, always
        /// true for a strictly interior intersection, or a T-intersection along the grid's own
        /// border) -- a real decision point where one flow must yield to another. A perimeter
        /// corner (exactly 2 perpendicular arms, a street simply bending 90 degrees with only one
        /// possible way through) never needs one. Only the arms that actually exist within the
        /// grid get a physical light instantiated. Mirrors CityGeneratorGroundBuilder's identical
        /// rule for zebra crossings/dash exclusion, so the drawn markings and the signalled set
        /// always agree.
        /// </summary>
        public static List<GameObject> BuildTrafficLights(GameObject trafficLightPrefab, Transform trafficLightsGroup, int gridWidth, int gridHeight, System.Random random)
        {
            var placed = new List<GameObject>();
            int intersectionIndex = 0;
            var armReal = new bool[4];

            for (int i = 0; i <= gridWidth; i++)
            {
                for (int j = 0; j <= gridHeight; j++)
                {
                    if (CountRealArms(gridWidth, gridHeight, i, j, armReal) < SignalledIntersectionMinArms)
                        continue;

                    Vector3 centre = new(
                        CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i), 0f,
                        CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j));

                    Transform intersectionGroup = GetOrCreateGroup(trafficLightsGroup, $"Intersection_{i}_{j}");
                    var lights = new TrafficLight[4];

                    for (int k = 0; k < 4; k++)
                    {
                        if (!armReal[k])
                            continue;

                        Vector3 corner = (Dirs[k] + RightOfDir(Dirs[k])) * CityGeneratorConstants.TrafficLightCornerOffset;
                        Vector3 position = centre + corner;
                        position.y = CityGeneratorConstants.GroundDatumY;
                        Quaternion rotation = Quaternion.LookRotation(-Dirs[k], Vector3.up);

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(trafficLightPrefab, intersectionGroup);
                        instance.name = "TrafficLight_" + k;
                        instance.transform.position = position;
                        instance.transform.rotation = rotation;

                        placed.Add(instance);
                        lights[k] = instance.GetComponent<TrafficLight>();
                    }

                    var intersectionComponent = intersectionGroup.gameObject.AddComponent<TrafficLightIntersection>();
                    WireIntersection(intersectionComponent, lights, intersectionIndex, random);
                    intersectionIndex++;
                }
            }

            return placed;
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): places lights at every intersection with at least 3
        /// real arms (a full 4-way or a T-intersection) -- a real decision point where one flow
        /// must yield to another. A plain straight-through point (2 opposite arms) or a
        /// perpendicular L-corner (exactly 2 arms, a single street simply bending 90 degrees, so a
        /// car arriving there has only one possible way to continue) never needs one, over the
        /// fixed MaxGridSize canvas. Only the arms that actually exist get a physical light
        /// instantiated; the same rule is mirrored by CityGeneratorGroundBuilder for zebra
        /// crossings/dash exclusion, so the drawn markings and the signalled set always agree.
        /// </summary>
        public static List<GameObject> BuildTrafficLights(GameObject trafficLightPrefab, Transform trafficLightsGroup, IReadOnlyCollection<Vector2Int> blockCells, System.Random random)
        {
            var cellSet = new HashSet<Vector2Int>(blockCells);
            var placed = new List<GameObject>();
            int intersectionIndex = 0;
            int canvas = CityGeneratorConstants.MaxGridSize;
            var armReal = new bool[4];

            for (int i = 0; i <= canvas; i++)
            {
                for (int j = 0; j <= canvas; j++)
                {
                    if (CountRealArms(cellSet, i, j, armReal) < SignalledIntersectionMinArms)
                        continue;

                    Vector3 centre = new(
                        CityGeneratorGrid.GetStreetAxisPosition(canvas, i), 0f,
                        CityGeneratorGrid.GetStreetAxisPosition(canvas, j));

                    Transform intersectionGroup = GetOrCreateGroup(trafficLightsGroup, $"Intersection_{i}_{j}");
                    var lights = new TrafficLight[4];

                    for (int k = 0; k < 4; k++)
                    {
                        if (!armReal[k])
                            continue;

                        Vector3 corner = (Dirs[k] + RightOfDir(Dirs[k])) * CityGeneratorConstants.TrafficLightCornerOffset;
                        Vector3 position = centre + corner;
                        position.y = CityGeneratorConstants.GroundDatumY;
                        Quaternion rotation = Quaternion.LookRotation(-Dirs[k], Vector3.up);

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(trafficLightPrefab, intersectionGroup);
                        instance.name = "TrafficLight_" + k;
                        instance.transform.position = position;
                        instance.transform.rotation = rotation;

                        placed.Add(instance);
                        lights[k] = instance.GetComponent<TrafficLight>();
                    }

                    var intersectionComponent = intersectionGroup.gameObject.AddComponent<TrafficLightIntersection>();
                    WireIntersection(intersectionComponent, lights, intersectionIndex, random);
                    intersectionIndex++;
                }
            }

            return placed;
        }

        // Mirrors TrafficNetwork.IsStreetSegmentReal / CityGeneratorGroundBuilder.HasCrossTraffic's
        // per-arm building block exactly, so the placed lights, the drawn markings and the
        // drivable graph all agree on which arms of an intersection are real.
        private static bool IsStreetSegmentReal(HashSet<Vector2Int> cells, int i, int j, int k)
        {
            switch (k)
            {
                case 0: return cells.Contains(new Vector2Int(i, j - 1)) || cells.Contains(new Vector2Int(i, j));
                case 1: return cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i - 1, j));
                case 2: return cells.Contains(new Vector2Int(i - 1, j)) || cells.Contains(new Vector2Int(i, j));
                default: return cells.Contains(new Vector2Int(i - 1, j - 1)) || cells.Contains(new Vector2Int(i, j - 1));
            }
        }

        private static void WireIntersection(TrafficLightIntersection component, TrafficLight[] lights, int intersectionIndex, System.Random random)
        {
            var serialized = new SerializedObject(component);
            SetLightList(serialized, "eastWest", lights[0], lights[1]);
            SetLightList(serialized, "northSouth", lights[2], lights[3]);
            serialized.FindProperty("startOffset").floatValue = (float)random.NextDouble() * CityGeneratorConstants.TrafficLightStartOffsetMax;
            serialized.FindProperty("startWithNorthSouth").boolValue = intersectionIndex % 2 == 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLightList(SerializedObject serialized, string fieldName, params TrafficLight[] values)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static Vector3 RightOfDir(Vector3 dir) => new(dir.z, 0f, -dir.x);

        /// <summary>
        /// Creates the <see cref="CityGeneratorConstants.VehicleLayerName"/> layer in this project
        /// if it doesn't already exist, using the first free slot from
        /// <see cref="CityGeneratorConstants.FirstUserLayerIndex"/> up. Requiring the user to create
        /// this layer by hand was a recurring source of confusion (SPEC 02 verification), and the
        /// tool can do it itself the same way any Editor script would: writing directly to
        /// <c>ProjectSettings/TagManager.asset</c> via <see cref="SerializedObject"/>. If every slot
        /// is already taken, warns instead of failing — <see cref="BuildVehicles"/> then leaves
        /// every vehicle's forward sensor mask empty rather than falling back to whatever layer the
        /// instance happens to share with unrelated scene geometry, so vehicles simply won't detect
        /// each other until a slot frees up.
        /// </summary>
        private static void EnsureVehicleLayerExists()
        {
            if (LayerMask.NameToLayer(CityGeneratorConstants.VehicleLayerName) >= 0)
                return;

            Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets.Length == 0)
                return;

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = CityGeneratorConstants.FirstUserLayerIndex; i < layers.arraySize; i++)
            {
                SerializedProperty layerSlot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layerSlot.stringValue))
                {
                    layerSlot.stringValue = CityGeneratorConstants.VehicleLayerName;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[City Generator] Created layer '{CityGeneratorConstants.VehicleLayerName}' at slot {i} (Project Settings > Tags and Layers) so vehicles can detect each other.");
                    return;
                }
            }

            Debug.LogWarning($"[City Generator] Could not auto-create the '{CityGeneratorConstants.VehicleLayerName}' layer: every user layer slot ({CityGeneratorConstants.FirstUserLayerIndex}-31) is already in use. Free one up in Project Settings > Tags and Layers — until then, vehicles won't detect each other (they'll still stop for lights and unsignalled-crossing priority).");
        }

        /// <summary>
        /// Distributes <paramref name="vehicleCount"/> vehicles across the entries in
        /// <paramref name="vehicles"/> by their configured percentage, and spawns each one at a
        /// distinct (shuffled) node of the already-built <paramref name="network"/> so it starts
        /// exactly on a lane, facing the lane's direction.
        /// </summary>
        public static List<GameObject> BuildVehicles(List<VehicleEntry> vehicles, int vehicleCount, TrafficNetwork network, Transform vehiclesGroup, System.Random random)
        {
            var placed = new List<GameObject>();
            if (vehicleCount <= 0 || vehicles.Count == 0 || network == null)
                return placed;

            int nodeCount = network.NodeCount;
            if (nodeCount == 0)
                return placed;

            // Exit nodes at the outer edge of the grid (facing off the map, no street segment
            // beyond them) have no outgoing exits and nothing ahead of them: a vehicle spawned
            // there fails CarAgent's initial FindNodeAhead and disables itself. Entries are
            // always safe (their own intersection's exit is always ahead of them).
            //
            // An entry node and the exit node of the direction to its right sit at the exact
            // same world position (TrafficNetwork skips an intermediate node for right turns),
            // so without deduplication two vehicles could each land on a distinct node index yet
            // spawn stacked on top of each other. Keep only one candidate per physical position.
            List<int> nodeOrder = Enumerable.Range(0, nodeCount)
                .Where(i =>
                {
                    TrafficNetwork.Node node = network.GetNode(i);
                    return node.IsEntry || node.Exits.Count > 0;
                })
                .GroupBy(i => network.GetNode(i).Position)
                .Select(g => g.First())
                .ToList();
            CityGeneratorRandomUtility.Shuffle(nodeOrder, random);

            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(vehicles, vehicleCount, v => v.percentage);
            EnsureVehicleLayerExists();
            int vehicleLayer = LayerMask.NameToLayer(CityGeneratorConstants.VehicleLayerName);

            for (int p = 0; p < vehicles.Count; p++)
            {
                GameObject prefab = vehicles[p].prefab;
                for (int c = 0; c < counts[p]; c++)
                {
                    TrafficNetwork.Node node = network.GetNode(nodeOrder[placed.Count % nodeOrder.Count]);

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, vehiclesGroup);
                    instance.name = $"{prefab.name}_{placed.Count}";
                    instance.transform.position = node.Position;
                    instance.transform.rotation = Quaternion.LookRotation(node.Direction, Vector3.up);

                    CarAgent carAgent = instance.GetComponent<CarAgent>();
                    if (carAgent == null)
                        carAgent = instance.AddComponent<CarAgent>();

                    // Injected here instead of each car finding it via FindFirstObjectByType in
                    // Start (see the technical review, A.7).
                    var serializedAgent = new SerializedObject(carAgent);
                    serializedAgent.FindProperty("network").objectReferenceValue = network;

                    // The Vehicle layer is assigned only to the sensor proxy collider's own
                    // GameObject (the instance root, never the user prefab's own colliders,
                    // wherever they sit in the hierarchy) — see CityGeneratorColliderUtility.
                    Collider proxyCollider = CityGeneratorColliderUtility.EnsureNonTriggerCollider(instance);
                    if (vehicleLayer >= 0)
                        proxyCollider.gameObject.layer = vehicleLayer;

                    // vehicleMask must match the proxy's layer exactly, not whatever LayerMask the
                    // prefab happened to be authored with: if the 'Vehicle' layer sits at a
                    // different index in this project, a mask baked for a different index would
                    // make the forward sensor miss every other vehicle. When EnsureVehicleLayerExists
                    // couldn't create the layer at all (every slot taken), there's no layer of the
                    // vehicles' own to match — mask stays 0 (Nothing) rather than falling back to
                    // whatever layer the instance happens to sit on, which could just as easily be
                    // shared with unrelated scene geometry. Cars simply don't brake for each other
                    // in that case; they still stop for lights and unsignalled-crossing priority.
                    serializedAgent.FindProperty("vehicleMask").intValue = vehicleLayer >= 0 ? 1 << proxyCollider.gameObject.layer : 0;
                    serializedAgent.ApplyModifiedPropertiesWithoutUndo();

                    placed.Add(instance);
                }
            }

            return placed;
        }

        private static Transform GetOrCreateGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            var group = new GameObject(name).transform;
            group.SetParent(parent);
            return group;
        }
    }
}
