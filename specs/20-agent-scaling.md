# SPEC 20 — Escalado de agentes: medición, LOD de física, reubicación y filtro coche↔peatón delegable

> **Estado:** Aprobado
> **Fecha:** 2026-11-14
> **Proyecto:** Unity-CityGenerator (`santiandrade/Unity-CityGenerator`)
> **Depende de:** SPEC 05 (tick centralizado, `TrafficLaneOccupancy`, `PedestrianRoadProximityGrid`, tests de rendimiento), SPEC 14/15 (API runtime por ciudad), SPEC 16 (varias ciudades), SPEC 17 (`Enable Physics`, `VehiclePedestrianCollisionFilter`), SPEC 19 (parachoques y `TrafficObstacle`)
> **Objetivo:** Que un proyecto pueda mover del orden de 1000 coches y 1000 peatones por una ciudad generada (o gestionarlos en una burbuja alrededor del jugador) sin que la física, el suelo de los peatones ni los pares de colisión dominen el frame. Todo es opcional: con las opciones por defecto, nada cambia para quien ya usa el paquete.

## Contexto — de dónde sale

Viene de un proyecto consumidor (*City Races*, SPEC 53 de ese repo, `specs/53-populated-city.md`):

- Una ciudad de 15 × 15 manzanas, con `Enable Physics` ON, necesitaría del orden de 1000 coches y 1000 peatones para verse poblada.
- Hoy tiene 400 + 400 y ya no sube más por rendimiento.
- La solución de ese proyecto es una **burbuja de población**: un pool de agentes horneados que aparecen y desaparecen alrededor del jugador, fuera de su vista.
- Para hacerla necesita piezas que solo el paquete puede dar.

Lo que cuesta cada agente hoy (lectura del código; el paso 1 lo mide):

- **Coche en modo físico:**
  - `Rigidbody` dinámico con la velocidad impuesta en cada `FixedUpdate`, esté donde esté;
  - en las colas, contactos continuos parachoques con parachoques, con `OnCollisionStay` → `CheckBumperContact` en C#.
- **Peatón:**
  - un `Physics.Raycast` al suelo por frame en `MoveTowards`, que no se escalona;
  - un collider sin `Rigidbody` movido por `transform`: un collider estático teletransportado, el caso caro de PhysX.
- **`VehiclePedestrianCollisionFilter`:** un par de `IgnoreCollision` por cada coche físico × peatón, es decir, 1 millón con 1000 × 1000. La tabla global de pares se recorre entera al destruir cualquier collider de la escena. `ClearAllPairs` existe precisamente por eso.
- **Escalonado:** el de `TrafficManager`/`PedestrianManager` siempre se centra en `Camera.main`. Un consumidor que tiene su propio foco (el coche del jugador) o varias cámaras no puede decir dónde importa la simulación.
- **Re-encendido:** un `CarAgent` que se reactiva no vuelve a entrar en `TrafficLaneOccupancy` hasta su siguiente tramo, porque `Enter` solo se llama desde `Start`. City Races lo anotó como limitación conocida en su SPEC 45.
- **Reubicación:** no hay forma soportada de mover un agente a otro punto de la red. Moverlo por `transform` deja mal su nodo objetivo, la ocupación de carril, la reserva de cruce y la ruta.

La revisión técnica (`docs/technical-review.md`, F.4) ya concluyó que el camino es este y no ECS: índices propios (hechos en SPEC 05) y, solo si el perfilado lo pide, Jobs + Burst sin Entities. Esta spec se queda en lo primero más el LOD de física. No añade ninguna dependencia de paquete.

## Scope

**Dentro:**

1. **Instrumentación:**
   - `ProfilerMarker`s en `TrafficManager` (`CityGenerator.Traffic.Tick`, `.Sensors`) y `PedestrianManager` (`CityGenerator.Pedestrians.Tick`, `.Separation`, `.ProximityGrid`, `.Grounding`), y el marcador de la transición de LOD de física;
   - `RuntimePerformanceTests` gana un caso de 15 × 15 con 1000 + 1000 agentes en modo físico, con todas las opciones nuevas apagadas y encendidas;
   - el resultado va a las notas de implementación, como en SPEC 05.
