using System.Collections.Generic;
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
        /// <summary>
        /// Name of the package's own building icon. The Editor ships no built-in icon that reads as
        /// a building, so this one is drawn procedurally (see <see cref="CreateBuildingIcon"/>)
        /// rather than shipped as a PNG asset.
        /// </summary>
        public const string Building = "CityGenerator.Building";

        private const int BuildingIconSize = 32;

        private static readonly Dictionary<(string, Color), Texture2D> recoloredCache = new Dictionary<(string, Color), Texture2D>();
        private static Texture2D buildingIcon;

        public static Texture2D Get(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            if (iconName == Building)
            {
                if (buildingIcon == null)
                    buildingIcon = CreateBuildingIcon();
                return buildingIcon;
            }

            GUIContent content = EditorGUIUtility.IconContent(iconName);
            return content != null ? content.image as Texture2D : null;
        }

        /// <summary>
        /// Returns a copy of a built-in icon recoloured to <paramref name="color"/>: each pixel keeps
        /// its alpha and its brightness relative to the icon's brightest pixel, but takes the new hue.
        /// A plain UI Toolkit tint can't do this — it multiplies, so tinting an already-coloured icon
        /// produces a muddy mix instead of a different colour. Built-in icons are not CPU-readable,
        /// hence the round trip through a RenderTexture. Falls back to the original icon if the copy fails.
        /// </summary>
        public static Texture2D GetRecolored(string iconName, Color color)
        {
            Texture2D source = Get(iconName);
            if (source == null)
                return null;

            if (recoloredCache.TryGetValue((iconName, color), out Texture2D cached) && cached != null)
                return cached;

            Texture2D readable = CopyToReadable(source);
            if (readable == null)
                return source;

            Color32[] pixels = readable.GetPixels32();
            float maxLuminance = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0)
                    continue;
                maxLuminance = Mathf.Max(maxLuminance, Luminance(pixels[i]));
            }

            if (maxLuminance <= 0f)
                maxLuminance = 1f;

            for (int i = 0; i < pixels.Length; i++)
            {
                float brightness = Mathf.Clamp01(Luminance(pixels[i]) / maxLuminance);
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(color.r * brightness * 255f),
                    (byte)Mathf.RoundToInt(color.g * brightness * 255f),
                    (byte)Mathf.RoundToInt(color.b * brightness * 255f),
                    pixels[i].a);
            }

            readable.SetPixels32(pixels);
            readable.Apply(false, true);
            readable.hideFlags = HideFlags.HideAndDontSave;
            recoloredCache[(iconName, color)] = readable;
            return readable;
        }

        /// <summary>
        /// Draws a white, 32x32 building silhouette (a tower with an antenna and a lower annex,
        /// windows and door cut out as transparency) at twice the 16px card icon size, so it stays
        /// crisp on high-DPI displays. White so <see cref="GetRecolored"/> can give it any colour.
        /// </summary>
        private static Texture2D CreateBuildingIcon()
        {
            var pixels = new Color32[BuildingIconSize * BuildingIconSize];
            var solid = new Color32(255, 255, 255, 255);
            var clear = new Color32(255, 255, 255, 0);

            // Rects are (x, yFromTop, width, height).
            // Tower, its antenna and the lower annex.
            FillRect(pixels, 4, 4, 14, 28, solid);
            FillRect(pixels, 10, 0, 2, 4, solid);
            FillRect(pixels, 19, 13, 9, 19, solid);

            // Tower windows: 3 columns x 4 floors, plus the door.
            for (int floor = 0; floor < 4; floor++)
            {
                for (int column = 0; column < 3; column++)
                    FillRect(pixels, 6 + column * 4, 7 + floor * 5, 2, 3, clear);
            }
            FillRect(pixels, 9, 27, 4, 5, clear);

            // Annex windows: 2 columns x 3 floors.
            for (int floor = 0; floor < 3; floor++)
            {
                for (int column = 0; column < 2; column++)
                    FillRect(pixels, 21 + column * 3, 16 + floor * 5, 2, 3, clear);
            }

            var texture = new Texture2D(BuildingIconSize, BuildingIconSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void FillRect(Color32[] pixels, int x, int yFromTop, int width, int height, Color32 color)
        {
            for (int row = yFromTop; row < yFromTop + height; row++)
            {
                // Texture rows run bottom-up.
                int textureY = BuildingIconSize - 1 - row;
                for (int col = x; col < x + width; col++)
                    pixels[textureY * BuildingIconSize + col] = color;
            }
        }

        private static float Luminance(Color32 c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        private static Texture2D CopyToReadable(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply(false, false);
                return copy;
            }
            catch (System.Exception)
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
