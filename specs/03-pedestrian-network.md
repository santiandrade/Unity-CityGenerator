# SPEC 03 — Red peatonal (NPCs) para City Generator

> **Estado:** Approved
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 02 (Unity Package Distribution)
> **Fecha:** 2026-08-24
> **Objetivo:** Añadir una red peatonal autónoma —NPCs que recorren aceras y cruzan por pasos de cebra semaforizados, con los vehículos frenando ante ellos— a la herramienta City Generator, espejando el sistema de tráfico rodado ya existente.

## Por qué existe esta spec

Hoy la herramienta genera tráfico rodado autónomo (`TrafficNetwork` + `CarAgent` + `TrafficManager`) pero la ciudad está vacía de personas: los 12 prefabs de `DefaultAssets/Prefabs/Characters/` solo se usan como candidatos a Player Prefab. Esta spec añade una segunda red, la peatonal, con NPCs que recorren rutas A→B por la ciudad, hacen paradas y respetan los semáforos — configurable desde la herramienta igual que ya lo son los vehículos ("Include Pedestrians", "Pedestrian Count", lista de prefabs con porcentajes).

Restricciones fijadas de antemano: los peatones solo pisan acera y pasos de cebra, y cruzan cuando el tráfico de esa calle tiene rojo. Tras evaluar grafo propio vs NavMesh vs híbrida, se descarta NavMesh: sobre esta geometría (acera y calzada separadas por 18 cm y contiguas) un bake no distingue acera de calzada, así que exigiría pintar áreas en generación + un `NavMeshLink` por brazo de cebra + parar igualmente al agente en el bordillo mirando el semáforo — es decir, reimplementar el grafo **y además** cargar con `com.unity.ai.navigation` en `package.json`, el bake y su peso en escena. La híbrida arrastra el mismo coste sin ahorrar nada. Coherente con el apartado F del technical review, que ya descartó ECS/DOTS con este mismo criterio.

Un hallazgo simplifica el diseño: `CityGeneratorGroundBuilder.BuildZebraCrossings` y `CityGeneratorTrafficBuilder.BuildTrafficLights` recorren exactamente el mismo rango (`for i = 1; i < gridWidth`, `for j = 1; j < gridHeight`). Por tanto cebra ⟺ semáforo: no existe ningún paso de peatones sin semáforo, y el caso "cruce no señalizado" —que en los coches obligó a toda la maquinaria de reservas y deadlocks— no aparece aquí. Corolario: con `gridWidth == 1` o `gridHeight == 1` no hay intersecciones interiores, luego no hay cebras ni cruces, y cada manzana queda aislada (los NPCs dan vueltas a su manzana). Degradación aceptable, avisada en la UI.

## Scope

**Dentro:**

