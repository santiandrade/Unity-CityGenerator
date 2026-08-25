# SPEC 05 — Rendimiento y suite de tests (informe técnico 2026-08-25)

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 02 (Unity Package Distribution), SPEC 03 (Red peatonal), SPEC 04 (Correcciones críticas y arquitectónicas)
> **Fecha:** 2026-08-25
> **Objetivo:** Añadir la suite de tests EditMode/PlayMode/Performance que faltaba, medir un baseline real de generación y de tráfico/peatones en runtime, y usar esas mediciones para sustituir el solapamiento O(n²) de colocación por un índice espacial, reducir el coste físico del tráfico (`SyncTransforms`, sensores) y escalar el pathfinding/separación peatonal — siguiendo el orden "medir antes de optimizar" que el propio informe técnico recomienda y que el SPEC 04 dejó reservado explícitamente para esta ronda.

## Por qué existe esta spec

`docs/technical-review-2026-08-25.md` agrupa 19 mejoras priorizadas. El SPEC 04 abordó el bloque crítico completo más los ítems de prioridad alta que eran correcciones arquitectónicas puras (framerate global, validación de inputs, tooling interno, singletons de managers, autoridad del Input System), dejando explícitamente para "un SPEC 05 posterior" los ítems 6 (suite de tests), 7 (índice espacial de solapamiento), 8 (coste físico del tráfico) y 9 (pathfinding y separación peatonal) — siguiendo la recomendación del propio informe de no optimizar sin medir antes. Este spec es ese SPEC 05.

Los ítems de prioridad media/baja sobre contenido demo y documentación (12, 14, 15, 17, 18, 19) quedan fuera, para specs futuros aún sin numerar, por no tener dependencia con el trabajo de rendimiento de este spec.

## Scope

**Dentro:**

- **Ítem 6 — Suite de tests EditMode/PlayMode/Performance.**
  - **`Assets/Tests/EditMode/` (nuevo, fuera del package)** — tests de: `CityGeneratorGrid` (bloques, plazas), `CityGeneratorDistributionUtility` (reparto de porcentajes), `CityGeneratorValidator`/`ValidateDetailed` (incluye los 7 huecos ya cubiertos por SPEC 04), pesos de ruta (`RouteWeight`/`Ring`), `PedestrianNetwork.FindPath` (BFS), persistencia de POI (`PointOfInterestDescriptor` sobrevive a `Build()`), y determinismo (misma semilla ⇒ mismo resultado).
  - **`Assets/Tests/PlayMode/` (nuevo)** — tests de: ciclo de `TrafficLightIntersection` (fases verde/ámbar/rojo), cruce peatonal (`CanCross` solo en rojo), registro/desregistro de agentes en `TrafficManager`/`PedestrianManager` (incluye reactivación tras `SetActive`), dos ciudades generadas en la misma escena con managers independientes, y un vehículo/peatón de prueba con collider solo en un hijo (verificación automatizada de lo que hoy es manual en SPEC 04).
  - **`Assets/Tests/EditMode/Generation/` (nuevo)** — generación completa con semilla fija en rejillas 1×3, 5×5 y 10×10, comprobando ausencia de excepciones y invariantes básicos (número de bloques, de edificios colocados, de nodos de red).
  - **`Assets/Tests/Performance/` (nuevo)** — usando `com.unity.test-framework.performance` (añadido al `manifest.json` del proyecto, no al `package.json` del package instalable, ya que los tests viven fuera de él): tiempo de generación por tamaño de rejilla, `GC Alloc` de generación y de un frame en runtime con tráfico/peatones activos, coste de `Physics.SyncTransforms`, coste de `TrafficManager.Update`/`PedestrianManager.Update`, y memoria aproximada de la escena generada.
  - Estos tests son la base de referencia ("baseline") que se mide antes de tocar nada de los ítems 7-9, y vuelven a ejecutarse después de cada uno para registrar el efecto real.

- **Ítem 7 — Índice espacial para solapamientos.**
  - **`Editor/CityGeneratorSpatialHash.cs` (nuevo)** — hash espacial uniforme, tamaño de celda derivado del tamaño de bloque de `CityGeneratorConstants`. Al insertar un obstáculo se registran las celdas de su `Rect`; al probar un candidato se consultan solo sus celdas y las vecinas.
  - **`Editor/CityGeneratorPlacementEngine.cs`** — sustituye la comparación lineal contra la lista completa de obstáculos por una consulta al hash espacial. La lista `obstacles` que hoy se pasa entre categorías (ver CLAUDE.md) se mantiene como fuente de verdad; el hash es una estructura auxiliar de índice sobre ella, no la reemplaza.

