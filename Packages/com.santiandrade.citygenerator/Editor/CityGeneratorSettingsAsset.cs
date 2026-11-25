using System;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// SPEC 24: a complete <see cref="CityGeneratorSettings"/> stored as a project asset, so the
    /// recipe of a city survives the window's layout and can be shared through version control.
    /// Every Build/Re-Build writes one next to its scene
    /// (<see cref="CityGeneratorSettingsAssetPaths.ForScene"/>); more can be created from
    /// Assets &gt; Create &gt; City Generator &gt; City Settings or the window's Save As.
    /// The window never edits this object directly: <see cref="CopySettings"/> and
    /// <see cref="Store"/> always deep-copy through <see cref="EditorJsonUtility"/>, so the two never
    /// share a list and nothing reaches the asset until the user saves.
    /// Editor-only on purpose: <see cref="CityGeneratorSettings"/> lives in the Editor assembly,
    /// nothing at runtime references this type, and it never enters a build (unless placed under a
    /// Resources/ folder, which the user manual warns against).
    /// </summary>
    [CreateAssetMenu(menuName = "City Generator/City Settings", fileName = "CityGeneratorSettings")]
    public sealed class CityGeneratorSettingsAsset : ScriptableObject
    {
        [SerializeField] internal CityGeneratorSettings settings = new();
        [Tooltip("Package version that last wrote this asset.")]
        [SerializeField] internal string packageVersion;

        // Assets created from the Create menu start from the package's shipped defaults, like a
        // freshly opened window. Reset() runs only for a brand-new instance (and the Inspector's
        // own Reset command), never on load, so an existing asset's settings are never touched.
        private void Reset()
        {
            settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            packageVersion = CurrentPackageVersion();
        }

        /// <summary>A deep copy of the stored settings, sharing no list or nested object with this asset.</summary>
        internal CityGeneratorSettings CopySettings()
        {
            return Clone(settings);
        }

        /// <summary>Overwrites the stored settings with a deep copy of <paramref name="source"/> and records the current package version. Callers handle Undo and saving.</summary>
        internal void Store(CityGeneratorSettings source)
        {
            settings = Clone(source);
            packageVersion = CurrentPackageVersion();
        }

        /// <summary>The JSON form <see cref="CopySettings"/>/<see cref="Store"/> copy through; also what the window compares to decide whether it is Modified.</summary>
        internal static string ToJson(CityGeneratorSettings source)
        {
            return EditorJsonUtility.ToJson(source);
        }

        /// <summary>
        /// Deep copy via <see cref="EditorJsonUtility"/>, which keeps prefab/clip/InputActionAsset
        /// references as asset references. Fields missing from the JSON (an asset written by an
        /// older package version) keep the initializer defaults of the fresh object they are
        /// written over, and <c>FormerlySerializedAs</c> renames resolve as for any serialized data.
        /// </summary>
        internal static CityGeneratorSettings Clone(CityGeneratorSettings source)
        {
            var copy = new CityGeneratorSettings();
            if (source != null)
                EditorJsonUtility.FromJsonOverwrite(ToJson(source), copy);
            return copy;
        }

        /// <summary>The installed package's version, or null if it can't be resolved.</summary>
        internal static string CurrentPackageVersion()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CityGeneratorSettingsAsset).Assembly);
            return packageInfo?.version;
        }

        /// <summary>
        /// True if this asset was written by a newer package version than the one installed: it
        /// still loads (unknown fields are ignored), but the caller warns that settings only the
        /// newer version understands were dropped.
        /// </summary>
        internal bool IsFromNewerPackageVersion()
        {
            return CompareVersions(packageVersion, CurrentPackageVersion()) > 0;
        }

        /// <summary>Logs the "written by a newer version" warning when it applies.</summary>
        internal void WarnIfFromNewerPackageVersion()
        {
            if (!IsFromNewerPackageVersion())
                return;

            Debug.LogWarning(
                $"[City Generator] '{name}' was saved with City Generator {packageVersion}, newer than the installed " +
                $"{CurrentPackageVersion()}. It was loaded, but any setting this version doesn't know about was ignored.",
                this);
        }

        /// <summary>Compares two "major.minor.patch[-suffix]" strings; unparsable or missing versions compare as equal.</summary>
        internal static int CompareVersions(string a, string b)
        {
            if (!TryParseVersion(a, out Version versionA) || !TryParseVersion(b, out Version versionB))
                return 0;
            return versionA.CompareTo(versionB);
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
                return false;

            int suffixIndex = text.IndexOfAny(new[] { '-', '+' });
            string core = suffixIndex >= 0 ? text.Substring(0, suffixIndex) : text;
            return Version.TryParse(core, out version);
        }

        /// <summary>
        /// Writes <paramref name="source"/> to the per-scene asset of <paramref name="scenePath"/>
        /// (<see cref="CityGeneratorSettingsAssetPaths.ForScene"/>), overwriting it in place when it
        /// already exists so it keeps its GUID across Re-Builds. Returns null, writing nothing, for
        /// an unsaved scene. Not recorded in Undo: like the minimap PNG, it is generation output.
        /// </summary>
        internal static CityGeneratorSettingsAsset WriteForScene(CityGeneratorSettings source, string scenePath)
        {
            string assetPath = CityGeneratorSettingsAssetPaths.ForScene(scenePath);
            if (assetPath == null)
                return null;

            CityGeneratorSettingsAssetPaths.EnsureSceneAssetFolder(scenePath);
            return WriteAt(source, assetPath);
        }

        /// <summary>
        /// Stores <paramref name="source"/> in the settings asset at <paramref name="assetPath"/>,
        /// reusing an existing one (same GUID) or creating it. Anything else already at that path
        /// is replaced, as <see cref="AssetDatabase.CreateAsset"/> does.
        /// </summary>
        internal static CityGeneratorSettingsAsset WriteAt(CityGeneratorSettings source, string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<CityGeneratorSettingsAsset>(assetPath);
            if (asset != null)
            {
                asset.Store(source);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                return asset;
            }

            asset = CreateInstance<CityGeneratorSettingsAsset>();
            asset.Store(source);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }
    }
}