- **`Runtime/PedestrianNetwork.cs`** — grafo propio no dirigido, espejo estructural de `TrafficNetwork`: `SetAxes()`/`Build()` públicos, `Awake` llama a `Build()`, `EnsureBuilt()` en accesores. Nodos `Ring` / `Curb` / `Crossing` / `PointOfInterest`, `FindPath` (BFS con arrays preasignados, sin alloc), `PickRandomDestination`, `CanCross` (delega en `TrafficNetwork`), `PrunePlacedObstacles()`, `[ContextMenu("Rebuild Network")]` + gizmos.
- **`Runtime/PedestrianAgent.cs`** — movimiento por transform (sin `CharacterController`/`Rigidbody`), estados `Walking`/`WaitingToCross`/`Idling` (`Interacting` reservado, no implementado), sigue rutas de `FindPath`, espera en `Curb` hasta `CanCross`, paradas ociosas y en `PointOfInterest`, animación con el mismo mapeo de `Speed`/`Grounded` que `PlayerController`, `Tick(dt, runLogic)` llamado por el manager, jitter lateral y de velocidad por instancia.
- **`Runtime/PedestrianManager.cs`** — espejo de `TrafficManager`: `Register`/`Unregister`, `Update` único escalonado por distancia a cámara, grid espacial ~8 m para separación local tipo boids (y base de interacción futura).
- **Vehículos frenan ante peatones**: `Runtime/CarAgent.cs` gana un `pedestrianMask` propio (independiente de `vehicleMask`) y un segundo `SphereCastNonAlloc` filtrado por él; un hit se trata igual que "coche por delante" (misma lógica de frenado progresivo, sin nuevo `CurrentStopReason` ni máquina de estados nueva).
- **Layer `Pedestrian` propio**, creado igual que `EnsureVehicleLayerExists` (mismo mecanismo sobre `ProjectSettings/TagManager.asset`, mismo fallback fail-closed si no hay slot libre: `pedestrianMask` queda en `0` y se avisa por consola, sin bloquear la generación).
- **Collider en runtime, no en el prefab de usuario**: `CityGeneratorPedestrianBuilder` añade a cada instancia generada un `BoxCollider` `isTrigger = true` (dimensionado desde `CityGeneratorBoundsUtility.GetWorldBounds`) en el layer `Pedestrian`. `isTrigger` es deliberado: lo detecta el `SphereCastNonAlloc` de `CarAgent` pero no genera colisión física con el `CharacterController` del jugador ni con nada más.
- **`Runtime/TrafficNetwork.cs`** — un accesor público que exponga el estado de la luz de una intersección para un eje dado, para que `PedestrianNetwork` no vuelva a escanear la escena.
- **`Editor/CityGeneratorPedestrianBuilder.cs`** — espejo de `CityGeneratorTrafficBuilder`: `AddNetworkComponent`, `AddManagerComponent`, `BuildPedestrians` (reparto por porcentaje, spawn en nodos `Ring`, `Animator.cullingMode = CullCompletely`), `EnsurePedestrianLayerExists`, asignación de `pedestrianMask` a los `CarAgent` ya colocados en escena (vía `SerializedObject`, corre después de `CityGeneratorTrafficBuilder`), podado en generación contra la lista `obstacles`, `RegisterPointsOfInterest` (bancos/fuente de plaza).
- **`Editor/CityGeneratorDistributionUtility.cs`** — extracción de `DistributePercentages` a firma reutilizable; `CityGeneratorTrafficBuilder` pasa a usarla.
- **`Editor/CityGeneratorSettings.cs`** — `includePedestrians`/`pedestrianCount` en `GeneralSettings`, `List<PedestrianEntry> pedestrians`, tipo `PedestrianEntry { prefab, percentage }` propio.
- **`Editor/CityGeneratorContentAssembler.cs`** — grupos `Pedestrians`/`PedestrianNetwork` (red construida solo si `includePedestrians`), excluidos de `MarkStatic`, orden de pipeline tras `TrafficBuilder`.
- **`Editor/CityGeneratorWindow.cs`** — sección "Pedestrians", `HelpBox` de rendimiento y de rejilla 1×N/N×1, menú `Tools > City Generator > Rebuild Pedestrian Network`.
- **`Editor/CityGeneratorValidator.cs`** — bloque espejo del de vehículos (prefabs no nulos, porcentajes suman 100); sin bloqueo por falta de `Animator`.
- **`Editor/CityGeneratorConstants.cs`** — constantes nuevas de geometría/tuning peatonal, cada una comentada (sin fijar sus valores numéricos en la spec, ver Decisiones).
- **`Editor/CityGeneratorDefaultAssets.cs`** / **`CityGeneratorDefaultAssetsWriter.cs`** — los 12 characters como peatones por defecto (~8.33 % repartido); `AppendPedestriansList` + `ReplaceField` para los campos nuevos.
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`), `README.md`/`README.es.md` — ajustes nuevos, podado auto-reparador y su dependencia de que los edificios tengan collider, limitación de rejillas 1×N.

**Fuera de alcance (para futuras specs):**

- Interacción peatón-peatón más allá de la separación local tipo boids (conversación, grupos, reacción al jugador).
- Colisión física peatón-jugador: el jugador atraviesa a los NPCs sin bloqueo (solo se añade colisión física en el sentido peatón→vehículo, vía el `pedestrianMask` de `CarAgent`).
- Cruces no semaforizados para peatones: no existen en la geometría actual (cebra ⟺ semáforo), así que no se diseña esa maquinaria.
- Publicación de una nueva versión del package (bump de `version`/tag): esta spec entrega el código; el release es un paso posterior con `Tools > City Generator > Release`, fuera de esta spec.
- Fijar los valores numéricos finales de las constantes de tuning (velocidades, umbral de aviso, probabilidad/duración de parada, jitter): se acotan por criterio (alinear con `PlayerWalkSpeed`/`PlayerRunSpeed`, proporcional a `VehicleDensityWarningThreshold`) pero se cierran durante `/spec-impl` con QA de animación.
- Cualquier rediseño de `CarAgent`/`TrafficNetwork` más allá de: el accesor de luz nuevo y el `pedestrianMask` + segundo sensor. Las cuatro reglas ya documentadas de reservas/deadlock en cruces no señalizados no se tocan.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs
[Serializable]
public struct PedestrianEntry
{
    public GameObject prefab;
    [Range(0, 100)] public float percentage;
}

// GeneralSettings
public bool includePedestrians = true;
public int pedestrianCount = 40;

// CityGeneratorSettings
public List<PedestrianEntry> pedestrians = new();
```

