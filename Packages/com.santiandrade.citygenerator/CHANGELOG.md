# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Minimap HUD: an optional (on by default) circular minimap in the top-left corner, showing a
  static top-down snapshot of the generated city centred on the player in real time, with Custom
  Places marked as Point of Interest labelled by name. Configured in a new "Minimap" tab
  (Enabled/Texture Resolution/View Radius). The snapshot is captured once during generation and
  saved as a PNG asset next to the generated scene (e.g. `Assets/Scenes/City1_Minimap.png`).


### Fixed

- Custom Places grid picker: the block/quadrant you clicked in the picker no longer lands on the
  wrong row once the city is generated. The picker's row axis was drawn opposite to how a generated
  city reads in a top-down view (e.g. Unity's own Top Scene View), so entries away from the grid's
  middle row landed one or more rows off from where they were placed; column position and the
  plaza-cell picker (same component) had the same row flip.

## [2.2.1] - 2026-08-27

### Changed

- The Custom Places card moved from its own "Custom Places" tab into the "City" tab (as its last
  card). The tab it used to live on is gone; the tool now has three tabs (City/Player/Pedestrians)
  instead of four.

## [2.2.0] - 2026-08-26

### Added

- **Custom Places**: a new "Custom Places" tab lets you define manually-placed entries (title,
  prefab, a block/quadrant chosen from a per-entry grid picker, and a fixed 90-degree orientation)
  that are instantiated instead of a random building at that position. A whole-block entry excludes
  all 4 corners of its block from the random building distribution; a quarter-block entry excludes
  only its own corner. Every Custom Place participates in the same shared obstacle list as other
  placed content, so props/vegetation never overlap it. Validated the same way as every other list
  in the tool: missing title/prefab, no position assigned, a plaza-block target, a slot conflict
  between two entries, or two entries sharing the same title all block generation with an inline
  error.
- A new demo model, **Hospital**, ships as a whole-block Custom Places entry in the default
  settings (and in the demo scene), showing off the feature out of the box instead of leaving the
  Custom Places tab empty on a fresh install.

### Removed

- The pedestrian network's Point of Interest machinery (`PedestrianNodeKind.PointOfInterest`,
  `PointOfInterestDescriptor`, `RegisterPointOfInterest`/`ConnectPointOfInterest`, and the
  bench/centerpiece POI registration previously done by `CityGeneratorPedestrianBuilder`) has been
  removed entirely. Pedestrians no longer walk to and linger at benches/the plaza centerpiece —
  those props are still generated exactly as before, just no longer wired into the pedestrian
  graph. `PedestrianBehaviourSettings.poiStopDurationMin`/`poiStopDurationMax` were removed along
  with it.

## [2.1.0] - 2026-08-26

### Added

- A test suite (`Assets/Tests/`, outside the package): EditMode tests for the pedestrian network's
  `CanCross` rules, connected-component routing and `PedestrianRoadProximityGrid`; PlayMode tests
  for `TrafficLightIntersection`'s phase cycle, manager registration/deregistration and
  collider-on-child detection; and Performance tests measuring a baseline generation and runtime
  frame cost. Baseline/delta measurements are recorded in `specs/05-performance-and-tests.md`.

### Changed

- `CityGeneratorPlacementEngine`'s overlap check now queries a spatial hash
  (`CityGeneratorSpatialHash`) over the shared obstacles list instead of scanning it linearly per
  candidate (measured -17.2% total generation time on a 10x10 grid).
- `Physics.SyncTransforms` moved from `TrafficNetwork` to `TrafficManager.Update`, and is only
  called when at least one `CarAgent` is registered.
- `CarAgent`'s forward vehicle sensor now scans an `OverlapSphere` centred on the car's own
  position instead of a `SphereCast` from a point offset ahead of it, closing a blind spot where a
  long vehicle (a `Truck`/`Garbage-Truck`) a couple of metres ahead could go undetected; the new
  `TrafficLaneOccupancy` fast path resolves "car ahead in the same lane segment" without a
  `SphereCast` at all, measuring the gap to the other car's actual collider surface (via its new
  `OwnCollider` property) instead of centre-to-centre, so a queue of cars no longer settles with
  their bodies visibly overlapping.
- `PedestrianNetwork` now computes connected components so `PedestrianAgent.PlanNewDestination`
  never attempts a route to an unreachable part of the graph; each agent's initial destination
  planning is staggered by spawn index instead of every agent planning on the same frame;
  `FindPath` caches the full BFS tree per origin instead of recomputing it for every candidate
  destination tried; and `PedestrianAgent` rents its path buffer from a shared
  `PedestrianPathBufferPool` instead of keeping a permanent per-agent array sized to the whole
  graph. `PedestrianManager`'s local-separation pass now processes each agent pair once instead of
  twice and skips pairs where neither side is due for a recalculation this frame.
- `CarAgent` can now query nearby pedestrians through `PedestrianRoadProximityGrid` instead of its
  own `SphereCast`, once the pedestrian count justifies it (same staggering threshold as vehicle
  detection). `PedestrianManager` feeds the grid the player's position separately from registered
  `PedestrianAgent` instances, since the player is on the same `Pedestrian` layer without being one
  — without this, once the pedestrian count crossed the staggering threshold, vehicles stopped
  detecting and braking for the player.

### Fixed

- `CarAgent`'s "ahead" direction is now the vector toward its own current target node instead of
  `transform.forward`: mid-corner, `RotateTowards` keeps a car's heading lagging behind its actual
  path for the whole turn, which could put another car rounding the same corner a few metres away
  outside the forward cone.
