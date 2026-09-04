using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Resolution entry point for generated cities at runtime (Editor Play Mode and player builds
    /// alike). Holds no cached city reference of its own: <see cref="CityGeneratorInfo"/> registers
    /// itself on <c>OnEnable</c> and unregisters on <c>OnDisable</c>, the same lifecycle pattern
    /// <see cref="TrafficManager"/>/<see cref="PedestrianManager"/> use for their agents, so a domain
    /// reload or a scene unload clears stale entries by construction instead of leaving a dangling
    /// reference behind. Every resolved <see cref="CityGeneratorCity"/> handle wraps its
    /// <see cref="CityGeneratorInfo"/> live and every module getter returns a safe default
    /// (0/false/<see cref="Vector2Int.zero"/>/<see cref="Vector3.zero"/>/null) once that city is
    /// gone, never throws.
    /// </summary>
    public static class CityGeneratorAPI
    {
        private static readonly List<CityGeneratorInfo> registered = new();
        private static bool warnedAmbiguousDefault;

        /// <summary>The one registered city, or null when there are zero or more than one.</summary>
        public static CityGeneratorCity? Default
        {
            get
            {
                if (registered.Count == 1)
                    return new CityGeneratorCity(registered[0]);

                if (registered.Count > 1 && !warnedAmbiguousDefault)
                {
                    warnedAmbiguousDefault = true;
                    Debug.LogWarning($"CityGeneratorAPI.Default is ambiguous: {registered.Count} cities are registered. Use InScene/For/All instead.");
                }

                return null;
            }
        }

        /// <summary>All registered (active) cities, in registration order. A live view, not a copy.</summary>
        public static IReadOnlyList<CityGeneratorCity> All => new CityList(registered);

        public static int Count => registered.Count;

        public static CityGeneratorCity? InScene(Scene scene)
        {
            foreach (CityGeneratorInfo candidate in registered)
            {
                if (candidate.gameObject.scene == scene)
                    return new CityGeneratorCity(candidate);
            }

            return null;
        }

        /// <summary>
        /// Resolves the handle for a known <see cref="CityGeneratorInfo"/> even while its root is
        /// deactivated (and therefore unregistered) -- the only way to query a preloaded, inactive city.
        /// </summary>
        public static CityGeneratorCity? For(CityGeneratorInfo info) => info != null ? new CityGeneratorCity(info) : null;

        internal static void Register(CityGeneratorInfo info)
        {
            if (!registered.Contains(info))
                registered.Add(info);
        }

        internal static void Unregister(CityGeneratorInfo info)
        {
            registered.Remove(info);
        }

        /// <summary>Adapts the internal registration list to <see cref="CityGeneratorCity"/> without copying it.</summary>
        private readonly struct CityList : IReadOnlyList<CityGeneratorCity>
        {
            private readonly List<CityGeneratorInfo> source;
            public CityList(List<CityGeneratorInfo> source) => this.source = source;

            public CityGeneratorCity this[int index] => new(source[index]);
            public int Count => source.Count;

            public IEnumerator<CityGeneratorCity> GetEnumerator()
            {
                foreach (CityGeneratorInfo info in source)
                    yield return new CityGeneratorCity(info);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
