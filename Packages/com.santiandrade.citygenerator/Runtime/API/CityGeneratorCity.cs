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

        /// <summary>SPEC 23: number of Districts stored in this city at its last Build/Re-Build. 0 for a
        /// city without districts, or built before SPEC 23 and not regenerated since.</summary>
        public int DistrictCount => info != null && info.districts != null ? info.districts.Count : 0;

        /// <summary>SPEC 23: name of district <paramref name="districtIndex"/>, trimmed, as set in the
        /// generator. Null when the index is out of range.</summary>
        public string GetDistrictName(int districtIndex)
        {
            return districtIndex >= 0 && districtIndex < DistrictCount ? info.districts[districtIndex].name : null;
        }

        /// <summary>
        /// SPEC 23: the district whose block is nearest to <paramref name="worldPosition"/> (per axis,
        /// so a block's area reaches to the axis of each street around it and a crossing is split
        /// between its four blocks). Returns false and -1 outside the city's layout canvas, on a block
        /// in no district, on a plaza, and on an empty Custom Grid cell. O(1) and allocation-free
        /// after the first call, so it can be called every frame.
        /// </summary>
        public bool TryGetDistrictAt(Vector3 worldPosition, out int districtIndex)
        {
            districtIndex = -1;
            return info != null
                && DistrictCount > 0
                && info.TryGetCell(worldPosition, out Vector2Int cell)
                && info.TryGetDistrictIndex(cell, out districtIndex);
        }

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

        /// <summary>SPEC 20: where the traffic measures staggering and physics-LOD distances from
        /// (e.g. the player's car). Null restores the main camera. No-op without traffic.</summary>
        public void SetSimulationFocus(Transform focus)
        {
            if (info != null && info.trafficManager != null)
                info.trafficManager.SimulationFocus = focus;
        }

        /// <summary>SPEC 20: sensor staggering beyond <paramref name="distance"/> of the focus, 1 of
        /// <paramref name="frames"/> ticks; with <paramref name="invisibleDistance"/> &gt; 0, also for
        /// cars beyond that distance that no camera renders. No-op without traffic.</summary>
        public void SetStagger(float distance, int frames, float invisibleDistance = 0f)
        {
            if (info != null && info.trafficManager != null)
                info.trafficManager.SetStagger(distance, frames, invisibleDistance);
        }

        /// <summary>SPEC 20: physics-mode cars beyond <paramref name="meters"/> of the focus drive as
        /// kinematic bodies; 0 turns it off. No-op without traffic or with Enable Physics off.</summary>
        public void SetPhysicsRadius(float meters)
        {
            if (info != null && info.trafficManager != null)
                info.trafficManager.PhysicsRadius = meters;
        }
    }

    public readonly struct PedestriansModule
    {
        private readonly CityGeneratorInfo info;
        internal PedestriansModule(CityGeneratorInfo info) => this.info = info;

        public bool IsEnabled => info != null && info.pedestriansEnabled;
        public int Count => info != null && info.pedestrianManager != null ? info.pedestrianManager.AgentCount : 0;
        public int CustomCount => info != null ? info.customPedestrianCount : 0;

        /// <summary>SPEC 20: where pedestrians measure staggering and collider-LOD distances from.
        /// Null restores the main camera. No-op without pedestrians.</summary>
        public void SetSimulationFocus(Transform focus)
        {
            if (info != null && info.pedestrianManager != null)
                info.pedestrianManager.SimulationFocus = focus;
        }

        /// <summary>SPEC 20: decision-logic staggering beyond <paramref name="distance"/> of the focus,
        /// 1 of <paramref name="frames"/> frames; with <paramref name="invisibleDistance"/> &gt; 0, also
        /// for agents beyond that distance that no camera renders. No-op without pedestrians.</summary>
        public void SetStagger(float distance, int frames, float invisibleDistance = 0f)
        {
            if (info != null && info.pedestrianManager != null)
                info.pedestrianManager.SetStagger(distance, frames, invisibleDistance);
        }

        /// <summary>SPEC 20: pedestrians beyond <paramref name="meters"/> of the focus have their root
        /// collider off while cars find them through the proximity grid; 0 turns it off. A collider
        /// switched back on is paired again with the physics vehicles, but loses any ignore pair the
        /// project's own code had set on it. No-op without pedestrians.</summary>
        public void SetColliderRadius(float meters)
        {
            if (info != null && info.pedestrianManager != null)
                info.pedestrianManager.ColliderRadius = meters;
        }

        /// <summary>SPEC 20: pedestrians beyond <paramref name="meters"/> of the focus skip their
        /// ground raycast while walking a stretch that lies on one flat surface, and cast it only on
        /// their logic frames while staggered; 0 turns it off. No-op without pedestrians.</summary>
        public void SetGroundingShortcutDistance(float meters)
        {
            if (info != null && info.pedestrianManager != null)
                info.pedestrianManager.GroundingShortcutDistance = meters;
        }
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

        /// <summary>Adds a marker that follows a Transform in world space.</summary>
        public MinimapMarkerHandle AddMarker(Transform target, RectTransform prefab, bool clampToEdge)
        {
            return info != null && info.minimapHUD != null
                ? info.minimapHUD.AddMarker(target, prefab, clampToEdge)
                : default;
        }

        /// <summary>Adds a marker at a fixed world-space position.</summary>
        public MinimapMarkerHandle AddMarker(Vector3 position, RectTransform prefab, bool clampToEdge)
        {
            return info != null && info.minimapHUD != null
                ? info.minimapHUD.AddMarker(position, prefab, clampToEdge)
                : default;
        }

        /// <summary>Removes a marker from this city's HUD. Invalid or foreign handles are ignored.</summary>
        public void RemoveMarker(MinimapMarkerHandle handle)
        {
            if (info != null && info.minimapHUD != null)
                info.minimapHUD.RemoveMarker(handle);
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