```csharp
// Runtime/PedestrianNetwork.cs
public enum PedestrianNodeKind { Ring, Curb, Crossing, PointOfInterest }

public struct PedestrianNode
{
    public Vector3 Position;
    public PedestrianNodeKind Kind;
    public TrafficLightIntersection Intersection; // solo Kind == Crossing
    public bool CrossingAxisIsX;                  // solo Kind == Crossing
    public Vector3? LookAt;                       // solo Kind == PointOfInterest
    public bool Blocked;
    public List<int> Neighbours;                  // grafo no dirigido
}
```

```csharp
// Runtime/CarAgent.cs — campo nuevo, paralelo a vehicleMask
[SerializeField] private LayerMask pedestrianMask;
```

```csharp
// Runtime/TrafficNetwork.cs — accesor nuevo
public bool IsAxisGreen(TrafficLightIntersection intersection, bool axisIsX);
```

Layer nuevo `Pedestrian` en `ProjectSettings/TagManager.asset`, creado por `CityGeneratorPedestrianBuilder.EnsurePedestrianLayerExists` con el mismo mecanismo que `EnsureVehicleLayerExists` (primer slot libre desde `CityGeneratorConstants.FirstUserLayerIndex`, distinto del que ya ocupa `Vehicle`).

Convenciones:

- `pedestrians` sigue la misma convención de reparto por porcentaje que `vehicles` (suman 100, `PercentageTolerance`).
- `PedestrianNode.Position.y` es 0.18 en acera (`GroundDatumY`) o `PedestrianRoadY` (0) en calzada; sin raycast, el agente interpola en Y con `MoveTowards`.
- El grafo es no dirigido (a diferencia de `TrafficNetwork`, que sí tiene sentido de circulación).

## Geometría del grafo

Números reales del proyecto (manzana 46 m, pitch 56 m, calle 10 m, `GroundDatumY` 0.18):

| Radio desde el centro de manzana | Qué hay |
|---|---|
| ~18 m | borde real de los edificios demo (slot 22 m, footprint máx. 13.9 m) |
| **19.5 m** | **anillo peatonal** (`PedestrianRingInset` 3.5) |
| 21 m | papeleras y árboles (`StreetEdgeInset` 2) |
| 22 m | farolas (`LampEdgeInset` 1) |
| 23 m | borde de manzana / bordillo |

El anillo cae en un hueco libre por construcción entre edificios y mobiliario urbano, razón por la que no hace falta evitación de obstáculos entre peatones y mobiliario.

**Nodos por manzana**: 4 esquinas del anillo + 1 punto medio por lado = 8. En manzanas de plaza, además nodos interiores: 4 radiales desde las esquinas del anillo hacia los 4 bancos (`PlazaBenchRadius`, ya existente) y un anillo corto alrededor del centerpiece.

