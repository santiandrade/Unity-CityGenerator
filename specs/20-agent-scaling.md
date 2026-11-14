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

0. **Coste de los sensores de los coches** (añadido tras la medición del paso 1, decidido por Santi: activo por defecto para todos los usuarios). Con 1000 coches, los sensores son el ~80 % del coste del tráfico (ver *Notas de implementación*):
   - **(a) Sensores una vez por frame en modo físico:**
     - `TrafficManager.FixedUpdate` solo pide los sensores en el primer paso de física de cada frame renderizado; en los demás pasos del mismo frame se reutilizan los valores cacheados, como ya hace el escalonado;
     - el escalonado de los coches lejanos se cuenta en frames con paso de física, no en pasos, para que un coche nunca caiga siempre en un paso sin sensores;
     - a 50 fps o más no cambia nada (hay como mucho un paso por frame); por debajo, los sensores van al ritmo del frame, igual que los de un coche cinemático;
     - corta la espiral en la que un frame lento trae más pasos de física y cada paso vuelve a pagar los sensores.
   - **(b) Sin `OverlapSphere` a mitad de manzana:**
     - si `TrafficLaneOccupancy` no encuentra coche delante en el tramo del coche y este está en el centro de su manzana (a más de `sensorRange + 8 m` de su nodo objetivo y a más de 12 m del centro del cruce que acaba de dejar), el `OverlapSphere` solo podría encontrar coches de su propio carril que el índice tenga bajo otra clave, o coches en `Recovering`. Los 8 m cubren el saliente de un vehículo largo y `arriveRadius`; los 12 m, media calle más medio vehículo largo;
     - esos dos casos se comprueban explícitamente: un índice nuevo por nodo objetivo (`TrafficLaneOccupancy.HasCarAheadHeadingTo`, cualquier clave, `(-1, objetivo)` incluida) y un registro de los coches en `Recovering` (`CarAgent.RecoveringCars`, normalmente vacío). Si hay alguno, se hace el `OverlapSphere` como hoy;
     - en cualquier otro caso todo sigue igual que hoy;
     - depende de que todo coche activo esté en el índice, así que el arreglo de re-entrada en `TrafficLaneOccupancy` al reactivar un coche (punto 8) se adelanta aquí;
     - la forma final salió de la verificación exacta del paso 1b (ver *Notas de implementación*).
   - **(c) Solo los peatones que pisan la calzada en el sensor de peatones:**
     - un peatón generado solo pisa la calzada en los tramos de paso de peatones (`Curb` ↔ `Crossing`). En la acera ya queda fuera del sensor, que descarta lo que esté a más de 1,6 m del centro del carril (la acera empieza a 2,4 m);
     - `PedestrianRoadProximityGrid` indexa solo esos peatones, con la posición cacheada al reconstruir (`PedestrianAgent.IsOnCrossingArm`);
     - la consulta de `CarAgent` recorre solo las celdas que cruza el rayo del sensor, unas 3–6, en vez del cuadrado de 5 × 5 celdas alrededor del origen;
     - el jugador a pie del paquete se sigue comprobando aparte, esté donde esté.
   - **(d) Sin búsquedas de escena por frame:**
     - `PedestrianManager.ApplyPlayerAvoidance` reintenta `FindAnyObjectByType<PlayerController>()` como mucho una vez por segundo mientras no lo encuentra;
     - lo mismo `CarAgent` con `FindAnyObjectByType<PedestrianRoadProximityGrid>()` en una ciudad sin peatones, donde hoy se buscaba en cada ejecución del sensor de cada coche.
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
6. **Suelo de los peatones por lotes** *(implementado y retirado en el paso 5: no mejoraba; ver *Notas de implementación*. Lo sustituye el atajo del suelo, añadido tras la fase 0 de City Races: ver *Notas de implementación*)*:
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
   - **Arreglo:** `CarAgent.OnEnable` vuelve a entrar en `TrafficLaneOccupancy` si ya tenía tramo (`targetNode >= 0`). Adelantado al paso 1b, porque el atajo (b) depende de él.
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
public void SetGroundingShortcutDistance(float meters); // Pedestrians; 0 = off (added after phase 0 of City Races)
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
[SerializeField] private float groundingShortcutDistance;    // 0 = off (default)
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
1b. **Coste de los sensores** (punto 0 del *Scope*, en este orden: d, a, c, b):
   - dos interruptores internos, solo para los tests (`CarAgent.VehicleSensorShortcut` y `CarAgent.PedestrianSensorShortcut`, `internal`), permiten comparar el comportamiento con y sin (b) y (c) en la misma ciudad, y `CarAgent.VerifyVehicleShortcut` ejecuta igualmente cada `OverlapSphere` que (b) se salta y anota lo que habría encontrado;
   - tests PlayMode (`Traffic/SensorCostTests`):
     - un coche a mitad de manzana frena detrás de un coche en `Recovering` parado en su carril, y detrás de uno reenganchado con `fromNode = -1`;
     - un coche se detiene ante un peatón en el paso de peatones, con el semáforo de los coches en verde y el peatón ya cruzando;
     - en modo físico, con varios pasos de física por frame, cada coche ejecuta los sensores como mucho una vez por frame, y un coche lejano los ejecuta alguna vez aunque siempre haya 2 pasos por frame;
   - **equivalencia** (`Traffic/SensorShortcutEquivalenceTests`, `[Explicit]` por su duración): en una ciudad de 15 × 15 con 1000 coches cinemáticos y 1000 peatones, 60 s con los atajos apagados y 60 s encendidos, se cuentan cada medio segundo los pares de coches cuyos colliders se solapan más de 0,3 m (`Physics.ComputePenetration`) y los peatones de un paso de peatones con un coche a menos de 1 m. En modo cinemático un coche solo frena por lo que ven sus sensores, así que un coche que no ve a otro acaba dentro de él. Con los atajos no puede haber más que sin ellos (con un margen de ruido de 2 o del 20 %);
   - se repite la medición del paso 1 y va a las notas.
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
8. **Experimento *Optimize Game Objects*:** aplazado a la fase 0 de City Races (ver *Notas de implementación*).
9. **Medición final** del caso 1000 + 1000 con todo apagado y con todo encendido, **documentación** y **versión 3.7.0** con la ventana de release.

