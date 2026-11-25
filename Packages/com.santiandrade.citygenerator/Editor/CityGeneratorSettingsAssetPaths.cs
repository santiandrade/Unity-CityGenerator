using System.IO;
using UnityEditor;

namespace CityGenerator.Editor
{
    /// <summary>
    /// SPEC 24: the per-scene folder every generated asset of a city lives in --
    /// <c>&lt;SceneFolder&gt;/&lt;SceneName&gt;/</c>, the same folder Unity itself creates next to a
    /// scene for baked lighting data. Computed in this one place so the minimap snapshot
    /// (<see cref="CityGeneratorMinimapBuilder"/>) and the settings asset
    /// (<see cref="CityGeneratorSettingsAsset"/>) can never end up in different folders.
    /// Every method returns null for an unsaved scene (empty path), which callers treat as
    /// "write nothing".
    /// </summary>
    internal static class CityGeneratorSettingsAssetPaths
    {
        internal const string SettingsAssetSuffix = "_CityGeneratorSettings.asset";
        internal const string MinimapSuffix = "_Minimap.png";

        /// <summary><c>&lt;SceneFolder&gt;/&lt;SceneName&gt;</c>, or null if <paramref name="scenePath"/> is empty.</summary>
        internal static string SceneAssetFolder(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return null;

            string sceneFolder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            return $"{sceneFolder}/{sceneName}";
        }

        /// <summary><c>&lt;SceneFolder&gt;/&lt;SceneName&gt;/&lt;SceneName&gt;_CityGeneratorSettings.asset</c>, or null if the scene is unsaved.</summary>
        internal static string ForScene(string scenePath)
        {
            string folder = SceneAssetFolder(scenePath);
            return folder == null ? null : $"{folder}/{Path.GetFileNameWithoutExtension(scenePath)}{SettingsAssetSuffix}";
        }

        /// <summary><c>&lt;SceneFolder&gt;/&lt;SceneName&gt;/&lt;SceneName&gt;_Minimap.png</c>, or null if the scene is unsaved.</summary>
        internal static string MinimapForScene(string scenePath)
        {
            string folder = SceneAssetFolder(scenePath);
            return folder == null ? null : $"{folder}/{Path.GetFileNameWithoutExtension(scenePath)}{MinimapSuffix}";
        }

        /// <summary>Creates the per-scene folder if it doesn't exist yet and returns it; null if the scene is unsaved.</summary>
        internal static string EnsureSceneAssetFolder(string scenePath)
        {
            string folder = SceneAssetFolder(scenePath);
            if (folder == null)
                return null;

            if (!AssetDatabase.IsValidFolder(folder))
            {
                string sceneFolder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
                AssetDatabase.CreateFolder(sceneFolder, Path.GetFileNameWithoutExtension(scenePath));
            }

            return folder;
        }
    }
}
