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
                        Vector3 lateral = RightOfDir(Dirs[k]) * CityGeneratorConstants.TrafficLightLateralOffset;
                        Vector3 position = centre - Dirs[k] * CityGeneratorConstants.TrafficLightOffset + lateral;
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

            List<int> nodeOrder = Enumerable.Range(0, nodeCount).ToList();
            CityGeneratorRandomUtility.Shuffle(nodeOrder, random);

            int[] counts = DistributePercentages(vehicles, vehicleCount);
            int vehicleLayer = LayerMask.NameToLayer(CityGeneratorConstants.VehicleLayerName);
            if (vehicleLayer < 0)
                Debug.LogWarning($"[City Generator] Layer '{CityGeneratorConstants.VehicleLayerName}' not found in this project; vehicles were spawned on the Default layer instead.");

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

                    if (vehicleLayer >= 0)
                        instance.layer = vehicleLayer;

                    if (instance.GetComponent<CarAgent>() == null)
                        instance.AddComponent<CarAgent>();

                    placed.Add(instance);
                }
            }

            return placed;
        }

        private static int[] DistributePercentages(List<VehicleEntry> vehicles, int totalCount)
        {
            var counts = new int[vehicles.Count];
            var remainders = new float[vehicles.Count];
            int assigned = 0;

            for (int i = 0; i < vehicles.Count; i++)
            {
                float exact = totalCount * (vehicles[i].percentage / 100f);
                counts[i] = Mathf.FloorToInt(exact);
                remainders[i] = exact - counts[i];
                assigned += counts[i];
            }

            List<int> byRemainder = Enumerable.Range(0, vehicles.Count).OrderByDescending(i => remainders[i]).ToList();
            int remaining = totalCount - assigned;
            for (int i = 0; i < remaining; i++)
                counts[byRemainder[i % byRemainder.Count]]++;

            return counts;
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