## Criterios de aceptación

**Compatibilidad**

- [ ] Con todas las opciones por defecto (filtro `true`, radios `0`, foco `null`), la suite completa (EditMode, PlayMode, Performance) pasa sin cambios y una ciudad de demo se comporta igual que en 3.6.1.
- [ ] Con `Enable Physics` OFF sigue sin haber `Rigidbody`, pares ni actividad en `FixedUpdate` (invariante de SPEC 17).

**Coste de los sensores**

- [ ] En la medición de 1000 + 1000, `CityGenerator.Traffic.Sensors` baja al menos un 60 % respecto al paso 1, y `Sensors.VehicleOverlap` deja de ser la mayor parte de `Sensors.Vehicles`.
- [ ] En modo físico, los sensores de un coche se ejecutan como mucho una vez por frame renderizado.
- [ ] Un coche a mitad de manzana sigue frenando detrás de un coche en `Recovering` o reenganchado que tenga delante.
- [ ] Un coche se sigue deteniendo ante un peatón que cruza por el paso de peatones.
- [ ] El test de equivalencia no da más solapes entre coches ni más peatones rozados con los atajos que sin ellos.
- [ ] `PedestrianManager` y `CarAgent` no llaman a `FindAnyObjectByType` más de una vez por segundo cuando no hay jugador a pie o no hay peatones.

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