- **Ítem 8 — Coste físico del tráfico (las 4 etapas).**
  - **Etapa 1 — `Runtime/TrafficNetwork.cs`** — `Physics.SyncTransforms()` solo se llama cuando `TrafficManager` tiene al menos un agente registrado.
  - **Etapa 2 — `Runtime/TrafficManager.cs`** — la llamada a `SyncTransforms` se mueve del grafo (`TrafficNetwork`) al manager, después de ticar todos los agentes del frame.
  - **Etapa 3 — `Runtime/TrafficLaneOccupancy.cs` (nuevo)** — índice de ocupación por carril/segmento que resuelve **solo** el caso "hay un `CarAgent` inmediatamente delante en el mismo tramo": `CarAgent` lo consulta primero y, si no da resultado (carril libre, fin de tramo, cruce), recurre al `SphereCastNonAlloc` existente como hoy para peatones, cruces y obstáculos arbitrarios. El sensor de peatones (`pedestrianMask`) no se toca en esta etapa.
  - **Etapa 4 — `Runtime/PedestrianRoadProximityGrid.cs` (nuevo)** — rejilla espacial compartida (mismo patrón que la separación de `PedestrianManager`) para que `CarAgent` consulte peatones cercanos a la calzada sin `SphereCast`, usada como optimización adicional del sensor de peatones cuando el número de peatones supera `staggerMinAgentCount`.

- **Ítem 9 — Pathfinding y separación peatonal (todas las propuestas).**
  - **`Runtime/PedestrianNetwork.cs`** — cálculo de componentes conexas tras `Build()` (recalculado junto con anillos/cruces/POI); `PlanNewDestination` filtra candidatos a la componente conexa del origen antes de intentar rutas, evitando BFS contra nodos inalcanzables.
  - **`Runtime/PedestrianManager.cs`** — la planificación inicial de destino (hoy concentrada en `Start` de cada agente) se reparte en varios frames tras el spawn, escalonada por índice de agente.
  - **`Runtime/PedestrianNetwork.cs`** — caché ligera de rutas/árboles BFS reutilizable por nodo de origen dentro de la misma ventana de frames, invalidada en cada `Build()`.
  - **`Runtime/PedestrianAgent.cs`** — los buffers usados por `FindPath` dejan de reservarse por agente de forma permanente (pooling/reutilización compartida), ya que la BFS en sí ya es zero-allocation por diseño de SPEC 03; esto ataca el pico de memoria O(peatones × nodos) al entrar en Play.
  - **`Runtime/PedestrianManager.cs`** — en la pasada de separación, cada pareja de agentes cercanos se procesa una sola vez (no dos, una por cada lado); la frecuencia de recálculo de separación para agentes lejos de cámara se reduce siguiendo el mismo patrón de staggering ya usado para las decisiones de sensor.

- **Medición.** Antes de tocar el ítem 7: ejecutar la suite de Performance en 1×3/5×5/10×10 y registrar los números en el spec (sección de resultados) o en la descripción del PR. Antes de tocar los ítems 8-9: generar una ciudad con 60/150/300 vehículos y peatones y repetir la medición. Después de cada ítem: repetir la medición correspondiente y anotar el delta.

**Fuera de alcance:**

- Ítems 12, 14, 15, 17, 18 y 19 (contenido demo, LOD, chunking de marcas viales, división de `CityGeneratorWindow`, documentación desactualizada) — specs futuros aún sin numerar, sin dependencia con el trabajo de rendimiento de este spec.
- ECS/DOTS, object pooling de GameObjects, combinar toda la señalización en una malla única, activar GPU instancing de forma indiscriminada — descartados explícitamente por el informe para el estado actual del proyecto.
- Cualquier cambio al comportamiento o resultado visual de la generación: los ítems 7-9 son optimizaciones internas: mismas reglas de colocación, mismo grafo de tráfico/peatones, mismo comportamiento observable de agentes.
- Umbrales numéricos de rendimiento como criterio de aceptación bloqueante (se registran como datos informativos, no como gate).
- Integración en un pipeline de CI (el proyecto no tiene uno; los tests y benchmarks se ejecutan manualmente desde el Test Runner de Unity).
- Publicar una nueva versión del package (bump de `version`/tag) — paso posterior con `Tools > City Generator > Release`.

## Modelo de datos

Ítem 6 no introduce estructuras nuevas (son tests contra las clases ya existentes). Ítem 8, etapas 1-2, tampoco (son cambios de dónde/cuándo se llama a `Physics.SyncTransforms()`, no de datos).

