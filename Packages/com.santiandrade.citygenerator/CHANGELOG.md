# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-08-20

### Added

- `CityGeneratorTrafficBuilder` now creates the `Vehicle` layer itself, the first time
  it generates traffic in a project that doesn't have one, using the first free layer
  slot (8-31) and logging that it did so. You no longer need to create it by hand.

### Fixed

- `CarAgent.vehicleMask` is now recomputed for every vehicle instance at generation
  time (`1 << instance.layer`), instead of relying on the value baked into the vehicle
  prefab's own serialized data. The baked value only happened to work when `Vehicle`
  landed on the same layer index the prefab was authored against (index 8, for the
  demo prefabs) — on any other index, vehicles would silently stop detecting each
  other, with no warning.
- `BuildVehicles` no longer logs a console warning for high vehicle density — the
  live `HelpBox` in `CityGeneratorWindow` already surfaces it before you click
  generate, and repeating it in the console after the fact added nothing.

### Changed

- If every layer slot is already taken and the `Vehicle` layer can't be auto-created,
  vehicles now stop detecting each other entirely (`vehicleMask` is left at `0`)
  instead of falling back to whatever layer their prefab happens to share with
  unrelated scene geometry. They still stop for traffic lights and unsignalled-crossing
  priority.

## [1.0.2] - 2026-08-20

### Added

- `licensesUrl` in `package.json`, pointing at `LICENSE.md` on GitHub. Without it,
  Unity Package Manager's "Licenses" link opened the local file through the OS file
  explorer instead of a browser, unlike the "Documentation" and "Changelog" links.

### Fixed

- Root README installation instructions no longer hardcode a version tag in the
  primary install URL — every past release required editing the README to keep the
  example current. The untagged URL (tracks the default branch) is now the default
  instruction, with pinning to a `#vX.Y.Z` release documented as an option that
  links to the [Releases page](https://github.com/santiandrade/Unity-CityGenerator/releases)
  instead of a hardcoded number.

## [1.0.1] - 2026-08-20

### Fixed

- `DefaultAssets/Input/InputSystem_Actions.inputactions` shipped with the same GUID
  Unity assigns to that asset in every new project created from the Input System
  template. Since the file lives in the package's immutable folder, Unity refused to
  reassign the GUID and silently ignored the asset, leaving `general.inputActions`
  unresolved (and the player/camera unable to read input) in any project that still
  has its own default `InputSystem_Actions.inputactions`. Reassigned a fresh GUID to
  the package's copy. **If you installed v1.0.0, reinstall with `#v1.0.1`** — this
  affects most default Unity projects.

## [1.0.0] - 2026-08-20

### Added

- First release of City Generator as an installable Unity package
  (`com.santiandrade.citygenerator`), embedding the tool's Runtime and Editor code,
  demo prefabs and runtime components — installable from Package Manager via
  "Install package from git URL".
- Demo content bundled inside the package under `DefaultAssets/`: the 22 sample
  prefabs (buildings, floors, props, vegetation, vehicles, player), the 14 URP/Lit
  materials, extracted ProBuilder meshes, `PlayerAnimator.controller` and
  `InputSystem_Actions.inputactions`, so the tool window opens with every field
  filled in right after installing.
- Root `README.md` / `README.es.md` covering installation, updating, requirements,
  demo content, requirements for user-supplied prefabs, recommended project
  settings, traffic scaling and render pipeline notes.
- `Tools > City Generator > Release` editor window (outside the package) to bump
  `package.json`'s version and roll this changelog for future releases.
- Only the model files a demo prefab or `PlayerAnimator.controller` actually
  references travel inside the package: 6 buildings, 4 vehicles, `character-male-d.fbx`
  and the fountain `.glb`, plus the three `Textures/colormap.png` atlases they share
  with the categories below (copied in, not moved, so the orphans keep working).
  Everything else from the same asset packs is **not** part of the package and stays
  in this repository's `Assets/Models/` for future use, decided by
  `AssetDatabase.GetDependencies` rather than by inspection:
  - `Buildings/` — 35 unreferenced models (extra buildings, low-detail variants,
    awnings, overhangs, parasols).
  - `Cars/` — 46 unreferenced models (other vehicles, wheels, debris parts).
  - `Characters/` — 25 unreferenced models (other characters, mobility aids,
    wheelchairs).
  - `Pets/` — 24 unreferenced models (none of this category is used by any demo
    prefab).
