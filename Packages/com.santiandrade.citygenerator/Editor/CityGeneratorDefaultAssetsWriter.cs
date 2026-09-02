using System;
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
            AppendVector2IntList(sb, "settings.general.plazaCells", settings.general.plazaCells);
            sb.AppendLine();

            AppendAssignment(sb, "settings.ground.roadBasePrefab", BuildGameObjectExpr(settings.ground.roadBasePrefab, warnings, "Ground > Road Base Prefab"));
            AppendAssignment(sb, "settings.ground.sidewalkPrefab", BuildGameObjectExpr(settings.ground.sidewalkPrefab, warnings, "Ground > Sidewalk Prefab"));
            AppendAssignment(sb, "settings.ground.roadLinePrefab", BuildGameObjectExpr(settings.ground.roadLinePrefab, warnings, "Ground > Road Line Prefab"));
            AppendAssignment(sb, "settings.ground.crosswalkLinePrefab", BuildGameObjectExpr(settings.ground.crosswalkLinePrefab, warnings, "Ground > Crosswalk Line Prefab"));
            AppendAssignment(sb, "settings.ground.emptyBlockPrefab", BuildGameObjectExpr(settings.ground.emptyBlockPrefab, warnings, "Ground > Empty Block Prefab"));
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

            AppendPedestriansList(sb, settings.pedestrians, warnings);
            sb.AppendLine();

            AppendCustomPlacesList(sb, settings.customPlaces, warnings);
            sb.AppendLine();

            AppendAssignment(sb, "settings.props.trafficLightPrefab", BuildGameObjectExpr(settings.props.trafficLightPrefab, warnings, "Props > Traffic Light Prefab"));
            AppendAssignment(sb, "settings.props.lampPrefab", BuildGameObjectExpr(settings.props.lampPrefab, warnings, "Props > Lamp Prefab"));
            AppendAssignment(sb, "settings.props.binPrefab", BuildGameObjectExpr(settings.props.binPrefab, warnings, "Props > Bin Prefab"));
            sb.AppendLine();

            AppendAmbienceClips(sb, settings.audio.ambience.clips, warnings);
            sb.AppendLine();

            AppendPlazaAudioClips(sb, settings.audio.plazaAudio.clips, warnings);

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);");
            sb.AppendLine("        private static AudioClip LoadAudioClip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);");
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

        private static void AppendVector2IntList(StringBuilder sb, string lhs, List<Vector2Int> items)
        {
            sb.AppendLine($"            {lhs} = new List<Vector2Int>");
            sb.AppendLine("            {");
            foreach (Vector2Int cell in items)
                sb.AppendLine($"                new Vector2Int({cell.x}, {cell.y}),");
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

        private static void AppendPedestriansList(StringBuilder sb, List<PedestrianEntry> pedestrians, List<string> warnings)
        {
            sb.AppendLine("            settings.pedestrians = new List<PedestrianEntry>");
            sb.AppendLine("            {");
            foreach (PedestrianEntry entry in pedestrians)
            {
                string expression = BuildGameObjectExpr(entry.prefab, warnings, "Pedestrians");
                if (expression == null)
                    continue;
                sb.AppendLine($"                new() {{ prefab = {expression}, percentage = {FormatFloat(entry.percentage)}f }},");
            }
            sb.AppendLine("            };");
        }

        private static void AppendCustomPlacesList(StringBuilder sb, List<CustomPlaceEntry> customPlaces, List<string> warnings)
        {
            sb.AppendLine("            settings.customPlaces = new List<CustomPlaceEntry>");
            sb.AppendLine("            {");
            foreach (CustomPlaceEntry entry in customPlaces)
            {
                string expression = BuildGameObjectExpr(entry.prefab, warnings, $"Custom Places > {entry.title}");
                if (expression == null)
                    continue;
                sb.AppendLine("                new()");
                sb.AppendLine("                {");
                sb.AppendLine($"                    title = \"{Escape(entry.title)}\",");
                sb.AppendLine($"                    prefab = {expression},");
                sb.AppendLine($"                    isPointOfInterest = {(entry.isPointOfInterest ? "true" : "false")},");
                sb.AppendLine($"                    occupiesFullBlock = {(entry.occupiesFullBlock ? "true" : "false")},");
                sb.AppendLine($"                    blockCell = new Vector2Int({entry.blockCell.x}, {entry.blockCell.y}),");
                sb.AppendLine($"                    cornerSlot = {entry.cornerSlot.ToString(CultureInfo.InvariantCulture)},");
                sb.AppendLine($"                    facing = CustomPlaceFacing.{entry.facing},");
                sb.AppendLine($"                    positionAssigned = {(entry.positionAssigned ? "true" : "false")},");
                sb.AppendLine("                },");
            }
            sb.AppendLine("            };");
        }

        private static void AppendAmbienceClips(StringBuilder sb, List<AmbienceClipEntry> clips, List<string> warnings)
        {
            sb.AppendLine("            settings.audio.ambience.clips = new List<AmbienceClipEntry>");
            sb.AppendLine("            {");
            foreach (AmbienceClipEntry entry in clips)
            {
                string expression = BuildAudioClipExpr(entry.clip, warnings, "Audio > Ambience");
                if (expression == null)
                    continue;
                sb.AppendLine($"                new() {{ clip = {expression}, volume = {FormatFloat(entry.volume)}f }},");
            }
            sb.AppendLine("            };");
        }

        private static void AppendPlazaAudioClips(StringBuilder sb, List<PlazaAudioClipEntry> clips, List<string> warnings)
        {
            sb.AppendLine("            settings.audio.plazaAudio.clips = new List<PlazaAudioClipEntry>");
            sb.AppendLine("            {");
            foreach (PlazaAudioClipEntry entry in clips)
            {
                string expression = BuildAudioClipExpr(entry.clip, warnings, "Audio > Plazas");
                if (expression == null)
                    continue;
                sb.AppendLine($"                new() {{ clip = {expression}, volume = {FormatFloat(entry.volume)}f, minDistance = {FormatFloat(entry.minDistance)}f, maxDistance = {FormatFloat(entry.maxDistance)}f }},");
            }
            sb.AppendLine("            };");
        }

        private static string BuildAudioClipExpr(AudioClip clip, List<string> warnings, string fieldLabel)
        {
            if (clip == null)
                return null;
            string relative = RelativeToRoot(clip);
            if (relative != null)
                return $"LoadAudioClip($\"{{DefaultAssetsRoot}}/{relative}\")";
            string fullPath = AssetDatabase.GetAssetPath(clip);
            warnings.Add($"{fieldLabel} ('{clip.name}') lives outside {DefaultAssetsRoot}/ (at '{fullPath}'). The generated default now hardcodes that path, which won't resolve in another project — move the asset into DefaultAssets/ and run \"Set Current Selection As Default\" again.");
            return $"LoadAudioClip(\"{Escape(fullPath)}\")";
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

        private static string RelativeToRoot(UnityEngine.Object asset)
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
            source = ReplaceField(source, "buildingsPerBlock", settings.general.buildingsPerBlock.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "playerEnabled", settings.general.playerEnabled ? "true" : "false");
            source = ReplaceField(source, "includeTraffic", settings.general.includeTraffic ? "true" : "false");
            source = ReplaceField(source, "vehicleCount", settings.general.vehicleCount.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "includePedestrians", settings.general.includePedestrians ? "true" : "false");
            source = ReplaceField(source, "pedestrianCount", settings.general.pedestrianCount.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "useCustomSeed", settings.general.useCustomSeed ? "true" : "false");
            source = ReplaceField(source, "seed", settings.general.seed.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "density", FormatFloat(settings.vegetation.density) + "f");
            source = ReplaceField(source, "lampDensity", FormatFloat(settings.props.lampDensity) + "f");
            source = ReplaceField(source, "binDensity", FormatFloat(settings.props.binDensity) + "f");

            PlayerSettings player = settings.player;
            source = ReplaceField(source, "walkSpeed", FormatFloat(player.walkSpeed) + "f");
            source = ReplaceField(source, "runSpeed", FormatFloat(player.runSpeed) + "f");
            source = ReplaceFieldInScope(source, "class PlayerSettings", "rotationSmoothTime", FormatFloat(player.rotationSmoothTime) + "f");
            source = ReplaceField(source, "gravity", FormatFloat(player.gravity) + "f");
            source = ReplaceField(source, "jumpHeight", FormatFloat(player.jumpHeight) + "f");
            source = ReplaceField(source, "controllerHeight", FormatFloat(player.controllerHeight) + "f");
            source = ReplaceField(source, "controllerRadius", FormatFloat(player.controllerRadius) + "f");
            source = ReplaceVector3Field(source, "controllerCenter", player.controllerCenter);
            source = ReplaceField(source, "controllerSlopeLimit", FormatFloat(player.controllerSlopeLimit) + "f");
            source = ReplaceField(source, "controllerStepOffset", FormatFloat(player.controllerStepOffset) + "f");
            source = ReplaceField(source, "controllerSkinWidth", FormatFloat(player.controllerSkinWidth) + "f");
            source = ReplaceField(source, "controllerMinMoveDistance", FormatFloat(player.controllerMinMoveDistance) + "f");
            source = ReplaceStringFieldInScope(source, "class PlayerSettings", "actionMapName", player.actionMapName);
            source = ReplaceStringFieldInScope(source, "class PlayerSettings", "moveActionName", player.moveActionName);
            source = ReplaceStringField(source, "jumpActionName", player.jumpActionName);
            source = ReplaceStringFieldInScope(source, "class PlayerSettings", "sprintActionName", player.sprintActionName);
            source = ReplaceStringFieldInScope(source, "class PlayerSettings", "lookActionName", player.lookActionName);
            source = ReplaceStringFieldInScope(source, "class PlayerSettings", "toggleActionName", player.toggleActionName);

            CameraSettings camera = settings.camera;
            source = ReplaceField(source, "fieldOfView", FormatFloat(camera.fieldOfView) + "f");
            source = ReplaceField(source, "verticalOffset", FormatFloat(camera.verticalOffset) + "f");
            source = ReplaceField(source, "horizontalOffset", FormatFloat(camera.horizontalOffset) + "f");
            source = ReplaceField(source, "distance", FormatFloat(camera.distance) + "f");
            source = ReplaceField(source, "minDistance", FormatFloat(camera.minDistance) + "f");
            source = ReplaceField(source, "sensitivity", FormatFloat(camera.sensitivity) + "f");
            source = ReplaceField(source, "minPitch", FormatFloat(camera.minPitch) + "f");
            source = ReplaceField(source, "maxPitch", FormatFloat(camera.maxPitch) + "f");
            source = ReplaceField(source, "followSmoothTime", FormatFloat(camera.followSmoothTime) + "f");
            source = ReplaceLayerMaskField(source, "collisionMask", camera.collisionMask);
            source = ReplaceField(source, "collisionRadius", FormatFloat(camera.collisionRadius) + "f");
            source = ReplaceField(source, "lockCursor", camera.lockCursor ? "true" : "false");

            FreeCameraSettings freeCamera = settings.freeCamera;
            source = ReplaceField(source, "enabled", freeCamera.enabled ? "true" : "false");
            source = ReplaceField(source, "moveSpeed", FormatFloat(freeCamera.moveSpeed) + "f");
            source = ReplaceField(source, "sprintMultiplier", FormatFloat(freeCamera.sprintMultiplier) + "f");
            source = ReplaceFieldInScope(source, "class FreeCameraSettings", "rotationSmoothTime", FormatFloat(freeCamera.rotationSmoothTime) + "f");
            source = ReplaceStringFieldInScope(source, "class FreeCameraSettings", "actionMapName", freeCamera.actionMapName);
            source = ReplaceStringFieldInScope(source, "class FreeCameraSettings", "moveActionName", freeCamera.moveActionName);
            source = ReplaceStringField(source, "verticalActionName", freeCamera.verticalActionName);
            source = ReplaceStringFieldInScope(source, "class FreeCameraSettings", "sprintActionName", freeCamera.sprintActionName);
            source = ReplaceStringFieldInScope(source, "class FreeCameraSettings", "lookActionName", freeCamera.lookActionName);
            source = ReplaceStringFieldInScope(source, "class FreeCameraSettings", "toggleActionName", freeCamera.toggleActionName);

            PedestrianBehaviourSettings behaviour = settings.pedestrianBehaviour;
            source = ReplaceField(source, "walkReferenceSpeed", FormatFloat(behaviour.walkReferenceSpeed) + "f");
            source = ReplaceField(source, "runReferenceSpeed", FormatFloat(behaviour.runReferenceSpeed) + "f");
            source = ReplaceField(source, "paceFraction", FormatFloat(behaviour.paceFraction) + "f");
            source = ReplaceField(source, "runnerChance", FormatFloat(behaviour.runnerChance) + "f");
            source = ReplaceField(source, "speedJitter", FormatFloat(behaviour.speedJitter) + "f");
            source = ReplaceField(source, "lateralJitter", FormatFloat(behaviour.lateralJitter) + "f");
            source = ReplaceField(source, "rotationSpeed", FormatFloat(behaviour.rotationSpeed) + "f");
            source = ReplaceField(source, "arriveRadius", FormatFloat(behaviour.arriveRadius) + "f");
            source = ReplaceField(source, "idleStopChance", FormatFloat(behaviour.idleStopChance) + "f");
            source = ReplaceField(source, "idleStopDurationMin", FormatFloat(behaviour.idleStopDurationMin) + "f");
            source = ReplaceField(source, "idleStopDurationMax", FormatFloat(behaviour.idleStopDurationMax) + "f");

            CrowdSettings crowd = settings.crowd;
            source = ReplaceField(source, "separationCellSize", FormatFloat(crowd.separationCellSize) + "f");
            source = ReplaceField(source, "separationRadius", FormatFloat(crowd.separationRadius) + "f");
            source = ReplaceField(source, "separationStrength", FormatFloat(crowd.separationStrength) + "f");
            source = ReplaceField(source, "playerAvoidanceRadius", FormatFloat(crowd.playerAvoidanceRadius) + "f");
            source = ReplaceField(source, "playerAvoidanceStrength", FormatFloat(crowd.playerAvoidanceStrength) + "f");
            source = ReplaceField(source, "staggerMinAgentCount", crowd.staggerMinAgentCount.ToString(CultureInfo.InvariantCulture));
            source = ReplaceField(source, "staggerDistance", FormatFloat(crowd.staggerDistance) + "f");
            source = ReplaceField(source, "staggerFrames", crowd.staggerFrames.ToString(CultureInfo.InvariantCulture));

            // minimap's scalars aren't inline field initializers (they come from
            // MinimapSettings.Default(), per the spec) so ReplaceField's pattern wouldn't match
            // them anyway; per the spec, ApplyTo's own MinimapSettings.Default() is already the
            // intended default and "Save Current As Default" isn't expected to override it.

            // DayNightSettings' scalars are likewise object-initializer assignments inside
            // Default(), not inline field initializers, so they need their own scoped replace
            // (scoped to that one method, since "enabled = ...," alone would also match
            // MinimapSettings.Default()'s unrelated "enabled" field).
            DayNightSettings dayNight = settings.dayNight;
            source = ReplaceInMethodBody(source, "DayNightSettings Default()", "enabled", dayNight.enabled ? "true" : "false");
            source = ReplaceInMethodBody(source, "DayNightSettings Default()", "startHour", FormatFloat(dayNight.startHour) + "f");
            source = ReplaceInMethodBody(source, "DayNightSettings Default()", "speedMultiplier", FormatFloat(dayNight.speedMultiplier) + "f");

            // ambience.clips/plazaAudio.clips themselves are rewritten wholesale in ApplyTo (see
            // BuildDefaultAssetsSource), same as every other asset list; only the on/off toggles
            // are struct Default() object-initializer assignments, so they need the same scoped
            // replace as DayNightSettings above.
            source = ReplaceInMethodBody(source, "AmbienceSettings Default()", "enabled", settings.audio.ambience.enabled ? "true" : "false");
            source = ReplaceInMethodBody(source, "PlazaAudioSettings Default()", "enabled", settings.audio.plazaAudio.enabled ? "true" : "false");

            return source;
        }

        // Every field name touched here is unique within CityGeneratorSettings.cs (verified by
        // inspection), so a global match-and-replace by name is safe without a full C# parser.
        private static string ReplaceField(string source, string fieldName, string newLiteral)
        {
            string pattern = $@"(public\s+(?:int|bool|float)\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
            return Regex.Replace(source, pattern, m => m.Groups[1].Value + newLiteral + ";");
        }

        private static string ReplaceStringField(string source, string fieldName, string newValue)
        {
            string pattern = $@"(public\s+string\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
            return Regex.Replace(source, pattern, m => m.Groups[1].Value + "\"" + Escape(newValue) + "\";");
        }

        // LayerMask has an implicit int conversion both ways, so a bare int literal compiles fine
        // as a field initializer (mirrors how the type is declared: "public LayerMask x = ~0;").
        private static string ReplaceLayerMaskField(string source, string fieldName, LayerMask newValue)
        {
            string pattern = $@"(public\s+LayerMask\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
            return Regex.Replace(source, pattern, m => m.Groups[1].Value + newValue.value + ";");
        }

        private static string ReplaceVector3Field(string source, string fieldName, Vector3 newValue)
        {
            string pattern = $@"(public\s+Vector3\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
            string literal = $"new({FormatFloat(newValue.x)}f, {FormatFloat(newValue.y)}f, {FormatFloat(newValue.z)}f)";
            return Regex.Replace(source, pattern, m => m.Groups[1].Value + literal + ";");
        }

        // Scoped variants of ReplaceField/ReplaceStringField, for a field name shared by more than
        // one class in this file (e.g. PlayerSettings and FreeCameraSettings both declare
        // actionMapName/moveActionName/sprintActionName/lookActionName/toggleActionName/
        // rotationSmoothTime) — an unscoped, file-wide Regex.Replace would silently overwrite the
        // other class's field with this one's value. Scoped to the class body via brace matching
        // (classSignature must be "class ClassName", found by IndexOf, then balanced from its
        // first '{').
        private static string ReplaceFieldInScope(string source, string classSignature, string fieldName, string newLiteral)
        {
            return ReplaceInClassScope(source, classSignature, body =>
            {
                string pattern = $@"(public\s+(?:int|bool|float)\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
                return Regex.Replace(body, pattern, m => m.Groups[1].Value + newLiteral + ";");
            });
        }

        private static string ReplaceStringFieldInScope(string source, string classSignature, string fieldName, string newValue)
        {
            return ReplaceInClassScope(source, classSignature, body =>
            {
                string pattern = $@"(public\s+string\s+{Regex.Escape(fieldName)}\s*=\s*)[^;]+;";
                return Regex.Replace(body, pattern, m => m.Groups[1].Value + "\"" + Escape(newValue) + "\";");
            });
        }

        private static string ReplaceInClassScope(string source, string classSignature, Func<string, string> transformBody)
        {
            int classStart = source.IndexOf(classSignature, StringComparison.Ordinal);
            if (classStart < 0)
                return source;
            int braceStart = source.IndexOf('{', classStart);
            if (braceStart < 0)
                return source;

            int depth = 0;
            int i = braceStart;
            for (; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            int classEnd = i;

            string body = source.Substring(classStart, classEnd - classStart);
            string newBody = transformBody(body);
            return source.Substring(0, classStart) + newBody + source.Substring(classEnd);
        }

        // Replaces an object-initializer assignment ("fieldName = ...,") but only within the one
        // method whose signature is methodSignature, so a field name shared with another struct's
        // Default() (e.g. "enabled") isn't touched outside its own method body. The body is taken
        // as the text up to the first "};" after the signature, which holds for every Default()
        // method in this file (none nest a "};" of their own).
        private static string ReplaceInMethodBody(string source, string methodSignature, string fieldName, string newLiteral)
        {
            int start = source.IndexOf(methodSignature, StringComparison.Ordinal);
            if (start < 0)
                return source;
            int end = source.IndexOf("};", start, StringComparison.Ordinal);
            if (end < 0)
                return source;
            end += "};".Length;

            string body = source.Substring(start, end - start);
            string pattern = $@"({Regex.Escape(fieldName)}\s*=\s*)[^,]+,";
            string newBody = Regex.Replace(body, pattern, m => m.Groups[1].Value + newLiteral + ",");
            return source.Substring(0, start) + newBody + source.Substring(end);
        }
    }
}