```csharp
// Editor/CityGeneratorSpatialHash.cs (nuevo) — Ítem 7
// Auxiliar sobre la lista `obstacles` ya existente: no la sustituye, la indexa.
internal sealed class CityGeneratorSpatialHash
{
    private readonly float cellSize; // derivado de CityGeneratorConstants (tamaño de bloque)
    private readonly Dictionary<(int x, int z), List<Rect>> cells = new();

    public CityGeneratorSpatialHash(float cellSize) { ... }

    public void Insert(Rect bounds); // registra `bounds` en cada celda que cubre
    public bool Overlaps(Rect candidate); // consulta solo las celdas de `candidate` y sus vecinas
}
```

```csharp
// Runtime/TrafficLaneOccupancy.cs (nuevo) — Ítem 8, etapa 3
// Resuelve solo "¿hay un CarAgent inmediatamente delante en el mismo tramo?".
// Todo lo demás (peatones, cruces, obstáculos arbitrarios) sigue por SphereCastNonAlloc.
public sealed class TrafficLaneOccupancy : MonoBehaviour
{
    // Clave = arista dirigida (nodo origen, nodo destino) del grafo de TrafficNetwork.
    // Valor = agentes en ese tramo, ordenados por DistanceTravelled.
    private readonly Dictionary<(int from, int to), List<CarAgent>> segmentOccupants = new();

    public void Enter(CarAgent agent, int fromNode, int toNode); // llamado por CarAgent al entrar en un tramo
    public void Leave(CarAgent agent, int fromNode, int toNode); // llamado al salir/llegar
    public bool TryGetCarAhead(CarAgent agent, out CarAgent ahead); // false ⇒ CarAgent recurre al SphereCast como hoy
}

// Runtime/TrafficNetwork.cs
[SerializeField] private TrafficLaneOccupancy laneOccupancy; // mismo GameObject que Manager
public TrafficLaneOccupancy LaneOccupancy => laneOccupancy;
```

```csharp
// Runtime/PedestrianRoadProximityGrid.cs (nuevo) — Ítem 8, etapa 4
// Rejilla espacial de peatones cercanos a la calzada, consultada por CarAgent en vez de
// SphereCastNonAlloc cuando pedestrianCount > staggerMinAgentCount.
public sealed class PedestrianRoadProximityGrid : MonoBehaviour
{
    private readonly Dictionary<(int x, int z), List<PedestrianAgent>> cells = new();

    public void Rebuild(IReadOnlyList<PedestrianAgent> agents); // llamado una vez por frame por PedestrianManager
    public void QueryNear(Vector3 position, float radius, List<PedestrianAgent> results);
}

// Runtime/PedestrianNetwork.cs
[SerializeField] private PedestrianRoadProximityGrid roadProximity; // mismo GameObject que Manager
public PedestrianRoadProximityGrid RoadProximity => roadProximity;
```

```csharp
// Runtime/PedestrianNetwork.cs — Ítem 9
// Componentes conexas: recalculadas en Build() junto a anillos/cruces/POI (flood fill sobre
// las aristas ya construidas). Evita que PlanNewDestination pruebe candidatos inalcanzables.
private int[] nodeComponent; // paralelo a `nodes`, tamaño = nodes.Count
public int ComponentOf(int nodeIndex) => nodeComponent[nodeIndex];

// Caché de árboles BFS por nodo origen, invalidada en cada Build(). Vida corta: pensada para
// que varios peatones que planifican en la misma ventana de frames desde nodos cercanos no
// repitan el mismo BFS, no como caché persistente entre sesiones.
private readonly Dictionary<int, int[]> cameFromCache = new(); // origen -> árbol BFS (cameFrom)
```

```csharp
// Runtime/PedestrianPathBufferPool.cs (nuevo) — Ítem 9
// Hoy cada PedestrianAgent reserva su propio array del tamaño del grafo de por vida
// (memoria O(peatones × nodos)). Como la planificación inicial pasa a repartirse entre
// frames (esta misma sección), en un instante dado solo un subconjunto pequeño de agentes
// está realmente planificando: un pool compartido, dimensionado al grafo, basta.
internal sealed class PedestrianPathBufferPool
{
    private readonly int nodeCount;
    private readonly Stack<int[]> visited = new();
    private readonly Stack<int[]> cameFrom = new();

    public PedestrianPathBufferPool(int nodeCount) { ... }
    public (int[] visited, int[] cameFrom) Rent();
    public void Return(int[] visited, int[] cameFrom);
}

// Runtime/PedestrianManager.cs
[SerializeField] private PedestrianPathBufferPool pathBufferPool; // construido en OnEnable, sized a network.NodeCount
```

