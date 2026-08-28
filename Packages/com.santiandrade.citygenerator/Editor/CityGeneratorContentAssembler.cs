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
        public readonly int binCount;
        public readonly int streetTreeCount;
        public readonly int trafficLightCount;
        public readonly int vehicleCount;
        public readonly int pedestrianCount;
        public readonly Vector3 playerSpawnPosition;

        public CityBuildSummary(int blockCount, int buildingCount, int plazaSolidCount, int lampCount, int binCount, int streetTreeCount, int trafficLightCount, int vehicleCount, int pedestrianCount, Vector3 playerSpawnPosition)
        {
            this.blockCount = blockCount;
            this.buildingCount = buildingCount;
            this.plazaSolidCount = plazaSolidCount;
            this.lampCount = lampCount;
            this.binCount = binCount;
            this.streetTreeCount = streetTreeCount;
            this.trafficLightCount = trafficLightCount;
            this.vehicleCount = vehicleCount;
            this.pedestrianCount = pedestrianCount;
            this.playerSpawnPosition = playerSpawnPosition;
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

        /// <summary>
        /// Runs the pipeline exactly as <see cref="Assemble(CityGeneratorSettings, Transform)"/>,
        /// additionally reporting coarse-grained progress through <paramref name="onProgress"/>
        /// (phase label, 0..1 fraction) so a caller can drive a progress bar during what would
        /// otherwise be a silent, UI-frozen generation. Purely additive: passing null behaves
        /// identically to the two-argument overload.
        /// </summary>
        public static CityBuildSummary Assemble(CityGeneratorSettings settings, Transform cityRoot, System.Action<string, float> onProgress)
        {
            void Report(string phase, float fraction) => onProgress?.Invoke(phase, fraction);

            // Added first, before any builder runs, so a temporary root left behind by a failed
            // rebuild (see CityGeneratorSceneBuilder.RebuildInActiveScene) is still identifiable.
            if (cityRoot.GetComponent<CityGeneratorRoot>() == null)
                cityRoot.gameObject.AddComponent<CityGeneratorRoot>();

            var random = settings.general.useCustomSeed
                ? new System.Random(settings.general.seed)
                : new System.Random();
            int gridWidth = settings.general.gridWidth;
            int gridHeight = settings.general.gridHeight;

            Transform roads = GetOrCreateGroup(cityRoot, "Roads");
            Transform sidewalks = GetOrCreateGroup(cityRoot, "Sidewalks");
            Transform roadMarkings = GetOrCreateGroup(cityRoot, "RoadMarkings");
            Transform customPlaces = GetOrCreateGroup(cityRoot, "CustomPlaces");
            Transform buildings = GetOrCreateGroup(cityRoot, "Buildings");
            Transform trafficLights = GetOrCreateGroup(cityRoot, "TrafficLights");
            Transform streetLights = GetOrCreateGroup(cityRoot, "StreetLights");
            Transform plaza = GetOrCreateGroup(cityRoot, "Plaza");
            Transform trees = GetOrCreateGroup(cityRoot, "Trees");
            Transform props = GetOrCreateGroup(cityRoot, "Props");
            Transform vehicles = GetOrCreateGroup(cityRoot, "Vehicles");
            Transform trafficNetworkGroup = GetOrCreateGroup(cityRoot, "TrafficNetwork");
            Transform pedestrians = GetOrCreateGroup(cityRoot, "Pedestrians");
            Transform pedestrianNetworkGroup = GetOrCreateGroup(cityRoot, "PedestrianNetwork");

            Report("Grid", 0f);
            List<BlockCell> blocks = CityGeneratorGrid.BuildBlocks(gridWidth, gridHeight, settings.general.plazaCells);

            Report("Ground", 0.1f);
            CityGeneratorGroundBuilder.BuildRoadBase(settings.ground.roadBasePrefab, roads, gridWidth, gridHeight);
            CityGeneratorGroundBuilder.BuildSidewalks(settings.ground.sidewalkPrefab, sidewalks, blocks);
            CityGeneratorGroundBuilder.BuildRoadMarkings(settings.ground.roadLinePrefab, settings.ground.crosswalkLinePrefab, roadMarkings, gridWidth, gridHeight);

            Report("Custom places", 0.2f);
            (List<GameObject> builtCustomPlaces, HashSet<(int gridX, int gridY, int slot)> reservedSlots, List<PointOfInterestEntry> pointsOfInterest) =
                CityGeneratorCustomPlaceBuilder.BuildCustomPlaces(settings.customPlaces, blocks, customPlaces);

            Report("Buildings", 0.25f);
            List<GameObject> builtBuildings = CityGeneratorBuildingBuilder.BuildBuildings(settings.buildingPrefabs, buildings, blocks, settings.general.buildingsPerBlock, random, reservedSlots);

            // Street furniture avoids buildings/plaza content, the plaza lawns, and each other via
            // one shared, growing obstacle list threaded through every category in turn; the cache
            // measures each object's overlap rect once (instead of on every future comparison) and
            // reuses one probe instance per prefab to test rejected candidates without an
            // Instantiate/DestroyImmediate pair each time.
            var cache = new ObstacleCache();
            var obstacles = new List<GameObject>(builtCustomPlaces);
            obstacles.AddRange(builtBuildings);
            // Mirrors `obstacles` but leaves out ground cover (plaza lawns) that the player is
            // meant to be able to stand on; used only to pick its spawn position below.
            var playerAvoidObstacles = new List<GameObject>(builtCustomPlaces);
            playerAvoidObstacles.AddRange(builtBuildings);

            Report("Plazas", 0.35f);
            List<GameObject> plazaSolids = CityGeneratorPlazaBuilder.BuildPlazas(settings.plaza, settings.vegetation, settings.audio.plazaAudio, plaza, trees, blocks, random, cache, out List<GameObject> plazaLawns);
            obstacles.AddRange(plazaSolids);
            obstacles.AddRange(plazaLawns);
            playerAvoidObstacles.AddRange(plazaSolids);

            Report("Street furniture", 0.45f);
            List<GameObject> lamps = CityGeneratorStreetPropsBuilder.BuildLamps(settings.props.lampPrefab, settings.props.lampDensity, streetLights, blocks, random, obstacles, cache);
            List<GameObject> bins = CityGeneratorStreetPropsBuilder.BuildBins(settings.props.binPrefab, settings.props.binDensity, props, blocks, random, obstacles, cache);
            List<GameObject> streetTrees = CityGeneratorStreetPropsBuilder.BuildStreetVegetation(settings.vegetation, trees, blocks, random, obstacles, cache);
            playerAvoidObstacles.AddRange(lamps);
            playerAvoidObstacles.AddRange(bins);
            playerAvoidObstacles.AddRange(streetTrees);

            Report("Player spawn", 0.55f);
            Vector3 playerSpawnPosition = CityGeneratorPlayerSpawner.FindSpawnPosition(
                settings.general.playerPrefab, blocks, playerAvoidObstacles, random, cityRoot, cache);

            cache.DestroyRemainingProbes();

            // The traffic network and its lights are always generated (every 4-way intersection
            // stays regulated), even when traffic itself is switched off.
            Report("Traffic network", 0.6f);
            TrafficNetwork network = CityGeneratorTrafficBuilder.AddNetworkComponent(trafficNetworkGroup, gridWidth, gridHeight);
            List<GameObject> trafficLightInstances = CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLights, gridWidth, gridHeight, random);
            network.Build();

            List<GameObject> vehicleInstances = new();
            if (settings.general.includeTraffic)
            {
                Report("Vehicles", 0.7f);
                CityGeneratorTrafficBuilder.AddManagerComponent(trafficNetworkGroup, network);
                vehicleInstances = CityGeneratorTrafficBuilder.BuildVehicles(settings.vehicles, settings.general.vehicleCount, network, vehicles, random);
                // Independent of includePedestrians: the player (placed by CityGeneratorSceneBuilder
                // on the same layer) needs vehicles to detect it too.
                CityGeneratorPedestrianBuilder.EnsurePedestrianLayerAndAssignMask(vehicles);
            }

            // The pedestrian network mirrors the traffic network: always generated (so its
            // crossings stay wired to the real traffic lights), independent of includePedestrians.
            Report("Pedestrian network", 0.8f);
            PedestrianNetwork pedestrianNetwork = CityGeneratorPedestrianBuilder.AddNetworkComponent(pedestrianNetworkGroup, gridWidth, gridHeight);
            pedestrianNetwork.Build();
            CityGeneratorPedestrianBuilder.PruneNodesAgainstObstacles(pedestrianNetwork, obstacles, cache);

            List<GameObject> pedestrianInstances = new();
            if (settings.general.includePedestrians)
            {
                Report("Pedestrians", 0.88f);
                CityGeneratorPedestrianBuilder.AddManagerComponent(pedestrianNetworkGroup, pedestrianNetwork, settings.crowd);
                pedestrianInstances = CityGeneratorPedestrianBuilder.BuildPedestrians(settings.pedestrians, settings.general.pedestrianCount, pedestrianNetwork, pedestrians, random, settings.pedestrianBehaviour);
            }

            // Runs last so the Vehicle/Pedestrian layers above already exist and can be excluded
            // from the snapshot's culling mask, even though the snapshot itself only captures
            // static geometry. Only fills MinimapData with an in-memory snapshot texture and the
            // POI list — CityGeneratorSceneBuilder finalises it into a saved PNG asset once the
            // generated scene's path is known (not yet the case here, mid-Assemble).
            Report("Minimap", 0.97f);
            CityGeneratorMinimapBuilder.Build(settings.minimap, cityRoot, gridWidth, gridHeight, pointsOfInterest);

            Report("Audio", 0.99f);
            CityGeneratorAudioBuilder.BuildAmbience(cityRoot, settings.audio.ambience);

            // Every group except Vehicles/Pedestrians is 100% static geometry once generated:
            // marking it unlocks static batching and is a prerequisite for baking occlusion
            // culling / the GPU Resident Drawer (see the technical review, A.1/C.3). Both agent
            // groups move by transform every frame instead.
            Report("Static flags", 0.95f);
            MarkStatic(roads);
            MarkStatic(sidewalks);
            MarkStatic(roadMarkings);
            MarkStatic(customPlaces);
            MarkStatic(buildings);
            MarkStatic(plaza);
            MarkStatic(trees);
            MarkStatic(streetLights);
            MarkStatic(props);
            MarkStatic(trafficLights);

            Report("Done", 1f);
            return new CityBuildSummary(
                blocks.Count, builtBuildings.Count, plazaSolids.Count,
                lamps.Count, bins.Count, streetTrees.Count,
                trafficLightInstances.Count, vehicleInstances.Count, pedestrianInstances.Count,
                playerSpawnPosition);
        }

        public static CityBuildSummary Assemble(CityGeneratorSettings settings, Transform cityRoot)
        {
            return Assemble(settings, cityRoot, onProgress: null);
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
