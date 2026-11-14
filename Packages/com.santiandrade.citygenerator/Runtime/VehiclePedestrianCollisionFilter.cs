using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// SPEC 17: suppresses the physical contact between a Rigidbody-mode vehicle and an NPC
    /// pedestrian, which otherwise lets a pedestrian shove a car around.
    ///
    /// A generated pedestrian is a plain <see cref="Collider"/> with no Rigidbody, moved by writing
    /// <c>transform.position</c> every frame (see <see cref="PedestrianAgent"/>) — to PhysX that is
    /// a *static* collider being teleported, i.e. one of infinite mass. No amount of vehicle mass
    /// wins against it: the solver depenetrates the car out of the pedestrian, pushing it off its
    /// lane, and the impulse of that contact is large enough to knock the car into
    /// <c>Recovering</c>, at which point the car stops correcting its own velocity and the
    /// pedestrian visibly drags it.
    ///
    /// The fix is a per-pair <see cref="Physics.IgnoreCollision(Collider, Collider, bool)"/> rather
    /// than a layer-matrix entry, precisely because the player shares the pedestrian layer (see
    /// CityGeneratorSceneBuilder.AssignPedestrianLayer): disabling Vehicle↔Pedestrian in the matrix
    /// would also let the player's CharacterController walk straight through cars, and would write
    /// to the user's Project Settings. Pairing only registered <see cref="PedestrianAgent"/>s keeps
    /// the player — and any other user object on that layer — colliding exactly as before.
    ///
    /// CarAgent registers only when it has a Rigidbody, so with <c>Enable Physics</c> off nothing is
    /// ever registered on the vehicle side, no pair is ever ignored, and the runtime stays
    /// bit-for-bit identical to before this spec.
    ///
    /// Only the root proxy collider of each side takes part, the same collider policy
    /// CityGeneratorColliderUtility already applies: a collider deeper in a user prefab's hierarchy
    /// is left completely untouched here too.
    /// </summary>
    internal static class VehiclePedestrianCollisionFilter
    {
        // Registered from OnEnable / removed from OnDisable on both sides, so a pair is (re)ignored
        // whenever either collider is re-enabled — Unity resets a collider's ignore state when it
        // is disabled and enabled again, which a one-shot pass at scene start would not survive.
        private static readonly List<Collider> VehicleColliders = new();
        private static readonly List<Collider> PedestrianColliders = new();

        // SPEC 20: whether pairs are actually ignored. Registration keeps tracking both sides
        // either way, so switching it back on can pair every collider that is active right now.
        private static bool enabled = true;

        /// <summary>
        /// SPEC 20: on by default. Off, no pair is ignored and every pair ignored so far is restored,
        /// for a project that keeps vehicles and pedestrians apart by its own means (typically its
        /// layer collision matrix, which this package never writes). Exposed as
        /// <see cref="CityGeneratorAPI.VehiclePedestrianPairFiltering"/>.
        /// </summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value)
                    return;

                // Off: restore the pairs while they are still flagged as ignored, then stop.
                // On: flag first, so SetAllPairs actually ignores them.
                if (!value)
                    SetAllPairs(false);
                enabled = value;
                if (value)
                    SetAllPairs(true);
            }
        }

        // Same reason as CarAgent.ResetCarIdCounter: with Domain Reload disabled these static lists
        // would otherwise carry destroyed colliders from the previous Play session into the next.
        // The -= before the += is there for the same reason: the subscription itself would survive
        // into the next session and fire twice.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistries()
        {
            VehicleColliders.Clear();
            PedestrianColliders.Clear();
            enabled = true;

            Application.quitting -= ClearAllPairs;
            Application.quitting += ClearAllPairs;
        }

        /// <summary>
        /// Empties the ignore table before teardown starts, which is what keeps quitting the game
        /// (or leaving Play mode) from taking ten seconds on a busy city.
        ///
        /// Unity keeps every ignored pair in one global table and rescans it in full whenever *any*
        /// collider is destroyed, to drop the entries naming it. A default demo city registers
        /// vehicles x pedestrians pairs — measured at 300 x 500 = 149,500 — and a scene teardown
        /// destroys every collider in the scene, not just the agents': measured on
        /// Assets/Scenes/City.unity, destroying 100 colliders that take part in no pair at all cost
        /// 210 ms with the table populated versus 78 ms with it emptied, i.e. ~1.3 ms of pure table
        /// scan per collider. Over that scene's 7,840 colliders that is the ~10 s stall, and it is
        /// PhysX-side, so a build stalls exactly the same way.
        ///
        /// Clearing the whole table up front costs one pass (65 ms for those 149,500 pairs) and
        /// leaves every subsequent destruction paying nothing. Application.quitting is the hook
        /// because it fires before anything is destroyed, in a build and on leaving Play mode
        /// alike, so the pairs are still valid and there is still a physics scene to unregister
        /// them from. Nothing re-registers afterwards: OnEnable is the only entry point.
        /// </summary>
        private static void ClearAllPairs()
        {
            if (enabled)
                SetAllPairs(false);

            VehicleColliders.Clear();
            PedestrianColliders.Clear();
        }

        /// <summary>Ignores (or restores) every registered vehicle x pedestrian pair that
        /// <see cref="Physics.IgnoreCollision(Collider, Collider, bool)"/> still accepts.</summary>
        private static void SetAllPairs(bool ignore)
        {
            for (int v = VehicleColliders.Count - 1; v >= 0; v--)
            {
                Collider vehicleCollider = VehicleColliders[v];
                if (!CanIgnore(vehicleCollider))
                    continue;

                for (int p = PedestrianColliders.Count - 1; p >= 0; p--)
                {
                    Collider pedestrianCollider = PedestrianColliders[p];
                    if (!CanIgnore(pedestrianCollider))
                        continue;

                    Physics.IgnoreCollision(vehicleCollider, pedestrianCollider, ignore);
                }
            }
        }

        /// <summary>
        /// True when <paramref name="collider"/> can still be passed to
        /// <see cref="Physics.IgnoreCollision(Collider, Collider, bool)"/>, which rejects a
        /// destroyed or inactive one. An inactive collider needs no clearing anyway: Unity already
        /// dropped its ignore state when it was disabled, which is exactly why registration happens
        /// in OnEnable rather than once at scene start.
        /// </summary>
        private static bool CanIgnore(Collider collider)
            => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

        public static void RegisterVehicle(Collider vehicleCollider)
        {
            if (vehicleCollider == null || VehicleColliders.Contains(vehicleCollider))
                return;

            VehicleColliders.Add(vehicleCollider);
            IgnoreAgainst(vehicleCollider, PedestrianColliders);
        }

        public static void UnregisterVehicle(Collider vehicleCollider)
            => VehicleColliders.Remove(vehicleCollider);

        public static void RegisterPedestrian(Collider pedestrianCollider)
        {
            if (pedestrianCollider == null || PedestrianColliders.Contains(pedestrianCollider))
                return;

            PedestrianColliders.Add(pedestrianCollider);
            IgnoreAgainst(pedestrianCollider, VehicleColliders);
        }

        public static void UnregisterPedestrian(Collider pedestrianCollider)
            => PedestrianColliders.Remove(pedestrianCollider);

        /// <summary>
        /// SPEC 20: pairs a registered pedestrian collider again with every vehicle after it was
        /// switched off and on (PedestrianManager's collider LOD), since Unity drops a disabled
        /// collider's ignored pairs. Registration itself is untouched.
        /// </summary>
        public static void RepairPedestrian(Collider pedestrianCollider)
        {
            if (pedestrianCollider != null && PedestrianColliders.Contains(pedestrianCollider))
                IgnoreAgainst(pedestrianCollider, VehicleColliders);
        }

        /// <summary>
        /// Ignores <paramref name="collider"/> against every collider in <paramref name="others"/>,
        /// dropping any entry whose GameObject was destroyed without going through OnDisable
        /// (Physics.IgnoreCollision throws on a destroyed collider).
        /// </summary>
        private static void IgnoreAgainst(Collider collider, List<Collider> others)
        {
            if (!enabled)
                return;

            for (int i = others.Count - 1; i >= 0; i--)
            {
                Collider other = others[i];
                if (other == null)
                {
                    others.RemoveAt(i);
                    continue;
                }

                Physics.IgnoreCollision(collider, other, true);
            }
        }
    }
}
