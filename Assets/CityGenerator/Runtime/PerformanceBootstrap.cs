using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Fixes an explicit frame rate target as soon as the runtime starts, so the game
    /// does not run uncapped. Ships with the tool (rather than as a project setting) so
    /// generated cities behave consistently even when the package is copied elsewhere.
    /// </summary>
    internal static class PerformanceBootstrap
    {
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyTargetFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
