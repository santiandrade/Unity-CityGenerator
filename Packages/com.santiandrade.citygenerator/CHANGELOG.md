# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Fixed

- **Traffic light validation now matches what the builder actually places.** The validator asked for
  a Traffic Light prefab only on a grid larger than 1x1 (or a custom shape containing a full 2x2 of
  cells), while `CityGeneratorTrafficBuilder` has signalled every intersection with at least 3 real
  arms since SPEC 11 — so a 1xN/Nx1 grid, or a custom shape with a T-intersection, passed validation
  with the prefab left empty and then failed generation with a `NullReferenceException` from
  instantiating a null prefab. Both sides now share one predicate
  (`CityGeneratorTrafficBuilder.HasSignalledIntersection`), pinned to the builder by
  `SignalledIntersectionAgreementTests`. The requirement is independent of `Include Traffic`, since
  the lights are generated even with traffic off.
- The Pedestrians tab's "isolated blocks" warning used that same stale rule and so wrongly claimed a
  1xN/Nx1 city had no crossings (a 1x2 city actually gets 6 lights, 6 crossings and a single
  connected pedestrian component); it also ignored Custom Grid entirely. It now asks the shared
  predicate and covers both grid modes.

## [2.10.0] - 2026-09-03
### Added

- New Runtime API (`CityGeneratorAPI`, `Packages/.../Runtime/API/`): a static, module-per-tab
  (`City`, `Player`, `Traffic`, `Pedestrians`, `Minimap`, `Audio`) read entry point for a generated
  city's data, working the same in Editor Play Mode and in a player build. `IsCityAvailable` and
  every getter degrade to a safe default (0/false/`Vector2Int.zero`/`Vector3.zero`/null) instead of
  throwing when no city is active. A small number of setters already safe at runtime are exposed:
  `City.SetDayNightEnabled`/`SetHour`, `Minimap.SetVisible`/`SetViewRadiusMeters`. Backed by a new
  `CityGeneratorInfo` runtime component that `CityGeneratorSceneBuilder`/`CityGeneratorContentAssembler`
  populate on every Build/Re-Build. See `docs/api-reference.md`. `CityGeneratorInfo`'s Inspector is
  now read-only (`Editor/CityGeneratorInfoEditor.cs`) since it's a build-time snapshot, not a live
  control — hand-editing its fields never had any effect on the generated city.

## [2.9.0] - 2026-09-02
### Added

- New "Free Camera" card (Player tab): a "Free View" mode toggled with the V key, replacing the
  Player and its third-person camera with a free-flying first-person camera (WASD + Q/E vertical,
  Shift to sprint, mouse-look with smoothed rotation, basic collision against scene colliders).
  Toggling again restores the Player exactly where it was and the third-person camera resumes
  orbiting it. Backed by a new "Free View" Input Actions map and a `Toggle` action added to the
  existing `Player` map, both editable like the rest of the project's input actions. Ignored (no
  Free Camera added, no error) when Player is disabled.

## [2.8.0] - 2026-09-02
### Added

- New "Custom Pedestrians" card (Pedestrians tab): per entry (prefab + count), trace a network of
  pedestrian nodes by hand on a new picker and the generated agents of that entry are confined to
  walking only within that network instead of the whole city. The picker groups the real pedestrian
  graph into clickable line zones (a Ring edge, an Interior spoke, a crossing — coloured by kind,
  selected zones highlighted) rather than one point per node, since a normal block's 13+ individual
  nodes were too small/dense to click reliably. It shows the real pedestrian graph before the city
  is ever generated, via a disposable preview that reuses the same generation code the real
  pipeline uses. `count` is a budget independent of the general Pedestrian Count, and changing the
  grid/Custom Grid/plazas/Custom Places after tracing a route invalidates and clears it instead of
  silently generating over the wrong nodes.
- New `Pets/` demo prefabs (`Animal-Cat`, `Animal-Dog`) usable as Custom Pedestrian entries.

### Fixed

- A pedestrian prefab whose model has no `SkinnedMeshRenderer` (e.g. `Pets/`'s rigid per-limb
  `MeshRenderer`s) no longer freezes its Animator permanently: `Animator.cullingMode` is now chosen
  per generated instance (`Cull Completely` only when a `SkinnedMeshRenderer` is present, `Always
  Animate` otherwise) instead of always forcing `Cull Completely`, which never resolves visibility
  for that rig shape. Also enabled `Loop Time`/`Loop Pose` on `animal-cat.fbx`/`animal-dog.fbx`'s
  locomotion clips, which defaulted to off and froze the walk cycle on its last frame after under a
  second.