- **Sí: los atajos de los sensores (punto 0) activos por defecto** (decidido por Santi tras la medición del paso 1). Son optimizaciones que no deberían cambiar el resultado de los sensores, demostrado con el test de equivalencia, y cualquier usuario del paquete gana con ellas, no solo quien tiene 1000 coches. Descartado ponerlas detrás de una opción: nadie la activaría, y la regla 3 de los sensores se matiza en la documentación en lugar de quedar intacta.
- **Sí: indexar solo los peatones que pisan la calzada, en lugar de consultar solo cerca de los cruces.** Las dos ideas dicen lo mismo (solo importan los peatones de los pasos de peatones), pero filtrar por distancia a los nodos de tráfico dependería de `crossingArmOffset` y de `laneOffset`, que son configurables. El tramo del propio peatón lo sabe sin geometría.
- **Sí: todo lo demás, opcional y apagado por defecto.** Los invariantes de SPEC 17 (modo cinemático idéntico, filtro por pares) y de SPEC 05 (`staggerMinAgentCount`) se mantienen para quien no active nada.
- **Sí: el filtro coche↔peatón se puede desactivar, pero el paquete nunca escribe la matriz.** El invariante «el arreglo nunca es una entrada de la matriz» existe porque el jugador a pie del paquete comparte la capa `Pedestrian` y porque el paquete no debe tocar los Project Settings del usuario. Las dos razones siguen en pie para el paquete. Un proyecto sin jugador a pie (City Races conduce un coche) puede usar su propia matriz y ahorrarse un millón de pares, y es él quien decide. El invariante de `CLAUDE.md` se matiza en ese sentido, no se elimina.
- **Sí: LOD de física por radio desde un foco, no por visibilidad.** La física importa donde puede haber contacto con el jugador, no donde mira la cámara.
- **Sí: cinemático con `MovePosition` fuera del radio, no sin `Rigidbody`.** Quitar y poner el componente cambiaría el modo detectado por `GetComponent<Rigidbody>()` (invariante de SPEC 17) y sería más caro.
- **Sí: escalonar sensores y lógica, nunca el movimiento.** Mover a saltos lo que podría estar en pantalla se ve; el movimiento es barato.
- **Probado y descartado: `RaycastCommand` para el suelo** (paso 5: con 1000 peatones costaba lo mismo que el raycast por agente). Se mantiene el raycast individual, que conserva el criterio de SPEC 03 (la altura sale del collider real bajo los pies). Descartada también una rejilla de alturas precalculada: tendría que reproducir plazas y lugares personalizados y se desincronizaría con cualquier collider del usuario.
- **Sí: reubicar en lugar de instanciar.** Un pool de agentes generados evita destruir colliders (con la tabla de pares, carísimo) y la configuración por instancia que hace el generador (capas, máscaras).
- **Descartado: la burbuja de población dentro del paquete.** Su política depende de cámaras, cinemáticas y teletransportes del juego. Si un segundo consumidor la necesita, se generaliza entonces.
- **Descartado (por ahora): Jobs + Burst y ECS.** Sigue valiendo el veredicto de la revisión técnica: sin dependencias nuevas mientras el perfilado no lo pida.
- **Descartado: bajar `staggerMinAgentCount`.** Invariante.
- **Descartado: actualizar los `Animator` lejanos a mano a menos frecuencia (`Animator.Update`).** Cada llamada manual se ejecuta en el hilo principal y saca a ese Animator del procesado multihilo.

## Riesgos identificados

- **El cambio dinámico ↔ cinemático justo cuando el jugador llega a un coche.** Con 135 km/h y un radio de 80 m hay algo más de 2 s de margen, y la histéresis evita el parpadeo. Lo cubren un criterio y un test.
- **Atajo (c) y peatones empujados a la calzada fuera de un paso:** la evitación del jugador a pie del paquete o la separación entre peatones podrían sacar a un peatón más de 0,8 m hacia la calzada a mitad de manzana, y el sensor ya no lo vería. Riesgo bajo: el empuje es de centímetros y no hay rutas por la calzada. Se documenta.
- **Atajo (b) y un coche que el índice no conoce por un camino no previsto** (otra forma de quedarse sin tramo). Mitigación: el registro `OffLaneCars` se llena desde los únicos sitios donde un coche deja el índice sin desactivarse, y el test de equivalencia lo vigila.
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

### Paso 1b — coste de los sensores (2026-11-14)

- **Implementado** como en el punto 0 del *Scope*, con dos interruptores internos en lugar de uno (`CarAgent.VehicleSensorShortcut` para (b), `CarAgent.PedestrianSensorShortcut` para (c)), para poder medir cada atajo por separado.
- **El atajo (b) final es más estricto que el del *Scope*.** Se saltó el `OverlapSphere` solo si se cumple todo esto:
  - el coche tiene tramo propio (`fromNode >= 0`);
  - está a más de `sensorRange + 8 m` de su nodo objetivo;
  - está a más de 12 m (`CrossingClearDistance`) del centro del cruce que acaba de dejar;
  - ningún otro coche que vaya hacia **el mismo nodo objetivo**, bajo cualquier clave, está por delante (`TrafficLaneOccupancy.HasCarAheadHeadingTo`, sobre un índice nuevo por nodo objetivo);
  - no hay ningún coche en `Recovering` a su alcance (`RecoveringCars`).
