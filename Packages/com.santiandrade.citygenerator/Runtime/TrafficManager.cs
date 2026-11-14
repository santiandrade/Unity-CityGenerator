using System.Collections.Generic;
using Unity.Profiling;
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
    ///
    /// SPEC 17: a car generated with Enable Physics on (<see cref="CarAgent.HasPhysics"/>) ticks
    /// from <c>FixedUpdate</c> instead, since it drives by setting Rigidbody velocity/rotation;
    /// a kinematic car keeps ticking from <c>Update</c> unchanged, even under the same manager.
    ///
    /// SPEC 20: distances are measured from <see cref="SimulationFocus"/> when one is set (the
    /// main camera otherwise, as before); a car neither near the focus nor visible to any camera
    /// can be staggered too (<see cref="invisibleStaggerDistance"/>); and physics-mode cars beyond
    /// <see cref="physicsRadius"/> of the focus drive as kinematic bodies. Every one of these is
    /// off by default.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficManager : MonoBehaviour
    {
        [Tooltip("Sensor staggering only activates once this many cars are registered.")]
        [SerializeField] private int staggerMinAgentCount = 60;

        [Tooltip("Cars farther than this from the simulation focus (the main camera when none is set) only run their forward sensor 1 out of StaggerFrames frames once staggering is active.")]
        [SerializeField] private float staggerDistance = 60f;

        [SerializeField] private int staggerFrames = 4;

        [Header("SPEC 20 (all off by default)")]
        [Tooltip("Where distances for staggering and physics LOD are measured from. Empty: the main camera.")]
        [SerializeField] private Transform simulationFocus;

        [Tooltip("Cars farther than this from the focus that no camera renders are staggered too, even inside Stagger Distance. 0 = off.")]
        [SerializeField] private float invisibleStaggerDistance;

        [Tooltip("Physics-mode cars farther than this from the focus drive as kinematic bodies (no contacts among themselves, no collision callbacks). 0 = off.")]
        [SerializeField] private float physicsRadius;

        [Tooltip("Extra distance beyond Physics Radius before a car goes back to kinematic, so one hovering at the edge doesn't flip every step.")]
        [SerializeField] private float physicsRadiusHysteresis = 10f;

        // HashSet, not List: Register is called from CarAgent.OnEnable (idempotent by nature —
        // an agent re-enabled without ever being unregistered must not end up ticked twice), and
        // membership check is what makes Add a no-op on a duplicate call.
        private readonly HashSet<CarAgent> agents = new();
        private int frameIndex;

        // SPEC 20: lets a consuming project split the frame time of a dense city by subsystem in
        // the Profiler (and in Performance tests through Measure.ProfilerMarkers). Marker names are
        // part of the package's observable surface: keep them stable.
        internal static readonly ProfilerMarker TickMarker = new("CityGenerator.Traffic.Tick");
        internal static readonly ProfilerMarker SensorsMarker = new("CityGenerator.Traffic.Sensors");
        internal static readonly ProfilerMarker VehicleSensorMarker = new("CityGenerator.Traffic.Sensors.Vehicles");
        internal static readonly ProfilerMarker VehicleOverlapMarker = new("CityGenerator.Traffic.Sensors.VehicleOverlap");
        internal static readonly ProfilerMarker PedestrianSensorMarker = new("CityGenerator.Traffic.Sensors.Pedestrians");
        internal static readonly ProfilerMarker ObstacleSensorMarker = new("CityGenerator.Traffic.Sensors.Obstacles");
        internal static readonly ProfilerMarker CollisionCallbacksMarker = new("CityGenerator.Traffic.CollisionCallbacks");
        private static readonly ProfilerMarker SyncTransformsMarker = new("CityGenerator.Traffic.SyncTransforms");
        private static readonly ProfilerMarker PhysicsLodMarker = new("CityGenerator.Traffic.PhysicsLod");

        // CarAgent.Tick can synchronously disable its own component (a Custom Grid dead end has
        // nowhere to send the car — see AdvanceToNextNode), which fires OnDisable -> Unregister
        // -> agents.Remove within the same foreach that is ticking it. Update/FixedUpdate iterate
        // these snapshots instead of the live set, rebuilt only when membership actually changes,
        // so a mid-frame Unregister never invalidates the enumeration in progress.
        //
        // SPEC 17: split by HasPhysics (detected per agent, not by a flag on this manager -- see
        // CarAgent.HasPhysics) so a Rigidbody-mode car ticks from FixedUpdate while a kinematic one
        // keeps ticking from Update exactly as before, even under the same manager (SPEC 16: two
        // cities, one with Enable Physics on and one off, can share a TrafficManager).
        private CarAgent[] kinematicSnapshot = System.Array.Empty<CarAgent>();
        private CarAgent[] physicsSnapshot = System.Array.Empty<CarAgent>();
        private bool agentsSnapshotDirty;
        // SPEC 20 (a): counts rendered frames that had at least one physics step, not steps, so a
        // far car's staggered sensor turn always lands on a step that runs sensors.
        private int physicsFrameIndex;
        private int lastPhysicsSensorFrame = -1;
        // SPEC 20: whether the physics LOD has any car kinematic right now, so switching it off
        // only walks the cars once to put them back.
        private bool anyKinematicByLod;

        // A car moves under a metre per frame, and the radius has a hysteresis band, so each car's
        // physics LOD is decided one frame with a physics step in this many.
        private const int PhysicsLodFrames = 4;

        /// <summary>Live count of currently registered agents — reflects a car that has since
        /// self-disabled (e.g. a Custom Grid dead end), unlike a count frozen at generation time.</summary>
        public int AgentCount => agents.Count;

        /// <summary>SPEC 20: where staggering and physics-LOD distances are measured from; null means
        /// the main camera, the behaviour before SPEC 20.</summary>
        public Transform SimulationFocus
        {
            get => simulationFocus;
            set => simulationFocus = value;
        }

        /// <summary>SPEC 20: physics-mode cars beyond this distance from the focus drive as kinematic
        /// bodies. 0 (the default) turns it off and puts every car back to dynamic.</summary>
        public float PhysicsRadius
        {
            get => physicsRadius;
            set => physicsRadius = Mathf.Max(0f, value);
        }

        /// <summary>SPEC 20: sets the staggering parameters at runtime. <paramref name="invisibleDistance"/>
        /// = 0 keeps visibility out of it (the default).</summary>
        public void SetStagger(float distance, int frames, float invisibleDistance = 0f)
        {
            staggerDistance = Mathf.Max(0f, distance);
            staggerFrames = Mathf.Max(1, frames);
            invisibleStaggerDistance = Mathf.Max(0f, invisibleDistance);
        }

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

        private void RebuildSnapshotsIfDirty()
        {
            if (!agentsSnapshotDirty)
                return;

            var kinematic = new List<CarAgent>(agents.Count);
            var physics = new List<CarAgent>(agents.Count);
            foreach (CarAgent agent in agents)
            {
                if (agent.HasPhysics)
                    physics.Add(agent);
                else
                    kinematic.Add(agent);
            }

            kinematicSnapshot = kinematic.ToArray();
            physicsSnapshot = physics.ToArray();
            agentsSnapshotDirty = false;
        }

        /// <summary>The focus position, or false when there is neither a focus nor a main camera.</summary>
        private bool TryGetFocusPosition(out Vector3 position)
        {
            if (simulationFocus != null)
            {
                position = simulationFocus.position;
                return true;
            }

            Camera cam = Camera.main;
            position = cam != null ? cam.transform.position : Vector3.zero;
            return cam != null;
        }

        /// <summary>Whether this car runs its sensors this tick under the staggering rules: always
        /// below staggerMinAgentCount or without a focus; otherwise 1 of staggerFrames ticks when it
        /// is beyond staggerDistance, or beyond invisibleStaggerDistance and seen by no camera.</summary>
        private bool ShouldRunSensor(CarAgent agent, bool staggeringActive, bool hasFocus, Vector3 focus, int index)
        {
            if (!staggeringActive || !hasFocus)
                return true;

            float sqrDistance = (agent.transform.position - focus).sqrMagnitude;
            bool staggered = sqrDistance > staggerDistance * staggerDistance
                             || (invisibleStaggerDistance > 0f
                                 && sqrDistance > invisibleStaggerDistance * invisibleStaggerDistance
                                 && !agent.IsVisible);
            return !staggered || (index + agent.CarId) % staggerFrames == 0;
        }

        private void Update()
        {
            if (agents.Count == 0)
                return;

            RebuildSnapshotsIfDirty();

            if (kinematicSnapshot.Length == 0)
                return;

            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            bool hasFocus = TryGetFocusPosition(out Vector3 focus);

            using (TickMarker.Auto())
            {
                foreach (CarAgent agent in kinematicSnapshot)
                    agent.Tick(dt, ShouldRunSensor(agent, staggeringActive, hasFocus, focus, frameIndex));
            }

            // CarAgent moves every kinematic car by writing transform.position directly (no
            // Rigidbody), and its forward sensor queries the physics scene in the same frame. With
            // DynamicsManager.m_AutoSyncTransforms off (the project default), the physics scene
            // only sees those moves at the next FixedUpdate, so at 60+ FPS the sensor reads
            // positions up to one frame stale — enough error for cars to miss each other on
            // corners. One sync here, after every kinematic CarAgent has ticked, is cheaper than
            // turning auto-sync back on (which would sync on every single query instead of once
            // per frame). Only called when there's at least one kinematic agent (the early return
            // above): a Rigidbody-mode car's position is already known to the physics engine, so
            // it never needs this sync.
            using (SyncTransformsMarker.Auto())
                Physics.SyncTransforms();

            frameIndex++;
        }

        // SPEC 17: writing a Rigidbody's velocity/rotation outside the physics step produces
        // jitter and lets an impact's momentum get overwritten erratically depending on
        // framerate, so every Rigidbody-mode CarAgent ticks here instead of Update. Only runs at
        // all once at least one registered agent is in physics mode (the early return below),
        // exactly mirroring how Update above costs nothing for a scene with zero CarAgents.
        private void FixedUpdate()
        {
            if (agents.Count == 0)
                return;

            RebuildSnapshotsIfDirty();

            if (physicsSnapshot.Length == 0)
                return;

            float dt = Time.fixedDeltaTime;

            // SPEC 20 (a): sensors run on the first physics step of each rendered frame only; the
            // remaining steps of the same frame reuse the cached clearances, exactly like a
            // staggered car. At 50 fps or more there is at most one step per frame, so nothing
            // changes; below that, sensors follow the frame rate (as a kinematic car's always
            // did) instead of paying again for every catch-up step of a slow frame.
            bool firstStepOfFrame = Time.frameCount != lastPhysicsSensorFrame;
            if (firstStepOfFrame)
            {
                lastPhysicsSensorFrame = Time.frameCount;
                physicsFrameIndex++;
            }

            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            bool hasFocus = TryGetFocusPosition(out Vector3 focus);

            // A car moves under a metre per rendered frame, so the LOD is decided once per frame.
            if (firstStepOfFrame)
                UpdatePhysicsLod(hasFocus, focus);

            using (TickMarker.Auto())
            {
                foreach (CarAgent agent in physicsSnapshot)
                {
                    bool runSensor = firstStepOfFrame && ShouldRunSensor(agent, staggeringActive, hasFocus, focus, physicsFrameIndex);
                    agent.Tick(dt, runSensor);
                }
            }
        }

        /// <summary>
        /// SPEC 20: puts every physics-mode car within physicsRadius of the focus in dynamic mode
        /// and every one beyond physicsRadius + hysteresis in kinematic mode (in between, it keeps
        /// its current mode). With the radius at 0, or without a focus, every car is dynamic, as
        /// before SPEC 20. CarAgent ignores the request while Recovering.
        /// </summary>
        private void UpdatePhysicsLod(bool hasFocus, Vector3 focus)
        {
            bool lodActive = physicsRadius > 0f && hasFocus;
            if (!lodActive && !anyKinematicByLod)
                return;

            using var _ = PhysicsLodMarker.Auto();
            anyKinematicByLod = false;
            float sqrInner = physicsRadius * physicsRadius;
            float outer = physicsRadius + Mathf.Max(0f, physicsRadiusHysteresis);
            float sqrOuter = outer * outer;

            foreach (CarAgent agent in physicsSnapshot)
            {
                if (!lodActive)
                {
                    if (!agent.IsPhysicsSimulated)
                        agent.SetPhysicsSimulated(true);
                    continue;
                }

                if ((physicsFrameIndex + agent.CarId) % PhysicsLodFrames == 0)
                {
                    float sqrDistance = (agent.transform.position - focus).sqrMagnitude;
                    if (sqrDistance <= sqrInner)
                    {
                        if (!agent.IsPhysicsSimulated)
                            agent.SetPhysicsSimulated(true);
                    }
                    else if (sqrDistance > sqrOuter && agent.IsPhysicsSimulated)
                    {
                        agent.SetPhysicsSimulated(false);
                    }
                }

                if (!agent.IsPhysicsSimulated)
                    anyKinematicByLod = true;
            }
        }
    }
}
