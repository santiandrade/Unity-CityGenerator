using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// Resolves built-in Editor icons by name for the window's cards, falling back to no icon
    /// (rather than throwing or logging) when a name doesn't resolve on the running Editor
    /// version — icon names are not part of Unity's public API and do shift between versions.
    /// </summary>
    internal static class CityGeneratorUiIcons
    {
        public static Texture2D Get(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            GUIContent content = EditorGUIUtility.IconContent(iconName);
            return content != null ? content.image as Texture2D : null;
        }
    }
}
