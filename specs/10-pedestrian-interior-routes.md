# SPEC 10 — Rutas peatonales interiores: manzanas y plazas

> **Status:** Implemented
> **Depends on:** SPEC 03 (Pedestrian network), SPEC 05 (Performance and tests), SPEC 06 (Custom Places)
> **Date:** 2026-08-29
> **Objective:** Ampliar `PedestrianNetwork` con dos nuevos tipos de nodo — `Interior` (una cruz que atraviesa el hueco entre los 4 slots de edificio de una manzana normal) y `Plaza` (una rejilla densa que cubre todo el bloque, evitando banco/fuente/árboles) — para que los peatones dejen de estar confinados al anillo de acera perimetral.

## Scope

**In:**

- Dos nuevos valores en `PedestrianNodeKind` (`Runtime/PedestrianNetwork.cs`): `Interior` y `Plaza`.
- `PedestrianNetwork.Build()` genera, por cada manzana no-plaza sin Custom Place a manzana completa, una cruz de 5 nodos `Interior` (centro + 4 brazos) conectada a los 4 nodos `Ring` de tipo midpoint de ese bloque.
- `PedestrianNetwork.Build()` genera, por cada manzana marcada como plaza (`BlockCell.isPlaza`), una rejilla de nodos `Plaza` (paso ~4 m, `CityGeneratorConstants.PlazaGridStep`) cubriendo el área del bloque, con aristas entre vecinos ortogonales, conectada a los 4 nodos `Ring` midpoint del bloque.
- Nueva información por bloque que `PedestrianNetwork` necesita para decidir Interior vs. Plaza vs. nada, pasada desde `CityGeneratorPedestrianBuilder.AddNetworkComponent` (que ya se llama después de que `blocks`/`reservedSlots` existen en `CityGeneratorContentAssembler`): por bloque, si es plaza y si tiene un Custom Place a manzana completa.
- Poda de los nodos `Interior`/`Plaza` contra `obstacles` reutilizando el pipeline existente (`PruneNodesAgainstObstacles` en generación, `PrunePlacedObstacles`/`Physics.CheckSphere` en `Awake` y el `[ContextMenu]` de rebake) — sin pipeline nuevo.
- Recalcular `CityGeneratorConstants.PedestrianCountWarningThreshold` (y el aviso que lo usa en `CityGeneratorWindow`) sobre `PedestrianNetwork.NodeCount` total en vez de solo nodos `Ring`.
- Actualizar `docs/architecture/pedestrians.md` y el invariante de `CLAUDE.md` ("`PedestrianNetwork` tiene exactamente tres tipos de nodo") para reflejar los 5 tipos.
- Ampliar `PedestrianNetworkTests.cs` (EditMode) para cubrir la cruz interior y la rejilla de plaza.

**Out of scope (para otro spec):**

- Cualquier geometría/visual nueva (asfalto, aceras) en el hueco interior de manzana — sigue siendo el mismo suelo ya generado, solo cambia el grafo.
- Custom Places a manzana completa como zona paseable (jardines, parques privados) — de momento siguen sin generar ningún nodo interior, como hoy.
- NavMesh o steering libre — se mantiene el mismo patrón BFS de nodos que el resto del proyecto.
- Cambios en `PedestrianManager`'s separación/staggering para adaptarse a la mayor densidad de nodos por plaza — si hace falta ajustar, es una iteración aparte tras medir con la rejilla ya en marcha.
- Que los vehículos detecten/reaccionen a peatones en manzanas interiores o plazas de forma distinta a hoy — `CarAgent.pedestrianMask`/`PedestrianRoadProximityGrid` no cambian.

## Data model

`Runtime/PedestrianNetwork.cs`:

```csharp
public enum PedestrianNodeKind { Ring, Curb, Crossing, Interior, Plaza }
```

`PedestrianNode` itself gains no new fields — `Interior`/`Plaza` nodes use the existing `Position`/`Kind`/`Blocked`/`Neighbours`, and leave `Intersection`/`CrossingAxisIsX` at their default (`null`/`false`), same as `Ring`/`Curb` do today.

Per-block metadata `PedestrianNetwork` needs to decide Interior vs. Plaza vs. nothing, added as a new serialized field:

