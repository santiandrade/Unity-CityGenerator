using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Measures and scales generated instances by their combined <see cref="Renderer"/> bounds
    /// in the XZ plane. Used both to fit floor prefabs (arbitrary authored size) to a target
    /// footprint and, later, to check overlap between placed objects.
    /// </summary>
    internal static class CityGeneratorBoundsUtility
    {
        private const float MinimumFootprintSize = 0.5f;

        public static Bounds GetWorldBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[City Generator] '{root.name}' has no Renderer in its hierarchy; using a minimum fallback footprint.", root);
                return new Bounds(root.transform.position, Vector3.one * MinimumFootprintSize);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        public static Vector2 GetXZSize(GameObject root)
        {
            Bounds bounds = GetWorldBounds(root);
            return new Vector2(Mathf.Max(bounds.size.x, MinimumFootprintSize), Mathf.Max(bounds.size.z, MinimumFootprintSize));
        }

        /// <summary>Rescales <paramref name="instance"/> on X/Z so its measured footprint matches the desired size, keeping its Y scale untouched.</summary>
        public static void ScaleToFootprint(GameObject instance, float desiredWidthX, float desiredDepthZ)
        {
            Vector2 measured = GetXZSize(instance);
            Vector3 scale = instance.transform.localScale;
            scale.x *= desiredWidthX / measured.x;
            scale.z *= desiredDepthZ / measured.y;
            instance.transform.localScale = scale;
        }
    }
}
