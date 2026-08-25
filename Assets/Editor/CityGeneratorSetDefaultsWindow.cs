using CityGenerator.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development-repo-only tooling: captures whatever is currently assigned in an open
/// CityGeneratorWindow and writes it back as the tool's new default, by editing the package's
/// own source files (CityGeneratorDefaultAssets.cs / CityGeneratorSettings.cs). Lives outside
/// Packages/com.santiandrade.citygenerator/ on purpose — it rewrites the package's own source,
/// which only makes sense in this development repo, never in a project that merely installed
/// the package. Reads CityGeneratorWindow.settings via the package Editor assembly's
/// InternalsVisibleTo (see Packages/com.santiandrade.citygenerator/Editor/AssemblyInfo.cs).
/// </summary>
internal static class CityGeneratorSetDefaultsWindow
{
    [MenuItem("Tools/City Generator/Set Current Selection As Default")]
    private static void SetCurrentSelectionAsDefaultMenuItem()
    {
        CityGeneratorWindow window = FindOpenWindow();
        if (window == null)
        {
            EditorUtility.DisplayDialog(
                "City Generator",
                "Open the City Generator window first (Tools > City Generator > Open) so there is a current selection to save.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "City Generator - Set Current Selection As Default",
            "This overwrites the tool's default settings (prefabs, counts, densities...) with what is currently assigned in the open City Generator window, by editing the package's own source files. This cannot be undone with Ctrl+Z.",
            "Save as Default",
            "Cancel");
        if (!confirmed)
            return;

        CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault(window.settings);
        EditorUtility.DisplayDialog("City Generator", "Current selection saved as the new default.", "OK");
    }

    private static CityGeneratorWindow FindOpenWindow()
    {
        CityGeneratorWindow[] windows = Resources.FindObjectsOfTypeAll<CityGeneratorWindow>();
        return windows.Length > 0 ? windows[0] : null;
    }
}
