using UnityEngine;

namespace TestAI
{
    /// <summary>
    /// Vehicle that travels along the <see cref="TrafficNetwork"/>: follows the lane graph,
    /// picks random turns at each crossing, stops at a traffic light that isn't green,
    /// and keeps its distance from the car ahead. Movement is continuous: it accelerates and
    /// brakes progressively, never teleporting or stopping for good.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarAgent : MonoBehaviour
    {
        [Header("Red")]
        [SerializeField] private TrafficNetwork network;

        [Header("Marcha")]
        [SerializeField] private float maxSpeed = 9f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float braking = 14f;
        [Tooltip("Velocidad de giro en grados por segundo. Determina el radio de las curvas.")]
        [SerializeField] private float turnSpeed = 230f;
        [Tooltip("Fracción de la velocidad máxima que se mantiene al trazar una curva.")]
        [SerializeField] private float cornerSpeedFactor = 0.45f;

        [Header("Nodos")]
        [SerializeField] private float arriveRadius = 1.5f;

        [Header("Detección")]
        [SerializeField] private LayerMask vehicleMask = ~0;
        [SerializeField] private float sensorRange = 12f;
        [SerializeField] private float sensorRadius = 0.7f;
        [SerializeField] private float minGap = 2.2f;
        [Tooltip("Distancia a partir de la cual se empieza a frenar ante un obstáculo o un rojo.")]
        [SerializeField] private float brakeDistance = 10f;

        [Header("Cruces sin semáforo")]
        [Tooltip("Distancia a la línea desde la que se aminora al acercarse a un cruce sin semáforo.")]
        [SerializeField] private float yieldDistance = 12f;

        [Tooltip("Distancia a la línea desde la que se reclama la prioridad del cruce.")]
        [SerializeField] private float claimDistance = 5f;

        [SerializeField] private float yieldSpeed = 4.5f;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        // Own identifier for crossing reservations; 0 means "crossing free".
        private static int nextCarId = 1;

        private int targetNode = -1;
        private int reservedIntersection = -1;
        private int carId;
        private float speed;
        private float distanceTravelled;
        private float stoppedTime;
        private StopReason stopReason;
        private bool approachingUnsignalled;

        /// <summary>Reason the vehicle is braking, useful for debugging jams.</summary>
        public enum StopReason
        {
            None,
            TrafficLight,
            Priority,
            VehicleAhead
        }

        public float Speed => speed;
        public int TargetNode => targetNode;
        public int ReservedIntersection => reservedIntersection;
        public float DistanceTravelled => distanceTravelled;
        /// <summary>Seconds it has been continuously stopped for.</summary>
        public float StoppedTime => stoppedTime;
        public StopReason CurrentStopReason => stopReason;

        private void Start()
        {
            if (network == null)
            {
                network = FindFirstObjectByType<TrafficNetwork>();
            }

            if (network == null)
            {
                Debug.LogError($"{name}: no hay TrafficNetwork en la escena.", this);
                enabled = false;
                return;
            }

            carId = nextCarId++;
            targetNode = network.FindNodeAhead(transform.position, transform.forward);
            if (targetNode < 0)
            {
                Debug.LogError($"{name}: no se encontró un nodo de la red por delante del vehículo.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            TrafficNetwork.Node node = network.GetNode(targetNode);

            Vector3 toTarget = node.Position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            stopReason = StopReason.None;
            approachingUnsignalled = false;

            float clearance = float.MaxValue;
            if (node.IsEntry)
            {
                clearance = IntersectionClearance(node, distance);
            }

            float ahead = VehicleAheadClearance();
            if (ahead < clearance)
            {
                clearance = ahead;
                stopReason = StopReason.VehicleAhead;
            }

            ReleaseStaleReservation(node);
            ReleaseReservationWhileBlocked();

            // Target speed: the maximum, trimmed down when approaching an obstacle and on corners.
            float targetSpeed = maxSpeed;
            if (clearance < brakeDistance)
            {
                targetSpeed = maxSpeed * Mathf.Clamp01(clearance / brakeDistance);
            }

            if (approachingUnsignalled)
            {
                targetSpeed = Mathf.Min(targetSpeed, yieldSpeed);
            }

            if (distance > 0.05f)
            {
                Vector3 desired = toTarget / distance;
                float turn = Vector3.Angle(transform.forward, desired);
                if (turn > 25f)
                {
                    targetSpeed = Mathf.Min(targetSpeed, maxSpeed * cornerSpeedFactor);
                }

                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(desired, Vector3.up), turnSpeed * Time.deltaTime);
            }

            if (clearance < 0.4f)
            {
                targetSpeed = 0f;
            }

            float rate = targetSpeed > speed ? acceleration : braking;
            speed = Mathf.MoveTowards(speed, targetSpeed, rate * Time.deltaTime);

            float step = speed * Time.deltaTime;
            distanceTravelled += step;
            stoppedTime = speed < 0.3f ? stoppedTime + Time.deltaTime : 0f;

            Vector3 position = transform.position + transform.forward * step;
            position.y = 0f;
            transform.position = position;

            if (distance < arriveRadius)
            {
                AdvanceToNextNode();
            }
        }

        /// <summary>
        /// Clear distance to the stop line of the crossing the vehicle is approaching:
        /// infinite if it has right of way, and the remaining distance to the line if it doesn't.
        /// Once past the line the vehicle keeps going, so it never stops inside the crossing.
        /// </summary>
        private float IntersectionClearance(TrafficNetwork.Node node, float distance)
        {
            float toStopLine = distance - network.StopLineBack;
            if (toStopLine < -0.5f)
            {
                return float.MaxValue;
            }

            bool mustStop;
            TrafficLightState? state = network.LightState(targetNode);
            if (state.HasValue)
            {
                // On amber only a car that still has enough room to brake comfortably stops.
                mustStop = state.Value == TrafficLightState.Red
                           || (state.Value == TrafficLightState.Amber && toStopLine > 5f);
                if (mustStop)
                {
                    stopReason = StopReason.TrafficLight;
                }
            }
            else if (toStopLine < yieldDistance)
            {
                // Unsignalled crossing: slows down like at a give-way sign, and priority is only
                // claimed right at the stop line, once the vehicle can cross immediately.
                // Claiming it from further away held it unused and blocked everyone else.
                approachingUnsignalled = true;
                mustStop = toStopLine < claimDistance && !network.TryReserve(node.Intersection, carId);
                if (mustStop)
                {
                    stopReason = StopReason.Priority;
                }
                else if (toStopLine < claimDistance)
                {
                    reservedIntersection = node.Intersection;
                }
            }
            else
            {
                mustStop = false;
            }

            return mustStop ? toStopLine : float.MaxValue;
        }

        /// <summary>Clear distance to the car ahead, minus the minimum gap.</summary>
        private float VehicleAheadClearance()
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 2.2f;
            int count = Physics.SphereCastNonAlloc(origin, sensorRadius, transform.forward, hits,
                sensorRange, vehicleMask, QueryTriggerInteraction.Ignore);

            float clearance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                // Discarded by identity, never by distance: a zero-distance hit is
                // the car's own collider, but also the car already bumper-to-bumper ahead, and
                // filtering by distance made it invisible and drove into it.
                CarAgent other = hits[i].collider.GetComponentInParent<CarAgent>();
                if (other == null || other == this)
                {
                    continue;
                }

                float gap = hits[i].distance <= 0.001f ? 0f : hits[i].distance - minGap;
                clearance = Mathf.Min(clearance, gap);
            }

            return clearance;
        }