- **Cómo se llegó a esas condiciones:** las dos últimas condiciones geométricas salieron de la verificación exacta (ver más abajo), que encontró dos huecos en la versión del *Scope*:
  1. **Un mismo carril físico tiene coches bajo claves distintas.** Uno que siguió recto por el cruce está registrado como (entrada anterior, entrada siguiente) y uno que giró como (salida, entrada siguiente). «Nadie delante en mi clave» no era «nadie delante en mi carril». El índice de carril original ya tenía este punto ciego y el `OverlapSphere` lo tapaba sin que se notara. La comprobación de `(-1, objetivo)` del *Scope* queda incluida en el índice por objetivo.
  2. **Justo a la salida de un cruce**, un coche que sigue dentro de él (girando por delante, o incorporándose antes de llegar al nodo donde cambiaría de tramo) puede estar delante en otro tramo.
- **Lo que deja el atajo:** cubre aproximadamente el 40 % central de cada manzana (de 12 m pasado el cruce a 20 m antes del siguiente), y solo cuando el carril está vacío por delante.
- **Tests** (`Traffic/SensorCostTests`, 6, en verde):
  - sin `OverlapSphere` a mitad de manzana sin nadie delante;
  - frena detrás de un coche en `Recovering`;
  - frena detrás de un coche `(-1, objetivo)`;
  - re-entrada en el índice al reactivarse;
  - en modo físico, sensores como mucho una vez por frame;
  - un coche escalonado los ejecuta igual con dos pasos por frame.

  **Verificado por mutación** dos veces (con la versión inicial y con la final): quitando las comprobaciones de excepción, los dos tests de frenado fallan (el coche acaba más de 20 m más allá de donde debía pararse).
- **Verificación exacta** (`Traffic/SensorShortcutEquivalenceTests.VehicleShortcut_NeverSkipsACarTheOverlapWouldSee`, `[Explicit]`):
  - con `CarAgent.VerifyVehicleShortcut` (solo tests), cada `OverlapSphere` que el atajo se salta se ejecuta igualmente, sin efectos secundarios;
  - cualquier coche por el que habría frenado (hueco < `brakeDistance`) se anota con el estado de los dos coches en el grafo;
  - ciudad de 15 × 15, 1000 coches cinemáticos y 1000 peatones, 120 s;
  - resultados: versión del *Scope*, más de 500 fallos (todos del hueco 1); con el índice por objetivo, 118 (todos del hueco 2); **versión final, 0**.
- **Equivalencia estadística** (mismo fichero, `[Explicit]`): ventanas alternas de 10 s con el atajo apagado y encendido, contando sucesos nuevos (pares de coches que empiezan a solaparse más de 0,3 m, separados entre mitad de manzana y el resto; peatones de un paso de peatones que empiezan a tener un coche a menos de 1 m), con un control que no cambia nada. Sucesos nuevos en 60 s, primeras → segundas ventanas, **con la versión del *Scope***:

  | Ejecución | Solapes a mitad de manzana | Otros solapes | Peatones |
  | --- | --- | --- | --- |
  | Control (nada cambia) | 295 → 307 (+4 %) | 244 → 274 (+12 %) | 481 → 458 (−5 %) |
  | Solo (b) | 246 → 263 (+7 %) | 153 → 233 | 455 → 410 (−10 %) |
  | Solo (c) | 255 → 273 (+7 %) | 256 → 224 | 490 → 440 (−10 %) |
  | (b) + (c), tres ejecuciones | +40 %, +57 %, +48 % | | −7 %, −2 %, −10 % |

  - **Lectura:** con los dos atajos juntos, (b) se notaba porque (c) hace que los coches que giran lleguen más rápido a las incorporaciones (ya no frenan por peatones de la acera), justo el hueco 2.
  - **Decisión:** el test estadístico se queda como herramienta de diagnóstico, pero la verificación exacta es la prueba que cuenta. La estadística tiene un ruido del orden del 10 % y no distingue entre un atajo que se pierde coches y un tráfico que cambia de ritmo.
  - **(c) reduce los peatones rozados** en todas las ejecuciones: los coches dejan de frenar por peatones de la acera en los giros (falsos positivos) y llegan menos atascados a los pasos.
