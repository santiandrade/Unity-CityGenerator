using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 24: every successful generation writes its settings to
    /// <c>&lt;SceneFolder&gt;/&lt;SceneName&gt;/&lt;SceneName&gt;_CityGeneratorSettings.asset</c>, a
    /// Re-Build overwrites it in place (same GUID), an unsaved scene gets none, and a city generated
    /// from a loaded asset is the same city as from the window it was saved from.
    /// The Re-Build cases open a disposable scene in Single mode, because
    /// <see cref="CityGeneratorSceneBuilder.RebuildInActiveScene"/> resolves "Minimap HUD" and
    /// "Directional Light" by name across every loaded scene; the previous scene setup is restored
    /// in TearDown, and the tests are skipped if a loaded scene has unsaved changes.
    /// </summary>
    internal class SettingsAssetGenerationTests
    {
        private const string TempFolder = "Assets/SettingsAssetGenerationTestsTemp";

        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 70000f;
        private SceneSetup[] previousSceneSetup;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "SettingsAssetGenerationTestsTemp");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in spawnedRoots)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
            spawnedRoots.Clear();

            if (previousSceneSetup != null)
            {
                if (previousSceneSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                previousSceneSetup = null;
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        /// <summary>A small, fast city: no traffic/pedestrians/minimap/audio/custom content.</summary>
        private static CityGeneratorSettings MakeSmallSettings(int seed)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            settings.general.plazaCells.Clear();
            settings.general.useCustomSeed = true;
            settings.general.seed = seed;
            settings.general.includeTraffic = false;
            settings.general.includePedestrians = false;
            settings.general.vehicleCount = 0;
            settings.general.pedestrianCount = 0;
            settings.general.playerEnabled = false;
            settings.customPlaces.Clear();
            settings.customPedestrians.Clear();
            settings.minimap.enabled = false;
            settings.audio.ambience.enabled = false;
            settings.audio.plazaAudio.enabled = false;
            return settings;
        }

        private void OpenDisposableSavedScene(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("Save the open scene(s) before running this test: it swaps the scene setup and restores it afterwards.");
            }

            previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, scenePath));
        }

        [Test]
        public void BuildAndSaveScene_WritesTheSettingsAssetNextToTheScene()
        {
            // Unity refuses to add a scene additively while an untitled, unsaved scene is open, so
            // the open scene is swapped for a saved disposable one first.
            OpenDisposableSavedScene($"{TempFolder}/Host.unity");
            CityGeneratorSettings settings = MakeSmallSettings(seed: 11);
            string scenePath = $"{TempFolder}/GenCity.unity";

            CityGeneratorSceneBuilder.BuildAndSaveScene(settings, scenePath, onProgress: null);

            string expectedPath = $"{TempFolder}/GenCity/GenCity_CityGeneratorSettings.asset";
            Assert.AreEqual(expectedPath, CityGeneratorSettingsAssetPaths.ForScene(scenePath));
            var asset = AssetDatabase.LoadAssetAtPath<CityGeneratorSettingsAsset>(expectedPath);
            Assert.IsNotNull(asset, "BuildAndSaveScene must write the settings asset in the per-scene folder.");
            Assert.AreEqual(CityGeneratorSettingsAsset.ToJson(settings), CityGeneratorSettingsAsset.ToJson(asset.settings), "The asset must hold exactly the settings used.");
            Assert.AreEqual(CityGeneratorSettingsAsset.CurrentPackageVersion(), asset.packageVersion);
        }

        [Test]
        public void RebuildInActiveScene_OverwritesTheSameAsset_KeepingItsGuid()
        {
            string scenePath = $"{TempFolder}/RebuildCity.unity";
            OpenDisposableSavedScene(scenePath);
            string assetPath = CityGeneratorSettingsAssetPaths.ForScene(scenePath);

            CityGeneratorSceneBuilder.RebuildInActiveScene(MakeSmallSettings(seed: 1));
            string firstGuid = AssetDatabase.AssetPathToGUID(assetPath);
            Assert.IsFalse(string.IsNullOrEmpty(firstGuid), "The first Re-Build must write the asset.");

            CityGeneratorSettings second = MakeSmallSettings(seed: 2);
            CityGeneratorSceneBuilder.RebuildInActiveScene(second);

            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(assetPath), "A Re-Build must keep the asset's GUID.");
            var asset = AssetDatabase.LoadAssetAtPath<CityGeneratorSettingsAsset>(assetPath);
            Assert.AreEqual(2, asset.settings.general.seed, "The asset always reflects the last generation.");
            Assert.AreEqual(CityGeneratorSettingsAsset.ToJson(second), CityGeneratorSettingsAsset.ToJson(asset.settings));
        }

        [Test]
        public void RebuildInActiveScene_UnsavedScene_WritesNothing()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("Save the open scene(s) before running this test: it swaps the scene setup and restores it afterwards.");
            }

            previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string[] before = AssetDatabase.FindAssets("t:CityGeneratorSettingsAsset");

            CityGeneratorSceneBuilder.RebuildInActiveScene(MakeSmallSettings(seed: 3));

            string[] after = AssetDatabase.FindAssets("t:CityGeneratorSettingsAsset");
            CollectionAssert.AreEquivalent(before, after, "An unsaved scene has no path, so no settings asset may be written.");
        }

        private Transform CreateOffsetRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(nextOffset, 0f, nextOffset);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        [Test]
        public void CityFromLoadedAsset_IsTheSameCityAsFromTheWindow()
        {
            CityGeneratorSettings windowSettings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(windowSettings);
            windowSettings.general.gridWidth = 4;
            windowSettings.general.gridHeight = 4;
            windowSettings.general.plazaCells = new List<Vector2Int> { new(1, 1) };
            windowSettings.general.useCustomSeed = true;
            windowSettings.general.seed = 2468;
            windowSettings.general.includeTraffic = false;
            windowSettings.general.includePedestrians = false;
            windowSettings.customPedestrians.Clear();
            windowSettings.minimap.enabled = false;
            Assume.That(windowSettings.buildingPrefabs.Count, Is.GreaterThanOrEqualTo(4), "This test needs the package's demo building prefabs.");
            windowSettings.districts = new List<DistrictEntry>
            {
                new()
                {
                    name = "North",
                    cells = new List<Vector2Int> { new(0, 3), new(1, 3), new(2, 3) },
                    buildingPrefabs = windowSettings.buildingPrefabs.GetRange(0, 2),
                    overrideBuildingsPerBlock = true,
                    buildingsPerBlock = 2,
                },
            };
            windowSettings.customPlaces = new List<CustomPlaceEntry>
            {
                new()
                {
                    title = "Landmark",
                    prefab = windowSettings.buildingPrefabs[3],
                    blockCell = new Vector2Int(3, 0),
                    cornerSlot = 1,
                    facing = CustomPlaceFacing.East,
                    positionAssigned = true,
                },
            };

            string assetPath = $"{TempFolder}/Equivalence.asset";
            CityGeneratorSettingsAsset asset = CityGeneratorSettingsAsset.WriteAt(windowSettings, assetPath);
            CityGeneratorSettings fromAsset = asset.CopySettings();

            Transform rootFromWindow = CreateOffsetRoot("FromWindow");
            Transform rootFromAsset = CreateOffsetRoot("FromAsset");
            CityBuildSummary summaryFromWindow = CityGeneratorContentAssembler.Assemble(windowSettings, rootFromWindow);
            CityBuildSummary summaryFromAsset = CityGeneratorContentAssembler.Assemble(fromAsset, rootFromAsset);

            Assert.AreEqual(summaryFromWindow.buildingCount, summaryFromAsset.buildingCount);
            CollectionAssert.AreEqual(CountBuildingsPerPrefab(rootFromWindow), CountBuildingsPerPrefab(rootFromAsset), "Same buildings from each pool.");
            CollectionAssert.AreEqual(CollectPlacement(rootFromWindow, "Buildings"), CollectPlacement(rootFromAsset, "Buildings"), "Same buildings in the same blocks and slots.");
            List<string> placesFromWindow = CollectPlacement(rootFromWindow, "CustomPlaces");
            Assert.Greater(placesFromWindow.Count, 0, "The Custom Place must have been placed.");
            CollectionAssert.AreEqual(placesFromWindow, CollectPlacement(rootFromAsset, "CustomPlaces"), "Same Custom Places at the same positions.");
        }

        private static SortedDictionary<string, int> CountBuildingsPerPrefab(Transform root)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (Transform building in Descendants(root.Find("Buildings")))
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(building.gameObject);
                if (source == null)
                    continue;
                counts.TryGetValue(source.name, out int count);
                counts[source.name] = count + 1;
            }
            return counts;
        }

        /// <summary>
        /// Name, source prefab, position and yaw of every prefab instance root under
        /// <paramref name="groupName"/>, relative to that group: the assembler parents its groups
        /// keeping their world position, so the group (not the offset root) is the common frame.
        /// </summary>
        private static List<string> CollectPlacement(Transform root, string groupName)
        {
            var placement = new List<string>();
            Transform group = root.Find(groupName);
            foreach (Transform instance in Descendants(group))
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                if (source == null)
                    continue;
                Vector3 position = group.InverseTransformPoint(instance.position);
                placement.Add($"{instance.name}|{source.name}|{position.x:F2},{position.y:F2},{position.z:F2}|{instance.eulerAngles.y:F0}");
            }
            return placement;
        }

        /// <summary>Outermost prefab instance roots under <paramref name="group"/>, in hierarchy order.</summary>
        private static IEnumerable<Transform> Descendants(Transform group)
        {
            if (group == null)
                yield break;

            foreach (Transform child in group)
            {
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject))
                {
                    yield return child;
                    continue;
                }

                foreach (Transform nested in Descendants(child))
                    yield return nested;
            }
        }
    }
}
