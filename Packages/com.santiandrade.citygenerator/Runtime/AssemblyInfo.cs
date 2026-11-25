using System.Runtime.CompilerServices;

// SPEC 23: CityGeneratorContentAssembler (Editor) drops CityGeneratorInfo's cached district index
// after rewriting its districts; Assets/Tests/{EditMode,PlayMode} (this repo's own dev tooling,
// outside the package, same convention as Editor/AssemblyInfo.cs) exercise the internal district
// lookup (TryGetCell/TryGetDistrictIndex) directly. Every other member of this assembly keeps its
// visibility exactly as before.
[assembly: InternalsVisibleTo("CityGenerator.Editor")]
[assembly: InternalsVisibleTo("CityGenerator.Tests.EditMode")]
[assembly: InternalsVisibleTo("CityGenerator.Tests.PlayMode")]