- **Hallazgo:** sin ningún atajo, 1000 coches cinemáticos producen ~250 solapes nuevos por minuto a mitad de manzana y otros tantos en los cruces. Son incorporaciones tras los giros (el punto ciego 1 del índice, que el `OverlapSphere` no siempre tapa a tiempo) y la ruptura de bloqueos, que hace pasar a un coche a propósito. En modo físico los `Rigidbody` lo convierten en empujones. Es una limitación de densidad del tráfico independiente del rendimiento, del mismo tipo que la falta de planificación de rutas (F.4.1).
- **Medición** de 1000 + 1000 (15 × 15, modo físico, la misma prueba del paso 1), mediana por frame en ms:

  | Marcador | Paso 1 (2.ª) | 1b, versión del *Scope* | 1b final | Cambio total |
  | --- | --- | --- | --- | --- |
  | Frame | 318 | 34,1 | 45,5 | −86 % |
  | `Traffic.Tick` | 229 | 10,8 | 15,5 | −93 % |
  | `Traffic.Sensors` | 188 | 7,0 | 9,2 | −95 % |
  | ↳ `.Vehicles` (↳ `.VehicleOverlap`) | 81 (54) | 5,0 (2,9) | 6,9 (4,2) | |
  | ↳ `.Pedestrians` | 104 | 1,8 | 2,1 | −98 % |
  | `Traffic.CollisionCallbacks` | 2,1 | 0,2 | 0,35 | |
  | `Pedestrians.Tick` (con `Grounding`) | 4,2 (0,9) | 4,6 (1,1) | 5,3 (1,3) | |
  | `Pedestrians.Separation` | 3,5 | 1,9 | 2,4 | |
  | `Physics.Simulate` | 27,6 | 3,4 | 4,1 | −85 % |

  - La versión del *Scope* era más rápida porque se saltaba consultas que no podía saltarse.
  - Parte de la bajada de `Traffic.Tick` y de `Physics.Simulate` viene de cortar la espiral (a).
  - Los marcadores de peatones, que no cambian en este paso, varían un ~15 % entre ejecuciones: es el ruido de medir en el Editor.
- **Criterios de aceptación:**
  - **Cumplido:** `Traffic.Sensors` baja un 95 %, por encima del 60 % pedido.
  - **No cumplido:** `Sensors.VehicleOverlap` sigue siendo la mayor parte de `Sensors.Vehicles` (61 %), porque el atajo seguro solo cubre el centro de cada manzana con el carril vacío. **Queda abierto** como posible mejora: usar el índice por objetivo como búsqueda principal del coche de delante (con filtro lateral) en vez de solo como guarda del atajo.
- **Suite:** EditMode 139/139 y PlayMode 98/98 en verde con la versión del *Scope*, y PlayMode 98/98 otra vez con la versión final (los tests `[Explicit]` no cuentan).

### Pasos 2–7 — opciones de escalado, reubicación y gancho de animación (2026-11-14)

- **Implementado** como en el *Scope* (puntos 2–5, 7–9), salvo lo que se indica aquí.
- **Filtro delegable (2):** `VehiclePedestrianCollisionFilter.Enabled` mantiene el registro de los dos lados aunque esté apagado. Así, volver a encenderlo empareja a todos los agentes activos en ese momento, y apagarlo restaura los pares ya ignorados. El paquete sigue sin escribir la matriz: el invariante de `CLAUDE.md` se matiza, no se elimina.
- **Foco y escalonado (3):** `TrafficManager`/`PedestrianManager.SimulationFocus` y `SetStagger(distance, frames, invisibleDistance)`.
  - La visibilidad se lee del primer `Renderer` de la jerarquía (`IsVisible`), cacheado en `OnEnable`/`Awake`.
  - El escalonado por visibilidad solo se aplica más allá de `invisibleDistance`. Así no se escalona un coche invisible pegado al jugador, por ejemplo el que va detrás del taxi en la vista de tercera persona.
- **LOD de física (4):**
  - un coche cinemático se mueve con `MovePosition`;
  - un impacto mientras es cinemático lo libera (`EnterRecovering` lo vuelve dinámico);
  - el modo se cachea en `CarAgent` (`kinematicByLod`), y el LOD se decide una vez por frame con paso de física, cada coche uno de cada 4 frames. La histéresis de 10 m absorbe ese retraso.

  En la primera versión, que miraba todos los coches en cada paso de física y le preguntaba al motor `rb.isKinematic`, el marcador costaba 0,31 ms apagado y 0,85 ms encendido; ahora cuesta 0 y 0,32 ms.
- **LOD de colliders de peatones (4):**
  - mismo criterio de coste: estado cacheado en `PedestrianAgent` y cada peatón comprobado uno de cada 4 frames;
  - un collider que se vuelve a encender se empareja de nuevo con los coches con físicas (`VehiclePedestrianCollisionFilter.RepairPedestrian`);
  - solo actúa mientras la rejilla de proximidad responde al sensor de los coches.
