using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    /// <summary>
    /// SPEC 24: <see cref="CityGeneratorSettingsAsset"/>'s deep copy (window -> asset -> window with
    /// nothing lost and nothing shared), the window's link/Modified/Revert/Save bookkeeping, and how
    /// an asset with a deleted prefab or from another package version loads. Assets are written to a
    /// disposable folder deleted in TearDown.
    /// </summary>
    internal class CityGeneratorSettingsAssetTests
    {
        private const string TempFolder = "Assets/CityGeneratorSettingsAssetTestsTemp";

        private readonly List<Object> spawnedObjects = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "CityGeneratorSettingsAssetTestsTemp");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in spawnedObjects)
            {
                if (spawned != null)
                    Object.DestroyImmediate(spawned);
            }
            spawnedObjects.Clear();
            AssetDatabase.DeleteAsset(TempFolder);
        }

        /// <summary>Defaults plus a non-default value in every area the spec lists for the round trip.</summary>
        internal static CityGeneratorSettings MakeRichSettings()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);

            settings.general.useCustomSeed = true;
            settings.general.seed = 424242;
            settings.general.useCustomGrid = true;
            settings.general.customBlockCells = new List<Vector2Int>
            {
                new(10, 10), new(11, 10), new(12, 10), new(10, 11), new(11, 11),
            };
            settings.general.plazaCells = new List<Vector2Int> { new(11, 11) };
            settings.general.buildingsPerBlock = 3;
            settings.general.vehicleCount = 17;
            settings.general.pedestrianCount = 23;

            settings.districts = new List<DistrictEntry>
            {
                new()
                {
                    name = "Downtown",
                    cells = new List<Vector2Int> { new(10, 10), new(11, 10) },
                    buildingPrefabs = new List<GameObject>(settings.buildingPrefabs.GetRange(0, Mathf.Min(2, settings.buildingPrefabs.Count))),
                    overrideBuildingsPerBlock = true,
                    buildingsPerBlock = 2,
                },
            };

            GameObject placePrefab = settings.buildingPrefabs.Count > 0 ? settings.buildingPrefabs[0] : null;
            settings.customPlaces = new List<CustomPlaceEntry>
            {
                new()
                {
                    title = "Museum",
                    prefab = placePrefab,
                    isPointOfInterest = true,
                    blockCell = new Vector2Int(12, 10),
                    cornerSlot = 2,
                    facing = CustomPlaceFacing.West,
                    positionAssigned = true,
                },
            };

            settings.customPedestrians = new List<CustomPedestrianEntry>
            {
                new()
                {
                    title = "Walkers",
                    prefab = settings.general.playerPrefab,
                    count = 3,
                    selectedNodeIndices = new List<int> { 4, 5, 6 },
                    graphFingerprint = 987654,
                },
            };

            settings.minimap.enabled = true;
            settings.minimap.textureResolution = 1024;
            settings.minimap.viewRadiusMeters = 77f;

            settings.dayNight.enabled = true;
            settings.dayNight.startHour = 18.5f;
            settings.dayNight.speedMultiplier = 42f;
            settings.dayNight.lightIntensityOverTime = AnimationCurve.Linear(0f, 0.1f, 24f, 0.9f);

            settings.audio.ambience.enabled = true;
            settings.audio.plazaAudio.enabled = false;
            if (settings.audio.ambience.clips != null && settings.audio.ambience.clips.Count > 0)
            {
                AmbienceClipEntry clip = settings.audio.ambience.clips[0];
                clip.volume = 0.33f;
                settings.audio.ambience.clips[0] = clip;
            }

            return settings;
        }

        private CityGeneratorSettingsAsset CreateAsset(CityGeneratorSettings settings, string fileName = "Settings.asset")
        {
            return CityGeneratorSettingsAsset.WriteAt(settings, $"{TempFolder}/{fileName}");
        }

        private CityGeneratorWindow CreateWindow()
        {
            var window = ScriptableObject.CreateInstance<CityGeneratorWindow>();
            spawnedObjects.Add(window);
            return window;
        }

        [Test]
        public void RoundTrip_WindowToAssetToWindow_LosesNothing()
        {
            CityGeneratorSettings original = MakeRichSettings();
            string originalJson = CityGeneratorSettingsAsset.ToJson(original);

            CityGeneratorSettingsAsset asset = CreateAsset(original);
            CityGeneratorSettings loaded = asset.CopySettings();

            Assert.AreEqual(originalJson, CityGeneratorSettingsAsset.ToJson(loaded), "The JSON of the settings must survive the round trip unchanged.");

            Assert.AreEqual(424242, loaded.general.seed);
            Assert.IsTrue(loaded.general.useCustomSeed);
            Assert.IsTrue(loaded.general.useCustomGrid);
            CollectionAssert.AreEqual(original.general.customBlockCells, loaded.general.customBlockCells);
            CollectionAssert.AreEqual(original.general.plazaCells, loaded.general.plazaCells);
            Assert.AreEqual(1, loaded.districts.Count);
            Assert.AreEqual("Downtown", loaded.districts[0].name);
            CollectionAssert.AreEqual(original.districts[0].cells, loaded.districts[0].cells);
            CollectionAssert.AreEqual(original.districts[0].buildingPrefabs, loaded.districts[0].buildingPrefabs);
            Assert.AreEqual(2, loaded.districts[0].buildingsPerBlock);
            Assert.AreEqual(CustomPlaceFacing.West, loaded.customPlaces[0].facing);
            Assert.AreEqual(new Vector2Int(12, 10), loaded.customPlaces[0].blockCell);
            Assert.AreEqual(2, loaded.customPlaces[0].cornerSlot);
            Assert.AreSame(original.customPlaces[0].prefab, loaded.customPlaces[0].prefab);
            CollectionAssert.AreEqual(original.buildingPrefabs, loaded.buildingPrefabs, "Prefab references must come back as the same assets.");
            Assert.AreSame(original.general.inputActions, loaded.general.inputActions);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, loaded.customPedestrians[0].selectedNodeIndices);
            Assert.AreEqual(987654, loaded.customPedestrians[0].graphFingerprint);
            Assert.AreEqual(1024, loaded.minimap.textureResolution);
            Assert.AreEqual(77f, loaded.minimap.viewRadiusMeters);
            Assert.AreEqual(18.5f, loaded.dayNight.startHour);
            Assert.AreEqual(42f, loaded.dayNight.speedMultiplier);
            Assert.AreEqual(original.dayNight.lightIntensityOverTime.Evaluate(12f), loaded.dayNight.lightIntensityOverTime.Evaluate(12f));
            Assert.IsFalse(loaded.audio.plazaAudio.enabled);
            Assert.AreEqual(original.audio.ambience.clips.Count, loaded.audio.ambience.clips.Count);
            if (loaded.audio.ambience.clips.Count > 0)
            {
                Assert.AreSame(original.audio.ambience.clips[0].clip, loaded.audio.ambience.clips[0].clip);
                Assert.AreEqual(0.33f, loaded.audio.ambience.clips[0].volume);
            }
        }

        [Test]
        public void RoundTrip_SurvivesReloadingTheAssetFromDisk()
        {
            CityGeneratorSettings original = MakeRichSettings();
            CityGeneratorSettingsAsset asset = CreateAsset(original);
            string path = AssetDatabase.GetAssetPath(asset);

            Resources.UnloadAsset(asset);
            var reloaded = AssetDatabase.LoadAssetAtPath<CityGeneratorSettingsAsset>(path);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(CityGeneratorSettingsAsset.ToJson(original), CityGeneratorSettingsAsset.ToJson(reloaded.CopySettings()));
            Assert.AreEqual(CityGeneratorSettingsAsset.CurrentPackageVersion(), reloaded.packageVersion);
        }

        [Test]
        public void Copies_NeverShareInstances_BetweenWindowAndAsset()
        {
            CityGeneratorSettings windowSettings = MakeRichSettings();
            CityGeneratorSettingsAsset asset = CreateAsset(windowSettings);
            string assetJsonBefore = CityGeneratorSettingsAsset.ToJson(asset.settings);

            // Window -> asset: editing the source after Store must not reach the asset.
            windowSettings.general.seed = 1;
            windowSettings.general.plazaCells.Add(new Vector2Int(10, 11));
            windowSettings.buildingPrefabs.Clear();
            DistrictEntry district = windowSettings.districts[0];
            district.cells.Add(new Vector2Int(12, 10));
            Assert.AreEqual(assetJsonBefore, CityGeneratorSettingsAsset.ToJson(asset.settings), "Changing the window must not change the asset.");

            // Asset -> window: editing a loaded copy must not reach the asset, and vice versa.
            CityGeneratorSettings loaded = asset.CopySettings();
            loaded.customPlaces.Clear();
            loaded.general.customBlockCells.Clear();
            Assert.AreEqual(assetJsonBefore, CityGeneratorSettingsAsset.ToJson(asset.settings), "Changing a loaded copy must not change the asset.");

            CityGeneratorSettings secondCopy = asset.CopySettings();
            asset.settings.general.customBlockCells.Add(new Vector2Int(0, 0));
            asset.settings.districts[0].buildingPrefabs.Clear();
            Assert.AreEqual(5, secondCopy.general.customBlockCells.Count, "Changing the asset must not change a copy already loaded.");
            Assert.Greater(secondCopy.districts[0].buildingPrefabs.Count, 0);
        }

        [Test]
        public void Window_LoadThenEdit_IsModified_AndRevertRestoresTheAssetExactly()
        {
            CityGeneratorSettingsAsset asset = CreateAsset(MakeRichSettings());
            string assetJson = CityGeneratorSettingsAsset.ToJson(asset.settings);
            CityGeneratorWindow window = CreateWindow();

            window.LoadSettingsAsset(asset);
            Assert.AreSame(asset, window.linkedSettingsAsset);
            Assert.IsFalse(window.IsModifiedAgainstLinkedAsset(), "A freshly loaded asset is not Modified.");

            window.settings.general.seed = 5;
            window.settings.customPlaces.Clear();
            Assert.IsTrue(window.IsModifiedAgainstLinkedAsset(), "Any change marks the window Modified.");
            Assert.AreEqual(assetJson, CityGeneratorSettingsAsset.ToJson(asset.settings), "Editing the window must not touch the asset.");

            window.RevertToLinkedAsset();
            Assert.AreEqual(assetJson, CityGeneratorSettingsAsset.ToJson(window.settings), "Revert must leave exactly the asset's settings.");
            Assert.IsFalse(window.IsModifiedAgainstLinkedAsset(), "Revert clears Modified.");
            Assert.AreSame(asset, window.linkedSettingsAsset);
        }

        [Test]
        public void Window_Save_WritesTheAsset_AndClearsModified()
        {
            CityGeneratorSettingsAsset asset = CreateAsset(MakeRichSettings());
            CityGeneratorWindow window = CreateWindow();
            window.LoadSettingsAsset(asset);

            window.settings.general.seed = 31337;
            Assert.IsTrue(window.IsModifiedAgainstLinkedAsset());

            window.SaveSettingsToLinkedAsset();
            Assert.IsFalse(window.IsModifiedAgainstLinkedAsset());
            Assert.AreEqual(31337, asset.settings.general.seed);

            // Saving copies: editing the window afterwards must not reach the asset.
            window.settings.general.seed = 1;
            Assert.AreEqual(31337, asset.settings.general.seed);

            string yaml = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(asset));
            StringAssert.Contains("seed: 31337", yaml, "The saved .asset YAML must reflect the change.");
        }

        [Test]
        public void Window_SaveAs_CreatesAndLinksANewAsset()
        {
            CityGeneratorWindow window = CreateWindow();
            window.settings = MakeRichSettings();

            CityGeneratorSettingsAsset created = window.SaveSettingsAsAt($"{TempFolder}/SavedAs.asset");

            Assert.IsNotNull(created);
            Assert.AreSame(created, window.linkedSettingsAsset);
            Assert.IsFalse(window.IsModifiedAgainstLinkedAsset());
            Assert.AreEqual(CityGeneratorSettingsAsset.ToJson(window.settings), CityGeneratorSettingsAsset.ToJson(created.settings));
        }

        [Test]
        public void AssetWithDeletedPrefab_LoadsWithAnEmptyEntry_FlaggedByTheValidator()
        {
            var source = new GameObject("TempBuilding");
            source.AddComponent<MeshRenderer>();
            string prefabPath = $"{TempFolder}/TempBuilding.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);

            CityGeneratorSettings settings = MakeRichSettings();
            settings.buildingPrefabs.Add(prefab);
            int deletedIndex = settings.buildingPrefabs.Count - 1;
            CityGeneratorSettingsAsset asset = CreateAsset(settings);

            AssetDatabase.DeleteAsset(prefabPath);

            CityGeneratorSettings loaded = null;
            Assert.DoesNotThrow(() => loaded = asset.CopySettings());
            Assert.AreEqual(settings.buildingPrefabs.Count, loaded.buildingPrefabs.Count, "The entry stays, empty.");
            Assert.IsTrue(loaded.buildingPrefabs[deletedIndex] == null, "A deleted prefab loads as an empty reference.");

            List<CityGeneratorValidationIssue> issues = null;
            Assert.DoesNotThrow(() => CityGeneratorValidator.ValidateDetailed(loaded, out issues));
            Assert.IsTrue(issues.Exists(issue => issue.settingsPath == "buildingPrefabs" && issue.message.Contains($"entry {deletedIndex + 1} is empty")),
                "The validator must flag the empty entry, as if it had been cleared by hand.");
        }

        [Test]
        public void AssetFromOlderVersion_MissingFieldsKeepTheirDefaults()
        {
            // An asset written before a field existed simply has no key for it in its data.
            CityGeneratorSettings settings = MakeRichSettings();
            string json = CityGeneratorSettingsAsset.ToJson(settings).Replace("\"vehiclePhysics\":", "\"vehiclePhysicsRenamedAway\":");

            var copy = new CityGeneratorSettings();
            EditorJsonUtility.FromJsonOverwrite(json, copy);

            var fresh = new VehiclePhysicsSettings();
            Assert.AreEqual(fresh.mass, copy.vehiclePhysics.mass, "A field missing from the data keeps its initializer default.");
            Assert.AreEqual(424242, copy.general.seed, "Everything else still loads.");
        }

        [Test]
        public void PackageVersionComparison_DetectsNewerAssets()
        {
            Assert.Greater(CityGeneratorSettingsAsset.CompareVersions("3.11.0", "3.10.9"), 0);
            Assert.Less(CityGeneratorSettingsAsset.CompareVersions("3.9.0", "3.10.0"), 0);
            Assert.AreEqual(0, CityGeneratorSettingsAsset.CompareVersions("3.10.0-preview.1", "3.10.0"));
            Assert.AreEqual(0, CityGeneratorSettingsAsset.CompareVersions(null, "3.10.0"));

            CityGeneratorSettingsAsset asset = CreateAsset(MakeRichSettings());
            Assert.IsFalse(asset.IsFromNewerPackageVersion(), "An asset written by this version is not newer.");
            asset.packageVersion = "999.0.0";
            Assert.IsTrue(asset.IsFromNewerPackageVersion());
        }

        [Test]
        public void ScenePaths_ShareThePerSceneFolderWithTheMinimap()
        {
            Assert.AreEqual("Assets/Scenes/City3/City3_CityGeneratorSettings.asset", CityGeneratorSettingsAssetPaths.ForScene("Assets/Scenes/City3.unity"));
            Assert.AreEqual("Assets/Scenes/City3/City3_Minimap.png", CityGeneratorSettingsAssetPaths.MinimapForScene("Assets/Scenes/City3.unity"));
            Assert.IsNull(CityGeneratorSettingsAssetPaths.ForScene(""));
            Assert.IsNull(CityGeneratorSettingsAssetPaths.ForScene(null));
        }

        [Test]
        public void InspectorSummary_ReportsTheAssetContents()
        {
            CityGeneratorSettingsAsset asset = CreateAsset(MakeRichSettings());
            var rows = new Dictionary<string, string>();
            foreach ((string label, string value) in CityGeneratorSettingsAssetEditor.BuildSummary(asset, asset.settings))
                rows[label] = value;

            Assert.AreEqual("Custom (5 blocks)", rows["Grid"]);
            Assert.AreEqual("424242", rows["Seed"]);
            Assert.AreEqual("1", rows["Districts"]);
            Assert.AreEqual("1", rows["Custom Places"]);
            Assert.AreEqual(asset.settings.buildingPrefabs.Count.ToString(), rows["Building Prefabs"]);
            Assert.AreEqual(CityGeneratorSettingsAsset.CurrentPackageVersion() ?? "Unknown", rows["Saved With Version"]);
        }
    }
}