2. **Filtro coche↔peatón delegable:**
   - `CityGeneratorAPI.VehiclePedestrianPairFiltering` (`bool`, por defecto `true`, estático y global, como el propio filtro).
   - Con `false`: `RegisterVehicle`/`RegisterPedestrian` no emparejan nada y los pares ya existentes se borran (`ClearAllPairs`).
   - Pensado para ponerlo antes de que cargue la escena (`RuntimeInitializeLoadType.BeforeSceneLoad`); cambiarlo con agentes ya activos también funciona.
   - **El paquete sigue sin tocar la matriz de colisiones:** quien lo desactiva asume impedir el contacto por su cuenta, normalmente con la matriz de su proyecto.
3. **Foco de simulación:**
   - `TrafficManager.SimulationFocus` y `PedestrianManager.SimulationFocus` (`Transform`; `null` = `Camera.main`, como hoy);
   - todas las distancias de escalonado y de LOD se miden desde ahí;
   - API: `city.Traffic.SetSimulationFocus(Transform)` y `city.Pedestrians.SetSimulationFocus(Transform)`.
4. **LOD de física de los coches** (solo modo físico):
   - `TrafficManager.physicsRadius` (`0` = apagado, por defecto) y `physicsRadiusHysteresis` (10 m).
   - Fuera del radio + histéresis, un coche en `Driving` pasa a `rb.isKinematic = true` y su `Tick` lo mueve con `rb.MovePosition`/`MoveRotation` en el mismo `FixedUpdate`. Los cuerpos cinemáticos no generan contactos entre sí ni con estáticos, así que las colas lejanas dejan de costar.
   - Al volver dentro del radio pasa a dinámico con `linearVelocity = forward · speed`, sin saltos de velocidad.
   - Un coche en `Recovering` no cambia de modo hasta reengancharse.
   - API: `city.Traffic.SetPhysicsRadius(float)`.
5. **LOD de colliders de peatones:**
   - `PedestrianManager.colliderRadius` (`0` = apagado, por defecto): fuera del radio + histéresis se apaga el collider raíz del peatón (`collider.enabled = false`).
   - **Solo actúa mientras `PedestrianRoadProximityGrid.HasEnoughAgents`:** por debajo del umbral, el sensor de peatones de `CarAgent` vuelve al `SphereCast` y necesita los colliders.
   - Al volver a encenderlo se registra otra vez en el filtro, porque Unity borra los pares ignorados de un collider que se apaga y se enciende.
   - API: `city.Pedestrians.SetColliderRadius(float)`.
6. **Suelo de los peatones por lotes:**
   - por encima de `staggerMinAgentCount`, `PedestrianManager` calcula primero la siguiente posición XZ de todos los peatones que caminan;
   - después lanza un único lote de `RaycastCommand` (`QueryParameters` con `groundMask` y triggers ignorados), lo completa en el mismo frame y aplica las alturas;
   - por debajo del umbral se queda el `Physics.Raycast` actual, idéntico bit a bit;
   - `RaycastCommand` y `NativeArray` son del módulo base de Unity, no del paquete Collections.
7. **Escalonado configurable y por visibilidad:**
   - `staggerDistance` y `staggerFrames` accesibles por API (`SetStagger(float distance, int frames)`) además del Inspector;
   - un agente que no se ve en ninguna cámara (`Renderer.isVisible` de un renderer principal cacheado) se escalona también aunque esté cerca;
   - se escalonan los sensores y la lógica, nunca el movimiento, para que no se vean saltos;
   - `staggerMinAgentCount` no cambia (invariante).
8. **Reubicación:**
   - **`CarAgent.Relocate(int fromNode, int toNode, float t)`:**
     - coloca el coche en ese tramo de carril (`t` de 0 a 1), con su rumbo;
     - deja la ocupación del tramo anterior y entra en el nuevo;
     - libera su reserva de cruce;
     - reinicia `speed` (a `maxSpeed · cornerSpeedFactor`), `stoppedTime`, el deadlock y el parachoques, y deja el estado físico en `Driving`;
     - en modo físico, también reinicia la velocidad del `Rigidbody`.
   - **`PedestrianAgent.Relocate(int node)`:** coloca al peatón en ese nodo (suelo resuelto con el raycast) y entra en `Idling` con una espera corta, que replanifica sola.
   - **Consultas nuevas de red y ocupación para elegir dónde aparecer:**
     - `TrafficNetwork.LaneSegmentCount` / `GetLaneSegment(int index, out int from, out int to)`: los tramos rectos entre la salida de un cruce y la entrada del siguiente, que son los únicos válidos para aparecer;
     - `TrafficLaneOccupancy.GetGap(int from, int to, Vector3 position, out float ahead, out float behind)`.
   - **Arreglo:** `CarAgent.OnEnable` vuelve a entrar en `TrafficLaneOccupancy` si ya tenía tramo (`targetNode >= 0`).
   - **Visibilidad:** `CarAgent.MaxSpeed`, `CarAgent.TargetNode`/`FromNode` y `PedestrianAgent.CurrentNode` públicos de solo lectura (`TargetNode` y `CurrentNode` ya lo son).