`PedestrianManager` también gana un campo de estado ligero (no serializado, no requiere struct propio) para escalonar la planificación inicial: un contador de frame por agente que difiere su primer `PlanNewDestination` en lugar de dispararlo todo en `Start`.

## Plan de implementación

1. **Añadir el paquete de Performance Testing y montar los proyectos de test.** Añadir `com.unity.test-framework.performance` al `manifest.json` del proyecto. Crear `Assets/Tests/EditMode/`, `Assets/Tests/PlayMode/` y `Assets/Tests/Performance/`, cada uno con su `.asmdef` referenciando los ensamblados `CityGenerator.Runtime`/`CityGenerator.Editor` y `UnityEngine.TestRunner`/`UnityEditor.TestRunner`. Verificación: el Test Runner de Unity (`Window > General > Test Runner`) detecta las tres carpetas vacías sin errores de compilación.

2. **Tests EditMode (ítem 6, primera mitad).** Escribir los tests de `CityGeneratorGrid`, `CityGeneratorDistributionUtility`, `CityGeneratorValidator.ValidateDetailed` (incluidos los 7 casos de SPEC 04), pesos de ruta, `PedestrianNetwork.FindPath` (BFS) y persistencia de POI. Verificación: todos pasan en verde contra el código actual (son tests de regresión sobre comportamiento ya existente, no deben requerir cambios de producción).

3. **Tests PlayMode (ítem 6, segunda mitad).** Escribir los tests de ciclo de semáforo, `CanCross`, registro/desregistro de agentes con reactivación, dos ciudades en la misma escena, y collider solo en un hijo. Verificación: todos pasan en verde contra el código actual.

4. **Tests de generación con semilla fija (ítem 6, tercera parte).** Añadir los tests 1×3, 5×5 y 10×10 con `useCustomSeed` activo, comprobando ausencia de excepciones y los invariantes acordados (número de bloques, edificios colocados, nodos de red). Verificación: los tres pasan en verde.

5. **Suite de Performance y medición baseline (ítem 6, cuarta parte).** Escribir los tests de `Assets/Tests/Performance/` (tiempo de generación por tamaño de rejilla, `GC Alloc`, coste de `SyncTransforms`, coste de los `Update` de los managers, memoria aproximada). Ejecutarlos contra 1×3/5×5/10×10 y registrar los números en la sección de resultados de este spec antes de tocar el ítem 7. Verificación: los tests corren y producen números reproducibles (mismo orden de magnitud en dos ejecuciones seguidas).

6. **Índice espacial de colocación (ítem 7).** Implementar `CityGeneratorSpatialHash` e integrarlo en `CityGeneratorPlacementEngine` como índice auxiliar sobre `obstacles`. Verificación: los tests de generación con semilla fija (paso 4) siguen dando el mismo resultado exacto que antes del cambio (misma disposición de objetos, la única diferencia es el coste de la consulta); repetir la medición de tiempo de generación de 10×10 del paso 5 y anotar el delta.

7. **Medición con tráfico/peatones cargados.** Generar una ciudad con 60/150/300 vehículos y peatones (tres corridas) y ejecutar la suite de Performance en runtime, registrando los números como baseline para los ítems 8-9. Verificación: los números quedan anotados en el spec/PR antes de continuar.

8. **`SyncTransforms` condicional y movido al manager (ítem 8, etapas 1-2).** Modificar `TrafficNetwork`/`TrafficManager` según el modelo de datos. Verificación: los tests PlayMode de tráfico (paso 3) siguen en verde; una escena sin ningún `CarAgent` registrado deja de invocar `Physics.SyncTransforms()` (comprobable con un `Profiler.BeginSample` temporal o un contador de llamadas en el test).

9. **Índice de ocupación por carril (ítem 8, etapa 3).** Implementar `TrafficLaneOccupancy`, conectarlo a `CarAgent.Enter`/`Leave` por tramo, y hacer que la comprobación de "coche delante" lo consulte antes de recurrir al `SphereCastNonAlloc`. Verificación: los tests de tráfico siguen en verde (comportamiento de frenado idéntico); repetir la medición de 150/300 vehículos del paso 7 y anotar el delta en coste de sensor.

10. **Rejilla de proximidad peatonal para vehículos (ítem 8, etapa 4).** Implementar `PedestrianRoadProximityGrid`, poblarla desde `PedestrianManager` una vez por frame, y hacer que `CarAgent` la consulte para el sensor de peatones cuando `pedestrianCount > staggerMinAgentCount`. Verificación: los tests de frenado ante peatón siguen en verde; repetir la medición con 300 peatones y anotar el delta.

