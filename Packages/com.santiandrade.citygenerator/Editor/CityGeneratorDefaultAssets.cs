using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Fills a fresh <see cref="CityGeneratorSettings"/> with the package's own demo prefabs,
    /// so the tool opens ready for a quick first generation instead of a wall of empty required
    /// fields. The demo content ships inside the package's Demo/ folder, so these paths resolve
    /// in any project that installs the package, not just this one.
    /// Every path is resolved defensively (silently left null if missing), since a project
    /// without these assets must still get an otherwise-empty, working settings object.
    /// Regenerated wholesale by <see cref="CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault"/>
    /// ("Tools &gt; City Generator &gt; Set Current Selection As Default") — hand edits survive
    /// until the next time that command runs.
    /// </summary>
    internal static class CityGeneratorDefaultAssets
    {
        private const string DefaultAssetsRoot = "Packages/com.santiandrade.citygenerator/DefaultAssets";

        public static void ApplyTo(CityGeneratorSettings settings)
        {
            settings.general.playerPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Characters/Character-Male-D.prefab");
            settings.general.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>($"{DefaultAssetsRoot}/Input/InputSystem_Actions.inputactions");

            settings.ground.roadBasePrefab = Load($"{DefaultAssetsRoot}/Prefabs/Floors/RoadBase.prefab");
            settings.ground.sidewalkPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Floors/RoadSidewalk.prefab");
            settings.ground.roadLinePrefab = Load($"{DefaultAssetsRoot}/Prefabs/Floors/RoadDash.prefab");
            settings.ground.crosswalkLinePrefab = Load($"{DefaultAssetsRoot}/Prefabs/Floors/RoadZebra.prefab");

            settings.plaza.centerpiecePrefab = Load($"{DefaultAssetsRoot}/Prefabs/Props/Fountain.prefab");
            settings.plaza.lawnPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Floors/Lawn.prefab");
            settings.plaza.benchPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Props/Bench.prefab");

            settings.buildingPrefabs = new List<GameObject>
            {
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-A.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-B.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-C.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-D.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-E.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-F.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-G.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-H.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-I.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-L.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-M.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-Skyscraper-A.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-Skyscraper-B.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-Skyscraper-C.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-Skyscraper-D.prefab"),
                Load($"{DefaultAssetsRoot}/Prefabs/Buildings/Building-Skyscraper-E.prefab"),
            };

            settings.vegetation.prefabs = new List<GameObject>
            {
                Load($"{DefaultAssetsRoot}/Prefabs/Vegetation/Tree.prefab"),
            };

            settings.vehicles = new List<VehicleEntry>
            {
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Ambulance.prefab"), percentage = 1f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Delivery-Flat.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/DeliveryCar.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Firetruck.prefab"), percentage = 1f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Garbage-Truck.prefab"), percentage = 2f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Hatchback-Sports.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/PoliceCar.prefab"), percentage = 3f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Sedan.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/SedanSportCar.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Suv.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Suv-Luxury.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/TaxiCar.prefab"), percentage = 23f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Truck.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Truck-Flat.prefab"), percentage = 7f },
                new() { prefab = Load($"{DefaultAssetsRoot}/Prefabs/Vehicles/Van.prefab"), percentage = 7f },
            };

            settings.props.trafficLightPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Props/TrafficLight.prefab");
            settings.props.lampPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Props/Lamp.prefab");
            settings.props.binPrefab = Load($"{DefaultAssetsRoot}/Prefabs/Props/Bin.prefab");
        }

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
}
