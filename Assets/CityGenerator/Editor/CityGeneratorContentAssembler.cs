using System.Collections.Generic;
using CityGenerator.Runtime;
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
        public static CityBuildSummary Assemble(CityGeneratorSettings settings, Transform cityRoot)
        {
            var random = new System.Random();
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
            List<GameObject> plazaSolids = CityGeneratorPlazaBuilder.BuildPlazas(settings.plaza, settings.vegetation, plaza, trees, blocks, random, out List<GameObject> plazaLawns);

            // Street furniture avoids buildings/plaza content, the plaza lawns, and each other
            // via one shared, growing obstacle list threaded through every category in turn.
            var obstacles = new List<GameObject>(builtBuildings);
            obstacles.AddRange(plazaSolids);
            obstacles.AddRange(plazaLawns);

            List<GameObject> lamps = CityGeneratorStreetPropsBuilder.BuildLamps(settings.props.lampPrefab, streetLights, blocks, obstacles);
            obstacles.AddRange(lamps);
            List<GameObject> busStops = CityGeneratorStreetPropsBuilder.BuildBusStops(settings.props.busStopPrefab, settings.props.busStopDensity, props, blocks, random, obstacles);
            obstacles.AddRange(busStops);
            List<GameObject> bins = CityGeneratorStreetPropsBuilder.BuildBins(settings.props.binPrefab, settings.props.binDensity, props, blocks, random, obstacles);
            obstacles.AddRange(bins);
            List<GameObject> streetTrees = CityGeneratorStreetPropsBuilder.BuildStreetVegetation(settings.vegetation, trees, blocks, random, obstacles);
            obstacles.AddRange(streetTrees);

            // The traffic network and its lights are always generated (every 4-way intersection
            // stays regulated), even when traffic itself is switched off.
            TrafficNetwork network = CityGeneratorTrafficBuilder.AddNetworkComponent(trafficNetworkGroup, gridWidth, gridHeight);
            List<GameObject> trafficLightInstances = CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLights, gridWidth, gridHeight, random);
            network.Build();

            List<GameObject> vehicleInstances = new();
            if (settings.general.includeTraffic)
                vehicleInstances = CityGeneratorTrafficBuilder.BuildVehicles(settings.vehicles, settings.general.vehicleCount, network, vehicles, random);

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
    }
}
