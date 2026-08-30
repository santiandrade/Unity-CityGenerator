using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Editor
{
    /// <summary>One problem found by <see cref="CityGeneratorValidator.ValidateDetailed"/>, tied to the settings path that caused it.</summary>
    internal readonly struct CityGeneratorValidationIssue
    {
        /// <summary>Relative path within <see cref="CityGeneratorSettings"/> (e.g. "ground.roadBasePrefab"), matching the paths <c>CityGeneratorWindow.FindProperty</c> resolves. Used by the window to highlight the offending field/card.</summary>
        public readonly string settingsPath;
        public readonly string message;
        /// <summary>Non-blocking issues still highlight their card/tab and appear in the validation panel, but never disable the Build buttons.</summary>
        public readonly bool isWarning;

        public CityGeneratorValidationIssue(string settingsPath, string message, bool isWarning = false)
        {
            this.settingsPath = settingsPath;
            this.message = message;
            this.isWarning = isWarning;
        }
    }

    /// <summary>
    /// Validates a <see cref="CityGeneratorSettings"/> instance before generation starts.
    /// </summary>
    internal static class CityGeneratorValidator
    {
        private const float PercentageTolerance = 0.01f;

        /// <summary>Same checks as <see cref="Validate"/>, but each issue carries the settings path that caused it, so a caller (the window) can highlight the offending field live instead of only showing text after Build is pressed.</summary>
        public static bool ValidateDetailed(CityGeneratorSettings settings, out List<CityGeneratorValidationIssue> issues)
        {
            issues = new List<CityGeneratorValidationIssue>();

            if (settings.ground.roadBasePrefab == null)
                issues.Add(new CityGeneratorValidationIssue("ground.roadBasePrefab", "Ground: Road Base prefab is required."));
            if (settings.ground.sidewalkPrefab == null)
                issues.Add(new CityGeneratorValidationIssue("ground.sidewalkPrefab", "Ground: Sidewalk prefab is required."));
            if (settings.ground.roadLinePrefab == null)
                issues.Add(new CityGeneratorValidationIssue("ground.roadLinePrefab", "Ground: Road Line prefab is required."));
            if (settings.ground.crosswalkLinePrefab == null)
                issues.Add(new CityGeneratorValidationIssue("ground.crosswalkLinePrefab", "Ground: Crosswalk Line prefab is required."));

            if (settings.general.plazaCells.Count > 0 && settings.plaza.lawnPrefab == null)
                issues.Add(new CityGeneratorValidationIssue("plaza.lawnPrefab", "Plaza: Lawn prefab is required when at least one plaza cell is selected."));

            if (settings.general.playerEnabled && settings.general.playerPrefab == null)
                issues.Add(new CityGeneratorValidationIssue("general.playerPrefab", "Player: Player Prefab is required when Player is enabled."));

            if (settings.general.playerEnabled && settings.general.inputActions == null)
                issues.Add(new CityGeneratorValidationIssue("general.inputActions", "Player: Input Actions asset is required when Player is enabled (otherwise the generated camera silently gets no input)."));

            for (int i = 0; i < settings.buildingPrefabs.Count; i++)
            {
                GameObject buildingPrefab = settings.buildingPrefabs[i];
                if (buildingPrefab == null)
                    issues.Add(new CityGeneratorValidationIssue("buildingPrefabs", $"Buildings: entry {i + 1} is empty and will be skipped.", isWarning: true));
                else if (buildingPrefab.GetComponentInChildren<Renderer>() == null)
                    issues.Add(new CityGeneratorValidationIssue("buildingPrefabs", $"Buildings: entry {i + 1} ('{buildingPrefab.name}') has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
            }

            if (settings.props.trafficLightPrefab != null && settings.props.trafficLightPrefab.GetComponentInChildren<Renderer>() == null)
                issues.Add(new CityGeneratorValidationIssue("props.trafficLightPrefab", "Props: Traffic Light prefab has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
            if (settings.props.lampPrefab != null && settings.props.lampPrefab.GetComponentInChildren<Renderer>() == null)
                issues.Add(new CityGeneratorValidationIssue("props.lampPrefab", "Props: Lamp prefab has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
            if (settings.props.binPrefab != null && settings.props.binPrefab.GetComponentInChildren<Renderer>() == null)
                issues.Add(new CityGeneratorValidationIssue("props.binPrefab", "Props: Bin prefab has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
            for (int i = 0; i < settings.vegetation.prefabs.Count; i++)
            {
                if (settings.vegetation.prefabs[i] == null)
                    issues.Add(new CityGeneratorValidationIssue("vegetation.prefabs", $"Vegetation: entry {i + 1} is empty and will be skipped.", isWarning: true));
            }

            if (settings.general.inputActions != null)
                ValidateInputActions(settings, issues);

            if (settings.general.gridWidth > 1 && settings.general.gridHeight > 1)
            {
                if (settings.props.trafficLightPrefab == null)
                {
                    issues.Add(new CityGeneratorValidationIssue("props.trafficLightPrefab", "Props: Traffic Light prefab is required when the grid has at least one interior intersection (Grid Width and Grid Height both greater than 1)."));
                }
                else if (settings.props.trafficLightPrefab.GetComponent<Runtime.TrafficLight>() == null)
                {
                    issues.Add(new CityGeneratorValidationIssue("props.trafficLightPrefab", "Props: Traffic Light prefab must have a TrafficLight component."));
                }
            }

            if (settings.vegetation.density > 0f && settings.vegetation.prefabs.Count == 0)
                issues.Add(new CityGeneratorValidationIssue("vegetation.prefabs", "Vegetation: at least one prefab is required when Density > 0."));

            if (settings.general.includeTraffic && settings.general.vehicleCount > 0)
            {
                if (settings.vehicles.Count == 0)
                {
                    issues.Add(new CityGeneratorValidationIssue("vehicles", "Vehicles: at least one vehicle entry is required when Vehicle Count > 0."));
                }
                else
                {
                    float percentageSum = 0f;
                    for (int i = 0; i < settings.vehicles.Count; i++)
                    {
                        VehicleEntry entry = settings.vehicles[i];
                        if (entry.prefab == null)
                            issues.Add(new CityGeneratorValidationIssue("vehicles", $"Vehicles: entry {i + 1} is missing its prefab."));
                        else if (entry.prefab.GetComponentInChildren<Renderer>() == null)
                            issues.Add(new CityGeneratorValidationIssue("vehicles", $"Vehicles: entry {i + 1} ('{entry.prefab.name}') has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
                        percentageSum += entry.percentage;
                    }

                    if (Mathf.Abs(percentageSum - 100f) > PercentageTolerance)
                        issues.Add(new CityGeneratorValidationIssue("vehicles", $"Vehicles: percentages must sum to 100 (currently {percentageSum:0.##})."));
                }
            }

            if (settings.general.includePedestrians && settings.general.pedestrianCount > 0)
            {
                if (settings.pedestrians.Count == 0)
                {
                    issues.Add(new CityGeneratorValidationIssue("pedestrians", "Pedestrians: at least one pedestrian entry is required when Pedestrian Count > 0."));
                }
                else
                {
                    float percentageSum = 0f;
                    for (int i = 0; i < settings.pedestrians.Count; i++)
                    {
                        PedestrianEntry entry = settings.pedestrians[i];
                        if (entry.prefab == null)
                            issues.Add(new CityGeneratorValidationIssue("pedestrians", $"Pedestrians: entry {i + 1} is missing its prefab."));
                        else if (entry.prefab.GetComponentInChildren<Renderer>() == null)
                            issues.Add(new CityGeneratorValidationIssue("pedestrians", $"Pedestrians: entry {i + 1} ('{entry.prefab.name}') has no Renderer in its hierarchy, so its footprint falls back to a fake 0.5m size.", isWarning: true));
                        percentageSum += entry.percentage;
                    }

                    if (Mathf.Abs(percentageSum - 100f) > PercentageTolerance)
                        issues.Add(new CityGeneratorValidationIssue("pedestrians", $"Pedestrians: percentages must sum to 100 (currently {percentageSum:0.##})."));
                }
            }

            if (settings.player.walkSpeed > settings.player.runSpeed)
                issues.Add(new CityGeneratorValidationIssue("player.runSpeed", "Player: Run Speed must be greater than or equal to Walk Speed."));
            if (settings.player.walkSpeed <= 0f || settings.player.runSpeed <= 0f)
                issues.Add(new CityGeneratorValidationIssue("player.walkSpeed", "Player: Walk Speed and Run Speed must both be greater than zero (a zero speed divides by zero in the animation blend tree)."));
            else if (Mathf.Approximately(settings.player.walkSpeed, settings.player.runSpeed))
                issues.Add(new CityGeneratorValidationIssue("player.walkSpeed", "Player: Walk Speed and Run Speed must be different (equal values collapse the animation blend tree's walk/run range)."));

            if (settings.pedestrianBehaviour.walkReferenceSpeed <= 0f || settings.pedestrianBehaviour.runReferenceSpeed <= 0f)
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.walkReferenceSpeed", "Pedestrians: Walk Reference Speed and Run Reference Speed must both be greater than zero (a zero speed divides by zero in the animation blend tree)."));
            else if (Mathf.Approximately(settings.pedestrianBehaviour.walkReferenceSpeed, settings.pedestrianBehaviour.runReferenceSpeed))
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.walkReferenceSpeed", "Pedestrians: Walk Reference Speed and Run Reference Speed must be different (equal values collapse the animation blend tree's walk/run range)."));

            if (settings.player.controllerHeight <= 0f)
                issues.Add(new CityGeneratorValidationIssue("player.controllerHeight", "Player: Controller Height must be greater than zero."));
            if (settings.player.controllerRadius <= 0f)
                issues.Add(new CityGeneratorValidationIssue("player.controllerRadius", "Player: Controller Radius must be greater than zero."));
            if (settings.player.controllerStepOffset < 0f)
                issues.Add(new CityGeneratorValidationIssue("player.controllerStepOffset", "Player: Controller Step Offset must not be negative."));
            if (settings.player.controllerSkinWidth < 0f)
                issues.Add(new CityGeneratorValidationIssue("player.controllerSkinWidth", "Player: Controller Skin Width must not be negative."));
            if (settings.player.controllerStepOffset >= settings.player.controllerHeight)
                issues.Add(new CityGeneratorValidationIssue("player.controllerStepOffset", "Player: Controller Step Offset must be smaller than Controller Height."));
            if (settings.player.controllerSkinWidth >= settings.player.controllerRadius)
                issues.Add(new CityGeneratorValidationIssue("player.controllerSkinWidth", "Player: Controller Skin Width must be smaller than Controller Radius."));

            if (settings.camera.minPitch >= settings.camera.maxPitch)
                issues.Add(new CityGeneratorValidationIssue("camera.maxPitch", "Camera: Max Pitch must be greater than Min Pitch."));
            if (settings.camera.minDistance > settings.camera.distance)
                issues.Add(new CityGeneratorValidationIssue("camera.distance", "Camera: Distance must be greater than or equal to Min Distance."));

            if (settings.pedestrianBehaviour.idleStopDurationMin > settings.pedestrianBehaviour.idleStopDurationMax)
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.idleStopDurationMax", "Pedestrians: Idle Stop Duration Max must be greater than or equal to Idle Stop Duration Min."));

            if (settings.pedestrianBehaviour.arriveRadius < 0f)
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.arriveRadius", "Pedestrians: Arrive Radius must not be negative."));
            if (settings.pedestrianBehaviour.idleStopDurationMin < 0f)
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.idleStopDurationMin", "Pedestrians: Idle Stop Duration Min must not be negative."));
            if (settings.pedestrianBehaviour.idleStopDurationMax < 0f)
                issues.Add(new CityGeneratorValidationIssue("pedestrianBehaviour.idleStopDurationMax", "Pedestrians: Idle Stop Duration Max must not be negative."));

            if (settings.crowd.separationCellSize < 0f)
                issues.Add(new CityGeneratorValidationIssue("crowd.separationCellSize", "Crowd: Separation Cell Size must not be negative."));
            if (settings.crowd.separationRadius < 0f)
                issues.Add(new CityGeneratorValidationIssue("crowd.separationRadius", "Crowd: Separation Radius must not be negative."));
            if (settings.crowd.playerAvoidanceRadius < 0f)
                issues.Add(new CityGeneratorValidationIssue("crowd.playerAvoidanceRadius", "Crowd: Player Avoidance Radius must not be negative."));
            if (settings.crowd.staggerDistance < 0f)
                issues.Add(new CityGeneratorValidationIssue("crowd.staggerDistance", "Crowd: Stagger Distance must not be negative."));

            ValidateCustomPlaces(settings, issues);
            ValidateMinimap(settings, issues);
            ValidateAudio(settings, issues);

            return !issues.Exists(issue => !issue.isWarning);
        }

        /// <summary>Blocking: an enabled Ambience/Plazas card with no entries, or an entry missing its clip, would silently play nothing — same "required field" treatment as every other prefab list in the tool.</summary>
        private static void ValidateAudio(CityGeneratorSettings settings, List<CityGeneratorValidationIssue> issues)
        {
            if (settings.audio.ambience.enabled)
            {
                if (settings.audio.ambience.clips.Count == 0)
                    issues.Add(new CityGeneratorValidationIssue("audio.ambience.clips", "Audio: Ambience is enabled but has no clip entries."));
                for (int i = 0; i < settings.audio.ambience.clips.Count; i++)
                {
                    if (settings.audio.ambience.clips[i].clip == null)
                        issues.Add(new CityGeneratorValidationIssue("audio.ambience.clips", $"Audio: Ambience entry {i + 1} is missing its clip."));
                }
            }

            if (settings.audio.plazaAudio.enabled)
            {
                if (settings.audio.plazaAudio.clips.Count == 0)
                    issues.Add(new CityGeneratorValidationIssue("audio.plazaAudio.clips", "Audio: Plazas is enabled but has no clip entries."));
                for (int i = 0; i < settings.audio.plazaAudio.clips.Count; i++)
                {
                    if (settings.audio.plazaAudio.clips[i].clip == null)
                        issues.Add(new CityGeneratorValidationIssue("audio.plazaAudio.clips", $"Audio: Plazas entry {i + 1} is missing its clip."));
                }
            }
        }

        // Both non-blocking: neither condition breaks generation, they just warn about a
        // consequence the user might not expect (memory/disk cost, or a zoom level that could
        // never show anything beyond the snapshot's own edge).
        private const int MinimapTextureResolutionWarningThreshold = 4096;

        private static void ValidateMinimap(CityGeneratorSettings settings, List<CityGeneratorValidationIssue> issues)
        {
            if (!settings.minimap.enabled)
                return;

            if (settings.minimap.textureResolution > MinimapTextureResolutionWarningThreshold)
                issues.Add(new CityGeneratorValidationIssue("minimap.textureResolution",
                    $"Minimap: Texture Resolution {settings.minimap.textureResolution}px is above {MinimapTextureResolutionWarningThreshold}px — a large snapshot costs noticeable texture memory and disk space for the generated PNG asset.",
                    isWarning: true));

            float width = settings.general.gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = settings.general.gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float coveredHalfExtent = Mathf.Min(width, depth) / 2f;
            if (settings.minimap.viewRadiusMeters > coveredHalfExtent)
                issues.Add(new CityGeneratorValidationIssue("minimap.viewRadiusMeters",
                    $"Minimap: View Radius ({settings.minimap.viewRadiusMeters:0.#}m) is larger than the snapshot's covered world size (~{coveredHalfExtent:0.#}m half-extent for this {settings.general.gridWidth}x{settings.general.gridHeight} grid) — the HUD could never zoom out far enough to show it.",
                    isWarning: true));
        }

        /// <summary>
        /// Per-entry blocking checks for Custom Places: title, prefab, an assigned position that
        /// resolves to a real, non-plaza block, no two entries claiming the same slot (a
        /// whole-block entry conflicts with any entry in the same block; two corner entries
        /// conflict only when they share the same corner), and no two entries sharing the same
        /// title.
        /// </summary>
        private static void ValidateCustomPlaces(CityGeneratorSettings settings, List<CityGeneratorValidationIssue> issues)
        {
            List<CustomPlaceEntry> customPlaces = settings.customPlaces;
            var plazaLookup = new HashSet<Vector2Int>(settings.general.plazaCells);

            for (int i = 0; i < customPlaces.Count; i++)
            {
                CustomPlaceEntry entry = customPlaces[i];
                string label = string.IsNullOrEmpty(entry.title) ? $"entry {i + 1}" : $"'{entry.title}'";

                if (string.IsNullOrEmpty(entry.title))
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: entry {i + 1} needs a title."));

                if (entry.prefab == null)
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: {label} is missing its prefab."));

                if (!entry.positionAssigned)
                {
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: {label} has no position assigned yet — click a block (and a quadrant, if not occupying the full block) in its grid preview."));
                    continue;
                }

                bool inGrid = entry.blockCell.x >= 0 && entry.blockCell.x < settings.general.gridWidth
                    && entry.blockCell.y >= 0 && entry.blockCell.y < settings.general.gridHeight;
                if (!inGrid)
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: {label} points at block ({entry.blockCell.x}, {entry.blockCell.y}), outside the {settings.general.gridWidth}x{settings.general.gridHeight} grid."));
                else if (plazaLookup.Contains(entry.blockCell))
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: {label} points at block ({entry.blockCell.x}, {entry.blockCell.y}), which is a plaza block."));
            }

            for (int i = 0; i < customPlaces.Count; i++)
            {
                CustomPlaceEntry a = customPlaces[i];
                if (!a.positionAssigned)
                    continue;

                for (int j = i + 1; j < customPlaces.Count; j++)
                {
                    CustomPlaceEntry b = customPlaces[j];
                    if (!b.positionAssigned || a.blockCell != b.blockCell)
                        continue;

                    bool conflicts = a.occupiesFullBlock || b.occupiesFullBlock || a.cornerSlot == b.cornerSlot;
                    if (!conflicts)
                        continue;

                    string labelA = string.IsNullOrEmpty(a.title) ? $"entry {i + 1}" : $"'{a.title}'";
                    string labelB = string.IsNullOrEmpty(b.title) ? $"entry {j + 1}" : $"'{b.title}'";
                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: {labelA} and {labelB} both claim the same slot in block ({a.blockCell.x}, {a.blockCell.y})."));
                }
            }

            for (int i = 0; i < customPlaces.Count; i++)
            {
                CustomPlaceEntry a = customPlaces[i];
                if (string.IsNullOrEmpty(a.title))
                    continue;

                for (int j = i + 1; j < customPlaces.Count; j++)
                {
                    CustomPlaceEntry b = customPlaces[j];
                    if (string.IsNullOrEmpty(b.title) || !string.Equals(a.title.Trim(), b.title.Trim(), System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    issues.Add(new CityGeneratorValidationIssue("customPlaces", $"Custom Places: entries {i + 1} and {j + 1} both use the title '{a.title}' — titles must be unique."));
                }
            }
        }

        /// <summary>Item 10 gap 5: confirms the Move/Sprint/Jump/Look action names configured under Player > Input Actions actually exist in the assigned asset's action map, with the expected control type — a typo here otherwise fails silently at runtime (the action is just never found).</summary>
        private static void ValidateInputActions(CityGeneratorSettings settings, List<CityGeneratorValidationIssue> issues)
        {
            InputActionAsset asset = settings.general.inputActions;
            InputActionMap map = asset.FindActionMap(settings.player.actionMapName);
            if (map == null)
            {
                issues.Add(new CityGeneratorValidationIssue("general.inputActions", $"General: Input Actions asset has no '{settings.player.actionMapName}' action map."));
                return;
            }

            ValidateInputAction(map, settings.player.moveActionName, InputActionType.Value, "player.moveActionName", "Move", issues);
            ValidateInputAction(map, settings.player.sprintActionName, InputActionType.Button, "player.sprintActionName", "Sprint", issues);
            ValidateInputAction(map, settings.player.jumpActionName, InputActionType.Button, "player.jumpActionName", "Jump", issues);
            ValidateInputAction(map, settings.player.lookActionName, InputActionType.Value, "player.lookActionName", "Look", issues);
        }

        private static void ValidateInputAction(InputActionMap map, string actionName, InputActionType expectedType, string settingsPath, string label, List<CityGeneratorValidationIssue> issues)
        {
            InputAction action = map.FindAction(actionName);
            if (action == null)
            {
                issues.Add(new CityGeneratorValidationIssue(settingsPath, $"General: {label} action '{actionName}' was not found in the '{map.name}' action map."));
                return;
            }

            if (action.type != expectedType)
                issues.Add(new CityGeneratorValidationIssue(settingsPath, $"General: {label} action '{actionName}' is a {action.type} action, expected {expectedType}."));
        }

        public static bool Validate(CityGeneratorSettings settings, out List<string> errors)
        {
            bool valid = ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);
            errors = new List<string>();
            foreach (CityGeneratorValidationIssue issue in issues)
            {
                if (!issue.isWarning)
                    errors.Add(issue.message);
            }
            return valid;
        }
    }
}
