# SPEC 17 — Físicas de vehículos

> **Estado:** Implementado
> **Depende de:** SPEC 01 (la tool original y el `CarAgent` cinemático que esta spec pasa a modo físico), SPEC 04 (managers no-singleton: el modo de tick se decide por `TrafficManager`, no globalmente), SPEC 05 (el staggering del sensor de `TrafficManager`, cuya cadencia esta spec traslada a pasos de física), SPEC 16 (varias ciudades: cada `TrafficManager` elige su modo de forma independiente)
> **Fecha:** 2026-09-09
> **Objetivo:** Añadir un parámetro `Enable Physics` (OFF por defecto) a la card Traffic que, al activarse, da a cada vehículo generado un `Rigidbody` dinámico y un physic material configurables desde la tool, de modo que responda de forma realista a los impactos de otros vehículos, de cualquier Rigidbody externo de la escena y de la geometría estática, y sea capaz de recuperarse y volver a su ruta después — dejando el comportamiento con `Enable Physics` OFF exactamente igual que hasta ahora.

## Scope

**Dentro:**

- **Nuevo parámetro `Enable Physics`** en la card Traffic (`general.enablePhysics`, `bool`, OFF por defecto), justo debajo de `Enabled`/`Vehicle Count`. Con OFF, la generación y el runtime son bit a bit idénticos a hoy: ningún `Rigidbody`, ningún physic material, `TrafficManager` sigue en `Update` con su `Physics.SyncTransforms()` actual.
- **Cuatro campos nuevos, visibles solo con `Enable Physics` ON:** `Mass`, `Drag`, `Angular Drag` (floats) y `Physic Material` (referencia a `PhysicMaterial`, opcional). Únicos para todos los vehículos generados, igual que el resto del tuning de tráfico (`maxSpeed`, `sensorRange`, etc.).
- **`CityGeneratorTrafficBuilder.BuildVehicles`** añade un `Rigidbody` no-kinemático a la instancia (en el mismo GameObject que `CarAgent`, junto al collider proxy que ya gestiona `CityGeneratorColliderUtility`) solo cuando `enablePhysics` está ON, configurado con `mass`/`drag`/`angularDrag` de los settings y `Interpolation = Interpolate`, `Collision Detection = ContinuousDynamic`, `Constraints = FreezeRotationX | FreezeRotationZ` fijos por código (no expuestos). Asigna el `Physic Material` al collider proxy si el campo no está vacío.
- **`CarAgent` gana un segundo modo de conducción**, seleccionado por la presencia de un `Rigidbody` no-kinemático en su propio GameObject (detectado una vez en `OnEnable`/`Start`, no leído de un flag de settings — el componente no conoce `CityGeneratorSettings`):
  - **Sin Rigidbody (hoy):** conducción cinemática sin cambios, tick en `Update` vía `TrafficManager`.
  - **Con Rigidbody:** mientras conduce (estado `Driving`), `useGravity = false`, constraint `FreezePositionY` añadida a las de rotación, y el tick pasa a `FixedUpdate`: en vez de escribir `transform.SetPositionAndRotation`, calcula la misma velocidad/rotación objetivo de hoy y las aplica como `rb.linearVelocity`/`rb.MoveRotation`.
  - **Al recibir un impacto que supera el umbral** (`OnCollisionEnter`, impulso de colisión ≥ `CityGeneratorConstants.VehicleImpactImpulseThreshold`), entra en estado `Recovering`: libera `useGravity`, quita `FreezePositionY`, suspende toda la lógica de conducción (semáforos, sensor, prioridad de cruce) y libera su reserva de cruce y su segmento de `LaneOccupancy` exactamente como ya hace `ReleaseReservationWhileBlocked`/`AdvanceToNextNode.Leave`. La física del motor gobierna el Rigidbody libremente durante este estado (puede recibir más impactos, de otro vehículo, de un Rigidbody externo dinámico, o rebotar contra geometría estática).
  - **Sale de `Recovering`** cuando la velocidad angular y la desviación respecto al plano horizontal caen bajo un umbral de calma (con un mínimo y un máximo de tiempo de seguridad, todos constantes). Al salir: se endereza progresivamente (rotación upright, componente Y del rumbo a 0) y llama a `network.FindNodeAhead(transform.position, heading actual)`, el mismo método que ya usa en `Start`, para reengancharse a la ruta desde donde quedó. Reintenta un número acotado de veces (separadas en el tiempo, el coche puede seguir asentándose) antes de rendirse; si todas fallan, `enabled = false` — el mismo fallback que ya usa `AdvanceToNextNode` ante un dead-end de Custom Grid. Al reenganchar con éxito, vuelve a `useGravity = false` + `FreezePositionY` y a estado `Driving`.
