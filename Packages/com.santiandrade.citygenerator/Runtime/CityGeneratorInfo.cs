using System.Collections.Generic;
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

        [Header("Districts")]
        [Tooltip("Distance in metres between the centres of two adjacent blocks (CityGeneratorConstants.CellPitch at build time).")]
        public float cellPitch;
        [Tooltip("Block positions per axis that block centres are laid out on: (gridWidth, gridHeight), or MaxGridSize x MaxGridSize with Custom Grid.")]
        public Vector2Int layoutSize;
        [Tooltip("Districts of this city in the generator's order; empty when it has none or was built before SPEC 23.")]
        public List<CityDistrict> districts = new();

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

        // SPEC 23: cell -> index into districts, built on the first query and never serialized (the
        // serialized list is the single copy of the data). Dropped by OnValidate and by
        // InvalidateDistrictLookup whenever the fields above are rewritten.
        private Dictionary<Vector2Int, int> districtLookup;

        private void OnEnable() => CityGeneratorAPI.Register(this);
        private void OnDisable() => CityGeneratorAPI.Unregister(this);
        private void OnValidate() => InvalidateDistrictLookup();

        /// <summary>Forces the next district query to rebuild its cell index from <see cref="districts"/>.</summary>
        internal void InvalidateDistrictLookup() => districtLookup = null;

        /// <summary>
        /// SPEC 23: the block cell whose centre is nearest to <paramref name="worldPosition"/> on each
        /// axis -- the exact inverse of CityGeneratorGrid.GetBlockAxisPosition, worked in this root's
        /// local space (like the traffic/pedestrian graphs since SPEC 16) so a moved city still
        /// answers correctly. The border between two cells is the axis of the street between them.
        /// False outside the layout canvas, or for a city built before SPEC 23 (no pitch/canvas).
        /// </summary>
        internal bool TryGetCell(Vector3 worldPosition, out Vector2Int cell)
        {
            cell = default;
            if (cellPitch <= 0f || layoutSize.x <= 0 || layoutSize.y <= 0)
                return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            int gx = Mathf.RoundToInt(local.x / cellPitch + (layoutSize.x - 1) / 2f);
            int gy = Mathf.RoundToInt(local.z / cellPitch + (layoutSize.y - 1) / 2f);
            if (gx < 0 || gy < 0 || gx >= layoutSize.x || gy >= layoutSize.y)
                return false;

            cell = new Vector2Int(gx, gy);
            return true;
        }

        /// <summary>
        /// SPEC 23: index into <see cref="districts"/> of the district owning <paramref name="cell"/>,
        /// or -1 and false when none does. A cell listed by two districts (a validation error a
        /// hand-made city could still carry) resolves to the first, as the building builder does.
        /// </summary>
        internal bool TryGetDistrictIndex(Vector2Int cell, out int districtIndex)
        {
            districtIndex = -1;
            if (districts == null || districts.Count == 0)
                return false;

            districtLookup ??= BuildDistrictLookup();
            if (districtLookup.TryGetValue(cell, out int index))
            {
                districtIndex = index;
                return true;
            }
            return false;
        }

        private Dictionary<Vector2Int, int> BuildDistrictLookup()
        {
            var lookup = new Dictionary<Vector2Int, int>();
            for (int d = 0; d < districts.Count; d++)
            {
                List<Vector2Int> cells = districts[d].cells;
                if (cells == null)
                    continue;
                foreach (Vector2Int cell in cells)
                    lookup.TryAdd(cell, d);
            }
            return lookup;
        }
    }
}
