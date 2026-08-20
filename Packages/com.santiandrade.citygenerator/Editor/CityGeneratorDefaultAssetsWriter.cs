using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Backs "Tools &gt; City Generator &gt; Set Current Selection As Default": captures whatever
    /// is currently assigned in an open <see cref="CityGeneratorWindow"/> and writes it back as the
    /// tool's new default, so the next window (or "Reset to Defaults") opens with it. Kept in its
    /// own file, separate from <see cref="CityGeneratorDefaultAssets"/>, because it overwrites that
    /// file's contents wholesale — it must never need to regenerate itself.
    /// </summary>
    internal static class CityGeneratorDefaultAssetsWriter
    {
        private const string DefaultAssetsRoot = "Packages/com.santiandrade.citygenerator/DefaultAssets";
        private const string DefaultAssetsFilePath = "Packages/com.santiandrade.citygenerator/Editor/CityGeneratorDefaultAssets.cs";
        private const string SettingsFilePath = "Packages/com.santiandrade.citygenerator/Editor/CityGeneratorSettings.cs";

        /// <summary>
        /// Regenerates <see cref="CityGeneratorDefaultAssets"/>'s <c>ApplyTo</c> body (asset lists
        /// can change length between calls, which a targeted text edit can't express as cleanly as
        /// a full rewrite) and patches the scalar field initializers in <c>CityGeneratorSettings</c>
        /// in place. Both are plain <c>File.WriteAllText</c> calls against the package's own source
        /// under <c>Packages/</c>, followed by a compilation request so the change takes effect.
        /// </summary>
        public static void SaveCurrentAsDefault(CityGeneratorSettings settings)
        {
            var warnings = new List<string>();

            File.WriteAllText(ToAbsolutePath(DefaultAssetsFilePath), BuildDefaultAssetsSource(settings, warnings));

            string settingsSource = File.ReadAllText(ToAbsolutePath(SettingsFilePath));
            File.WriteAllText(ToAbsolutePath(SettingsFilePath), ReplaceScalarDefaults(settingsSource, settings));

            foreach (string warning in warnings)
                Debug.LogWarning("[City Generator] " + warning);

            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(assetPath);

        private static string BuildDefaultAssetsSource(CityGeneratorSettings settings, List<string> warnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.InputSystem;");
            sb.AppendLine();
            sb.AppendLine("namespace CityGenerator.Editor");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Fills a fresh <see cref=\"CityGeneratorSettings\"/> with the package's own demo prefabs,");
            sb.AppendLine("    /// so the tool opens ready for a quick first generation instead of a wall of empty required");
            sb.AppendLine("    /// fields. The demo content ships inside the package's Demo/ folder, so these paths resolve");
            sb.AppendLine("    /// in any project that installs the package, not just this one.");
            sb.AppendLine("    /// Every path is resolved defensively (silently left null if missing), since a project");
            sb.AppendLine("    /// without these assets must still get an otherwise-empty, working settings object.");
            sb.AppendLine("    /// Regenerated wholesale by <see cref=\"CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault\"/>");
            sb.AppendLine("    /// (\"Tools &gt; City Generator &gt; Set Current Selection As Default\") — hand edits survive");
            sb.AppendLine("    /// until the next time that command runs.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal static class CityGeneratorDefaultAssets");
            sb.AppendLine("    {");
            sb.AppendLine("        private const string DefaultAssetsRoot = \"Packages/com.santiandrade.citygenerator/DefaultAssets\";");
            sb.AppendLine();
            sb.AppendLine("        public static void ApplyTo(CityGeneratorSettings settings)");
            sb.AppendLine("        {");

            AppendAssignment(sb, "settings.general.playerPrefab", BuildGameObjectExpr(settings.general.playerPrefab, warnings, "General > Player Prefab"));
            AppendAssignment(sb, "settings.general.inputActions", BuildInputActionsExpr(settings.general.inputActions, warnings));
            sb.AppendLine();

            AppendAssignment(sb, "settings.ground.roadBasePrefab", BuildGameObjectExpr(settings.ground.roadBasePrefab, warnings, "Ground > Road Base Prefab"));
            AppendAssignment(sb, "settings.ground.sidewalkPrefab", BuildGameObjectExpr(settings.ground.sidewalkPrefab, warnings, "Ground > Sidewalk Prefab"));
            AppendAssignment(sb, "settings.ground.roadLinePrefab", BuildGameObjectExpr(settings.ground.roadLinePrefab, warnings, "Ground > Road Line Prefab"));
            AppendAssignment(sb, "settings.ground.crosswalkLinePrefab", BuildGameObjectExpr(settings.ground.crosswalkLinePrefab, warnings, "Ground > Crosswalk Line Prefab"));
            sb.AppendLine();

            AppendAssignment(sb, "settings.plaza.centerpiecePrefab", BuildGameObjectExpr(settings.plaza.centerpiecePrefab, warnings, "Plaza > Centerpiece Prefab"));
            AppendAssignment(sb, "settings.plaza.lawnPrefab", BuildGameObjectExpr(settings.plaza.lawnPrefab, warnings, "Plaza > Lawn Prefab"));
            AppendAssignment(sb, "settings.plaza.benchPrefab", BuildGameObjectExpr(settings.plaza.benchPrefab, warnings, "Plaza > Bench Prefab"));
            sb.AppendLine();

            AppendGameObjectList(sb, "settings.buildingPrefabs", settings.buildingPrefabs, warnings, "Buildings");
            sb.AppendLine();

            AppendGameObjectList(sb, "settings.vegetation.prefabs", settings.vegetation.prefabs, warnings, "Vegetation");
            sb.AppendLine();

            AppendVehiclesList(sb, settings.vehicles, warnings);
            sb.AppendLine();

            AppendAssignment(sb, "settings.props.trafficLightPrefab", BuildGameObjectExpr(settings.props.trafficLightPrefab, warnings, "Props > Traffic Light Prefab"));
            AppendAssignment(sb, "settings.props.lampPrefab", BuildGameObjectExpr(settings.props.lampPrefab, warnings, "Props > Lamp Prefab"));
            AppendAssignment(sb, "settings.props.binPrefab", BuildGameObjectExpr(settings.props.binPrefab, warnings, "Props > Bin Prefab"));

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendAssignment(StringBuilder sb, string lhs, string expression)
        {
            if (expression == null)
                return;
            sb.AppendLine($"            {lhs} = {expression};");
        }

        private static void AppendGameObjectList(StringBuilder sb, string lhs, List<GameObject> items, List<string> warnings, string fieldLabel)
        {
            sb.AppendLine($"            {lhs} = new List<GameObject>");
            sb.AppendLine("            {");
            foreach (GameObject item in items)
            {
                string expression = BuildGameObjectExpr(item, warnings, fieldLabel);
                if (expression != null)
                    sb.AppendLine($"                {expression},");
            }
            sb.AppendLine("            };");
        }

        private static void AppendVehiclesList(StringBuilder sb, List<VehicleEntry> vehicles, List<string> warnings)
        {
            sb.AppendLine("            settings.vehicles = new List<VehicleEntry>");
            sb.AppendLine("            {");
            foreach (VehicleEntry entry in vehicles)
            {
                string expression = BuildGameObjectExpr(entry.prefab, warnings, "Vehicles");
                if (expression == null)
                    continue;
                sb.AppendLine($"                new() {{ prefab = {expression}, percentage = {FormatFloat(entry.percentage)}f }},");
            }
            sb.AppendLine("            };");
        }

        // Prefabs assigned from outside DefaultAssetsRoot still get a working default (the tool
        // never blocks on it), but the resulting default only resolves in this project — flagged
        // via warnings rather than silently dropped, since a portability break here is easy to miss.
        private static string BuildGameObjectExpr(GameObject prefab, List<string> warnings, string fieldLabel)
        {
            if (prefab == null)
                return null;
            string relative = RelativeToRoot(prefab);
            if (relative != null)
                return $"Load($\"{{DefaultAssetsRoot}}/{relative}\")";
            string fullPath = AssetDatabase.GetAssetPath(prefab);
            warnings.Add($"{fieldLabel} ('{prefab.name}') lives outside {DefaultAssetsRoot}/ (at '{fullPath}'). The generated default now hardcodes that path, which won't resolve in another project — move the asset into DefaultAssets/ and run \"Set Current Selection As Default\" again.");
            return $"AssetDatabase.LoadAssetAtPath<GameObject>(\"{Escape(fullPath)}\")";
        }

        private static string BuildInputActionsExpr(InputActionAsset asset, List<string> warnings)
        {
            if (asset == null)
                return null;
            string relative = RelativeToRoot(asset);
            if (relative != null)
                return $"AssetDatabase.LoadAssetAtPath<InputActionAsset>($\"{{DefaultAssetsRoot}}/{relative}\")";
            string fullPath = AssetDatabase.GetAssetPath(asset);
            warnings.Add($"General > Input Actions ('{asset.name}') lives outside {DefaultAssetsRoot}/ (at '{fullPath}'). The generated default now hardcodes that path, which won't resolve in another project — move the asset into DefaultAssets/ and run \"Set Current Selection As Default\" again.");
            return $"AssetDatabase.LoadAssetAtPath<InputActionAsset>(\"{Escape(fullPath)}\")";
        }

        private static string RelativeToRoot(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            const string prefix = DefaultAssetsRoot + "/";
            return path.StartsWith(prefix) ? path.Substring(prefix.Length) : null;
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string FormatFloat(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string ReplaceScalarDefaults(string source, CityGeneratorSettings settings)
        {
            source = ReplaceField(source, "gridWidth", settings.general.gridWidth.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "gridHeight", settings.general.gridHeight.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "plazaCount", settings.general.plazaCount.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "buildingsPerBlock", settings.general.buildingsPerBlock.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "includeTraffic", settings.general.includeTraffic ? "true" : "false");
            source = ReplaceField(source, "vehicleCount", settings.general.vehicleCount.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "useCustomSeed", settings.general.useCustomSeed ? "true" : "false");
            source = ReplaceField(source, "seed", settings.general.seed.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "density", FormatFloat(settings.vegetation.density) + "f");
            source = ReplaceField(source, "lampDensity", FormatFloat(settings.props.lampDensity) + "f");
            source = ReplaceField(source, "binDensity", FormatFloat(settings.props.binDensity) + "f");
            return source;
        }

        // Every field name touched here is unique within CityGeneratorSettings.cs (verified by
        // inspection), so a global match-and-replace by name is safe without a full C# parser.
        private static string ReplaceField(string source, string fieldName, string newLiteral)
        {
            string pattern = $@"(public\s+(?:int|bool|float)\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
            return Regex.Replace(source, pattern, m => m.Groups[1].Value + newLiteral + ";");
        }
    }
}
