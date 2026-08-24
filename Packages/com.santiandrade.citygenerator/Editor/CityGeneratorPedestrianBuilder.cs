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
        // Same diagonal arrangement as CityGeneratorPlazaBuilder's own BenchOffsets (not shared
        // directly: that field is private, and duplicating four Vector2 literals built from the
        // same public constant is cheaper than exposing internal layout details across builders).
        private static readonly Vector2[] BenchOffsets =
        {
            new(CityGeneratorConstants.PlazaBenchRadius, CityGeneratorConstants.PlazaBenchRadius),
            new(CityGeneratorConstants.PlazaBenchRadius, -CityGeneratorConstants.PlazaBenchRadius),
            new(-CityGeneratorConstants.PlazaBenchRadius, -CityGeneratorConstants.PlazaBenchRadius),
            new(-CityGeneratorConstants.PlazaBenchRadius, CityGeneratorConstants.PlazaBenchRadius),
        };

        private static readonly Vector2[] CenterpieceRingOffsets =
        {
            new(CityGeneratorConstants.PlazaCenterpieceRingRadius, CityGeneratorConstants.PlazaCenterpieceRingRadius),
            new(CityGeneratorConstants.PlazaCenterpieceRingRadius, -CityGeneratorConstants.PlazaCenterpieceRingRadius),
            new(-CityGeneratorConstants.PlazaCenterpieceRingRadius, -CityGeneratorConstants.PlazaCenterpieceRingRadius),
            new(-CityGeneratorConstants.PlazaCenterpieceRingRadius, CityGeneratorConstants.PlazaCenterpieceRingRadius),
        };

        /// <summary>
        /// Adds the <see cref="PedestrianNetwork"/> component to the "PedestrianNetwork" group and
        /// sets its axes, mirroring <see cref="CityGeneratorTrafficBuilder.AddNetworkComponent"/>.
        /// Does not build the graph yet: call <see cref="PedestrianNetwork.Build"/> only after the
        /// traffic lights exist (it scans the scene for TrafficLightIntersection) and, ideally,
        /// after TrafficNetwork.Build() so CanCross can resolve real light states right away.
        /// </summary>
        public static PedestrianNetwork AddNetworkComponent(Transform pedestrianNetworkGroup, int gridWidth, int gridHeight)
        {
            var network = pedestrianNetworkGroup.gameObject.AddComponent<PedestrianNetwork>();
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

        /// <summary>Adds the PedestrianManager that ticks every generated PedestrianAgent from one central Update. Only called when pedestrians are actually generated.</summary>
        public static void AddManagerComponent(Transform pedestrianNetworkGroup)
        {
            pedestrianNetworkGroup.gameObject.AddComponent<PedestrianManager>();
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
        public static List<GameObject> BuildPedestrians(List<PedestrianEntry> pedestrians, int pedestrianCount, PedestrianNetwork network, Transform pedestriansGroup, System.Random random)
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

                    if (pedestrianLayer >= 0)
                        instance.layer = pedestrianLayer;

                    PedestrianAgent agent = instance.GetComponent<PedestrianAgent>();
                    if (agent == null)
                        agent = instance.AddComponent<PedestrianAgent>();

                    var serializedAgent = new SerializedObject(agent);
                    serializedAgent.FindProperty("network").objectReferenceValue = network;
                    serializedAgent.ApplyModifiedPropertiesWithoutUndo();

                    Animator animator = instance.GetComponent<Animator>();
                    if (animator != null)
                        animator.cullingMode = AnimatorCullingMode.CullCompletely;

                    CityGeneratorColliderUtility.EnsureNonTriggerCollider(instance);

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
        /// Adds a bench-radial + short centerpiece-loop of PointOfInterest nodes to every plaza
        /// block, wired into the already-built ring: 4 nodes near the benches (LookAt the block
        /// centre, matching how the bench itself faces inward), each linked to the ring corner in
        /// its own diagonal and to the corresponding node of a short loop around the centerpiece —
        /// mirrors CityGeneratorPlazaBuilder's own bench/centerpiece placement.
        /// </summary>
        public static void RegisterPointsOfInterest(PedestrianNetwork network, PlazaSettings plazaSettings, IReadOnlyList<BlockCell> blocks)
        {
            if (plazaSettings.benchPrefab == null && plazaSettings.centerpiecePrefab == null)
                return;

            foreach (BlockCell block in blocks)
            {
                if (!block.isPlaza)
                    continue;

                int[] benchNodes = null;
                if (plazaSettings.benchPrefab != null)
                {
                    benchNodes = new int[BenchOffsets.Length];
                    for (int i = 0; i < BenchOffsets.Length; i++)
                    {
                        Vector2 offset = BenchOffsets[i];
                        Vector3 benchPos = block.center + new Vector3(offset.x, CityGeneratorConstants.GroundDatumY, offset.y);

                        int poi = network.AddNode(benchPos, PedestrianNodeKind.PointOfInterest, block.center);
                        int corner = network.FindNearestNode(benchPos, PedestrianNodeKind.Ring);
                        if (corner >= 0)
                            network.Connect(poi, corner);

                        benchNodes[i] = poi;
                    }
                }

                if (plazaSettings.centerpiecePrefab != null)
                {
                    var ringNodes = new int[CenterpieceRingOffsets.Length];
                    for (int i = 0; i < CenterpieceRingOffsets.Length; i++)
                    {
                        Vector2 offset = CenterpieceRingOffsets[i];
                        Vector3 pos = block.center + new Vector3(offset.x, CityGeneratorConstants.GroundDatumY, offset.y);
                        ringNodes[i] = network.AddNode(pos, PedestrianNodeKind.PointOfInterest, block.center);
                    }

                    for (int i = 0; i < ringNodes.Length; i++)
                        network.Connect(ringNodes[i], ringNodes[(i + 1) % ringNodes.Length]);

                    if (benchNodes != null)
                    {
                        for (int i = 0; i < ringNodes.Length; i++)
                            network.Connect(ringNodes[i], benchNodes[i]);
                    }
                }
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