        /// <summary>
        /// Releases priority if the vehicle holding it has stopped for another reason
        /// and hasn't entered the crossing yet: holding it while waiting for another car is what
        /// caused the mutual deadlock. If it's already inside the crossing it keeps it until leaving.
        /// </summary>
        private void ReleaseReservationWhileBlocked()
        {
            if (reservedIntersection < 0 || speed > 0.5f || stopReason != StopReason.VehicleAhead)
            {
                return;
            }

            Vector3 centre = network.IntersectionCentre(reservedIntersection);
            if (Vector3.Distance(transform.position, centre) > 7f)
            {
                network.Release(reservedIntersection, carId);
                reservedIntersection = -1;
            }
        }

        /// <summary>Releases a crossing's priority as soon as the vehicle has moved away from it.</summary>
        private void ReleaseStaleReservation(TrafficNetwork.Node node)
        {
            if (reservedIntersection < 0 || reservedIntersection == node.Intersection)
            {
                return;
            }

            Vector3 centre = network.IntersectionCentre(reservedIntersection);
            if (Vector3.Distance(transform.position, centre) > 10f)
            {
                network.Release(reservedIntersection, carId);
                reservedIntersection = -1;
            }
        }

        private void AdvanceToNextNode()
        {
            int next = network.PickNextNode(targetNode);
            if (next < 0)
            {
                // No exits (shouldn't happen on a grid): fall back to the nearest lane.
                next = network.FindNodeAhead(transform.position, transform.forward);
            }

            if (next >= 0)
            {
                targetNode = next;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (network == null || targetNode < 0)
            {
                return;
            }

            TrafficNetwork.Node node = network.GetNode(targetNode);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + Vector3.up, node.Position + Vector3.up);
            Gizmos.DrawWireSphere(node.Position + Vector3.up * 0.3f, 0.6f);

            if (node.IsEntry)
            {
                Gizmos.color = Color.red;
                Vector3 stop = network.StopLinePosition(targetNode);
                Gizmos.DrawLine(stop + Vector3.up * 0.1f, stop + Vector3.up * 1.5f);
            }
        }
    }
}
