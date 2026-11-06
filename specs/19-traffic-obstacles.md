# SPEC 19 — Obstáculos de tráfico y parachoques de los coches con físicas

> **Estado:** Implementado
> **Fecha:** 2026-11-06
> **Proyecto:** Unity-CityGenerator (`santiandrade/Unity-CityGenerator`)
> **Depende de:** SPEC 05 (sensor frontal, `LaneOccupancy` y staggering del sensor), SPEC 17 (`Enable Physics`, máquina de estados `Driving`/`Recovering`)
> **Objetivo:** Que un vehículo generado deje de arrastrar a un objeto físico ajeno a la tool (típicamente el coche que conduce el jugador): que lo vea y frene por él, que se detenga si aun así acaba empujándolo, y que no salga disparado a la velocidad previa al choque al recuperarse.

## Contexto — el fallo observado

Detectado en un proyecto consumidor (*City Races*, minijuego *Taxi!*): el jugador conduce un coche con `Rigidbody` propio por una ciudad generada con `Enable Physics` ON. Al chocar con un vehículo generado, este sale despedido (entra en `Recovering`), pero al reengancharse acelera hacia delante y, si el coche del jugador está delante, lo arrastra calle abajo sin que haya forma de volver a "impactarlo". Tres causas independientes se suman:

1. **El sensor frontal no ve nada que no sea un `CarAgent` o un peatón.** `VehicleAheadClearance` filtra por `vehicleMask` (capa `Vehicle`) y descarta todo collider que no esté en `ColliderRegistry`; `PedestrianAheadClearance` filtra por `pedestrianMask`. El coche del jugador vive en su propia capa (`Player` en City Races) y no es un `CarAgent`: es invisible. Esto ocurre también con `Enable Physics` OFF.
2. **En `Driving`, la velocidad se impone cada paso de física** (`rb.linearVelocity = forward * speed`). PhysX resuelve el contacto, pero el paso siguiente vuelve a imponer la velocidad: en la práctica, fuerza infinita. La masa del coche del jugador es irrelevante.
3. **El reenganche conserva `speed`.** `TryReattach` vuelve a `Driving` sin tocar `speed`, así que el coche retoma de golpe la velocidad que llevaba antes del impacto. Y si al reengancharse ya está en contacto con el coche del jugador, ese contacto es continuo: Unity solo genera `OnCollisionStay`, nunca un nuevo `OnCollisionEnter`, que es lo único que lo devuelve a `Recovering`.

## Scope

**Dentro:**

- **Nuevo componente `TrafficObstacle` (`Runtime`)**, que el usuario añade a cualquier objeto que el tráfico generado deba tratar como un vehículo más por delante (el coche del jugador, un vehículo propio, una barrera móvil). Registra en un registro estático los colliders sólidos de su jerarquía (no triggers, no `WheelCollider`) en `OnEnable` y los quita en `OnDisable`.
- **`CarAgent` gana un tercer sensor, `ObstacleAheadClearance`**, que recorre los `TrafficObstacle` registrados y, para cada uno dentro de alcance, lanza tres rayos paralelos (`Collider.Raycast`, por collider, sin capa ni layer mask) a lo largo del rumbo hacia su nodo objetivo. Su resultado se combina con los otros dos (`Mathf.Min`) con `StopReason.VehicleAhead`, igual que ya hace el peatón. Funciona en ambos modos de conducción (cinemático y Rigidbody).
- **"Parachoques" para los coches en modo Rigidbody**: `CarAgent.OnCollisionStay` (y `OnCollisionEnter` por debajo del umbral de impacto) detecta un contacto sostenido en el frontal del coche contra un `Rigidbody` dinámico que no es otro `CarAgent` ni un peatón, y detiene el coche en seco (`speed = 0`) mientras dure el contacto y durante `VehicleBumperHoldDuration` después.
- **`speed = 0` al entrar en `Recovering`**, de modo que al reengancharse el coche arranca desde parado con su aceleración normal.
- **Tests PlayMode** en `Assets/Tests/PlayMode/Traffic/`.
- **Documentación**: `docs/architecture/runtime-and-traffic.md`, `docs/user-manual.md`/`.es.md` (sección nueva sobre `TrafficObstacle` y nota en 6.2), `CLAUDE.md` (nuevo invariante y fila en la tabla de specs), `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance:**

- **Conducción por fuerzas o límite de Δv por paso** (el punto 4 de la propuesta original). Haría que un obstáculo pesado pudiera resistir de verdad, pero toca el seguimiento de carril que SPEC 17 dejó calibrado; se aparca hasta tener una necesidad concreta.
- **Añadir `TrafficObstacle` automáticamente** a nada generado por la tool (el player generado ya está en la capa `Pedestrian` y el sensor de peatones lo ve).
- **Que los peatones esquiven un `TrafficObstacle`.** La red de peatones sigue siendo puramente física (`PrunePlacedObstacles`), sin cambios.
- **Reacción de "atasco" o maniobra para rodear un obstáculo parado.** Un coche generado detrás de un `TrafficObstacle` parado espera indefinidamente, igual que detrás de un coche averiado.

## Modelo de datos

### `TrafficObstacle` (nuevo, `Runtime/TrafficObstacle.cs`)

```csharp
[DisallowMultipleComponent]
public class TrafficObstacle : MonoBehaviour
{
    // Left empty: every enabled, non-trigger collider in the hierarchy except WheelColliders,
    // gathered in OnEnable.
    [SerializeField] private Collider[] colliders;

