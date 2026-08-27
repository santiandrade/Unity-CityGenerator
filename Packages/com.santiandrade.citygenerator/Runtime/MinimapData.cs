using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>Custom Place marked as Point of Interest: display title and world position, projected
    /// from the editor-only CustomPlaceEntry by CityGeneratorMinimapBuilder.</summary>
    [Serializable]
    public struct PointOfInterestEntry
    {
        public string title;
        public Vector3 worldPosition;
    }

    /// <summary>
    /// Added to the root of every generated city (alongside CityGeneratorRoot) by
    /// CityGeneratorMinimapBuilder when the Minimap is enabled. Ships in Runtime so it also exists
    /// in player builds, not just the Editor; MinimapHUD reads it at Awake/Start to render the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class MinimapData : MonoBehaviour
    {
        [Tooltip("Top-down snapshot of the generated city, captured once during generation.")]
        public Texture2D snapshot;
        [Tooltip("World-space XZ origin (min corner) of the area covered by the snapshot.")]
        public Vector2 worldOrigin;
        [Tooltip("World-space size (width, depth) in meters of the area covered by the snapshot.")]
        public Vector2 worldSize;
        [Tooltip("Custom Places marked as Point of Interest: display title and world position.")]
        public List<PointOfInterestEntry> pointsOfInterest = new();
    }
}
