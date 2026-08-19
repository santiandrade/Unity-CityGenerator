using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Fills a fresh <see cref="CityGeneratorSettings"/> with this project's own reference-city
    /// prefabs, so the tool opens ready for a quick first generation instead of a wall of empty
    /// required fields. This is the one place in the tool that is project-specific rather than
    /// portable — strip or repoint this file when distributing the tool to another project.
    /// Every path is resolved defensively (silently left null if missing), since a project
    /// without these assets must still get an otherwise-empty, working settings object.
    /// </summary>
    internal static class CityGeneratorDefaultAssets
    {
        public static void ApplyTo(CityGeneratorSettings settings)
        {
            settings.general.playerPrefab = Load("Assets/Prefabs/Characters/Player.prefab");
            settings.general.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");

            settings.ground.roadBasePrefab = Load("Assets/Prefabs/Floors/RoadBase.prefab");
            settings.ground.sidewalkPrefab = Load("Assets/Prefabs/Floors/RoadSidewalk.prefab");
            settings.ground.roadLinePrefab = Load("Assets/Prefabs/Floors/RoadDash.prefab");
            settings.ground.crosswalkLinePrefab = Load("Assets/Prefabs/Floors/RoadZebra.prefab");

            settings.plaza.centerpiecePrefab = Load("Assets/Prefabs/Props/Fountain.prefab");
            settings.plaza.lawnPrefab = Load("Assets/Prefabs/Floors/Lawn.prefab");
            settings.plaza.benchPrefab = Load("Assets/Prefabs/Props/Bench.prefab");

            settings.buildingPrefabs = new List<GameObject>
            {
                Load("Assets/Prefabs/Buildings/Building-A.prefab"),
                Load("Assets/Prefabs/Buildings/Building-F.prefab"),
                Load("Assets/Prefabs/Buildings/Building-I.prefab"),
                Load("Assets/Prefabs/Buildings/Building-M.prefab"),
                Load("Assets/Prefabs/Buildings/Building-Skyscraper-C.prefab"),
                Load("Assets/Prefabs/Buildings/Building-Skyscraper-E.prefab"),
            };

            settings.vegetation.prefabs = new List<GameObject>
            {
                Load("Assets/Prefabs/Vegetation/Tree.prefab"),
            };

            settings.vehicles = new List<VehicleEntry>
            {
                new() { prefab = Load("Assets/Prefabs/Vehicles/DeliveryCar.prefab"), percentage = 25f },
                new() { prefab = Load("Assets/Prefabs/Vehicles/PoliceCar.prefab"), percentage = 10f },
                new() { prefab = Load("Assets/Prefabs/Vehicles/SedanSportCar.prefab"), percentage = 5f },
                new() { prefab = Load("Assets/Prefabs/Vehicles/TaxiCar.prefab"), percentage = 60f },
            };

            settings.props.trafficLightPrefab = Load("Assets/Prefabs/Props/TrafficLight.prefab");
            settings.props.lampPrefab = Load("Assets/Prefabs/Props/Lamp.prefab");
            settings.props.busStopPrefab = Load("Assets/Prefabs/Props/BusStop.prefab");
            settings.props.binPrefab = Load("Assets/Prefabs/Props/Bin.prefab");
        }

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
}
