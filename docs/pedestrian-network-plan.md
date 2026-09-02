# Red de peatones (NPCs) para City Generator

> **Documento superado, conservado solo como historia.** Se escribió *antes* de implementar
> la red peatonal y describe el plan tal como se acordó entonces; el sistema real lleva
> implementado desde SPEC 03 y ha cambiado en varios puntos respecto a lo que se planeó aquí
> (entre otros: los POI peatonales se implementaron y luego se retiraron enteros en SPEC 06,
> y con SPEC 11 sí existen cruces sin semáforo). **La autoridad es
> [`specs/03-pedestrian-network.md`](../specs/03-pedestrian-network.md) y
> [`docs/architecture/pedestrians.md`](architecture/pedestrians.md)** — no tomes nada de este
> fichero como descripción del comportamiento actual.

Documento de planificación original, tal como se escribió.

## Contexto

Hoy la herramienta genera tráfico rodado autónomo (`TrafficNetwork` + `CarAgent` + `TrafficManager`) pero la ciudad está vacía de personas: los 12 prefabs de `DefaultAssets/Prefabs/Characters/` solo se usan como candidatos a Player Prefab. Se quiere una segunda red, la peatonal, con NPCs que recorran rutas A→B por la ciudad, hagan paradas y, en el futuro, interactúen entre ellos y con el jugador — configurable desde la herramienta igual que ya lo son los vehículos ("Include Pedestrians", "Pedestrian Count", lista de prefabs con porcentajes).

Restricciones que fija el usuario: los peatones **solo pisan acera y pasos de cebra**, y cruzan **cuando el tráfico de esa calle tiene rojo**.

**Decisiones tomadas** (tras evaluar grafo propio vs NavMesh vs híbrida):

- **Grafo propio `PedestrianNetwork`**, espejo de `TrafficNetwork`. NavMesh queda descartado: sobre esta geometría (acera y calzada separadas por 18 cm y contiguas) un bake no distingue acera de calzada, así que exigiría pintar áreas en generación + un `NavMeshLink` por brazo de cebra + parar igualmente al agente en el bordillo mirando el semáforo — es decir, reimplementar el grafo **y además** cargar con `com.unity.ai.navigation` en `package.json`, el bake y su peso en escena. La híbrida arrastra el mismo coste sin ahorrar nada. Coherente con el apartado F del technical review, que ya descartó ECS/DOTS con este mismo criterio.
- **Robustez ante ediciones posteriores en tres niveles**: podado en generación, podado auto-reparador en `Awake`, y botón de re-bake explícito.
- **Alcance de comportamiento**: esperar semáforo + paradas ociosas + paradas en puntos de interés (bancos, fuente, césped) + separación local entre peatones.

## Hallazgo que simplifica el diseño

`CityGeneratorGroundBuilder.BuildZebraCrossings` y `CityGeneratorTrafficBuilder.BuildTrafficLights` recorren **exactamente el mismo rango** (`for i = 1; i < gridWidth`, `for j = 1; j < gridHeight`). Por tanto **cebra ⟺ semáforo**: no existe ningún paso de peatones sin semáforo, y el caso "cruce no señalizado" —que en los coches obligó a toda la maquinaria de reservas y deadlocks— **no aparece aquí**. Los peatones solo cruzan por cebra, y toda cebra tiene luz.

Corolario a documentar: con `gridWidth == 1` o `gridHeight == 1` no hay intersecciones interiores, luego no hay cebras ni cruces, y cada manzana queda aislada (los NPCs dan vueltas a su manzana). Degradación aceptable; se avisa en la UI.

## Geometría del grafo

Números reales del proyecto (manzana 46 m, pitch 56 m, calle 10 m, `GroundDatumY` 0.18):

| Radio desde el centro de manzana | Qué hay |
|---|---|
| ~18 m | borde real de los edificios demo (slot 22 m, footprint máx. 13.9 m) |
| **19.5 m** | **anillo peatonal** (`PedestrianRingInset` 3.5) |
| 21 m | papeleras y árboles (`StreetEdgeInset` 2) |
| 22 m | farolas (`LampEdgeInset` 1) |
| 23 m | borde de manzana / bordillo |

El anillo cae en un hueco **libre por construcción** entre edificios y mobiliario urbano. Esa es la razón de que no haga falta evitación de obstáculos.

