using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    internal class CityGeneratorWindow : EditorWindow
    {
        [SerializeField] private CityGeneratorSettings settings = new();

        private SerializedObject serializedWindow;
        private Vector2 scrollPosition;

        [MenuItem("Tools/City Generator")]
        private static void ShowWindow()
        {
            var window = GetWindow<CityGeneratorWindow>();
            window.titleContent = new GUIContent("City Generator");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            serializedWindow ??= new SerializedObject(this);
            serializedWindow.Update();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawGeneralSection();
            DrawGroundSection();
            DrawPlazaSection();
            DrawBuildingsSection();
            DrawVegetationSection();
            DrawVehiclesSection();
            DrawPropsSection();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(12f);
            if (GUILayout.Button("Build City", GUILayout.Height(32f)))
            {
                BuildCity();
            }

            serializedWindow.ApplyModifiedProperties();
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.LabelField("General Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("general.gridWidth"));
            EditorGUILayout.PropertyField(FindProperty("general.gridHeight"));
            EditorGUILayout.PropertyField(FindProperty("general.plazaCount"));
            EditorGUILayout.PropertyField(FindProperty("general.buildingsPerBlock"));
            EditorGUILayout.PropertyField(FindProperty("general.includeTraffic"));
            EditorGUILayout.PropertyField(FindProperty("general.vehicleCount"));
            EditorGUILayout.PropertyField(FindProperty("general.seed"));
            EditorGUILayout.PropertyField(FindProperty("general.playerPrefab"));
            EditorGUILayout.PropertyField(FindProperty("general.globalVolumeProfile"));
            EditorGUILayout.Space(8f);
        }

        private void DrawGroundSection()
        {
            EditorGUILayout.LabelField("Ground", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("ground.roadBasePrefab"));
            EditorGUILayout.PropertyField(FindProperty("ground.sidewalkPrefab"));
            EditorGUILayout.PropertyField(FindProperty("ground.roadLinePrefab"));
            EditorGUILayout.PropertyField(FindProperty("ground.crosswalkLinePrefab"));
            EditorGUILayout.Space(8f);
        }

        private void DrawPlazaSection()
        {
            EditorGUILayout.LabelField("Plazas", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("plaza.centerpiecePrefab"));
            EditorGUILayout.PropertyField(FindProperty("plaza.lawnPrefab"));
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
            EditorGUILayout.PropertyField(FindProperty("vegetation.prefabs"), includeChildren: true);
            EditorGUILayout.PropertyField(FindProperty("vegetation.density"));
            EditorGUILayout.Space(8f);
        }

        private void DrawVehiclesSection()
        {
            EditorGUILayout.LabelField("Vehicles", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("vehicles"), includeChildren: true);
            EditorGUILayout.Space(8f);
        }

        private void DrawPropsSection()
        {
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(FindProperty("props.trafficLightPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.lampPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.lampDensity"));
            EditorGUILayout.PropertyField(FindProperty("props.busStopPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.busStopDensity"));
            EditorGUILayout.PropertyField(FindProperty("props.binPrefab"));
            EditorGUILayout.PropertyField(FindProperty("props.binDensity"));
            EditorGUILayout.Space(8f);
        }

        private SerializedProperty FindProperty(string relativePath)
        {
            return serializedWindow.FindProperty("settings." + relativePath);
        }

        private void BuildCity()
        {
            if (!CityGeneratorValidator.Validate(settings, out List<string> errors))
            {
                foreach (string error in errors)
                    Debug.LogError("[City Generator] " + error);

                EditorUtility.DisplayDialog(
                    "City Generator - Validation Errors",
                    $"Found {errors.Count} error(s):\n\n{string.Join("\n", errors)}\n\nSee the Console for details.",
                    "OK");
                return;
            }

            Debug.Log("City Generator: validation passed. Generation is not implemented yet.");
        }
    }
}