- **`TrafficManager` gana un tick de física** (`FixedUpdate`), activo únicamente si al menos uno de sus agentes registrados corre en modo Rigidbody (detectado por agente, no por un flag propio del manager — coherente con SPEC 16, donde una escena puede tener una ciudad con físicas y otra sin ellas bajo el mismo `TrafficManager` si ambas comparten manager, o managers distintos si no). El `staggerMinAgentCount`/`staggerDistance`/`staggerFrames` existentes se reutilizan sin cambiar de valor, contados ahora en pasos de física para los agentes en modo Rigidbody; los agentes en modo cinemático del mismo manager se siguen ticando en `Update` exactamente como hoy. `Physics.SyncTransforms()` deja de llamarse para los agentes en modo Rigidbody (ya no hace falta: el motor conoce sus posiciones) pero se mantiene para cualquier agente cinemático restante.
- **Nuevo `PhysicMaterial` en `DefaultAssets`** (`DefaultAssets/Physics/Vehicle.physicsmaterial`, fricción media, rebote bajo) y `CityGeneratorDefaultAssets.ApplyTo` lo asigna por defecto al nuevo campo `Physic Material`. Opcional: si el usuario lo vacía, el collider proxy se queda sin material (default de Unity), sin bloquear la generación.
- **Constantes nuevas en `CityGeneratorConstants`**: umbral de impulso de impacto, umbral de calma (velocidad angular + desviación de plano), tiempo mínimo/máximo de `Recovering`, número de reintentos de reenganche y su separación temporal.
- **Tests EditMode** (`Assets/Tests/EditMode/Generation/`): con `enablePhysics` OFF, la instancia generada no tiene `Rigidbody` y es indistinguible de antes de esta spec; con ON, tiene un `Rigidbody` no-kinemático con `mass`/`drag`/`angularDrag` de los settings, las constraints/interpolation/collision-detection fijas correctas, y el `Physic Material` asignado al collider proxy cuando el campo no está vacío.
- **Tests PlayMode** (`Assets/Tests/PlayMode/` o carpeta equivalente): un `CarAgent` sintético en modo Rigidbody, golpeado por otro Rigidbody con impulso por encima del umbral, entra en `Recovering`, libera su reserva de cruce y su segmento de `LaneOccupancy`; tras calmarse, vuelve a `Driving` reenganchado a un nodo válido de la red. Un impacto por debajo del umbral no dispara `Recovering` (cubre el caso de una cola de coches tocándose en un semáforo).
- **Documentación**: `docs/user-manual.md`/`.es.md` (el nuevo parámetro y los cuatro campos, qué activa y qué no); `docs/architecture/runtime-and-traffic.md` (el segundo modo de `CarAgent`, el tick de física de `TrafficManager`, la máquina de estados `Driving`/`Recovering`); `docs/architecture/editor-tool.md` (los campos nuevos de la card Traffic, el `PhysicMaterial` de `DefaultAssets`); `CLAUDE.md` (nuevo invariante: con `Enable Physics` OFF el comportamiento es idéntico al de antes de esta spec; el modo de un `CarAgent` se detecta por la presencia de un Rigidbody no-kinemático propio, nunca leído de settings); `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- **Colisión con peatones o con el player.** Un peatón no tiene Rigidbody hoy (`PedestrianAgent` es puramente cinemático, como documenta `docs/architecture/pedestrians.md`) y el player usa `CharacterController`, que no genera `OnCollisionEnter` de la forma que este sistema necesita. El coche los sigue evitando exactamente igual que hoy: frenando por el sensor frontal (`PedestrianAheadClearance`), sin ninguna respuesta física nueva en ninguno de los dos sentidos.
- **Masa por tipo de vehículo.** `Mass`/`Drag`/`Angular Drag` son únicos para las 15 instancias generadas, no una columna nueva en `VehicleEntry`.
- **Feedback de impacto** (sonido, partículas, daño visual/deformación de malla). Ninguno se añade; el impacto es puramente físico y silencioso.
- **Un menú `Tools > City Generator > Apply Vehicle Physics`** para añadir/quitar físicas a una ciudad ya presente en la escena sin regenerar. `Enable Physics` es un ajuste de generación como cualquier otro: se cambia y se hace Build o Re-Build.
- **Vehículos que aplican fuerzas/torque propios (motor físico real).** El modo Rigidbody sigue imponiendo velocidad/rotación mientras conduce (`Driving`); solo `Recovering` deja el Rigidbody libre. Un CarAgent con motor por fuerzas es un rediseño completo de la conducción, fuera de esta spec.
- **CI en batchmode.** Sigue siendo el otro P0 pendiente ajeno a esta spec.

## Modelo de datos

### `GeneralSettings` — nuevo campo y card asociada

```csharp
// Editor/CityGeneratorSettings.cs — dentro de GeneralSettings, junto a includeTraffic

[Tooltip("Give generated vehicles their own Rigidbody and physic material, so they respond realistically to impacts from other vehicles, external Rigidbodies in the scene, or static geometry, and recover back onto their route afterwards. Off leaves vehicle behaviour exactly as before this feature.")]
public bool enablePhysics = false;
```

```csharp
// Editor/CityGeneratorSettings.cs — nueva clase, campo `physics` en CityGeneratorSettings,
// visible solo con general.enablePhysics == true (misma convención que TrafficSettings/PropsSettings)

[Serializable]
internal class VehiclePhysicsSettings
{
    [Tooltip("Rigidbody mass (kg), shared by every generated vehicle instance.")]
    public float mass = 1200f;
    [Tooltip("Rigidbody linear drag.")]
    public float drag = 0.3f;
    [Tooltip("Rigidbody angular drag.")]
    public float angularDrag = 1f;
    [Tooltip("Physic material assigned to each vehicle's proxy collider. Optional -- left empty, the collider keeps Unity's default material.")]
    public PhysicMaterial physicMaterial;
}
```

`CityGeneratorSettings` gana `public VehiclePhysicsSettings vehiclePhysics = new();`, con el mismo patrón que `minimap`/`dayNight`/`audio`: un bloque propio, no campos sueltos en `GeneralSettings` — cuatro campos son ya suficiente para justificar su propia clase, y es coherente con cómo la tool separa "flags de generación" (`GeneralSettings`) de "tuning de un subsistema" (todo lo demás).

### `CarAgent` — estado de físicas

```csharp
// Runtime/CarAgent.cs — dentro de CarAgent

