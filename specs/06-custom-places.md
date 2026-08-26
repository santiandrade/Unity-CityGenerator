# SPEC 06 — Custom Places

> **Estado:** Implemented
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 03 (Red peatonal), SPEC 04 (Correcciones críticas y arquitectónicas)
> **Fecha:** 2026-08-26
> **Nota posterior (2026-08-27):** la tab dedicada "Custom Places" descrita aquí se eliminó; la card de Custom Places se movió a la tab "City" (última card de esa tab). El resto de esta spec (modelo de datos, builder, validación, picker visual) sigue vigente sin cambios — ver `docs/architecture/custom-places.md` y `docs/architecture/editor-tool.md` para el estado actual.
> **Objetivo:** Añadir "Custom Places" — lugares definidos manualmente por el usuario (título, prefab, posición fija en manzana/slot elegida en un grid visual, orientación fija) que se instancian en lugar de un edificio aleatorio en esa posición — y, en paralelo, desvincular por completo el sistema de puntos de interés (POI) de la red peatonal, eliminando la parada aleatoria de peatones en bancos/plaza.

## Scope

**Dentro:**

- **Nueva tab "Custom Places"** en `CityGeneratorWindow` (`CityGeneratorTabBar` gana una pestaña adicional, junto a City/Player/Pedestrians), con una lista de entradas `CustomPlaceEntry` (añadir/quitar, igual convención que `vehicles`/`pedestrians`).
- **`Editor/CityGeneratorSettings.cs`** — `struct CustomPlaceEntry { title, prefab, isPointOfInterest, occupiesFullBlock, blockCell, cornerSlot, facing }` y `List<CustomPlaceEntry> customPlaces` en `CityGeneratorSettings`. Ver detalle en la sección "Modelo de datos".
- **Picker visual por entrada**: cada entrada de la lista lleva su propio mini grid preview (variante de `CityGeneratorGridPreview`, selección única en vez de multi-toggle) para elegir la manzana; si `occupiesFullBlock` es falso, un clic adicional dentro de la manzana permite elegir uno de los 4 cuadrantes (mismos `SlotOffsets` que `CityGeneratorBuildingBuilder`). Un selector de 4 direcciones (0/90/180/270°) fija la orientación (`facing`) manualmente — nunca aleatoria.
- **`Editor/CityGeneratorCustomPlaceBuilder.cs`** (nuevo, espejo de `CityGeneratorBuildingBuilder`) — instancia cada `CustomPlaceEntry` en su posición exacta (centro de manzana si `occupiesFullBlock`, offset de cuadrante si no) con la rotación fija de `facing`, y devuelve tanto la lista de instancias como el conjunto de slots que reserva (manzana, y cuadrante o "manzana completa").
- **`CityGeneratorBuildingBuilder.BuildBuildings`** gana un parámetro con los slots reservados por Custom Places: un cuadrante reservado se excluye del reparto aleatorio de esa manzana (los otros 3 cuadrantes se siguen rellenando normalmente); una manzana con un Custom Place de manzana entera se excluye por completo (sus 4 cuadrantes).
- **Pipeline**: `CityGeneratorCustomPlaceBuilder` corre antes que `CityGeneratorBuildingBuilder` (después de `Grid`), y sus instancias se añaden a la lista compartida `obstacles` como cualquier otro objeto colocado, para que props/vegetación no se solapen con ellas.
- **`Editor/CityGeneratorContentAssembler.cs`** — nuevo grupo `CustomPlaces` en la jerarquía generada, incluido en `MarkStatic` (como buildings; un custom place no se mueve en runtime).
- **`Editor/CityGeneratorValidator.cs`** — nuevos issues bloqueantes por entrada: título no vacío, prefab asignado, posición válida elegida (manzana dentro de `gridWidth`/`gridHeight` y no marcada como plaza), y sin conflicto de slot (ninguna otra entrada ocupa el mismo cuadrante/manzana, ni una manzana-completa choca con una entrada de cuadrante en la misma manzana).
- **Desvinculación total de POIs y peatones**: eliminación completa (no deshabilitado) de:
  - `Runtime/PedestrianNetwork.cs`: `PedestrianNodeKind.PointOfInterest`, `PointOfInterestDescriptor`, `RegisterPointOfInterest`, `ConnectPointOfInterest`, `ReinsertPointsOfInterest`, el campo serializado `pointsOfInterest` y su gizmo de color.
  - `Editor/CityGeneratorPedestrianBuilder.cs`: `RegisterPointsOfInterest` (bucle bench-radial + centerpiece-loop) y su llamada desde el pipeline.
  - `Editor/CityGeneratorSettings.cs`: `PedestrianBehaviourSettings.poiStopDurationMin/Max`.
  - `Editor/CityGeneratorValidator.cs`: las dos validaciones de `poiStopDurationMax`/`Min`.
  - `Runtime/PedestrianAgent.cs`: la rama de estado que trata la llegada a un nodo `PointOfInterest` (parada larga tipo "sentarse").
  - Tests de `Assets/Tests/` que cubren específicamente POI (`RegisterPointOfInterest_RepeatedlyAfterBuild_DoesNotThrow` y cualquier otro test de POI/persistencia).
