using System.Collections.Generic;
using System.Linq;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds Custom Pedestrian instances (SPEC 12): per valid entry, spreads `count` copies of
    /// `prefab` over `selectedNodeIndices`' Ring nodes and confines each spawned PedestrianAgent to
    /// that entry's node subset via PedestrianAgent.SetAllowedNodes. Mirrors
    /// <see cref="CityGeneratorPedestrianBuilder.BuildPedestrians"/>, but runs against the real
    /// <see cref="PedestrianNetwork"/> already built by that class -- never a preview instance --
    /// so the saved indices resolve to the same nodes the picker showed.
    /// </summary>
    internal static class CityGeneratorCustomPedestrianBuilder
    {
        public static List<GameObject> BuildCustomPedestrians(List<CustomPedestrianEntry> entries, PedestrianNetwork network, Transform pedestriansGroup, System.Random random, PedestrianBehaviourSettings behaviour)
        {
            var placed = new List<GameObject>();
            if (entries == null || entries.Count == 0 || network == null)
                return placed;

            int pedestrianLayer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);

            foreach (CustomPedestrianEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.title) || entry.prefab == null || entry.count < 1
                    || entry.selectedNodeIndices == null || entry.selectedNodeIndices.Count < 2)
                {
                    continue;
                }

                List<int> spawnCandidates = entry.selectedNodeIndices
                    .Where(i => i >= 0 && i < network.NodeCount && network.GetNode(i).Kind == PedestrianNodeKind.Ring && !network.GetNode(i).Blocked)
                    .ToList();
                if (spawnCandidates.Count == 0)
                    continue;

                CityGeneratorRandomUtility.Shuffle(spawnCandidates, random);
                var allowedNodes = new List<int>(entry.selectedNodeIndices);

                for (int c = 0; c < entry.count; c++)
                {
                    PedestrianNode node = network.GetNode(spawnCandidates[c % spawnCandidates.Count]);

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, pedestriansGroup);
                    instance.name = $"{entry.title}_{c}";
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
                    agent.SetAllowedNodes(allowedNodes);

                    CityGeneratorPedestrianBuilder.ApplyAnimatorCullingMode(instance);

                    // The Pedestrian layer is assigned only to the sensor proxy collider's own
                    // GameObject, same convention as BuildPedestrians/BuildVehicles.
                    Collider proxyCollider = CityGeneratorColliderUtility.EnsureNonTriggerCollider(instance);
                    if (pedestrianLayer >= 0)
                        proxyCollider.gameObject.layer = pedestrianLayer;

                    placed.Add(instance);
                }
            }

            return placed;
        }
    }
}
