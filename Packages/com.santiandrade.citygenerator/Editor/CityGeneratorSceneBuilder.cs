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

                CreateDirectionalLight(scene);

                GameObject player = null;
                if (settings.general.playerPrefab != null)
                {
                    player = (GameObject)PrefabUtility.InstantiatePrefab(settings.general.playerPrefab, scene);
                    player.name = "Player";
                    player.transform.position = summary.playerSpawnPosition;
                    ConfigurePlayer(player, settings.general.inputActions, settings.player);
                    AssignPedestrianLayer(player);
                }

                CreateMainCamera(scene, player, settings.general.inputActions, settings.player.actionMapName, settings.player.lookActionName, settings.camera);
                CreateMinimapHud(scene, settings.minimap);

                EditorSceneManager.SaveScene(scene, scenePath);

                return (scenePath, summary);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Regenerates the city in the currently active scene from <paramref name="settings"/>,
        /// transactionally: the new city is built under a temporary root first, and the previous
        /// one (found by <see cref="CityGeneratorRoot"/>, not by name) is only destroyed once
        /// generation finishes without throwing. If <see cref="CityGeneratorContentAssembler.Assemble"/>
        /// throws, the failed temporary root is destroyed, the previous city is left completely
        /// intact, and the exception is rethrown for the caller (<c>CityGeneratorWindow</c>) to
        /// report. Everything else in the scene (light, volume, camera, player) is left untouched.
        /// Does not save the scene: the caller leaves that to the usual Editor "unsaved changes"
        /// flow.
        /// </summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings)
        {
            return RebuildInActiveScene(settings, onProgress: null);
        }

        /// <summary>Same as <see cref="RebuildInActiveScene(CityGeneratorSettings)"/>, forwarding generation progress to <paramref name="onProgress"/>.</summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings, System.Action<string, float> onProgress)
        {
            Scene scene = EditorSceneManager.GetActiveScene();

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
                throw;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild City");

            GameObject previousMinimapHud = GameObject.Find(MinimapHudInstanceName);
            if (previousMinimapHud != null)
                Undo.DestroyObjectImmediate(previousMinimapHud);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == cityRootGO)
                    continue;
                if (root.GetComponent<CityGeneratorRoot>() != null)
                {
                    Undo.DestroyObjectImmediate(root);
                    break;
                }
            }

            Undo.RegisterCreatedObjectUndo(cityRootGO, "Rebuild City");
            Undo.RecordObject(cityRootGO, "Rebuild City");
            cityRootGO.name = "City";

            CreateMinimapHud(scene, settings.minimap);

            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(scene);
            return summary;
        }

        private static void CreateDirectionalLight(Scene scene)
        {
            var lightGO = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightGO, scene);
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
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

        private static void CreateMainCamera(Scene scene, GameObject player, UnityEngine.InputSystem.InputActionAsset inputActions, string actionMapName, string lookActionName, CameraSettings settings)
        {
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
            cameraSerialized.FindProperty("actionMapName").stringValue = actionMapName;
            cameraSerialized.FindProperty("lookActionName").stringValue = lookActionName;
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

            if (player == null)
                return;

            var playerController = player.GetComponent<PlayerController>();
            if (playerController == null)
                return;

            var playerSerialized = new SerializedObject(playerController);
            playerSerialized.FindProperty("cameraTransform").objectReferenceValue = cameraGO.transform;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Loaded by a fixed package path (like ThumbnailPath in CityGeneratorWindow), not a
        // settings field: the HUD prefab is fixed tool content, not something a user is expected
        // to swap out. Silently skipped (same fail-closed fallback as AssignPedestrianLayer) if
        // the package's DefaultAssets/ prefab is ever missing, so a broken/partial install still
        // produces a working city, just without the HUD.
        private static void CreateMinimapHud(Scene scene, MinimapSettings settings)
        {
            if (!settings.enabled)
                return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinimapHudPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[City Generator] Minimap is enabled but the MinimapHUD prefab is missing from DefaultAssets/ — skipping the HUD.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = MinimapHudInstanceName;

            var hud = instance.GetComponentInChildren<MinimapHUD>(true);
            if (hud == null)
                return;

            var serialized = new SerializedObject(hud);
            serialized.FindProperty("viewRadiusMeters").floatValue = settings.viewRadiusMeters;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
