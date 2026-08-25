using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Shared collider policy for generated vehicle and pedestrian instances: the instance root
    /// always ends up with exactly one dedicated, non-trigger collider — the "proxy" — used
    /// exclusively for CarAgent's/PedestrianAgent's own sensor detection (CarAgent.OnEnable in
    /// particular assumes a single root Collider, since every demo vehicle already carries its
    /// own root BoxCollider). If the root already has a collider, it's reused as the proxy
    /// (forced non-trigger); otherwise a BoxCollider dimensioned from the combined Renderer
    /// bounds is added there. Either way, any collider that only exists deeper in the user's
    /// prefab hierarchy is left completely untouched — its own layer and isTrigger keep serving
    /// whatever purpose the user had for it (typically physical collision against the player's
    /// CharacterController), never the sensor's layer-filtered detection, which only ever looks
    /// at the proxy's own GameObject (the instance root, whose layer the caller assigns).
    /// </summary>
    internal static class CityGeneratorColliderUtility
    {
        /// <summary>Ensures the instance root carries the sensor proxy collider and returns it, so
        /// the caller can assign the Vehicle/Pedestrian layer to it (and only it).</summary>
        public static Collider EnsureNonTriggerCollider(GameObject instance)
        {
            Collider rootCollider = instance.GetComponent<Collider>();
            if (rootCollider != null)
            {
                rootCollider.isTrigger = false;
                return rootCollider;
            }

            Bounds worldBounds = CityGeneratorBoundsUtility.GetWorldBounds(instance);
            BoxCollider proxy = instance.AddComponent<BoxCollider>();
            proxy.isTrigger = false;
            proxy.center = instance.transform.InverseTransformPoint(worldBounds.center);

            Vector3 worldSize = worldBounds.size;
            Vector3 lossyScale = instance.transform.lossyScale;
            proxy.size = new Vector3(
                lossyScale.x != 0f ? worldSize.x / lossyScale.x : worldSize.x,
                lossyScale.y != 0f ? worldSize.y / lossyScale.y : worldSize.y,
                lossyScale.z != 0f ? worldSize.z / lossyScale.z : worldSize.z);

            return proxy;
        }
    }
}