- **Suelo de los peatones por lotes (5): implementado, medido y retirado.**
  - Con 1000 peatones, `Pedestrians.Tick` + `Grounding` costaban lo mismo con el lote de `RaycastCommand` que con el raycast por agente: 5,4 ms frente a 5,3 ms, y 5,4 ms frente a 5,1 ms en una segunda medición, incluso después de escribir la altura solo cuando cambiaba.
  - Programar y completar el lote en el hilo principal y volver a escribir la altura se comen la ganancia del reparto entre hilos.
  - Siguiendo el criterio de aceptación («cada opción que no mejore su subsistema se retira»), el código y su test se quitaron.
- **Reubicación (6):**
  - **Tramos de calle:** `TrafficNetwork.LaneSegmentCount`/`GetLaneSegment`/`IsLaneSegment`, es decir, los pares salida → entrada del siguiente cruce, cacheados y reiniciados en cada `Build`.
  - **Firma de `GetGap` distinta a la del *Scope*:** queda `GetGap(toNode, position, forward, out ahead, out behind)` y no `GetGap(from, to, position, …)`. El índice no conoce las posiciones de los nodos, y lo que importa es el carril físico, que se identifica por el nodo objetivo con cualquier clave.
  - **`CarAgent.Relocate(from, to, t)`:**
    - funciona también con el coche inactivo: la ocupación se registra en su `OnEnable`;
    - si se llama antes de su primer `Start`, este ya no busca nodo, porque el coche ya lo sabe.
  - **`PedestrianAgent.Relocate(node)`** rechaza los nodos `Crossing`, los bloqueados y los que quedan fuera de los nodos permitidos de un Custom Pedestrian.
- **Gancho de animación (7):** `PedestrianAgent.AnimationSpeed` y `SetAnimatorDriven(bool)`.
- **Experimento *Optimize Game Objects* (8): aplazado.**
  - Los tests de rendimiento del paquete no tienen cámara, así que todos los `Animator` de los peatones están en `Cull Completely`, no se ven y su coste no aparece: no hay con qué medirlo.
  - Se medirá en la fase 0 de City Races, que tiene cámara y peatones en pantalla. Si `Animation` pesa allí, se hará como cambio aparte del paquete.
- **Tests nuevos** (`Traffic/AgentScalingTests`, 10, en verde):
  - filtro delegable;
  - foco que sustituye a la cámara;
  - escalonado por invisibilidad;
  - LOD de física (cambio de modo sin salto de velocidad, y un coche en `Recovering` no cambia);
  - LOD de colliders con re-emparejado;
  - reubicación de coche (tramo, ocupación vieja y nueva, sigue circulando) y de coche en `Recovering`;
  - reubicación de peatón (nodo, rechazo de `Crossing`, vuelve a andar);
  - `SetAnimatorDriven`.
- **Suite:** EditMode 139/139 y PlayMode 108/108 en verde.
- **Medición** de 1000 + 1000 (15 × 15, modo físico), mediana por frame en ms. Columna «con opciones»: foco en el centro de la ciudad, física a 80 m, colliders a 40 m, escalonado de lo invisible más allá de 25 m. El test no tiene cámara, así que nada es visible, como todo lo que queda fuera de pantalla en un juego. El filtro por pares sigue encendido, porque la matriz de este proyecto no separa coches y peatones.

  | Marcador | Opciones apagadas | Con opciones |
  | --- | --- | --- |
  | Frame | 37,4 | 21,8 |
  | `Traffic.Tick` | 12,4 | 4,2 |
  | `Traffic.Sensors` | 7,8 | 2,0 |
  | `Traffic.CollisionCallbacks` | 0,26 | 0 |
  | `Traffic.PhysicsLod` | 0 | 0,32 |
  | `Pedestrians.Tick` (con `Grounding`) | 4,9 (1,1) | 4,7 (0,9) |
  | `Pedestrians.Separation` | 2,0 | 1,8 |
  | `Pedestrians.ColliderLod` | 0 | 0,32 |
  | `Physics.Simulate` | 3,7 | 1,0 |

  Respecto al paso 1 (318 ms de frame con 1000 + 1000), el frame queda en 37 ms solo con lo que va activo por defecto y en 22 ms con las opciones. Lo que más pesa ahora son los peatones (`Tick` + `Separation`, ~6,5 ms), y el resto del frame es el propio Editor y el render. Lo siguiente se mide en el juego real (fase 0 de City Races).
