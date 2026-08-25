using System.Runtime.CompilerServices;

// Lets Assets/Editor/CityGeneratorSetDefaultsWindow.cs (this repo's own dev tooling, outside the
// package, deliberately kept out of the distributable package — see CLAUDE.md's "Tooling
// internal" convention) read CityGeneratorWindow.settings and call
// CityGeneratorDefaultAssetsWriter.SaveCurrentAsDefault. Every other type in this assembly stays
// internal/private exactly as before: this is the one visibility change needed for the moved
// "Set Current Selection As Default" command to keep working.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