**Nodos de cruce**: el brazo de cebra está a `ZebraArmOffset` (7.6 m) del centro de intersección. En coordenadas de manzana, la cadena es esquina del anillo `(19.5, 19.5)` → bordillo `(20.4, 23)` → mitad de calzada `(20.4, 28)` → bordillo opuesto `(20.4, 33)` → esquina del anillo de la manzana contigua.

**Semáforo aplicable**: un peatón que cruza en dirección Z atraviesa el flujo de dirección ±X, luego debe cruzar cuando la luz de ese eje está en rojo. `TrafficLightIntersection` ya alterna `eastWest` / `northSouth`, así que basta consultar `IsAxisGreen` de una de las dos direcciones de ese eje.

## El podado en tres niveles

1. **En generación** — contra la lista `obstacles`, vía `CityGeneratorBoundsUtility`. Detecta el prefab de usuario que invade el anillo antes de que se vea mal.
2. **En `Awake`** — `PrunePlacedObstacles()` lanza un `Physics.CheckSphere` por nodo a 1 m de altura (por encima de la acera, así que el suelo no cuenta y no hace falta ninguna capa nueva) más un raycast hacia abajo para descartar nodos que se han quedado sin suelo. Esto hace que el grafo se auto-repare si el usuario mueve un edificio después de generar. El `CharacterController` del jugador (altura 0.72, centro 0.36) no llega a 1 m, así que no poda nada por accidente. Limitación a documentar: un prefab de edificio sin collider no se detecta.
3. **Re-bake explícito** — `[ContextMenu]` en el componente + menú item, para recalcular contra la escena actual sin regenerar la ciudad.

Un nodo bloqueado no se borra: lleva flag `Blocked` y el BFS lo salta, así no hay que reconstruir aristas. Si el podado deja a un agente sin ruta, este salta al nodo válido más cercano y replanifica.

## Determinismo

Todo el spawn (elección de prefab, nodo, jitter) usa el `System.Random` que `Assemble` ya pasa por parámetro, nunca `UnityEngine.Random`. El comportamiento en runtime (destinos, paradas) usa `UnityEngine.Random`, igual que `CarAgent` — misma convención ya documentada.

## Implementation plan

