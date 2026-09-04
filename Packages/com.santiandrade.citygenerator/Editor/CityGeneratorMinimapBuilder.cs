using System.Collections.Generic;
using System.IO;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Captures a top-down orthographic snapshot of the generated city and fills a
    /// <see cref="MinimapData"/> component on <c>cityRoot</c> with it, the world bounding box it
    /// covers, and the projected Point of Interest list. Runs at the end of
    /// <see cref="CityGeneratorContentAssembler.Assemble"/> (after every other builder, including
    /// <c>TrafficBuilder</c>/<c>PedestrianBuilder</c>), so the "Vehicles"/"Pedestrians" groups those
    /// populate already exist and can be hidden from the snapshot.
    /// <para>
    /// The snapshot is only captured here, into an in-memory (non-asset) <see cref="Texture2D"/>:
    /// at this point in the pipeline the final scene path (used to name the sibling PNG asset,
    /// e.g. <c>Assets/Scenes/City1_Minimap.png</c>) isn't known yet — a brand new scene is only
    /// saved after <c>Assemble</c> returns. <see cref="SaveSnapshotAsset"/> finalises it into a real
    /// PNG asset once <c>CityGeneratorSceneBuilder</c> knows that path, for both a new scene and a
    /// Re-Build of the current one.
    /// </para>
    /// <para>
    /// The capture camera is not scene-scoped by default — it renders every currently loaded scene
    /// within its frustum, not just <c>cityRoot</c>'s own, and every *other* root object already
    /// present in <c>cityRoot</c>'s own scene ("Build City in New Scene" deliberately leaves any
    /// currently open scene loaded, and <c>CityGeneratorSceneBuilder.RebuildInActiveScene</c> only
    /// destroys the *previous* city's root — found via <see cref="CityGeneratorRoot"/> — **after**
    /// `Assemble` returns, so during the capture itself the old city's fully-active
    /// `Vehicles`/`Pedestrians` are still sitting right there in the same scene as the new one).
    /// <see cref="Camera.scene"/> looks like the natural fix but does **not** work here: it silently
    /// fails to filter when the target scene hasn't been saved yet, which <c>cityRoot</c>'s scene
    /// never has been at this point in the pipeline. Hiding every other root via
    /// <c>GameObject.SetActive(false)</c> (an earlier version of this fix) doesn't work either, for
    /// a much stranger reason confirmed by direct repro: a manual <see cref="Camera.Render"/> call
    /// only reflects a change (active state, layer, *or* transform position — all three were tested)
    /// made to a GameObject that has already been rendered at least once before, if that change
    /// happened in an *earlier* Editor update than the render — changing and rendering it within the
    /// same script call leaves the render showing the pre-change state regardless of which of the
    /// three mechanisms is used. Brand new GameObjects (never rendered before) aren't affected and
    /// respond to all three synchronously, which is exactly why hiding <c>vehiclesGroup</c>/
    /// <c>pedestriansGroup</c> below still works: both are created earlier in this same `Assemble`
    /// call. So instead of touching any pre-existing object at all, the fix moves <c>cityRoot</c>
    /// itself — always freshly created for this call, never previously rendered — to an isolated
    /// point in world space far from anything else that might be loaded, points the capture camera
    /// there instead, and moves it back after. No pre-existing content ever needs to be hidden.
    /// </para>
    /// <para>
    /// Lighting is not scoped to <c>cityRoot</c>'s isolated position at all — a Directional Light
    /// anywhere in memory (the city's own, mid-Day/Night Cycle, or one belonging to another
    /// currently-loaded scene) illuminates every camera equally regardless of where that camera
    /// points. Left alone, this makes the snapshot reflect whatever hour of day the Directional
    /// Light happens to be configured for. Unlike hiding a pre-existing <c>GameObject</c> (see
    /// above), toggling a pre-existing <see cref="Light"/>'s <c>enabled</c>/color/intensity *does*
    /// take effect on the very next <see cref="Camera.Render"/> within the same script call —
    /// confirmed directly with a minimal repro — so every currently-enabled directional light is
    /// simply disabled for the duration of the capture and a fresh neutral one takes its place,
    /// guaranteeing a consistently well-lit snapshot no matter the time of day or scene state.
    /// </para>
    /// </summary>
    internal static class CityGeneratorMinimapBuilder
    {
        // High enough above any plausible building/prop to frame the whole city regardless of
        // height; the camera is strictly top-down and orthographic, so height itself is otherwise
        // irrelevant to the captured footprint.
        private const float SnapshotCameraHeight = 300f;
        private const float SnapshotFarClipMargin = 50f;

        // Far enough that no plausible generated city (even an extreme grid size) could ever reach
        // it from the origin, but well inside float precision's safe range (jitter/z-fighting starts
        // becoming noticeable well beyond this) — see the class remarks on why cityRoot is moved
        // here instead of hiding everything else.
        private const float SnapshotIsolationOffsetX = 50000f;

        // Matches the rotation CityGeneratorSceneBuilder forces on the Directional Light (its
        // DirectionalLightYaw, referenced rather than copied so the two can't drift apart): a
        // neutral, well-lit daytime angle.
        private static readonly Quaternion NeutralSnapshotLightRotation =
            Quaternion.Euler(50f, CityGeneratorSceneBuilder.DirectionalLightYaw, 0f);
        private const float NeutralSnapshotLightIntensity = 1f;

        /// <summary>No-op when <paramref name="settings"/>.enabled is false: no <see cref="MinimapData"/> is added, matching the "no regression when disabled" acceptance criterion.</summary>
        public static void Build(MinimapSettings settings, Transform cityRoot, int gridWidth, int gridHeight, List<PointOfInterestEntry> pointsOfInterest)
        {
            if (!settings.enabled)
                return;

            float width = gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            Vector3 worldCenter = cityRoot.TransformPoint(Vector3.zero);
            BuildCore(settings, cityRoot, width, depth, worldCenter, pointsOfInterest);
        }

        /// <summary>
        /// Custom Grid overload (SPEC 11): frames the minimum rectangle that actually wraps the
        /// real blocks in <paramref name="blockCells"/> instead of the fixed MaxGridSize canvas, so
        /// a small or corner-hugging shape doesn't get a mostly-empty minimap.
        /// </summary>
        public static void Build(MinimapSettings settings, Transform cityRoot, IReadOnlyCollection<Vector2Int> blockCells, List<PointOfInterestEntry> pointsOfInterest)
        {
            if (!settings.enabled || blockCells.Count == 0)
                return;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (Vector2Int cell in blockCells)
            {
                minX = Mathf.Min(minX, cell.x);
                maxX = Mathf.Max(maxX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxY = Mathf.Max(maxY, cell.y);
            }

            int canvas = CityGeneratorConstants.MaxGridSize;
            Vector3 minCorner = CityGeneratorGrid.GetBlockCenter(minX, minY, canvas, canvas);
            Vector3 maxCorner = CityGeneratorGrid.GetBlockCenter(maxX, maxY, canvas, canvas);

            float width = (maxCorner.x - minCorner.x) + CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = (maxCorner.z - minCorner.z) + CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            var localCenter = new Vector3((minCorner.x + maxCorner.x) / 2f, 0f, (minCorner.z + maxCorner.z) / 2f);
            Vector3 worldCenter = cityRoot.TransformPoint(localCenter);

            BuildCore(settings, cityRoot, width, depth, worldCenter, pointsOfInterest);
        }

        private static void BuildCore(MinimapSettings settings, Transform cityRoot, float width, float depth, Vector3 worldCenter, List<PointOfInterestEntry> pointsOfInterest)
        {

            // Excluding Vehicle/Pedestrian by Camera.cullingMask alone doesn't work: that layer is
            // assigned only to each instance's root sensor/proxy collider (see the invariant that a
            // collider deeper in a user prefab's hierarchy is left untouched), never cascaded onto
            // the child mesh Renderers that actually draw the car/pedestrian — those stay on
            // whatever layer the prefab's own meshes were authored with (typically Default), so
            // they'd still render into the snapshot. Hiding the two groups outright avoids depending
            // on layer assignment at all. This is safe to do synchronously (see the class remarks):
            // both groups were created earlier in this same Assemble call, so they've never been
            // rendered before.
            Transform vehiclesGroup = cityRoot.Find("Vehicles");
            Transform pedestriansGroup = cityRoot.Find("Pedestrians");
            bool vehiclesWereActive = vehiclesGroup != null && vehiclesGroup.gameObject.activeSelf;
            bool pedestriansWereActive = pedestriansGroup != null && pedestriansGroup.gameObject.activeSelf;

            // See the class remarks: rather than hiding every other pre-existing root (unreliable —
            // a manual Camera.Render() doesn't pick up a same-call change to an already-rendered
            // GameObject, whatever form that change takes), cityRoot itself is moved to an isolated
            // point in space no other loaded content could plausibly reach, and the capture camera
            // is pointed there instead. cityRoot is always freshly created for this call, so the
            // move is guaranteed to be reflected in the very next render.
            Vector3 originalPosition = cityRoot.position;
            var isolationOffset = new Vector3(SnapshotIsolationOffsetX, 0f, 0f);

            // Disable every directional light currently in memory so the snapshot never reflects
            // whatever hour of day a Day/Night Cycle (this city's own, or one left over from a
            // previously-generated city still in the scene during a Rebuild) happens to be at; a
            // fresh neutral one takes over lighting for the capture only. See the class remarks.
            var directionalLights = new List<Light>();
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type == LightType.Directional && light.enabled)
                    directionalLights.Add(light);
            }
            foreach (Light light in directionalLights)
                light.enabled = false;

            var neutralLightGO = new GameObject("Minimap Snapshot Light (temp)") { hideFlags = HideFlags.HideAndDontSave };

            Texture2D snapshot;
            try
            {
                if (vehiclesGroup != null)
                    vehiclesGroup.gameObject.SetActive(false);
                if (pedestriansGroup != null)
                    pedestriansGroup.gameObject.SetActive(false);
                cityRoot.position = originalPosition + isolationOffset;

                neutralLightGO.transform.rotation = NeutralSnapshotLightRotation;
                var neutralLight = neutralLightGO.AddComponent<Light>();
                neutralLight.type = LightType.Directional;
                neutralLight.color = Color.white;
                neutralLight.intensity = NeutralSnapshotLightIntensity;
                neutralLight.shadows = LightShadows.None;

                snapshot = CaptureSnapshot(worldCenter + isolationOffset, width, depth, settings.textureResolution);
            }
            finally
            {
                Object.DestroyImmediate(neutralLightGO);
                foreach (Light light in directionalLights)
                    light.enabled = true;

                cityRoot.position = originalPosition;
                if (vehiclesGroup != null)
                    vehiclesGroup.gameObject.SetActive(vehiclesWereActive);
                if (pedestriansGroup != null)
                    pedestriansGroup.gameObject.SetActive(pedestriansWereActive);
            }

            var data = cityRoot.GetComponent<MinimapData>();
            if (data == null)
                data = cityRoot.gameObject.AddComponent<MinimapData>();

            data.snapshot = snapshot;
            data.worldOrigin = new Vector2(worldCenter.x - width / 2f, worldCenter.z - depth / 2f);
            data.worldSize = new Vector2(width, depth);
            data.localCenter = cityRoot.InverseTransformPoint(worldCenter);
            data.localSize = new Vector2(width, depth);
            data.pointsOfInterest = new List<PointOfInterestEntry>(pointsOfInterest);
        }

        /// <summary>
        /// Encodes <c>cityRoot</c>'s <see cref="MinimapData"/> in-memory snapshot (left there by
        /// <see cref="Build"/>) to a PNG asset saved inside the scene's own per-scene folder —
        /// <c>&lt;SceneFolder&gt;/&lt;SceneName&gt;/&lt;SceneName&gt;_Minimap.png</c>, the same
        /// folder Unity itself creates next to the scene for things like baked lighting data — then
        /// repoints <see cref="MinimapData.snapshot"/> at the imported asset — same path on every
        /// Re-Build, so the asset keeps its GUID instead of leaving orphaned copies behind. No-op if
        /// the city has no <see cref="MinimapData"/> (Minimap disabled) or <paramref name="scenePath"/>
        /// is empty (an unsaved scene).
        /// </summary>
        public static void SaveSnapshotAsset(Transform cityRoot, string scenePath)
        {
            var data = cityRoot.GetComponent<MinimapData>();
            if (data == null || data.snapshot == null || string.IsNullOrEmpty(scenePath))
                return;

            string sceneFolder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string folder = $"{sceneFolder}/{sceneName}";
            string pngPath = $"{folder}/{sceneName}_Minimap.png";

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(sceneFolder, sceneName);

            byte[] pngBytes = data.snapshot.EncodeToPNG();
            File.WriteAllBytes(Path.GetFullPath(pngPath), pngBytes);
            AssetDatabase.ImportAsset(pngPath);

            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }

            Texture2D importedSnapshot = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            Object.DestroyImmediate(data.snapshot);
            data.snapshot = importedSnapshot;
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// SPEC 16: recaptures the minimap for whichever CityGeneratorRoot(s) with a MinimapData
        /// component exist in <paramref name="scene"/>, at their current position -- the fix for
        /// "I moved the city and the minimap went out of sync" without regenerating. With a single
        /// city, recaptures its own footprint straight away; with two or more, shows a confirmation
        /// dialog (count, combined area, editable resolution) and, once confirmed, captures one
        /// snapshot covering the union of their footprints and points every one of their MinimapData
        /// at it. No-op (informational dialog) if no city in the scene has Minimap enabled.
        /// </summary>
        public static void RebuildMinimap(Scene scene)
        {
            var roots = new List<CityGeneratorRoot>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                var root = go.GetComponent<CityGeneratorRoot>();
                if (root != null && root.GetComponent<MinimapData>() != null)
                    roots.Add(root);
            }

            if (roots.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "City Generator",
                    "No city with the Minimap enabled was found in the current scene. Generate a city with Minimap enabled first.",
                    "OK");
                return;
            }

            int defaultResolution = 2048;
            foreach (CityGeneratorRoot root in roots)
            {
                Texture2D snapshot = root.GetComponent<MinimapData>().snapshot;
                if (snapshot != null)
                {
                    defaultResolution = snapshot.width;
                    break;
                }
            }

            int resolution = defaultResolution;
            if (roots.Count >= 2)
            {
                (_, Vector2 size) = ComputeUnionFootprint(roots);
                string names = string.Join(", ", roots.ConvertAll(root => root.name));
                string message =
                    $"Found {roots.Count} cities with the Minimap enabled: {names}.\n" +
                    $"This will recapture a single minimap snapshot covering their combined area " +
                    $"(~{size.x:0} x {size.y:0} m), replacing the current one for all of them.";

                (bool confirmed, int chosenResolution) = CityGeneratorRebuildMinimapDialog.Show(message, defaultResolution);
                if (!confirmed)
                    return;

                resolution = chosenResolution;
            }

            StartTwoPhaseCapture(roots, resolution);
        }

        private static (Vector2 origin, Vector2 size) ComputeWorldFootprint(CityGeneratorRoot root)
        {
            MinimapData data = root.GetComponent<MinimapData>();
            Transform t = root.transform;

            // localCenter/localSize is the stable source of truth (SPEC 16): correct regardless of
            // where the root sits right now. A city generated before this spec has both left at
            // their default zero, which is indistinguishable from a real (impossible) zero-size
            // footprint -- treated the same as "unknown", falling back to Renderer.bounds so the
            // menu never fails cold on an existing scene, at the cost of a looser frame that
            // includes anything hand-added to the hierarchy.
            if (data != null && (data.localSize.x > 0f || data.localSize.y > 0f))
            {
                Vector3 worldCenter = t.TransformPoint(data.localCenter);
                var origin = new Vector2(worldCenter.x - data.localSize.x / 2f, worldCenter.z - data.localSize.y / 2f);
                return (origin, data.localSize);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return (new Vector2(t.position.x, t.position.z), Vector2.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return (new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.size.x, bounds.size.z));
        }

        private static (Vector2 origin, Vector2 size) ComputeUnionFootprint(List<CityGeneratorRoot> roots)
        {
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            foreach (CityGeneratorRoot root in roots)
            {
                (Vector2 origin, Vector2 size) = ComputeWorldFootprint(root);
                min = Vector2.Min(min, origin);
                max = Vector2.Max(max, origin + size);
            }

            return (min, max - min);
        }

        /// <summary>
        /// Phase 1 (synchronous): hides every city's Vehicles/Pedestrians groups, saving their prior
        /// active state, then defers the actual capture to the next Editor update via
        /// <see cref="EditorApplication.delayCall"/>. Needed because these roots already existed
        /// before this call (unlike generation's freshly-created ones): a manual Camera.Render()
        /// does not reflect a same-call visibility change on an object Unity has already rendered
        /// before -- see the class remarks. Visibility is restored in a finally once phase 2 (or its
        /// own failure) completes.
        /// </summary>
        private static void StartTwoPhaseCapture(List<CityGeneratorRoot> roots, int resolution)
        {
            var vehicleGroups = new List<Transform>();
            var pedestrianGroups = new List<Transform>();
            var vehiclesWereActive = new List<bool>();
            var pedestriansWereActive = new List<bool>();

            foreach (CityGeneratorRoot root in roots)
            {
                Transform vehicles = root.transform.Find("Vehicles");
                Transform pedestrians = root.transform.Find("Pedestrians");
                vehicleGroups.Add(vehicles);
                pedestrianGroups.Add(pedestrians);
                vehiclesWereActive.Add(vehicles != null && vehicles.gameObject.activeSelf);
                pedestriansWereActive.Add(pedestrians != null && pedestrians.gameObject.activeSelf);

                if (vehicles != null)
                    vehicles.gameObject.SetActive(false);
                if (pedestrians != null)
                    pedestrians.gameObject.SetActive(false);
            }

            EditorApplication.CallbackFunction phase2 = null;
            phase2 = () =>
            {
                EditorApplication.delayCall -= phase2;
                try
                {
                    CompleteTwoPhaseCapture(roots, resolution);
                }
                finally
                {
                    for (int i = 0; i < roots.Count; i++)
                    {
                        // A root captured in phase 1 may have been destroyed in the interim (the
                        // user closing/changing the scene while the delayCall was pending).
                        if (roots[i] == null)
                            continue;

                        if (vehicleGroups[i] != null)
                            vehicleGroups[i].gameObject.SetActive(vehiclesWereActive[i]);
                        if (pedestrianGroups[i] != null)
                            pedestrianGroups[i].gameObject.SetActive(pedestriansWereActive[i]);
                    }
                }
            };
            EditorApplication.delayCall += phase2;
        }

        /// <summary>
        /// Phase 2 (deferred): places the temporary camera over the union of every root's footprint,
        /// captures under a neutral light exactly like <see cref="BuildCore"/>, then saves the PNG
        /// once and points every root's <see cref="MinimapData"/> at it with the merged POI list.
        /// </summary>
        private static void CompleteTwoPhaseCapture(List<CityGeneratorRoot> roots, int resolution)
        {
            roots.RemoveAll(root => root == null);
            if (roots.Count == 0)
                return;

            (Vector2 origin, Vector2 size) = ComputeUnionFootprint(roots);
            if (size.x <= 0f || size.y <= 0f)
                return;

            Vector3 worldCenter = new(origin.x + size.x / 2f, 0f, origin.y + size.y / 2f);

            var directionalLights = new List<Light>();
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type == LightType.Directional && light.enabled)
                    directionalLights.Add(light);
            }
            foreach (Light light in directionalLights)
                light.enabled = false;

            var neutralLightGO = new GameObject("Minimap Snapshot Light (temp)") { hideFlags = HideFlags.HideAndDontSave };

            Texture2D snapshot;
            try
            {
                neutralLightGO.transform.rotation = NeutralSnapshotLightRotation;
                var neutralLight = neutralLightGO.AddComponent<Light>();
                neutralLight.type = LightType.Directional;
                neutralLight.color = Color.white;
                neutralLight.intensity = NeutralSnapshotLightIntensity;
                neutralLight.shadows = LightShadows.None;

                snapshot = CaptureSnapshot(worldCenter, size.x, size.y, resolution);
            }
            finally
            {
                Object.DestroyImmediate(neutralLightGO);
                foreach (Light light in directionalLights)
                    light.enabled = true;
            }

            var mergedPois = new List<PointOfInterestEntry>();
            var targets = new List<MinimapData>();
            foreach (CityGeneratorRoot root in roots)
            {
                MinimapData data = root.GetComponent<MinimapData>();
                targets.Add(data);
                mergedPois.AddRange(data.pointsOfInterest);
            }

            Scene scene = roots[0].gameObject.scene;
            SaveCombinedSnapshotAsset(snapshot, scene.path, targets, origin, size, mergedPois);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Same PNG path/import convention as <see cref="SaveSnapshotAsset"/>, but writes one asset
        /// and points every <see cref="MinimapData"/> in <paramref name="targets"/> at it -- used by
        /// <see cref="RebuildMinimap"/> so a city copied from another scene stops referencing that
        /// scene's own PNG (SPEC 16: a single unified asset, not one per city plus a combined one).
        /// </summary>
        private static void SaveCombinedSnapshotAsset(Texture2D snapshot, string scenePath, List<MinimapData> targets, Vector2 worldOrigin, Vector2 worldSize, List<PointOfInterestEntry> mergedPois)
        {
            if (targets.Count == 0 || string.IsNullOrEmpty(scenePath))
            {
                Object.DestroyImmediate(snapshot);
                return;
            }

            string sceneFolder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string folder = $"{sceneFolder}/{sceneName}";
            string pngPath = $"{folder}/{sceneName}_Minimap.png";

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(sceneFolder, sceneName);

            byte[] pngBytes = snapshot.EncodeToPNG();
            File.WriteAllBytes(Path.GetFullPath(pngPath), pngBytes);
            AssetDatabase.ImportAsset(pngPath);

            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }

            Texture2D importedSnapshot = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            Object.DestroyImmediate(snapshot);

            foreach (MinimapData data in targets)
            {
                data.snapshot = importedSnapshot;
                data.worldOrigin = worldOrigin;
                data.worldSize = worldSize;
                data.pointsOfInterest = new List<PointOfInterestEntry>(mergedPois);
                EditorUtility.SetDirty(data);
            }
        }

        private static Texture2D CaptureSnapshot(Vector3 worldCenter, float width, float depth, int resolution)
        {
            var cameraGO = new GameObject("Minimap Snapshot Camera (temp)") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture renderTexture = null;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                cameraGO.transform.position = new Vector3(worldCenter.x, worldCenter.y + SnapshotCameraHeight, worldCenter.z);
                cameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var camera = cameraGO.AddComponent<Camera>();
                camera.orthographic = true;
                // Frames depth exactly (half-height = depth/2) and forces the aspect ratio to
                // stretch width to fit the square render texture too, so a non-square world
                // bounding box still maps onto the square snapshot without distortion in either
                // axis relative to the other.
                camera.orthographicSize = depth / 2f;
                camera.aspect = width / depth;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = SnapshotCameraHeight + SnapshotFarClipMargin;
                camera.clearFlags = CameraClearFlags.SolidColor;
                // Kept in sync with MinimapWindow.mat's _OutOfBoundsColor (currently hex 434343):
                // the HUD keeps the player centred, so its window reaches past the snapshot near a
                // city edge, and that material paints the out-of-range remainder with this same
                // colour. Matching the two makes the area beyond the city read as one continuous
                // background. If the material's colour changes again, update this to match.
                camera.backgroundColor = new Color(67f / 255f, 67f / 255f, 67f / 255f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;

                renderTexture = new RenderTexture(resolution, resolution, 24);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (renderTexture != null)
                {
                    cameraGO.GetComponent<Camera>().targetTexture = null;
                    renderTexture.Release();
                    Object.DestroyImmediate(renderTexture);
                }
                Object.DestroyImmediate(cameraGO);
            }
        }
    }
}
