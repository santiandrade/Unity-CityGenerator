using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Ticks every registered <see cref="CarAgent"/> from a single <c>Update</c> instead of each
    /// car paying Unity's per-component Update marshalling cost individually. Once enough cars are
    /// registered it also staggers the forward-sensor <c>SphereCast</c> for cars far from the main
    /// camera, reusing the previous frame's clearance on skipped frames — see the technical
    /// review, A.7. Below <see cref="staggerMinAgentCount"/> every car casts every frame, so a
    /// small generated city (e.g. the default 30-car demo) behaves exactly as before.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficManager : MonoBehaviour
    {
        [Tooltip("Sensor staggering only activates once this many cars are registered.")]
        [SerializeField] private int staggerMinAgentCount = 60;

        [Tooltip("Cars farther than this from the main camera only run their forward sensor 1 out of StaggerFrames frames once staggering is active.")]
        [SerializeField] private float staggerDistance = 60f;

        [SerializeField] private int staggerFrames = 4;

        // HashSet, not List: Register is called from CarAgent.OnEnable (idempotent by nature —
        // an agent re-enabled without ever being unregistered must not end up ticked twice), and
        // membership check is what makes Add a no-op on a duplicate call.
        private readonly HashSet<CarAgent> agents = new();
        private int frameIndex;

        public void Register(CarAgent agent) => agents.Add(agent);

        public void Unregister(CarAgent agent) => agents.Remove(agent);

        private void Update()
        {
            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            Camera cam = staggeringActive ? Camera.main : null;
            Vector3 camPosition = cam != null ? cam.transform.position : Vector3.zero;
            float sqrStaggerDistance = staggerDistance * staggerDistance;

            foreach (CarAgent agent in agents)
            {
                bool runSensor = true;

                if (cam != null)
                {
                    float sqrDistance = (agent.transform.position - camPosition).sqrMagnitude;
                    if (sqrDistance > sqrStaggerDistance)
                        runSensor = (frameIndex + agent.CarId) % staggerFrames == 0;
                }

                agent.Tick(dt, runSensor);
            }

            frameIndex++;
        }
    }
}