**Nodos por manzana**: 4 esquinas del anillo + 1 punto medio por lado = 8. En manzanas de plaza, además nodos interiores: 4 radiales desde las esquinas del anillo hacia los 4 bancos (`PlazaBenchRadius`, ya existente) y un anillo corto alrededor del centerpiece.

**Nodos de cruce**: el brazo de cebra está a `ZebraArmOffset` (7.6 m) del centro de intersección. En coordenadas de manzana, la cadena es esquina del anillo `(19.5, 19.5)` → bordillo `(20.4, 23)` → mitad de calzada `(20.4, 28)` → bordillo opuesto `(20.4, 33)` → esquina del anillo de la manzana contigua. La `y` de cada nodo es propia (0.18 en acera, `PedestrianRoadY` 0 en calzada) y el agente hace `MoveTowards` en Y — sin raycasts.

**Semáforo aplicable**: un peatón que cruza en dirección Z atraviesa el flujo de dirección ±X, luego debe cruzar cuando la luz de ese eje está en rojo. `TrafficLightIntersection` ya alterna `eastWest` / `northSouth`, así que basta consultar el estado de una de las dos direcciones de ese eje.

## Archivos nuevos

### `Runtime/PedestrianNetwork.cs`
Espejo estructural de `Runtime/TrafficNetwork.cs`: `SetAxes()` + `Build()` públicos, `Awake` llama a `Build()`, `EnsureBuilt()` en los accesores, nada serializado salvo los ejes y los parámetros de tuning. Nodo con `Position`, `Kind` (Ring / Curb / Crossing / PointOfInterest), `Intersection`, `CrossingAxisIsX`, `LookAt` (para POIs), `Blocked`, y `List<int> Neighbours` (grafo no dirigido, a diferencia del de coches).

- `FindPath(int from, int to, List<int> result)` — BFS con arrays `visited`/`parent` **preasignados y reutilizados** entre llamadas (main thread, sin alloc). ~200 nodos en una 5×5: coste despreciable, y solo se ejecuta cuando un NPC llega a destino.
- `PickRandomDestination(int from)` — nodo aleatorio no bloqueado.
- `CanCross(int crossingNode)` — delega en `TrafficNetwork` el estado de la luz del eje que bloquea. Si no hay `TrafficNetwork`, devuelve true y avisa **una sola vez** por consola (fail-safe explicable, no fallback silencioso).
- `PrunePlacedObstacles()` — el podado auto-reparador (ver más abajo).
- `[ContextMenu("Rebuild Network")]` + `OnDrawGizmosSelected` dibujando el grafo, con los nodos podados en rojo (mismo patrón que `TrafficNetwork.OnDrawGizmosSelected`, incluido el **no** llamar a `EnsureBuilt()` desde los gizmos).

### `Runtime/PedestrianAgent.cs`
Movimiento por transform, sin `CharacterController` ni `Rigidbody` (igual que `CarAgent`; los prefabs de `Characters/` no traen collider y así siguen). Máquina de estados `Walking` / `WaitingToCross` / `Idling`, con `Interacting` reservado para más adelante.

- Sigue la ruta devuelta por `FindPath`; al agotarla pide otro destino y replanifica.
- En un nodo `Curb` que precede a un `Crossing`, pasa a `WaitingToCross` hasta que `CanCross` da luz verde.
- Al llegar a un nodo `PointOfInterest`, con probabilidad configurable pasa a `Idling` unos segundos, orientándose hacia `LookAt`.
- Animator: escribe `Speed` y `Grounded` con **el mismo mapeo que `PlayerController`** (`Speed` 0.5 = walk, 1 = run), de modo que `CharacterAnimator.controller` funciona sin tocarlo. Si el prefab no trae `Animator`, camina igual y avisa una vez — un prefab sin animación es del usuario, no un error.
- `Tick(float dt, bool runLogic)` público, llamado por el manager, con la misma forma que `CarAgent.Tick(dt, runSensor)`.
- Jitter por instancia: offset lateral (`PedestrianLaneJitter`) y velocidad ±10 %, para que no vayan en fila.

### `Runtime/PedestrianManager.cs`
Espejo de `Runtime/TrafficManager.cs`: `Register`/`Unregister` desde `Start`/`OnDisable`, un único `Update`, escalonado por distancia a `Camera.main`. Añade el **grid espacial**: celdas de ~8 m reconstruidas en O(N) por frame, consultadas en 3×3 para la separación local tipo boids. Sin física y sin queries — más barato que RVO. Ese mismo grid es la base de la interacción futura ("¿quién tengo cerca?"), que no depende del sistema de navegación.