- Los bancos y el centerpiece de plaza (`PlazaSettings.benchPrefab`/`centerpiecePrefab`) siguen instanciándose igual que hoy, como props visuales normales — sin ninguna llamada a la red peatonal.
- `isPointOfInterest` en `CustomPlaceEntry` se guarda y se muestra en la UI, pero no tiene ningún efecto funcional en esta spec (reservado para un futuro sistema de minimapa/POI que reemplazará al actual).
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`), y las referencias a POI en `README.md`/`README.es.md` si existen.

**Fuera de alcance (para futuras specs):**

- Cualquier uso funcional de `isPointOfInterest` (minimapa, marcador, interacción de peatones con custom places): solo se guarda el dato.
- Overlap-check entre un Custom Place y edificios/otros Custom Places más allá de la reserva de slot (igual que los edificios normales, un prefab sobredimensionado puede clipear visualmente con su vecino — responsabilidad del usuario).
- Rediseño del sistema de paradas/comportamiento de peatones en general (jitter, idle aleatorio en calle): solo se quita la rama POI: el resto de `PedestrianAgent` no cambia.
- Cualquier reordenación estructural mayor de `CityGeneratorWindow`/tabs más allá de añadir la tab "Custom Places" y su contenido.
- Publicación de una nueva versión del package (bump de `version`/tag): esta spec entrega el código; el release es un paso posterior con `Tools > City Generator > Release`.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public List<CustomPlaceEntry> customPlaces = new();

[Serializable]
internal enum CustomPlaceFacing { North, East, South, West } // pasos de 90°, mismo eje que BuildingBuilder's Euler(0, 90*n, 0)

[Serializable]
internal struct CustomPlaceEntry
{
    [Tooltip("Display name for this entry in the tool UI and in validation messages. Required.")]
    public string title;
    [Tooltip("Prefab instantiated at the chosen position. Required.")]
    public GameObject prefab; // required
    [Tooltip("Reserved for a future minimap/POI system. No functional effect yet.")]
    public bool isPointOfInterest;
    [Tooltip("If true, occupies the whole block (all 4 corner slots) instead of a single 22 m corner slot.")]
    public bool occupiesFullBlock;
    [Tooltip("Block (x, y) chosen by clicking this entry's grid preview. Must be within the grid and not a plaza block.")]
    public Vector2Int blockCell;
    [Tooltip("Corner slot within the block (0-3, same convention as CityGeneratorBuildingBuilder.SlotOffsets), chosen by clicking a quadrant. Ignored when occupiesFullBlock is true.")]
    public int cornerSlot;
    [Tooltip("Fixed orientation, in 90° steps. Never randomised, unlike normal buildings.")]
    public CustomPlaceFacing facing;
    // Internal bookkeeping: whether blockCell/cornerSlot were ever set via the grid preview
    // (distinguishes "not placed yet" from a legitimate (0,0) selection), read by the validator.
    public bool positionAssigned;
}
```

