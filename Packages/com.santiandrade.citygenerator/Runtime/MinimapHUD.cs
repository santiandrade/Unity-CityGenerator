using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Circular minimap HUD, built by <c>CityGeneratorSceneBuilder</c> from the package's
    /// <c>DefaultAssets/Prefabs/MinimapHUD.prefab</c> (Canvas Screen Space Overlay + a circle-masked
    /// <see cref="RawImage"/>) and driven entirely from the <see cref="MinimapData"/> left on the
    /// city root by <c>CityGeneratorMinimapBuilder</c>: never recalculates <see cref="MinimapData.worldOrigin"/>/
    /// <see cref="MinimapData.worldSize"/> on its own, so the two stay in sync by construction.
    /// The map itself never rotates (north stays up) — only <see cref="playerMarker"/> rotates, to
    /// reflect the player's yaw.
    /// <para>
    /// The window is always centred on the player, so near a city edge it necessarily reaches past
    /// the snapshot: <see cref="UpdateMapWindow"/> writes a <see cref="RawImage.uvRect"/> that
    /// leaves the [0, 1] range. That is handled in the prefab's material, not here — the map
    /// <see cref="RawImage"/> uses <c>DefaultAssets/Materials/MinimapWindow.mat</c>, whose shader
    /// paints anything outside [0, 1] with the snapshot's own background colour. Swapping it back
    /// to the stock UI material brings back the bug it fixes: the snapshot's Clamp wrap mode smears
    /// its outermost row/column of pixels across the rest of the minimap.
    /// </para>
    /// </summary>
    public class MinimapHUD : MonoBehaviour
    {
        [Tooltip("Radius, in meters, of the world area visible around the player. Written by CityGeneratorSceneBuilder from MinimapSettings.viewRadiusMeters.")]
        [SerializeField] private float viewRadiusMeters = 60f;

        /// <summary>Radius, in meters, of the world area visible around the player. Settable at runtime via CityGeneratorCity.Minimap.SetViewRadiusMeters.</summary>
        public float ViewRadiusMeters
        {
            get => viewRadiusMeters;
            set => viewRadiusMeters = value;
        }

        [Tooltip("Displays MinimapData.snapshot, windowed via uvRect to the area within View Radius Meters around the player.")]
        [SerializeField] private RawImage mapImage;
        [Tooltip("Fixed at the HUD's centre; rotates to reflect the player's current yaw. The map itself never rotates.")]
        [SerializeField] private RectTransform playerMarker;
        [Tooltip("Deactivated template cloned once per visible Point of Interest; reused as the single generic icon+label for every POI.")]
        [SerializeField] private RectTransform poiMarkerTemplate;
        [Tooltip("Parent for POI marker clones.")]
        [SerializeField] private RectTransform poiMarkerContainer;

        private MinimapData data;
        private PlayerController player;
        private readonly List<RectTransform> poiMarkerPool = new();

        private void Start()
        {
            player = FindAnyObjectByType<PlayerController>();
            data = player != null ? FindDataForPlayer(player.transform.position) : null;

            if (data == null || player == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (mapImage != null)
                mapImage.texture = data.snapshot;

            EnsurePoiMarkerPool(data.pointsOfInterest.Count);
        }

        /// <summary>
        /// With several cities in the scene (SPEC 16), picks the one whose captured footprint
        /// actually contains the player -- falling back to the closest by centre distance if none
        /// does (e.g. the player standing just outside every captured worldOrigin/worldSize, or a
        /// zero-sized footprint left by a MinimapData never captured). With a single city this
        /// always resolves to it, same as the old FindAnyObjectByType<MinimapData>() behaviour.
        /// </summary>
        private static MinimapData FindDataForPlayer(Vector3 playerPosition)
        {
            MinimapData[] candidates = FindObjectsByType<MinimapData>(FindObjectsInactive.Exclude);
            if (candidates.Length == 0)
                return null;

            MinimapData closest = null;
            float closestDistance = float.MaxValue;

            foreach (MinimapData candidate in candidates)
            {
                Vector2 origin = candidate.worldOrigin;
                Vector2 size = candidate.worldSize;
                if (playerPosition.x >= origin.x && playerPosition.x <= origin.x + size.x &&
                    playerPosition.z >= origin.y && playerPosition.z <= origin.y + size.y)
                {
                    return candidate;
                }

                Vector2 centre = origin + size * 0.5f;
                float distance = (new Vector2(playerPosition.x, playerPosition.z) - centre).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = candidate;
                }
            }

            return closest;
        }

        private void LateUpdate()
        {
            if (data == null || player == null || mapImage == null)
                return;

            Vector3 playerPosition = player.transform.position;
            UpdateMapWindow(playerPosition);
            UpdatePlayerMarker();
            UpdatePoiMarkers(playerPosition);
        }

        private void UpdateMapWindow(Vector3 playerPosition)
        {
            Vector2 worldOrigin = data.worldOrigin;
            Vector2 worldSize = data.worldSize;
            if (worldSize.x <= 0f || worldSize.y <= 0f)
                return;

            float uWidth = 2f * viewRadiusMeters / worldSize.x;
            float vHeight = 2f * viewRadiusMeters / worldSize.y;
            float u0 = (playerPosition.x - worldOrigin.x - viewRadiusMeters) / worldSize.x;
            float v0 = (playerPosition.z - worldOrigin.y - viewRadiusMeters) / worldSize.y;

            // Deliberately not clamped into [0, 1]: keeping the player exactly at the HUD's centre
            // matters more than staying inside the snapshot, and the out-of-range remainder is
            // painted as background by MinimapWindow.mat (see the class remarks).
            mapImage.uvRect = new Rect(u0, v0, uWidth, vHeight);
        }

        private void UpdatePlayerMarker()
        {
            if (playerMarker == null)
                return;

            float yaw = player.transform.eulerAngles.y;
            playerMarker.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }

        private void UpdatePoiMarkers(Vector3 playerPosition)
        {
            List<PointOfInterestEntry> pointsOfInterest = data.pointsOfInterest;
            EnsurePoiMarkerPool(pointsOfInterest.Count);

            RectTransform mapRect = mapImage.rectTransform;
            float pixelsPerMeter = mapRect.rect.width / (2f * viewRadiusMeters);

            for (int i = 0; i < poiMarkerPool.Count; i++)
            {
                RectTransform marker = poiMarkerPool[i];
                if (i >= pointsOfInterest.Count)
                {
                    marker.gameObject.SetActive(false);
                    continue;
                }

                PointOfInterestEntry poi = pointsOfInterest[i];
                Vector2 worldOffset = new(poi.worldPosition.x - playerPosition.x, poi.worldPosition.z - playerPosition.z);
                if (worldOffset.magnitude > viewRadiusMeters)
                {
                    marker.gameObject.SetActive(false);
                    continue;
                }

                marker.gameObject.SetActive(true);
                marker.anchoredPosition = worldOffset * pixelsPerMeter;
            }
        }

        private void EnsurePoiMarkerPool(int count)
        {
            if (poiMarkerTemplate == null || poiMarkerContainer == null)
                return;

            while (poiMarkerPool.Count < count)
            {
                RectTransform clone = Instantiate(poiMarkerTemplate, poiMarkerContainer);
                clone.gameObject.SetActive(false);
                Text label = clone.GetComponentInChildren<Text>(true);
                if (label != null && data.pointsOfInterest.Count > poiMarkerPool.Count)
                    label.text = data.pointsOfInterest[poiMarkerPool.Count].title;
                poiMarkerPool.Add(clone);
            }
        }
    }
}
