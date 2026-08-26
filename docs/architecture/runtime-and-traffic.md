# Runtime components — `Packages/com.santiandrade.citygenerator/Runtime/`

Detail behind the "Runtime components" section of the root `CLAUDE.md`. Namespace `CityGenerator.Runtime`, references `Unity.InputSystem`. These ship with the tool and are what a generated city runs on. The pedestrian half lives in `pedestrians.md`.

## Player and camera

- **`PlayerInputAuthority.cs`** — the single component allowed to call `Enable()`/`Disable()` on the Player action map (`OnEnable`/`OnDisable`), added to the Player instance by `CityGeneratorSceneBuilder.ConfigurePlayer` alongside `CharacterController`/`PlayerController`. `PlayerController` and `ThirdPersonCamera` only ever read actions already enabled by this component — previously each called `Enable()`/`Disable()` on the same shared map from its own `OnEnable`/`OnDisable`, so disabling either one alone cut input to the other too.
- **`PlayerController.cs`** — walk/run/jump via the new Input System (`Player` action map: `Move`, `Sprint`, `Jump`). Movement is camera-relative. Drives the Animator's `Speed`, `Grounded`, `Jump`, `VerticalSpeed`. `[RequireComponent(typeof(CharacterController))]`, but neither it nor `CharacterController` are baked into the demo character prefabs.
- **`ThirdPersonCamera.cs`** — Mario-64-style orbit camera, reads `Look` from the same map. Only the target-follow *position* is smoothed (`SmoothDamp`); rotation is recomputed every frame as `LookRotation` from the camera's actual (possibly lagging) position to the pivot, so position and aim can never desync — **do not reintroduce separate smoothing on `transform.rotation`**, that was tried and caused visible motion sickness. Pivot offset is `verticalOffset` + `horizontalOffset` (the latter along the orbit's local right axis, i.e. over-the-shoulder), not a single `Vector3`. Has camera-vs-environment `SphereCast` collision. `OnDisable` restores cursor lock state/visibility (previously absent, so leaving Play or disabling this component left the cursor stranded locked/hidden).

## Traffic lights

**`TrafficLight.cs`** swaps lamp materials (`LightRed`/`LightAmber`/`LightGreen`/`LightOff`) to show its state. **`TrafficLightIntersection.cs`** runs the coroutine alternating green between its `eastWest` and `northSouth` groups with amber and all-red phases, with a per-intersection `startOffset` so neighbours are not in sync.

## `TrafficNetwork.cs`

*Generates* the lane graph in `Awake` from the same layout as the geometry (`laneOffset` 2.6; nodes are computed, not serialized). The axes are **two independent arrays**, `axesX` and `axesZ`, so grids need not be square; `SetAxes` + the public `Build()` let the generator set the layout, place all traffic lights, and only then build the graph (`Build` matches lights by scanning the scene, so order matters). `EnsureBuilt` covers accessors called before `Awake`.

Per intersection and direction there is an *entry* node (the inner corner of the crossing: where the turn is chosen and where the light applies) and an *exit* node; turning right needs no intermediate node because one direction's entry coincides geometrically with the exit of the direction to its right.

Lights are matched **by geometry, not by name**: each entry gets the `TrafficLight` facing it head-on (`Dot(light.forward, direction) < -0.9`), which leaves outer intersections unsignalled.

Exposes `LaneOccupancy`/`RoadProximity`, both on the same GameObject as `Manager`. `Physics.SyncTransforms()` (needed because the project runs with `m_AutoSyncTransforms: 0` and `CarAgent` moves by transform then raycasts in the same frame) moved to `TrafficManager.Update` in SPEC 05; `TrafficNetwork` itself no longer calls it.

## `CarAgent.cs`

Follows the graph picking turns at random (`straightWeight` favours going straight), brakes progressively for red/amber and for the car ahead, and takes corners with `RotateTowards` plus a speed cut, not by interpolating positions. Falls back to `FindAnyObjectByType<TrafficNetwork>()` in `OnEnable`/`Start` if `network` wasn't injected by the generator, since a scene only ever has one. Ticked by `TrafficManager` rather than through its own `Update`; resolves its manager via `network.Manager` (falling back to `FindAnyObjectByType<TrafficManager>()`, then auto-creating one, for standalone use outside the generator) and registers with it in `OnEnable` — idempotent, since `TrafficManager.agents` is a `HashSet`.

Four rules are load-bearing:

