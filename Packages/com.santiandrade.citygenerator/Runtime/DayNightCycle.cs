using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Added to the generated "Directional Light" by CityGeneratorSceneBuilder, mirroring how
    /// MinimapData is projected from the editor-only MinimapSettings. Rotates the light on a
    /// single pitch axis over a 24h cycle (yaw/roll are captured once and held fixed) and samples
    /// <see cref="lightColorOverTime"/>/<see cref="lightIntensityOverTime"/> for its color/intensity.
    /// Advances <see cref="currentHour"/> automatically in Play Mode via speedMultiplier — but only
    /// while this Behaviour's own <c>enabled</c> is true, which CityGeneratorSceneBuilder sets to
    /// the Editor's Day/Night Cycle "Enabled" toggle: when off, the component stays attached but
    /// inert (Unity skips Update on a disabled Behaviour), so the light stays fixed at Start Hour
    /// instead of cycling. The Editor preview right after generation is driven by a single explicit
    /// ApplySun call from the builder, called unconditionally regardless of enabled, so this
    /// component has no [ExecuteAlways] behaviour of its own.
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

        /// <summary>
        /// Overrides the yaw/roll used by <see cref="ApplySun"/>, bypassing the "captured once"
        /// guard in <see cref="CaptureBaseRotation"/>. Used by CityGeneratorSceneBuilder to force
        /// the Directional Light's yaw on every build/re-build, even when this component already
        /// existed with a different baked-in yaw.
        /// </summary>
        public void SetBaseRotation(Quaternion rotation)
        {
            baseRotation = rotation;
            baseRotationCaptured = true;
        }
    }
}