```csharp
// Flattened [bi, bj] -> flag, set by CityGeneratorPedestrianBuilder.AddNetworkComponent from
// BlockCell.isPlaza / reservedSlots (slot == -1). Runtime-only bools: PedestrianNetwork.Build()
// must not know about BlockCell or CustomPlaceEntry (Editor-only types).
[SerializeField] private bool[] blockIsPlaza;       // length blocksX * blocksZ
[SerializeField] private bool[] blockIsFullyReserved; // length blocksX * blocksZ
```

`CityGeneratorPedestrianBuilder.AddNetworkComponent` gains two new parameters (`IReadOnlyList<BlockCell> blocks`, `HashSet<(int gridX, int gridY, int slot)> reservedSlots`) to compute and set these two arrays via `SerializedObject`, mirroring how `AddManagerComponent` already wires other fields.

New layout constants in `CityGeneratorConstants`:

```csharp
public const float PlazaGridStep = 4f;      // spacing of the Plaza node grid
public const float PlazaGridInset = 2f;     // keeps the outermost Plaza row/column off the block edge
```

`PedestrianNetwork` mirrors the geometry constants pattern it already follows for `ringRadius`/`crossingArmOffset` (its own `[SerializeField]` copies, not a reference into `CityGeneratorConstants` — that class is Editor-only): a new `[SerializeField] private float plazaGridStep = 4f;` set by `AddNetworkComponent` from the constant, plus a fixed 5-node cross geometry for `Interior` (offsets ± half the block's building-slot gap, hardcoded like `BuildBlockRing`'s ring offsets already are — no new constant needed since it derives directly from `ringRadius`).

`CityGeneratorConstants.PedestrianCountWarningThreshold`'s consumer (the block that reads it, in `CityGeneratorWindow`) switches its denominator from a Ring-only count to `PedestrianNetwork.NodeCount`/an equivalent total computed at validation time — no new constant, existing 0.7 value kept.

## Implementation plan

1. Add `Interior` and `Plaza` to `PedestrianNodeKind`, plus their gizmo colours in `PedestrianNetwork`'s existing `Kind switch`. No behaviour change yet — compiles, existing graph identical. Manual test: generate a city, confirm nothing changed.

2. Add `blockIsPlaza`/`blockIsFullyReserved` serialized fields to `PedestrianNetwork`, plus the two new parameters on `CityGeneratorPedestrianBuilder.AddNetworkComponent` (`blocks`, `reservedSlots`) that populate them via `SerializedObject`, and update its one call site in `CityGeneratorContentAssembler`. Still no nodes generated from this data. Manual test: generate a city, inspect the `PedestrianNetwork` component in the Inspector, confirm the two arrays hold the right flags per block index.

3. Implement `BuildInteriorCross(bi, bj, cornerNode)`: 5 `Interior` nodes (block centre + 4 arm midpoints) for a block where `!blockIsPlaza && !blockIsFullyReserved`, connected in a cross and to the block's 4 `Ring` midpoint nodes. Call it from `Build()` right after `BuildBlockRing` for each qualifying block. Manual test: generate a city, enable `drawGraph`, confirm the interior cross appears in every normal block's Scene gizmo and connects to the ring; enter Play, watch a pedestrian occasionally cut through a block interior.

4. Implement `BuildPlazaGrid(bi, bj, cornerNode)`: a `plazaGridStep`-spaced grid of `Plaza` nodes over the block's footprint (inset by `PlazaGridInset`), 4-connected to orthogonal neighbours, tied into the block's 4 `Ring` midpoints via nearest-node connections. Call it from `Build()` for `blockIsPlaza` blocks instead of step 3's cross. Manual test: generate a city with a plaza, confirm the grid gizmo covers the block and pedestrians walk across the plaza interior, not just its ring.

5. Verify (no code change expected) that `CityGeneratorPedestrianBuilder.PruneNodesAgainstObstacles` and `PedestrianNetwork.PrunePlacedObstacles` already correctly block `Interior`/`Plaza` nodes overlapping a building, Custom Place quarter-slot, bench, fountain or tree, since both iterate `network.NodeCount` generically. Manual test: generate a city, confirm pedestrians route around the plaza centerpiece/benches and around a quarter-slot Custom Place instead of walking through them.

