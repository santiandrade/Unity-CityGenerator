using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Added to the generated "Directional Light" by CityGeneratorSceneBuilder when the Editor's
    /// Day/Night Cycle setting is enabled, mirroring how MinimapData is projected from the
    /// editor-only MinimapSettings. Rotates the light on a single pitch axis over a 24h cycle
    /// (yaw/roll are captured once and held fixed) and samples <see cref="lightColorOverTime"/>/
    /// <see cref="lightIntensityOverTime"/> for its color/intensity. Advances <see cref="currentHour"/>
    /// automatically in Play Mode via speedMultiplier; the Editor preview right after generation is
    /// driven by a single explicit ApplySun call from the builder instead, so this component has no
    /// [ExecuteAlways] behaviour of its own.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("How fast simulated time passes relative to real time. 1 = real time, 2 = twice real time, etc.")]
        public float speedMultiplier = 1f;
        [Tooltip("Light color over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public Gradient lightColorOverTime;
        [Tooltip("Light intensity over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public AnimationCurve lightIntensityOverTime;

        [Tooltip("Current simulated hour of day (0-24). Advances automatically in Play Mode.")]
        public float currentHour;

        private Light cachedLight;
        private Quaternion baseRotation;
        private bool baseRotationCaptured;

        private void Awake()
        {
            CaptureBaseRotation();
        }

        private void Update()
        {
            currentHour = (currentHour + Time.deltaTime * speedMultiplier / 3600f) % 24f;
            ApplySun(currentHour);
        }

        /// <summary>
        /// Rotates this GameObject's Light to <paramref name="hourOfDay"/> (pitch only, on the X
        /// axis; yaw/roll stay at whatever they were when the component was added) and samples
        /// <see cref="lightColorOverTime"/>/<see cref="lightIntensityOverTime"/> at t = hourOfDay / 24
        /// for its color/intensity. Sets <see cref="currentHour"/> to match.
        /// </summary>
        public void ApplySun(float hourOfDay)
        {
            CaptureBaseRotation();
            currentHour = hourOfDay;

            float pitch = hourOfDay / 24f * 360f - 90f;
            Vector3 baseEuler = baseRotation.eulerAngles;
            transform.rotation = Quaternion.Euler(pitch, baseEuler.y, baseEuler.z);

            if (cachedLight == null)
                cachedLight = GetComponent<Light>();
            if (cachedLight == null)
                return;

            float t = hourOfDay / 24f;
            if (lightColorOverTime != null)
                cachedLight.color = lightColorOverTime.Evaluate(t);
            if (lightIntensityOverTime != null)
                cachedLight.intensity = lightIntensityOverTime.Evaluate(t);
        }

        private void CaptureBaseRotation()
        {
            if (baseRotationCaptured)
                return;
            baseRotation = transform.rotation;
            baseRotationCaptured = true;
        }
    }
}
