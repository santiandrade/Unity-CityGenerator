using System.IO;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private const string InputActionsAssetPath = "Assets/InputSystem_Actions.inputactions";

        public static (string scenePath, CityBuildSummary summary) BuildAndSaveScene(CityGeneratorSettings settings)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                var cityRootGO = new GameObject("City");
                SceneManager.MoveGameObjectToScene(cityRootGO, scene);
                CityBuildSummary summary = CityGeneratorContentAssembler.Assemble(settings, cityRootGO.transform);

                CreateDirectionalLight(scene);

                GameObject player = null;
                if (settings.general.playerPrefab != null)
                {
                    player = (GameObject)PrefabUtility.InstantiatePrefab(settings.general.playerPrefab, scene);
                    player.name = "Player";
                }

                CreateMainCamera(scene, player);

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
            CityBuildSummary summary = CityGeneratorContentAssembler.Assemble(settings, cityRootGO.transform);

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

        private static void CreateMainCamera(Scene scene, GameObject player)
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
            cameraSerialized.FindProperty("inputActions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsAssetPath);
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
            } while (File.Exists(path));

            return path;
        }
    }
}