    internal static IReadOnlyList<Collider> RegisteredColliders { get; }
}
```

- Registro estático de colliders, no de componentes: el sensor trabaja collider a collider (`Collider.Raycast`), y aplanarlo al registrar evita recorrer jerarquías en cada tick.
- Se vacía en `SubsystemRegistration`, como `CarAgent.ColliderRegistry`, para las sesiones de Play con Domain Reload desactivado.
- `WheelCollider` se excluye: no es un volumen de carrocería y `Collider.Raycast` sobre él no representa el obstáculo.

### `CarAgent` — estado nuevo

```csharp
private const float VehicleBumperHoldDuration = 0.5f;   // s parado tras perder el contacto
private const float VehicleBumperMaxNormalY = 0.5f;     // |normal.y| máximo: excluye suelo/techo

private float lastObstacleClearance = float.MaxValue;   // cacheado como lastPedestrianClearance
private float bumperBlockedUntil;                        // Time.time hasta el que el parachoques retiene
private float bumperFrontZ;                              // z local a partir de la cual un contacto es "frontal"
```

Las constantes son copias propias de `CarAgent`, igual que las de SPEC 17 (`CityGeneratorConstants` es Editor-only).

## Plan de implementación

1. **`TrafficObstacle`**: componente, registro estático, reset en `SubsystemRegistration`.
2. **`ObstacleAheadClearance` en `CarAgent`**: se calcula cuando `runSensor` es true (cacheado en `lastObstacleClearance` el resto), se combina con el sensor de vehículos y el de peatones. Para cada collider registrado: descarta los de la propia jerarquía del coche, descarta por distancia (`bounds.SqrDistance` > (2,2 + `sensorRange`)²), descarta los que no solapan en altura con la carrocería del coche, y lanza tres rayos (centro y ±media anchura del coche) desde el centro del coche, a una altura dentro del solape vertical, en la dirección hacia el nodo objetivo. La distancia se refiere al mismo origen que los otros sensores (2,2 m por delante del centro) para que `minGap` signifique lo mismo.
3. **Parachoques**: `bumperFrontZ` se calcula en `OnEnable` cuando hay `Rigidbody` (cuarto delantero del `BoxCollider` raíz, 0,5 m si no es una caja). `OnCollisionStay` y la rama por debajo del umbral de `OnCollisionEnter` llaman a `CheckBumperContact`. En `Tick`, si `Time.time < bumperBlockedUntil`: `clearance = 0`, `stopReason = VehicleAhead` y `speed = 0` directamente, sin rampa de frenado.
4. **`speed = 0` en `EnterRecovering`.**
5. **Tests PlayMode** (`CarAgentObstacleTests`).
6. **Documentación.**

## Criterios de aceptación

**Sensor de obstáculos**

- [ ] Un coche generado (cinemático o con físicas) frena y se detiene antes de un objeto con `TrafficObstacle` parado en su carril, con el mismo margen (`minGap`) que ante otro coche.
- [ ] El sensor funciona sea cual sea la capa del obstáculo, sin tocar `vehicleMask`/`pedestrianMask` ni la matriz de colisiones del proyecto.
- [ ] Un `TrafficObstacle` en el carril contiguo (fuera del corredor del coche) no lo hace frenar.
- [ ] Sin ningún `TrafficObstacle` en escena, el comportamiento es idéntico al de antes de esta spec, en ambos modos.

**Parachoques (solo `Enable Physics` ON)**

- [ ] Un coche en `Driving` en contacto frontal sostenido con un `Rigidbody` dinámico sin `TrafficObstacle` se detiene en seco en lugar de empujarlo, y retoma la marcha `VehicleBumperHoldDuration` después de perder el contacto.
- [ ] El contacto con otro `CarAgent`, con un peatón, con un collider estático o con un `Rigidbody` kinemático no activa el parachoques.
- [ ] Un contacto por la parte trasera o lateral no activa el parachoques.

**Recuperación**

- [ ] Al reengancharse tras `Recovering`, el coche arranca desde `speed = 0`.

**Tests y documentación**

- [ ] Los tests PlayMode nuevos pasan, y la suite completa (EditMode, PlayMode, Performance) sigue pasando.
- [ ] `docs/architecture/runtime-and-traffic.md`, `docs/user-manual.md`/`.es.md`, `CLAUDE.md` y `CHANGELOG.md` actualizados.

## Decisiones tomadas y descartadas

**Cómo ve el tráfico un objeto ajeno**

- **Componente opt-in (`TrafficObstacle`), no detección automática de cualquier `Rigidbody`.** Un `OverlapSphere` sin filtro de capa encontraría suelo, aceras, props y edificios en cada tick, y filtrar "lo que es un vehículo" por heurística (tiene `Rigidbody`, es dinámico, tiene cierto tamaño) es frágil y cambia el comportamiento en proyectos que no lo pidieron. Un componente explícito es portable, no depende de capas del proyecto del usuario y, sin él, no cambia nada.
- **Descartado: una capa "Traffic Obstacle" configurable en la card Traffic.** Obligaría al usuario a mover su coche a una capa concreta, cuando su capa suele estar ocupada por su propia lógica (en City Races el taxi está en `Player` a propósito). Además ampliaría `vehicleMask`, cuyos aciertos se descartan por identidad contra `ColliderRegistry` — un invariante que esta spec no quiere tocar.
- **`Collider.Raycast` por collider registrado, no una consulta de física de escena.** Ignora capas por construcción (prueba solo ese collider), no comparte buffer con el resto de la escena y su coste es proporcional al número de obstáculos registrados (normalmente uno), con un descarte previo por `bounds`. Tres rayos (centro y bordes del coche) cubren un obstáculo que invade parcialmente el carril; la altura del rayo se ajusta al solape vertical entre el coche y el obstáculo para no fallar con un obstáculo bajo.
- **Mismo `StopReason.VehicleAhead` que un coche o un peatón**, sin estado propio: así `ReleaseReservationWhileBlocked` suelta la prioridad de un cruce sin semáforo si el obstáculo bloquea al coche antes de entrar, exactamente como con otro coche — sin una tercera vía hacia el deadlock de cinco minutos.

**Parachoques**

- **Solo contra `Rigidbody` dinámicos**, porque son lo único que un coche puede arrastrar. Contra geometría estática o un kinemático el coche no arrastra nada (choca contra un muro), y añadirlos convertiría un roce lateral con un edificio tras un `Recovering` en un coche parado para siempre.
- **Nunca contra otro `CarAgent`.** El coche-contra-coche ya lo gobiernan el sensor y las reglas de deadlock; en particular, la regla de deadlock de frente deja que uno de los dos empuje durante `deadlockBreakDuration`. Si el parachoques reaccionara a otro `CarAgent`, dos coches con físicas morro con morro se pararían mutuamente para siempre.
- **Ni contra peatones** (`IsPedestrianContact`): ya están cubiertos por `VehiclePedestrianCollisionFilter` y por el sensor de peatones; SPEC 17 dejó claro que una persona no altera el estado de conducción de un coche.
- **Contacto "frontal" = cuarto delantero del collider raíz y normal casi horizontal.** La normal horizontal excluye el contacto con el suelo (el collider puede rozarlo con la Y congelada), y el cuarto delantero excluye el roce lateral y trasero (un coche golpeado por detrás no debe frenar).
- **Parada en seco (`speed = 0`), no frenada progresiva.** Si ya hay contacto, la rampa de `braking` significa seguir empujando durante medio segundo largo.
- **Retención de `VehicleBumperHoldDuration` tras perder el contacto**, para que el coche no oscile entre avanzar y parar cada paso de física frente a un obstáculo quieto.
- **Descartado: volver a `Recovering` con `OnCollisionStay`.** Soltar el Rigidbody mientras el obstáculo sigue delante solo repite el ciclo: el coche se reengancharía, volvería a empujar y volvería a soltarse.

**Recuperación**

- **`speed = 0` en `EnterRecovering`, no solo en el reenganche.** Durante `Recovering` el coche no conduce, así que `Speed` (público, de diagnóstico) no debe seguir mostrando la velocidad previa al choque; y al reengancharse arranca desde parado sin un caso especial en `TryReattach`.

## Riesgos identificados

- **Un `TrafficObstacle` parado puede bloquear el tráfico indefinidamente**, igual que un coche detenido. Es el comportamiento esperado (la alternativa, atravesarlo o arrastrarlo, es precisamente el fallo), pero se documenta en el manual.
- **El parachoques puede detener un coche que en realidad estaba siendo empujado por detrás de forma oblicua**, si el contacto cae en el cuarto delantero. Riesgo bajo: solo afecta a `Rigidbody` dinámicos que no son `CarAgent`, es decir, al coche del jugador, y detener el coche generado es la reacción menos mala.
- **El sensor de obstáculos se escalona igual que los otros** (`TrafficManager.staggerDistance`): un coche lejos de la cámara puede reaccionar unos frames tarde a un obstáculo que se mueve rápido. Aceptable: es lejos de la cámara por definición.