- Removed `CityGeneratorPedestrianBuilder.PruneNodesAgainstObstacles` ("level 1" pedestrian node
  pruning): it blocked a node whenever it fell inside an obstacle's full renderer-bounds rect
  (`ObstacleCache.GetRect`), which for anything with a visual element sticking out further than its
  solid footprint (a balcony/canopy/sign arm, a `Collider`-less lawn/vegetation patch) is far wider
  than what actually blocks a pedestrian — found via SPEC 12 QA: ~35% of a generated city's nodes
  came back `Blocked` from this check alone, though a fresh `Physics`-based `PrunePlacedObstacles()`
  pass against the same scene found zero real overlaps. A first fix narrowed the rect check to
  `Collider`-less obstacles only, but this project's own demo vegetation is itself `Collider`-less
  by design, so it kept over-blocking identically — pedestrian obstacle avoidance is now purely
  physics-based (`PrunePlacedObstacles`'s `Physics.CheckSphere`, already running automatically at
  the end of every graph build): an obstacle with no `Collider` anywhere in its hierarchy is no
  longer treated as blocking pedestrians at all. If a `Collider`-less asset should block pedestrian
  routes, give it a `Collider`.
- Fixed the actual cause of a Custom Pedestrians entry intermittently spawning 0 instances on
  "Re-Build City in Current Scene" (roughly every other rebuild, worst with a small hand-traced
  route): the previous city stayed in the exact same world-space footprint as the one under
  construction for the whole call (it's only destroyed after generation succeeds, so a failed
  rebuild doesn't lose it), so two full sets of static colliders sat stacked exactly on top of each
  other — confirmed to make `PrunePlacedObstacles`'s downward ground raycast intermittently find no
  hit at all, wrongly marking most/all nodes `Blocked`. The previous city is now moved far aside
  before the new one is built (mirroring the minimap snapshot's own isolate-by-moving-the-root
  trick) and only destroyed after success, moved back on failure so the existing-city-survives
  guarantee still holds.

## [2.7.0] - 2026-09-01
### Added

- Custom Grid: every gap of a custom shape is now filled with a new "Empty Block Prefab (custom
  grids only)" ground slab (Ground card, defaulting to the same lawn prefab the plazas use and
  placed at the same height), so a custom city comes out as the plain rectangle of its own
  bounding box instead of ending in holes of empty space — a shape spanning 6 x 8 blocks generates
  a 6 x 8 city. The fill stops exactly at the outer edge of the perimeter sidewalk instead of
  covering it, and is a blocking validation error to leave unassigned while Customize mode is on.

- A generated city now always ends in sidewalk instead of in bare asphalt, on both the
  rectangular and the Custom Grid footprint: a 6 m sidewalk band is laid on the far side of every
  perimeter street, following the shape's own contour (the inner contour of a Custom Grid hole
  included), so that street has a walkable far side exactly like an interior one. The ground
  reaches 11 m past the outermost street axis instead of 6 m to carry it, which the road base, the
  minimap snapshot footprint and the window's size preview all follow automatically.
- The pedestrian network gained a walkway along that perimeter sidewalk, reached from the blocks
  through the crosswalk already painted at every border T-intersection -- which until now led
  nowhere, since a crosswalk arm with no block behind it was skipped. Its nodes are ordinary
  walkable `Ring` nodes, so pedestrians spawn on and walk to the perimeter like any other
  sidewalk, and the Pedestrians tab's density estimate accounts for them.
