using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CityGenerator.Runtime
{
    internal sealed class MinimapMarkerToken
    {
    }

    /// <summary>Opaque reference to one runtime minimap marker.</summary>
    public readonly struct MinimapMarkerHandle
    {
        private readonly MinimapHUD owner;
        private readonly MinimapMarkerToken token;

        internal MinimapMarkerHandle(MinimapHUD owner, MinimapMarkerToken token)
        {
            this.owner = owner;
            this.token = token;
        }

        /// <summary>True while the owning HUD and marker registration still exist.</summary>
        public bool IsValid => owner != null && owner.ContainsDynamicMarker(token);

        internal MinimapHUD Owner => owner;
        internal MinimapMarkerToken Token => token;
    }

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
        private sealed class DynamicMarker
        {
            public Transform target;
            public Vector3 fixedPosition;
            public bool followsTransform;
            public bool clampToEdge;
            public RectTransform instance;
        }

        private readonly struct MapProjection
        {
            public readonly double worldOffsetX;
            public readonly double worldOffsetY;
            public readonly double worldDistance;
            public readonly double projectedDistance;
            public readonly Vector2 uiOffset;

            public MapProjection(
                double worldOffsetX,
                double worldOffsetY,
                double worldDistance,
                double projectedDistance,
                Vector2 uiOffset)
            {
                this.worldOffsetX = worldOffsetX;
                this.worldOffsetY = worldOffsetY;
                this.worldDistance = worldDistance;
                this.projectedDistance = projectedDistance;
                this.uiOffset = uiOffset;
            }
        }

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
        [Tooltip("Fixed at the HUD's centre; rotates to reflect the tracked transform's current yaw. The map itself never rotates.")]
        [SerializeField] private RectTransform playerMarker;
        [Tooltip("Transform the marker follows. Left null, it falls back to finding the PlayerController in the scene.")]
        [SerializeField] private Transform markerTarget;
        [Tooltip("Deactivated template cloned once per visible Point of Interest; reused as the single generic icon+label for every POI.")]
        [SerializeField] private RectTransform poiMarkerTemplate;
        [Tooltip("Parent for POI marker clones.")]
        [SerializeField] private RectTransform poiMarkerContainer;

        /// <summary>Transform the marker follows. Null falls back to the scene's PlayerController.</summary>
        public Transform MarkerTarget
        {
            get => markerTarget;
            set => markerTarget = value;
        }

        private MinimapData data;
        private Transform trackedTransform;
        private readonly List<RectTransform> poiMarkerPool = new();
        private readonly Dictionary<MinimapMarkerToken, DynamicMarker> dynamicMarkers = new();
        private readonly List<MinimapMarkerToken> dynamicMarkerSnapshot = new();
        private bool isUpdatingDynamicMarkers;
        private bool isTearingDown;

        /// <summary>Adds a marker that follows a Transform in world space.</summary>
        public MinimapMarkerHandle AddMarker(Transform target, RectTransform prefab, bool clampToEdge)
        {
            if (target == null || prefab == null)
                return default;

            return AddDynamicMarker(target, target.position, true, prefab, clampToEdge);
        }

        /// <summary>Adds a marker at a fixed world-space position.</summary>
        public MinimapMarkerHandle AddMarker(Vector3 position, RectTransform prefab, bool clampToEdge)
        {
            if (prefab == null || !IsFinite(position))
                return default;

            return AddDynamicMarker(null, position, false, prefab, clampToEdge);
        }

        /// <summary>Removes a marker owned by this HUD. Invalid or foreign handles are ignored.</summary>
        public void RemoveMarker(MinimapMarkerHandle handle)
        {
            if (handle.Owner != this || handle.Token == null ||
                !dynamicMarkers.Remove(handle.Token, out DynamicMarker marker))
                return;

            DestroyMarkerInstance(marker.instance);
        }

        internal bool ContainsDynamicMarker(MinimapMarkerToken token) =>
            token != null && dynamicMarkers.ContainsKey(token);

        private MinimapMarkerHandle AddDynamicMarker(
            Transform target,
            Vector3 fixedPosition,
            bool followsTransform,
            RectTransform prefab,
            bool clampToEdge)
        {
            if (isTearingDown)
                return default;

            // Positions are written as anchoredPosition, so the parent's centre has to be the
            // map's centre: POIMarkerContainer (a zero-sized rect at MapPanel's centre) or, failing
            // that, MapImage's own parent -- MapImage stretches to fill it, so the two share a
            // centre. The HUD root is *not* an option: its centre is the screen's, which would put
            // markers in mid-screen with nothing to explain it. No such parent, no marker.
            RectTransform parent = poiMarkerContainer != null
                ? poiMarkerContainer
                : mapImage != null
                    ? mapImage.rectTransform.parent as RectTransform
                    : null;
            if (parent == null)
                return default;

            RectTransform instance = Instantiate(prefab, parent);
            if (this == null || instance == null || isTearingDown)
            {
                DestroyMarkerInstance(instance);
                return default;
            }

            var token = new MinimapMarkerToken();
            var marker = new DynamicMarker
            {
                target = target,
                fixedPosition = fixedPosition,
                followsTransform = followsTransform,
                clampToEdge = clampToEdge,
                instance = instance,
            };
            dynamicMarkers.Add(token, marker);

            // Overrides whatever anchors the caller's prefab was authored with, so the projected
            // offset means the same thing for every marker (documented in docs/api-reference.md).
            instance.anchorMin = new Vector2(0.5f, 0.5f);
            instance.anchorMax = new Vector2(0.5f, 0.5f);

            // The only step here that can run the consumer's code -- and so destroy the HUD, the
            // clone, or this very registration -- is SetActive, through the clone's OnDisable.
            instance.gameObject.SetActive(false);
            if (!IsOwnedDynamicMarker(this, token, marker))
            {
                CleanupFailedDynamicMarker(this, token, marker);
                return default;
            }

            return new MinimapMarkerHandle(this, token);
        }

        private static bool IsOwnedDynamicMarker(
            MinimapHUD owner,
            MinimapMarkerToken token,
            DynamicMarker marker) =>
            owner != null && !owner.isTearingDown && marker.instance != null &&
            owner.dynamicMarkers.TryGetValue(token, out DynamicMarker current) &&
            ReferenceEquals(current, marker);

        private static void CleanupFailedDynamicMarker(
            MinimapHUD owner,
            MinimapMarkerToken token,
            DynamicMarker marker)
        {
            if (owner != null &&
                owner.dynamicMarkers.TryGetValue(token, out DynamicMarker current) &&
                ReferenceEquals(current, marker))
            {
                owner.dynamicMarkers.Remove(token);
            }

            DestroyMarkerInstance(marker.instance);
        }

        private static void DestroyMarkerInstance(RectTransform instance)
        {
            if (instance == null)
                return;

            instance.gameObject.SetActive(false);
            if (instance == null)
                return;

            if (Application.isPlaying)
                Destroy(instance.gameObject);
            else
                DestroyImmediate(instance.gameObject);
        }

        private void OnEnable()
        {
            bool canRender = TryGetDynamicMarkerPlayerPosition(out Vector3 playerPosition);
            UpdateDynamicMarkers(playerPosition, canRender);
        }

        private void OnDestroy()
        {
            isTearingDown = true;
            var markers = new List<DynamicMarker>(dynamicMarkers.Values);
            dynamicMarkers.Clear();
            dynamicMarkerSnapshot.Clear();

            foreach (DynamicMarker marker in markers)
                DestroyMarkerInstance(marker.instance);
        }

        private void Start()
        {
            if (markerTarget == null)
            {
                PlayerController player = FindAnyObjectByType<PlayerController>();
                trackedTransform = player != null ? player.transform : null;
            }
            else
            {
                trackedTransform = markerTarget;
            }

            data = trackedTransform != null ? FindDataForPlayer(trackedTransform.position) : null;

            if (data == null || trackedTransform == null)
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
            bool canRender = TryGetDynamicMarkerPlayerPosition(out Vector3 playerPosition);
            UpdateDynamicMarkers(playerPosition, canRender);
            if (!canRender)
                return;

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

            float yaw = trackedTransform.eulerAngles.y;
            playerMarker.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }

        private void UpdatePoiMarkers(Vector3 playerPosition)
        {
            List<PointOfInterestEntry> pointsOfInterest = data.pointsOfInterest;
            EnsurePoiMarkerPool(pointsOfInterest.Count);

            RectTransform mapRect = mapImage.rectTransform;
            double unitsPerMeter = UnitsPerMeter(mapRect);

            for (int i = 0; i < poiMarkerPool.Count; i++)
            {
                RectTransform marker = poiMarkerPool[i];
                if (i >= pointsOfInterest.Count)
                {
                    marker.gameObject.SetActive(false);
                    continue;
                }

                PointOfInterestEntry poi = pointsOfInterest[i];
                MapProjection projection = ProjectWorldToUi(poi.worldPosition, playerPosition, unitsPerMeter);
                if (projection.worldDistance > viewRadiusMeters)
                {
                    marker.gameObject.SetActive(false);
                    continue;
                }

                marker.gameObject.SetActive(true);
                marker.anchoredPosition = projection.uiOffset;
            }
        }

        private bool TryGetDynamicMarkerPlayerPosition(out Vector3 playerPosition)
        {
            playerPosition = default;
            if (data == null || trackedTransform == null || mapImage == null)
                return false;

            playerPosition = trackedTransform.position;
            return true;
        }

        private void UpdateDynamicMarkers(Vector3 playerPosition, bool canRender)
        {
            if (isTearingDown || isUpdatingDynamicMarkers)
                return;

            isUpdatingDynamicMarkers = true;
            dynamicMarkerSnapshot.Clear();
            dynamicMarkerSnapshot.AddRange(dynamicMarkers.Keys);

            try
            {
                RectTransform mapRect = canRender ? mapImage.rectTransform : null;
                bool canProject = mapRect != null && IsFinite(viewRadiusMeters) && viewRadiusMeters > 0f &&
                                  IsFinite(playerPosition) &&
                                  IsFinite(mapRect.rect.width) && IsFinite(mapRect.rect.height) &&
                                  mapRect.rect.width > 0f && mapRect.rect.height > 0f;
                double unitsPerMeter = canProject
                    ? UnitsPerMeter(mapRect)
                    : 0d;
                // Half the width, not Mathf.Min(width, height): this has to be the very circle
                // worldDistance == viewRadiusMeters maps to, and UnitsPerMeter derives that from
                // the width alone. Expressed in the marker parent's space, which shares the map's
                // centre and scale (see AddDynamicMarker).
                float mapRadius = canProject ? mapRect.rect.width * 0.5f : 0f;

                for (int i = 0; i < dynamicMarkerSnapshot.Count && !isTearingDown; i++)
                {
                    MinimapMarkerToken token = dynamicMarkerSnapshot[i];
                    if (!dynamicMarkers.TryGetValue(token, out DynamicMarker marker))
                        continue;

                    if ((marker.followsTransform && marker.target == null) || marker.instance == null)
                    {
                        RemoveDynamicMarker(token);
                        continue;
                    }

                    if (!canProject ||
                        (marker.followsTransform && !marker.target.gameObject.activeInHierarchy))
                    {
                        marker.instance.gameObject.SetActive(false);
                        continue;
                    }

                    Vector3 worldPosition = marker.followsTransform ? marker.target.position : marker.fixedPosition;
                    if (!IsFinite(worldPosition))
                    {
                        marker.instance.gameObject.SetActive(false);
                        continue;
                    }

                    MapProjection projection = ProjectWorldToUi(worldPosition, playerPosition, unitsPerMeter);
                    if (!marker.clampToEdge && projection.worldDistance > viewRadiusMeters)
                    {
                        marker.instance.gameObject.SetActive(false);
                        continue;
                    }

                    Vector2 position = projection.uiOffset;
                    if (marker.clampToEdge)
                    {
                        float markerRadius = MarkerCornerRadius(marker);
                        float safeRadius = IsFinite(markerRadius) ? Mathf.Max(0f, mapRadius - markerRadius) : 0f;
                        if (projection.projectedDistance > safeRadius && projection.worldDistance > 0d)
                        {
                            double scale = safeRadius / projection.worldDistance;
                            position = ToFiniteVector2(
                                projection.worldOffsetX * scale,
                                projection.worldOffsetY * scale);
                        }
                    }

                    marker.instance.anchoredPosition = position;
                    marker.instance.gameObject.SetActive(true);
                }
            }
            finally
            {
                dynamicMarkerSnapshot.Clear();
                isUpdatingDynamicMarkers = false;
            }
        }

        private void RemoveDynamicMarker(MinimapMarkerToken token)
        {
            if (dynamicMarkers.Remove(token, out DynamicMarker marker))
                DestroyMarkerInstance(marker.instance);
        }

        private double UnitsPerMeter(RectTransform mapRect) =>
            (double)mapRect.rect.width / (2d * viewRadiusMeters);

        private static MapProjection ProjectWorldToUi(
            Vector3 worldPosition,
            Vector3 playerPosition,
            double unitsPerMeter)
        {
            double worldOffsetX = (double)worldPosition.x - playerPosition.x;
            double worldOffsetY = (double)worldPosition.z - playerPosition.z;
            // Squaring cannot overflow a double for any finite float input: the widest case
            // reachable here (a float.MaxValue-wide offset scaled by the largest unitsPerMeter a
            // float.MaxValue map width and a float.Epsilon radius can produce) squares to ~7e243,
            // against double's 1.8e308 -- so no scaled-hypot dance is needed.
            double worldDistance =
                System.Math.Sqrt(worldOffsetX * worldOffsetX + worldOffsetY * worldOffsetY);
            return new MapProjection(
                worldOffsetX,
                worldOffsetY,
                worldDistance,
                // |(x, y) * k| == |(x, y)| * k for k > 0, and unitsPerMeter is positive wherever a
                // caller reads this: a multiply instead of a second square root, per projection and
                // so per POI and per frame.
                worldDistance * unitsPerMeter,
                ToFiniteVector2(worldOffsetX * unitsPerMeter, worldOffsetY * unitsPerMeter));
        }

        private static Vector2 ToFiniteVector2(double x, double y) =>
            new(ToFiniteFloat(x), ToFiniteFloat(y));

        private static float ToFiniteFloat(double value)
        {
            if (double.IsNaN(value))
                return 0f;
            if (value > float.MaxValue)
                return float.MaxValue;
            if (value < -float.MaxValue)
                return -float.MaxValue;
            return (float)value;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        /// <summary>
        /// Distance from the instance's pivot to the farthest corner of its root rect, in the
        /// parent's space -- the inset that keeps the whole declared rect inside the map circle.
        /// <para>
        /// Read straight off <see cref="RectTransform.rect"/>, which is already expressed relative
        /// to the pivot, times the local scale. The farthest corner is the one pairing the largest
        /// |x| with the largest |y|, since the magnitude grows with each independently, and a
        /// rotation cannot change it — it preserves magnitudes. So this needs neither
        /// <c>GetWorldCorners</c> and four <c>InverseTransformVector</c> round trips per frame, nor
        /// a cache to avoid them.
        /// </para>
        /// </summary>
        private static float MarkerCornerRadius(DynamicMarker marker)
        {
            RectTransform instance = marker.instance;
            Rect rect = instance.rect;
            Vector3 scale = instance.localScale;
            float x = Mathf.Max(Mathf.Abs(rect.xMin), Mathf.Abs(rect.xMax)) * Mathf.Abs(scale.x);
            float y = Mathf.Max(Mathf.Abs(rect.yMin), Mathf.Abs(rect.yMax)) * Mathf.Abs(scale.y);
            return new Vector2(x, y).magnitude;
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