/// <summary>Driving state when the vehicle has a dynamic Rigidbody (Enable Physics on generation).
/// A vehicle with no Rigidbody never leaves an implicit "cinematic" mode and never reads this.</summary>
private enum PhysicsState
{
    /// <summary>Driving normally: CarAgent owns the Rigidbody's velocity/rotation every FixedUpdate,
    /// gravity off, Y position frozen -- the physical equivalent of today's y = 0f.</summary>
    Driving,
    /// <summary>An impact released the Rigidbody: gravity on, Y unfrozen, all driving logic
    /// suspended, engine physics free to move/rotate it until it settles.</summary>
    Recovering
}

private Rigidbody rb;              // null when Enable Physics was off at generation time
private PhysicsState physicsState;
private float recoveringSince;
private int reattachAttempts;
```

Notas:

- No hay ningún campo nuevo serializado por `SerializedObject` desde el builder aparte del propio `Rigidbody` (el builder configura el componente `Rigidbody`, no `CarAgent`): `CarAgent` detecta su modo leyendo `GetComponent<Rigidbody>()` en `OnEnable`, igual que ya lee `GetComponent<Collider>()`. Esto es deliberado — ver Decisiones.
- `PhysicsState`/`recoveringSince`/`reattachAttempts` son puro runtime, no se serializan ni se inspeccionan desde fuera; no hace falta un property público salvo que se quiera depurar (se puede añadir un getter de solo lectura si aporta al gizmo de debug, sin forzarlo aquí).

### `CityGeneratorConstants` — nuevas constantes

```csharp
// Editor/CityGeneratorConstants.cs (constantes leídas también desde Runtime, como el resto)