- Custom Grid: a "Customize" button on the General Options card's grid preview replaces the
  rectangular `Grid Width` x `Grid Height` footprint with an arbitrarily shaped, hand-edited
  poliomino (no islands) on a fixed 10x10 canvas. A "Define City Area" / "Define Plazas" selector
  switches the preview between adding/removing blocks (with live "+"/"-" affordances gated by
  contiguity) and toggling plazas on the real blocks only. Streets, sidewalks, road markings,
  traffic lights/network and the pedestrian network are all generated to match the shape's
  contour instead of a full rectangle; the minimap frames the shape's own bounding box. A Custom
  Place whose block is removed under it is flagged as a blocking validation error, same as an
  out-of-range block on a grid resize. Traffic lights and pedestrian crossings are placed at every
  intersection with at least 3 real street arms (a full 4-way or a T-intersection, including one
  on the shape's own border) — a plain 2-arm perimeter corner never gets one, since a car arriving
  there has only one possible way through. The vehicle/pedestrian density `HelpBox`es on the
  Traffic/Pedestrians tabs are shape-aware in Custom Grid mode instead of staying pinned to the
  grid's old `Grid Width`/`Grid Height` values.
- Custom Place entry pickers (City tab) overlay the General Options grid's configured plazas in
  green, as a visual reference for where plazas already sit while placing a Custom Place.
- Player > Player card gained an "Enabled" toggle (`general.playerEnabled`). Player Prefab and
  Input Actions are now only required when it's on, and no player is spawned when it's off, even
  if a Player Prefab is still assigned.

### Changed

- New "Player Settings" card on the Player tab, holding everything the Player card used to have
  except Player Prefab and Input Actions (which stayed on the Player card alongside the new
  Enabled toggle).
- New "Traffic" tab, between Player and Pedestrians: a Traffic card ("Enabled", "Vehicle Count")
  and a Vehicles card (the weighted vehicle prefab list), both moved out of the City tab's General
  card / vehicles card.
- New "Pedestrian Settings" card on the Pedestrians tab ("Enabled", "Pedestrian Count", plus the
  pedestrian-density and isolated-blocks `HelpBox`es), moved out of the City tab's General card.
- Traffic lights and pedestrian crossings are now also placed at a classic (non-Custom Grid)
  city's own border T-intersections, not just at strictly-interior 4-way crossings — previously
  every side of a rectangular grid had none at all.
- The "~N buildings · N vehicles · N pedestrians" summary line moved out of the City tab's General
  card and into the footer, above the build/rebuild/reset buttons, so it stays visible on every tab;
  it now also reports the custom place count.

### Fixed

- The minimap no longer smears its border pixels across the HUD when the player gets close to a
  city edge. `MinimapHUD` keeps the player exactly centred, so its `uvRect` window necessarily
  reaches past the snapshot within View Radius of an edge, and the snapshot's `Clamp` wrap mode
  then repeated its outermost row/column outwards -- stretching whatever sat on that border (an
  empty block's grass, a strip of sidewalk) over the rest of the map. The map image now uses a new
  `MinimapWindow` material whose shader paints anything outside the snapshot with the capture
  camera's own background colour instead.

- Vehicles no longer strand themselves at the city's outer edge. A vehicle's *initial* target
  comes from a blind spatial search (`TrafficNetwork.FindNodeAhead`), not from the graph, so one
  spawned on a perimeter entry facing out of the city locked onto the outward-facing exit node
  past the intersection -- a dead end -- drove to it and disabled itself on arrival, parked there
  for the rest of the session (5 of 80 cars on a 5x5 grid). That search now never returns a node
  with no exits of its own, and a vehicle standing exactly on a node targets that node rather than
  the next one along, so it arrives immediately and gets routed properly, turns included.
- Custom Grid: a `RoadBaseMargin`-square gap was left uncovered at every outward-facing corner of
  the shape (the edge margin strips only covered the shape's straight sides, never its corners).
  Both contour bands are now tiled as an exact dilation difference, which also removes the
  z-fighting overlap the old per-block strips left at every concave corner.
- Custom Grid: `TrafficNetwork`/`PedestrianNetwork`'s custom-shape state (`useCustomShape` and the
  real block/plaza/reserved cell sets) was held in plain, unserialized fields. It survived only
  until the next domain reload or scene reload, at which point `Awake()` silently rebuilt the
  graph as an unrestricted full rectangle over the whole 10x10 canvas — the built geometry stayed
  correctly shaped (it's static), but vehicles routed and drove across the entire canvas,
  including holes with nothing built there. Now `[SerializeField]`.
- Custom Grid: a vehicle reaching a genuine dead end (a street legitimately ending at the shape's
  own boundary) used to fall back to a "nearest node ahead" search across the *entire* network
  instead of stopping, occasionally latching onto an unrelated, disconnected node far outside the
  built shape and driving off-road to reach it. The vehicle now simply stops, same as when no
  network node can be found ahead of it at spawn time.

## [2.6.0] - 2026-08-30
### Added

- Pedestrians can now cut through a normal block's interior instead of only ever walking its
  perimeter sidewalk ring: every non-plaza block without a full-block Custom Place gets a 5-node
  `Interior` cross (block centre + 4 arm midpoints) wired into its own ring. A plaza block or a
  full-block Custom Place block gets none — pedestrians stay confined to the ring around them.
- The pedestrian density warning now measures against the network's total node count (ring +
  interior), not just ring nodes, since ring alone stopped representing a block's real walkable
  capacity. Default `pedestrianCount` raised to 150 accordingly.

## [2.5.0] - 2026-08-29
### Added

- New "Audio" tab with two cards: Ambience (2D looping clips that play regardless of camera
  position) and Plazas (3D positional clips, one AudioSource per configured entry per generated
  plaza block, with logarithmic rolloff and a per-entry min/max distance). Both are on by
  default; Ambience ships with a default `city-ambiance.wav` clip at volume 1.
- Ambience/Plazas volume sliders now have a numeric field next to them for typing an exact value,
  kept in sync with the slider in both directions.
- Plazas ships with two default clips: `plaza-ambiance-fountain.wav` (volume 1, 4/20m min/max
  distance) and `plaza-ambiance-birds.wav` (volume 1, 20/50m min/max distance).

### Fixed

- "Set Current Selection As Default" never captured `settings.audio` (Ambience/Plazas clip lists),
  so running it silently dropped the tool's default audio wiring; "Reset to Defaults" (and a freshly
  opened window) would then leave Ambience's clip empty and Plazas' clip list empty instead of the
  documented defaults. `CityGeneratorDefaultAssetsWriter` now regenerates both lists, matching the
  existing pattern for every other prefab/asset list.
- Audio: editing a Plaza clip entry's Min Distance/Max Distance in the tool UI silently kept the
  entry's previous value instead of the typed one, so generated cities always used stale/default
  distances regardless of what was configured. Caused by the row holding onto a `SerializedProperty`
  captured when the row was built, which goes stale once anything else in the window edits the
  shared `SerializedObject` in between; the Ambience/Plazas clip lists now re-fetch each property by
  array index at write time instead, matching `CityGeneratorWeightedPrefabList`'s already-safe
  pattern.

### Changed

- Day/Night Cycle: the Directional Light's yaw is now forced to -110° (was -90°) on every
  "Build City in New Scene" and "Rebuild City in Current Scene", so the sun rises
  east-north-east and sets west-south-west instead of exactly along the world X axis. The
  minimap snapshot's own neutral light follows the same constant.

## [2.4.1] - 2026-08-28
### Changed

- Day/Night Cycle: the Directional Light's yaw is now always forced to -90° (was -30°) on every
  "Build City in New Scene" and "Rebuild City in Current Scene", so the sun rises due East and sets
  due West, matching the minimap's East/West orientation (minimap-right is East). Previously the
  yaw was only set on first creation and left untouched on a Rebuild.

### Fixed

- Minimap snapshot: the captured top-down image no longer reflects the Directional Light's Day/Night
  Cycle state. Any directional light present at capture time (this city's own, mid-cycle, or one left
  over from a previous city during a Rebuild) is temporarily disabled and replaced by a fresh neutral
  daytime light for the snapshot only, so the minimap stays clearly readable regardless of the hour
  the city was generated at.
- Day/Night Cycle: Start Hour is now always applied to the Directional Light, even when Enabled is
  off. Previously, disabling the cycle skipped the `ApplySun` call entirely, leaving the light at its
  static default orientation instead of the configured Start Hour. The `DayNightCycle` component now
  stays on the light regardless of Enabled; the toggle only controls whether it auto-advances the
  hour in Play Mode.

## [2.4.0] - 2026-08-27
### Added

- Day/Night Cycle: an optional (off by default) 24h cycle for the generated Directional Light,
  configured in a new "Day/Night Cycle" card in the City tab (Enabled/Start Hour/Speed Multiplier,
  plus a `Gradient` for light color and an `AnimationCurve` for light intensity over the day). When
  enabled, the light already previews oriented/colored for Start Hour right after generation, and
  rotates/changes color and intensity continuously in Play Mode at the configured speed.
  "Rebuild City in Current Scene" now reconfigures the Directional Light's cycle to match the
  current settings (adding/updating/removing it), while its base rotation and shadows stay
  untouched.

## [2.3.0] - 2026-08-27
### Added

- Minimap HUD: an optional (on by default) circular minimap in the top-left corner, showing a
  static top-down snapshot of the generated city centred on the player in real time, with Custom
  Places marked as Point of Interest labelled by name. Configured in a new "Minimap" tab
  (Enabled/Texture Resolution/View Radius). The snapshot is captured once during generation and
  saved as a PNG asset inside the scene's own per-scene folder (e.g.
  `Assets/Scenes/City1/City1_Minimap.png`, the same folder Unity itself creates next to a scene for
  things like baked lighting data).

### Changed

- Applied a post-processing volume profile (`Assets/Settings/PolaroidVolumeProfile.asset`) to the
  demo scene (`Assets/Scenes/City.unity`), plus a shadow tuning pass on `PC_RPAsset.asset`. This
  repo's test scene only — the package itself carries no post-processing dependency.

## [2.2.2] - 2026-08-27

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
