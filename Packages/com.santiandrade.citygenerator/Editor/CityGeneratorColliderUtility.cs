using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Shared collider policy for generated vehicle and pedestrian instances: if the user's
    /// prefab already carries a collider anywhere in its hierarchy, it is kept as-is (never
    /// added to on top of it); otherwise a BoxCollider dimensioned from the combined Renderer
    /// bounds is added to the instance root, the same way pedestrian NPCs have always gotten
    /// one. Either way isTrigger is forced to false: CarAgent's forward SphereCast and
    /// PedestrianAgent's own collider both rely on a solid collider to be detected, and it's
    /// also what lets the player's CharacterController physically collide with the instance
    /// instead of walking through it.
    /// </summary>
    internal static class CityGeneratorColliderUtility
    {
        public static void EnsureNonTriggerCollider(GameObject instance)
        {
            Collider[] existing = instance.GetComponentsInChildren<Collider>(true);
            if (existing.Length > 0)
            {
                foreach (Collider collider in existing)
                    collider.isTrigger = false;
                return;
            }

            Bounds worldBounds = CityGeneratorBoundsUtility.GetWorldBounds(instance);
            BoxCollider boxCollider = instance.AddComponent<BoxCollider>();
            boxCollider.isTrigger = false;
            boxCollider.center = instance.transform.InverseTransformPoint(worldBounds.center);

            Vector3 worldSize = worldBounds.size;
            Vector3 lossyScale = instance.transform.lossyScale;
            boxCollider.size = new Vector3(
                lossyScale.x != 0f ? worldSize.x / lossyScale.x : worldSize.x,
                lossyScale.y != 0f ? worldSize.y / lossyScale.y : worldSize.y,
                lossyScale.z != 0f ? worldSize.z / lossyScale.z : worldSize.z);
        }
    }
}
