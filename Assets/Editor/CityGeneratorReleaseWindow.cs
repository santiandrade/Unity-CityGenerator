using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development-repo-only tooling: bumps the package version and CHANGELOG for a release.
/// Lives outside Packages/com.santiandrade.citygenerator/ on purpose — it has nothing to do
/// once the package is installed in someone else's project. Never touches git; it only edits
/// package.json and CHANGELOG.md and shows the tag command for the user to run themselves,
/// so a release tag is never created by accident.
/// </summary>
internal class CityGeneratorReleaseWindow : EditorWindow
{
    private const string PackageRelativePath = "Packages/com.santiandrade.citygenerator";
    private static readonly Regex VersionFieldRegex = new(@"""version""\s*:\s*""(\d+)\.(\d+)\.(\d+)""");
    private static readonly Regex UnreleasedHeadingRegex = new(@"^## \[Unreleased\]\s*$", RegexOptions.Multiline);

    private enum BumpKind
    {
        Major,
        Minor,
        Patch
    }

    private BumpKind bumpKind = BumpKind.Patch;
    private (int major, int minor, int patch) currentVersion;
    private bool versionFound;

    [MenuItem("Tools/City Generator/Release")]
    private static void Open()
    {
        GetWindow<CityGeneratorReleaseWindow>(true, "City Generator - Release");
    }

    private void OnEnable()
    {
        ReadCurrentVersion();
    }

    private void OnGUI()
    {
        if (!versionFound)
        {
            EditorGUILayout.HelpBox($"Could not read a version from {PackageJsonPath()}.", MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("Current version", FormatVersion(currentVersion));

        EditorGUILayout.Space();
        bumpKind = (BumpKind)EditorGUILayout.EnumPopup("Bump", bumpKind);

        (int major, int minor, int patch) next = NextVersion(currentVersion, bumpKind);
        EditorGUILayout.LabelField("New version", FormatVersion(next));

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply", GUILayout.Height(28f)))
        {
            ApplyRelease(next);
        }
    }

    private void ApplyRelease((int major, int minor, int patch) next)
    {
        string versionString = FormatVersion(next);
        WritePackageVersion(next);
        WriteChangelogEntry(versionString);
        ReadCurrentVersion();

        string tagCommand = $"git tag v{versionString} && git push origin v{versionString}";
        EditorGUIUtility.systemCopyBuffer = tagCommand;
        EditorUtility.DisplayDialog(
            "City Generator - Release",
            $"package.json and CHANGELOG.md updated to {versionString}.\n\n" +
            "This tool does not run git. Run this command yourself to tag and push the release " +
            "(already copied to your clipboard):\n\n" + tagCommand,
            "OK");
    }

    private static (int major, int minor, int patch) NextVersion((int major, int minor, int patch) current, BumpKind kind)
    {
        return kind switch
        {
            BumpKind.Major => (current.major + 1, 0, 0),
            BumpKind.Minor => (current.major, current.minor + 1, 0),
            _ => (current.major, current.minor, current.patch + 1),
        };
    }

    private static string FormatVersion((int major, int minor, int patch) version) =>
        $"{version.major}.{version.minor}.{version.patch}";

    private void ReadCurrentVersion()
    {
        versionFound = false;
        string path = PackageJsonPath();
        if (!File.Exists(path))
            return;

        Match match = VersionFieldRegex.Match(File.ReadAllText(path));
        if (!match.Success)
            return;

        currentVersion = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        versionFound = true;
    }

    private static void WritePackageVersion((int major, int minor, int patch) next)
    {
        string path = PackageJsonPath();
        string text = File.ReadAllText(path);
        text = VersionFieldRegex.Replace(text, $"\"version\": \"{FormatVersion(next)}\"", 1);
        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(ToAssetPath(path));
    }

    private static void WriteChangelogEntry(string versionString)
    {
        string path = ChangelogPath();
        string text = File.ReadAllText(path);

        string date = DateTime.Now.ToString("yyyy-MM-dd");
        string replacement = $"## [Unreleased]\n\n## [{versionString}] - {date}";
        text = UnreleasedHeadingRegex.Replace(text, replacement, 1);

        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(ToAssetPath(path));
    }

    private static string PackageJsonPath() => Path.Combine(PackageAbsolutePath(), "package.json");
    private static string ChangelogPath() => Path.Combine(PackageAbsolutePath(), "CHANGELOG.md");

    private static string PackageAbsolutePath()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot!, PackageRelativePath).Replace('\\', '/');
    }

    private static string ToAssetPath(string absolutePath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
        return absolutePath.Replace('\\', '/').Replace(projectRoot + "/", "");
    }
}
