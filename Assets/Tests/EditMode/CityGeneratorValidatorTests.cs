using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    internal class CityGeneratorValidatorTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            spawned.Clear();
        }

        private GameObject MakePrefabLike(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshRenderer>();
            spawned.Add(go);
            return go;
        }

        private GameObject MakePrefabLikeNoRenderer(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private CityGeneratorSettings MakeMinimalValidSettings()
        {
            var settings = new CityGeneratorSettings();
            settings.general.gridWidth = 1;
            settings.general.gridHeight = 1; // no interior intersections: no traffic light required
            settings.general.includeTraffic = false;
            settings.general.includePedestrians = false;
            settings.general.playerEnabled = false; // avoid the unrelated "Player Prefab / Input Actions required" issues in tests not about the player
            settings.minimap.enabled = false; // avoid the unrelated "View Radius larger than the snapshot" issue on this deliberately tiny grid
            settings.ground.roadBasePrefab = MakePrefabLike("RoadBase");
            settings.ground.sidewalkPrefab = MakePrefabLike("Sidewalk");
            settings.ground.roadLinePrefab = MakePrefabLike("RoadLine");
            settings.ground.crosswalkLinePrefab = MakePrefabLike("Crosswalk");
            settings.vegetation.density = 0f; // avoid the unrelated "no prefabs" issue in tests not about vegetation
            settings.audio.ambience.enabled = false; // avoid the unrelated "missing clip" issue in tests not about audio
            settings.audio.plazaAudio.enabled = false; // avoid the unrelated "no clip entries" issue in tests not about audio
            return settings;
        }

        [Test]
        public void ValidateDetailed_MinimalValidSettings_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void ValidateDetailed_MissingRoadBasePrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.ground.roadBasePrefab = null;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "ground.roadBasePrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_PlazaCellsWithoutLawnPrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.plazaCells.Add(new Vector2Int(0, 0));

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "plaza.lawnPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_PlayerPrefabWithoutInputActions_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.playerEnabled = true;
            settings.general.playerPrefab = MakePrefabLike("Player");

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "general.inputActions" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_EmptyBuildingPrefabEntry_IsWarningOnly()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.buildingPrefabs.Add(null);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "buildingPrefabs" && i.isWarning));
        }

        [Test]
        public void ValidateDetailed_BuildingPrefabWithoutRenderer_IsWarningOnly()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.buildingPrefabs.Add(MakePrefabLikeNoRenderer("NoRenderer"));

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "buildingPrefabs" && i.isWarning));
        }

        [Test]
        public void ValidateDetailed_InteriorIntersectionWithoutTrafficLightPrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2; // at least one interior intersection

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_TrafficLightPrefabWithoutComponent_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            settings.props.trafficLightPrefab = MakePrefabLike("TrafficLightNoComponent");

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_TrafficLightPrefabWithComponent_ClearsInteriorIntersectionIssue()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            GameObject light = MakePrefabLike("TrafficLight");
            light.AddComponent<CityGenerator.Runtime.TrafficLight>();
            settings.props.trafficLightPrefab = light;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void ValidateDetailed_VegetationDensityWithoutPrefabs_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.vegetation.density = 0.5f;
            settings.vegetation.prefabs.Clear();

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "vegetation.prefabs" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_VehiclesRequiredWhenTrafficAndCountPositive()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.includeTraffic = true;
            settings.general.vehicleCount = 10;
            settings.vehicles.Clear();

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "vehicles" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_VehiclePercentagesMustSumTo100()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.includeTraffic = true;
            settings.general.vehicleCount = 10;
            settings.vehicles.Add(new VehicleEntry { prefab = MakePrefabLike("Car"), percentage = 50f });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "vehicles" && !i.isWarning && i.message.Contains("100")));
        }

        [Test]
        public void ValidateDetailed_PedestrianPercentagesSummingTo100_IsValid()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.includePedestrians = true;
            settings.general.pedestrianCount = 10;
            settings.pedestrians.Add(new PedestrianEntry { prefab = MakePrefabLike("Ped"), percentage = 100f });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void ValidateDetailed_RunSpeedBelowWalkSpeed_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.player.walkSpeed = 8f;
            settings.player.runSpeed = 4f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "player.runSpeed" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_ZeroWalkSpeed_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.player.walkSpeed = 0f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "player.walkSpeed" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_EqualWalkAndRunSpeed_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.player.walkSpeed = 5f;
            settings.player.runSpeed = 5f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "player.walkSpeed" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_ControllerStepOffsetNotSmallerThanHeight_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.player.controllerHeight = 1f;
            settings.player.controllerStepOffset = 1f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "player.controllerStepOffset" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_ControllerSkinWidthNotSmallerThanRadius_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.player.controllerRadius = 0.2f;
            settings.player.controllerSkinWidth = 0.2f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "player.controllerSkinWidth" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_CameraMaxPitchNotGreaterThanMinPitch_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.camera.minPitch = 10f;
            settings.camera.maxPitch = 10f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "camera.maxPitch" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_CameraDistanceBelowMinDistance_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.camera.minDistance = 5f;
            settings.camera.distance = 1f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "camera.distance" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_PedestrianIdleStopMaxBelowMin_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.pedestrianBehaviour.idleStopDurationMin = 10f;
            settings.pedestrianBehaviour.idleStopDurationMax = 1f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "pedestrianBehaviour.idleStopDurationMax" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_NegativeCrowdSeparationRadius_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.crowd.separationRadius = -1f;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "crowd.separationRadius" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_CustomPlaceMissingTitle_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = string.Empty,
                prefab = MakePrefabLike("Kiosk"),
                blockCell = new Vector2Int(0, 0),
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("title")));
        }

        [Test]
        public void ValidateDetailed_CustomPlaceMissingPrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = null,
                blockCell = new Vector2Int(0, 0),
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("prefab")));
        }

        [Test]
        public void ValidateDetailed_CustomPlaceWithoutPositionAssigned_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("Kiosk"),
                positionAssigned = false,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("no position assigned")));
        }

        [Test]
        public void ValidateDetailed_CustomPlaceOnPlazaBlock_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.plaza.lawnPrefab = MakePrefabLike("Lawn");
            settings.general.plazaCells.Add(new Vector2Int(0, 0));
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("Kiosk"),
                blockCell = new Vector2Int(0, 0),
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("plaza")));
        }

        [Test]
        public void ValidateDetailed_TwoCustomPlacesClaimingSameCorner_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk A",
                prefab = MakePrefabLike("KioskA"),
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 0,
                positionAssigned = true,
            });
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk B",
                prefab = MakePrefabLike("KioskB"),
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 0,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("same slot")));
        }

        [Test]
        public void ValidateDetailed_TwoCustomPlacesWithDuplicateTitle_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("KioskA"),
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 0,
                positionAssigned = true,
            });
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = " kiosk ",
                prefab = MakePrefabLike("KioskB"),
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 1,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("titles must be unique")));
        }

        [Test]
        public void ValidateDetailed_CustomGrid_CustomPlaceOnRemovedBlock_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5) };
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("Kiosk"),
                blockCell = new Vector2Int(6, 6), // not in customBlockCells
                cornerSlot = 0,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "customPlaces" && !i.isWarning && i.message.Contains("no longer exists in the custom grid shape")));
        }

        [Test]
        public void ValidateDetailed_CustomGrid_CustomPlaceOnRealBlock_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5) };
            settings.ground.emptyBlockPrefab = MakePrefabLike("EmptyBlock");
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("Kiosk"),
                blockCell = new Vector2Int(5, 5),
                cornerSlot = 0,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void ValidateDetailed_GridLargerThanMaxGridSize_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = CityGeneratorConstants.MaxGridSize + 1;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "general.gridWidth" && !i.isWarning));
            Assert.IsFalse(issues.Exists(i => i.settingsPath == "general.gridHeight"));
        }

        [Test]
        public void ValidateDetailed_CustomGridCellOutsideCanvas_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5), new(5, CityGeneratorConstants.MaxGridSize) };
            settings.ground.emptyBlockPrefab = MakePrefabLike("EmptyBlock");

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "general.customBlockCells" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_ValidCustomPlace_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.customPlaces.Add(new CustomPlaceEntry
            {
                title = "Kiosk",
                prefab = MakePrefabLike("Kiosk"),
                blockCell = new Vector2Int(0, 0),
                cornerSlot = 0,
                facing = CustomPlaceFacing.North,
                positionAssigned = true,
            });

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        // Regression: the validator and CityGeneratorTrafficBuilder once used different rules for
        // "this intersection needs a light". The builder signals any intersection with >= 3 real
        // arms, so a 1x2/2x1 grid (whose middle points are T-intersections) or a Custom shape with
        // a T got lights, while the validator only asked for the prefab on a grid larger than 1x1
        // (or a Custom shape containing a full 2x2 of cells). With no Traffic Light prefab set,
        // that configuration passed validation and then instantiated a null prefab mid-generation.
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void ValidateDetailed_ThinGridWithoutTrafficLightPrefab_IsBlocking(int gridWidth, int gridHeight)
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = gridWidth;
            settings.general.gridHeight = gridHeight;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        // The lights are built even with Include Traffic off (CityGeneratorContentAssembler keeps
        // every intersection regulated regardless), so the prefab requirement must not depend on it.
        [Test]
        public void ValidateDetailed_ThinGridWithTrafficDisabled_StillRequiresTrafficLightPrefab()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 1;
            settings.general.gridHeight = 2;
            settings.general.includeTraffic = false;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_ThinGridWithTrafficLightPrefab_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 1;
            settings.general.gridHeight = 2;
            GameObject light = MakePrefabLike("TrafficLight");
            light.AddComponent<CityGenerator.Runtime.TrafficLight>();
            settings.props.trafficLightPrefab = light;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        // A 1x1 grid's four corners have exactly 2 perpendicular arms each: no decision point, so
        // no light is built and none is required. This is the boundary the fix must not cross.
        [Test]
        public void ValidateDetailed_SingleBlockGridWithoutTrafficLightPrefab_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 1;
            settings.general.gridHeight = 1;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        // Custom Grid counterparts: a domino and an L-triomino both contain T-intersections but no
        // full 2x2 of cells, which is exactly what the old validator predicate looked for.
        [Test]
        public void ValidateDetailed_CustomGrid_DominoWithoutTrafficLightPrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5), new(6, 5) };

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_CustomGrid_LTriominoWithoutTrafficLightPrefab_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5), new(6, 5), new(5, 6) };

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "props.trafficLightPrefab" && !i.isWarning));
        }

        [Test]
        public void ValidateDetailed_CustomGrid_SingleCellWithoutTrafficLightPrefab_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5) };
            settings.ground.emptyBlockPrefab = MakePrefabLike("EmptyBlock");

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void Validate_MirrorsValidateDetailed_OnlyReturningBlockingMessages()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.buildingPrefabs.Add(null); // warning only

            bool valid = CityGeneratorValidator.Validate(settings, out List<string> errors);

            Assert.IsTrue(valid);
            Assert.AreEqual(0, errors.Count);
        }

        // SPEC 21: Districts. The minimal settings are a 1x1 grid, so (0, 0) is the only block.
        private DistrictEntry MakeValidDistrict(string name) => new()
        {
            name = name,
            cells = new List<Vector2Int> { new(0, 0) },
            buildingPrefabs = new List<GameObject> { MakePrefabLike(name + "Building") },
        };

        private GameObject MakeTrafficLightPrefab()
        {
            GameObject light = MakePrefabLike("TrafficLight");
            light.AddComponent<CityGenerator.Runtime.TrafficLight>();
            return light;
        }

        private static void AssertBlockingDistrictIssue(bool valid, List<CityGeneratorValidationIssue> issues)
        {
            Assert.IsFalse(valid);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && !i.isWarning), string.Join("; ", issues.ConvertAll(i => i.message)));
        }

        [Test]
        public void ValidateDetailed_ValidDistrict_HasNoBlockingIssues()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.districts.Add(MakeValidDistrict("Downtown"));

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
            Assert.IsFalse(issues.Exists(i => i.settingsPath == "districts"));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void ValidateDetailed_DistrictWithoutName_IsBlocking(string name)
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.districts.Add(MakeValidDistrict("Downtown"));
            DistrictEntry entry = settings.districts[0];
            entry.name = name;
            settings.districts[0] = entry;

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
        }

        [Test]
        public void ValidateDetailed_DuplicateDistrictName_IgnoringCaseAndSpaces_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.gridWidth = 2; // a 1x2 grid: two blocks, so the names are the only conflict
            settings.props.trafficLightPrefab = MakeTrafficLightPrefab();
            settings.districts.Add(MakeValidDistrict("Downtown"));
            DistrictEntry second = MakeValidDistrict(" downtown ");
            second.cells = new List<Vector2Int> { new(1, 0) };
            settings.districts.Add(second);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && i.message.Contains("unique")));
        }

        [Test]
        public void ValidateDetailed_DistrictWithoutBlocks_IsBlocking([Values(true, false)] bool nullList)
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            DistrictEntry entry = MakeValidDistrict("Downtown");
            entry.cells = nullList ? null : new List<Vector2Int>();
            settings.districts.Add(entry);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
        }

        [Test]
        public void ValidateDetailed_DistrictBlockOutsideGrid_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            DistrictEntry entry = MakeValidDistrict("Downtown");
            entry.cells.Add(new Vector2Int(1, 0));
            settings.districts.Add(entry);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
        }

        [Test]
        public void ValidateDetailed_DistrictBlockMissingFromCustomShape_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int> { new(5, 5) };
            settings.ground.emptyBlockPrefab = MakePrefabLike("EmptyBlock");
            DistrictEntry entry = MakeValidDistrict("Downtown");
            entry.cells = new List<Vector2Int> { new(5, 5), new(6, 5) };
            settings.districts.Add(entry);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && i.message.Contains("(6, 5)")));
            Assert.IsFalse(issues.Exists(i => i.settingsPath == "districts" && i.message.Contains("(5, 5)")));
        }

        [Test]
        public void ValidateDetailed_DistrictPlazaBlock_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.general.plazaCells = new List<Vector2Int> { new(0, 0) };
            settings.plaza.lawnPrefab = MakePrefabLike("Lawn");
            settings.districts.Add(MakeValidDistrict("Downtown"));

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
        }

        [Test]
        public void ValidateDetailed_BlockInTwoDistricts_IsBlocking()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.districts.Add(MakeValidDistrict("Downtown"));
            settings.districts.Add(MakeValidDistrict("Uptown"));

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && i.message.Contains("'Downtown'") && i.message.Contains("'Uptown'")));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        public void ValidateDetailed_DistrictWithNoUsablePrefab_IsBlocking(int nullEntries)
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            DistrictEntry entry = MakeValidDistrict("Downtown");
            entry.buildingPrefabs = nullEntries < 0 ? null : new List<GameObject>();
            for (int i = 0; i < nullEntries; i++)
                entry.buildingPrefabs.Add(null);
            settings.districts.Add(entry);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            AssertBlockingDistrictIssue(valid, issues);
        }

        [Test]
        public void ValidateDetailed_DistrictNullEntryOrRendererlessPrefab_OnlyWarns()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            DistrictEntry entry = MakeValidDistrict("Downtown");
            entry.buildingPrefabs.Add(null);
            entry.buildingPrefabs.Add(MakePrefabLikeNoRenderer("Hollow"));
            settings.districts.Add(entry);

            bool valid = CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            Assert.IsTrue(valid, string.Join("; ", issues.ConvertAll(i => i.message)));
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && i.isWarning && i.message.Contains("is empty and will be skipped")));
            Assert.IsTrue(issues.Exists(i => i.settingsPath == "districts" && i.isWarning && i.message.Contains("has no Renderer")));
        }
    }
}
