using System;
using System.Collections.Generic;
using CityGenerator.Editor.UI;
using CityGenerator.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor
{
    internal class CityGeneratorWindow : EditorWindow
    {
        private const int MinGridSize = 1;
        private const int MaxGridSize = 10;
        private const string UiFolder = "Packages/com.santiandrade.citygenerator/Editor/UI/";
        private const string UxmlPath = UiFolder + "CityGeneratorWindow.uxml";
        private const string UssPath = UiFolder + "CityGeneratorWindow.uss";
        private const string UssDarkPath = UiFolder + "CityGeneratorWindow_Dark.uss";
        private const string UssLightPath = UiFolder + "CityGeneratorWindow_Light.uss";
        private const string ThumbnailPath = "Packages/com.santiandrade.citygenerator/Editor/ToolThumbnail.png";

        [SerializeField] private CityGeneratorSettings settings = new();
        [SerializeField] private bool defaultsInitialized;

        private SerializedObject serializedWindow;

        // Populated by BuildUi; consulted by Revalidate to mark a card/field as the source of a
        // validation issue, and to size badges/summaries live as the user edits.
        private readonly Dictionary<string, CityGeneratorCard> cardsBySettingsSegment = new();
        private readonly List<RequiredRow> requiredRows = new();
        private CityGeneratorCard generalCard;
        private CityGeneratorCard buildingsCard;
        private CityGeneratorCard vegetationCard;
        private CityGeneratorCard vehiclesCard;
        private CityGeneratorCard pedestriansCard;
        private CityGeneratorGridPreview gridPreview;
        private Label gridPreviewCaption;
        private Label summaryLine;
        private HelpBox vehicleDensityWarning;
        private HelpBox pedestrianDensityWarning;
        private HelpBox isolatedBlocksWarning;
        private VisualElement validationPanel;
        private VisualElement resultPanel;
        private PropertyField seedField;
        private Button buildNewSceneButton;
        private Button rebuildCurrentSceneButton;

        private readonly struct RequiredRow
        {
            public readonly VisualElement row;
            public readonly Func<bool> isRequired;
            public readonly Func<bool> isEmpty;

            public RequiredRow(VisualElement row, Func<bool> isRequired, Func<bool> isEmpty)
            {
                this.row = row;
                this.isRequired = isRequired;
                this.isEmpty = isEmpty;
            }
        }

        [MenuItem("Tools/City Generator/Open")]
        private static void ShowWindow()
        {
            var window = GetWindow<CityGeneratorWindow>();
            window.titleContent = new GUIContent("City Generator");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
        }

        /// <summary>
        /// Captures whatever is currently assigned in the open window and writes it back as the
        /// tool's new default (source files under the package's own Editor/ folder), so the next
        /// window and "Reset to Defaults" both open with it. Requires an already-open window
        /// rather than opening one itself: creating a fresh one just to save its empty/default
        /// state as the new default would be self-defeating.
        /// </summary>
        [MenuItem("Tools/City Generator/Set Current Selection As Default")]
        private static void SetCurrentSelectionAsDefaultMenuItem()
        {
            CityGeneratorWindow window = FindOpenWindow();
            if (window == null)
            {
                EditorUtility.DisplayDialog(
                    "City Generator",
                    "Open the City Generator window first (Tools > City Generator > Open) so there is a current selection to save.",
                    "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "City Generator - Set Current Selection As Default",
                "This overwrites the tool's default settings (prefabs, counts, densities...) with what is currently assigned in the open City Generator window, by editing the package's own source files. This cannot be undone with Ctrl+Z.",
                "Save as Default",
                "Cancel");
            if (!confirmed)
                return;

            CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault(window.settings);
            EditorUtility.DisplayDialog("City Generator", "Current selection saved as the new default.", "OK");
        }

        /// <summary>
        /// Recalculates the pedestrian graph against the scene as it currently stands, without
        /// regenerating the city — the explicit re-bake (level 3 of the pruning described in the
        /// spec), useful after hand-editing/moving a building. Equivalent to the component's own
        /// "Rebuild Network" context menu, exposed here too since a generated city's
        /// PedestrianNetwork is buried inside the City/PedestrianNetwork group.
        /// </summary>
        [MenuItem("Tools/City Generator/Rebuild Pedestrian Network")]
        private static void RebuildPedestrianNetworkMenuItem()
        {
            var network = UnityEngine.Object.FindAnyObjectByType<PedestrianNetwork>();
            if (network == null)
            {
                EditorUtility.DisplayDialog(
                    "City Generator",
                    "No PedestrianNetwork found in the current scene. Generate a city first.",
                    "OK");
                return;
            }

            network.Build();
            Debug.Log("[City Generator] Pedestrian network rebuilt.");
        }

        private static CityGeneratorWindow FindOpenWindow()
        {
            CityGeneratorWindow[] windows = Resources.FindObjectsOfTypeAll<CityGeneratorWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        // Runs once per window instance (not on every domain reload's OnEnable, since
        // defaultsInitialized is itself serialized): AssetDatabase can't be touched from a field
        // initializer, so the tool's own reference-city prefabs are assigned here instead.
        private void OnEnable()
        {
            if (defaultsInitialized)
                return;

            CityGeneratorDefaultAssets.ApplyTo(settings);
            defaultsInitialized = true;
        }

        private void CreateGUI()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            // Clear() only removes children; it does not undo the previous Bind/
            // TrackSerializedObjectValue call on rootVisualElement itself. Without this Unbind,
            // rebuilding the UI (e.g. from ResetToDefaults) throws NotSupportedException when
            // TrackSerializedObjectValue tries to track a new SerializedObject on an element still
            // tracking the old one.
            rootVisualElement.Unbind();
            rootVisualElement.Clear();
            cardsBySettingsSegment.Clear();
            requiredRows.Clear();

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var baseStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            var themeStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(EditorGUIUtility.isProSkin ? UssDarkPath : UssLightPath);
            if (visualTree == null || baseStyle == null || themeStyle == null)
            {
                rootVisualElement.Add(new Label("City Generator UI assets are missing from the package."));
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            // Theme sheet first so the base sheet's var() lookups (--cg-*) resolve against it.
            rootVisualElement.styleSheets.Add(themeStyle);
            rootVisualElement.styleSheets.Add(baseStyle);

            serializedWindow = new SerializedObject(this);

            BuildBanner();
            VisualElement cardsContainer = rootVisualElement.Q<VisualElement>("cg-cards");
            BuildGeneralCard(cardsContainer);
            BuildGroundCard(cardsContainer);
            BuildPlazaCard(cardsContainer);
            BuildBuildingsCard(cardsContainer);
            BuildVegetationCard(cardsContainer);
            BuildVehiclesCard(cardsContainer);
            BuildPedestriansCard(cardsContainer);
            BuildPropsCard(cardsContainer);
            BuildFooter();

            rootVisualElement.Bind(serializedWindow);
            rootVisualElement.TrackSerializedObjectValue(serializedWindow, _ => RefreshDynamicUi());
            RefreshDynamicUi();
        }

        /// <summary>
        /// The header is just the thumbnail, at whatever width the window currently has, with its
        /// full image always visible — never cropped or distorted. USS can't derive height from an
        /// element's own resolved width, so the aspect-correct height is recomputed here on every
        /// layout pass instead (mirrors the old IMGUI window's own <c>width / aspect</c> math).
        /// </summary>
        private void BuildBanner()
        {
            var banner = rootVisualElement.Q<VisualElement>("cg-banner");
            var thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(ThumbnailPath);
            if (thumbnail == null)
            {
                banner.style.display = DisplayStyle.None;
                return;
            }

            banner.style.backgroundImage = new StyleBackground(thumbnail);
            float aspect = (float)thumbnail.width / thumbnail.height;
            banner.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float width = evt.newRect.width;
                if (width > 0f)
                    banner.style.height = width / aspect;
            });

            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CityGeneratorWindow).Assembly);
            banner.tooltip = packageInfo != null ? $"City Generator v{packageInfo.version}" : "City Generator";
        }

        private void BuildGeneralCard(VisualElement parent)
        {
            generalCard = AddCard(parent, "general", "General Options", "d_SceneAsset Icon", defaultExpanded: true);
            VisualElement content = generalCard.ContentContainer;

            gridPreview = new CityGeneratorGridPreview();
            gridPreview.Bind(FindProperty("general.plazaCells"), RefreshDynamicUi);
            content.Add(gridPreview);
            gridPreviewCaption = new Label();
            gridPreviewCaption.AddToClassList("cg-grid-preview__caption");
            content.Add(gridPreviewCaption);
            var gridPreviewHint = new Label("Click a block above to toggle it as a plaza.");
            gridPreviewHint.AddToClassList("cg-grid-preview__caption");
            content.Add(gridPreviewHint);

            content.Add(CreateIntSlider(FindProperty("general.gridWidth"), "Grid Width", MinGridSize, MaxGridSize));
            content.Add(CreateIntSlider(FindProperty("general.gridHeight"), "Grid Height", MinGridSize, MaxGridSize));
            content.Add(CreateIntSlider(FindProperty("general.buildingsPerBlock"), "Buildings Per Block", 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock));

            summaryLine = new Label();
            summaryLine.AddToClassList("cg-summary-line");
            content.Add(summaryLine);

            content.Add(CreateField("general.includeTraffic"));
            content.Add(CreateField("general.vehicleCount"));
            vehicleDensityWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            vehicleDensityWarning.style.display = DisplayStyle.None;
            content.Add(vehicleDensityWarning);

            content.Add(CreateField("general.includePedestrians"));
            content.Add(CreateField("general.pedestrianCount"));
            pedestrianDensityWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            pedestrianDensityWarning.style.display = DisplayStyle.None;
            content.Add(pedestrianDensityWarning);
            isolatedBlocksWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            isolatedBlocksWarning.style.display = DisplayStyle.None;
            content.Add(isolatedBlocksWarning);

            content.Add(CreateField("general.playerPrefab"));
            AddRequiredField(content, "general.inputActions", "Input Actions (if Player Prefab is set)",
                () => FindProperty("general.playerPrefab").objectReferenceValue != null);

            content.Add(CreateField("general.useCustomSeed", "Custom Seed"));
            PropertyField seedField = CreateField("general.seed", "Seed");
            content.Add(seedField);
            // Visibility only, re-applied every RefreshDynamicUi pass (see below) rather than a
            // dedicated poll, since a settings change already triggers that refresh.
            this.seedField = seedField;
        }

        private void BuildGroundCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "ground", "Ground", "d_Terrain Icon", defaultExpanded: false);
            AddRequiredField(card.ContentContainer, "ground.roadBasePrefab", "Road Base Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.sidewalkPrefab", "Sidewalk Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.roadLinePrefab", "Road Line Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.crosswalkLinePrefab", "Crosswalk Line Prefab", () => true);
        }

        private void BuildPlazaCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "plaza", "Plazas", "d_Prefab Icon", defaultExpanded: false);
            card.ContentContainer.Add(CreateField("plaza.centerpiecePrefab"));
            AddRequiredField(card.ContentContainer, "plaza.lawnPrefab", "Lawn Prefab (if any plaza block is selected)",
                () => FindProperty("general.plazaCells").arraySize > 0);
            card.ContentContainer.Add(CreateField("plaza.benchPrefab"));
        }

        private void BuildBuildingsCard(VisualElement parent)
        {
            buildingsCard = AddCard(parent, "buildingPrefabs", "Buildings", "d_BoxCollider Icon", defaultExpanded: false);
            var grid = new CityGeneratorPrefabGrid(RefreshDynamicUi);
            grid.Bind(FindProperty("buildingPrefabs"));
            buildingsCard.ContentContainer.Add(grid);
        }

        private void BuildVegetationCard(VisualElement parent)
        {
            vegetationCard = AddCard(parent, "vegetation", "Vegetation", "d_tree_icon", defaultExpanded: false);
            var grid = new CityGeneratorPrefabGrid(RefreshDynamicUi);
            grid.Bind(FindProperty("vegetation.prefabs"));
            vegetationCard.ContentContainer.Add(grid);
            vegetationCard.ContentContainer.Add(CreateField("vegetation.density"));
        }

        private void BuildVehiclesCard(VisualElement parent)
        {
            vehiclesCard = AddCard(parent, "vehicles", "Vehicles", "d_WheelCollider Icon", defaultExpanded: false);
            var list = new CityGeneratorWeightedPrefabList(RefreshDynamicUi);
            list.Bind(FindProperty("vehicles"));
            vehiclesCard.ContentContainer.Add(list);
        }

        private void BuildPedestriansCard(VisualElement parent)
        {
            pedestriansCard = AddCard(parent, "pedestrians", "Pedestrians", "d_Avatar Icon", defaultExpanded: false);
            var list = new CityGeneratorWeightedPrefabList(RefreshDynamicUi);
            list.Bind(FindProperty("pedestrians"));
            pedestriansCard.ContentContainer.Add(list);
        }

        private void BuildPropsCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "props", "Props", "d_Light Icon", defaultExpanded: false);
            AddRequiredField(card.ContentContainer, "props.trafficLightPrefab", "Traffic Light Prefab (if Include Traffic)",
                () => FindProperty("general.includeTraffic").boolValue);
            card.ContentContainer.Add(CreateField("props.lampPrefab"));
            card.ContentContainer.Add(CreateField("props.lampDensity"));
            card.ContentContainer.Add(CreateField("props.binPrefab"));
            card.ContentContainer.Add(CreateField("props.binDensity"));
        }

        private void BuildFooter()
        {
            validationPanel = rootVisualElement.Q<VisualElement>("cg-validation-panel");
            resultPanel = rootVisualElement.Q<VisualElement>("cg-result-panel");
            resultPanel.style.display = DisplayStyle.None;

            buildNewSceneButton = rootVisualElement.Q<Button>("cg-build-new-scene-button");
            buildNewSceneButton.clicked += BuildCityInNewScene;

            rebuildCurrentSceneButton = rootVisualElement.Q<Button>("cg-rebuild-current-scene-button");
            rebuildCurrentSceneButton.clicked += RebuildCityInCurrentScene;

            var resetButton = rootVisualElement.Q<Button>("cg-reset-defaults-button");
            resetButton.clicked += ResetToDefaults;
        }

        private CityGeneratorCard AddCard(VisualElement parent, string settingsSegment, string title, string iconName, bool defaultExpanded)
        {
            var card = new CityGeneratorCard(settingsSegment, title, iconName, defaultExpanded);
            parent.Add(card);
            cardsBySettingsSegment[settingsSegment] = card;
            return card;
        }

        private PropertyField CreateField(string relativePath, string label = null)
        {
            SerializedProperty property = FindProperty(relativePath);
            var field = new PropertyField(property, label);
            field.AddToClassList("cg-field-row");
            return field;
        }

        private VisualElement CreateIntSlider(SerializedProperty property, string label, int min, int max)
        {
            var slider = new SliderInt(label, min, max) { value = property.intValue, showInputField = true };
            slider.AddToClassList("cg-field-row");
            slider.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == property.intValue)
                    return;
                property.serializedObject.Update();
                property.intValue = evt.newValue;
                property.serializedObject.ApplyModifiedProperties();
            });
            return slider;
        }

        /// <summary>
        /// Draws <paramref name="relativePath"/>'s field with a "required" marker (see the
        /// <c>cg-required</c>/<c>cg-required--missing</c> USS classes) that only shows while
        /// <paramref name="isRequired"/> holds — e.g. the Lawn Prefab only while a plaza block is selected —
        /// mirroring the old IMGUI window's conditional red asterisk. Registered in
        /// <see cref="requiredRows"/> so <see cref="RefreshDynamicUi"/> can re-evaluate it live.
        /// </summary>
        private void AddRequiredField(VisualElement parent, string relativePath, string label, Func<bool> isRequired)
        {
            SerializedProperty property = FindProperty(relativePath);
            PropertyField field = CreateField(relativePath, label);
            parent.Add(field);
            requiredRows.Add(new RequiredRow(field, isRequired, () => property.objectReferenceValue == null));
        }

        private SerializedProperty FindProperty(string relativePath)
        {
            return serializedWindow.FindProperty("settings." + relativePath);
        }

        /// <summary>
        /// Re-derives everything that depends on the current settings values: card badges, the
        /// grid preview/summary, the three density HelpBoxes, required-field highlighting, and
        /// the live validation panel (which also enables/disables the Build button). Called after
        /// every settings change via <c>TrackSerializedObjectValue</c>, plus once right after
        /// building the UI.
        /// </summary>
        private void RefreshDynamicUi()
        {
            if (serializedWindow == null)
                return;

            int gridWidth = FindProperty("general.gridWidth").intValue;
            int gridHeight = FindProperty("general.gridHeight").intValue;
            int plazaCount = FindProperty("general.plazaCells").arraySize;
            int blockCount = gridWidth * gridHeight;
            int buildingsPerBlock = FindProperty("general.buildingsPerBlock").intValue;
            int vehicleCount = FindProperty("general.vehicleCount").intValue;
            int pedestrianCount = FindProperty("general.pedestrianCount").intValue;

            generalCard.SetBadge($"{gridWidth} x {gridHeight}");
            buildingsCard.SetBadge($"{FindProperty("buildingPrefabs").arraySize} prefabs");
            vegetationCard.SetBadge($"{FindProperty("vegetation.prefabs").arraySize} prefabs");
            vehiclesCard.SetBadge($"{FindProperty("vehicles").arraySize} entries");
            pedestriansCard.SetBadge($"{FindProperty("pedestrians").arraySize} entries");

            gridPreview.SetGrid(gridWidth, gridHeight);
            int estimatedBuildableBlocks = Mathf.Max(0, blockCount - Mathf.Min(plazaCount, blockCount));
            int estimatedBuildings = estimatedBuildableBlocks * buildingsPerBlock;
            float totalSize = gridWidth * CityGeneratorConstants.CellPitch;
            float totalSizeZ = gridHeight * CityGeneratorConstants.CellPitch;
            gridPreviewCaption.text = $"{blockCount} blocks ({plazaCount} plaza) · {totalSize:0}m x {totalSizeZ:0}m";
            summaryLine.text = $"~{estimatedBuildings} buildings · {vehicleCount} vehicles · {pedestrianCount} pedestrians";

            bool useCustomSeed = FindProperty("general.useCustomSeed").boolValue;
            seedField.style.display = useCustomSeed ? DisplayStyle.Flex : DisplayStyle.None;

            SetWarning(vehicleDensityWarning, GetVehicleDensityWarning());
            SetWarning(pedestrianDensityWarning, GetPedestrianDensityWarning());
            SetWarning(isolatedBlocksWarning, GetIsolatedBlocksWarning());

            foreach (RequiredRow row in requiredRows)
            {
                bool required = row.isRequired();
                bool missing = required && row.isEmpty();
                row.row.EnableInClassList("cg-required", required);
                row.row.EnableInClassList("cg-required--missing", missing);
            }

            RefreshValidation();
        }

        private static void SetWarning(HelpBox helpBox, string message)
        {
            helpBox.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (message != null)
                helpBox.text = message;
        }

        private void RefreshValidation()
        {
            CityGeneratorValidator.ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);

            foreach (CityGeneratorCard card in cardsBySettingsSegment.Values)
                card.SetHasError(false);

            validationPanel.Clear();
            foreach (CityGeneratorValidationIssue issue in issues)
            {
                var label = new Label(issue.message);
                label.AddToClassList("cg-validation-panel__item");
                validationPanel.Add(label);

                int dotIndex = issue.settingsPath.IndexOf('.');
                string segment = dotIndex >= 0 ? issue.settingsPath.Substring(0, dotIndex) : issue.settingsPath;
                if (cardsBySettingsSegment.TryGetValue(segment, out CityGeneratorCard card))
                    card.SetHasError(true);
            }

            bool valid = issues.Count == 0;
            buildNewSceneButton.SetEnabled(valid);
            rebuildCurrentSceneButton.SetEnabled(valid);
            string tooltip = valid ? string.Empty : $"{issues.Count} problem(s) to fix — see below.";
            buildNewSceneButton.tooltip = tooltip;
            rebuildCurrentSceneButton.tooltip = tooltip;
        }

        /// <summary>
        /// Non-blocking density warning: CarAgent has no route planning or congestion avoidance,
        /// so traffic gridlocks once vehicles fill too large a fraction of the grid's spawn nodes
        /// (see <see cref="CityGeneratorConstants.VehicleDensityWarningThreshold"/>). Returns null
        /// when there's nothing to warn about.
        /// </summary>
        private string GetVehicleDensityWarning()
        {
            int gridWidth = FindProperty("general.gridWidth").intValue;
            int gridHeight = FindProperty("general.gridHeight").intValue;
            int vehicleCount = FindProperty("general.vehicleCount").intValue;
            if (vehicleCount <= 0)
                return null;

            int validNodes = TrafficNetwork.EstimateValidSpawnNodeCount(gridWidth + 1, gridHeight + 1);
            float occupancy = (float)vehicleCount / validNodes;
            if (occupancy <= CityGeneratorConstants.VehicleDensityWarningThreshold)
                return null;

            int recommendedMax = Mathf.FloorToInt(validNodes * CityGeneratorConstants.VehicleDensityWarningThreshold);
            return $"{vehicleCount} vehicles is {occupancy:P0} of this grid's {validNodes} spawn points. " +
                   $"Traffic has no route planning, so it tends to gridlock above ~{CityGeneratorConstants.VehicleDensityWarningThreshold:P0} " +
                   $"(recommended max ~{recommendedMax} for a {gridWidth}x{gridHeight} grid).";
        }

        /// <summary>
        /// Non-blocking density warning, mirroring <see cref="GetVehicleDensityWarning"/>: pedestrians
        /// only spawn on Ring nodes (8 per block, none of the crossing/curb nodes), so that count —
        /// not the network's full node count — is the relevant denominator.
        /// </summary>
        private string GetPedestrianDensityWarning()
        {
            int gridWidth = FindProperty("general.gridWidth").intValue;
            int gridHeight = FindProperty("general.gridHeight").intValue;
            int pedestrianCount = FindProperty("general.pedestrianCount").intValue;
            if (pedestrianCount <= 0)
                return null;

            int ringNodeCount = 8 * gridWidth * gridHeight;
            float occupancy = (float)pedestrianCount / ringNodeCount;
            if (occupancy <= CityGeneratorConstants.PedestrianCountWarningThreshold)
                return null;

            int recommendedMax = Mathf.FloorToInt(ringNodeCount * CityGeneratorConstants.PedestrianCountWarningThreshold);
            return $"{pedestrianCount} pedestrians is {occupancy:P0} of this grid's {ringNodeCount} sidewalk spawn points. " +
                   $"Above ~{CityGeneratorConstants.PedestrianCountWarningThreshold:P0} the crowd starts reading as overcrowded " +
                   $"(recommended max ~{recommendedMax} for a {gridWidth}x{gridHeight} grid).";
        }

        /// <summary>
        /// A 1xN or Nx1 grid has no interior intersections, so it has no zebra crossings/traffic
        /// lights either: every block's pedestrian ring ends up isolated from every other one.
        /// </summary>
        private string GetIsolatedBlocksWarning()
        {
            if (!FindProperty("general.includePedestrians").boolValue)
                return null;

            int gridWidth = FindProperty("general.gridWidth").intValue;
            int gridHeight = FindProperty("general.gridHeight").intValue;
            if (gridWidth > 1 && gridHeight > 1)
                return null;

            return $"A {gridWidth}x{gridHeight} grid has no interior intersections, so it has no crossings: " +
                   "every block's pedestrians stay confined to their own sidewalk ring.";
        }

        private void ResetToDefaults()
        {
            settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            BuildUi();
        }

        private bool ValidateOrReport()
        {
            if (CityGeneratorValidator.Validate(settings, out List<string> errors))
                return true;

            foreach (string error in errors)
                Debug.LogError("[City Generator] " + error);

            EditorUtility.DisplayDialog(
                "City Generator - Validation Errors",
                $"Found {errors.Count} error(s):\n\n{string.Join("\n", errors)}\n\nSee the Console for details.",
                "OK");
            return false;
        }

        private void BuildCityInNewScene()
        {
            if (!ValidateOrReport())
                return;

            try
            {
                (string scenePath, CityBuildSummary summary) = GenerateCity(ReportProgress);
                ShowResult(scenePath, summary, success: true);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[City Generator] Generation failed: " + exception);
                ShowResult(null, default, success: false, exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void RebuildCityInCurrentScene()
        {
            if (!ValidateOrReport())
                return;

            bool confirmed = EditorUtility.DisplayDialog(
                "City Generator - Re-Build City",
                "This will delete the \"City\" object in the current scene and regenerate it with the current configuration. The light, volume, camera and player are left untouched.",
                "Confirm",
                "Cancel");
            if (!confirmed)
                return;

            try
            {
                CityBuildSummary summary = CityGeneratorSceneBuilder.RebuildInActiveScene(settings, ReportProgress);
                string scenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
                LogSummary(scenePath, summary);
                ShowResult(scenePath, summary, success: true);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[City Generator] Generation failed: " + exception);
                ShowResult(null, default, success: false, exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ReportProgress(string phase, float fraction)
        {
            EditorUtility.DisplayProgressBar("City Generator", phase, fraction);
        }

        private void ShowResult(string scenePath, CityBuildSummary summary, bool success, string errorMessage = null)
        {
            resultPanel.Clear();
            resultPanel.style.display = DisplayStyle.Flex;
            resultPanel.EnableInClassList("cg-result-panel--success", success);

            var title = new Label(success ? "City generated" : "Generation failed");
            title.AddToClassList("cg-result-panel__title");
            resultPanel.Add(title);

            if (!success)
            {
                var errorLabel = new Label(errorMessage + " (see the Console for details)");
                errorLabel.AddToClassList("cg-result-panel__stats");
                resultPanel.Add(errorLabel);
                return;
            }

            int propsTotal = summary.lampCount + summary.binCount;
            int vegetationTotal = summary.plazaSolidCount + summary.streetTreeCount;
            var stats = new Label(
                $"{scenePath}\n{summary.blockCount} blocks · {summary.buildingCount} buildings · " +
                $"{propsTotal} props · {vegetationTotal} vegetation · {summary.trafficLightCount} traffic lights · " +
                $"{summary.vehicleCount} vehicles · {summary.pedestrianCount} pedestrians");
            stats.AddToClassList("cg-result-panel__stats");
            resultPanel.Add(stats);

            if (!string.IsNullOrEmpty(scenePath))
            {
                var pingButton = new Button(() => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath)))
                    { text = "Ping Scene" };
                resultPanel.Add(pingButton);
            }
        }

        /// <summary>Validated settings -> generated, saved scene. No dialogs: kept separate from <see cref="BuildCityInNewScene"/> so it can be exercised directly (e.g. from tests) without a modal blocking the Editor.</summary>
        internal (string scenePath, CityBuildSummary summary) GenerateCity(Action<string, float> onProgress = null)
        {
            (string scenePath, CityBuildSummary summary) = CityGeneratorSceneBuilder.BuildAndSaveScene(settings, onProgress);
            LogSummary(scenePath, summary);
            return (scenePath, summary);
        }

        private static void LogSummary(string scenePath, CityBuildSummary summary)
        {
            int propsTotal = summary.lampCount + summary.binCount;
            int vegetationTotal = summary.plazaSolidCount + summary.streetTreeCount;

            Debug.Log(
                $"[City Generator] Built '{scenePath}': {summary.blockCount} blocks, {summary.buildingCount} buildings, " +
                $"{propsTotal} props (lamps {summary.lampCount}, bins {summary.binCount}), " +
                $"{vegetationTotal} vegetation instances, {summary.trafficLightCount} traffic lights, " +
                $"{summary.vehicleCount} vehicles, {summary.pedestrianCount} pedestrians.");
        }
    }
}
