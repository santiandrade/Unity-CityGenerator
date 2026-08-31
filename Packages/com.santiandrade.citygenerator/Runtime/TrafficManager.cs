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

        // CarAgent.Tick can synchronously disable its own component (a Custom Grid dead end has
        // nowhere to send the car — see AdvanceToNextNode), which fires OnDisable -> Unregister
        // -> agents.Remove within the same foreach that is ticking it. Update iterates this
        // snapshot instead of the live set, rebuilt only when membership actually changes, so a
        // mid-frame Unregister never invalidates the enumeration in progress.
        private CarAgent[] agentsSnapshot = System.Array.Empty<CarAgent>();
        private bool agentsSnapshotDirty;

        public void Register(CarAgent agent)
        {
            if (agents.Add(agent))
                agentsSnapshotDirty = true;
        }

        public void Unregister(CarAgent agent)
        {
            if (agents.Remove(agent))
                agentsSnapshotDirty = true;
        }

        private void Update()
        {
            if (agents.Count == 0)
                return;

            if (agentsSnapshotDirty)
            {
                agentsSnapshot = new CarAgent[agents.Count];
                agents.CopyTo(agentsSnapshot);
                agentsSnapshotDirty = false;
            }

            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            Camera cam = staggeringActive ? Camera.main : null;
            Vector3 camPosition = cam != null ? cam.transform.position : Vector3.zero;
            float sqrStaggerDistance = staggerDistance * staggerDistance;

            foreach (CarAgent agent in agentsSnapshot)
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

            // CarAgent moves every car by writing transform.position directly (no Rigidbody), and
            // its forward sensor queries the physics scene in the same frame. With
            // DynamicsManager.m_AutoSyncTransforms off (the project default), the physics scene
            // only sees those moves at the next FixedUpdate, so at 60+ FPS the sensor reads
            // positions up to one frame stale — enough error for cars to miss each other on
            // corners. One sync here, after every CarAgent has ticked, is cheaper than turning
            // auto-sync back on (which would sync on every single query instead of once per
            // frame). Only called when there's at least one agent (the early return above), and
            // moved here from TrafficNetwork.LateUpdate so a scene with a TrafficNetwork but zero
            // registered CarAgents never pays for it at all.
            Physics.SyncTransforms();

            frameIndex++;
        }
    }
}