public const float VehicleImpactImpulseThreshold = 400f;   // kg·m/s, filtra el roce de una cola parada
public const float VehicleRecoveryAngularVelocitySettle = 0.5f; // rad/s
public const float VehicleRecoveryUprightDot = 0.98f;       // Vector3.Dot(transform.up, Vector3.up)
public const float VehicleRecoveryMinDuration = 0.5f;
public const float VehicleRecoveryMaxDuration = 8f;
public const int VehicleReattachMaxAttempts = 5;
public const float VehicleReattachRetryInterval = 0.5f;
```

Todas viven donde ya viven el resto de constantes de layout/tuning que `CLAUDE.md` exige no dejar inline, no en `CityGeneratorSettings` (esas son ajustables por el usuario; estas son mecanismo interno, igual que `TrafficLightCornerOffset` o `GroundDatumY`).

### `TrafficManager` — sin campos serializados nuevos

`staggerMinAgentCount`/`staggerDistance`/`staggerFrames` no cambian de tipo ni de valor por defecto. El manager distingue internamente, por agente, si corre en `Update` o en `FixedUpdate` consultando `agent.HasPhysics` (nuevo getter de solo lectura en `CarAgent`, `rb != null`) — no hay ningún flag nuevo en el propio `TrafficManager`.

## Plan de implementación

Cada paso deja el proyecto compilando y es comprobable por sí solo.

1. **Constantes nuevas.** Añadir a `CityGeneratorConstants` las siete constantes de físicas (umbral de impacto, umbrales de calma, duraciones de recuperación, reintentos). Sin efecto observable. Test manual: compila, ningún comportamiento cambia.

2. **`Enable Physics` y `VehiclePhysicsSettings` en el modelo de settings.** `GeneralSettings.enablePhysics` (OFF por defecto) y la nueva clase `VehiclePhysicsSettings` con su campo `vehiclePhysics` en `CityGeneratorSettings`. Sin UI todavía — el campo existe pero no se puede editar desde la ventana. Test manual: compila, `CityGeneratorSettingsAsset`/save-load de settings (si existe) no rompe con el campo nuevo a sus valores por defecto.

3. **UI de la card Traffic.** `BuildTrafficCard` añade el toggle `Enable Physics` y, condicionados a él (mismo patrón que otros campos condicionales de la ventana, p. ej. `Include Traffic` → resto de la card), los cuatro campos `Mass`/`Drag`/`Angular Drag`/`Physic Material`. Test manual: abrir la ventana, marcar/desmarcar `Enable Physics` y comprobar que los cuatro campos aparecen/desaparecen; los valores se mantienen al desmarcar y remarcar.

4. **`PhysicMaterial` de `DefaultAssets`.** Nuevo asset `DefaultAssets/Physics/Vehicle.physicsmaterial` (fricción media, rebote bajo) y `CityGeneratorDefaultAssets.ApplyTo` lo asigna al campo `Physic Material` por defecto. Test manual: `Set Current Selection As Default` / abrir la ventana en un proyecto limpio y comprobar que el campo llega relleno.

5. **`CityGeneratorTrafficBuilder.BuildVehicles` añade el Rigidbody cuando `enablePhysics` está ON.** Tras `EnsureNonTriggerCollider`, si `enablePhysics`: `AddComponent<Rigidbody>()` en la instancia, `mass`/`drag`/`angularDrag` de `vehiclePhysics`, `constraints = FreezeRotationX | FreezeRotationZ`, `interpolation = Interpolate`, `collisionDetectionMode = ContinuousDynamic`, `useGravity = false`; y si `vehiclePhysics.physicMaterial != null`, se asigna al collider proxy. Con `enablePhysics` OFF, comportamiento idéntico al actual (ningún Rigidbody, ningún material). Test manual: generar con `Enable Physics` OFF y comprobar en el Inspector que ninguna instancia tiene Rigidbody (igual que hoy); generar con ON y comprobar que las instancias sí lo tienen, con los valores configurados.

6. **`CarAgent` detecta su modo y añade el getter `HasPhysics`.** `OnEnable` resuelve `rb = GetComponent<Rigidbody>()`; `HasPhysics => rb != null`. Sin cambio de comportamiento todavía — el modo Rigidbody no se usa aún en `Tick`. Test manual: generar con `Enable Physics` ON, entrar en Play y comprobar (breakpoint o log temporal) que `HasPhysics` es `true`; los coches siguen moviéndose exactamente como antes porque `Tick` no lo consulta todavía (el Rigidbody está ahí pero inerte: `isKinematic` por defecto en `AddComponent` es `false`, así que fijar explícitamente `useGravity=false` + freeze Y en este mismo paso, antes de mover la lógica de conducción, evita que caiga por gravedad en el frame intermedio).

7. **`TrafficManager` gana `FixedUpdate` y reparte sus agentes por modo.** Los agentes con `HasPhysics == true` se tican en `FixedUpdate` (mismo staggering, contado en pasos de física); los que no, siguen en `Update` exactamente como hoy, incluido `Physics.SyncTransforms()` solo si queda al menos uno de ellos. `CarAgent.Tick` gana la rama Rigidbody: en vez de `transform.SetPositionAndRotation`, aplica `rb.MoveRotation(rotation)` y `rb.linearVelocity = (rotation * Vector3.forward) * speed` (con la Y ya congelada por constraint, no hace falta poner Y=0 a mano). Test manual: generar con `Enable Physics` ON, Play, y comprobar que los coches conducen con normalidad (mismo trazado, mismas paradas en semáforos/cruces) — sin chocar todavía con nada, el comportamiento visual debe ser indistinguible del modo cinemático.

8. **Detección de impacto y transición a `Recovering`.** `CarAgent` implementa `OnCollisionEnter(Collision)`: si `HasPhysics` y `physicsState == Driving` y `collision.impulse.magnitude >= VehicleImpactImpulseThreshold`, entra en `Recovering` — libera `reservedIntersection` (mismo camino que `ReleaseReservationWhileBlocked`), sale de `LaneOccupancy` si tenía segmento, `useGravity = true`, quita la constraint `FreezePositionY`. Mientras `Recovering`, `Tick` se salta toda la lógica de conducción (early-return tras comprobar el estado). Test manual: escena de test con `Enable Physics` ON, colocar manualmente un Rigidbody dinámico (un cubo) en la trayectoria de un coche con la masa/velocidad suficiente para superar el umbral, Play, y comprobar que el coche entra en `Recovering` (dejar de frenar/virar hacia su nodo, caer con gravedad) al chocar.

9. **Salida de `Recovering` y reenganche.** Cada `FixedUpdate` en `Recovering`, tras `VehicleRecoveryMinDuration`, comprueba `rb.angularVelocity.magnitude < VehicleRecoveryAngularVelocitySettle && Vector3.Dot(transform.up, Vector3.up) > VehicleRecoveryUprightDot` (o se fuerza la salida a `VehicleRecoveryMaxDuration`). Al salir: enderezar rotación (`Quaternion.FromToRotation` o slerp hacia upright, con yaw conservado), `network.FindNodeAhead(transform.position, transform.forward)`; si encuentra nodo, fija `targetNode`/`fromNode = -1`, re-entra en `LaneOccupancy`, restaura `useGravity=false` + `FreezePositionY`, pasa a `Driving`. Si no encuentra nodo, incrementa `reattachAttempts` y reintenta tras `VehicleReattachRetryInterval`; al superar `VehicleReattachMaxAttempts`, `enabled = false`. Test manual: mismo escenario del paso 8, dejar que el coche se calme tras el impacto y comprobar que retoma la conducción (targetNode válido, vuelve a frenar en semáforos) desde su posición final, no desde donde estaba antes del golpe.

10. **Tests EditMode.** `Assets/Tests/EditMode/Generation/VehiclePhysicsBuildTests.cs`: con `enablePhysics` OFF, la instancia no tiene `Rigidbody`; con ON, lo tiene con `mass`/`drag`/`angularDrag`/constraints/interpolation/collision-detection correctos, y el `Physic Material` asignado al collider proxy cuando no está vacío (y ausente cuando sí lo está).

11. **Tests PlayMode.** `Assets/Tests/PlayMode/Traffic/CarAgentPhysicsRecoveryTests.cs` (o ruta equivalente): un `CarAgent` sintético en modo Rigidbody con reserva de cruce activa, golpeado por encima del umbral, entra en `Recovering` y libera la reserva/el segmento de `LaneOccupancy`; un impacto por debajo del umbral no dispara `Recovering`; tras calmarse (simulando pasos de física hasta que el estado se asiente), vuelve a `Driving` con un `targetNode` válido.

12. **Documentación.** `docs/user-manual.md`/`.es.md`: el parámetro `Enable Physics` y sus cuatro campos, qué cambia y qué no. `docs/architecture/runtime-and-traffic.md`: el segundo modo de `CarAgent`, la máquina de estados `Driving`/`Recovering`, el `FixedUpdate` de `TrafficManager`. `docs/architecture/editor-tool.md`: los campos nuevos de la card Traffic y el `PhysicMaterial` de `DefaultAssets`. `CLAUDE.md`: el invariante de paridad con OFF y el de detección de modo por componente, no por settings. `CHANGELOG.md`: entrada en `## [Unreleased]`.