1. Once past the stop line a car keeps going even if the light changes, so it never freezes inside a crossing.
2. At unsignalled crossings priority is an exclusive per-intersection reservation (`TryReserve`/`Release`, with a timeout), because the forward sensor cannot see cross traffic. **That reservation once deadlocked every car for over five minutes and its current shape *is* the fix — do not revert any of its three pieces**: (a) the owner does *not* refresh its timestamp when re-claiming (if it does, a blocked car holds the reservation forever just by asking every frame and the timeout never expires); (b) priority is claimed only within `claimDistance` of the stop line, slowing down beforehand like a give-way, never from far off; (c) `ReleaseReservationWhileBlocked` drops it when its owner is stopped by another car and has not entered the crossing, breaking the "A waits for the priority B holds, B waits for A to move" cycle. `CurrentStopReason`/`StoppedTime`/`DistanceTravelled` exist to diagnose jams.
3. In the forward sensor (filtered by `vehicleMask`, set per-instance by `CityGeneratorTrafficBuilder`) hits are discarded **by identity** (`other == this`), never by distance: a zero-distance hit is both the car's own collider *and* the car already bumper-to-bumper ahead, and filtering by distance made cars fail to see each other and drive inside one another on corners. SPEC 05 changed the "car ahead" check to first query `TrafficNetwork.LaneOccupancy.TryGetCarAhead` and only fall back to the physics sensor (now an `OverlapSphere` centred on the car's own position, not a `SphereCast` from an offset point ahead — closes a blind spot where a long vehicle a couple of metres ahead could go undetected) when the lane index has no answer (free lane, end of segment, inside a crossing). The pedestrian side of the sensor (`pedestrianMask`) is untouched by the lane index; above `staggerMinAgentCount` pedestrians it queries `TrafficNetwork.RoadProximity` instead of `SphereCastNonAlloc`.
4. Route weighting (`RouteWeight`/`Ring`, with `interiorBias` and `borderPenalty`) exists because without it all traffic circles the perimeter: on a border street, going straight is the only non-turning exit, ~71% likely at every crossing with the raw straight weight. Measured with the weights, traffic settles around 9/9 perimeter/interior.

## `TrafficManager.cs`

Ticks every registered `CarAgent` from a single `Update` (`Register`/`Unregister`, called from each car's `OnEnable`/`OnDisable`) instead of each car paying its own Update marshalling cost (technical review, A.7).

Not a scene-global singleton (no `static Instance`, removed in SPEC 04): `CityGeneratorTrafficBuilder.AddManagerComponent` adds it to the `TrafficNetwork` GameObject only when `includeTraffic` is on, and wires it into `TrafficNetwork.Manager` (same GameObject) so `CarAgent` resolves it through the network rather than a global; a `CarAgent` used outside the generator falls back to finding or auto-creating one, so it still drives standalone. This is what lets multiple generated cities coexist in the same scene without their managers fighting over which one ticks which cars.

Below `staggerMinAgentCount` (60 by default) every car's forward sensor runs every frame, identical to the old per-car `Update` — **do not lower this without re-verifying the default 80-car demo still behaves the same**. Above it, cars farther than `staggerDistance` from `Camera.main` only run their sensor 1 of every `staggerFrames` frames, reusing the previous clearance in between.

Since SPEC 05 it also calls `Physics.SyncTransforms()` once per frame after ticking every agent (moved here from `TrafficNetwork`), and only when `agents.Count > 0` — a scene with traffic disabled or with every `CarAgent` unregistered no longer pays for it.

## `TrafficLaneOccupancy.cs` (SPEC 05)

A per-directed-edge (`(fromNode, toNode)`) occupancy index of the `CarAgent`s currently in that lane segment, ordered by `DistanceTravelled`. `CarAgent` reports itself via `Enter`/`Leave` as it moves between graph nodes; `TryGetCarAhead` resolves "car immediately ahead in the same segment" without a physics query. Lives on the same GameObject as `TrafficNetwork.Manager`, exposed as `TrafficNetwork.LaneOccupancy`. Deliberately scoped to *only* the same-segment-car-ahead case — crossings, obstacles and pedestrians still go through the physics sensor, keeping this index decoupled from `CarAgent`'s pedestrian-braking rule (rule 3 above).

## `CityGeneratorRoot.cs`

An empty `[DisallowMultipleComponent]` marker `MonoBehaviour` added to the root of every generated city (first thing `CityGeneratorContentAssembler.Assemble` does, before any builder runs). Lives in `Runtime/` (not `Editor/`) so it also exists in player builds. Lets `CityGeneratorSceneBuilder.RebuildInActiveScene` find the previous city by component instead of by GameObject name (which the user may have renamed, or which an unrelated object might coincidentally share).

## Collider policy — `CityGeneratorColliderUtility.EnsureNonTriggerCollider` (`Editor/`)

The shared policy for both generated vehicle and pedestrian instances: the instance **root** always ends up with exactly one dedicated, non-trigger "proxy" collider used exclusively for `CarAgent`'s/`PedestrianAgent`'s own sensor detection — reusing one already on the root (forced non-trigger) if present, or adding a `BoxCollider` sized from the combined `Renderer` bounds otherwise.

**A collider that only exists deeper in the prefab's hierarchy is left completely untouched** (not even its `isTrigger`) — that was the SPEC 04 fix: previously such a collider was found via `GetComponentsInChildren` and had `isTrigger` forced off, but its *layer* was never touched, so it still went undetected by a sensor's layer-filtered query (a collider's layer comes from its own GameObject, not its parent's). The layer the builders assign (`Vehicle`/`Pedestrian`) is applied only to the proxy's own GameObject (the instance root), returned by `EnsureNonTriggerCollider` for that purpose — never propagated to children, so a nested collider keeps whatever layer/`isTrigger` the user's prefab gave it, free to serve its own purpose (typically physical collision against the player's `CharacterController`).

Reusing a root collider when present (rather than unconditionally adding a second one) matters because `CarAgent.OnEnable` does `GetComponent<Collider>()` (root-only, singular) to register itself for the bumper-to-bumper identity check — every demo vehicle prefab already carries its own root `BoxCollider`. Called from both `CityGeneratorTrafficBuilder.BuildVehicles` and `CityGeneratorPedestrianBuilder.BuildPedestrians`, never touching the prefab asset.
