# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Replaced the "Plaza Count" field with direct plaza placement: click a block in the grid preview
  to toggle it as a plaza (`GeneralSettings.plazaCells`, a `List<Vector2Int>` of block coordinates,
  replaces the old `plazaCount` int). The preview now always matches the generated scene exactly —
  previously, with more than one plaza, `CityGeneratorGrid` picked blocks at random
  (`System.Random`) while the preview only approximated the picture with a reading-order stand-in,
  so the two could disagree. `CityGeneratorGrid.BuildBlocks` no longer takes a `System.Random`
  parameter, since plaza placement is no longer randomized.

- Rebuilt the City Generator Editor window in UI Toolkit (UXML/USS), replacing the previous
  IMGUI layout entirely: a non-stretched banner, collapsible cards per section (state persisted
  in `EditorPrefs`) with a live summary badge, thumbnail grids for the Building/Vegetation prefab
  lists, a percentage-weighted list with a stacked bar and a "Normalize to 100%" button for the
  Vehicles/Pedestrians lists, and a top-down grid/plaza preview with an estimated build summary.
  Validation now runs continuously as settings change (`CityGeneratorValidator.ValidateDetailed`)
  instead of only on Build: invalid fields and their card are highlighted live, the problem list
  shows in the footer, and the Build buttons stay disabled until every issue is fixed. Generation
  now reports coarse per-phase progress through an `EditorUtility.DisplayProgressBar` (new optional
  `onProgress` parameter threaded through `CityGeneratorContentAssembler.Assemble`,
  `CityGeneratorSceneBuilder.BuildAndSaveScene`/`RebuildInActiveScene` and
  `CityGeneratorWindow.GenerateCity` — all additive, default `null` behaves exactly as before), and
  the result is shown in an in-window panel (with a "Ping Scene" button) instead of a blocking
  `EditorUtility.DisplayDialog`. No change to generation behaviour or output.

## [1.4.0] - 2026-08-24

### Added

- Autonomous pedestrian network, mirroring the vehicle traffic system: `PedestrianNetwork`
  (an undirected graph — an 8-node sidewalk ring per block plus a curb/crossing/curb chain at
  every interior intersection, aligned to the real zebra crossings and matched to the actual
  `TrafficLightIntersection` in the scene) and `PedestrianAgent`/`PedestrianManager` (walk/wait/
  idle state machine, ticked centrally like `CarAgent`/`TrafficManager`, with a spatial-grid local
  separation nudge between nearby NPCs). Configurable from the tool window ("Include Pedestrians",
  "Pedestrian Count", a percentage-weighted prefab list) exactly like vehicles, with the same
  three-level pruning (generation-time obstacle avoidance, an `Awake`-time auto-repair pass, and
  an explicit re-bake via `[ContextMenu]`/`Tools > City Generator > Rebuild Pedestrian Network`).
  Vehicles now brake for detected pedestrians too, via a `CarAgent.pedestrianMask` and a second
  forward sensor independent of `vehicleMask` — reusing the existing "vehicle ahead" braking
  branch rather than a new state. The 12 `DefaultAssets/Prefabs/Characters/` prefabs are the
  default pedestrian list (~8.33% each), on top of remaining available as Player Prefab candidates.
- `TrafficNetwork.IsAxisGreen` and `TrafficLightIntersection.EastWestState`/`NorthSouthState`:
  read an intersection's light state for a given axis without re-scanning the scene for
  `TrafficLight` instances, so `PedestrianNetwork` can decide when a crossing is safe.
- `CityGeneratorDistributionUtility.DistributePercentages`: the percentage-to-count distribution
  logic used by vehicles, extracted into a shared, generic utility so pedestrians reuse it too.

### Changed

- A grid with `gridWidth == 1` or `gridHeight == 1` has no interior intersections, so pedestrians
  can't cross between blocks — each block's sidewalk ring is isolated. The tool window now warns
  about this (non-blocking), same as the existing vehicle density warning.
- The player is now placed on the same Pedestrian layer as NPC pedestrians, so vehicles brake for
  it exactly like they do for a pedestrian, regardless of whether "Include Pedestrians" is on.
  Pedestrian NPCs' `BoxCollider` is no longer a trigger, so the player's `CharacterController` now
  physically collides with them too, matching how it already collides with vehicles (pedestrians
  themselves are unaffected — `PedestrianAgent` moves by transform and never gets pushed back).

### Known limitations

- A user-supplied building/prop prefab with no `Collider` in its hierarchy isn't detected by the
  pedestrian network's `Awake`-time auto-repair pruning (`Physics.CheckSphere`-based); it still
  gets avoided at generation time via the shared obstacle list.

## [1.3.0] - 2026-08-24

### Added

- 12 selectable Player Prefab characters (`Character-Male-A` through `-F`,
  `Character-Female-A` through `-F`), replacing the single hardcoded `Player`
  prefab. Each is a clean model+Animator prefab (`CharacterAnimator.controller`,
  shared across all 12) with no movement setup baked in — `CharacterController`
  and `PlayerController` are now added by `CityGeneratorSceneBuilder` at
  generation time, with hardcoded default tuning, to whichever prefab is
  assigned, so any of the 12 (or a user-supplied one) works without extra setup.
- `CityGeneratorPlayerSpawner`: picks the player's spawn position inside a plaza
  block when the city has one (random order across plazas), or a random block
  otherwise, checked against every already-placed building/plaza solid/prop/
  vegetation instance so the player never spawns overlapping them.

### Changed

- Default `general.playerPrefab` is now `Character-Male-D` (previously `Player`).
- The 10 mobility-aid models (canes, crutches, masks, glasses, hearing aid,
  defibrillators) and 4 wheelchair models under `Assets/Models/Characters/`,
  never referenced by any demo prefab, have been removed from the repo rather
  than kept as orphans.

## [1.2.0] - 2026-08-21
### Added

- 12 new demo prefabs: 10 buildings (`Building-B/C/D/E/G/H/L` and
  `Building-Skyscraper-A/B/D`) and 11 vehicles (`Ambulance`, `Delivery-Flat`,
  `Firetruck`, `Garbage-Truck`, `Hatchback-Sports`, `Sedan`, `Suv`, `Suv-Luxury`,
  `Truck`, `Truck-Flat`, `Van`), each vehicle with its own `CarAgent` tuning.
- `Tools > City Generator > Set Current Selection As Default`: captures whatever is
  currently assigned in an open City Generator window and writes it back as the
  tool's new default (prefabs, counts, densities...), so the next window and
  "Reset to Defaults" both open with it.

### Changed

- The tool's default settings now reflect the full demo prefab lineup above: all 16
  building prefabs, all 15 vehicle prefabs (with rebalanced percentages), a 5x5 grid
  and a vehicle count of 80.

### Fixed

- 4 of the demo vehicle prefabs (`DeliveryCar`, `PoliceCar`, `SedanSportCar`,
  `TaxiCar`) had lost their baked `CarAgent` component while being reassigned to
  new base models; the 11 new vehicles never had one either. Without it,
  `CityGeneratorTrafficBuilder.BuildVehicles` fell back to adding a fresh `CarAgent`
  with the script's own defaults, so every generated vehicle drove identically
  regardless of type. Restored/added on all 15 prefabs.

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