## Criterios de aceptación

**Paridad con `Enable Physics` OFF**

- [ ] Con `Enable Physics` OFF, ninguna instancia de vehículo generada tiene `Rigidbody` ni `PhysicMaterial` asignado — idéntico a antes de esta spec.
- [ ] Con `Enable Physics` OFF, `TrafficManager` sigue ticando en `Update` con `Physics.SyncTransforms()`, sin ningún `FixedUpdate` activo.
- [ ] Con `Enable Physics` OFF, el trazado, la velocidad y el comportamiento en semáforos/cruces de los vehículos es indistinguible del de antes de esta spec.

**UI**

- [ ] La card Traffic muestra el toggle `Enable Physics`, OFF por defecto.
- [ ] Los campos `Mass`, `Drag`, `Angular Drag`, `Physic Material` solo son visibles/editables cuando `Enable Physics` está ON.
- [ ] `Physic Material` llega pre-rellenado con `DefaultAssets/Physics/Vehicle.physicsmaterial` tras `Set Current Selection As Default` o en un proyecto donde nunca se ha tocado.
- [ ] Vaciar `Physic Material` y generar no bloquea la generación (el validador no lo exige).

**Generación con `Enable Physics` ON**

- [ ] Cada instancia de vehículo generada tiene un `Rigidbody` no-kinemático con `mass`/`drag`/`angularDrag` iguales a los configurados en la card Traffic.
- [ ] El `Rigidbody` tiene `constraints = FreezeRotationX | FreezeRotationZ`, `interpolation = Interpolate`, `collisionDetectionMode = ContinuousDynamic`, `useGravity = false` al generar.
- [ ] Si `Physic Material` no está vacío, el collider proxy de cada instancia lo tiene asignado.

**Conducción normal con físicas ON**

- [ ] En Play, con `Enable Physics` ON y sin ningún impacto, los vehículos circulan, giran, frenan en semáforos y respetan la prioridad de cruce sin diferencia visual apreciable respecto al modo OFF.
- [ ] Ningún vehículo cae por gravedad ni se despega del plano de la calzada mientras conduce con normalidad.

**Respuesta al impacto**

- [ ] Un vehículo golpeado por otro vehículo generado (impulso por encima del umbral) entra en estado `Recovering`: deja de frenar por semáforos/sensor/prioridad, libera su reserva de cruce y su segmento de `LaneOccupancy`, y su Rigidbody responde libremente a la física del motor (puede moverse, girar, volcar).
- [ ] Un vehículo golpeado por un Rigidbody dinámico externo (no generado por la tool) responde de la misma forma.
- [ ] Un vehículo desviado que choca contra geometría estática (un edificio, una farola) rebota/se detiene contra ella en vez de atravesarla.
- [ ] Un roce por debajo del umbral de impulso (p. ej. una cola de coches tocándose en un semáforo) NO dispara `Recovering`.
- [ ] Un peatón que camina contra un vehículo no lo desplaza, no lo desvía de su carril y no dispara `Recovering`, por rápido que llegue.
- [ ] El player sigue chocando físicamente con los vehículos (no los atraviesa), y tampoco los saca de `Driving`.
- [ ] Con `Enable Physics` OFF no se ignora ninguna pareja de colliders: el comportamiento físico de la escena es idéntico a antes de esta spec.

**Recuperación**

- [ ] Tras calmarse (velocidad angular y desviación de plano por debajo de los umbrales, o al alcanzar la duración máxima de seguridad), el vehículo se endereza y vuelve a estado `Driving` desde su posición final, no desde donde estaba antes del impacto.
- [ ] Al reenganchar, retoma semáforos/sensor/prioridad de cruce con normalidad, y vuelve a `useGravity = false` con la posición Y congelada.
- [ ] Si tras los reintentos configurados no encuentra ningún nodo de red por delante, el vehículo se desactiva (`enabled = false`), sin excepciones ni referencias a red rotas.

**Multi-ciudad y managers**

- [ ] Dos ciudades en la misma escena (SPEC 16), una generada con `Enable Physics` ON y otra con OFF, funcionan cada una según su propio modo sin interferir entre sí.

**Tests**

- [ ] Los tests EditMode de `VehiclePhysicsBuildTests` (o ruta equivalente) existen y pasan, cubriendo la configuración del Rigidbody/material en ambos estados del flag.
- [ ] Los tests PlayMode de recuperación existen y pasan, cubriendo: entrada en `Recovering` por impacto por encima del umbral, no-entrada por debajo del umbral, liberación de reserva/segmento, y reenganche a un nodo válido.
- [ ] La suite completa (EditMode, PlayMode y Performance) de `Assets/Tests/` sigue pasando en su totalidad.

**Documentación**

