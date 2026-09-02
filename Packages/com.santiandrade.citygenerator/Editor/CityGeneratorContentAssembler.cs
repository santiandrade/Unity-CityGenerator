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

            // Added alongside CityGeneratorRoot for the same reason, and populated below once every
            // count/reference this method computes is known — CityGeneratorSceneBuilder fills the
            // remaining fields (player, freeCameraController, dayNightCycle, minimapHUD, minimapData)
            // right after creating each of those, which live outside this method's scope.
            CityGeneratorInfo info = cityRoot.GetComponent<CityGeneratorInfo>();
            if (info == null)
                info = cityRoot.gameObject.AddComponent<CityGeneratorInfo>();

            var random = settings.general.useCustomSeed
                ? new System.Random(settings.general.seed)
                : new System.Random();
            int gridWidth = settings.general.gridWidth;
            int gridHeight = settings.general.gridHeight;

            Transform roads = GetOrCreateGroup(cityRoot, "Roads");
            Transform emptyBlocks = GetOrCreateGroup(cityRoot, "EmptyBlocks");
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
            List<BlockCell> blocks = settings.general.useCustomGrid
                ? CityGeneratorGrid.BuildBlocks(settings.general.customBlockCells, settings.general.plazaCells)
                : CityGeneratorGrid.BuildBlocks(gridWidth, gridHeight, settings.general.plazaCells);

            Report("Ground", 0.1f);
            if (settings.general.useCustomGrid)
            {
                CityGeneratorGroundBuilder.BuildRoadBase(settings.ground.roadBasePrefab, roads, settings.general.customBlockCells);
                CityGeneratorGroundBuilder.BuildRoadMarkings(settings.ground.roadLinePrefab, settings.ground.crosswalkLinePrefab, roadMarkings, settings.general.customBlockCells);
                CityGeneratorGroundBuilder.BuildPerimeterSidewalks(settings.ground.sidewalkPrefab, sidewalks, settings.general.customBlockCells);
                CityGeneratorGroundBuilder.BuildEmptyBlocks(settings.ground.emptyBlockPrefab, emptyBlocks, settings.general.customBlockCells);
            }
            else
            {
                CityGeneratorGroundBuilder.BuildRoadBase(settings.ground.roadBasePrefab, roads, gridWidth, gridHeight);
                CityGeneratorGroundBuilder.BuildRoadMarkings(settings.ground.roadLinePrefab, settings.ground.crosswalkLinePrefab, roadMarkings, gridWidth, gridHeight);
                CityGeneratorGroundBuilder.BuildPerimeterSidewalks(settings.ground.sidewalkPrefab, sidewalks, gridWidth, gridHeight);
            }
            CityGeneratorGroundBuilder.BuildSidewalks(settings.ground.sidewalkPrefab, sidewalks, blocks);

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
                settings.general.playerEnabled ? settings.general.playerPrefab : null, blocks, playerAvoidObstacles, random, cityRoot, cache);

            cache.DestroyRemainingProbes();

            // The traffic network and its lights are always generated (every 4-way intersection
            // stays regulated), even when traffic itself is switched off.
            Report("Traffic network", 0.6f);
            TrafficNetwork network;
            List<GameObject> trafficLightInstances;
            if (settings.general.useCustomGrid)
            {
                network = CityGeneratorTrafficBuilder.AddNetworkComponent(trafficNetworkGroup);
                trafficLightInstances = CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLights, settings.general.customBlockCells, random);
                network.BuildFromBlockCells(settings.general.customBlockCells);
            }
            else
            {
                network = CityGeneratorTrafficBuilder.AddNetworkComponent(trafficNetworkGroup, gridWidth, gridHeight);
                trafficLightInstances = CityGeneratorTrafficBuilder.BuildTrafficLights(settings.props.trafficLightPrefab, trafficLights, gridWidth, gridHeight, random);
                network.Build();
            }

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
            PedestrianNetwork pedestrianNetwork;
            if (settings.general.useCustomGrid)
            {
                pedestrianNetwork = CityGeneratorPedestrianBuilder.AddNetworkComponent(pedestrianNetworkGroup);
                var fullyReservedCells = new List<Vector2Int>();
                foreach (var slot in reservedSlots)
                {
                    if (slot.slot == -1)
                        fullyReservedCells.Add(new Vector2Int(slot.gridX, slot.gridY));
                }
                pedestrianNetwork.BuildFromBlockCells(settings.general.customBlockCells, settings.general.plazaCells, fullyReservedCells);
            }
            else
            {
                pedestrianNetwork = CityGeneratorPedestrianBuilder.AddNetworkComponent(pedestrianNetworkGroup, gridWidth, gridHeight, blocks, reservedSlots);
                pedestrianNetwork.Build();
            }
            List<GameObject> pedestrianInstances = new();
            if (settings.general.includePedestrians)
            {
                Report("Pedestrians", 0.88f);
                CityGeneratorPedestrianBuilder.AddManagerComponent(pedestrianNetworkGroup, pedestrianNetwork, settings.crowd);
                pedestrianInstances = CityGeneratorPedestrianBuilder.BuildPedestrians(settings.pedestrians, settings.general.pedestrianCount, pedestrianNetwork, pedestrians, random, settings.pedestrianBehaviour);
            }

            // Custom Pedestrians (SPEC 12) are a budget independent of pedestrianCount/includePedestrians
            // -- they run whenever entries exist, mirroring how Custom Places aren't gated by any
            // general toggle. Needs its own PedestrianManager when includePedestrians was off above.
            List<GameObject> customPedestrianInstances = new();
            if (settings.customPedestrians.Count > 0)
            {
                Report("Custom pedestrians", 0.92f);
                if (pedestrianNetwork.Manager == null)
                {
                    CityGeneratorPedestrianBuilder.AddManagerComponent(pedestrianNetworkGroup, pedestrianNetwork, settings.crowd);
                }
                customPedestrianInstances = CityGeneratorCustomPedestrianBuilder.BuildCustomPedestrians(settings.customPedestrians, pedestrianNetwork, pedestrians, random, settings.pedestrianBehaviour);
                pedestrianInstances.AddRange(customPedestrianInstances);
            }

            // Runs last so the Vehicle/Pedestrian layers above already exist and can be excluded
            // from the snapshot's culling mask, even though the snapshot itself only captures
            // static geometry. Only fills MinimapData with an in-memory snapshot texture and the
            // POI list — CityGeneratorSceneBuilder finalises it into a saved PNG asset once the
            // generated scene's path is known (not yet the case here, mid-Assemble).
            Report("Minimap", 0.97f);
            if (settings.general.useCustomGrid)
                CityGeneratorMinimapBuilder.Build(settings.minimap, cityRoot, settings.general.customBlockCells, pointsOfInterest);
            else
                CityGeneratorMinimapBuilder.Build(settings.minimap, cityRoot, gridWidth, gridHeight, pointsOfInterest);

            Report("Audio", 0.99f);
            CityGeneratorAudioBuilder.BuildAmbience(cityRoot, settings.audio.ambience);

            // Counted post-hoc instead of re-deriving CityGeneratorAudioBuilder's own null-clip
            // skip logic: 2D ambience sources land as spatialBlend 0, 3D plaza sources (nested
            // under Plaza, one per non-null PlazaAudioClipEntry per plaza block) as spatialBlend 1.
            int ambienceClipCount = 0;
            int plazaAudioSourceCount = 0;
            foreach (AudioSource source in cityRoot.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.spatialBlend <= 0f)
                    ambienceClipCount++;
                else
                    plazaAudioSourceCount++;
            }

            // Every group except Vehicles/Pedestrians is 100% static geometry once generated:
            // marking it unlocks static batching and is a prerequisite for baking occlusion
            // culling / the GPU Resident Drawer (see the technical review, A.1/C.3). Both agent
            // groups move by transform every frame instead.
            Report("Static flags", 0.95f);
            MarkStatic(roads);
            MarkStatic(emptyBlocks);
            MarkStatic(sidewalks);
            MarkStatic(roadMarkings);
            MarkStatic(customPlaces);
            MarkStatic(buildings);
            MarkStatic(plaza);
            MarkStatic(trees);
            MarkStatic(streetLights);
            MarkStatic(props);
            MarkStatic(trafficLights);

            info.useCustomGrid = settings.general.useCustomGrid;
            info.gridSize = settings.general.useCustomGrid ? ComputeCustomGridBounds(blocks) : new Vector2Int(gridWidth, gridHeight);
            info.blockCount = blocks.Count;

            info.buildingCount = builtBuildings.Count;
            info.plazaCount = 0;
            foreach (BlockCell block in blocks)
            {
                if (block.isPlaza)
                    info.plazaCount++;
            }
            info.customPlaceCount = builtCustomPlaces.Count;
            info.lampCount = lamps.Count;
            info.binCount = bins.Count;
            info.streetTreeCount = streetTrees.Count;
            info.trafficLightCount = trafficLightInstances.Count;
            info.customPedestrianCount = settings.customPedestrians.Count;

            info.useCustomSeed = settings.general.useCustomSeed;
            info.seed = settings.general.useCustomSeed ? settings.general.seed : 0;

            info.playerEnabled = settings.general.playerEnabled;
            info.trafficEnabled = settings.general.includeTraffic;
            info.pedestriansEnabled = settings.general.includePedestrians;

            info.ambienceEnabled = settings.audio.ambience.enabled;
            info.ambienceClipCount = ambienceClipCount;
            info.plazaAudioEnabled = settings.audio.plazaAudio.enabled;
            info.plazaAudioSourceCount = plazaAudioSourceCount;

            info.trafficManager = network.Manager;
            info.pedestrianManager = pedestrianNetwork.Manager;
            // Left null when Minimap is disabled (CityGeneratorMinimapBuilder.Build is then a
            // no-op) — CityGeneratorSceneBuilder fills minimapHUD separately, once it exists.
            info.minimapData = cityRoot.GetComponent<MinimapData>();

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

        // Custom Grid has no fixed gridWidth/gridHeight — CityGeneratorInfo.gridSize instead
        // reports the bounding box of the real cells, matching CityGeneratorGroundBuilder's own
        // "bounding rectangle grown by RoadBaseMargin" concept minus the margin itself.
        private static Vector2Int ComputeCustomGridBounds(List<BlockCell> blocks)
        {
            if (blocks.Count == 0)
                return Vector2Int.zero;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (BlockCell block in blocks)
            {
                if (block.gridX < minX) minX = block.gridX;
                if (block.gridX > maxX) maxX = block.gridX;
                if (block.gridY < minY) minY = block.gridY;
                if (block.gridY > maxY) maxY = block.gridY;
            }

            return new Vector2Int(maxX - minX + 1, maxY - minY + 1);
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
