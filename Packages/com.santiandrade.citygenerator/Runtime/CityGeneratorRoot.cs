using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>Marker added to the root of every generated city, so a rebuild can find the
    /// previous city by component instead of by GameObject name (which the user may have
    /// renamed). Ships in Runtime so it also exists in player builds, not just the Editor.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class CityGeneratorRoot : MonoBehaviour
    {
    }
}
