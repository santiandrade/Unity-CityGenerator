using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Immutable handle to one generated city. Wraps a <see cref="CityGeneratorInfo"/> reference;
    /// holds no copied data, so a handle can never go stale relative to its city -- every read goes
    /// straight through to the wrapped component. Obtained from <see cref="CityGeneratorAPI"/>,
    /// never constructed directly.
    /// </summary>
    public readonly struct CityGeneratorCity : IEquatable<CityGeneratorCity>
    {
        private readonly CityGeneratorInfo info;

        internal CityGeneratorCity(CityGeneratorInfo info)
        {
            this.info = info;
        }

        /// <summary>False once the underlying city has been destroyed.</summary>
        public bool IsValid => info != null;

        /// <summary>False when the city's root is deactivated -- it is then absent from All/Default/InScene too.</summary>
        public bool IsActive => info != null && info.isActiveAndEnabled;

        public Scene Scene => info != null ? info.gameObject.scene : default;

        /// <summary>Escape hatch to the underlying component. Null when <see cref="IsValid"/> is false.</summary>
        public CityGeneratorInfo Info => info;

        public CityModule City => new(info);
        public PlayerModule Player => new(info);
        public TrafficModule Traffic => new(info);
        public PedestriansModule Pedestrians => new(info);
        public MinimapModule Minimap => new(info);
        public AudioModule Audio => new(info);

        public bool Equals(CityGeneratorCity other) => info == other.info;
        public override bool Equals(object obj) => obj is CityGeneratorCity other && Equals(other);
        public override int GetHashCode() => info != null ? info.GetHashCode() : 0;
        public static bool operator ==(CityGeneratorCity left, CityGeneratorCity right) => left.Equals(right);
        public static bool operator !=(CityGeneratorCity left, CityGeneratorCity right) => !left.Equals(right);
    }

    public readonly struct CityModule
    {
        private readonly CityGeneratorInfo info;
        internal CityModule(CityGeneratorInfo info) => this.info = info;

        public bool IsCustomGrid => info != null && info.useCustomGrid;
        public Vector2Int GridSize => info != null ? info.gridSize : Vector2Int.zero;
        public int BlockCount => info != null ? info.blockCount : 0;
        public int BuildingCount => info != null ? info.buildingCount : 0;
        public int PlazaCount => info != null ? info.plazaCount : 0;
        public int CustomPlaceCount => info != null ? info.customPlaceCount : 0;
        public int LampCount => info != null ? info.lampCount : 0;
        public int BinCount => info != null ? info.binCount : 0;
        public int StreetTreeCount => info != null ? info.streetTreeCount : 0;
        public int TrafficLightCount => info != null ? info.trafficLightCount : 0;
        public bool IsSeeded => info != null && info.useCustomSeed;
        public int Seed => info != null ? info.seed : 0;

        public bool IsDayNightEnabled => info != null && info.dayNightCycle != null && info.dayNightCycle.enabled;
        public float CurrentHour => info != null && info.dayNightCycle != null ? info.dayNightCycle.currentHour : 0f;

        public void SetDayNightEnabled(bool enabled)
        {
            if (info != null && info.dayNightCycle != null)
                info.dayNightCycle.enabled = enabled;
        }

        public void SetHour(float hour)
        {
            if (info != null && info.dayNightCycle != null)
                info.dayNightCycle.ApplySun(hour);
        }
    }

    public readonly struct PlayerModule
    {
        private readonly CityGeneratorInfo info;
        internal PlayerModule(CityGeneratorInfo info) => this.info = info;

        public bool IsEnabled => info != null && info.playerEnabled && info.player != null;
        public Vector3 Position => info != null && info.player != null ? info.player.position : Vector3.zero;
        public bool IsFreeViewActive => info != null && info.freeCameraController != null && info.freeCameraController.IsActive;
    }

    public readonly struct TrafficModule
    {
        private readonly CityGeneratorInfo info;
        internal TrafficModule(CityGeneratorInfo info) => this.info = info;

        public bool IsEnabled => info != null && info.trafficEnabled;
        public int VehicleCount => info != null && info.trafficManager != null ? info.trafficManager.AgentCount : 0;
    }

    public readonly struct PedestriansModule
    {
        private readonly CityGeneratorInfo info;
        internal PedestriansModule(CityGeneratorInfo info) => this.info = info;

        public bool IsEnabled => info != null && info.pedestriansEnabled;
        public int Count => info != null && info.pedestrianManager != null ? info.pedestrianManager.AgentCount : 0;
        public int CustomCount => info != null ? info.customPedestrianCount : 0;
    }

    public readonly struct MinimapModule
    {
        private readonly CityGeneratorInfo info;
        internal MinimapModule(CityGeneratorInfo info) => this.info = info;

        public bool IsEnabled => info != null && info.minimapHUD != null;
        public int PointOfInterestCount => info != null && info.minimapData != null ? info.minimapData.pointsOfInterest.Count : 0;
        public float ViewRadiusMeters => info != null && info.minimapHUD != null ? info.minimapHUD.ViewRadiusMeters : 0f;
        public bool IsVisible => info != null && info.minimapHUD != null && info.minimapHUD.gameObject.activeSelf;

        public void SetViewRadiusMeters(float meters)
        {
            if (info != null && info.minimapHUD != null)
                info.minimapHUD.ViewRadiusMeters = meters;
        }

        public void SetVisible(bool visible)
        {
            if (info != null && info.minimapHUD != null)
                info.minimapHUD.gameObject.SetActive(visible);
        }
    }

    public readonly struct AudioModule
    {
        private readonly CityGeneratorInfo info;
        internal AudioModule(CityGeneratorInfo info) => this.info = info;

        public bool IsAmbienceEnabled => info != null && info.ambienceEnabled;
        public int AmbienceClipCount => info != null ? info.ambienceClipCount : 0;
        public bool IsPlazaAudioEnabled => info != null && info.plazaAudioEnabled;
        public int PlazaAudioSourceCount => info != null ? info.plazaAudioSourceCount : 0;
    }
}
