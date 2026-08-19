using System.Collections.Generic;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>Counts produced by <see cref="CityGeneratorContentAssembler.Assemble"/>, for the final summary log.</summary>
    internal readonly struct CityBuildSummary
    {
        public readonly int blockCount;
        public readonly int buildingCount;
        public readonly int plazaSolidCount;
        public readonly int lampCount;
        public readonly int busStopCount;
        public readonly int binCount;
        public readonly int streetTreeCount;
        public readonly int trafficLightCount;
        public readonly int vehicleCount;

        public CityBuildSummary(int blockCount, int buildingCount, int plazaSolidCount, int lampCount, int busStopCount, int binCount, int streetTreeCount, int trafficLightCount, int vehicleCount)
        {
            this.blockCount = blockCount;
            this.buildingCount = buildingCount;
            this.plazaSolidCount = plazaSolidCount;
            this.lampCount = lampCount;
            this.busStopCount = busStopCount;
            this.binCount = binCount;
            this.streetTreeCount = streetTreeCount;
            this.trafficLightCount = trafficLightCount;
            this.vehicleCount = vehicleCount;
        }
    }

    /// <summary>
    /// Runs the full generation pipeline (grid, ground, buildings, plazas, street furniture,
    /// traffic network/lights, vehicles) into an already-created group hierarchy under
    /// <paramref name="cityRoot"/>. Scene-level objects (light, volume, camera, player) are
    /// handled separately by <see cref="CityGeneratorSceneBuilder"/>.
    /// </summary>
    internal static class CityGeneratorContentAssembler
    {
        // Batching Static lets Unity combine every instance in a group into a handful of draw
        // calls; Occluder/Occludee Static are what an occlusion culling bake needs. Vehicles are
        // the only group excluded — CarAgent moves them by transform every frame.
        private static readonly StaticEditorFlags MarkedStaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        public static CityBuildSummary Assemble(CityGeneratorSettings settings, Transform cityRoot)
        {
            var random = settings.general.useCustomSeed
                ? new System.Random(settings.general.seed)
                : new System.Random();
            int gridWidth = settings.general.gridWidth;
            int gridHeight = settings.general.gridHeight;

            Transform roads = GetOrCreateGroup(cityRoot, "Roads");
            Transform sidewalks = GetOrCreateGroup(cityRoot, "Sidewalks");
            Transform roadMarkings = GetOrCreateGroup(cityRoot, "RoadMarkings");
            Transform buildings = GetOrCreateGroup(cityRoot, "Buildings");
            Transform trafficLights = GetOrCreateGroup(cityRoot, "TrafficLights");
            Transform streetLights = GetOrCreateGroup(cityRoot, "StreetLights");
            Transform plaza = GetOrCreateGroup(cityRoot, "Plaza");
            Transform trees = GetOrCreateGroup(cityRoot, "Trees");
            Transform props = GetOrCreateGroup(cityRoot, "Props");
            Transform vehicles = GetOrCreateGroup(cityRoot, "Vehicles");
            Transform trafficNetworkGroup = GetOrCreateGroup(cityRoot, "TrafficNetwork");

            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(gridWidth, gridHeight, settings.general.plazaCount, random);

            CityGeneratorGroundBuilder.BuildRoadBase(settings.ground.roadBasePrefab, roads, gridWidth, gridHeight);
            CityGeneratorGroundBuilder.BuildSidewalks(settings.ground.sidewalkPrefab, sidewalks, blocks);
            CityGeneratorGroundBuilder.BuildRoadMarkings(settings.ground.roadLinePrefab, settings.ground.crosswalkLinePrefab, roadMarkings, gridWidth, gridHeight);

            List<GameObject> builtBuildings = CityGeneratorBuildingBuilder.BuildBuildings(settings.buildingPrefabs, buildings, blocks, settings.general.buildingsPerBlock, random);

            // Street furniture avoids buildings/plaza content, the plaza lawns, and each other via
            // one shared, growing obstacle list threaded through every category in turn; the cache
            // measures each object's overlap rect once (instead of on every future comparison) and
            // reuses one probe instance per prefab to test rejected candidates without an
            // Instantiate/DestroyImmediate pair each time.
            var cache = new ObstacleCache();
            var obstacles = new List<GameObject>(builtBuildings);

            List<GameObject> plazaSolids = CityGeneratorPlazaBuilder.BuildPlazas(settings.plaza, settings.vegetation, plaza, trees, blocks, random, cache, out List<GameObject> plazaLawns);
            obstacles.AddRange(plazaSolids);
            obstacles.AddRange(plazaLawns);

            List<GameObject> lamps = CityGeneratorStreetPropsBuilder.BuildLamps(settings.props.lampPrefab, settings.props.lampDensity, streetLights, blocks, random, obstacles, cache);
            List<GameObject> busStops = CityGeneratorStreetPropsBuilder.BuildBusStops(settings.props.busStopPrefab, settings.props.busStopDensity, props, blocks, random, obstacles, cache);
            List<GameObject> bins = CityGeneratorStreetPropsBuilder.BuildBins(settings.props.binPrefab, settings.props.binDensity, props, blocks, random, obstacles, cache);
            List<GameObject> streetTrees = CityGeneratorStreetPropsBuilder.BuildStreetVegetation(settings.vegetation, trees, blocks, random, obstacles, cache);
            cache.DestroyRemainingProbes();

            // The traffic network and its lights are always generated (every 4-way intersection
            // stays regulated), even when traffic itself is switched off.
            TrafficNetwork network = CityGeneratorTrafficBuilder.AddNetworkComponent(trafficNetworkGroup, gridWidth, gridHeight);
            List<GameObject> trafficLightInstances = CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLights, gridWidth, gridHeight, random);
            network.Build();

            List<GameObject> vehicleInstances = new();
            if (settings.general.includeTraffic)
                vehicleInstances = CityGeneratorTrafficBuilder.BuildVehicles(settings.vehicles, settings.general.vehicleCount, network, vehicles, random);

            // Every group except Vehicles is 100% static geometry once generated: marking it
            // unlocks static batching and is a prerequisite for baking occlusion culling / the GPU
            // Resident Drawer (see the technical review, A.1/C.3).
            MarkStatic(roads);
            MarkStatic(sidewalks);
            MarkStatic(roadMarkings);
            MarkStatic(buildings);
            MarkStatic(plaza);
            MarkStatic(trees);
            MarkStatic(streetLights);
            MarkStatic(props);
            MarkStatic(trafficLights);

            return new CityBuildSummary(
                blocks.Count, builtBuildings.Count, plazaSolids.Count,
                lamps.Count, busStops.Count, bins.Count, streetTrees.Count,
                trafficLightInstances.Count, vehicleInstances.Count);
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

        private static void MarkStatic(Transform group)
        {
            foreach (Transform child in group.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, MarkedStaticFlags);
        }
    }
}
