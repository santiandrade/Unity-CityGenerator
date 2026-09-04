using CityGenerator.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Assembles a brand new scene around the generated city: Directional Light,
    /// Main Camera (with <see cref="ThirdPersonCamera"/>) and, if assigned, a Player instance —
    /// then saves it as the next free <c>Assets/Scenes/City&lt;N&gt;.unity</c>.
    /// </summary>
    internal static class CityGeneratorSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MinimapHudPrefabPath = "Packages/com.santiandrade.citygenerator/DefaultAssets/Prefabs/MinimapHUD.prefab";
        private const string MinimapHudInstanceName = "Minimap HUD";

        // Same isolate-by-moving-the-root pattern CityGeneratorMinimapBuilder's snapshot capture
        // uses (a different offset so the two never coincide), applied here for the same reason:
        // hiding/disabling an already-loaded GameObject isn't reliable against physics queries
        // that already have it indexed, but moving it is.
        private const float PreviousCityIsolationOffsetX = -50000f;

        public static (string scenePath, CityBuildSummary summary) BuildAndSaveScene(CityGeneratorSettings settings)
        {
            return BuildAndSaveScene(settings, onProgress: null);
        }

        /// <summary>Same as <see cref="BuildAndSaveScene(CityGeneratorSettings)"/>, forwarding generation progress to <paramref name="onProgress"/> — see <see cref="CityGeneratorContentAssembler.Assemble(CityGeneratorSettings, Transform, System.Action{string, float})"/>.</summary>
        public static (string scenePath, CityBuildSummary summary) BuildAndSaveScene(CityGeneratorSettings settings, System.Action<string, float> onProgress)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                var cityRootGO = new GameObject("City");
                SceneManager.MoveGameObjectToScene(cityRootGO, scene);
                CityBuildSummary summary = CityGeneratorContentAssembler.Assemble(settings, cityRootGO.transform, onProgress);

                // Known now (before the scene itself is saved) purely so the sibling PNG asset can
                // be named after it; see CityGeneratorMinimapBuilder's remarks on the two-phase split.
                string scenePath = GetNextFreeScenePath();
                CityGeneratorMinimapBuilder.SaveSnapshotAsset(cityRootGO.transform, scenePath);

                CityGeneratorInfo info = cityRootGO.GetComponent<CityGeneratorInfo>();
                info.dayNightCycle = CreateDirectionalLight(scene, settings.dayNight);

                GameObject player = null;
                if (settings.general.playerEnabled && settings.general.playerPrefab != null)
                {
                    player = (GameObject)PrefabUtility.InstantiatePrefab(settings.general.playerPrefab, scene);
                    player.name = "Player";
                    player.transform.position = summary.playerSpawnPosition;
                    ConfigurePlayer(player, settings.general.inputActions, settings.player);
                    AssignPedestrianLayer(player);
                    info.player = player.transform;
                }

                info.freeCameraController = CreateMainCamera(scene, player, settings.general.inputActions, settings.player, settings.camera, settings.freeCamera);
                info.minimapHUD = CreateMinimapHud(scene, settings.minimap);

                EditorSceneManager.SaveScene(scene, scenePath);

                return (scenePath, summary);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Every <see cref="CityGeneratorRoot"/> at the root of <paramref name="scene"/>, in
        /// hierarchy order. Used both by <see cref="RebuildInActiveScene"/> and by
        /// <c>CityGeneratorWindow</c> to decide whether a Re-Build needs the "several cities will be
        /// destroyed" confirmation.
        /// </summary>
        public static System.Collections.Generic.List<GameObject> FindAllCityRoots(Scene scene)
        {
            var roots = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<CityGeneratorRoot>() != null)
                    roots.Add(root);
            }

            return roots;
        }

        /// <summary>
        /// Regenerates the city in the currently active scene from <paramref name="settings"/>,
        /// transactionally: the new city is built under a temporary root first, and every previous
        /// one (found by <see cref="CityGeneratorRoot"/>, not by name -- SPEC 16: there can be more
        /// than one) is only destroyed once generation finishes without throwing. If
        /// <see cref="CityGeneratorContentAssembler.Assemble"/> throws, the failed temporary root is
        /// destroyed, every previous city is left completely intact (moved back to its original
        /// position), and the exception is rethrown for the caller (<c>CityGeneratorWindow</c>) to
        /// report. Camera, volume and player are left untouched; the Directional Light's day/night
        /// cycle is the one exception — its <see cref="DayNightCycle"/> is added/updated to match
        /// <paramref name="settings"/>.dayNight and reapplies Start Hour, everything else about the
        /// light (base rotation, shadows) stays as it was. Does not save the scene: the caller
        /// leaves that to the usual Editor "unsaved changes" flow.
        /// </summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings)
        {
            return RebuildInActiveScene(settings, onProgress: null);
        }

        /// <summary>Same as <see cref="RebuildInActiveScene(CityGeneratorSettings)"/>, forwarding generation progress to <paramref name="onProgress"/>.</summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings, System.Action<string, float> onProgress)
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            // Moved aside (never destroyed yet) *before* the new city is built: none of the
            // previous cities are destroyed until Assemble below succeeds (see the invariant this
            // preserves, right below), so without this they'd sit in the exact same world-space
            // footprint as the new one for the whole call -- two sets of static colliders stacked
            // exactly on top of each other made PedestrianNetwork.PrunePlacedObstacles' downward
            // Physics.Raycast ground check intermittently (roughly every other Rebuild) find no hit
            // at all, wrongly marking most/all nodes Blocked -- confirmed directly against this
            // exact scene. Moved back in the catch below so a failed rebuild still leaves every
            // previous city exactly where it was. Each gets its own offset multiple (index-based) so
            // two or more previous cities never overlap each other while isolated either.
            System.Collections.Generic.List<GameObject> previousCityRoots = FindAllCityRoots(scene);
            var previousCityRootPositions = new Vector3[previousCityRoots.Count];
            for (int i = 0; i < previousCityRoots.Count; i++)
            {
                previousCityRootPositions[i] = previousCityRoots[i].transform.position;
                previousCityRoots[i].transform.position =
                    previousCityRootPositions[i] + new Vector3(PreviousCityIsolationOffsetX * (i + 1), 0f, 0f);
            }

            var cityRootGO = new GameObject("City (generating)");
            SceneManager.MoveGameObjectToScene(cityRootGO, scene);

            CityBuildSummary summary;
            try
            {
                summary = CityGeneratorContentAssembler.Assemble(settings, cityRootGO.transform, onProgress);
                CityGeneratorMinimapBuilder.SaveSnapshotAsset(cityRootGO.transform, scene.path);
            }
            catch
            {
                Object.DestroyImmediate(cityRootGO);
                for (int i = 0; i < previousCityRoots.Count; i++)
                    previousCityRoots[i].transform.position = previousCityRootPositions[i];
                throw;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild City");

            GameObject previousMinimapHud = GameObject.Find(MinimapHudInstanceName);
            if (previousMinimapHud != null)
                Undo.DestroyObjectImmediate(previousMinimapHud);

            foreach (GameObject previousCityRoot in previousCityRoots)
                Undo.DestroyObjectImmediate(previousCityRoot);

            Undo.RegisterCreatedObjectUndo(cityRootGO, "Rebuild City");
            Undo.RecordObject(cityRootGO, "Rebuild City");
            cityRootGO.name = "City";

            CityGeneratorInfo info = cityRootGO.GetComponent<CityGeneratorInfo>();
            info.minimapHUD = CreateMinimapHud(scene, settings.minimap);
            info.dayNightCycle = UpdateDirectionalLight(scene, settings.dayNight);

            // Camera and player are left untouched by a Re-Build (see this method's remarks), so
            // they're resolved from whatever the scene already has instead of being (re)created.
            GameObject playerGO = GameObject.Find("Player");
            info.player = playerGO != null ? playerGO.transform : null;
            GameObject cameraGO = GameObject.Find("Main Camera");
            info.freeCameraController = cameraGO != null ? cameraGO.GetComponent<FreeCameraController>() : null;

            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(scene);
            return summary;
        }

        // Roughly east-west sun alignment: with the minimap's snapshot camera looking straight
        // down (CityGeneratorMinimapBuilder, Euler(90,0,0)), minimap-right maps to world +X and
        // minimap-top to world +Z, so minimap-right is East. A yaw of -90 would put the sunrise
        // (per DayNightCycle's pitch formula, hour 6) due East and the sunset due West; -110 tilts
        // that axis by 20 degrees, so the sun rises east-north-east and sets west-south-west
        // instead — close enough that the minimap still reads right, off-axis enough that shadows
        // don't fall exactly along a street. Forced on every build/re-build (see
        // UpdateDirectionalLight), never left to whatever yaw the light happened to have.
        internal const float DirectionalLightYaw = -110f;

        // Finds the "Directional Light" GameObject by name (same pattern, and same fragility if
        // the user renames it, as CreateMinimapHud's lookup of "Minimap HUD"), recreating it via
        // CreateDirectionalLight if it was deleted, then reconciles its DayNightCycle with the
        // current settings. Never moves the light or touches its shadows — but the yaw (see
        // DirectionalLightYaw) and the day/night cycle are always forced to the current settings,
        // even on a light that already existed with a different baked-in yaw.
        private static DayNightCycle UpdateDirectionalLight(Scene scene, DayNightSettings dayNight)
        {
            GameObject lightGO = GameObject.Find("Directional Light");
            if (lightGO == null)
                return CreateDirectionalLight(scene, dayNight);

            return ConfigureDayNightCycle(lightGO, dayNight);
        }

        private static DayNightCycle CreateDirectionalLight(Scene scene, DayNightSettings dayNight)
        {
            var lightGO = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightGO, scene);
            lightGO.transform.rotation = Quaternion.Euler(50f, DirectionalLightYaw, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;

            return ConfigureDayNightCycle(lightGO, dayNight);
        }

        // Adds/updates DayNightCycle on an existing Directional Light GameObject and previews the
        // result immediately (ApplySun), without touching the light's shadows or any other setting
        // that isn't part of the day/night cycle or its yaw. The component is kept even when
        // dayNight.enabled is false: Start Hour is always reflected on the light (via ApplySun),
        // and dayNight.enabled only toggles the component's own MonoBehaviour.enabled, which gates
        // whether Update() auto-advances the hour in Play Mode — Unity skips Update on a disabled
        // Behaviour, so a disabled cycle simply stays put at Start Hour. The yaw/roll ApplySun
        // rotates around is forced to DirectionalLightYaw/0 via SetBaseRotation on every call,
        // bypassing DayNightCycle's own "captured once" base rotation so a light re-built with an
        // old yaw gets corrected too.
        private static DayNightCycle ConfigureDayNightCycle(GameObject lightGO, DayNightSettings dayNight)
        {
            var cycle = lightGO.GetComponent<DayNightCycle>();
            if (cycle == null)
                cycle = lightGO.AddComponent<DayNightCycle>();

            cycle.SetBaseRotation(Quaternion.Euler(0f, DirectionalLightYaw, 0f));
            cycle.enabled = dayNight.enabled;
            cycle.speedMultiplier = dayNight.speedMultiplier;
            cycle.lightColorOverTime = dayNight.lightColorOverTime;
            cycle.lightIntensityOverTime = dayNight.lightIntensityOverTime;
            cycle.ApplySun(dayNight.startHour);
            return cycle;
        }

        // Lets any character model be assigned as Player Prefab, not just one already set up for
        // it: DefaultAssets/Prefabs/Characters/ models stay clean (just their Animator), and
        // CharacterController/PlayerController are added here whenever the assigned prefab
        // doesn't already carry them. The tuning itself is applied unconditionally, on the
        // instance only (never on the prefab asset): the Player tab in CityGeneratorWindow is
        // the single source of truth, even for a prefab that ships its own baked tuning.
        private static void ConfigurePlayer(GameObject player, UnityEngine.InputSystem.InputActionAsset inputActions, PlayerSettings settings)
        {
            var characterController = player.GetComponent<CharacterController>();
            if (characterController == null)
                characterController = player.AddComponent<CharacterController>();
            characterController.height = settings.controllerHeight;
            characterController.radius = settings.controllerRadius;
            characterController.slopeLimit = settings.controllerSlopeLimit;
            characterController.stepOffset = settings.controllerStepOffset;
            characterController.skinWidth = settings.controllerSkinWidth;
            characterController.minMoveDistance = settings.controllerMinMoveDistance;
            characterController.center = settings.controllerCenter;

            var playerController = player.GetComponent<PlayerController>();
            if (playerController == null)
                playerController = player.AddComponent<PlayerController>();

            var inputAuthority = player.GetComponent<PlayerInputAuthority>();
            if (inputAuthority == null)
                inputAuthority = player.AddComponent<PlayerInputAuthority>();
            var authoritySerialized = new SerializedObject(inputAuthority);
            authoritySerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
            authoritySerialized.FindProperty("actionMapName").stringValue = settings.actionMapName;
            authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

            var serialized = new SerializedObject(playerController);
            serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
            serialized.FindProperty("actionMapName").stringValue = settings.actionMapName;
            serialized.FindProperty("moveActionName").stringValue = settings.moveActionName;
            serialized.FindProperty("jumpActionName").stringValue = settings.jumpActionName;
            serialized.FindProperty("sprintActionName").stringValue = settings.sprintActionName;
            serialized.FindProperty("walkSpeed").floatValue = settings.walkSpeed;
            serialized.FindProperty("runSpeed").floatValue = settings.runSpeed;
            serialized.FindProperty("rotationSmoothTime").floatValue = settings.rotationSmoothTime;
            serialized.FindProperty("gravity").floatValue = settings.gravity;
            serialized.FindProperty("jumpHeight").floatValue = settings.jumpHeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Puts the player on the same layer CityGeneratorPedestrianBuilder uses for NPC
        // pedestrians, so CarAgent's pedestrian sensor (CarAgent.pedestrianMask) stops for the
        // player exactly like it does for a pedestrian. The layer itself is created (and the
        // mask assigned to every vehicle) by CityGeneratorPedestrianBuilder.EnsurePedestrianLayerAndAssignMask
        // whenever vehicles exist, independent of includePedestrians — this only has to look it
        // up. Left at its default layer if that layer doesn't exist yet (no vehicles were
        // generated), the same fail-closed fallback used elsewhere in the tool.
        private static void AssignPedestrianLayer(GameObject player)
        {
            int pedestrianLayer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);
            if (pedestrianLayer >= 0)
                player.layer = pedestrianLayer;
        }

        private static FreeCameraController CreateMainCamera(Scene scene, GameObject player, UnityEngine.InputSystem.InputActionAsset inputActions, PlayerSettings playerSettings, CameraSettings settings, FreeCameraSettings freeCameraSettings)
        {
            FreeCameraController freeCameraController = null;
            var cameraGO = new GameObject("Main Camera") { tag = "MainCamera" };
            SceneManager.MoveGameObjectToScene(cameraGO, scene);
            cameraGO.transform.position = new Vector3(36f, 28f, -36f);
            cameraGO.transform.rotation = Quaternion.Euler(27f, -45f, 0f);
            var camera = cameraGO.AddComponent<Camera>();
            camera.fieldOfView = settings.fieldOfView;
            cameraGO.AddComponent<AudioListener>();
            var thirdPersonCamera = cameraGO.AddComponent<ThirdPersonCamera>();

            var cameraSerialized = new SerializedObject(thirdPersonCamera);
            cameraSerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
            cameraSerialized.FindProperty("actionMapName").stringValue = playerSettings.actionMapName;
            cameraSerialized.FindProperty("lookActionName").stringValue = playerSettings.lookActionName;
            if (player != null)
                cameraSerialized.FindProperty("target").objectReferenceValue = player.transform;
            cameraSerialized.FindProperty("verticalOffset").floatValue = settings.verticalOffset;
            cameraSerialized.FindProperty("horizontalOffset").floatValue = settings.horizontalOffset;
            cameraSerialized.FindProperty("distance").floatValue = settings.distance;
            cameraSerialized.FindProperty("minDistance").floatValue = settings.minDistance;
            cameraSerialized.FindProperty("sensitivity").floatValue = settings.sensitivity;
            cameraSerialized.FindProperty("minPitch").floatValue = settings.minPitch;
            cameraSerialized.FindProperty("maxPitch").floatValue = settings.maxPitch;
            cameraSerialized.FindProperty("followSmoothTime").floatValue = settings.followSmoothTime;
            cameraSerialized.FindProperty("collisionMask").intValue = settings.collisionMask.value;
            cameraSerialized.FindProperty("collisionRadius").floatValue = settings.collisionRadius;
            cameraSerialized.FindProperty("lockCursor").boolValue = settings.lockCursor;
            cameraSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Ignored in silence (no FreeCameraController, no validation error) when Player itself
            // is disabled or absent — see FreeCameraSettings.enabled's remarks.
            if (player != null && freeCameraSettings.enabled)
            {
                freeCameraController = cameraGO.AddComponent<FreeCameraController>();
                var freeCameraSerialized = new SerializedObject(freeCameraController);
                freeCameraSerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
                freeCameraSerialized.FindProperty("playerActionMapName").stringValue = playerSettings.actionMapName;
                freeCameraSerialized.FindProperty("playerToggleActionName").stringValue = playerSettings.toggleActionName;
                freeCameraSerialized.FindProperty("actionMapName").stringValue = freeCameraSettings.actionMapName;
                freeCameraSerialized.FindProperty("moveActionName").stringValue = freeCameraSettings.moveActionName;
                freeCameraSerialized.FindProperty("verticalActionName").stringValue = freeCameraSettings.verticalActionName;
                freeCameraSerialized.FindProperty("sprintActionName").stringValue = freeCameraSettings.sprintActionName;
                freeCameraSerialized.FindProperty("lookActionName").stringValue = freeCameraSettings.lookActionName;
                freeCameraSerialized.FindProperty("toggleActionName").stringValue = freeCameraSettings.toggleActionName;
                freeCameraSerialized.FindProperty("moveSpeed").floatValue = freeCameraSettings.moveSpeed;
                freeCameraSerialized.FindProperty("sprintMultiplier").floatValue = freeCameraSettings.sprintMultiplier;
                freeCameraSerialized.FindProperty("rotationSmoothTime").floatValue = freeCameraSettings.rotationSmoothTime;
                freeCameraSerialized.FindProperty("player").objectReferenceValue = player;
                freeCameraSerialized.FindProperty("thirdPersonCamera").objectReferenceValue = thirdPersonCamera;
                freeCameraSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (player == null)
                return freeCameraController;

            var playerController = player.GetComponent<PlayerController>();
            if (playerController == null)
                return freeCameraController;

            var playerSerialized = new SerializedObject(playerController);
            playerSerialized.FindProperty("cameraTransform").objectReferenceValue = cameraGO.transform;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();

            return freeCameraController;
        }

        // Loaded by a fixed package path (like ThumbnailPath in CityGeneratorWindow), not a
        // settings field: the HUD prefab is fixed tool content, not something a user is expected
        // to swap out. Silently skipped (same fail-closed fallback as AssignPedestrianLayer) if
        // the package's DefaultAssets/ prefab is ever missing, so a broken/partial install still
        // produces a working city, just without the HUD.
        private static MinimapHUD CreateMinimapHud(Scene scene, MinimapSettings settings)
        {
            if (!settings.enabled)
                return null;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinimapHudPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[City Generator] Minimap is enabled but the MinimapHUD prefab is missing from DefaultAssets/ — skipping the HUD.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = MinimapHudInstanceName;

            var hud = instance.GetComponentInChildren<MinimapHUD>(true);
            if (hud == null)
                return null;

            var serialized = new SerializedObject(hud);
            serialized.FindProperty("viewRadiusMeters").floatValue = settings.viewRadiusMeters;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        // AssetDatabase.GenerateUniqueAssetPath would suffix collisions as "City 1.unity" (with a
        // space), breaking the documented City<N> naming convention — so the free slot is still
        // found by trying City1.unity, City2.unity, ... in order, just via the AssetDatabase
        // (aware of pending imports) instead of a raw File.Exists on a relative path.
        private static string GetNextFreeScenePath()
        {
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            int n = 1;
            string path;
            do
            {
                path = $"{ScenesFolder}/City{n}.unity";
                n++;
            } while (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null);

            return path;
        }
    }
}