Notas:

- `cornerSlot` reutiliza el mismo índice 0-3 que `CityGeneratorBuildingBuilder.SlotOffsets` (no un nuevo enum de esquinas), para que el picker y el builder compartan una única fuente de verdad geométrica.
- `PedestrianBehaviourSettings` pierde `poiStopDurationMin`/`poiStopDurationMax` (eliminados, no deprecados).
- `PedestrianNetwork.PointOfInterestDescriptor` y el node kind `PointOfInterest` se eliminan por completo — no hay modelo de datos nuevo que los sustituya en esta spec (los peatones no interactúan con Custom Places).

## Plan de implementación

1. **Modelo de datos base.** Añadir `CustomPlaceEntry`, `CustomPlaceFacing` y `customPlaces` a `CityGeneratorSettings.cs`; quitar `poiStopDurationMin`/`Max` de `PedestrianBehaviourSettings`. El proyecto compila; sin UI ni builder todavía, la lista queda vacía por defecto.

2. **Eliminar la maquinaria POI de la red peatonal.** En `Runtime/PedestrianNetwork.cs`: quitar `PedestrianNodeKind.PointOfInterest`, `PointOfInterestDescriptor`, `pointsOfInterest`, `RegisterPointOfInterest`, `ConnectPointOfInterest`, `ReinsertPointsOfInterest` y su rama en `Build()`/gizmos. En `Runtime/PedestrianAgent.cs`: quitar la rama de estado para `PointOfInterest`. En `Editor/CityGeneratorPedestrianBuilder.cs`: quitar `RegisterPointsOfInterest` y su llamada. En `Editor/CityGeneratorValidator.cs`: quitar las dos validaciones de `poiStopDuration*`. Actualizar/quitar los tests de `Assets/Tests/` que cubrían específicamente POI. El sistema peatonal sigue funcionando igual (Ring/Curb/Crossing intactos), solo sin paradas en bancos/plaza.

3. **`CityGeneratorCustomPlaceBuilder`.** Nuevo fichero, espejo de `CityGeneratorBuildingBuilder`: dado `customPlaces`, `blocks` y el grupo destino, instancia cada entrada válida (título+prefab+posición asignados) en su posición/rotación fija, devuelve `(List<GameObject> placed, HashSet<(int gridX, int gridY, int slot)> reservedSlots)` donde `slot == -1` representa "manzana completa". Sin UI todavía; se puede probar con datos puestos a mano en el inspector serializado.

4. **`CityGeneratorBuildingBuilder` respeta los slots reservados.** `BuildBuildings` recibe el `HashSet` de slots reservados y los excluye del reparto de esa manzana (o la manzana entera, si el slot es `-1`). Sin cambios de comportamiento cuando el set está vacío.

5. **Cablear el pipeline.** `CityGeneratorContentAssembler.Assemble`: nuevo grupo `CustomPlaces`, llama a `CustomPlaceBuilder` antes que `BuildingBuilder`, añade sus instancias a `obstacles`, y aplica `MarkStatic` al grupo. La ciudad generada por código (sin UI de Custom Places aún) ya refleja entradas puestas a mano en settings.

6. **Validación.** `CityGeneratorValidator.ValidateDetailed` gana los 4 checks bloqueantes por entrada (título, prefab, posición, conflicto de slot/plaza), con paths registrados para el resaltado de tab/card.

7. **Picker visual reutilizable.** Extraer/generalizar `CityGeneratorGridPreview` para soportar un modo de selección única con cuadrantes (usado por cada entrada de Custom Place), sin romper el modo multi-toggle que sigue usando la tab City para plazas.

8. **Tab "Custom Places" en `CityGeneratorWindow`.** Nueva pestaña en `CityGeneratorTabBar`, card de lista (añadir/quitar entradas, cada una con título, prefab, toggle "Is Point of Interest", toggle "Occupies Full Block", el grid preview de selección única + cuadrante, selector de `facing`). Badge de card y resaltado en rojo por validación, igual que el resto de cards.

