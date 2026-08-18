using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Validates a <see cref="CityGeneratorSettings"/> instance before generation starts.
    /// </summary>
    internal static class CityGeneratorValidator
    {
        private const float PercentageTolerance = 0.01f;

        public static bool Validate(CityGeneratorSettings settings, out List<string> errors)
        {
            errors = new List<string>();

            if (settings.ground.roadBasePrefab == null)
                errors.Add("Ground: Road Base prefab is required.");
            if (settings.ground.sidewalkPrefab == null)
                errors.Add("Ground: Sidewalk prefab is required.");
            if (settings.ground.roadLinePrefab == null)
                errors.Add("Ground: Road Line prefab is required.");
            if (settings.ground.crosswalkLinePrefab == null)
                errors.Add("Ground: Crosswalk Line prefab is required.");

            if (settings.general.plazaCount > 0 && settings.plaza.lawnPrefab == null)
                errors.Add("Plaza: Lawn prefab is required when Plaza Count > 0.");

            if (settings.general.includeTraffic)
            {
                if (settings.props.trafficLightPrefab == null)
                {
                    errors.Add("Props: Traffic Light prefab is required when Include Traffic is enabled.");
                }
                else if (settings.props.trafficLightPrefab.GetComponent<Runtime.TrafficLight>() == null)
                {
                    errors.Add("Props: Traffic Light prefab must have a TrafficLight component.");
                }
            }

            if (settings.vegetation.density > 0f && settings.vegetation.prefabs.Count == 0)
                errors.Add("Vegetation: at least one prefab is required when Density > 0.");

            if (settings.general.vehicleCount > 0)
            {
                if (settings.vehicles.Count == 0)
                {
                    errors.Add("Vehicles: at least one vehicle entry is required when Vehicle Count > 0.");
                }
                else
                {
                    float percentageSum = 0f;
                    for (int i = 0; i < settings.vehicles.Count; i++)
                    {
                        VehicleEntry entry = settings.vehicles[i];
                        if (entry.prefab == null)
                            errors.Add($"Vehicles: entry {i + 1} is missing its prefab.");
                        percentageSum += entry.percentage;
                    }

                    if (Mathf.Abs(percentageSum - 100f) > PercentageTolerance)
                        errors.Add($"Vehicles: percentages must sum to 100 (currently {percentageSum:0.##}).");
                }
            }

            return errors.Count == 0;
        }
    }
}