11. **Componentes conexas y filtrado de destinos (ítem 9, primera parte).** Añadir `nodeComponent` a `PedestrianNetwork`, recalculado en `Build()`, y filtrar `PlanNewDestination` por componente conexa antes de intentar rutas. Verificación: el test EditMode de BFS (paso 2) sigue en verde; en una rejilla con `gridWidth == 1` (ring aislado por bloque, ver CLAUDE.md) los peatones dejan de intentar candidatos inalcanzables — comprobable contando llamadas a `FindPath` que fallan por "sin ruta" antes/después.

12. **Reparto de planificación inicial entre frames (ítem 9, segunda parte).** `PedestrianManager` escalona el primer `PlanNewDestination` de cada agente en vez de dispararlos todos en `Start`. Verificación: repetir la medición de "pico al entrar en Play" con 300 peatones (`GC Alloc`/tiempo del primer frame tras `Awake`) y anotar el delta frente al paso 7.

13. **Caché de rutas BFS y pool de buffers (ítem 9, tercera y cuarta parte).** Añadir `cameFromCache` a `PedestrianNetwork` y `PedestrianPathBufferPool` a `PedestrianManager`; `PedestrianAgent.PlanNewDestination` pasa a pedir sus buffers al pool en vez de mantener uno propio permanente. Verificación: los tests EditMode/PlayMode de pathfinding siguen en verde; medir memoria total con 300 peatones y compararla contra el baseline O(peatones × nodos) del paso 7.

14. **Deduplicar pares y escalonar separación (ítem 9, quinta parte).** `PedestrianManager` procesa cada pareja de agentes cercanos una sola vez y reduce la frecuencia de recálculo para agentes lejos de cámara, siguiendo el mismo patrón de staggering ya usado para sensores. Verificación: comportamiento de separación visualmente idéntico (los peatones no se atraviesan); medir coste de `PedestrianManager.Update` con 300 peatones y anotar el delta.

15. **Medición final y cierre.** Repetir la suite de Performance completa (generación 1×3/5×5/10×10, runtime con 60/150/300 agentes) y volcar la comparación completa antes/después en el spec o en la descripción del PR.

## Criterios de aceptación

- [x] Las carpetas `Assets/Tests/EditMode/`, `Assets/Tests/PlayMode/` y `Assets/Tests/Performance/` existen, compilan y aparecen en el Test Runner de Unity.
- [x] Todos los tests EditMode del paso 2 (grid, distribución de porcentajes, validación completa, pesos de ruta, BFS, persistencia de POI) pasan en verde.
- [x] Todos los tests PlayMode del paso 3 (ciclo de semáforo, `CanCross`, registro/desregistro con reactivación, dos ciudades en la misma escena, collider solo en un hijo) pasan en verde.
- [x] Los tests de generación con semilla fija (1×3, 5×5, 10×10) pasan en verde tanto antes como después de introducir el índice espacial (ítem 7), produciendo exactamente la misma disposición de objetos en ambos casos.
- [x] La suite de Performance se ejecuta sin errores en los tres tamaños de rejilla y en las tres cargas de agentes (60/150/300), y sus números quedan registrados (baseline y post-cambio) en el spec o en la descripción del PR — sin exigir un umbral numérico fijo como condición de aceptación.
- [x] `CityGeneratorPlacementEngine` usa `CityGeneratorSpatialHash` para las comprobaciones de solapamiento; el comportamiento observable de colocación (qué queda colocado y dónde, dada una semilla) no cambia.
- [x] Una escena sin ningún `CarAgent` registrado no invoca `Physics.SyncTransforms()`.
- [x] `TrafficLaneOccupancy` resuelve correctamente el caso "coche delante en el mismo tramo" sin `SphereCast`; el frenado ante peatones y en cruces sigue funcionando exactamente igual que antes (verificado por los tests PlayMode de tráfico).
- [x] `PedestrianRoadProximityGrid` permite que los vehículos sigan frenando ante peatones cercanos a la calzada cuando `pedestrianCount > staggerMinAgentCount`, sin usar `SphereCast` para ese caso.
- [x] En una rejilla con `gridWidth == 1` o `gridHeight == 1` (rings aislados por bloque), los peatones ya no intentan planificar rutas hacia nodos de otra componente conexa inalcanzable.
- [x] La planificación inicial de destino de los peatones se reparte en varios frames tras el spawn, en vez de concentrarse toda en el primer frame de `Start`.
- [x] `PedestrianAgent` deja de reservar un array del tamaño del grafo por agente de forma permanente; los buffers de BFS se obtienen de `PedestrianPathBufferPool`.
- [x] La pasada de separación de `PedestrianManager` procesa cada pareja de agentes cercanos una sola vez, y el comportamiento visual de separación (los peatones no se atraviesan entre sí) no cambia.
- [x] El proyecto compila sin warnings nuevos y una generación completa (rejilla 5×5 con tráfico y peatones activados) sigue completándose sin excepciones tras aplicar todos los cambios de este spec.
- [x] La comparación final de mediciones (antes/después, paso 15) queda documentada.