6. Switch the pedestrian density warning (`CityGeneratorWindow`) from a Ring-only node count to `PedestrianNetwork.NodeCount` (or the validator's equivalent count at validation time, before a network instance exists — reuse whatever `CityGeneratorValidator` already estimates for the Ring-only version today). Manual test: generate a city with `pedestrianCount` near the old Ring-based threshold, confirm the warning no longer fires spuriously now that the real capacity is much larger.

7. Extend `PedestrianNetworkTests.cs` (EditMode) with cases for: a non-plaza block gets exactly 5 `Interior` nodes connected to its ring; a plaza block gets a `Plaza` grid connected to its ring; a full-block Custom Place block gets neither.

8. Update `docs/architecture/pedestrians.md` (node kinds list, obstacle pruning coverage) and the root `CLAUDE.md` invariant line ("`PedestrianNetwork` has exactly three node kinds") to describe the five kinds and when each is generated.

## Acceptance criteria

- [x] `PedestrianNodeKind` has exactly five values: `Ring`, `Curb`, `Crossing`, `Interior`, `Plaza`.
- [x] Generating a city produces exactly 5 `Interior` nodes per non-plaza block that has no full-block Custom Place, each connected to the block's own 4 `Ring` midpoint nodes.
- [x] A block with a full-block Custom Place (`occupiesFullBlock == true`) gets zero `Interior` and zero `Plaza` nodes.
- [x] Generating a city with at least one plaza block produces a `Plaza` node grid (step `PlazaGridStep`) covering that block's footprint, 4-connected to its orthogonal neighbours and to the block's 4 `Ring` midpoint nodes.
- [x] A `Plaza`/`Interior` node whose position falls inside an obstacle's footprint (building, Custom Place quarter-slot, bench, fountain, tree) is `Blocked == true` after generation, same as any pruned `Ring` node today.
- [x] In Play mode, a pedestrian is observed walking through a normal block's interior (not just around its ring) within a reasonable observation window.
- [x] In Play mode, a pedestrian is observed walking across a plaza's interior, visibly routing around the centerpiece and benches rather than through them.
- [x] The pedestrian density warning in `CityGeneratorWindow` no longer fires against `PedestrianCountWarningThreshold` using only Ring node count — it reflects the larger total node count once Interior/Plaza nodes exist.
- [x] `PedestrianNetworkTests.cs` covers all three block cases (normal, plaza, full-block Custom Place) and passes.
- [x] `docs/architecture/pedestrians.md` and the `CLAUDE.md` invariant both describe five node kinds, not three.

## Implementation note (found during manual QA, not anticipated by the plan above)

`FindPath`'s BFS resolves same-hop-count ties in favour of whichever edge was built first. A block's `Interior`/`Plaza` nodes tie in hop count with the block's own `Ring`-only route between the same two midpoints (e.g. south midpoint to north midpoint: 4 hops either way around the ring, and also 4 hops via the interior cross) — and ring edges are always built first — so a same-block `Ring`-to-`Ring` route would *never* actually cross through `Interior`/`Plaza`, no matter how the graph is shaped. Verified empirically in Play mode (sped up via `Time.timeScale`): with the original `PickRandomDestination` (`Ring` nodes only, per SPEC 03/05), zero of 90 pedestrians came within 3m of an `Interior` node after 12+ simulated minutes. Fixed by widening `PickRandomDestination` to accept `Interior`/`Plaza` as valid final destinations too (excluding only `Curb`/`Crossing`, the mid-crosswalk link nodes) — re-verified the same way: pedestrians reliably walk into both a normal block's interior and a plaza's interior. Without this, the two new node kinds would exist in the graph and pass every structural test, but would be dead weight a pedestrian could never actually be observed using.

## Decisions

- **Yes:** solo grafo peatonal (nodos/aristas nuevos), sin geometría/visual nueva en el hueco interior de manzana. El suelo ya existe (ground/sidewalk); añadir una franja de pavimento visible sería puramente estético y no lo pidió el usuario.
- **Yes:** cruz simple de 5 nodos para el interior de manzana normal, no una rejilla densa ahí. El caso de uso es "atajo por el interior", no pasear libremente como en una plaza — una rejilla completa multiplicaría nodos/aristas sin aportar nada al caso de uso real.
- **No:** rejilla densa también para manzanas normales. Descartado por lo anterior — coste de memoria/BFS sin beneficio percibido.
- **Yes:** rejilla densa (paso 4 m) solo para bloques de plaza, siguiendo el mismo patrón BFS de nodos que ya usa todo `PedestrianNetwork` (Ring/Curb/Crossing). Coherente con la arquitectura existente, sin dependencias nuevas.
- **No:** NavMesh real (`NavMeshSurface`/`NavMeshAgent`) para las plazas. Introduciría una arquitectura de movimiento paralela (BFS fuera de la plaza, NavMesh dentro), una dependencia de paquete nueva (`com.unity.ai.navigation`) y coste de bake en generación — desproporcionado frente al patrón ya establecido y probado del proyecto.
- **Yes:** los nodos `Interior`/`Plaza` se generan siempre (salvo bloque con Custom Place a manzana completa) y se podan contra `obstacles` reutilizando el pipeline existente (`PruneNodesAgainstObstacles` + `PrunePlacedObstacles`/`Physics.CheckSphere`), en vez de calcular de antemano los huecos libres. Es el mismo enfoque que ya usa el anillo (`Ring`) contra edificios/props, evita duplicar lógica de geometría contra obstáculos.
- **No:** cálculo fino de huecos libres antes de generar los nodos. Redundante con el pruning genérico ya existente y battle-tested.
- **Yes:** conexión al anillo solo por los 4 nodos `Ring` de tipo midpoint (no las 4 esquinas), tanto para la cruz interior como para la rejilla de plaza. Coherente con la geometría de la cruz de 4 brazos y evita aristas diagonales largas cruzando el bloque.
- **No:** conectar también las 4 esquinas del anillo. Más aristas sin mejorar rutas de forma perceptible, dado que las esquinas ya están conectadas a los midpoints dentro del propio anillo.
- **Yes:** dos `PedestrianNodeKind` nuevos y distintos (`Interior`, `Plaza`) en vez de uno genérico. Mismo convenio que el proyecto ya sigue (`Ring`/`Curb`/`Crossing` como tipos distintos aunque geométricamente relacionados); permite que gizmos, tests y futura lógica distingan ambos casos sin heurísticas por cantidad de nodos.
- **No:** un solo node kind reutilizado para ambos casos. Perdería la distinción semántica y obligaría a inferir el tipo de zona por el número de nodos generados.
- **Yes:** el Custom Place a manzana completa sigue sin generar ningún nodo interior (ni `Interior` ni `Plaza`), igual que hoy. Extenderlo exigiría decidir qué cuenta como obstáculo dentro de un prefab de Custom Place arbitrario — fuera de alcance de este spec.
- **Yes:** `PedestrianCountWarningThreshold` se recalcula sobre el total de nodos del grafo, no solo `Ring`. El anillo deja de representar la capacidad real de la manzana una vez existen `Interior`/`Plaza`; mantener el aviso contra solo `Ring` lo volvería demasiado alarmista sin motivo.

## Risks

| Risk | Mitigation |
| --- | --- |
| Una plaza grande genera muchos nodos `Plaza` (~100+ por bloque), aumentando memoria/coste de BFS y del pruning O(nodos × obstáculos) | El paso de 4 m ya se eligió como punto de partida razonable (Bloque 2); si el coste medido resulta alto con muchas plazas, ajustar `PlazaGridStep` es un cambio de una constante, no de arquitectura |
| El valor 0.7 de `PedestrianCountWarningThreshold`, pensado para solo nodos `Ring`, puede quedar mal calibrado sobre el nuevo total (mucho más alto) | Se mantiene 0.7 como punto de partida (decisión ya tomada); si en QA manual se ve que el aviso ya no dispara nunca de forma útil, se afina el valor en un cambio posterior, sin tocar el mecanismo |
| Un `Interior`/`Plaza` node cuya conexión al `Ring` midpoint más cercano cruce por encima de un obstáculo no detectado en ese punto exacto (hueco entre nodos, no en un nodo) | Mismo riesgo ya aceptado hoy por el anillo/crossings existentes — el pruning es por nodo, no por arista; no es una regresión introducida por este spec |

## What is **not** in this spec

- Geometría/visual nueva (asfalto, aceras) en el hueco interior de manzana — sigue siendo el mismo suelo generado hoy, solo cambia el grafo peatonal.
- Custom Places a manzana completa como zona paseable (jardines, parques privados con sus propios obstáculos internos).
- NavMesh o steering libre — se mantiene el patrón BFS de nodos existente en todo el proyecto.
- Ajustes a `PedestrianManager` (separación/staggering) para adaptarse a la mayor densidad de nodos por plaza.
- Cambios en cómo los vehículos detectan/reaccionan a peatones en manzanas interiores o plazas.

Cada uno de estos, si llega a implementarse, va en su propio spec.
