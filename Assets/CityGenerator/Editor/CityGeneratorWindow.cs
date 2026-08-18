using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    internal class CityGeneratorWindow : EditorWindow
    {
        private const int MinGridSize = 1;
        private const int MaxGridSize = 10;
        private const string ThumbnailPath = "Assets/CityGenerator/Editor/ToolThumbnail.png";

        [SerializeField] private CityGeneratorSettings settings = new();
        [SerializeField] private bool defaultsInitialized;

        private SerializedObject serializedWindow;
        private Vector2 scrollPosition;
        private Texture2D thumbnail;

        [MenuItem("Tools/City Generator")]
        private static void ShowWindow()
        {
            var window = GetWindow<CityGeneratorWindow>();
            window.titleContent = new GUIContent("City Generator");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
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

        private void OnGUI()
        {
            DrawThumbnail();

            serializedWindow ??= new SerializedObject(this);
            serializedWindow.Update();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // The default auto-computed label width does not grow with this window's long
            // labels (e.g. "Vehicles (if Vehicle Count > 0, percentages must sum to 100)"),
            // so it clips against the field control regardless of window width. Scale it with
            // the actual window width instead, within a sane range.
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.55f, 160f, 420f);

            DrawGeneralSection();
            DrawGroundSection();
            DrawPlazaSection();
            DrawBuildingsSection();
            DrawVegetationSection();
            DrawVehiclesSection();
            DrawPropsSection();

            EditorGUIUtility.labelWidth = previousLabelWidth;

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            DrawRequiredLegend();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Build City in New Scene", GUILayout.Height(32f)))
            {
                BuildCityInNewScene();
            }

            if (GUILayout.Button("Re-Build City in Current Scene", GUILayout.Height(32f)))
            {
                RebuildCityInCurrentScene();
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Reset to Defaults", GUILayout.Height(22f)))
            {
                ResetToDefaults();
                // The SerializedObject below still refers to the settings instance we just
                // replaced: applying it now would write this frame's (pre-reset) UI values
                // straight back onto the new object. Bail out and let the next OnGUI pass
                // build a fresh SerializedObject against the reset settings instead.
                return;
            }

            serializedWindow.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws <paramref name="property"/> with its normal label, plus a real (non-rich-text)
        /// red asterisk right after the label whenever <paramref name="isRequired"/> is true —
        /// callers whose "required" status depends on another field's value (e.g. the Lawn
        /// Prefab only when Plaza Count > 0) pass that condition in so the asterisk only shows
        /// while it actually applies. PropertyField's own label style does not render
        /// `&lt;color&gt;` tags, and a trailing GUILayout control after a flexible-width field
        /// gets pushed past the visible area on a narrow window (e.g. once the scrollbar eats
        /// into the width) — drawn as a Rect overlay inside the field's own reserved space
        /// instead, it always stays inside the label column regardless of window width.
        /// </summary>
        private static void DrawRequiredField(SerializedProperty property, string label, bool includeChildren = false, bool isRequired = true)
        {
            float height = EditorGUI.GetPropertyHeight(property, includeChildren);
            Rect rect = EditorGUILayout.GetControlRect(true, height);
            EditorGUI.PropertyField(rect, property, new GUIContent(label), includeChildren);
            if (isRequired)
                DrawRedAsterisk(new Rect(rect.x + EditorGUIUtility.labelWidth - 10f, rect.y, 10f, EditorGUIUtility.singleLineHeight));
        }

        private static void DrawRedAsterisk(Rect rect)
        {
            Color previousColor = GUI.color;
            GUI.color = Color.red;
            GUI.Label(rect, "*");
            GUI.color = previousColor;
        }

        private static void DrawRedAsterisk()
        {
            Color previousColor = GUI.color;
            GUI.color = Color.red;
            GUILayout.Label("*", GUILayout.Width(10f));
            GUI.color = previousColor;
        }

        private static void DrawRequiredLegend()
        {
            EditorGUILayout.BeginHorizontal();
            DrawRedAsterisk();
            EditorGUILayout.LabelField("Required field (some only when the related option is enabled)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawThumbnail()
        {
            if (thumbnail == null)
                thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(ThumbnailPath);
            if (thumbnail == null)
                return;

            float aspect = (float)thumbnail.width / thumbnail.height;
            float width = EditorGUIUtility.currentViewWidth;
            float height = width / aspect;

            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, thumbnail, ScaleMode.StretchToFill);
            EditorGUILayout.Space(10f);
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.LabelField("General Options", EditorStyles.boldLabel);
            DrawGridSizeSlider("general.gridWidth", "Grid Width");
            DrawGridSizeSlider("general.gridHeight", "Grid Height");
            EditorGUILayout.PropertyField(FindProperty("general.plazaCount"));
            EditorGUILayout.PropertyField(FindProperty("general.buildingsPerBlock"));
            EditorGUILayout.PropertyField(FindProperty("general.includeTraffic"));
            EditorGUILayout.PropertyField(FindProperty("general.vehicleCount"));
            EditorGUILayout.PropertyField(FindProperty("general.playerPrefab"));
            EditorGUILayout.PropertyField(FindProperty("general.useCustomSeed"), new GUIContent("Custom Seed"));
            if (FindProperty("general.useCustomSeed").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(FindProperty("general.seed"), new GUIContent("Seed"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawGroundSection()
        {
            EditorGUILayout.LabelField("Ground", EditorStyles.boldLabel);
            DrawRequiredField(FindProperty("ground.roadBasePrefab"), "Road Base Prefab");
            DrawRequiredField(FindProperty("ground.sidewalkPrefab"), "Sidewalk Prefab");
            DrawRequiredField(FindProperty("ground.roadLinePrefab"), "Road Line Prefab");
            DrawRequiredField(FindProperty("ground.crosswalkLinePrefab"), "Crosswalk Line Prefab");
            EditorGUILayout.Space(8f);
        }

        private void DrawPlazaSection()
        {
            EditorGUILayout.LabelField("Plazas", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("plaza.centerpiecePrefab"));
            DrawRequiredField(FindProperty("plaza.lawnPrefab"), "Lawn Prefab (if Plaza Count > 0)", isRequired: FindProperty("general.plazaCount").intValue > 0);
            EditorGUILayout.PropertyField(FindProperty("plaza.benchPrefab"));
            EditorGUILayout.Space(8f);
        }

        private void DrawBuildingsSection()
        {
            EditorGUILayout.LabelField("Buildings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("buildingPrefabs"), includeChildren: true);
            EditorGUILayout.Space(8f);
        }

        private void DrawVegetationSection()
        {
            EditorGUILayout.LabelField("Vegetation", EditorStyles.boldLabel);
            DrawRequiredField(FindProperty("vegetation.prefabs"), "Prefabs (if Density > 0)", includeChildren: true, isRequired: FindProperty("vegetation.density").floatValue > 0f);
            EditorGUILayout.PropertyField(FindProperty("vegetation.density"));
            EditorGUILayout.Space(8f);
        }

        private void DrawVehiclesSection()
        {
            EditorGUILayout.LabelField("Vehicles", EditorStyles.boldLabel);
            DrawRequiredField(FindProperty("vehicles"), "Vehicles (if Vehicle Count > 0, percentages must sum to 100)", includeChildren: true, isRequired: FindProperty("general.vehicleCount").intValue > 0);
            EditorGUILayout.Space(8f);
        }

        private void DrawPropsSection()
        {
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);
            DrawRequiredField(FindProperty("props.trafficLightPrefab"), "Traffic Light Prefab (if Include Traffic)", isRequired: FindProperty("general.includeTraffic").boolValue);
            EditorGUILayout.PropertyField(FindProperty("props.lampPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.busStopPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.busStopDensity"));
            EditorGUILayout.PropertyField(FindProperty("props.binPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.binDensity"));
            EditorGUILayout.Space(8f);
        }

        private void DrawGridSizeSlider(string relativePath, string label)
        {
            SerializedProperty property = FindProperty(relativePath);
            property.intValue = EditorGUILayout.IntSlider(label, property.intValue, MinGridSize, MaxGridSize);
        }

        private SerializedProperty FindProperty(string relativePath)
        {
            return serializedWindow.FindProperty("settings." + relativePath);
        }

        private void ResetToDefaults()
        {
            settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            serializedWindow = null;
            GUI.FocusControl(null);
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
                (string scenePath, CityBuildSummary _) = GenerateCity();
                EditorUtility.DisplayDialog("City Generator", $"City generated successfully at:\n{scenePath}", "OK");
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[City Generator] Generation failed: " + exception);
                EditorUtility.DisplayDialog("City Generator - Generation Failed", exception.Message + "\n\nSee the Console for details.", "OK");
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
                CityBuildSummary summary = CityGeneratorSceneBuilder.RebuildInActiveScene(settings);
                LogSummary(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path, summary);
                EditorUtility.DisplayDialog("City Generator", "City regenerated in the current scene.", "OK");
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[City Generator] Generation failed: " + exception);
                EditorUtility.DisplayDialog("City Generator - Generation Failed", exception.Message + "\n\nSee the Console for details.", "OK");
            }
        }

        /// <summary>Validated settings -> generated, saved scene. No dialogs: kept separate from <see cref="BuildCityInNewScene"/> so it can be exercised directly (e.g. from tests) without a modal blocking the Editor.</summary>
        internal (string scenePath, CityBuildSummary summary) GenerateCity()
        {
            (string scenePath, CityBuildSummary summary) = CityGeneratorSceneBuilder.BuildAndSaveScene(settings);
            LogSummary(scenePath, summary);
            return (scenePath, summary);
        }

        private static void LogSummary(string scenePath, CityBuildSummary summary)
        {
            int propsTotal = summary.lampCount + summary.busStopCount + summary.binCount;
            int vegetationTotal = summary.plazaSolidCount + summary.streetTreeCount;

            Debug.Log(
                $"[City Generator] Built '{scenePath}': {summary.blockCount} blocks, {summary.buildingCount} buildings, " +
                $"{propsTotal} props (lamps {summary.lampCount}, bus stops {summary.busStopCount}, bins {summary.binCount}), " +
                $"{vegetationTotal} vegetation instances, {summary.trafficLightCount} traffic lights, {summary.vehicleCount} vehicles.");
        }
    }
}
