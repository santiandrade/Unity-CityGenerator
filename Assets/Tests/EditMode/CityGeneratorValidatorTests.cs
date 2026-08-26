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
            settings.ground.roadBasePrefab = MakePrefabLike("RoadBase");
            settings.ground.sidewalkPrefab = MakePrefabLike("Sidewalk");
            settings.ground.roadLinePrefab = MakePrefabLike("RoadLine");
            settings.ground.crosswalkLinePrefab = MakePrefabLike("Crosswalk");
            settings.vegetation.density = 0f; // avoid the unrelated "no prefabs" issue in tests not about vegetation
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

        [Test]
        public void Validate_MirrorsValidateDetailed_OnlyReturningBlockingMessages()
        {
            CityGeneratorSettings settings = MakeMinimalValidSettings();
            settings.buildingPrefabs.Add(null); // warning only

            bool valid = CityGeneratorValidator.Validate(settings, out List<string> errors);

            Assert.IsTrue(valid);
            Assert.AreEqual(0, errors.Count);
        }
    }
}