- Fixed an `IndexOutOfRangeException` when a plaza's `PointOfInterest` node was registered after
  `PedestrianNetwork.Build()`, growing the node list without keeping the connected-components array
  in sync (regression introduced while implementing the connected-components routing above; caught
  by the new runtime performance tests and covered by a regression test).

## [2.0.0] - 2026-08-25

### Changed

- **Breaking:** removed `PerformanceBootstrap` (the `[RuntimeInitializeOnLoadMethod]` that forced
  `vSyncCount = 0`/`targetFrameRate = 60` on any scene in a project consuming this package). No
  opt-in replacement is provided; a consuming project that relied on this implicit behaviour
  should set its own frame rate/VSync preference.
- "Re-Build City in Current Scene" is now transactional: the new city is built under a temporary
  root and only replaces the previous one (found via the new `CityGeneratorRoot` marker, not by
  GameObject name) once generation finishes without error, undoable in a single Ctrl+Z. If
  generation fails partway through, the previous city is left completely intact and the error is
  shown in the window's result panel.
- The generated sensor collider on vehicle/pedestrian instances (`CityGeneratorColliderUtility`)
  is now always added to the instance root (reusing one already there) instead of leaving a
  user prefab's collider wherever it happens to sit in the hierarchy — a collider only present on
  a child was previously invisible to `CarAgent`'s/other agents' layer-filtered sensors.
- `TrafficManager`/`PedestrianManager` are no longer scene-global singletons (`Instance` removed):
  `CarAgent`/`PedestrianAgent` resolve their manager through `TrafficNetwork.Manager`/
  `PedestrianNetwork.Manager` instead, so multiple generated cities coexisting in the same scene
  no longer fight over a single manager.
- Player input is now owned by a single new `PlayerInputAuthority` component (added to the
  generated Player instance alongside `PlayerController`); `PlayerController` and
  `ThirdPersonCamera` no longer each call `Enable()`/`Disable()` on the shared action map.
  `ThirdPersonCamera` now also restores cursor lock state/visibility in `OnDisable`.
- Validation (`CityGeneratorValidator`) now catches several previously-silent misconfigurations
  (zero/equal walk-run speeds, negative radii/durations, a typo'd input action name, an
  inconsistent `CharacterController` tuning, `Include Traffic` no longer gates the Traffic Light
  requirement — grid geometry does) and warns (without blocking generation) about empty prefab
  list entries and prefabs with no `Renderer` in their hierarchy.
- A plaza's `PointOfInterest` nodes (bench/fountain stops) now survive the Awake -> Build() cycle
  in Play and an explicit `Tools > City Generator > Rebuild Pedestrian Network` re-bake, instead of
  disappearing the moment the pedestrian network graph rebuilds.
- `Tools > City Generator > Set Current Selection As Default` moved from the package's
  `CityGeneratorWindow` to this repo's own `Assets/Editor/CityGeneratorSetDefaultsWindow.cs` —
  dev-repo-only tooling that has nothing to do once the package is installed elsewhere.

## [1.6.0] - 2026-08-25

### Added

- The City Generator window now has three tabs — **City**, **Player** and **Pedestrians** —
  instead of a single scrolling list of cards. The Player tab exposes the Player Prefab/Input
  Actions fields plus a new **Player** card (movement, `CharacterController` tuning, input action
  names) and **Camera** card (`ThirdPersonCamera` orbit/collision tuning, field of view). The
  Pedestrians tab keeps the existing weighted prefab list and adds a **Behaviour** card (pace,
  jitter, stop durations, animation reference speeds) and a **Crowd** card (`PedestrianManager`
  separation/avoidance/performance staggering). All of these were previously hardcoded in
  `CityGeneratorConstants` or left at the runtime scripts' own C# defaults, invisible from the
  window.
- Every field in the window now has a tooltip explaining what it does, so the tool is
  self-documenting without needing to read the source.

### Changed

- Player and pedestrian tuning is now applied unconditionally to the generated instance, never to
  the prefab asset: even a Player Prefab or pedestrian prefab that already carries its own
  `PlayerController`/`CharacterController`/`PedestrianAgent` gets its values overwritten with
  whatever the window's Player/Pedestrians tabs specify, so the tool's settings are always the
  single source of truth for what ends up in the generated scene.

## [1.5.1] - 2026-08-25

### Fixed

- Unified the collider policy for generated vehicle and pedestrian instances into a shared
  `CityGeneratorColliderUtility.EnsureNonTriggerCollider`, called from both
  `CityGeneratorTrafficBuilder.BuildVehicles` and `CityGeneratorPedestrianBuilder.BuildPedestrians`.
  Pedestrians previously got a trigger `BoxCollider` unconditionally; now, like vehicles, an
  existing `Collider` anywhere in the prefab's hierarchy is kept as-is (only forced to
  `isTrigger = false`), and a non-trigger `BoxCollider` is added only when the prefab has none.
  This lets the player's `CharacterController` physically collide with pedestrians and lets
  vehicles detect them, instead of walking/driving through.
- `PedestrianAgent` no longer spams a console warning every frame for a pedestrian prefab with no
  `Animator`, or an `Animator` with no controller assigned — it now checks a cached
  `hasAnimatorController` flag before calling `SetFloat`/`SetBool`, and still walks normally
  without animation.
- Nudged `PlayerControllerCenter` (`CharacterController.center`) from `0.36` to `0.4` to better
  match the default character prefabs' pivot.

## [1.5.0] - 2026-08-24

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
