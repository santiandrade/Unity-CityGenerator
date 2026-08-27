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

        private const string TabCity = "city";
        private const string TabPlayer = "player";
        private const string TabPedestrians = "pedestrians";
        private const string TabMinimap = "minimap";

        private const string BuildNewSceneButtonTooltip = "Generate a new city and save it as the next free Assets/Scenes/City<N>.unity, leaving any currently open scene untouched.";
        private const string RebuildCurrentSceneButtonTooltip = "Delete the \"City\" object in the current scene and regenerate it with these settings. Light, camera and player are left untouched.";

        // internal, not private: CityGeneratorSetDefaultsWindow (Assets/Editor/, outside the
        // package, in Assembly-CSharp-Editor) reads this to implement "Set Current Selection As
        // Default" — see Editor/AssemblyInfo.cs's InternalsVisibleTo.
        [SerializeField] internal CityGeneratorSettings settings = new();
        [SerializeField] private bool defaultsInitialized;

        private SerializedObject serializedWindow;

        // Populated by BuildUi; consulted by RefreshValidation to mark a card/field/tab as the
        // source of a validation issue, and to size badges/summaries live as the user edits.
        // Keyed by settings segment (e.g. "props") as the fallback resolution, plus an exact-path
        // override (e.g. "general.playerPrefab") for fields that were moved to a card whose own
        // segment differs from theirs (general.playerPrefab/inputActions now live in the Player card).
        private readonly Dictionary<string, CityGeneratorCard> cardsBySettingsSegment = new();
        private readonly Dictionary<string, CityGeneratorCard> cardsByExactPath = new();
        private readonly Dictionary<string, string> tabIdBySettingsSegment = new();
        private readonly Dictionary<string, string> tabIdByExactPath = new();
        private readonly List<RequiredRow> requiredRows = new();
        private CityGeneratorCard generalCard;
        private CityGeneratorCard buildingsCard;
        private CityGeneratorCard vegetationCard;
        private CityGeneratorCard vehiclesCard;
        private CityGeneratorCard pedestriansCard;
        private CityGeneratorCard playerCard;
        private CityGeneratorCard cameraCard;
        private CityGeneratorCard pedestrianBehaviourCard;
        private CityGeneratorCard crowdCard;
        private CityGeneratorCard customPlacesCard;
        private CityGeneratorCustomPlaceList customPlaceList;
        private CityGeneratorCard minimapCard;
        private HelpBox minimapResolutionWarning;
        private HelpBox minimapViewRadiusWarning;
        private CityGeneratorTabBar tabBar;
        private HelpBox referenceSpeedMismatchWarning;
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
            cardsByExactPath.Clear();
            tabIdBySettingsSegment.Clear();
            tabIdByExactPath.Clear();
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

            VisualElement tabsContainer = rootVisualElement.Q<VisualElement>("cg-tabs");
            VisualElement cityContainer = rootVisualElement.Q<VisualElement>("cg-cards-city");
            VisualElement playerContainer = rootVisualElement.Q<VisualElement>("cg-cards-player");
            VisualElement pedestriansContainer = rootVisualElement.Q<VisualElement>("cg-cards-pedestrians");
            VisualElement minimapContainer = rootVisualElement.Q<VisualElement>("cg-cards-minimap");

            tabBar = new CityGeneratorTabBar(tabsContainer);
            tabBar.AddTab(TabCity, "City", cityContainer);
            tabBar.AddTab(TabPlayer, "Player", playerContainer);
            tabBar.AddTab(TabPedestrians, "Pedestrians", pedestriansContainer);
            tabBar.AddTab(TabMinimap, "Minimap", minimapContainer);

            BuildGeneralCard(cityContainer);
            BuildGroundCard(cityContainer);
            BuildPlazaCard(cityContainer);
            BuildBuildingsCard(cityContainer);
            BuildVegetationCard(cityContainer);
            BuildVehiclesCard(cityContainer);
            BuildPropsCard(cityContainer);
            BuildCustomPlacesCard(cityContainer);

            BuildPlayerCard(playerContainer);
            BuildCameraCard(playerContainer);

            BuildPedestriansCard(pedestriansContainer);
            BuildPedestrianBehaviourCard(pedestriansContainer);
            BuildCrowdCard(pedestriansContainer);

            BuildMinimapCard(minimapContainer);

            BuildFooter();

            rootVisualElement.Bind(serializedWindow);
            rootVisualElement.TrackSerializedObjectValue(serializedWindow, _ => RefreshDynamicUi());
            tabBar.RestoreSelection(TabCity);
            RefreshDynamicUi();
        }

        /// <summary>
        /// The header is the thumbnail at a fixed height (set in USS) regardless of window width,
        /// so a wide window doesn't let the banner eat the space needed for the actual parameters.
        /// The image is centered and cropped to fill that height via USS background-size/-position.
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

            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CityGeneratorWindow).Assembly);
            banner.tooltip = packageInfo != null ? $"City Generator v{packageInfo.version}" : "City Generator";
        }

        private void BuildGeneralCard(VisualElement parent)
        {
            generalCard = AddCard(parent, "general", "General Options", "d_SceneAsset Icon", defaultExpanded: true, TabCity);
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

            content.Add(CreateField("general.useCustomSeed", "Custom Seed"));
            PropertyField seedField = CreateField("general.seed", "Seed");
            content.Add(seedField);
            // Visibility only, re-applied every RefreshDynamicUi pass (see below) rather than a
            // dedicated poll, since a settings change already triggers that refresh.
            this.seedField = seedField;
        }

        private void BuildGroundCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "ground", "Ground", "d_Terrain Icon", defaultExpanded: false, TabCity);
            AddRequiredField(card.ContentContainer, "ground.roadBasePrefab", "Road Base Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.sidewalkPrefab", "Sidewalk Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.roadLinePrefab", "Road Line Prefab", () => true);
            AddRequiredField(card.ContentContainer, "ground.crosswalkLinePrefab", "Crosswalk Line Prefab", () => true);
        }

        private void BuildPlazaCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "plaza", "Plazas", "d_Prefab Icon", defaultExpanded: false, TabCity);
            card.ContentContainer.Add(CreateField("plaza.centerpiecePrefab"));
            AddRequiredField(card.ContentContainer, "plaza.lawnPrefab", "Lawn Prefab (if any plaza block is selected)",
                () => FindProperty("general.plazaCells").arraySize > 0);
            card.ContentContainer.Add(CreateField("plaza.benchPrefab"));
        }

        private void BuildBuildingsCard(VisualElement parent)
        {
            buildingsCard = AddCard(parent, "buildingPrefabs", "Buildings", "d_BoxCollider Icon", defaultExpanded: false, TabCity);
            var grid = new CityGeneratorPrefabGrid(RefreshDynamicUi);
            grid.Bind(FindProperty("buildingPrefabs"));
            buildingsCard.ContentContainer.Add(grid);
        }

        private void BuildVegetationCard(VisualElement parent)
        {
            vegetationCard = AddCard(parent, "vegetation", "Vegetation", "d_tree_icon", defaultExpanded: false, TabCity);
            var grid = new CityGeneratorPrefabGrid(RefreshDynamicUi);
            grid.Bind(FindProperty("vegetation.prefabs"));
            vegetationCard.ContentContainer.Add(grid);
            vegetationCard.ContentContainer.Add(CreateField("vegetation.density"));
        }

        private void BuildVehiclesCard(VisualElement parent)
        {
            vehiclesCard = AddCard(parent, "vehicles", "Vehicles", "d_WheelCollider Icon", defaultExpanded: false, TabCity);
            var list = new CityGeneratorWeightedPrefabList(RefreshDynamicUi);
            list.Bind(FindProperty("vehicles"));
            vehiclesCard.ContentContainer.Add(list);
        }

        private void BuildPedestriansCard(VisualElement parent)
        {
            pedestriansCard = AddCard(parent, "pedestrians", "Pedestrians", "d_Avatar Icon", defaultExpanded: true, TabPedestrians);
            var list = new CityGeneratorWeightedPrefabList(RefreshDynamicUi);
            list.Bind(FindProperty("pedestrians"));
            pedestriansCard.ContentContainer.Add(list);
        }

        private void BuildPropsCard(VisualElement parent)
        {
            CityGeneratorCard card = AddCard(parent, "props", "Props", "d_Light Icon", defaultExpanded: false, TabCity);
            AddRequiredField(card.ContentContainer, "props.trafficLightPrefab", "Traffic Light Prefab (if Include Traffic)",
                () => FindProperty("general.includeTraffic").boolValue);
            card.ContentContainer.Add(CreateField("props.lampPrefab"));
            card.ContentContainer.Add(CreateField("props.lampDensity"));
            card.ContentContainer.Add(CreateField("props.binPrefab"));
            card.ContentContainer.Add(CreateField("props.binDensity"));
        }

        private void BuildPlayerCard(VisualElement parent)
        {
            playerCard = AddCard(parent, "player", "Player", "d_CharacterController Icon", defaultExpanded: true, TabPlayer);
            VisualElement content = playerCard.ContentContainer;

            content.Add(CreateField("general.playerPrefab", "Player Prefab"));
            AddRequiredField(content, "general.inputActions", "Input Actions (if Player Prefab is set)",
                () => FindProperty("general.playerPrefab").objectReferenceValue != null);
            RegisterCardPathAlias("general.playerPrefab", playerCard, TabPlayer);
            RegisterCardPathAlias("general.inputActions", playerCard, TabPlayer);

            content.Add(CreateField("player.walkSpeed"));
            content.Add(CreateField("player.runSpeed"));
            content.Add(CreateField("player.rotationSmoothTime"));
            content.Add(CreateField("player.gravity"));
            content.Add(CreateField("player.jumpHeight"));

            content.Add(CreateField("player.controllerHeight"));
            content.Add(CreateField("player.controllerRadius"));
            content.Add(CreateField("player.controllerCenter"));
            content.Add(CreateField("player.controllerSlopeLimit"));
            content.Add(CreateField("player.controllerStepOffset"));
            content.Add(CreateField("player.controllerSkinWidth"));
            content.Add(CreateField("player.controllerMinMoveDistance"));

            content.Add(CreateField("player.actionMapName"));
            content.Add(CreateField("player.moveActionName"));
            content.Add(CreateField("player.jumpActionName"));
            content.Add(CreateField("player.sprintActionName"));
            content.Add(CreateField("player.lookActionName"));
        }

        private void BuildCameraCard(VisualElement parent)
        {
            cameraCard = AddCard(parent, "camera", "Camera", "d_Camera Icon", defaultExpanded: false, TabPlayer);
            VisualElement content = cameraCard.ContentContainer;

            content.Add(CreateField("camera.fieldOfView"));
            content.Add(CreateField("camera.verticalOffset"));
            content.Add(CreateField("camera.horizontalOffset"));
            content.Add(CreateField("camera.distance"));
            content.Add(CreateField("camera.minDistance"));
            content.Add(CreateField("camera.sensitivity"));
            content.Add(CreateField("camera.minPitch"));
            content.Add(CreateField("camera.maxPitch"));
            content.Add(CreateField("camera.followSmoothTime"));
            content.Add(CreateField("camera.collisionMask"));
            content.Add(CreateField("camera.collisionRadius"));
            content.Add(CreateField("camera.lockCursor"));
        }

        private void BuildPedestrianBehaviourCard(VisualElement parent)
        {
            pedestrianBehaviourCard = AddCard(parent, "pedestrianBehaviour", "Behaviour", "d_AnimatorController Icon", defaultExpanded: false, TabPedestrians);
            VisualElement content = pedestrianBehaviourCard.ContentContainer;

            content.Add(CreateField("pedestrianBehaviour.walkReferenceSpeed"));
            content.Add(CreateField("pedestrianBehaviour.runReferenceSpeed"));
            referenceSpeedMismatchWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            referenceSpeedMismatchWarning.style.display = DisplayStyle.None;
            content.Add(referenceSpeedMismatchWarning);

            content.Add(CreateField("pedestrianBehaviour.paceFraction"));
            content.Add(CreateField("pedestrianBehaviour.runnerChance"));
            content.Add(CreateField("pedestrianBehaviour.speedJitter"));
            content.Add(CreateField("pedestrianBehaviour.lateralJitter"));
            content.Add(CreateField("pedestrianBehaviour.rotationSpeed"));
            content.Add(CreateField("pedestrianBehaviour.arriveRadius"));

            content.Add(CreateField("pedestrianBehaviour.idleStopChance"));
            content.Add(CreateField("pedestrianBehaviour.idleStopDurationMin"));
            content.Add(CreateField("pedestrianBehaviour.idleStopDurationMax"));
        }

        private void BuildCrowdCard(VisualElement parent)
        {
            crowdCard = AddCard(parent, "crowd", "Crowd", "d_NavMeshAgent Icon", defaultExpanded: false, TabPedestrians);
            VisualElement content = crowdCard.ContentContainer;

            content.Add(CreateField("crowd.separationCellSize"));
            content.Add(CreateField("crowd.separationRadius"));
            content.Add(CreateField("crowd.separationStrength"));
            content.Add(CreateField("crowd.playerAvoidanceRadius"));
            content.Add(CreateField("crowd.playerAvoidanceStrength"));
            content.Add(CreateField("crowd.staggerMinAgentCount"));
            content.Add(CreateField("crowd.staggerDistance"));
            content.Add(CreateField("crowd.staggerFrames"));
        }

        private void BuildCustomPlacesCard(VisualElement parent)
        {
            customPlacesCard = AddCard(parent, "customPlaces", "Custom Places", "d_Prefab On Icon", defaultExpanded: true, TabCity);
            customPlaceList = new CityGeneratorCustomPlaceList(RefreshDynamicUi);
            customPlaceList.Bind(FindProperty("customPlaces"));
            customPlacesCard.ContentContainer.Add(customPlaceList);
        }

        private void BuildMinimapCard(VisualElement parent)
        {
            minimapCard = AddCard(parent, "minimap", "Minimap", "d_GridLayoutGroup Icon", defaultExpanded: true, TabMinimap);
            VisualElement content = minimapCard.ContentContainer;

            content.Add(CreateField("minimap.enabled", "Enabled"));
            content.Add(CreateField("minimap.textureResolution", "Texture Resolution"));
            minimapResolutionWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            minimapResolutionWarning.style.display = DisplayStyle.None;
            content.Add(minimapResolutionWarning);

            content.Add(CreateField("minimap.viewRadiusMeters", "View Radius (m)"));
            minimapViewRadiusWarning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            minimapViewRadiusWarning.style.display = DisplayStyle.None;
            content.Add(minimapViewRadiusWarning);
        }

        private void BuildFooter()
        {
            validationPanel = rootVisualElement.Q<VisualElement>("cg-validation-panel");
            resultPanel = rootVisualElement.Q<VisualElement>("cg-result-panel");
            resultPanel.style.display = DisplayStyle.None;

            buildNewSceneButton = rootVisualElement.Q<Button>("cg-build-new-scene-button");
            buildNewSceneButton.tooltip = BuildNewSceneButtonTooltip;
            buildNewSceneButton.clicked += BuildCityInNewScene;

            rebuildCurrentSceneButton = rootVisualElement.Q<Button>("cg-rebuild-current-scene-button");
            rebuildCurrentSceneButton.tooltip = RebuildCurrentSceneButtonTooltip;
            rebuildCurrentSceneButton.clicked += RebuildCityInCurrentScene;

            var resetButton = rootVisualElement.Q<Button>("cg-reset-defaults-button");
            resetButton.tooltip = "Discard every change and restore the tool's shipped default settings.";
            resetButton.clicked += ResetToDefaults;
        }

        private CityGeneratorCard AddCard(VisualElement parent, string settingsSegment, string title, string iconName, bool defaultExpanded, string tabId)
        {
            var card = new CityGeneratorCard(settingsSegment, title, iconName, defaultExpanded);
            parent.Add(card);
            cardsBySettingsSegment[settingsSegment] = card;
            tabIdBySettingsSegment[settingsSegment] = tabId;
            return card;
        }

        /// <summary>
        /// Points an exact settings path at a card/tab other than the one its own top-level
        /// segment would resolve to — used for fields that were relocated to a different card
        /// (e.g. <c>general.playerPrefab</c>/<c>general.inputActions</c> now live in the Player
        /// card even though they're still serialized under <c>GeneralSettings</c>). Consulted by
        /// <see cref="RefreshValidation"/> before falling back to the segment-based lookup.
        /// </summary>
        private void RegisterCardPathAlias(string exactPath, CityGeneratorCard card, string tabId)
        {
            cardsByExactPath[exactPath] = card;
            tabIdByExactPath[exactPath] = tabId;
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
            var slider = new SliderInt(label, min, max) { value = property.intValue, showInputField = true, tooltip = property.tooltip };
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

            float walkSpeed = FindProperty("player.walkSpeed").floatValue;
            float runSpeed = FindProperty("player.runSpeed").floatValue;
            playerCard.SetBadge($"{walkSpeed:0.#} / {runSpeed:0.#} m/s");
            cameraCard.SetBadge($"FOV {FindProperty("camera.fieldOfView").floatValue:0}°");

            float paceFraction = FindProperty("pedestrianBehaviour.paceFraction").floatValue;
            pedestrianBehaviourCard.SetBadge($"{paceFraction:P0} pace");
            crowdCard.SetBadge($"{FindProperty("crowd.staggerMinAgentCount").intValue}+ staggered");

            gridPreview.SetGrid(gridWidth, gridHeight);
            customPlaceList.SetGrid(gridWidth, gridHeight);
            customPlacesCard.SetBadge($"{FindProperty("customPlaces").arraySize} entries");
            minimapCard.SetBadge(FindProperty("minimap.enabled").boolValue ? "Enabled" : "Disabled");
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
            SetWarning(referenceSpeedMismatchWarning, GetReferenceSpeedMismatchWarning());
            SetWarning(minimapResolutionWarning, GetMinimapResolutionWarning());
            SetWarning(minimapViewRadiusWarning, GetMinimapViewRadiusWarning());

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
            var tabsWithErrors = new HashSet<string>();

            validationPanel.Clear();
            foreach (CityGeneratorValidationIssue issue in issues)
            {
                var label = new Label(issue.message);
                label.AddToClassList("cg-validation-panel__item");
                label.AddToClassList(issue.isWarning ? "cg-validation-panel__item--warning" : "cg-validation-panel__item--error");
                validationPanel.Add(label);

                // Exact-path aliases (e.g. general.playerPrefab -> the Player card) take priority
                // over the segment-based lookup, so a relocated field still lights up the card and
                // tab it now visually lives in, not the one its settings segment would suggest.
                if (cardsByExactPath.TryGetValue(issue.settingsPath, out CityGeneratorCard aliasedCard))
                {
                    aliasedCard.SetHasError(true);
                }
                else
                {
                    int dotIndex = issue.settingsPath.IndexOf('.');
                    string segment = dotIndex >= 0 ? issue.settingsPath.Substring(0, dotIndex) : issue.settingsPath;
                    if (cardsBySettingsSegment.TryGetValue(segment, out CityGeneratorCard card))
                        card.SetHasError(true);
                }

                if (tabIdByExactPath.TryGetValue(issue.settingsPath, out string aliasedTabId))
                {
                    tabsWithErrors.Add(aliasedTabId);
                }
                else
                {
                    int dotIndex = issue.settingsPath.IndexOf('.');
                    string segment = dotIndex >= 0 ? issue.settingsPath.Substring(0, dotIndex) : issue.settingsPath;
                    if (tabIdBySettingsSegment.TryGetValue(segment, out string tabId))
                        tabsWithErrors.Add(tabId);
                }
            }

            tabBar.SetHasError(TabCity, tabsWithErrors.Contains(TabCity));
            tabBar.SetHasError(TabPlayer, tabsWithErrors.Contains(TabPlayer));
            tabBar.SetHasError(TabPedestrians, tabsWithErrors.Contains(TabPedestrians));
            tabBar.SetHasError(TabMinimap, tabsWithErrors.Contains(TabMinimap));

            int blockingCount = 0;
            foreach (CityGeneratorValidationIssue issue in issues)
            {
                if (!issue.isWarning)
                    blockingCount++;
            }
            bool valid = blockingCount == 0;
            buildNewSceneButton.SetEnabled(valid);
            rebuildCurrentSceneButton.SetEnabled(valid);
            string problemSuffix = valid ? string.Empty : $" Disabled: {blockingCount} problem(s) to fix — see below.";
            buildNewSceneButton.tooltip = BuildNewSceneButtonTooltip + problemSuffix;
            rebuildCurrentSceneButton.tooltip = RebuildCurrentSceneButtonTooltip + problemSuffix;
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

        /// <summary>
        /// Non-blocking warning: Behaviour > Walk/Run Reference Speed are calibration anchors for
        /// CharacterAnimator.controller's Locomotion blend tree, and must match Player > Walk/Run
        /// Speed (see CityGeneratorSettings.PedestrianBehaviourSettings) or pedestrians foot-slide.
        /// </summary>
        private string GetReferenceSpeedMismatchWarning()
        {
            float playerWalkSpeed = FindProperty("player.walkSpeed").floatValue;
            float playerRunSpeed = FindProperty("player.runSpeed").floatValue;
            float walkReferenceSpeed = FindProperty("pedestrianBehaviour.walkReferenceSpeed").floatValue;
            float runReferenceSpeed = FindProperty("pedestrianBehaviour.runReferenceSpeed").floatValue;

            if (Mathf.Approximately(playerWalkSpeed, walkReferenceSpeed) && Mathf.Approximately(playerRunSpeed, runReferenceSpeed))
                return null;

            return $"Walk/Run Reference Speed ({walkReferenceSpeed:0.##}/{runReferenceSpeed:0.##}) no longer match Player > Walk/Run Speed " +
                   $"({playerWalkSpeed:0.##}/{playerRunSpeed:0.##}). CharacterAnimator.controller's Locomotion blend tree is calibrated " +
                   "against these, so pedestrians will foot-slide until they're aligned again.";
        }

        /// <summary>Mirrors <see cref="CityGeneratorValidator"/>'s minimap texture resolution warning, inline next to the field instead of only in the bottom validation panel.</summary>
        private string GetMinimapResolutionWarning()
        {
            if (!FindProperty("minimap.enabled").boolValue)
                return null;

            int textureResolution = FindProperty("minimap.textureResolution").intValue;
            const int warningThreshold = 4096;
            if (textureResolution <= warningThreshold)
                return null;

            return $"Texture Resolution {textureResolution}px is above {warningThreshold}px — a large snapshot costs noticeable texture memory and disk space for the generated PNG asset.";
        }

        /// <summary>Mirrors <see cref="CityGeneratorValidator"/>'s minimap view radius warning, inline next to the field instead of only in the bottom validation panel.</summary>
        private string GetMinimapViewRadiusWarning()
        {
            if (!FindProperty("minimap.enabled").boolValue)
                return null;

            int gridWidth = FindProperty("general.gridWidth").intValue;
            int gridHeight = FindProperty("general.gridHeight").intValue;
            float viewRadiusMeters = FindProperty("minimap.viewRadiusMeters").floatValue;

            float width = gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float coveredHalfExtent = Mathf.Min(width, depth) / 2f;
            if (viewRadiusMeters <= coveredHalfExtent)
                return null;

            return $"View Radius ({viewRadiusMeters:0.#}m) is larger than the snapshot's covered world size (~{coveredHalfExtent:0.#}m half-extent for this {gridWidth}x{gridHeight} grid) — the HUD could never zoom out far enough to show it.";
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
                "This will regenerate the city in the current scene with the current configuration. The light, volume, camera and player are left untouched. If generation fails partway through, the existing city is left intact.",
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
                ShowResult(null, default, success: false, exception.Message + " The previous city has not been lost — it is still in the scene.");
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
                    { text = "Ping Scene", tooltip = "Highlight the generated scene asset in the Project window." };
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
