using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Small modal confirmation dialog for <c>Tools &gt; City Generator &gt; Rebuild Minimap</c> when
    /// two or more cities are found in the scene: unlike <see cref="EditorUtility.DisplayDialog"/>,
    /// it lets the user edit the capture's texture resolution before confirming, since the union of
    /// several cities' footprints can be considerably larger than a single one's generation-time
    /// resolution.
    /// </summary>
    internal sealed class CityGeneratorRebuildMinimapDialog : EditorWindow
    {
        private const int MinResolution = 64;

        private string message;
        private int resolution;
        private bool confirmed;

        public static (bool confirmed, int resolution) Show(string message, int defaultResolution)
        {
            var window = CreateInstance<CityGeneratorRebuildMinimapDialog>();
            window.titleContent = new GUIContent("City Generator - Rebuild Minimap");
            window.message = message;
            window.resolution = defaultResolution;
            window.minSize = new Vector2(420f, 170f);
            window.maxSize = window.minSize;
            window.ShowModal();
            return (window.confirmed, window.resolution);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
            GUILayout.Space(10f);
            resolution = EditorGUILayout.IntField("Texture Resolution", resolution);
            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    confirmed = false;
                    Close();
                }

                if (GUILayout.Button("Confirm", GUILayout.Width(80f)))
                {
                    confirmed = true;
                    resolution = Mathf.Max(MinResolution, resolution);
                    Close();
                }
            }
        }
    }
}
