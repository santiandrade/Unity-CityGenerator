using System.Collections.Generic;
using System.IO;
using CityGenerator.Runtime;
using UnityEditor;
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
    /// within its frustum, not just <c>cityRoot</c>'s own. "Build City in New Scene" deliberately
    /// leaves any currently open scene loaded, so without isolating the capture, generating a new
    /// city while a previous one is open bleeds that other scene's buildings/vehicles/pedestrians
    /// into the new snapshot — found in QA by comparing a snapshot's vehicle pixel positions against
    /// the still-open other scene's own vehicle transforms, an exact match. <see cref="Camera.scene"/>
    /// looks like the natural fix (it's Unity's own supported mechanism for exactly this, used
    /// internally for Prefab Mode isolation) but does **not** work here: it silently fails to filter
    /// when the target scene hasn't been saved yet (empty name/path) — confirmed directly, and
    /// <c>cityRoot</c>'s scene is unsaved at this point in the pipeline (a brand new scene is only
    /// saved after <c>Assemble</c> returns). So every other loaded scene's root objects are
    /// deactivated for the capture instead (restored right after, in a <c>finally</c>) — this works
    /// regardless of save state, since it doesn't depend on scene identity at all.
    /// </para>
    /// </summary>
    internal static class CityGeneratorMinimapBuilder
    {
        // High enough above any plausible building/prop to frame the whole city regardless of
        // height; the camera is strictly top-down and orthographic, so height itself is otherwise
        // irrelevant to the captured footprint.
        private const float SnapshotCameraHeight = 300f;
        private const float SnapshotFarClipMargin = 50f;

        /// <summary>No-op when <paramref name="settings"/>.enabled is false: no <see cref="MinimapData"/> is added, matching the "no regression when disabled" acceptance criterion.</summary>
        public static void Build(MinimapSettings settings, Transform cityRoot, int gridWidth, int gridHeight, List<PointOfInterestEntry> pointsOfInterest)
        {
            if (!settings.enabled)
                return;

            float width = gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            Vector3 worldCenter = cityRoot.TransformPoint(Vector3.zero);

            // Excluding Vehicle/Pedestrian by Camera.cullingMask alone doesn't work: that layer is
            // assigned only to each instance's root sensor/proxy collider (see the invariant that a
            // collider deeper in a user prefab's hierarchy is left untouched), never cascaded onto
            // the child mesh Renderers that actually draw the car/pedestrian — those stay on
            // whatever layer the prefab's own meshes were authored with (typically Default), so
            // they'd still render into the snapshot. Hiding the two groups outright avoids depending
            // on layer assignment at all.
            Transform vehiclesGroup = cityRoot.Find("Vehicles");
            Transform pedestriansGroup = cityRoot.Find("Pedestrians");
            bool vehiclesWereActive = vehiclesGroup != null && vehiclesGroup.gameObject.activeSelf;
            bool pedestriansWereActive = pedestriansGroup != null && pedestriansGroup.gameObject.activeSelf;

            // See the class remarks: every other loaded scene's root objects are hidden too, since
            // the capture camera isn't otherwise scene-scoped.
            Scene ownScene = cityRoot.gameObject.scene;
            var otherSceneRoots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || scene == ownScene)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.activeSelf)
                        otherSceneRoots.Add(root);
                }
            }

            Texture2D snapshot;
            try
            {
                if (vehiclesGroup != null)
                    vehiclesGroup.gameObject.SetActive(false);
                if (pedestriansGroup != null)
                    pedestriansGroup.gameObject.SetActive(false);
                foreach (GameObject root in otherSceneRoots)
                    root.SetActive(false);

                snapshot = CaptureSnapshot(worldCenter, width, depth, settings.textureResolution);
            }
            finally
            {
                if (vehiclesGroup != null)
                    vehiclesGroup.gameObject.SetActive(vehiclesWereActive);
                if (pedestriansGroup != null)
                    pedestriansGroup.gameObject.SetActive(pedestriansWereActive);
                foreach (GameObject root in otherSceneRoots)
                {
                    if (root != null)
                        root.SetActive(true);
                }
            }

            var data = cityRoot.GetComponent<MinimapData>();
            if (data == null)
                data = cityRoot.gameObject.AddComponent<MinimapData>();

            data.snapshot = snapshot;
            data.worldOrigin = new Vector2(worldCenter.x - width / 2f, worldCenter.z - depth / 2f);
            data.worldSize = new Vector2(width, depth);
            data.pointsOfInterest = new List<PointOfInterestEntry>(pointsOfInterest);
        }

        /// <summary>
        /// Encodes <c>cityRoot</c>'s <see cref="MinimapData"/> in-memory snapshot (left there by
        /// <see cref="Build"/>) to a PNG asset saved next to <paramref name="scenePath"/>
        /// (<c>&lt;SceneName&gt;_Minimap.png</c>), then repoints <see cref="MinimapData.snapshot"/>
        /// at the imported asset — same path on every Re-Build, so the asset keeps its GUID instead
        /// of leaving orphaned copies behind. No-op if the city has no <see cref="MinimapData"/>
        /// (Minimap disabled) or <paramref name="scenePath"/> is empty (an unsaved scene).
        /// </summary>
        public static void SaveSnapshotAsset(Transform cityRoot, string scenePath)
        {
            var data = cityRoot.GetComponent<MinimapData>();
            if (data == null || data.snapshot == null || string.IsNullOrEmpty(scenePath))
                return;

            string folder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string pngPath = $"{folder}/{sceneName}_Minimap.png";

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
                camera.backgroundColor = Color.black;
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