### `Editor/CityGeneratorPedestrianBuilder.cs`
Espejo de `CityGeneratorTrafficBuilder`:
- `AddNetworkComponent(...)` con `BuildAxes(...)` (idéntico al del tráfico).
- `AddManagerComponent(...)`.
- `BuildPedestrians(...)` — reparte por porcentaje, spawnea en nodos `Ring` distintos (mismo patrón `nodeOrder[placed.Count % nodeOrder.Count]` que `BuildVehicles`), inyecta la referencia a la red por `SerializedObject` y fija **`Animator.cullingMode = CullCompletely`** en cada instancia. Esto último es la optimización de mayor impacto: el coste dominante de N personajes skinned es la evaluación del Animator, no la lógica.
- Podado en generación: recibe la lista `obstacles` que `ContentAssembler` ya enhebra y marca bloqueados los nodos tapados, usando `CityGeneratorBoundsUtility.GetWorldBounds` (mismo mecanismo que `CityGeneratorPlacementEngine`). Cubre el caso "prefab de edificio de usuario demasiado ancho" en el momento de generar.
- `RegisterPointsOfInterest(...)`: la plaza ya conoce sus posiciones (`CityGeneratorPlazaBuilder.BenchOffsets`, el centerpiece en `block.center`), así que se pasan a la red como nodos POI con su `LookAt`.

### `Editor/CityGeneratorDistributionUtility.cs`
Extracción de `CityGeneratorTrafficBuilder.DistributePercentages` (hoy privado) a una firma reutilizable sobre `IReadOnlyList<float> percentages`. `BuildVehicles` pasa a usarla — no duplicar el reparto de porcentajes en dos sitios.

## Archivos modificados

- **`Runtime/TrafficNetwork.cs`** — un accesor público que dé el estado de la luz de una intersección para un eje, para que `PedestrianNetwork` no vuelva a escanear la escena. Es la única modificación a la lógica de tráfico existente.
- **`Editor/CityGeneratorSettings.cs`** — `includePedestrians` (true) y `pedestrianCount` (40) en `GeneralSettings`, junto a `includeTraffic`/`vehicleCount`; `List<PedestrianEntry> pedestrians` en `CityGeneratorSettings`; `PedestrianEntry { prefab, percentage }`. Se añade un tipo propio en vez de reutilizar `VehicleEntry`: renombrarlo obligaría a tocar el generador de código del writer para ganar dos campos.
- **`Editor/CityGeneratorContentAssembler.cs`** — grupo `Pedestrians` + grupo `PedestrianNetwork`, red construida **solo si `includePedestrians`** (a diferencia de la de tráfico, que se genera siempre porque los semáforos lo hacen), instancias añadidas a `CityBuildSummary`, y `Pedestrians` **excluido de `MarkStatic`** igual que `Vehicles`. El podado en generación consume la lista `obstacles` ya existente.
- **`Editor/CityGeneratorWindow.cs`** — `includePedestrians`/`pedestrianCount` en la sección General; nueva sección "Pedestrians" tras "Vehicles" con `DrawRequiredField(..., isRequired: pedestrianCount > 0)`; `HelpBox` de rendimiento por encima de `PedestrianCountWarningThreshold`; aviso cuando la rejilla es 1×N o N×1 y las manzanas quedan aisladas. Un menú item `Tools > City Generator > Rebuild Pedestrian Network` para el re-bake explícito.
- **`Editor/CityGeneratorValidator.cs`** — bloque espejo del de vehículos (prefabs no nulos, porcentajes suman 100 con `PercentageTolerance`). **No** se bloquea por falta de `Animator`: el agente funciona sin él y avisa.
- **`Editor/CityGeneratorConstants.cs`** — `PedestrianRingInset` 3.5, `PedestrianRoadY` 0, `PedestrianLaneJitter`, `PedestrianCountWarningThreshold`, velocidades y probabilidad/duración de parada. Cada una con su comentario del *porqué*, como el resto del fichero.
- **`Editor/CityGeneratorDefaultAssets.cs`** — los 12 characters como peatones por defecto, repartidos a ~8.33 % (el último ajustado para que sumen 100 exacto).
- **`Editor/CityGeneratorDefaultAssetsWriter.cs`** — `AppendPedestriansList` análogo a `AppendVehiclesList`, y `ReplaceField` para `includePedestrians`/`pedestrianCount`. Ambos nombres son únicos en `CityGeneratorSettings.cs`, así que el `Regex` sigue siendo seguro.
- **`Editor/CityGeneratorTrafficBuilder.cs`** — usar `CityGeneratorDistributionUtility`.
- **`CHANGELOG.md`** (sección `## [Unreleased]`), **`README.md`** y **`README.es.md`** — documentar los ajustes nuevos, el podado auto-reparador y su dependencia de que los edificios tengan collider, y la limitación de las rejillas 1×N.

