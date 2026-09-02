using System;
using System.Collections.Generic;
using CityGenerator.Runtime;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds a disposable, hidden preview of the pedestrian graph for the current settings (SPEC
    /// 12), so the Custom Pedestrians node picker can show real node positions/adjacency before the
    /// city is ever generated. Reuses the exact same generation code the real pipeline uses --
    /// CityGeneratorTrafficBuilder's deterministic TrafficLightIntersection placement (no real
    /// light behaviour, no vehicles) plus a real <see cref="PedestrianNetwork"/>.Build()/
    /// BuildFromBlockCells() -- rather than a parallel reimplementation of the graph geometry, so
    /// the two can only diverge if the real pipeline changes the order it calls those pieces in
    /// (see CityGeneratorPedestrianPreview.Fingerprint / the entry invalidation mechanism for the
    /// safety net if that ever happens).
    ///
    /// Built under a HideFlags.HideAndDontSave root, never saved to the scene; the caller disposes
    /// it once done reading nodes. Known limitation: PedestrianNetwork.Build() matches crossings by
    /// scanning the whole scene for TrafficLightIntersection, so if a real city is already
    /// generated in the same open scene while a picker is opened, this preview's graph is matched
    /// against both its own temporary intersections and the real city's -- harmless in practice
    /// (both sit at the same world positions), but not scene-isolated the way the real pipeline is.
    /// </summary>
    internal sealed class CityGeneratorPedestrianPreview : IDisposable
    {
        private readonly GameObject root;
        private readonly PedestrianNetwork network;

        public int NodeCount => network.NodeCount;

        public PedestrianNode GetNode(int index) => network.GetNode(index);

        private CityGeneratorPedestrianPreview(GameObject root, PedestrianNetwork network)
        {
            this.root = root;
            this.network = network;
        }

        /// <summary>
        /// Builds a fresh preview from the current settings: grid/Custom Grid, plazaCells and
        /// customPlaces (for which blocks end up fully reserved) all feed the same node graph the
        /// real pipeline would produce for the same settings.
        /// </summary>
        public static CityGeneratorPedestrianPreview Build(CityGeneratorSettings settings)
        {
            var root = new GameObject("CityGeneratorPedestrianPreview (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            var trafficLightsGroup = new GameObject("TrafficLights").transform;
            trafficLightsGroup.SetParent(root.transform);
            var pedestrianNetworkGroup = new GameObject("PedestrianNetwork").transform;
            pedestrianNetworkGroup.SetParent(root.transform);

            // Only the graph's structure matters here (which arms get a crossing), never light
            // timing/colour, so a fixed seed keeps repeated Build() calls between edits producing
            // byte-identical intersection wiring instead of gratuitously differing.
            var random = new System.Random(0);
            int gridWidth = settings.general.gridWidth;
            int gridHeight = settings.general.gridHeight;

            List<BlockCell> blocks = settings.general.useCustomGrid
                ? CityGeneratorGrid.BuildBlocks(settings.general.customBlockCells, settings.general.plazaCells)
                : CityGeneratorGrid.BuildBlocks(gridWidth, gridHeight, settings.general.plazaCells);

            HashSet<(int gridX, int gridY, int slot)> reservedSlots =
                CityGeneratorCustomPlaceBuilder.ResolveReservedSlots(settings.customPlaces, blocks);

            // Without a Traffic Light Prefab assigned yet, no TrafficLight component exists to
            // place/match against, so PedestrianNetwork.Build() can't place any Curb/Crossing node
            // (FindNearestIntersection requires a real TrafficLightIntersection with children) --
            // the preview then simply shows Ring/Interior nodes only, exactly as many nodes as a
            // real generation would produce with includeTraffic's crossings absent for the same reason.
            if (settings.props.trafficLightPrefab != null)
            {
                if (settings.general.useCustomGrid)
                {
                    CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLightsGroup, settings.general.customBlockCells, random);
                }
                else
                {
                    CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLightsGroup, gridWidth, gridHeight, random);
                }
            }

            PedestrianNetwork network;
            if (settings.general.useCustomGrid)
            {
                network = CityGeneratorPedestrianBuilder.AddNetworkComponent(pedestrianNetworkGroup);
                var fullyReservedCells = new List<Vector2Int>();
                foreach (var slot in reservedSlots)
                {
                    if (slot.slot == -1)
                        fullyReservedCells.Add(new Vector2Int(slot.gridX, slot.gridY));
                }
                network.BuildFromBlockCells(settings.general.customBlockCells, settings.general.plazaCells, fullyReservedCells);
            }
            else
            {
                network = CityGeneratorPedestrianBuilder.AddNetworkComponent(pedestrianNetworkGroup, gridWidth, gridHeight, blocks, reservedSlots);
                network.Build();
            }

            return new CityGeneratorPedestrianPreview(root, network);
        }

        /// <summary>
        /// Hash of every setting that determines the pedestrian graph's shape (grid/Custom Grid,
        /// plazaCells, customPlaces, the traffic light prefab's identity). Single source of truth
        /// for "did anything that would change the graph change" -- used by both
        /// CityGeneratorCustomPedestrianList (to decide whether to rebuild its cached preview) and
        /// CityGeneratorValidator (same decision, for its own fallback preview) so the two never
        /// drift into deciding differently.
        /// </summary>
        public static int ComputeSettingsSignature(CityGeneratorSettings settings)
        {
            unchecked
            {
                int hash = settings.general.useCustomGrid ? 1 : 0;
                hash = hash * 31 + settings.general.gridWidth;
                hash = hash * 31 + settings.general.gridHeight;
                foreach (Vector2Int cell in settings.general.customBlockCells)
                    hash = hash * 31 + cell.GetHashCode();
                foreach (Vector2Int cell in settings.general.plazaCells)
                    hash = hash * 31 + cell.GetHashCode();
                hash = hash * 31 + (settings.props.trafficLightPrefab != null ? settings.props.trafficLightPrefab.GetEntityId().GetHashCode() : 0);
                foreach (CustomPlaceEntry place in settings.customPlaces)
                {
                    hash = hash * 31 + place.blockCell.GetHashCode();
                    hash = hash * 31 + place.cornerSlot;
                    hash = hash * 31 + (place.occupiesFullBlock ? 1 : 0);
                    hash = hash * 31 + (place.positionAssigned ? 1 : 0);
                }

                return hash;
            }
        }

        /// <summary>
        /// Cheap fingerprint of the current graph (node count + a position hash), used by
        /// CustomPedestrianEntry.graphFingerprint to detect that grid/plaza/Custom Place settings
        /// changed since a route was last traced (SPEC 12 plan step 8) without keeping a whole
        /// preview instance alive just to compare.
        /// </summary>
        public int Fingerprint()
        {
            unchecked
            {
                int hash = NodeCount;
                for (int i = 0; i < NodeCount; i++)
                {
                    Vector3 position = GetNode(i).Position;
                    hash = hash * 31 + Mathf.RoundToInt(position.x * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(position.z * 10f);
                }
                return hash;
            }
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
