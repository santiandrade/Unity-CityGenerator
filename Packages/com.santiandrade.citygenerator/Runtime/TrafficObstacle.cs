using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Makes generated traffic treat this object as one more vehicle ahead: every
    /// <see cref="CarAgent"/> brakes for it and keeps its usual gap, exactly as it does for another
    /// car or a pedestrian. Add it to anything driving or standing on the road that is not a
    /// generated vehicle -- typically the car the player drives.
    ///
    /// SPEC 19: without it, a CarAgent's forward sensor only ever sees other CarAgents (vehicle
    /// layer, resolved through its collider registry) and pedestrians (pedestrian layer), so a
    /// user's own car on any other layer is invisible to it -- and with Enable Physics on, a
    /// generated car that imposes its velocity every physics step drags it down the street.
    ///
    /// Works on any layer: the sensor tests each registered collider individually with
    /// Collider.Raycast, never through a layer mask, so nothing in the project's layers or
    /// collision matrix has to change.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficObstacle : MonoBehaviour
    {
        [Tooltip("Colliders that make up this obstacle's body. Leave empty to use every non-trigger collider in this object's hierarchy (WheelColliders excluded), gathered when the component is enabled.")]
        [SerializeField] private Collider[] colliders;

        // Flattened to colliders rather than components: the sensor works collider by collider,
        // so walking each obstacle's hierarchy on every tick of every car would be wasted work.
        private static readonly List<Collider> Registered = new();

        private readonly List<Collider> ownRegistered = new();

        /// <summary>Every collider currently registered by an enabled TrafficObstacle.</summary>
        internal static IReadOnlyList<Collider> RegisteredColliders => Registered;

        // Reset for Play sessions with Domain Reload disabled, where a static list otherwise keeps
        // destroyed colliders from the previous session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            Registered.Clear();
        }

        private void OnEnable()
        {
            ownRegistered.Clear();

            if (colliders != null && colliders.Length > 0)
            {
                foreach (Collider candidate in colliders)
                    TryAdd(candidate);
            }
            else
            {
                foreach (Collider candidate in GetComponentsInChildren<Collider>())
                    TryAdd(candidate);
            }

            if (ownRegistered.Count == 0)
            {
                Debug.LogWarning($"{name}: TrafficObstacle found no solid collider to register; generated traffic will not see this object.", this);
            }
        }

        private void OnDisable()
        {
            foreach (Collider registered in ownRegistered)
                Registered.Remove(registered);
            ownRegistered.Clear();
        }

        private void TryAdd(Collider candidate)
        {
            // A WheelCollider is a suspension raycast, not a body volume: Collider.Raycast against
            // it says nothing about where the obstacle actually is.
            if (candidate == null || candidate.isTrigger || candidate is WheelCollider || ownRegistered.Contains(candidate))
                return;

            ownRegistered.Add(candidate);
            Registered.Add(candidate);
        }
    }
}