9. **Gancho de animación para un render externo** (lo usa la fase 3 de City Races; no tiene coste si no se usa):
   - `PedestrianAgent.AnimationSpeed`: el `normalizedSpeed` del blend tree, o 0 si está parado;
   - `PedestrianAgent.SetAnimatorDriven(bool)`: con `false`, el agente deja de escribir parámetros en el `Animator` y lo desactiva; con `true`, lo vuelve a activar con los parámetros de su estado.
10. **Experimento medido: *Optimize Game Objects* en los FBX de personajes de `DefaultAssets`.** Se adopta solo si el paso 1 muestra una mejora clara del coste de animación (≥ 15 % de `Animation` en la medición de 1000 peatones). Se hace sin exponer huesos, porque ningún script del paquete los usa. Si se adopta, va en el CHANGELOG como cambio que puede afectar a quien cuelgue cosas de los huesos de los personajes de demo.
11. **Documentación:**
    - `docs/architecture/runtime-and-traffic.md` y `docs/architecture/pedestrians.md`;
    - `docs/api-reference.md`/`.es.md`;
    - `CLAUDE.md` (invariante del filtro matizado, fila de la tabla de specs);
    - `CHANGELOG.md` (`## [Unreleased]` → **3.7.0**).

**Fuera de alcance:**

- **La burbuja de población en sí** (cuándo y dónde aparecen y desaparecen los agentes, qué cámaras cuentan, densidades). Es política del juego y depende de sus cámaras, cinemáticas y teletransportes; vive en City Races. El paquete solo da las piezas.
- **Simulación de agentes con Jobs + Burst** (estado en `NativeArray`, seguimiento de coches por índice de carril). Es el paso F.4.4 de la revisión técnica: solo si el perfilado lo pide tras esta spec, y en su propia spec, porque añadiría dependencias de paquete.
- **Planificación de rutas en `CarAgent`** (F.4.1) y cambios en la resolución de atascos.
- **Opciones nuevas en la ventana del generador.** Todo se configura en runtime por API (o en el Inspector de los managers). Una opción en `CityGeneratorSettings` obligaría a regenerar para cambiarla; se añadirá si alguien la pide.
- **Instanciar agentes en runtime.** Reubicar agentes ya generados cubre el caso del pool.
- **Animación horneada en textura o render de multitudes en GPU.** Es del consumidor; el paquete solo da el gancho del punto 9.

## Modelo de datos

### `CityGeneratorAPI` (estático)

```csharp
// Default true: every physics-mode vehicle is paired with every pedestrian (SPEC 17).
// false: no pairs are registered and existing ones are cleared. The caller becomes
// responsible for keeping vehicles and pedestrians from colliding (e.g. its layer matrix).
public static bool VehiclePedestrianPairFiltering { get; set; }
```

### `TrafficModule` / `PedestriansModule` (API por ciudad)

```csharp
public void SetSimulationFocus(Transform focus);        // both
public void SetStagger(float distance, int frames);     // both
public void SetPhysicsRadius(float meters);             // Traffic; 0 = off
public void SetColliderRadius(float meters);            // Pedestrians; 0 = off
```

### `TrafficManager`

```csharp
[SerializeField] private Transform simulationFocus;          // null = Camera.main
[SerializeField] private float physicsRadius;                // 0 = off (default)
[SerializeField] private float physicsRadiusHysteresis = 10f;
```

### `PedestrianManager`

```csharp
[SerializeField] private Transform simulationFocus;          // null = Camera.main
[SerializeField] private float colliderRadius;               // 0 = off (default)
[SerializeField] private float colliderRadiusHysteresis = 5f;
```

### `CarAgent` — estado y API nuevos

```csharp
public int FromNode => fromNode;
public float MaxSpeed => maxSpeed;
public bool IsPhysicsSimulated { get; }                     // dynamic Rigidbody right now
internal void SetPhysicsSimulated(bool simulated);           // called by TrafficManager
public bool Relocate(int fromNode, int toNode, float t);     // false if the segment is invalid
```