## Decisiones tomadas y descartadas

- **Tests fuera del package, en `Assets/Tests/`.** Se descarta meterlos dentro de `Packages/com.santiandrade.citygenerator/Tests/` (la convención estándar de Unity) porque, igual que `CityGeneratorReleaseWindow.cs`/`CityGeneratorSetDefaultsWindow.cs`, son herramientas de desarrollo de este repo, no parte del package distribuible; un usuario que instala el package por git URL no necesita ni espera esta suite.
- **`com.unity.test-framework.performance` sí como dependencia, pero del proyecto, no del package.** Se añade al `manifest.json` de este proyecto porque los tests viven fuera del package; `Packages/com.santiandrade.citygenerator/package.json` no gana ninguna dependencia nueva por este spec.
- **Umbrales de rendimiento informativos, no bloqueantes.** Se descarta fijar ahora números concretos (p. ej. "10×10 en <X ms") porque el propio informe técnico advierte que sus estimaciones de ganancia son arquitectónicas, sin profiling real previo — fijar un umbral sin datos de referencia sería inventar un número. Los benchmarks se ejecutan y se registran; la aceptación depende de que las mediciones existan y de que el comportamiento funcional no haya cambiado, no de superar una cifra.
- **Ítem 8 completo (las 4 etapas), no solo la etapa barata.** Se descarta parar en el `SyncTransforms` condicional (etapa 1, la única de coste S) porque el propio informe encuadra las 4 etapas como un único ítem progresivo, y este spec ya sigue el orden "medir primero" con una medición intermedia entre la etapa 1-2 y la 3-4, lo que permite decidir con datos si merece la pena seguir sin necesitar un spec adicional solo para eso.
- **Índice de ocupación por carril (ítem 8, etapa 3): sustituye solo el sensor de "coche delante", no el de peatones.** Se descarta que la misma estructura resuelva también la detección de peatones porque son problemas de forma distinta (ocupación discreta por tramo vs. proximidad continua en el plano); mezclarlos habría acoplado dos optimizaciones independientes y aumentado el riesgo de romper el frenado ante peatones, que es una de las cuatro reglas "load-bearing" de `CarAgent` documentadas en CLAUDE.md.
- **Ítem 9 completo (todas las propuestas), no solo el subconjunto barato.** Se descarta dejar fuera la reutilización de árboles BFS y el pooling de buffers porque comparten el mismo mecanismo que las otras dos mejoras (siempre tocando `PlanNewDestination`/`PedestrianNetwork.Build()`) y dividirlas habría sido trabajo redundante entre dos specs, igual que se decidió para el ítem 10 en SPEC 04.
- **Pool de buffers compartido en vez de un array por agente.** Se descarta que cada `PedestrianAgent` siga reservando su propio array de tamaño del grafo (aunque ya sea zero-allocation por llamada) porque el coste real señalado por el informe es de memoria total O(peatones × nodos), no de GC por frame; un pool dimensionado al grafo y prestado durante la ventana de planificación (ahora repartida en frames, paso 12) ataca directamente ese número sin cambiar la naturaleza zero-allocation del BFS.
- **Caché de rutas BFS con vida corta (invalidada en cada `Build()`), no persistente entre sesiones.** Se descarta serializarla o mantenerla más allá de un `Build()` porque el grafo puede cambiar (rebuild manual, reinserción de POI); una caché que sobreviviera a un `Build()` correría el riesgo de devolver rutas obsoletas.
- **Índice espacial de colocación (ítem 7) como estructura auxiliar sobre `obstacles`, no como reemplazo de la lista.** Seguridad ante desincronización: si `CityGeneratorSpatialHash` y `obstacles` divergieran, sería un bug sutil de generación. Mantener `obstacles` como fuente de verdad y el hash como índice deriva de ella minimiza superficie de error, siguiendo el mismo principio ya aplicado en `CityGeneratorGridPreview` (SPEC 04: "la imagen y la escena generada nunca pueden discrepar").
- **Sin umbral de tamaño de rejilla nuevo.** Se descarta subir o quitar el límite de 10×10 de la UI como parte de este spec: el índice espacial mejora el coste asintótico, pero decidir si el límite debe cambiar es una decisión de producto que depende de las mediciones del paso 15, no algo a fijar de antemano.
- **Sin cambios en `CarAgent`/`PedestrianAgent` fuera de lo descrito.** Las cuatro reglas "load-bearing" de `CarAgent` documentadas en CLAUDE.md (identidad por referencia en el sensor, reserva de prioridad en cruces sin semáforo, etc.) y el comportamiento de `PedestrianAgent` (sin mecánica de atasco propia) se mantienen intactos; este spec optimiza cómo se obtienen los datos que alimentan esas reglas, no las reglas mismas.