1. **`CityGeneratorConstants.cs`** — añadir las constantes de geometría/tuning peatonal (`PedestrianRingInset`, `PedestrianRoadY`, `PedestrianLaneJitter`, `PedestrianCountWarningThreshold`, velocidades, probabilidad/duración de parada), cada una con su comentario del *porqué*. No cambia comportamiento existente.
2. **`TrafficNetwork.cs`** — añadir `IsAxisGreen(TrafficLightIntersection, bool axisIsX)` público. Manual test: llamarlo desde el log de una escena ya generada con tráfico y confirmar que refleja el estado real de la luz.
3. **`Runtime/PedestrianNetwork.cs`** — esqueleto compilable: nodos, `SetAxes`/`Build`, `FindPath`, `PickRandomDestination`, `CanCross`, `PrunePlacedObstacles`, gizmos. Sin nada que lo instancie todavía.
4. **`Runtime/PedestrianAgent.cs`** — máquina de estados y `Tick(dt, runLogic)`, consumiendo `PedestrianNetwork`. Compilable, sin nada que lo instancie.
5. **`Runtime/PedestrianManager.cs`** — `Register`/`Unregister`, `Update` escalonado, grid espacial de separación local.
6. **`CarAgent.cs`** — añadir `pedestrianMask` y el segundo `SphereCastNonAlloc`, integrado en la misma rama de frenado que "coche por delante". Manual test: en una escena con tráfico ya generado, colocar a mano un collider de prueba en un layer temporal, apuntar `pedestrianMask` a él y confirmar en Play que el coche frena.
7. **`Editor/CityGeneratorDistributionUtility.cs`** — extraer `DistributePercentages`; `CityGeneratorTrafficBuilder` pasa a usarla. Manual test: regenerar una ciudad con tráfico y confirmar que el reparto de vehículos no cambia.
8. **`Editor/CityGeneratorSettings.cs`** — `includePedestrians`, `pedestrianCount`, `pedestrians`, `PedestrianEntry`. Compila; la ventana aún no los muestra.
9. **`Editor/CityGeneratorPedestrianBuilder.cs`** — `EnsurePedestrianLayerExists`, `AddNetworkComponent`, `AddManagerComponent`, `BuildPedestrians` (spawn, `BoxCollider` trigger, `Animator.cullingMode`), asignación de `pedestrianMask` a los `CarAgent` ya colocados, podado contra `obstacles`, `RegisterPointsOfInterest`. Aún no llamado desde el pipeline.
10. **`Editor/CityGeneratorContentAssembler.cs`** — grupos `Pedestrians`/`PedestrianNetwork`, llamada a `CityGeneratorPedestrianBuilder` tras `TrafficBuilder`, exclusión de `MarkStatic`, entradas en `CityBuildSummary`. Manual test: generar una ciudad 5×5 con `includePedestrians` y ver NPCs caminando en Play.
11. **`Editor/CityGeneratorValidator.cs`** — bloque espejo del de vehículos.
12. **`Editor/CityGeneratorWindow.cs`** — sección "Pedestrians", `HelpBox`s de rendimiento y de rejilla 1×N, menú `Rebuild Pedestrian Network`.
13. **`Editor/CityGeneratorDefaultAssets.cs`** — los 12 characters como peatones por defecto. Manual test: abrir la ventana en un proyecto limpio y comprobar que la lista de peatones ya viene rellena.
14. **`Editor/CityGeneratorDefaultAssetsWriter.cs`** — `AppendPedestriansList` + `ReplaceField` para los campos nuevos. Manual test: `Set Current Selection As Default` tras tocar la lista de peatones y confirmar que `CityGeneratorDefaultAssets.cs` se regenera correctamente.
15. **`CHANGELOG.md` / `README.md` / `README.es.md`** — documentar la feature, el podado auto-reparador, la dependencia de collider en edificios, y la limitación de rejillas 1×N.

## Acceptance criteria

- [ ] Los `.cs` nuevos compilan tras `CompilationPipeline.RequestScriptCompilation()` y los tipos resuelven en `Unity_ReadConsole` sin errores.
- [ ] Generar una ciudad 5×5 con `includePedestrians` activo y ~60 NPCs completa sin errores y regenera `Assets/Scenes/City.unity`.
- [ ] Con el `PedestrianNetwork` seleccionado y sin Play, los gizmos muestran el anillo cayendo entre edificios y mobiliario, los cruces enganchados a las cebras, y ningún nodo huérfano.
- [ ] En Play, ningún NPC pisa calzada fuera de una cebra.
- [ ] En Play, los NPCs esperan en el bordillo y solo cruzan cuando el tráfico de esa calle está en rojo (verificable vía `IsAxisGreen`).
- [ ] Se producen paradas ociosas y paradas junto a bancos/fuente en una manzana de plaza.
- [ ] Los NPCs no se atraviesan entre sí (separación local del grid espacial activa).
- [ ] Al llegar a destino, un NPC replanifica y camina hacia un nuevo destino sin quedarse parado indefinidamente.
- [ ] Las velocidades por defecto no producen *foot sliding* visible con `CharacterAnimator.controller` sin modificarlo.
- [ ] Un `CarAgent` que detecta un peatón en su sensor (`pedestrianMask`) frena/para igual que ante un coche por delante, sin nuevo `CurrentStopReason`.
- [ ] El layer `Pedestrian` se crea automáticamente la primera vez (mismo mecanismo que `Vehicle`); si no hay slot libre, `pedestrianMask` queda en `0` y se registra un aviso en consola, sin bloquear la generación.
- [ ] El `BoxCollider` de cada peatón generado es `isTrigger = true`: el jugador (`CharacterController`) atraviesa a los NPCs sin colisión física, y el `SphereCastNonAlloc` de `CarAgent` sigue detectándolos.
- [ ] Moviendo a mano un edificio hasta invadir el anillo y entrando en Play, los nodos afectados se podan (`PrunePlacedObstacles`) y los NPCs los rodean; repitiendo con el botón/menú de re-bake sin entrar en Play se obtiene el mismo resultado.
- [ ] Perfilando a 60 y 150 NPCs, `Animator.cullingMode = CullCompletely` recorta el coste fuera de cámara y `PedestrianManager.Update` se mantiene plano.
- [ ] Con rejilla 1×3, la ventana avisa de manzanas aisladas y la generación no se rompe.
- [ ] `pedestrianCount` a `0` genera la ciudad sin peatones y sin errores.
- [ ] Una lista de peatones vacía con `pedestrianCount > 0` bloquea la generación en `CityGeneratorValidator`.
- [ ] Con `includeTraffic` desactivado, los semáforos se siguen generando y los peatones siguen esperando/cruzando correctamente.