9. **Valores por defecto y regresión.** No se añade ningún Custom Place por defecto en `CityGeneratorDefaultAssets` (lista vacía es un estado válido). Generar la escena de test (`Assets/Scenes/City.unity`) con un par de entradas de ejemplo (una 1/4 de manzana, una manzana entera) para verificar visualmente, sin comprometerla al repo como parte permanente si no aporta valor de demo — decisión final durante `/spec-impl`.

10. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`): añadir Custom Places, quitar/mover la mención de paradas en POI de peatones. Revisar `README.md`/`README.es.md` por referencias a POI/paradas peatonales que ya no apliquen.

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con `customPlaces: List<CustomPlaceEntry>` y `CustomPlaceEntry`/`CustomPlaceFacing` tal como se definieron.
- [x] `PedestrianBehaviourSettings` ya no tiene `poiStopDurationMin`/`poiStopDurationMax`; `CityGeneratorValidator` ya no valida esos campos.
- [x] `PedestrianNetwork` ya no contiene `PedestrianNodeKind.PointOfInterest`, `PointOfInterestDescriptor`, `RegisterPointOfInterest`, `ConnectPointOfInterest` ni `ReinsertPointsOfInterest`; `PedestrianAgent` ya no tiene rama de estado para POI.
- [x] `CityGeneratorPedestrianBuilder` ya no registra bancos/centerpiece como POIs; los bancos y el centerpiece se siguen instanciando visualmente igual que antes de esta spec.
- [x] Generar una ciudad con un Custom Place de 1/4 de manzana: el prefab elegido aparece exactamente en la esquina/manzana/orientación configuradas, y esa esquina no recibe un edificio aleatorio (las otras 3 sí).
- [x] Generar una ciudad con un Custom Place de manzana entera: el prefab aparece centrado en esa manzana, y ningún edificio aleatorio se coloca en ninguna de sus 4 esquinas.
- [x] Un Custom Place aparece en la lista `obstacles`: un prop/vegetación generado cerca no se solapa con él.
- [x] La tab "Custom Places" permite añadir/quitar entradas, asignar título/prefab/toggle POI/toggle manzana completa/orientación, y elegir la posición con el grid visual (clic en manzana, clic en cuadrante si aplica).
- [x] Intentar generar con una entrada sin título, sin prefab, sin posición asignada, o con conflicto de slot (dos entradas en el mismo cuadrante, o una entrada apuntando a una manzana marcada como plaza) bloquea ambos botones de Build y resalta la tab/card correspondiente en rojo, sin diálogo bloqueante (mismo mecanismo que el resto de validaciones).
- [x] La lista `customPlaces` vacía por defecto no cambia el comportamiento de generación existente (ninguna regresión en `CityGeneratorBuildingBuilder` cuando no hay entradas).
- [x] Los tests de `Assets/Tests/` relacionados con POI se eliminan o adaptan; la suite EditMode/PlayMode/Performance sigue pasando en su totalidad.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo Custom Places y la eliminación de paradas peatonales en POI.

## Decisiones tomadas y descartadas

- **Custom Place de manzana entera solo en manzanas normales, nunca en plaza.** Descartado permitirlo sobre una manzana plaza (que sustituiría lawn/centerpiece/bancos): mismo criterio que los edificios normales, que tampoco pueden colocarse en una manzana plaza. Mantiene una única regla "una manzana es o plaza o edificable" sin casos especiales nuevos.
- **Un Custom Place de 1/4 coexiste con edificios normales en los otros 3 cuadrantes de su manzana.** Descartado bloquear la manzana entera al primer Custom Place parcial: perdería densidad de edificios sin necesidad y complicaría la UX (el usuario tendría que llenar los 4 cuadrantes con Custom Places para conseguir una manzana completamente customizada, en vez de usar el toggle "Occupies Full Block" que ya existe para ese caso).
- **Los Custom Places se añaden a la lista compartida `obstacles`.** A diferencia de los edificios normales (deliberadamente exentos de overlap-check), un Custom Place sí participa: coherente con que props/vegetación ya evitan solaparse entre sí, y evita que un lamp/banco/árbol generado aleatoriamente termine dentro de un modelo que el usuario colocó a propósito.
- **Conflicto de slot es un error de validación bloqueante, no resolución silenciosa por orden de lista.** Mismo patrón que el resto de `CityGeneratorValidator` (prefabs vacíos, porcentajes que no suman 100): errores de configuración se comunican explícitamente antes de generar, nunca se resuelven adivinando la intención del usuario.
- **Orientación fija elegida manualmente (4 direcciones, pasos de 90°), nunca aleatoria.** A diferencia de `CityGeneratorBuildingBuilder` (rotación aleatoria por slot), un Custom Place es una elección deliberada del usuario — tiene sentido que también controle su orientación final, no que dependa del azar del seed.
- **Eliminación completa de la maquinaria POI de la red peatonal, no solo desactivación.** El usuario pidió desvincular "toda relación" entre POIs y peatones, y planea rediseñar esa integración en el futuro apoyándose en Custom Places; dejar código muerto (node kind, descriptor, métodos Register/Connect) solo añadiría superficie a mantener sin ningún consumidor. El futuro sistema parte de cero.
- **Los bancos y el centerpiece de plaza no cambian de comportamiento.** Solo se retira su registro como nodos POI en `PedestrianNetwork`; `PlazaBuilder` los sigue instanciando exactamente igual — son props visuales, nunca dependieron de la red peatonal para existir.
- **`isPointOfInterest` en `CustomPlaceEntry` se guarda sin efecto funcional.** Es un placeholder deliberado para el futuro sistema de minimapa/POI mencionado por el usuario; implementarlo ahora sería adelantar trabajo de una spec que aún no está definida.
- **`cornerSlot` reutiliza el índice 0-3 de `CityGeneratorBuildingBuilder.SlotOffsets`** en vez de definir un enum de esquinas nuevo (NE/NW/SE/SW): una sola fuente de verdad geométrica entre el picker, el builder de Custom Places y el builder de edificios.
- **El picker de posición vive dentro de cada entrada de la lista** (mini grid preview por Custom Place), no un único grid compartido arriba de la tab: consistente con que cada entrada es autocontenida (mismo patrón que `CityGeneratorWeightedPrefabList`), y evita el estado adicional de "qué entrada estoy editando ahora mismo" que un grid compartido necesitaría.

## Riesgos identificados

- **Generalizar `CityGeneratorGridPreview` para selección única + cuadrantes puede romper el modo multi-toggle de plazas** si la refactorización no aísla bien el estado de cada modo. Mitigación: cubrir ambos modos con la suite EditMode/PlayMode existente y una verificación manual de la tab City tras el cambio, antes de dar por cerrado el paso 7 del plan.
- **Eliminar tests de POI reduce la cobertura de `PedestrianNetwork`** en la zona de POI/persistencia que ya había atrapado una regresión real (SPEC 05, `IndexOutOfRangeException` en `nodeComponent`). Mitigación: al quitar `PointOfInterest` del enum, esa clase entera de bug deja de ser alcanzable (no hay más nodos que crecer dinámicamente tras `Build()`), así que la cobertura perdida no protege código que sigue existiendo.
- **Ajustar `CityGeneratorBuildingBuilder.BuildBuildings` para aceptar slots reservados** toca una firma pública/interna ya usada por `CityGeneratorContentAssembler`; un desajuste en el orden de llamada (Custom Places después de Buildings, por error) haría que un edificio aleatorio y un Custom Place se solapen físicamente sin que ningún validador lo detecte (la validación de conflicto de slot solo compara Custom Places entre sí, no contra el resultado de `BuildingBuilder`). Mitigación: el paso 5 del plan fija explícitamente el orden en el pipeline, y el criterio de aceptación de "1/4 de manzana" cubre esto manualmente.