## El podado en tres niveles

1. **En generación** — contra la lista `obstacles`, vía `CityGeneratorBoundsUtility`. Detecta el prefab de usuario que invade el anillo antes de que se vea mal.
2. **En `Awake`** — `PrunePlacedObstacles()` lanza un `Physics.CheckSphere` por nodo **a 1 m de altura** (por encima de la acera, así que el suelo no cuenta y no hace falta ninguna capa nueva) más un raycast hacia abajo para descartar nodos que se han quedado sin suelo. ~200 comprobaciones una sola vez al arrancar. Esto es lo que hace que el grafo **se auto-repare** si el usuario mueve un edificio después de generar. El `CharacterController` del jugador (altura 0.72, centro 0.36) no llega a 1 m, así que no poda nada por accidente. Limitación a documentar: un prefab de edificio sin collider no se detecta.
3. **Re-bake explícito** — `[ContextMenu]` en el componente + menú item, para recalcular contra la escena actual sin regenerar la ciudad. Es el gesto equivalente al Bake del NavMesh.

Un nodo bloqueado no se borra: lleva flag `Blocked` y el BFS lo salta, así no hay que reconstruir aristas. Si el podado deja a un agente sin ruta, este salta al nodo válido más cercano y replanifica.

## Determinismo

Todo el spawn (elección de prefab, nodo, jitter) usa el `System.Random` que `Assemble` ya pasa por parámetro, nunca `UnityEngine.Random`. El comportamiento en runtime (destinos, paradas) usa `UnityEngine.Random`, igual que `CarAgent` — misma convención ya documentada.

## Verificación (al implementar)

1. **Compilar**: crear los `.cs` y forzar `CompilationPipeline.RequestScriptCompilation()`, esperar al domain reload y comprobar `Unity_ReadConsole` — los tipos nuevos no resuelven hasta entonces.
2. **Generar** una ciudad 5×5 con `includePedestrians` y ~60 NPCs, y regenerar `Assets/Scenes/City.unity`.
3. **En el editor, sin Play**: seleccionar el `PedestrianNetwork` y verificar con los gizmos que el anillo cae entre edificios y mobiliario, que los cruces se enganchan a las cebras, y que no hay nodos huérfanos.
4. **En Play**, comprobar:
   - Ningún NPC pisa calzada fuera de una cebra.
   - Los NPCs esperan en el bordillo y cruzan solo con el tráfico de esa calle en rojo.
   - Se producen paradas ociosas y paradas junto a bancos/fuente.
   - No se atraviesan entre ellos (separación local activa).
   - Las rutas se renuevan al llegar a destino, sin NPCs parados para siempre.
5. **Animación** — punto explícito de QA: verificar que no hay *foot sliding*. Las velocidades por defecto se alinean con `PlayerWalkSpeed` 4 / `PlayerRunSpeed` 8 precisamente para que el blend tree de `CharacterAnimator.controller` cuadre sin tocarlo; si se ve demasiado rápido, hay que bajar velocidad **y** mapeo a la vez.
6. **Robustez**: con la escena generada, mover a mano un edificio hasta que invada el anillo, entrar en Play y confirmar que los nodos afectados se podan y los NPCs los rodean. Repetir con el botón de re-bake sin entrar en Play.
7. **Rendimiento**: perfilar con `Unity_Profiler_*` a 60 y a 150 NPCs; confirmar que el `cullingMode = CullCompletely` recorta el coste de Animator fuera de cámara y que `PedestrianManager.Update` se mantiene plano.
8. **Casos límite**: rejilla 1×3 (manzanas aisladas, debe avisar y no romperse), `pedestrianCount` 0, lista de peatones vacía con count > 0 (debe bloquear el validator), e `includeTraffic` desactivado (los semáforos siguen generándose, así que los peatones deben seguir esperando correctamente).