- **Pendiente para cerrar la spec:**
  - publicar la versión 3.7.0 (necesita el visto bueno de Santi);
  - verificar en un consumidor real (City Races) que las opciones funcionan juntas con su burbuja.

### Atajo del suelo de los peatones (2026-11-14, tras la fase 0 de City Races)

- **Por qué:** en la fase 0 de City Races (SPEC 53), con la ciudad de 1000 + 1000 en pantalla, el raycast de suelo de cada peatón en cada frame seguía siendo de lo que más pesaba en `Pedestrians.Tick`. El lote de `RaycastCommand` del punto 6 ya se había descartado, así que se probó a lanzar menos rayos en vez de lanzarlos más baratos.
- **Opción nueva, apagada por defecto:** `PedestrianManager.GroundingShortcutDistance` (`0` = apagado) y `city.Pedestrians.SetGroundingShortcutDistance(float)`. Solo actúa con foco de simulación (`SimulationFocus` o la cámara principal).
- **Cerca del foco** (a esa distancia o menos), nada cambia: un raycast por peatón y frame.
- **Más lejos:**
  - **En un tramo plano** el peatón no lanza rayos: reutiliza la altura del tramo.
    - El tramo se identifica por el par (nodo actual, siguiente nodo del camino) y se evalúa una vez al empezarlo, con dos rayos: en la posición del peatón y en el punto al que apunta.
    - Es plano si los dos rayos dan en el mismo collider, las dos normales tienen `y ≥ 0,999` y la diferencia de altura es como mucho de 5 mm.
    - Un tramo que empieza o acaba en un nodo `Curb` o `Crossing` nunca es plano: los bordillos y los pasos de cebra siempre usan el raycast.
  - **Fuera de un tramo plano** el raycast solo se lanza en los frames en los que corre la lógica del peatón (con escalonado, 1 de cada `staggerFrames`); entre medias conserva la altura anterior.
- **API interna:** `PedestrianAgent.Tick(dt, runLogic)` sigue igual y delega en `Tick(dt, runLogic, exactGrounding)`, que decide el manager.
- **Test nuevo** (`PlayMode/PedestrianGroundingShortcutTests`): ciudad de 3 × 3 con 100 peatones y el foco a 2 km. Con el atajo, las llamadas a `Grounding` deben quedar por debajo del 35 % de las del modo exacto; menos del 5 % de las muestras pueden estar a más de 2 cm del suelo, y ninguna a más de 35 cm (un peatón escalonado puede ir unos frames tarde en un bordillo).
- **Medición en la suite de rendimiento** (1000 + 1000, 15 × 15, modo físico, mediana en ms). La variante con opciones usa ahora también el atajo a 30 m:

  | Marcador | Con opciones, sin atajo (pasos 2–7) | Con opciones y atajo |
  | --- | --- | --- |
  | Frame | 21,8 | 18,5 |
  | `Pedestrians.Tick` | 4,7 | 3,2 |
  | `Pedestrians.Grounding` | 0,9 | 0,14 |
  | `Pedestrians.Separation` | 1,8 | 1,7 |

  En el benchmark de City Races (`CityStress`, 1000 + 1000, development build en High), `Grounding` baja de 1,0–1,2 ms a 0,17–0,19 ms; las cifras completas están en la SPEC 53 de City Races. Lo siguiente que más pesa en los peatones es `Separation`.
- **Suite:**
  - EditMode 139/139, en verde.
  - PlayMode en el Editor: 118/119.
  - Hay dos tests que dependen del ritmo de frames y fallan igual sin este cambio (comprobado guardando los cambios con `git stash`):
    - `CarAgentPhysicsRecoveryTests.Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt` falla en el Editor con la ventana sin foco (el Editor se mantiene ejecutando con `set_autotick` de la CLI), y pasa en batch mode.
    - `AgentScalingTests.CarRelocate_PutsItOnTheSegment_LeavesTheOldOne_AndDrivesOn` y `PedestrianRelocate_PutsItOnTheNode_AndItWalksOn` hacen lo contrario: pasan en el Editor y fallan en batch mode, porque el coche o el peatón reubicado no llega a moverse.
  - Pendiente: hacerlos independientes del ritmo de frames antes de publicar la 3.7.0.