- [ ] `docs/user-manual.md`/`.es.md` documentan `Enable Physics` y sus cuatro campos.
- [ ] `docs/architecture/runtime-and-traffic.md` documenta el segundo modo de `CarAgent`, la máquina de estados y el `FixedUpdate` de `TrafficManager`.
- [ ] `docs/architecture/editor-tool.md` documenta los campos nuevos de la card Traffic y el `PhysicMaterial` de `DefaultAssets`.
- [ ] `CLAUDE.md` tiene los dos invariantes nuevos añadidos a la lista de invariantes del proyecto.
- [ ] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo la nueva funcionalidad.

## Decisiones tomadas y descartadas

**Modo de conducción con físicas**

- **Rigidbody dinámico con velocidad impuesta mientras conduce**, no kinemático y no motor por fuerzas. Decisión explícita del usuario tras aclarar el caso real: el objetivo no es que los coches generados choquen entre sí, sino que respondan de forma realista al golpe de un coche externo (p. ej. conducido por el player). Un Rigidbody kinemático colisiona con uno dinámico pero es inamovible — el coche del player rebotaría contra un muro invisible, justo lo contrario de "colisión realista". Solo un Rigidbody no-kinemático puede salir despedido, girar o desplazarse por el impacto.
- **Descartado: motor por fuerzas (AddForce/torque) en vez de velocidad impuesta.** Más realista en teoría, pero rediseña por completo la conducción: los radios de giro, `arriveRadius`, el sensor frontal y la reserva de cruces sin semáforo están calibrados para movimiento cinemático exacto. `CLAUDE.md` declara el comportamiento del tráfico "done and correct" — tocarlo por completo es trabajo no pedido y de riesgo alto para un beneficio que la spec no necesita (el impacto en sí ya es física real; conducir con fuerzas no cambia el impacto, solo la conducción normal).
- **Descartado: Rigidbody kinemático que pasa a no-kinemático solo al detectar colisión.** Preserva el comportamiento actual con exactitud milimétrica mientras conduce, pero kinemático-contra-kinemático no dispara `OnCollisionEnter` en absoluto, así que dos vehículos generados nunca se detectarían entre sí como impacto — incumple directamente "colisión de otro coche" del pedido original.

**Origen del Rigidbody y del material**

- **Añadidos a la instancia por el generador, no baked en los 15 prefabs de `DefaultAssets`.** Decisión explícita del usuario. Funciona con cualquier prefab de vehículo del usuario, no solo los de la demo — invariante de portabilidad del proyecto. Con `Enable Physics` OFF la escena queda literalmente idéntica a hoy sin que el generador tenga que *quitar* nada de un prefab que ya lo trajera baked.
- **Mass/Drag/Angular Drag/Physic Material configurables desde la tool**, no fijos en `CityGeneratorConstants`. Decisión explícita del usuario tras la primera ronda: la masa en concreto decide si el coche del player mueve al generado o rebota contra él, así que dejarla fija habría sido demasiado rígido para el caso de uso real.
- **Únicos para todos los vehículos, no por entrada de `VehicleEntry`.** Coherente con cómo la tool ya trata el resto de tuning de tráfico (`maxSpeed`, `sensorRange`, etc., iguales para todos). Un camión pesando igual que un deportivo es menos realista, pero ningún comportamiento de la tool depende de la masa relativa entre tipos, y añadir una columna por tipo solo tendría sentido con `Enable Physics` ON — una dependencia cruzada entre dos cards que el usuario no pidió.
- **`Interpolation`, `Collision Detection` y las constraints de rotación X/Z fijos por código, no expuestos en la UI.** No son gusto del usuario: son lo que evita que un coche a 9 m/s atraviese un collider en un frame, y lo que evita que vuelque conduciendo en recto. Exponerlos convertiría un bug de "mis coches vuelan a través de las paredes" en un problema de soporte imposible de diagnosticar a distancia.
- **`Physic Material` propio en `DefaultAssets`, opcional, nunca obligatorio.** Da un tacto de choque razonable por defecto sin forzar al usuario. No se valida como requerido (a diferencia del Traffic Light Prefab) porque su ausencia degrada el tacto pero no rompe nada — el collider simplemente usa el material default de Unity.

**Recuperación tras el impacto**

- **Recupera y retoma desde donde quedó** (`FindNodeAhead` desde la posición/heading final), no vuelve al nodo que tenía como objetivo ni se teletransporta. Decisión explícita del usuario. Volver al nodo antiguo podría significar circular en contradirección si el golpe lo giró 180°; teletransportarse es visualmente peor que el propio choque. Reutiliza el mismo método que `Start` ya usa para el primer enganche a la red — sin lógica nueva de búsqueda.
- **Toda la lógica de conducción se suspende durante `Recovering`, incluida la reserva de cruce y el segmento de `LaneOccupancy`.** Decisión explícita del usuario, y la única compatible con el invariante de `CLAUDE.md` sobre el deadlock de cinco minutos: un coche accidentado que retuviera su reserva de cruce mientras da vueltas descontroladas bloquearía ese cruce indefinidamente para el resto del tráfico — el mismo patrón de fallo, con otra causa.
- **Umbral de impulso constante + salida por umbrales de calma (no duración fija, no cualquier contacto).** Decisión explícita del usuario. Un umbral filtra el roce inevitable de una cola de coches parados en un semáforo (que sí genera `OnCollisionEnter` constantemente); sin él, la ciudad entraría en `Recovering` perpetuo y dejaría de respetar los semáforos. Duración de calma en vez de fija: un toque leve se resuelve en medio segundo, un vuelco no se corta a mitad — con mínimo y máximo de seguridad para no quedar nunca indefinidamente en el estado ni salir antes de asentarse.
- **Sin gravedad y con la posición Y congelada mientras conduce; ambas liberadas solo durante `Recovering`.** Reproduce exactamente el `position.y = 0f` de hoy en el régimen normal (coherente con "OFF y sin impacto = igual que antes"), y solo permite que el golpe levante/incline el vehículo de forma creíble durante el estado que ya está pensado para verse físico. La alternativa de gravedad siempre activa exige que el `BoxCollider` de cada prefab esté ajustado a las ruedas (sin ninguna garantía hoy) y añade resolución de contacto permanente para 80 vehículos parados sobre el suelo.
- **Tras agotar los reintentos de reenganche, se desactiva (`enabled = false`)**, nunca se recoloca por teletransporte ni se destruye la instancia. Mismo fallback que ya usa `AdvanceToNextNode` ante un dead-end de Custom Grid — coherencia de comportamiento ante "no hay ruta posible", y evita tanto un salto visual como romper la simetría de que nada se autodestruye en runtime en el resto de la tool.