No se identifican riesgos adicionales que requieran una sección propia más allá de lo ya cubierto en Scope/Decisiones.

## Resultados de medición

Máquina de referencia: Intel Core Ultra 9 285H, RTX 5060 Laptop GPU, 32 GB RAM, Windows 11, Unity 6000.5.8f1, Editor en modo Development, `com.unity.test-framework.performance@3.5.0`.

### Baseline previo al ítem 7 (paso 5) — generación, semilla fija, `Measure.Method` (5 muestras, 1 warmup) + `GC()`

| Rejilla | Tiempo medio (ms) | Min / Max (ms) | GC Alloc por ejecución (bytes) |
|---|---|---|---|
| 1×3 | 62.13 | 57.40 / 67.99 | 6 057 |
| 5×5 | 258.84 | 248.36 / 272.70 | 18 723 |
| 10×10 | 1 458.39 | 1 400.19 / 1 493.08 | 68 008 |

Memoria managed aproximada de una ciudad 5×5 generada (`GC.GetTotalMemory` antes/después, con colección forzada antes de medir): **≈0.223 MB** de delta neto — nota: esto mide solo el crecimiento del heap managed inmediatamente after `Assemble`; no incluye memoria nativa (mallas, texturas, materiales) de los GameObjects instanciados, que es donde vive la mayor parte del coste real de una ciudad generada.

### Baseline previo a los ítems 8-9 (paso 7) — runtime con tráfico y peatones activos, rejilla 10×10, `Measure.Frames` (60 muestras, 10 warmup)

| Agentes (vehículos + peatones) | Frame time medio (ms) | Min / Max (ms) |
|---|---|---|
| 60 + 60 | 4.94 | 4.21 / 7.73 |
| 150 + 150 | 7.30 | 6.62 / 8.50 |
| 300 + 300 | 11.61 | 10.64 / 12.91 |

Esta medición es el coste de frame combinado (`TrafficManager.Update` + `PedestrianManager.Update` + `Physics.SyncTransforms` + el resto del frame de Test Runner en Play Mode), no aislado por subsistema: la base de código no tiene `ProfilerMarker`s propios en estos métodos, y añadir instrumentación solo para medir queda fuera del alcance de este spec (ver "Sin cambios en `CarAgent`/`PedestrianAgent` fuera de lo descrito"). Sirve como número de referencia informativo para comparar el antes/después de los ítems 8-9, no como medición aislada de cada optimización.

### Después del ítem 7 — índice espacial de solapamiento (paso 6)

| Rejilla | Tiempo medio antes (ms) | Tiempo medio después (ms) | Delta |
|---|---|---|---|
| 10×10 | 1 458.39 | 1 189.37 | **-18.4%** |

GC Alloc por ejecución (10×10): 68 008 → 68 660 bytes (+652 B, despreciable — las listas del hash espacial). Los tests de generación con semilla fija (`SeededGenerationTests`) siguen en verde después del cambio, incluyendo `Assemble_SameSeed_ProducesIdenticalBuildingLayout`: la disposición generada es exactamente la misma, solo cambia el coste de la comprobación de solapamiento.

### Después del ítem 8 — coste físico del tráfico, las 4 etapas (pasos 8-10)

Cambios: `Physics.SyncTransforms()` movido de `TrafficNetwork.LateUpdate` a `TrafficManager.Update` (solo si `agents.Count > 0`); `TrafficLaneOccupancy` resuelve el caso "coche delante en el mismo carril" sin `SphereCast`; `PedestrianRoadProximityGrid` permite a `CarAgent` consultar peatones cercanos sin `SphereCast` una vez hay más peatones que `staggerMinAgentCount`.

| Agentes (vehículos + peatones) | Frame time medio antes (ms) | Frame time medio después (ms) | Delta |
|---|---|---|---|
| 60 + 60 | 4.94 | 5.20 | +5% (ruido; staggering aún no activo con 60 = staggerMinAgentCount) |
| 150 + 150 | 7.30 | 7.48 | +2% (dentro del ruido de medición del Editor) |
| 300 + 300 | 11.61 | 11.23 | **-3.3%** |

