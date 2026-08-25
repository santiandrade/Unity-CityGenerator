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

            var serialized = new SerializedObject(network);
            serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Places 4 traffic lights (one per arm) at every intersection fully surrounded by
        /// blocks (both axis indices strictly interior — the same set as the zebra crossings),
        /// wired into a <see cref="TrafficLightIntersection"/> that cycles east-west vs north-south.
        /// </summary>
        public static List<GameObject> BuildTrafficLights(GameObject trafficLightPrefab, Transform trafficLightsGroup, int gridWidth, int gridHeight, System.Random random)
        {
            var placed = new List<GameObject>();
            int intersectionIndex = 0;

            for (int i = 1; i < gridWidth; i++)
            {
                for (int j = 1; j < gridHeight; j++)
                {
                    Vector3 centre = new(
                        CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i), 0f,
                        CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j));

                    Transform intersectionGroup = GetOrCreateGroup(trafficLightsGroup, $"Intersection_{i}_{j}");
                    var lights = new TrafficLight[4];

                    for (int k = 0; k < 4; k++)
                    {
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