## Decisiones tomadas y descartadas

- **Sí:** grafo propio `PedestrianNetwork`, espejo de `TrafficNetwork`. NavMesh descartado: sobre esta geometría (acera/calzada separadas 18 cm y contiguas) el bake no distingue una de otra, exigiría pintar áreas + un `NavMeshLink` por brazo de cebra + parar igual al agente en el bordillo mirando el semáforo — es decir, reimplementar el grafo **y además** cargar con `com.unity.ai.navigation`, el bake y su peso en escena. La híbrida arrastra el mismo coste sin ahorrar nada.
- **No:** ECS/DOTS para el movimiento de NPCs. Mismo criterio que el technical review (apartado F) ya aplicó al tráfico rodado.
- **Sí:** podado en tres niveles (generación, `Awake` auto-reparador, re-bake explícito). Cubre el ciclo de vida completo: prefab de usuario que invade el anillo al generar, edición manual posterior de la escena, y un gesto explícito equivalente al Bake de NavMesh.
- **Sí:** una spec única para todo (runtime + editor + UI + defaults), no dividida. Coherente con cómo se implementó el tráfico rodado en SPEC 01; el conjunto es una sola pieza cerrada y dividirla generaría PRs a medio terminar sin valor independiente.
- **Sí:** los vehículos frenan ante peatones detectados por `CarAgent`, vía un `pedestrianMask` y un segundo `SphereCastNonAlloc` independientes de `vehicleMask`. Mantiene intacta la lógica ya delicada de detección coche-coche (con su historial de deadlock documentado) y añade la de peatones como una rama adicional, no una reescritura.
- **No:** fusionar peatones en el mismo `Vehicle` layer/mask. Mezclaría semánticas y complicaría el `borderPenalty`/`interiorBias` ya afinados para el reparto de tráfico perimetral/interior.
- **No:** un `CurrentStopReason` ni una máquina de estados nuevos para "peatón detectado". Se reutiliza la rama de frenado progresivo de "coche por delante": el peatón es un obstáculo más en el sensor, no un caso especial.
- **Sí:** `BoxCollider` añadido en runtime por `CityGeneratorPedestrianBuilder` a cada instancia generada, nunca al prefab de usuario en `DefaultAssets/`. Igual que con `CarAgent`, no se exige al usuario preparar su propio prefab.
- **Sí:** ese `BoxCollider` es `isTrigger = true`. Resuelve a la vez las dos restricciones confirmadas: lo detecta el `SphereCastNonAlloc` de `CarAgent` (que por defecto sí ve triggers), pero no genera colisión física contra el `CharacterController` del jugador ni contra nada más, ya que ni peatones ni jugador llevan `Rigidbody`.
- **Sí:** layer `Pedestrian` propio, creado con el mismo mecanismo fail-closed que `EnsureVehicleLayerExists` (primer slot libre desde `FirstUserLayerIndex`; sin slot libre, `pedestrianMask` queda en `0` y se avisa una vez, sin bloquear la generación). Coherente con la preferencia ya registrada de fallar limpio antes que con un fallback confuso.
- **No:** fijar en esta spec los valores numéricos exactos de las constantes de tuning nuevas (velocidades, `PedestrianCountWarningThreshold`, probabilidad/duración de parada, jitter). Se acotan por criterio (alinear con `PlayerWalkSpeed`/`PlayerRunSpeed`, proporcional a `VehicleDensityWarningThreshold`) y se cierran durante `/spec-impl` con la QA de animación, igual que ya se hizo con el tuning de `CarAgent` por prefab.
- **No:** cruces peatonales no semaforizados. La geometría actual garantiza cebra ⟺ semáforo (mismo rango de iteración en `BuildZebraCrossings` y `BuildTrafficLights`), así que el caso no existe y no se diseña la maquinaria de reservas que sí hizo falta para coches.
- **No:** interacción peatón-peatón más allá de separación local tipo boids, ni reacción de los NPCs al jugador. Queda para una spec futura; el grid espacial de `PedestrianManager` es la base para ella pero no se implementa aquí.
- **No:** tipo `PedestrianEntry` reutilizando `VehicleEntry`. Renombrar `VehicleEntry` obligaría a tocar el generador de código de `CityGeneratorDefaultAssetsWriter` para ganar dos campos con el mismo shape; un tipo propio es más barato.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| `SphereCastNonAlloc` no detecta triggers si el proyecto tiene `Physics.queriesHitTriggers = false` en algún punto de configuración | `CarAgent` fija explícitamente `QueryTriggerInteraction.Collide` en la llamada del sensor de peatones, sin depender del ajuste global de físicas. |
| No quedan slots de layer libres (8–31 ya ocupados) cuando se crea `Pedestrian`, además de `Vehicle` | Mismo fallback fail-closed ya validado para `Vehicle`: `pedestrianMask` queda en `0`, se avisa una vez por consola, y tanto la generación como el resto de la simulación siguen funcionando (los coches simplemente no ven peatones). |
| Añadir un segundo `SphereCastNonAlloc` por `CarAgent` duplica el coste del sensor de tráfico, ya identificado como sensible en rendimiento (`TrafficManager.staggerFrames`) | El escalonado existente de `TrafficManager` ya reduce la frecuencia de sensor en coches lejanos a cámara; se perfila explícitamente el impacto del sensor doble en el paso de verificación de rendimiento (60/150 NPCs) antes de dar la spec por cerrada en implementación. |
| Un `BoxCollider` trigger mal dimensionado (por un prefab de peatón con jerarquía de renderers atípica) deja huecos donde `CarAgent` no detecta al NPC | Se dimensiona con `CityGeneratorBoundsUtility.GetWorldBounds`, el mismo mecanismo ya usado y validado para el resto de overlap-avoidance de la herramienta; un prefab de usuario con renderers ocultos/desactivados queda fuera, igual que ya ocurre con el resto de bounds del proyecto. |
| Un peatón podado (`Blocked`) deja a un `CarAgent` con su `pedestrianMask` apuntando a una instancia que ya no se mueve, pareciendo un atasco fantasma | Fuera del alcance de esta spec resolverlo de forma especial: el `CarAgent` ya frena/espera ante cualquier obstáculo detectado igual que ante un coche parado; si el peatón nunca se mueve, el comportamiento visible es el mismo que un vehículo averiado, aceptado como caso límite documentado en el README junto con la limitación de rejillas 1×N. |

## Lo que **no** entra en esta spec

- Interacción peatón-peatón más allá de separación local tipo boids, ni reacción de los NPCs al jugador.
- Colisión física peatón-jugador (el jugador atraviesa a los NPCs sin bloqueo).
- Cruces peatonales no semaforizados.
- Publicación de una nueva versión del package (bump de `version`/tag, GitHub Release).
- Valores numéricos finales de las constantes de tuning peatonal.
- Cualquier cambio en `CarAgent`/`TrafficNetwork` más allá del accesor de luz y el `pedestrianMask` + segundo sensor; las cuatro reglas de reservas/deadlock en cruces no señalizados no se tocan.

Cada uno de estos puntos, si se aborda, va en su propia spec futura.
