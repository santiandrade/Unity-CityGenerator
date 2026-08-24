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

                CreateDirectionalLight(scene);

                GameObject player = null;
                if (settings.general.playerPrefab != null)
                {
                    player = (GameObject)PrefabUtility.InstantiatePrefab(settings.general.playerPrefab, scene);
                    player.name = "Player";
                    player.transform.position = summary.playerSpawnPosition;
                    EnsurePlayerComponents(player, settings.general.inputActions);
                    AssignPedestrianLayer(player);
                }

                CreateMainCamera(scene, player, settings.general.inputActions);

                string scenePath = GetNextFreeScenePath();
                EditorSceneManager.SaveScene(scene, scenePath);

                return (scenePath, summary);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Deletes the "City" root object in the currently active scene (if any) and regenerates
        /// it from <paramref name="settings"/>. Everything else in the scene (light, volume,
        /// camera, player) is left untouched. Does not save the scene: the caller leaves that to
        /// the usual Editor "unsaved changes" flow.
        /// </summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings)
        {
            return RebuildInActiveScene(settings, onProgress: null);
        }

        /// <summary>Same as <see cref="RebuildInActiveScene(CityGeneratorSettings)"/>, forwarding generation progress to <paramref name="onProgress"/>.</summary>
        public static CityBuildSummary RebuildInActiveScene(CityGeneratorSettings settings, System.Action<string, float> onProgress)
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "City")
                {
                    Object.DestroyImmediate(root);
                    break;
                }
            }

            var cityRootGO = new GameObject("City");
            SceneManager.MoveGameObjectToScene(cityRootGO, scene);
            CityBuildSummary summary = CityGeneratorContentAssembler.Assemble(settings, cityRootGO.transform, onProgress);

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
        // CharacterController/PlayerController are added here, with hardcoded default values,
        // whenever the assigned prefab doesn't already carry them.
        private static void EnsurePlayerComponents(GameObject player, UnityEngine.InputSystem.InputActionAsset inputActions)
        {
            if (player.GetComponent<CharacterController>() == null)
            {
                var characterController = player.AddComponent<CharacterController>();
                characterController.height = CityGeneratorConstants.PlayerControllerHeight;
                characterController.radius = CityGeneratorConstants.PlayerControllerRadius;
                characterController.slopeLimit = CityGeneratorConstants.PlayerControllerSlopeLimit;
                characterController.stepOffset = CityGeneratorConstants.PlayerControllerStepOffset;
                characterController.skinWidth = CityGeneratorConstants.PlayerControllerSkinWidth;
                characterController.minMoveDistance = CityGeneratorConstants.PlayerControllerMinMoveDistance;
                characterController.center = CityGeneratorConstants.PlayerControllerCenter;
            }

            if (player.GetComponent<PlayerController>() == null)
            {
                var playerController = player.AddComponent<PlayerController>();
                var serialized = new SerializedObject(playerController);
                serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
                serialized.FindProperty("actionMapName").stringValue = CityGeneratorConstants.PlayerActionMapName;
                serialized.FindProperty("moveActionName").stringValue = CityGeneratorConstants.PlayerMoveActionName;
                serialized.FindProperty("jumpActionName").stringValue = CityGeneratorConstants.PlayerJumpActionName;
                serialized.FindProperty("sprintActionName").stringValue = CityGeneratorConstants.PlayerSprintActionName;
                serialized.FindProperty("walkSpeed").floatValue = CityGeneratorConstants.PlayerWalkSpeed;
                serialized.FindProperty("runSpeed").floatValue = CityGeneratorConstants.PlayerRunSpeed;
                serialized.FindProperty("rotationSmoothTime").floatValue = CityGeneratorConstants.PlayerRotationSmoothTime;
                serialized.FindProperty("gravity").floatValue = CityGeneratorConstants.PlayerGravity;
                serialized.FindProperty("jumpHeight").floatValue = CityGeneratorConstants.PlayerJumpHeight;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
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

        private static void CreateMainCamera(Scene scene, GameObject player, UnityEngine.InputSystem.InputActionAsset inputActions)
        {
            var cameraGO = new GameObject("Main Camera") { tag = "MainCamera" };
            SceneManager.MoveGameObjectToScene(cameraGO, scene);
            cameraGO.transform.position = new Vector3(36f, 28f, -36f);
            cameraGO.transform.rotation = Quaternion.Euler(27f, -45f, 0f);
            var camera = cameraGO.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            cameraGO.AddComponent<AudioListener>();
            var thirdPersonCamera = cameraGO.AddComponent<ThirdPersonCamera>();

            var cameraSerialized = new SerializedObject(thirdPersonCamera);
            cameraSerialized.FindProperty("inputActions").objectReferenceValue = inputActions;
            if (player != null)
                cameraSerialized.FindProperty("target").objectReferenceValue = player.transform;
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