### `PedestrianAgent` — API nueva

```csharp
public float AnimationSpeed { get; }
public void SetAnimatorDriven(bool driven);
public bool Relocate(int node);                              // false if the node is blocked or invalid
```

## Plan de implementación

1. **Instrumentación y medición de partida:**
   - marcadores;
   - caso 1000 + 1000 (15 × 15, `Enable Physics` ON) en `RuntimePerformanceTests`;
   - tabla de partida en las notas.
2. **Filtro delegable:**
   - `VehiclePedestrianPairFiltering`;
   - tests PlayMode: con `false` no hay pares y un coche físico sigue sin poder ser empujado por un peatón si la matriz lo impide; con `true`, todo idéntico a SPEC 17.
3. **Foco y escalonado:**
   - `SimulationFocus` en los dos managers, API de foco y de escalonado;
   - escalonado de los agentes no visibles;
   - test: el escalonado se centra en el foco dado.
4. **LOD de física de coches y de colliders de peatones:**
   - cambio dinámico ↔ cinemático con histéresis y continuidad de velocidad;
   - LOD de colliders con re-registro en el filtro;
   - tests PlayMode (`Traffic/PhysicsLodTests`):
     - un coche cruza el radio en los dos sentidos sin cambio de velocidad mayor de 0,5 m/s;
     - un coche en `Recovering` no cambia de modo;
     - un peatón al que se le vuelve a encender el collider queda emparejado de nuevo.
5. **Suelo por lotes:** `RaycastCommand` por encima del umbral. Test: la altura resultante coincide con la del raycast individual en acera, calzada y bordillo.
6. **Reubicación y consultas:**
   - `Relocate` en los dos agentes, `LaneSegmentCount`/`GetLaneSegment`, `GetGap`;
   - re-entrada en `TrafficLaneOccupancy` en `OnEnable`;
   - tests PlayMode:
     - un coche reubicado sigue su carril, frena por el de delante y no deja ocupación en el tramo anterior;
     - un peatón reubicado camina y cruza por los pasos;
     - reactivar un coche lo vuelve a poner en la ocupación de su tramo.
7. **Gancho de animación** (`AnimationSpeed`, `SetAnimatorDriven`) con su test.
8. **Experimento *Optimize Game Objects*:** medir y adoptar o descartar (se anota en las dos opciones).
9. **Medición final** del caso 1000 + 1000 con todo apagado y con todo encendido, **documentación** y **versión 3.7.0** con la ventana de release.

## Criterios de aceptación

**Compatibilidad**

- [ ] Con todas las opciones por defecto (filtro `true`, radios `0`, foco `null`), la suite completa (EditMode, PlayMode, Performance) pasa sin cambios y una ciudad de demo se comporta igual que en 3.6.1.
- [ ] Con `Enable Physics` OFF sigue sin haber `Rigidbody`, pares ni actividad en `FixedUpdate` (invariante de SPEC 17).

**Filtro delegable**

- [ ] Con `VehiclePedestrianPairFiltering = false` antes de cargar la escena no se registra ningún par de `IgnoreCollision`.
- [ ] Ponerlo a `false` con agentes activos borra los pares existentes.
- [ ] El paquete no escribe nunca en la matriz de colisiones del proyecto.

**LOD**

- [ ] Con `physicsRadius > 0`, los coches físicos fuera del radio + histéresis son cinemáticos y no generan llamadas a `OnCollisionStay` entre ellos.
- [ ] Al cruzar el radio, la velocidad de un coche no cambia más de 0,5 m/s entre dos pasos de física.
- [ ] Dentro del radio, un choque fuerte lo lleva a `Recovering` como hoy.
- [ ] Con `colliderRadius > 0` y la rejilla de proximidad activa, los peatones fuera del radio no tienen collider activo. Por debajo del umbral de la rejilla, todos lo conservan.
- [ ] Los coches siguen frenando por los peatones (rejilla) y no atraviesan ningún semáforo en rojo con las opciones encendidas.
- [ ] Los agentes escalonados por no ser visibles mueven su posición en cada frame. Solo su lógica y sus sensores se escalonan.

**Reubicación**

