using System.Collections.Generic;
using System.IO;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEngine;

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

            Texture2D snapshot;
            try
            {
                if (vehiclesGroup != null)
                    vehiclesGroup.gameObject.SetActive(false);
                if (pedestriansGroup != null)
                    pedestriansGroup.gameObject.SetActive(false);
                cityRoot.position = originalPosition + isolationOffset;

                snapshot = CaptureSnapshot(worldCenter + isolationOffset, width, depth, settings.textureResolution);
            }
            finally
            {
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