**Tick y `TrafficManager`**

- **`FixedUpdate` para los agentes en modo Rigidbody, `Update` sin cambios para los cinemáticos, coexistiendo bajo el mismo manager.** Decisión explícita del usuario. Escribir velocidad fuera del paso de física produce jitter y hace que el impulso de un impacto se sobrescriba de forma errática según el framerate. El coste es que el staggering del sensor (`staggerMinAgentCount`/`staggerDistance`/`staggerFrames`) pasa a contarse en pasos de física para esos agentes — motivo por el que esta spec añade SPEC 05 a sus dependencias y por el que el criterio de aceptación exige reverificar una ciudad demo, tal y como `CLAUDE.md` ya pedía para cualquier cambio a ese valor.
- **El modo se detecta por componente (`GetComponent<Rigidbody>()`), no por un flag leído de `CityGeneratorSettings`.** `CarAgent` no conoce `CityGeneratorSettings` hoy (vive en `Runtime`, los settings en `Editor`) y esta spec no rompe esa separación de ensamblados. Es también lo que permite, sin ningún flag adicional, que SPEC 16 siga funcionando sin fricción: dos ciudades bajo managers distintos (o el mismo) pueden tener modos de físicas distintos porque cada `CarAgent` decide por sí mismo.

**Verificación**

- **Tests EditMode del generador + PlayMode de la máquina de estados**, en vez de solo QA manual. Decisión explícita del usuario, siguiendo el precedente ya sentado por SPEC 05/15/16: el bug caro de esta spec (una reserva de cruce que no se libera durante `Recovering`, reproduciendo el deadlock de cinco minutos por otra vía) es exactamente el tipo de regresión silenciosa que solo se manifiesta en PlayMode, no en una inspección visual rápida.

**Peatones contra vehículos (añadido durante QA manual, tras el paso 9)**

- **Un peatón nunca mueve ni desestabiliza a un coche.** Detectado en QA: un peatón que choca contra un coche lo arrastra. La causa no es la masa sino el tipo de cuerpo — un peatón generado es un `Collider` sin `Rigidbody` movido por `transform.position` (`PedestrianAgent`), es decir un collider *estático* teletransportado, de masa infinita para PhysX. La depenetración empuja al coche fuera de su carril y el impulso del contacto supera de largo `VehicleImpactImpulseThreshold`, así que el coche entra en `Recovering`, deja de reescribir su propia velocidad y el peatón lo arrastra visiblemente.
- **La solución son dos piezas, ambas necesarias.** (1) `VehiclePedestrianCollisionFilter` (`Runtime`) empareja con `Physics.IgnoreCollision` el collider proxy de cada vehículo en modo Rigidbody con el de cada `PedestrianAgent` registrado, así que el contacto no llega a existir; el registro ocurre en el `OnEnable` de ambos agentes (Unity resetea el estado de ignore de un collider al deshabilitarlo y volver a habilitarlo, así que un pase único al arrancar la escena no sobreviviría) y solo para un coche que realmente tiene `Rigidbody`. (2) `CarAgent.OnCollisionEnter` descarta cualquier contacto que `IsPedestrianContact` reconozca (capa de `pedestrianMask` primero, y solo entonces un `GetComponentInParent<PedestrianAgent>()` para el prefab de usuario cuyo collider profundo — intacto por la política de colliders, por tanto nunca en esa capa — es el que ha tocado), sea cual sea el impulso.
- **Descartado: desactivar el par Vehicle↔Pedestrian en la matriz de colisión del proyecto.** Una línea en vez de un registro por parejas, pero el player vive en la capa `Pedestrian` a propósito (`CityGeneratorSceneBuilder.AssignPedestrianLayer`), así que atravesaría los coches — regresión visible. Además escribe en los Project Settings del usuario, justo lo que el invariante de portabilidad evita. El emparejamiento por `PedestrianAgent` registrado deja al player, y a cualquier otro objeto que el usuario ponga en esa capa, colisionando exactamente igual que antes.
- **Descartado: convertir el collider proxy del peatón en `isTrigger`.** También elimina la respuesta física y los sensores del coche seguirían viéndolo (`QueryTriggerInteraction.Collide`), pero el player dejaría de chocar con los peatones y cambiaría el comportamiento también con `Enable Physics` OFF — incumple la paridad bit a bit que esta spec exige.
- **Descartado: subir `VehicleImpactImpulseThreshold`.** El umbral gobierna también los choques legítimos entre coches; subirlo hasta tapar a un peatón de masa infinita (no hay tal valor) mataría los accidentes reales.
- **Descartado: `Rigidbody` kinemático en el peatón.** Un kinemático sigue teniendo masa infinita y empuja igual; solo abarataría mover un collider estático.
- **Solo el collider proxy raíz de cada lado entra en el emparejamiento**, coherente con la política de colliders que ya sigue `CityGeneratorColliderUtility`: un collider más profundo en la jerarquía del prefab del usuario se deja intacto también aquí. La segunda pieza (el filtro de `OnCollisionEnter`) es la que cubre ese caso.

