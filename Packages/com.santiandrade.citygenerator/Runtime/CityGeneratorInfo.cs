using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Added to the root of every generated city (alongside CityGeneratorRoot) by
    /// CityGeneratorSceneBuilder/CityGeneratorContentAssembler on every Build/Re-Build. Ships in
    /// Runtime so it also exists in player builds, not just the Editor; CityGeneratorCity reads it
    /// as its single source of truth instead of each module resolving its own references.
    /// Registers itself with CityGeneratorAPI on OnEnable/OnDisable, the same lifecycle pattern
    /// TrafficManager/PedestrianManager use for their agents -- never a global search.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class CityGeneratorInfo : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("True when this city was generated with Custom Grid (customBlockCells) instead of a rectangular gridWidth x gridHeight.")]
        public bool useCustomGrid;
        [Tooltip("Rectangular grid: (gridWidth, gridHeight). Custom Grid: bounding box of the real cells.")]
        public Vector2Int gridSize;
        public int blockCount;

        [Header("Content counts")]
        public int buildingCount;
        public int plazaCount;
        public int customPlaceCount;
        public int lampCount;
        public int binCount;
        public int streetTreeCount;
        public int trafficLightCount;
        [Tooltip("Configured Custom Pedestrian entry count (settings.customPedestrians), not a live agent count.")]
        public int customPedestrianCount;

        [Header("Seed")]
        public bool useCustomSeed;
        public int seed;

        [Header("Feature flags (from GeneralSettings at build time)")]
        public bool playerEnabled;
        public bool trafficEnabled;
        public bool pedestriansEnabled;

        [Header("Audio")]
        public bool ambienceEnabled;
        public int ambienceClipCount;
        public bool plazaAudioEnabled;
        public int plazaAudioSourceCount;

        [Header("Component references (resolved once at build time)")]
        public Transform player;
        public FreeCameraController freeCameraController;
        public TrafficManager trafficManager;
        public PedestrianManager pedestrianManager;
        public DayNightCycle dayNightCycle;
        public MinimapHUD minimapHUD;
        public MinimapData minimapData;

        private void OnEnable() => CityGeneratorAPI.Register(this);
        private void OnDisable() => CityGeneratorAPI.Unregister(this);
    }
}