- [ ] `CarAgent.Relocate` deja el coche en el tramo pedido con su rumbo, sin ocupación ni reserva colgando del sitio anterior, y circula con normalidad.
- [ ] `PedestrianAgent.Relocate` deja al peatón en el nodo, en el suelo, y camina hacia un destino nuevo.
- [ ] Un coche desactivado y reactivado vuelve a estar en `TrafficLaneOccupancy` desde el primer frame.

**Rendimiento**

- [ ] La tabla de 1000 + 1000 (opciones apagadas frente a encendidas) está en las notas. Cada opción que no mejore el marcador de su subsistema se retira antes de publicar, en lugar de publicarse sin beneficio.

**Documentación**

- [ ] Docs de arquitectura, referencia de API en los dos idiomas, `CLAUDE.md` y `CHANGELOG.md` actualizados. Versión 3.7.0 publicada.

## Decisiones tomadas y descartadas

- **Sí: todo opcional y apagado por defecto.** Los invariantes de SPEC 17 (modo cinemático idéntico, filtro por pares) y de SPEC 05 (`staggerMinAgentCount`) se mantienen para quien no active nada.
- **Sí: el filtro coche↔peatón se puede desactivar, pero el paquete nunca escribe la matriz.** El invariante «el arreglo nunca es una entrada de la matriz» existe porque el jugador a pie del paquete comparte la capa `Pedestrian` y porque el paquete no debe tocar los Project Settings del usuario. Las dos razones siguen en pie para el paquete. Un proyecto sin jugador a pie (City Races conduce un coche) puede usar su propia matriz y ahorrarse un millón de pares, y es él quien decide. El invariante de `CLAUDE.md` se matiza en ese sentido, no se elimina.
- **Sí: LOD de física por radio desde un foco, no por visibilidad.** La física importa donde puede haber contacto con el jugador, no donde mira la cámara.
- **Sí: cinemático con `MovePosition` fuera del radio, no sin `Rigidbody`.** Quitar y poner el componente cambiaría el modo detectado por `GetComponent<Rigidbody>()` (invariante de SPEC 17) y sería más caro.
- **Sí: escalonar sensores y lógica, nunca el movimiento.** Mover a saltos lo que podría estar en pantalla se ve; el movimiento es barato.
- **Sí: `RaycastCommand` para el suelo, manteniendo el raycast.** Conserva el criterio de SPEC 03 (la altura sale del collider real bajo los pies) y lo reparte entre hilos. Descartada una rejilla de alturas precalculada: tendría que reproducir plazas y lugares personalizados y se desincronizaría con cualquier collider del usuario.
- **Sí: reubicar en lugar de instanciar.** Un pool de agentes generados evita destruir colliders (con la tabla de pares, carísimo) y la configuración por instancia que hace el generador (capas, máscaras).
- **Descartado: la burbuja de población dentro del paquete.** Su política depende de cámaras, cinemáticas y teletransportes del juego. Si un segundo consumidor la necesita, se generaliza entonces.
- **Descartado (por ahora): Jobs + Burst y ECS.** Sigue valiendo el veredicto de la revisión técnica: sin dependencias nuevas mientras el perfilado no lo pida.
- **Descartado: bajar `staggerMinAgentCount`.** Invariante.
- **Descartado: actualizar los `Animator` lejanos a mano a menos frecuencia (`Animator.Update`).** Cada llamada manual se ejecuta en el hilo principal y saca a ese Animator del procesado multihilo.

## Riesgos identificados

- **El cambio dinámico ↔ cinemático justo cuando el jugador llega a un coche.** Con 135 km/h y un radio de 80 m hay algo más de 2 s de margen, y la histéresis evita el parpadeo. Lo cubren un criterio y un test.
- **Un collider de peatón apagado y un `TrafficObstacle` o código del usuario que esperaba encontrarlo con una consulta física lejos del foco.** Mitigación: opción apagada por defecto, documentada.
- **`Relocate` en mitad de un cruce reservado por otro coche.** Mitigación: solo se admiten tramos rectos (`GetLaneSegment`); `Relocate` devuelve `false` con un tramo que no lo es.
- **`RaycastCommand` completado en el mismo frame no sale más barato que N raycasts si hay pocos peatones.** Mitigación: solo por encima del umbral; lo dice la medición.
- **El experimento *Optimize Game Objects* rompe a algún usuario que cuelga objetos de los huesos de los personajes de demo.** Mitigación: solo se adopta con una mejora clara y se avisa en el CHANGELOG.
- **Desarrollo en paralelo con City Races** (Unity 6000.6 frente a 6000.5 aquí). El paquete declara `unity: 6000.0` y las APIs usadas existen desde mucho antes, pero se prueba en los dos Editores antes de publicar.

