# Pedestrian system

Detail behind the "Pedestrian system" bullet of the root `CLAUDE.md`. Added by `specs/03-pedestrian-network.md`, structurally mirroring the vehicle traffic system (`runtime-and-traffic.md`) throughout.

## `PedestrianNetwork.cs`

An **undirected** graph (unlike `TrafficNetwork`'s directed one: a pedestrian can walk either way along every edge), rebuilt in `Awake`/`Build()` from the same street axes.

- Every block gets an 8-node ring (4 corners + 4 side midpoints) sitting in the gap between the building slot edge and street furniture.
- Every intersection with a real `TrafficLightIntersection` nearby (any with at least 3 real street arms — a full 4-way, or a T-intersection including along the grid/shape's own border; a perimeter corner with exactly 2 perpendicular arms never gets one, since a car arriving there has only one possible way through) additionally gets, per real arm, a curb → crossing → curb chain linking two ring corners across the street, with the crossing node matched **by geometry** (nearest `TrafficLightIntersection`, same convention as `TrafficNetwork`'s light matching, within a 14 m cutoff) so `CanCross` can read the real light state via `TrafficNetwork.AxisState`/`IsAxisGreen` (`TrafficLightIntersection.EastWestState`/`NorthSouthState`). `BuildCrossings` is called for every axis intersection (not just strictly interior ones) but skips any arm whose neighbouring block is out of range or a Custom Grid shape hole (`BlockExists`), and the 14 m cutoff means an unsignalled corner simply finds no match — widening the loop can't add a crossing where there's no real light.
- `CanCross` only allows stepping onto the road on **Red**, not just "not green" — Amber still has cars moving through.
- `FindPath` is a zero-allocation BFS into caller-supplied buffers (shortest by hop count, since every edge is unweighted).
- A grid with `gridWidth == 1` or `gridHeight == 1` has no interior intersections, so every block's ring is isolated — surfaced as a non-blocking `HelpBox` in `CityGeneratorWindow`, same convention as the vehicle density warning.
- `Build()` wipes `nodes` from scratch every call (Awake in Play, or an explicit re-bake) and rebuilds four node kinds: `Ring`, `Curb`, `Crossing`, and (SPEC 10) `Interior` — nothing else the generator adds to this graph after `Build()` returns. SPEC 06 removed the one thing that used to (the Point of Interest machinery); see below.
- SPEC 10 (`Interior`): every normal block **without** a full-block Custom Place (`BlockCell.isPlaza == false` and no `reservedSlots` entry with `slot == -1`) gets a 5-node `Interior` cross (block centre + 4 arm midpoints), connected to that block's own 4 `Ring` midpoint nodes — a shortcut through the block's interior, not a full walkable area. A plaza block (`BlockCell.isPlaza == true`) or a block with a full-block Custom Place gets neither — pedestrians stay confined to the ring around them. Per-block flags (`blockIsPlaza`/`blockIsFullyReserved`, flattened `[bi, bj] -> bool` arrays) are computed by `CityGeneratorPedestrianBuilder.AddNetworkComponent` from `BlockCell`/`reservedSlots` (both Editor-only types `PedestrianNetwork.Build()` must not reference directly) and written via `SerializedObject`, mirroring every other field `AddNetworkComponent`/`AddManagerComponent` wire in.
- A block's `Interior` nodes tie into its `Ring` midpoints at exactly the same hop-count a same-block `Ring`-only route already has (e.g. the south and north midpoints are 4 hops apart either way around the ring, and also 4 hops via the interior cross), so `FindPath`'s BFS — which resolves same-length ties in favour of whichever edge was built first, always the ring — would never actually route a pedestrian through it **as an intermediate waypoint**: detouring into an Interior pocket and back out always costs more hops than continuing along the street/ring grid, so BFS only ever enters one when it's the trip's actual endpoint. `PlanNewDestination` (below) is what decides that endpoint, and is where SPEC 10's fixes live.

SPEC 05 added two performance structures, both recalculated inside `Build()`:

- **Connected components** (`nodeComponent`, a flood fill over the finished ring/crossing edges, one entry per node, read via `ComponentOf`) — `PedestrianAgent.PlanNewDestination` filters candidates to the origin's component before attempting a route, so an isolated single-block ring never wastes a BFS on an unreachable node.
- A **short-lived BFS tree cache** (`cameFromCache`, keyed by origin node, invalidated on every `Build()`) so several pedestrians planning from nearby origins in the same frame window don't each repeat the same BFS.

## Obstacle pruning

Three levels, cheapest/coarsest to most thorough, matching the vehicle system's own generation-time-only check but going further:

1. `CityGeneratorPedestrianBuilder.PruneNodesAgainstObstacles` at generation time, against the same shared `obstacles` list every other category uses.
2. `PedestrianNetwork.PrunePlacedObstacles`, a `Physics.CheckSphere`-based auto-repair pass that runs every `Awake` (so it also catches a building moved/added by hand after generation).
3. The same method exposed as `[ContextMenu("Prune Placed Obstacles")]` / `Tools > City Generator > Rebuild Pedestrian Network` for an explicit re-bake without entering Play.

Levels 2/3 can't detect a user prefab with **no `Collider` anywhere in its hierarchy** — it still gets avoided at generation time via level 1.

All three levels iterate `network.NodeCount` generically, so `Interior` nodes (SPEC 10) get exactly the same pruning as `Ring`/`Curb`/`Crossing` with no pipeline changes — a quarter-slot Custom Place blocks any node that lands on it.

## `PedestrianAgent.cs`

Walks the graph destination to destination, moving by transform (no `CharacterController`/`Rigidbody`, mirroring `CarAgent`), driving the same `Speed`/`Grounded` Animator parameters `PlayerController` uses so it shares `CharacterAnimator.controller`'s Locomotion blend tree unmodified.

Every `Animator.SetFloat`/`SetBool` call is guarded by a `hasAnimatorController` flag cached in `Awake` — a pedestrian prefab with no `Animator`, or an `Animator` with no controller assigned, still walks fine, but calling `SetFloat`/`SetBool` on it would otherwise spam a console warning every frame.

Waits at a curb (`WaitingToCross` state) until `CanCross` clears. `PlanNewDestination` rolls several random candidate destinations and tries them **farthest-first** (by straight-line distance from the pedestrian's current position), not just the single farthest one — a grid-corner block with only one link to the rest of the network would otherwise almost always draw an unreachable "farthest" candidate and idle forever.

SPEC 10: any drawn `Interior` candidate is tried **first, in draw order** (no distance ranking) instead of competing in that same farthest-first sort — a point on the boundary of any spatially localized area is, by definition, at least as far from any approach direction as a point at its centre, so ranking it by raw distance meant a chosen destination inside one was almost never actually deep inside it, only ever its outer edge. `Ring` candidates are unaffected, still sorted farthest-first exactly as before.

Every serialized field on it (`walkReferenceSpeed`/`runReferenceSpeed`, `paceFraction`, `runnerChance`, jitter, stop durations…) is written unconditionally onto each generated instance by `CityGeneratorPedestrianBuilder.BuildPedestrians` from `CityGeneratorSettings.pedestrianBehaviour` (Pedestrians tab, Behaviour card) — the script's own field initializers only matter for a `PedestrianAgent` used standalone.

Since SPEC 05, `FindPath`'s output buffers are rented from `PedestrianManager`'s `PedestrianPathBufferPool` for the duration of planning rather than kept as a permanent per-agent array sized to the whole graph, and the agent's first `PlanNewDestination` after spawn is deferred by `PedestrianManager`'s staggering instead of firing in `Start`.

## `PedestrianPathBufferPool.cs` (SPEC 05)

A plain pool of `int[]` buffers sized to `PedestrianNetwork.NodeCount`, constructed by `PedestrianManager` in `OnEnable`. Exists because the old per-agent permanent buffer was O(pedestrians × nodes) of memory; now that initial planning is staggered across frames, only a small subset of agents is planning at any one instant, so a shared pool sized to the graph is enough. `PedestrianAgent.PlanNewDestination` calls `Rent`/`Return` around its `FindPath` calls.

## `PedestrianManager.cs`

Ticks every registered `PedestrianAgent` from one central `Update` (`Register`/`Unregister`, called from each agent's `OnEnable`/`OnDisable`), same convention as `TrafficManager`/`CarAgent` (not a scene-global singleton either — `PedestrianAgent` resolves it via `network.Manager`), with the same far-from-camera decision-logic staggering above `staggerMinAgentCount`.

On top of that it rebuilds a coarse spatial grid every frame and applies a small local separation nudge between nearby agents, plus a stronger player-avoidance nudge — the only peer-interaction mechanic implemented so far (a `PedestrianState.Interacting` value exists on the enum but is never entered, reserved for a future spec). `PedestrianAgent` has **no jam/gridlock mechanics of its own** — crowding only ever shows as this separation nudge, never a stuck agent — so `CityGeneratorConstants.PedestrianCountWarningThreshold` (0.7) is much higher than the vehicle one (0.4). Pedestrians still only ever spawn on `Ring` nodes, but since SPEC 10 the threshold is measured against the network's **total** node count (`PedestrianNetwork.NodeCount`/`CityGeneratorWindow`'s validation-time estimate of it) rather than just the `Ring` count — `Ring` alone stopped representing the block's real walkable capacity once `Interior` nodes exist. `CityGeneratorPedestrianBuilder.AddManagerComponent` configures every field on the generated instance from `CityGeneratorSettings.crowd` (Pedestrians tab, Crowd card).

SPEC 05 changes: it owns the `PedestrianPathBufferPool`; it staggers each agent's first `PlanNewDestination` by spawn index across several frames instead of firing every agent's initial plan in the same `Start`; its separation pass processes each nearby agent pair once instead of twice (once per side) and skips a pair entirely when neither side is "active" this frame under the existing sensor staggering; and it rebuilds `PedestrianRoadProximityGrid` once per frame, passing in the resolved player `Transform` separately since the player isn't a `PedestrianAgent`.

## `PedestrianRoadProximityGrid.cs` (SPEC 05)

A uniform spatial grid of registered `PedestrianAgent` positions, rebuilt once per frame by `PedestrianManager` and queried by `CarAgent.pedestrianMask` sensing (above `staggerMinAgentCount` pedestrians) instead of `SphereCastNonAlloc`. The **player** is not a `PedestrianAgent` (it's the manually-driven player instance, sharing the `Pedestrian` layer) so it can't be bucketed like one; `PedestrianManager` passes the resolved player `Transform` into `Rebuild` separately, and the grid tracks it via `TryGetPlayerPosition`, checked alongside the bucketed query so cars keep braking for the player once this grid takes over from the `SphereCast` fallback.

## Generation and layers

`CityGeneratorPedestrianBuilder` mirrors `CityGeneratorTrafficBuilder`: the `PedestrianNetwork` graph is always built and pruned against the shared `obstacles` list, **independent of `includePedestrians`**, so its crossings stay wired to the real traffic lights even when no NPCs are spawned; NPC instances are only placed when `includePedestrians` is on.

Vehicles brake for pedestrians (and for the player) via `CarAgent.pedestrianMask` and a second, independent forward sensor (`PedestrianAheadClearance`) feeding the same `StopReason.VehicleAhead` braking branch — no dedicated state. `EnsurePedestrianLayerAndAssignMask` creates the `Pedestrian` layer the same fail-closed way `EnsureVehicleLayerExists` creates `Vehicle` (first free slot from `FirstUserLayerIndex`, warns and leaves the mask at `0` if none is free) and sets `pedestrianMask` per vehicle instance, exactly like `vehicleMask`. It is called whenever vehicles exist (not gated on `includePedestrians`), since `CityGeneratorSceneBuilder` puts the **player** on this same `Pedestrian` layer and vehicles must detect it either way.

`PedestrianAgent` itself never gets pushed back by its own collider since it moves by setting `transform.position` directly. The 12 `DefaultAssets/Prefabs/Characters/` prefabs double as the default pedestrian list (~8.33% each), same prefabs also selectable as Player Prefab.

## Points of Interest — removed

Benches and the plaza centerpiece (`PlazaSettings.benchPrefab`/`centerpiecePrefab`) are plain visual props placed by `CityGeneratorPlazaBuilder`, exactly like a lamp or a bin — **not** wired into `PedestrianNetwork` in any way.

They used to register `PointOfInterest` nodes that pedestrians would occasionally walk to and linger at; SPEC 06 removed that entire mechanism (node kind, descriptor, register/connect methods, and `PedestrianBehaviourSettings.poiStopDurationMin`/`Max`) at the user's request, to decouple POIs from pedestrians ahead of the minimap/POI system SPEC 07 built on Custom Places instead (`CustomPlaceEntry.isPointOfInterest` → the Minimap HUD — see `custom-places.md` and `editor-tool.md`'s "Minimap" section). **Don't reintroduce a pedestrian-side POI stop without a new spec.**
