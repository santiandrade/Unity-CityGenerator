using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// SPEC 24: a read-only summary of a <see cref="CityGeneratorSettingsAsset"/> plus a button to
    /// load it into the City Generator window. The settings are deliberately not editable here:
    /// they are edited in the window, which owns the validation and every custom control (grid
    /// previews, district/Custom Place pickers) a raw property list would bypass.
    /// </summary>
    [CustomEditor(typeof(CityGeneratorSettingsAsset))]
    public sealed class CityGeneratorSettingsAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (CityGeneratorSettingsAsset)target;
            CityGeneratorSettings settings = asset.settings ?? new CityGeneratorSettings();

            EditorGUILayout.HelpBox(
                "City Generator settings. Read-only here: open them in the City Generator window to edit, then Save from its Settings bar.",
                MessageType.Info);

            if (GUILayout.Button("Open in City Generator", GUILayout.Height(26f)))
                CityGeneratorWindow.OpenWithSettingsAsset(asset);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                foreach ((string label, string value) in BuildSummary(asset, settings))
                    EditorGUILayout.TextField(label, value);
            }

            if (asset.IsFromNewerPackageVersion())
            {
                EditorGUILayout.HelpBox(
                    $"Saved with City Generator {asset.packageVersion}, newer than the installed {CityGeneratorSettingsAsset.CurrentPackageVersion()}. Settings this version doesn't know about are ignored when loading.",
                    MessageType.Warning);
            }
        }

        /// <summary>The summary rows shown above, exposed for tests.</summary>
        internal static (string label, string value)[] BuildSummary(CityGeneratorSettingsAsset asset, CityGeneratorSettings settings)
        {
            GeneralSettings general = settings.general ?? new GeneralSettings();
            string grid = general.useCustomGrid
                ? $"Custom ({general.customBlockCells?.Count ?? 0} blocks)"
                : $"{general.gridWidth} x {general.gridHeight}";
            string seed = general.useCustomSeed ? general.seed.ToString() : "Random (Custom Seed off)";

            return new[]
            {
                ("Grid", grid),
                ("Seed", seed),
                ("Districts", (settings.districts?.Count ?? 0).ToString()),
                ("Custom Places", (settings.customPlaces?.Count ?? 0).ToString()),
                ("Building Prefabs", (settings.buildingPrefabs?.Count ?? 0).ToString()),
                ("Vehicle Prefabs", (settings.vehicles?.Count ?? 0).ToString()),
                ("Pedestrian Prefabs", (settings.pedestrians?.Count ?? 0).ToString()),
                ("Saved With Version", string.IsNullOrEmpty(asset.packageVersion) ? "Unknown" : asset.packageVersion),
            };
        }
    }
}
