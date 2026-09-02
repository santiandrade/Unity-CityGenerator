# SPEC 12 — Custom Pedestrians

> **Estado:** Implementado
> **Depende de:** SPEC 03 (Red peatonal), SPEC 06 (Custom Places), SPEC 10 (Rutas peatonales interiores de manzana), SPEC 11 (Custom Grid)
> **Fecha:** 2026-09-01
> **Objetivo:** Añadir "Custom Pedestrians" — una nueva card en la tab Pedestrians donde, por cada entrada (prefab + cantidad), el usuario traza a mano sobre el grid de la ciudad una red de nodos peatonales conectados entre sí, y esos peatones quedan confinados a moverse únicamente dentro de esa red, en vez de recorrer la ciudad entera como un peatón normal.

## Scope

**Dentro:**

- **Nueva card "Custom Pedestrians"** en la tab Pedestrians de `CityGeneratorWindow`, con una lista de entradas `CustomPedestrianEntry` (añadir/quitar, misma convención que `customPlaces`/`vehicles`/`pedestrians`).
- **`Editor/CityGeneratorSettings.cs`** — `struct CustomPedestrianEntry { title, prefab, count, selectedNodeIndices }` y `List<CustomPedestrianEntry> customPedestrians` en `CityGeneratorSettings`.
- **Picker visual por entrada**: un nuevo modo de `CityGeneratorGridPreview` que, en vez de manzanas/cuadrantes, dibuja el grid de la ciudad como **zonas clicables** (líneas), no como puntos individuales — ver "Actualización (2026-09-02): picker por zonas" más abajo para el diseño final. El usuario hace clic para añadir/quitar zonas de la selección de esa entrada; un clic solo se acepta si la zona comparte un nodo real con la selección actual, salvo el primer clic (libre). Cada zona seleccionada se dibuja resaltada. Quitar una zona que partiría la selección en varios grupos conexos deja solo el grupo conexo más grande tras la deselección.
- **Montaje temporal de preview**: para poder mostrar el picker antes de generar la ciudad, se construye en un `GameObject` oculto y desechable la parte determinista de `CityGeneratorTrafficBuilder` (colocación de `TrafficLightIntersection`, sin semáforos reales ni resto de tráfico) más `PedestrianNetwork.Build()`/`BuildFromBlockCells()` completo, reutilizando el código real de generación (nunca una reimplementación paralela de la geometría), a partir de los settings actuales (grid o Custom Grid, plazas, Custom Places). Se reconstruye cuando cambian los settings relevantes y se destruye tras leer los nodos.
- **`Editor/CityGeneratorCustomPedestrianBuilder.cs`** (nuevo) — instancia `count` copias del prefab de cada entrada, repartidas sobre los nodos de `selectedNodeIndices` de esa entrada (mismo criterio de spawn que un peatón normal: solo sobre nodos `Ring`), y configura cada `PedestrianAgent` generado con su subconjunto de nodos permitido.
- **Restricción de ruta en runtime**: `PedestrianNetwork`/`PedestrianAgent` ganan la capacidad de confinar `PickRandomDestination`/`FindPath` a un subconjunto de nodos dado (nunca planifican ni caminan fuera de `selectedNodeIndices`). Un peatón normal (sin restricción) no cambia de comportamiento.
- **`Editor/CityGeneratorValidator.cs`** — nuevos issues bloqueantes por entrada: título no vacío, prefab asignado, `count >= 1`, al menos 2 nodos seleccionados, y el conjunto de nodos seleccionados forma un único componente conexo (defensa en profundidad; el picker ya debería impedir construir un conjunto inválido, pero los datos serializados se validan igual que el resto de cards).
- **Pipeline**: `CityGeneratorCustomPedestrianBuilder` corre en `CityGeneratorContentAssembler` después de que la `PedestrianNetwork` real ya está construida (después de `PedestrianBuilder`'s `AddNetworkComponent`+`Build()`), reutilizando la misma instancia real (no la de preview) para resolver a qué nodos reales corresponden los índices guardados.
- Los Custom Pedestrians generados se añaden al recuento total de peatones de forma independiente de `pedestrianCount` (no restan del general) y se registran en `PedestrianManager` igual que cualquier otro `PedestrianAgent` (separación, staggering, etc.).
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`), `docs/architecture/pedestrians.md` y `docs/architecture/editor-tool.md`.

**Fuera de alcance (para futuras specs):**

- Exclusión de mobiliario urbano (`CityGeneratorStreetPropsBuilder`) sobre nodos usados por una ruta Custom Pedestrian: un nodo de una ruta custom puede quedar `Blocked` por un prop igual que le pasa hoy a cualquier nodo `Ring`/`Interior` normal (riesgo ya aceptado en SPEC 10).
- Exclusividad de nodos entre entradas: varias entradas Custom Pedestrian pueden compartir/solapar nodos sin ninguna validación de conflicto.
- Cualquier orden o secuencia de recorrido fija: la selección es una red (subgrafo conexo), no un camino con un orden de paso obligatorio — el peatón elige rutas libremente dentro de esa red, igual que un peatón normal elige rutas dentro de toda la ciudad.
- Rediseño de `PickRandomDestination`/`FindPath` para peatones normales: sus firmas ganan un parámetro opcional de restricción, pero su comportamiento por defecto (sin restricción) es idéntico al actual.
- Cambios en `PedestrianManager`'s separación/staggering para distinguir Custom Pedestrians de peatones normales.
- Publicación de una nueva versión del package: esta spec entrega el código; el release es un paso posterior.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public List<CustomPedestrianEntry> customPedestrians = new();

[Serializable]
internal struct CustomPedestrianEntry
{
    [Tooltip("Display name for this entry in the tool UI and in validation messages. Required.")]
    public string title;
    [Tooltip("Prefab instantiated at each spawn node. Required.")]
    public GameObject prefab; // required
    [Tooltip("Number of agents of this prefab spawned across this entry's node network. Must be >= 1.")]
    public int count;
    [Tooltip(
        "Indices into the deterministic node ordering PedestrianNetwork.Build()/BuildFromBlockCells() " +
        "produces for the current settings (grid or Custom Grid, plazas, Custom Places). Chosen via " +
        "this entry's grid preview (node-graph picker). Must contain at least 2 indices forming a " +
        "single connected component in the real pedestrian graph. Internal bookkeeping only -- never " +
        "shown as raw numbers in the UI.")]
    public List<int> selectedNodeIndices;
}
```

Notas:

- No se introduce ningún nuevo tipo de nodo ni cambio en `PedestrianNodeKind`: una entrada puede mezclar `Ring`, `Interior`, `Curb` y `Crossing` libremente, siempre que formen un único componente conexo por aristas reales del grafo.
- `selectedNodeIndices` guarda índices, no posiciones — son válidos mientras la configuración que determina el grafo (grid/Custom Grid, plazas, Custom Places) no cambie; si el usuario cambia esa configuración después de trazar una ruta, la card se re-valida (mismo mecanismo que ya invalida configuraciones de Custom Places al cambiar el grid) y puede requerir volver a trazar la ruta. Se detalla en el plan de implementación cómo se detecta esta invalidación.
- `PedestrianNetwork` gana un mecanismo runtime (sin nuevo tipo de dato serializado propio de esta spec, más allá de lo que cada `PedestrianAgent` necesita) para restringir `PickRandomDestination`/`FindPath` a un subconjunto de nodos — el subconjunto en sí vive en el `PedestrianAgent` generado (una lista/array de índices de nodo, escrito por `CityGeneratorCustomPedestrianBuilder.BuildCustomPedestrians` igual que el resto de campos de comportamiento), no en `PedestrianNetwork`.

## Plan de implementación

1. **Modelo de datos base.** Añadir `CustomPedestrianEntry` y `customPedestrians` a `CityGeneratorSettings.cs`. El proyecto compila; sin UI ni builder todavía, la lista queda vacía por defecto.

2. **Restricción de ruta en runtime.** Ampliar `PedestrianNetwork.PickRandomDestination` y `PedestrianNetwork.FindPath` con un parámetro opcional de restricción (p. ej. `IReadOnlyList<int> allowedNodes = null`): sin él, comportamiento idéntico al actual; con él, `PickRandomDestination` solo devuelve nodos de esa lista y `FindPath` solo expande vecinos que estén en ella. `PedestrianAgent` gana un campo opcional (nulo para un peatón normal) con el subconjunto de nodos permitido, usado en `PlanNewDestination` cuando está presente. Manual test: con un `PedestrianAgent` de prueba configurado a mano con un subconjunto pequeño, confirmar en Play que nunca sale de esos nodos; sin restricción, confirmar que el comportamiento no cambia frente a antes del cambio.

3. **Montaje temporal de preview.** Nuevo `Editor/CityGeneratorPedestrianPreview.cs`: dado el estado actual de settings (grid/Custom Grid, `plazaCells`, `customPlaces`), construye en un `GameObject` oculto (`HideFlags.HideAndDontSave`, nunca guardado en la escena) la colocación determinista de `TrafficLightIntersection` (reutilizando la parte de `CityGeneratorTrafficBuilder` que las coloca, sin semáforos de verdad ni vehículos) y luego una `PedestrianNetwork` real (`Build()`/`BuildFromBlockCells()`), expone `NodeCount`/`GetNode(i)`/vecinos para lectura, y se destruye (`DestroyImmediate`) cuando el picker se cierra o los settings relevantes cambian. Manual test: invocar el preview desde un menú de depuración temporal o un test EditMode, confirmar que `NodeCount` y las posiciones coinciden con las de una ciudad generada de verdad con los mismos settings.

4. **Modo "node graph" de `CityGeneratorGridPreview`.** Nuevo modo que consume el `CityGeneratorPedestrianPreview` del paso 3, con el picker por zonas descrito en "Actualización (2026-09-02)": agrupa el grafo real en zonas clicables (arista de Ring, radio de Interior, línea de cruce) y dibuja cada una como una línea, resaltando las zonas cuyos nodos están todos seleccionados. Clic en una zona no seleccionada la añade solo si comparte un nodo con la selección actual (o si la selección está vacía); clic en una zona seleccionada la quita (protegiendo los nodos que siga usando otra zona todavía seleccionada), y si eso parte el resto en varios componentes conexos, se conserva solo el mayor. Manual test: en una ventana de prueba con una entrada `CustomPedestrianEntry` puesta a mano, trazar una red pequeña a golpe de clic sobre las líneas y verificar visualmente el resaltado y las reglas de adyacencia/desconexión.

5. **`CityGeneratorCustomPedestrianBuilder`.** Nuevo fichero: dado `customPedestrians` y la `PedestrianNetwork` **real** ya construida (no la de preview), por cada entrada válida instancia `count` copias del prefab repartidas sobre los nodos `Ring` de `selectedNodeIndices` (mismo criterio de spawn que un peatón normal), configura cada `PedestrianAgent` con el subconjunto de nodos permitido (del paso 2) y con los mismos campos de comportamiento (`CityGeneratorSettings.pedestrianBehaviour`) que un peatón normal, y los registra igual que `BuildPedestrians`. Manual test: con una entrada puesta a mano en el inspector serializado, generar una ciudad y confirmar en Play que esos agentes aparecen y quedan confinados a la red elegida.

6. **Cablear el pipeline.** `CityGeneratorContentAssembler.Assemble` llama a `CityGeneratorCustomPedestrianBuilder` justo después de que la `PedestrianNetwork` real termine su `Build()`/poda de obstáculos. Manual test: generar la ciudad de test con una entrada de ejemplo y confirmar que convive sin errores con peatones normales, vehículos y Custom Places.

7. **Validación.** `CityGeneratorValidator.ValidateDetailed` gana los checks bloqueantes por entrada (título, prefab, `count >= 1`, ≥2 nodos, componente conexo único usando el `CityGeneratorPedestrianPreview` del paso 3 para resolver el grafo actual). Manual test: provocar cada caso de error a mano en los datos serializados y confirmar que bloquea Build/Re-Build y resalta la tab/card en rojo.

8. **Invalidación por cambio de settings.** Cuando cambian grid/Custom Grid, `plazaCells` o `customPlaces` de forma que el grafo de preview ya no tiene los mismos índices de nodo (detectado comparando `NodeCount`/una huella simple del grafo, p. ej. hash de posiciones, contra el guardado la última vez que se editó cada entrada), se marca la entrada como "ruta desactualizada" (mismo patrón visual que otros badges de aviso) y sus `selectedNodeIndices` se limpian al abrir su picker, obligando a volver a trazarla. Manual test: trazar una ruta, cambiar el tamaño del grid, reabrir la card y confirmar que la entrada se marca y su selección se limpia en vez de apuntar a nodos equivocados en silencio.

9. **Tab Pedestrians: card "Custom Pedestrians".** Nueva card en `CityGeneratorWindow` con lista de entradas (título, prefab, count, el picker del paso 4), badge/resaltado en rojo por validación igual que el resto de cards.

10. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`), `docs/architecture/pedestrians.md` (nueva sección Custom Pedestrians, mecanismo de restricción de ruta) y `docs/architecture/editor-tool.md` (nuevo modo de `CityGeneratorGridPreview`, montaje temporal de preview).

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con `customPedestrians: List<CustomPedestrianEntry>` y `CustomPedestrianEntry` tal como se definió.
- [x] `PedestrianNetwork.PickRandomDestination`/`FindPath` aceptan un parámetro opcional de restricción a un subconjunto de nodos; sin él, el comportamiento y las llamadas existentes de `PedestrianAgent` no cambian.
- [x] La card "Custom Pedestrians" (tab Pedestrians) permite añadir/quitar entradas, asignar título/prefab/count, y trazar la red con el picker visual por zonas (clic para añadir/quitar una zona, solo zonas que comparten un nodo con la selección actual salvo la primera, resaltado por cada zona seleccionada).
- [x] Quitar una zona puente de la selección conserva solo el componente conexo más grande restante, sin dejar la selección en un estado desconectado.
- [x] El picker representa los 4 tipos de nodo (Ring, Interior, Curb, Crossing) agrupados en zonas clicables, correctamente posicionadas **antes** de generar la ciudad por primera vez en la escena, coincidiendo exactamente con el grafo que produce una generación real con los mismos settings.
- [x] Generar una ciudad con una entrada Custom Pedestrian de ≥2 nodos conectados: aparecen exactamente `count` instancias del prefab elegido, spawneadas sobre nodos `Ring` de esa red.
- [x] En Play mode, observado durante una ventana razonable, un Custom Pedestrian nunca camina fuera de los nodos de `selectedNodeIndices` de su entrada.
- [x] Un peatón normal (`pedestrianCount`) sigue pudiendo recorrer toda la ciudad exactamente igual que antes de esta spec; el total de peatones en escena es `pedestrianCount` + suma de `count` de todas las entradas Custom Pedestrian.
- [x] Intentar generar con una entrada sin título, sin prefab, `count < 1`, menos de 2 nodos seleccionados, o con nodos que no forman un único componente conexo, bloquea ambos botones de Build y resalta la tab/card correspondiente en rojo.
- [x] Cambiar el grid/Custom Grid/plazas/Custom Places tras trazar una ruta marca la entrada afectada como desactualizada y limpia su selección al reabrir el picker, en vez de generar silenciosamente sobre nodos equivocados.
- [x] La lista `customPedestrians` vacía por defecto no cambia el comportamiento de generación existente (ninguna regresión en peatones normales, tráfico o Custom Places).
- [x] `docs/architecture/pedestrians.md` y `docs/architecture/editor-tool.md` documentan Custom Pedestrians, el mecanismo de restricción de ruta y el montaje temporal de preview.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo Custom Pedestrians.

## Decisiones tomadas y descartadas

- **El picker de nodos se resuelve con un montaje temporal oculto que reutiliza el código real de generación** (`CityGeneratorTrafficBuilder`'s colocación de intersecciones + `PedestrianNetwork.Build()`/`BuildFromBlockCells()`), en vez de reimplementar la geometría del grafo peatonal por separado para el Editor. Descartado duplicar la lógica: dos implementaciones de la misma geometría inevitablemente divergerían con el tiempo (nuevo tipo de nodo, cambio de constante, etc.), y este proyecto ya trata "una sola fuente de verdad" como invariante (ver `obstacles`, `SlotOffsets`).
- **Curb/Crossing se incluyen en el picker desde el primer momento**, aceptando el coste de construir también la parte de colocación de semáforos en el montaje temporal, en vez de limitar el picker a Ring/Interior hasta la primera generación real. El usuario quería poder trazar rutas que crucen calles con semáforo sin depender de haber generado antes.
- **La selección es un subgrafo conexo arbitrario (con bifurcaciones/bucles), no un camino lineal ni una secuencia ordenada de clics.** Decisión explícita del usuario: quiere definir una "red" de nodos por la que el peatón elige rutas libremente, no un recorrido fijo paso a paso.
- **Quitar un nodo puente conserva el componente conexo más grande restante**, en vez de rechazar la deselección. Decisión explícita del usuario, priorizando fluidez de edición sobre evitar sorpresas.
- **Sin exclusividad de nodos entre entradas.** Varias entradas Custom Pedestrian pueden compartir zona sin restricción — decisión explícita del usuario, coherente con que son peatones normales restringidos, no "dueños" de una zona.
- **El `count` de cada entrada es un presupuesto independiente de `pedestrianCount`**, no resta del general. Decisión explícita del usuario.
- **Un nodo de una ruta Custom Pedestrian puede quedar `Blocked` por mobiliario urbano igual que hoy le pasa a Ring/Interior**, sin ninguna exclusión nueva en `CityGeneratorStreetPropsBuilder`. Decisión explícita del usuario tras aclarar el caso concreto (un prop puede caer sobre un nodo elegido a mano en una generación posterior): mismo riesgo ya aceptado en SPEC 10, no se introduce mecanismo nuevo para evitarlo.
- **Curb/Crossing se seleccionan y conectan con la misma regla de adyacencia directa que Ring/Interior**, sin reglas especiales de cruce nuevas — `CanCross`/espera de semáforo ya existente se reutiliza sin cambios.
- **Cambiar la configuración que determina el grafo invalida las rutas ya trazadas (limpieza + aviso) en vez de intentar remapear silenciosamente los índices guardados.** Remapear correctamente sería frágil (el grafo puede ganar/perder nodos de formas no triviales) y un remapeo erróneo generaría una ciudad silenciosamente distinta a la que el usuario diseñó.
- **Hallazgo de QA manual (2026-09-01/02): los prefabs de demo `Animal-Cat`/`Animal-Dog` (`DefaultAssets/Prefabs/Pets/`) no reproducían su animación de marcha al añadirlos como Custom Pedestrian**, pese a que `PedestrianAgent` escribía `Speed`/`Grounded` con normalidad. Dos causas independientes, ambas en el contenido, no en `PedestrianAgent`/`CityGeneratorCustomPedestrianBuilder`: (1) su rig (`MeshRenderer`s rígidos por extremidad, sin `SkinnedMeshRenderer`) se congelaba bajo `Animator.cullingMode = Cull Completely` — heredado sin más de la convención de `Characters/` — porque ese modo de culling no resuelve visibilidad para ese tipo de rig; arreglado generalizando `CityGeneratorPedestrianBuilder.ApplyAnimatorCullingMode` (usado por ambos builders) para elegir `Cull Completely`/`Always Animate` según haya o no `SkinnedMeshRenderer`. (2) los clips `idle`/`walk`/`run` de `animal-cat.fbx`/`animal-dog.fbx` tenían `Loop Time` desactivado (valor por defecto de un clip auto-generado por take, `ModelImporter.clipAnimations` vacío), así que la pose se clavaba en el último frame tras la primera pasada aunque el estado del Animator siguiera avanzando; arreglado activando `loopTime`/`loopPose` en `ModelImporter.clipAnimations` para ambos FBX. Detalle completo en `docs/architecture/pedestrians.md`'s sección "Animator culling mode" y en `docs/architecture/demo-content.md`'s bullet `Pets/`.

## Actualización (2026-09-02): picker por zonas

**Problema de QA manual:** el picker original (un punto clicable por nodo real) resultaba impráctico en ciudades con varias manzanas — hasta 13 nodos por manzana (8 de Ring + 5 de Interior) más los de Curb/Crossing de cada cruce con semáforo, todos apretados en el mismo espacio de la miniatura, hacían casi imposible acertar al nodo deseado con el ratón.

**Solución adoptada:** en vez de nodos sueltos, el picker agrupa el grafo real en **zonas clicables**, dibujadas como líneas en vez de puntos — mucho más fáciles de acertar porque el área de clic es toda su longitud, no un punto de pocos píxeles. Cada zona sigue resolviéndose internamente a un subconjunto de nodos reales, así que `selectedNodeIndices` no cambia de forma ni el resto del pipeline (`CityGeneratorCustomPedestrianBuilder`, `CityGeneratorValidator`, runtime) se entera de que existen zonas — es un cambio exclusivo de `CityGeneratorGridPreview`'s modo `NodeGraphPicker`.

Las zonas se derivan del grafo real por patrón de tipo/grado de nodo, nunca de la numeración interna de manzanas (que el picker no conoce, solo ve `PedestrianNetwork` a través de `CityGeneratorPedestrianPreview`), así que el mismo código vale para grid rectangular y Custom Grid sin distinción:

- **Arista de Ring**: cada arista real entre dos nodos `Ring` es su propia zona (2 nodos). Un anillo de manzana normal aporta 8 — 2 por lado —, y las tiras del paseo perimetral (SPEC 11) aportan las suyas igual, sin caso especial.
- **Radio de Interior**: se detecta el nodo centro de una cruz Interior (`Kind == Interior` con sus 4 vecinos también `Interior`); cada uno de sus 4 vecinos (`arm`) más el vecino de ese `arm` que no es el centro (el midpoint de Ring al que se conecta) forman una zona de 3 nodos `[centro, arm, midpoint]`. Una manzana con una Custom Place a manzana completa o una plaza no tiene cruz Interior, luego tampoco radios — igual que hoy no tiene nodos Interior.
- **Línea de cruce**: se detecta cada nodo `Crossing` (grado 2, ambos vecinos `Curb`) y se expande un salto más a cada lado hasta el nodo `Ring` de cada acera — zona de 5 nodos `[ladoA, curbNear, crossing, curbFar, ladoB]`. Una manzana/cruce sin `Traffic Light Prefab` asignado sigue sin generar ninguna, igual que antes.

Reglas de selección, expresadas sobre zonas en vez de nodos sueltos (mismo espíritu que las decisiones ya tomadas para nodos):

- Una zona está "seleccionada" cuando **todos** sus nodos reales están en `selectedNodeIndices`.
- Clic en una zona no seleccionada la añade si la selección está vacía o si comparte al menos un nodo con la selección actual (equivalente a "vecino directo", porque las zonas solo se tocan en un nodo compartido real).
- Clic en una zona seleccionada la quita, pero nunca borra un nodo que siga perteneciendo a **otra** zona que sigue completamente seleccionada (evita romper visualmente una zona vecina que el usuario no tocó); tras la quita se aplica el mismo `KeepLargestConnectedComponent` de siempre sobre el conjunto de nodos resultante.
- El picker ya no dibuja los nodos reales como puntos — solo las líneas de zona (color por tipo: Ring verde, Interior azul, Crossing naranja; resaltado en amarillo/grosor mayor cuando está seleccionada). Descartado añadir puntos de referencia en los cruces de zonas: la meta explícita era reducir el ruido visual, no solo el número de elementos clicables.

Esto no cambia el modelo de datos (`CustomPedestrianEntry.selectedNodeIndices` sigue siendo índices de nodo), ni `CityGeneratorPedestrianPreview`, ni `PedestrianNetwork`, ni el builder, ni el validator — todos siguen operando sobre nodos, ajenos a que el picker los agrupe visualmente en zonas.

## Riesgos identificados

- **El montaje temporal de preview debe producir exactamente el mismo grafo (mismo número de nodos, mismo orden) que la generación real**, o los índices guardados en `selectedNodeIndices` apuntarían a nodos equivocados sin que nada lo detecte. Mitigación: ambos caminos comparten literalmente el mismo código (`PedestrianNetwork.Build()`/`BuildFromBlockCells()`, misma lógica de colocación de intersecciones), así que solo pueden divergir si el pipeline real cambia el orden en que llama a esas piezas — el paso 8 (invalidación por huella del grafo) actúa como red de seguridad si eso llega a ocurrir.
- **Construir un montaje temporal completo (intersecciones + red peatonal) cada vez que se abre un picker o cambian los settings puede ser lento** en grids grandes, notándose como lag en la UI del Editor. Mitigación: cachear el resultado mientras los settings relevantes no cambien (mismo criterio que ya usa el resto de estimadores de la ventana), reconstruyendo solo bajo demanda.
- **La regla de "conservar el componente conexo más grande" al quitar un nodo puente puede sorprender al usuario** borrando nodos que no tocó directamente. Mitigación: decisión explícita y confirmada por el usuario; se puede mitigar en UX (no en esta spec) resaltando qué nodos se van a perder antes de confirmar la deselección, si el manual QA lo pide.
- **Ampliar `FindPath`/`PickRandomDestination` con un parámetro de restricción toca rutas ya usadas por todo peatón normal.** Un error en la rama "sin restricción" rompería el sistema peatonal existente para todos los usuarios del tool, no solo para Custom Pedestrians. Mitigación: el criterio de aceptación de "peatón normal sin regresión" cubre esto explícitamente, y el parámetro se diseña con valor por defecto `null`/comportamiento idéntico al actual.