## Notas de implementación

### Paso 1 — instrumentación y medición de partida (2026-11-14)

- **Marcadores** (`TrafficManager`, `CarAgent`, `PedestrianManager`, `PedestrianAgent`):
  - `CityGenerator.Traffic.Tick` y, dentro, `CityGenerator.Traffic.Sensors` con un submarcador por sensor: `.Vehicles` (con `.VehicleOverlap` para el `OverlapSphere` de respaldo), `.Pedestrians` y `.Obstacles`;
  - `CityGenerator.Traffic.CollisionCallbacks` (`OnCollisionEnter`/`OnCollisionStay`) y `CityGenerator.Traffic.SyncTransforms`;
  - `CityGenerator.Pedestrians.Tick` (con `CityGenerator.Pedestrians.Grounding` dentro), `.Separation` y `.ProximityGrid`.
- **Tests nuevos** en `RuntimePerformanceTests`: `PerFrameCost_400Agents_15x15_Physics` y `PerFrameCost_1000Agents_15x15_Physics`.
  - Graban también los marcadores y `Physics.Simulate`.
  - Esperan `pedestrianCount × 0,01 + 1` s antes de medir, porque el primer destino de cada peatón se planifica escalonado.
  - **La ciudad del test no tiene `Main Camera`** (`CityGeneratorContentAssembler` no la crea; la crea `CityGeneratorSceneBuilder`). Sin cámara no hay escalonado de sensores, así que los tests miden el peor caso: todos los coches ejecutan los sensores en cada paso.
- **Máquina:** ASUS ROG Zephyrus G16, Intel Core Ultra 9 285H, RTX 5060 Laptop, 32 GB. Medido en el Editor (Unity 6000.5.8f1), Play Mode del Test Runner.
- **Resultados** (mediana por frame, ms):

  | Marcador | 400 + 400 | 1000 + 1000 (1.ª) | 1000 + 1000 (2.ª, con submarcadores) |
  | --- | --- | --- | --- |
  | Frame | 13,6 | 196 | 318 |
  | `Traffic.Tick` | 4,1 | 135 | 229 |
  | `Traffic.Sensors` | 3,2 | 111 | 188 |
  | ↳ `.Vehicles` (↳ `.VehicleOverlap`) | — | — | 81 (54) |
  | ↳ `.Pedestrians` | — | — | 104 |
  | ↳ `.Obstacles` | — | — | 0,9 |
  | `Traffic.CollisionCallbacks` | 0,05 | 1,2 | 2,1 |
  | `Pedestrians.Tick` (con `Grounding`) | 1,5 (0,4) | 4,0 (0,9) | 4,2 (0,9) |
  | `Pedestrians.Separation` | 1,1 | 3,3 | 3,5 |
  | `Pedestrians.ProximityGrid` | 0,2 | 0,7 | 0,7 |
  | `Physics.Simulate` | 0,9 | 16,9 | 27,6 |

- **Lectura:**
  - **El coste dominante son los sensores de los coches**, no la física: el ~80 % de `Traffic.Tick`.
  - Dentro de los sensores, el de peatones (consulta a `PedestrianRoadProximityGrid`: 25 celdas por coche con radio 12 m y celdas de 10 m) pesa más que el de vehículos.
  - En el de vehículos, dos tercios son el `OverlapSphere` de respaldo. Se ejecuta siempre que `TrafficLaneOccupancy` no encuentra coche delante en el mismo tramo, es decir, también en el caso más común: carril libre a mitad de manzana.
  - **Espiral de pasos de física:** en modo físico los sensores corren en `FixedUpdate`. Con frames de 200–300 ms hay hasta 16 pasos por frame (`Maximum Allowed Timestep` 0,333 s), y cada paso vuelve a ejecutar los sensores; por eso el frame empeora de una ejecución a otra.
  - La física, las colisiones, el suelo de los peatones y la separación son de segundo orden a esta escala.
- **Hallazgo aparte:** `PedestrianManager.ApplyPlayerAvoidance` llama a `FindAnyObjectByType<PlayerController>()` en cada frame mientras la escena no tiene el jugador a pie del paquete (el caso de City Races, `playerEnabled = false`).
