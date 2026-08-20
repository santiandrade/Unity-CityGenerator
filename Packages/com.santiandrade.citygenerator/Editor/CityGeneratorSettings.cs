using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Editor
{
    [Serializable]
    internal class CityGeneratorSettings
    {
        public GeneralSettings general = new();
        public GroundSettings ground = new();
        public PlazaSettings plaza = new();
        public List<GameObject> buildingPrefabs = new();
        public VegetationSettings vegetation = new();
        public List<VehicleEntry> vehicles = new();
        public PropsSettings props = new();
    }

    [Serializable]
    internal class GeneralSettings
    {
        public int gridWidth = 5;
        public int gridHeight = 5;
        public int plazaCount = 1;
        public int buildingsPerBlock = 4; // clamped 0-4
        public bool includeTraffic = true;
        public int vehicleCount = 80;
        public bool useCustomSeed = false;
        public int seed = 0;
        public GameObject playerPrefab; // optional
        public InputActionAsset inputActions; // required if playerPrefab is set (drives the generated camera's Look input)
    }

    [Serializable]
    internal class GroundSettings
    {
        public GameObject roadBasePrefab; // required
        public GameObject sidewalkPrefab; // required
        public GameObject roadLinePrefab; // required
        public GameObject crosswalkLinePrefab; // required
    }

    [Serializable]
    internal class PlazaSettings
    {
        public GameObject centerpiecePrefab; // optional
        public GameObject lawnPrefab; // required if plazaCount > 0
        public GameObject benchPrefab; // optional
    }

    [Serializable]
    internal class VegetationSettings
    {
        public List<GameObject> prefabs = new(); // 1+ required if density > 0
        [Range(0f, 1f)] public float density = 0.2f;
    }

    [Serializable]
    internal class VehicleEntry
    {
        public GameObject prefab;
        [Range(0f, 100f)] public float percentage;
    }

    [Serializable]
    internal class PropsSettings
    {
        public GameObject trafficLightPrefab; // required if includeTraffic
        public GameObject lampPrefab; // optional
        [Range(0f, 1f)] public float lampDensity = 1f; // 1 = every candidate point along the sidewalk (3 per side)
        public GameObject binPrefab;
        [Range(0f, 1f)] public float binDensity = 0.3f;
    }
}