La ganancia medida es modesta a esta escala: el coste dominante en el Editor a estos tamaños de agentes es el propio overhead del Test Runner/Editor por frame, no el sensor de `CarAgent` en sí — la ganancia real de evitar `SphereCastNonAlloc` debería notarse más en un build standalone o con recuentos de agentes bastante mayores. Todos los tests PlayMode de tráfico/peatones (ciclo de semáforo, `CanCross`, registro/desregistro) siguen en verde; los tests de generación con semilla fija siguen dando exactamente el mismo resultado (los cambios del ítem 8 no tocan la colocación, solo el runtime).

### Después del ítem 9 — pathfinding y separación peatonal, todas las propuestas (pasos 11-14)

Cambios: componentes conexas (`PedestrianNetwork.ComponentOf`) filtran `PlanNewDestination` antes de intentar una ruta; la planificación inicial de cada peatón se reparte en varios segundos según su orden de spawn en vez de dispararse toda en el primer frame; `FindPath` cachea el árbol BFS completo por nodo origen (`cameFromCache`, invalidado en cada `Build()`/registro de POI) en vez de recalcularlo por cada uno de los hasta 8 candidatos que prueba `PlanNewDestination`; `PedestrianAgent` ya no reserva un array del tamaño del grafo de por vida, sino que pide un buffer a `PedestrianManager.PathBufferPool` solo durante la planificación; `PedestrianManager.ApplyLocalSeparation` procesa cada pareja de agentes cercanos una sola vez (antes se procesaba dos veces, una por cada lado) y omite una pareja por completo cuando ninguno de los dos agentes está "activo" ese frame según el mismo staggering ya usado para el sensor.

| Agentes (vehículos + peatones) | Frame time medio después del ítem 8 (ms) | Frame time medio después del ítem 9 (ms) | Delta |
|---|---|---|---|
| 60 + 60 | 5.20 | 5.31 | +2% (ruido) |
| 150 + 150 | 7.48 | 7.02 | **-6.1%** |
| 300 + 300 | 11.23 | 10.80 | **-3.8%** |

Un bug real se detectó y corrigió gracias a esta propia suite de Performance: al añadir componentes conexas, cada punto de interés de plaza registrado tras `Build()` (`RegisterPointOfInterest`, llamado por `CityGeneratorPedestrianBuilder.RegisterPointsOfInterest`) hacía crecer la lista de nodos sin mantener sincronizado el array de componentes, lanzando `IndexOutOfRangeException` la primera vez que un peatón intentaba planificar destino — `PerFrameCost_150Agents`/`_300Agents`/`_60Agents` fallaron con esa excepción en la primera ejecución tras implementar el ítem 9, y siguen en la suite como regresión (`RegisterPointOfInterest_RepeatedlyAfterBuild_DoesNotThrow`, `PedestrianNetworkTests`).

El pico de memoria O(peatones × nodos) al entrar en Play y el efecto exacto del reparto de la planificación inicial entre frames no se miden directamente en esta suite (requerirían capturar el primer frame tras `Awake` en aislamiento, fuera del alcance práctico de `Measure.Frames`); su corrección se verifica arquitectónicamente (el campo `path` de `PedestrianAgent` ya no se dimensiona a `network.NodeCount` en `Start()`, ver el código) en vez de con un número.

### Comparación final (paso 15)

| Medición | Baseline (paso 5/7) | Final (tras ítems 7-9) | Delta total |
|---|---|---|---|
| Generación 1×3 (tiempo medio) | 62.13 ms | 67.17 ms | +8% (ruido del Editor a esta escala; ítem 7 no aporta en rejillas casi sin obstáculos) |
| Generación 5×5 (tiempo medio) | 258.84 ms | 250.74 ms | -3.1% |
| Generación 10×10 (tiempo medio) | 1 458.39 ms | 1 206.82 ms | **-17.2%** |
| Runtime 60+60 agentes (frame medio) | 4.94 ms | 5.31 ms | +7.5% (ruido; por debajo de `staggerMinAgentCount`, el staggering nunca se activa a este tamaño) |
| Runtime 150+150 agentes (frame medio) | 7.30 ms | 7.02 ms | -3.8% |
| Runtime 300+300 agentes (frame medio) | 11.61 ms | 10.80 ms | **-7.0%** |

Todos los tests EditMode/PlayMode/Performance del spec están en verde tras aplicar los ítems 7-9; los tests de generación con semilla fija confirman que la disposición generada no cambió. La ganancia es clara y creciente con el tamaño de la rejilla/número de agentes (que es exactamente donde el informe técnico predijo el problema O(n²)); a los tamaños pequeños (1×3, 60 agentes) el ruido del Editor domina sobre la ganancia real, como cabía esperar de optimizaciones cuyo coste evitado crece con n.
