using System.Collections.Generic;
using System.Linq;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds the pedestrian network graph and the NPC instances distributed across its Ring
    /// nodes. Mirrors <see cref="CityGeneratorTrafficBuilder"/>.
    /// </summary>
    internal static class CityGeneratorPedestrianBuilder
    {
        /// <summary>
        /// Adds the <see cref="PedestrianNetwork"/> component to the "PedestrianNetwork" group and
        /// sets its axes, mirroring <see cref="CityGeneratorTrafficBuilder.AddNetworkComponent"/>.
        /// Does not build the graph yet: call <see cref="PedestrianNetwork.Build"/> only after the
        /// traffic lights exist (it scans the scene for TrafficLightIntersection) and, ideally,
        /// after TrafficNetwork.Build() so CanCross can resolve real light states right away.
        /// </summary>
        public static PedestrianNetwork AddNetworkComponent(Transform pedestrianNetworkGroup, int gridWidth, int gridHeight, IReadOnlyList<BlockCell> blocks, HashSet<(int gridX, int gridY, int slot)> reservedSlots)
        {
            var network = pedestrianNetworkGroup.gameObject.AddComponent<PedestrianNetwork>();
            network.SetAxes(BuildAxes(gridWidth), BuildAxes(gridHeight));

            // Flattened [bi, bj] -> flag, index = bi * gridHeight + bj (bi == gridX, bj == gridY,
            // gridHeight == blocksZ), matching PedestrianNetwork's own flattening convention.
            var isPlaza = new bool[gridWidth * gridHeight];
            var isFullyReserved = new bool[gridWidth * gridHeight];
            foreach (BlockCell block in blocks)
            {
                int index = block.gridX * gridHeight + block.gridY;
                isPlaza[index] = block.isPlaza;
                isFullyReserved[index] = reservedSlots.Contains((block.gridX, block.gridY, -1));
            }

            var serialized = new SerializedObject(network);
            ApplyBoolArray(serialized.FindProperty("blockIsPlaza"), isPlaza);
            ApplyBoolArray(serialized.FindProperty("blockIsFullyReserved"), isFullyReserved);
            serialized.FindProperty("plazaGridStep").floatValue = CityGeneratorConstants.PlazaGridStep;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return network;
        }

        private static void ApplyBoolArray(SerializedProperty property, bool[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).boolValue = values[i];
        }

        private static float[] BuildAxes(int gridCount)
        {
            var axes = new float[gridCount + 1];
            for (int i = 0; i <= gridCount; i++)
                axes[i] = CityGeneratorGrid.GetStreetAxisPosition(gridCount, i);
            return axes;
        }

        /// <summary>Adds the PedestrianManager that ticks every generated PedestrianAgent from one
        /// central Update, configured from <paramref name="settings"/>, and wires it into
        /// <paramref name="network"/> (same GameObject) so PedestrianAgent can resolve it via
        /// <see cref="PedestrianNetwork.Manager"/> instead of a global static Instance. Only called
        /// when pedestrians are actually generated.</summary>
        public static void AddManagerComponent(Transform pedestrianNetworkGroup, PedestrianNetwork network, CrowdSettings settings)
        {
            PedestrianManager manager = pedestrianNetworkGroup.gameObject.AddComponent<PedestrianManager>();
            PedestrianRoadProximityGrid roadProximity = pedestrianNetworkGroup.gameObject.AddComponent<PedestrianRoadProximityGrid>();

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("staggerMinAgentCount").intValue = settings.staggerMinAgentCount;
            serialized.FindProperty("staggerDistance").floatValue = settings.staggerDistance;
            serialized.FindProperty("staggerFrames").intValue = settings.staggerFrames;
            serialized.FindProperty("cellSize").floatValue = settings.separationCellSize;
            serialized.FindProperty("separationRadius").floatValue = settings.separationRadius;
            serialized.FindProperty("separationStrength").floatValue = settings.separationStrength;
            serialized.FindProperty("playerAvoidanceRadius").floatValue = settings.playerAvoidanceRadius;
            serialized.FindProperty("playerAvoidanceStrength").floatValue = settings.playerAvoidanceStrength;
            serialized.FindProperty("roadProximityGrid").objectReferenceValue = roadProximity;
            serialized.FindProperty("network").objectReferenceValue = network;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var networkSerialized = new SerializedObject(network);
            networkSerialized.FindProperty("manager").objectReferenceValue = manager;
            networkSerialized.FindProperty("roadProximity").objectReferenceValue = roadProximity;
            networkSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// "Level 1" pruning: marks any node whose position falls inside an already-placed
        /// obstacle's XZ footprint (the same shared obstacles list every other category avoids)
        /// as Blocked, before a single pedestrian is spawned. Doesn't touch PrunePlacedObstacles'
        /// own runtime Physics-based pass (levels 2/3), which auto-repairs the graph later against
        /// the scene as it stands, including edits made after generation.
        /// </summary>
        public static void PruneNodesAgainstObstacles(PedestrianNetwork network, List<GameObject> obstacles, ObstacleCache cache)
        {
            for (int i = 0; i < network.NodeCount; i++)
            {
                Vector3 position = network.GetNode(i).Position;
                var point = new Vector2(position.x, position.z);

                for (int o = 0; o < obstacles.Count; o++)
                {
                    if (cache.GetRect(obstacles[o]).Contains(point))
                    {
                        network.SetBlocked(i, true);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Distributes <paramref name="pedestrianCount"/> NPCs across the entries in
        /// <paramref name="pedestrians"/> by their configured percentage, spawning each one at a
        /// distinct (shuffled) Ring node — mirrors <see cref="CityGeneratorTrafficBuilder.BuildVehicles"/>.
        /// </summary>
        public static List<GameObject> BuildPedestrians(List<PedestrianEntry> pedestrians, int pedestrianCount, PedestrianNetwork network, Transform pedestriansGroup, System.Random random, PedestrianBehaviourSettings behaviour)
        {
            var placed = new List<GameObject>();
            if (pedestrianCount <= 0 || pedestrians.Count == 0 || network == null)
                return placed;

            List<int> ringNodes = Enumerable.Range(0, network.NodeCount)
                .Where(i => network.GetNode(i).Kind == PedestrianNodeKind.Ring && !network.GetNode(i).Blocked)
                .ToList();
            if (ringNodes.Count == 0)
                return placed;

            CityGeneratorRandomUtility.Shuffle(ringNodes, random);

            int[] counts = CityGeneratorDistributionUtility.DistributePercentages(pedestrians, pedestrianCount, p => p.percentage);
            // Idempotent: EnsurePedestrianLayerAndAssignMask, called earlier whenever vehicles
            // exist (see CityGeneratorContentAssembler), may already have created this layer. Kept
            // here too so BuildPedestrians still works if ever called on its own.
            EnsurePedestrianLayerExists();
            int pedestrianLayer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);

            // Excludes the Pedestrian layer from the network's own obstacle-pruning check: without
            // this, PedestrianNetwork.Awake() rebuilding the graph in Play would see every
            // pedestrian's own collider sitting right on its spawn node and wrongly prune it (and
            // any neighbour whose only route ran through it) as if a building were there.
            if (pedestrianLayer >= 0)
            {
                var serializedNetwork = new SerializedObject(network);
                serializedNetwork.FindProperty("obstacleMask").intValue = ~(1 << pedestrianLayer);
                serializedNetwork.ApplyModifiedPropertiesWithoutUndo();
            }

            for (int p = 0; p < pedestrians.Count; p++)
            {
                GameObject prefab = pedestrians[p].prefab;
                for (int c = 0; c < counts[p]; c++)
                {
                    PedestrianNode node = network.GetNode(ringNodes[placed.Count % ringNodes.Count]);

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pedestriansGroup);
                    instance.name = $"{prefab.name}_{placed.Count}";
                    instance.transform.position = node.Position;
                    if (node.Neighbours.Count > 0)
                    {
                        Vector3 facing = network.GetNode(node.Neighbours[0]).Position - node.Position;
                        facing.y = 0f;
                        if (facing.sqrMagnitude > 0.0001f)
                            instance.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
                    }

                    PedestrianAgent agent = instance.GetComponent<PedestrianAgent>();
                    if (agent == null)
                        agent = instance.AddComponent<PedestrianAgent>();

                    var serializedAgent = new SerializedObject(agent);
                    serializedAgent.FindProperty("network").objectReferenceValue = network;
                    serializedAgent.FindProperty("spawnIndex").intValue = placed.Count;
                    serializedAgent.FindProperty("walkReferenceSpeed").floatValue = behaviour.walkReferenceSpeed;
                    serializedAgent.FindProperty("runReferenceSpeed").floatValue = behaviour.runReferenceSpeed;
                    serializedAgent.FindProperty("paceFraction").floatValue = behaviour.paceFraction;
                    serializedAgent.FindProperty("runnerChance").floatValue = behaviour.runnerChance;
                    serializedAgent.FindProperty("speedJitter").floatValue = behaviour.speedJitter;
                    serializedAgent.FindProperty("lateralJitter").floatValue = behaviour.lateralJitter;
                    serializedAgent.FindProperty("rotationSpeed").floatValue = behaviour.rotationSpeed;
                    serializedAgent.FindProperty("arriveRadius").floatValue = behaviour.arriveRadius;
                    serializedAgent.FindProperty("idleStopChance").floatValue = behaviour.idleStopChance;
                    serializedAgent.FindProperty("idleStopDurationMin").floatValue = behaviour.idleStopDurationMin;
                    serializedAgent.FindProperty("idleStopDurationMax").floatValue = behaviour.idleStopDurationMax;
                    serializedAgent.ApplyModifiedPropertiesWithoutUndo();

                    Animator animator = instance.GetComponent<Animator>();
                    if (animator != null)
                        animator.cullingMode = AnimatorCullingMode.CullCompletely;

                    // The Pedestrian layer is assigned only to the sensor proxy collider's own
                    // GameObject (the instance root, never the user prefab's own colliders,
                    // wherever they sit in the hierarchy) — see CityGeneratorColliderUtility.
                    Collider proxyCollider = CityGeneratorColliderUtility.EnsureNonTriggerCollider(instance);
                    if (pedestrianLayer >= 0)
                        proxyCollider.gameObject.layer = pedestrianLayer;

                    placed.Add(instance);
                }
            }

            return placed;
        }

        /// <summary>
        /// Ensures the Pedestrian layer exists (creating it if needed, same fail-closed fallback as
        /// EnsureVehicleLayerExists) and sets CarAgent.pedestrianMask on every already-placed
        /// vehicle to match it, mirroring how BuildVehicles matches vehicleMask to the actual
        /// Vehicle layer index instead of trusting whatever a prefab was authored with. Called
        /// whenever vehicles exist, independent of includePedestrians: CityGeneratorSceneBuilder
        /// puts the player on this same layer, so vehicles must detect it even when no NPC
        /// pedestrians are spawned.
        /// </summary>
        public static void EnsurePedestrianLayerAndAssignMask(Transform vehiclesGroup)
        {
            EnsurePedestrianLayerExists();
            int pedestrianLayer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);
            int mask = pedestrianLayer >= 0 ? 1 << pedestrianLayer : 0;

            foreach (CarAgent carAgent in vehiclesGroup.GetComponentsInChildren<CarAgent>())
            {
                var serialized = new SerializedObject(carAgent);
                serialized.FindProperty("pedestrianMask").intValue = mask;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Creates the CityGeneratorConstants.PedestrianLayerName layer if it doesn't already
        /// exist, exactly like EnsureVehicleLayerExists: same TagManager.asset write, same
        /// first-free-slot search, same fail-closed fallback (pedestrianMask left at 0, warned
        /// once) if every slot is taken.
        /// </summary>
        private static void EnsurePedestrianLayerExists()
        {
            if (LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName) >= 0)
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
                    layerSlot.stringValue = CityGeneratorConstants.PedestrianLayerName;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[City Generator] Created layer '{CityGeneratorConstants.PedestrianLayerName}' at slot {i} (Project Settings > Tags and Layers) so vehicles can detect pedestrians.");
                    return;
                }
            }

            Debug.LogWarning($"[City Generator] Could not auto-create the '{CityGeneratorConstants.PedestrianLayerName}' layer: every user layer slot ({CityGeneratorConstants.FirstUserLayerIndex}-31) is already in use. Free one up in Project Settings > Tags and Layers — until then, vehicles won't detect pedestrians.");
        }
    }
}