**Fuera de alcance**

- **Sin colisión con peatones/player, sin feedback de impacto, sin menú de aplicar físicas a una ciudad ya generada.** Los tres, decisión explícita del usuario en las rondas de clarificación: el primero porque ni `PedestrianAgent` ni el `CharacterController` del player encajan con el mecanismo de `OnCollisionEnter` que esta spec usa (merecería su propio diseño) — lo que la spec sí hace, tras el hallazgo de QA de arriba, es *suprimir* esa interacción física, no modelarla: atropellar a un peatón (derribo, ragdoll, feedback) sigue fuera de alcance y necesitaría su propia spec; el segundo porque toca subsistemas ajenos (audio, mallas) sin que se haya pedido; el tercero porque `Enable Physics` es un ajuste de generación como cualquier otro — cambiarlo y regenerar es el flujo ya establecido por la tool para todo lo demás.

## Riesgos identificados

- **El `FixedUpdate` de `TrafficManager` para agentes en modo Rigidbody es la parte de mayor riesgo de regresión silenciosa.** El staggering del sensor (`staggerMinAgentCount`/`staggerDistance`/`staggerFrames`) fue calibrado y verificado en frames de `Update`; contarlo en pasos de física puede cambiar su cadencia efectiva (el timestep fijo no coincide con el framerate real) de forma que solo se note con muchos vehículos en pantalla. Mitigación: el criterio de aceptación exige reverificar una ciudad demo con `Enable Physics` ON antes de dar el paso 7 por cerrado, tal y como `CLAUDE.md` ya pedía para cualquier cambio a `staggerMinAgentCount`.
- **Interpolation + ContinuousDynamic en 80+ Rigidbodies simultáneos tiene un coste de física no trivial**, especialmente en `Recovering` (constraints liberadas, resolución de contacto contra geometría estática). Una ciudad grande con `Enable Physics` ON puede caer de framerate de forma perceptible aunque ninguna funcionalidad esté "rota". Mitigación: ninguna automática en esta spec — se documenta como coste conocido de activar el flag; si llega a ser un problema real, ajustar `Fixed Timestep`/reducir `Collision Detection` a `ContinuousSpeculative` queda como ampliación futura, no bloquea esta entrega.
- **La transición `Driving` → `Recovering` → `Driving` toca el mismo mecanismo de liberación de reserva de cruce que ya resolvió el deadlock de cinco minutos documentado en `CLAUDE.md`.** Reutilizarlo mal (p. ej. liberar la reserva pero no salir de `LaneOccupancy`, o al revés) reintroduciría una variante del mismo bloqueo por una vía distinta — visualmente indistinguible de un atasco normal hasta que alguien lo deja correr varios minutos. Mitigación: el test PlayMode del paso 11 verifica explícitamente ambas liberaciones (reserva y segmento), no solo el cambio de estado.
- **Detectar el modo por `GetComponent<Rigidbody>()` en vez de por un flag asume que nadie añade un Rigidbody a un vehículo generado por otro motivo** (físicas de otro sistema del usuario, un Ragdoll, etc.) — esa instancia pasaría a modo Rigidbody sin haber sido configurada por `CityGeneratorTrafficBuilder` (sin `mass`/`drag`/material correctos, sin constraints). Mitigación: documentado como comportamiento esperado en `CLAUDE.md`/`docs/architecture/runtime-and-traffic.md`: cualquier `Rigidbody` en el GameObject del `CarAgent` activa el modo físico, sea quien sea quien lo puso ahí.
- **El umbral de impulso fijo (`VehicleImpactImpulseThreshold`) es una única constante para 15 vehículos de masas potencialmente distintas** (si el usuario ajusta `Mass` pensando en un tipo de vehículo concreto) y para cualquier Rigidbody externo del usuario, con su propia masa arbitraria. Un umbral que funciona bien para el valor por defecto puede resultar demasiado sensible o demasiado insensible si el usuario cambia mucho `Mass` sin ajustar nada más. Mitigación: ninguna en esta spec — el umbral es una constante interna, no expuesta, por decisión ya tomada (ver Decisiones); si en la práctica resulta mal calibrado para casos extremos de `Mass`, ajustar el valor por defecto es un cambio de una línea, no una nueva spec.
- **Ninguna escena de test dedicada con físicas activas queda en el repositorio** (los tests son sintéticos, EditMode/PlayMode). Una regresión visual sutil (un coche que se ve raro al recuperarse, un enderezado brusco) no la detecta ningún test automatizado, solo QA manual puntual. Mitigación: ninguna estructural — mismo criterio que SPEC 16 aplicó a su propio caso multi-ciudad: los tests cubren la lógica con precisión, y una escena de test permanente solo para esto sube el peso del repo sin dejar más cobertura real.
