using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>One problem found by <see cref="CityGeneratorValidator.ValidateDetailed"/>, tied to the settings path that caused it.</summary>
    internal readonly struct CityGeneratorValidationIssue
    {
        /// <summary>Relative path within <see cref="CityGeneratorSettings"/> (e.g. "ground.roadBasePrefab"), matching the paths <c>CityGeneratorWindow.FindProperty</c> resolves. Used by the window to highlight the offending field/card.</summary>
        public readonly string settingsPath;
        public readonly string message;

        public CityGeneratorValidationIssue(string settingsPath, string message)
        {
            this.settingsPath = settingsPath;
            this.message = message;
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

            if (settings.general.playerPrefab != null && settings.general.inputActions == null)
                issues.Add(new CityGeneratorValidationIssue("general.inputActions", "General: Input Actions asset is required when Player Prefab is set (otherwise the generated camera silently gets no input)."));

            if (settings.general.includeTraffic)
            {
                if (settings.props.trafficLightPrefab == null)
                {
                    issues.Add(new CityGeneratorValidationIssue("props.trafficLightPrefab", "Props: Traffic Light prefab is required when Include Traffic is enabled."));
                }
                else if (settings.props.trafficLightPrefab.GetComponent<Runtime.TrafficLight>() == null)
                {
                    issues.Add(new CityGeneratorValidationIssue("props.trafficLightPrefab", "Props: Traffic Light prefab must have a TrafficLight component."));
                }
            }

            if (settings.vegetation.density > 0f && settings.vegetation.prefabs.Count == 0)
                issues.Add(new CityGeneratorValidationIssue("vegetation.prefabs", "Vegetation: at least one prefab is required when Density > 0."));

            if (settings.general.vehicleCount > 0)
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
                        percentageSum += entry.percentage;
                    }

                    if (Mathf.Abs(percentageSum - 100f) > PercentageTolerance)
                        issues.Add(new CityGeneratorValidationIssue("vehicles", $"Vehicles: percentages must sum to 100 (currently {percentageSum:0.##})."));
                }
            }

            if (settings.general.pedestrianCount > 0)
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
                        percentageSum += entry.percentage;
                    }

                    if (Mathf.Abs(percentageSum - 100f) > PercentageTolerance)
                        issues.Add(new CityGeneratorValidationIssue("pedestrians", $"Pedestrians: percentages must sum to 100 (currently {percentageSum:0.##})."));
                }
            }

            return issues.Count == 0;
        }

        public static bool Validate(CityGeneratorSettings settings, out List<string> errors)
        {
            bool valid = ValidateDetailed(settings, out List<CityGeneratorValidationIssue> issues);
            errors = new List<string>(issues.Count);
            foreach (CityGeneratorValidationIssue issue in issues)
                errors.Add(issue.message);
            return valid;
        }
    }
}
