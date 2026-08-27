# SPEC 08 — Ciclo de día y noche

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool)
> **Fecha:** 2026-08-27
> **Objetivo:** Añadir un ciclo de día/noche opcional que, activado desde la tab City, hace que la Directional Light generada rote simulando las 24h del día y cambie de intensidad/color según la hora, con hora inicial y velocidad configurables.

## Scope

**Dentro:**

- **Nueva card "Day/Night Cycle"** en la tab City de `CityGeneratorWindow` (junto a General/Ground/Plazas/Buildings/Vegetation/Vehicles/Props/Custom Places), con: toggle `enabled` (desactivado por defecto), slider `Start Hour` (0-24), slider `Speed Multiplier` (0.1-100, 1 = tiempo real), un `Gradient` de color de luz por hora y una `AnimationCurve` de intensidad de luz por hora.
- **`Editor/CityGeneratorSettings.cs`** — nueva `struct DayNightSettings { enabled, startHour, speedMultiplier, lightColorOverTime, lightIntensityOverTime }` y campo `DayNightSettings dayNight` en `CityGeneratorSettings`.
- **`Runtime/DayNightCycle.cs`** (nuevo `MonoBehaviour`) — añadido al GameObject "Directional Light" cuando `dayNight.enabled`. Expone los mismos campos serializados que `DayNightSettings` (proyectados, igual que `MinimapData`/`PointOfInterestEntry` hacen con `CustomPlaceEntry`), un método `ApplySun(float hourOfDay)` que calcula rotación (pitch sobre el eje X, yaw/roll fijos al valor con el que se creó la luz), color e intensidad para esa hora y los aplica al `Light` del propio GameObject, y en `Update` (solo en Play) avanza la hora simulada según `speedMultiplier` y llama a `ApplySun`.
- **`Editor/CityGeneratorSceneBuilder.CreateDirectionalLight`** — cuando `dayNight.enabled`, añade y configura `DayNightCycle` en la luz recién creada y llama a `ApplySun(startHour)` inmediatamente, de forma que la luz ya se ve orientada/coloreada según `startHour` en el Editor sin necesidad de Play. Cuando `dayNight.enabled` es `false`, la luz se comporta exactamente igual que hoy (rotación estática `Euler(50,-30,0)`, sin `DayNightCycle`).
- **`Editor/CityGeneratorSceneBuilder.RebuildInActiveScene`** — deja de dejar la luz completamente intacta: localiza la Directional Light existente en la escena (por nombre "Directional Light", igual que ya hace con el Minimap HUD; si no existe, la crea con `CreateDirectionalLight`, igual que si faltara tras un borrado manual), añade/actualiza su `DayNightCycle` (o lo quita si `dayNight.enabled` pasó a `false`) con los ajustes actuales, y llama a `ApplySun(startHour)` para previsualizar en el Editor. Rotación base, sombras y demás ajustes de la luz que no dependen del ciclo día/noche siguen sin tocarse.
- **Tooltip del botón "Rebuild City in Current Scene"** (`RebuildCurrentSceneButtonTooltip`) actualizado para reflejar que el ciclo día/noche de la luz ahora sí se reconfigura, aunque el resto de la luz (posición base, sombras) siga intacto.
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- Ambient lighting / skybox dinámico según la hora: esta spec solo anima la Directional Light (rotación, color, intensidad). El ambient y el skybox quedan con la configuración estática actual de Unity.
- Trayectoria solar realista (azimut variable, latitud configurable): la rotación es un pitch simple sobre un único eje a lo largo de 24h, con yaw/roll fijos.
- Luna, estrellas, niebla nocturna, o cualquier otro elemento visual asociado a la noche más allá de la propia luz.
- Persistencia de la hora simulada entre sesiones de Play o al guardar la escena: cada vez que se entra en Play, el ciclo arranca desde `startHour`.
- Sincronización con hora real del sistema operativo.
- Publicación de una nueva versión del package (bump de `version`/tag): esta spec entrega el código; el release es un paso posterior con `Tools > City Generator > Release`.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public DayNightSettings dayNight = DayNightSettings.Default();

[Serializable]
internal struct DayNightSettings
{
    [Tooltip("Whether the Directional Light simulates a 24h day/night cycle in Play Mode. Off by default.")]
    public bool enabled;
    [Tooltip("Hour of day (0-24) the cycle starts at, both in Play Mode and as an Editor preview right after generation.")]
    public float startHour;
    [Tooltip("How fast simulated time passes relative to real time. 1 = real time, 2 = twice real time, etc.")]
    public float speedMultiplier;
    [Tooltip("Light color over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
    public Gradient lightColorOverTime;
    [Tooltip("Light intensity over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
    public AnimationCurve lightIntensityOverTime;

    public static DayNightSettings Default() => new DayNightSettings
    {
        enabled = false,
        startHour = 8f,
        speedMultiplier = 1f,
        lightColorOverTime = DefaultColorGradient(),
        lightIntensityOverTime = DefaultIntensityCurve(),
    };
}
```

```csharp
// Runtime/DayNightCycle.cs

namespace CityGenerator.Runtime
{
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("How fast simulated time passes relative to real time. 1 = real time, 2 = twice real time, etc.")]
        public float speedMultiplier = 1f;
        [Tooltip("Light color over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public Gradient lightColorOverTime;
        [Tooltip("Light intensity over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public AnimationCurve lightIntensityOverTime;

        [Tooltip("Current simulated hour of day (0-24). Advances automatically in Play Mode.")]
        public float currentHour;

        private Light light;
        private Quaternion baseRotation;

        public void ApplySun(float hourOfDay) { /* rotates on X (pitch), samples gradient/curve, applies to Light */ }
    }
}
```

Notas:

- `DayNightCycle` sigue el mismo patrón que `MinimapData`/`MinimapHUD`: un DTO/componente propio en `Runtime/` que no conoce `DayNightSettings` (tipo `internal` de `Editor/`) — `CityGeneratorSceneBuilder` proyecta los campos al añadir el componente.
- **Fórmula de rotación** (pitch, eje X, grados): `pitch = hourOfDay / 24f * 360f - 90f`. Con esto, hora 6 (amanecer) = 0° (luz en el horizonte), hora 12 (mediodía) = 90° (luz apuntando hacia abajo, sol en el cenit), hora 18 (atardecer) = 180° (horizonte opuesto), hora 0/24 (medianoche) = -90° (luz apuntando hacia arriba, sol "por debajo"). El yaw y roll con los que se creó la luz (por defecto -30° y 0°, los mismos de hoy) se mantienen fijos; solo el pitch varía.
- `baseRotation` guarda el yaw/roll originales en `Awake`/al añadir el componente, para que `ApplySun` solo modifique el pitch sin depender de en qué rotación exacta se creó la luz.
- El sampler de `Gradient`/`AnimationCurve` usa `hourOfDay / 24f` como `t` (0-1), por lo que ambos recorren un ciclo completo por cada 24h simuladas, independientemente de `speedMultiplier`.
- `Update()` (activo solo en Play, sin `[ExecuteAlways]`) hace `currentHour = (currentHour + Time.deltaTime * speedMultiplier / 3600f) % 24f` y llama a `ApplySun(currentHour)`. El "preview inmediato en Editor" (`Build New Scene`/`Re-Build`) no depende de `Update`: `CityGeneratorSceneBuilder` llama a `ApplySun(startHour)` directamente una vez, tanto en creación como en cada Re-Build.
- Valores por defecto de `DefaultColorGradient()`/`DefaultIntensityCurve()` (definidos en `DayNightSettings`, no en el código de spec): intensidad baja de madrugada, sube hacia el amanecer, máxima al mediodía, baja hacia el atardecer, mínima (no cero) de noche; color frío/oscuro de noche, cálido/anaranjado en amanecer y atardecer, blanco al mediodía — de forma que la luz nunca se apaga del todo (decidido: sigue activa con intensidad mínima marcada por la curva, sin `light.enabled = false`).

## Plan de implementación

1. **Modelo de datos base.** Añadir `DayNightSettings` y el campo `dayNight` a `CityGeneratorSettings.cs`, con `DefaultColorGradient()`/`DefaultIntensityCurve()`. El proyecto compila; sin `DayNightCycle`, builder ni UI todavía, `dayNight.enabled` no tiene ningún efecto observable.

2. **`Runtime/DayNightCycle.cs`.** Nuevo `MonoBehaviour` con los campos serializados, `ApplySun(float hourOfDay)` (rotación por la fórmula de pitch, color/intensidad muestreados de `lightColorOverTime`/`lightIntensityOverTime`) y `Update()` avanzando `currentHour` según `speedMultiplier` solo en Play. Se puede probar añadiendo el componente a mano a una Directional Light en la escena de test y variando `currentHour` en el Inspector.

3. **`Editor/CityGeneratorSceneBuilder.CreateDirectionalLight`.** Recibe `DayNightSettings dayNight`; cuando `enabled`, añade `DayNightCycle`, proyecta los campos desde `dayNight` y llama a `ApplySun(dayNight.startHour)`. Cuando no está activo, se comporta igual que hoy. `BuildAndSaveScene` pasa `settings.dayNight` en su llamada existente a `CreateDirectionalLight`. Generar una ciudad nueva con el ciclo activo ya deja la luz orientada/coloreada según `startHour`, visible en el Editor sin entrar en Play.

4. **`Editor/CityGeneratorSceneBuilder.RebuildInActiveScene`.** Localiza la Directional Light existente por nombre (o la crea si falta), añade/actualiza o quita `DayNightCycle` según `dayNight.enabled`, y llama a `ApplySun(startHour)`. Actualizar el tooltip `RebuildCurrentSceneButtonTooltip`. Re-Build sobre una escena existente refleja los cambios de ajustes del ciclo día/noche sin tocar el resto de la luz.

5. **Card "Day/Night Cycle" en la tab City.** Nueva card en `CityGeneratorWindow` (junto a las demás de `TabCity`) con: toggle `Enabled`, slider `Start Hour` (0-24), slider `Speed Multiplier` (0.1-100), y los campos de `Gradient`/`AnimationCurve` para color/intensidad. Badge de card igual que el resto (p. ej. hora de inicio o "Off").

6. **Verificación en Play Mode.** Generar la escena de test (`Assets/Scenes/City.unity`) con el ciclo activo, entrar en Play y comprobar visualmente que la luz rota y cambia de color/intensidad a lo largo del tiempo simulado, a la velocidad configurada.

7. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`): añadir el ciclo de día y noche.

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con `dayNight: DayNightSettings` tal como se definió, con `enabled = false` por defecto.
- [x] `Runtime/DayNightCycle.cs` existe en `Runtime/` (no `Editor/`), compila también en player builds, y expone `speedMultiplier`, `lightColorOverTime`, `lightIntensityOverTime`, `currentHour` y `ApplySun(float hourOfDay)`.
- [x] Generar una ciudad nueva ("Build City in New Scene") con `dayNight.enabled = true` añade `DayNightCycle` al GameObject "Directional Light", y la luz aparece ya orientada/coloreada según `startHour` en la vista de Editor, sin necesidad de entrar en Play. Verificado: pitch 30° para `startHour=8` (fórmula `8/24*360-90=30`), yaw/roll conservados (-30°/0°), color/intensidad muestreados correctamente del gradiente/curva por defecto.
- [x] Generar una ciudad nueva con `dayNight.enabled = false` deja la Directional Light exactamente como hoy (sin `DayNightCycle`, rotación estática `Euler(50,-30,0)`). Verificado directamente.
- [x] En Play Mode, con el ciclo activo, la luz rota continuamente y su color/intensidad varían a lo largo del tiempo simulado, completando un ciclo de 24h simuladas en `24h / speedMultiplier` de tiempo real. Verificado con `speedMultiplier` acelerado: `currentHour` avanzó de 8 a ~12.5 en ~4.5s reales, con rotación/color/intensidad actualizados en consecuencia.
- [x] Con `speedMultiplier = 1`, una hora simulada tarda una hora real. Confirmado por la fórmula (`Time.deltaTime * speedMultiplier / 3600f`) y por extrapolación lineal desde la prueba anterior a `speedMultiplier` alto.
- [x] "Rebuild City in Current Scene" sobre una escena ya generada actualiza `DayNightCycle` en la Directional Light existente según los ajustes actuales (lo añade si se activó, lo quita si se desactivó, actualiza sus valores si ya existía), sin recrear ni mover la luz, y sin afectar cámara ni player. Verificado en ambas direcciones (añadir y quitar), cámara intacta tras el Re-Build.
- [x] Si la Directional Light fue borrada manualmente antes de un Re-Build, este la recrea con el ciclo día/noche correctamente configurado, sin lanzar excepciones. Verificado: luz recreada con `DayNightCycle` (`currentHour=20`, `speed=5`) tras borrarla manualmente.
- [x] La card "Day/Night Cycle" en la tab City permite activar/desactivar el ciclo y editar `Start Hour`, `Speed Multiplier`, el `Gradient` de color y la `AnimationCurve` de intensidad.
- [x] La suite EditMode/PlayMode/Performance de `Assets/Tests/` sigue pasando en su totalidad. 63 EditMode + 14 PlayMode + 7 Performance = 84/84.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo el ciclo de día y noche.

## Decisiones tomadas y descartadas

- **Card nueva en la tab City, no una tab dedicada.** Solo 4-5 parámetros (enabled, startHour, speedMultiplier, gradient, curve); no justifica una tab propia como sí lo hizo Minimap (que preveía crecer con más ajustes visuales del HUD).
- **Rotación como pitch simple sobre un único eje, yaw/roll fijos.** Evita modelar una trayectoria solar realista (azimut, latitud); es predecible, fácil de razonar y suficiente para el objetivo pedido ("simular el movimiento del sol como si fuesen 24h").
- **No:** trayectoria solar realista con azimut/latitud configurable. Descartado por complejidad desproporcionada frente al alcance pedido; se puede añadir en una spec futura si hace falta.
- **Intensidad y color configurables vía `Gradient`/`AnimationCurve`, no una fórmula fija en código.** Da control artístico completo al usuario (paleta día/noche) sin código extra, siguiendo el patrón de otros ajustes configurables de la tool.
- **No:** ajustar ambient lighting/skybox. Mantiene el alcance acotado a lo que pidió el usuario (la luz direccional como sol); es candidato natural para una spec futura si se quiere un cielo dinámico completo.
- **La luz nunca se desactiva de noche (`light.enabled` siempre `true`); la oscuridad se logra con intensidad mínima en la curva.** Más simple, sin lógica de encendido/apagado ni riesgo de "pop" visible en el umbral.
- **`speedMultiplier` acotado con un slider (0.1-100) en vez de validación no bloqueante.** Evita valores absurdos (pausado, negativo) por diseño de la UI, sin necesidad de un `HelpBox` adicional.
- **Hora del día como un único `float` en `[0,24)`, no hora/minuto separados.** Más simple de serializar, mostrar en un slider y usar en los cálculos de rotación/curvas.
- **`Re-Build City in Current Scene` pasa a reconfigurar la luz (rompe el invariante previo "light... left untouched").** Decisión explícita del usuario: sin esto, activar/desactivar o ajustar el ciclo día/noche desde la tool no tendría efecto sobre una ciudad ya generada en la escena activa, obligando siempre a regenerar en una escena nueva. Se documenta el cambio en el tooltip del botón para que quede visible al usuario de la tool.
- **`RebuildInActiveScene` recrea la Directional Light si no la encuentra por nombre, en vez de lanzar un error.** Coherente con que el ciclo día/noche debe poder activarse igualmente aunque el usuario haya borrado manualmente la luz; sigue el mismo patrón defensivo que ya usa el Minimap HUD al buscar su instancia previa por nombre.
- **Preview inmediato en el Editor (`ApplySun(startHour)` llamado directamente desde el builder), no vía `[ExecuteAlways]` en `DayNightCycle`.** Decisión explícita del usuario (aplicar `startHour` sin esperar a Play). Llamar a `ApplySun` una vez desde el builder es más simple y predecible que un componente `[ExecuteAlways]` recalculando en cada refresco del Editor, y evita el coste/complejidad de animar en modo Editor.

## Riesgos identificados

- **Romper el invariante documentado "Light, camera and player are left untouched" en `Re-Build City in Current Scene`.** Cualquier código o documentación que asuma que la luz nunca cambia en un Re-Build (comentarios en `CityGeneratorSceneBuilder`, el tooltip del botón) debe actualizarse a la vez; de lo contrario queda una descripción incorrecta en la UI. Mitigación: el paso 4 del plan incluye explícitamente actualizar el tooltip y el comentario XML de `RebuildInActiveScene`.
- **Valores por defecto de `Gradient`/`AnimationCurve` poco convincentes visualmente.** Un gradiente o curva por defecto mal calibrados podrían dar un ciclo día/noche con transiciones bruscas o poco creíbles la primera vez que el usuario lo activa. Mitigación: el paso 6 del plan (verificación en Play Mode) sirve de QA manual antes de dar la spec por completa; los valores son ajustables desde la card sin tocar código.
- **Localizar la Directional Light por nombre ("Directional Light") es frágil si el usuario la renombra manualmente.** Mismo patrón (y misma limitación) que ya acepta el Minimap HUD al buscar su instancia previa por nombre; no se introduce un mecanismo más robusto (p. ej. un marcador de componente) porque el resto del pipeline ya asume este patrón como suficiente.

## Qué **no** está en esta spec

- Ambient lighting / skybox dinámico según la hora.
- Trayectoria solar realista (azimut, latitud).
- Luna, estrellas, niebla nocturna.
- Persistencia de la hora simulada entre sesiones.
- Sincronización con la hora real del sistema.

Cada uno de ellos, si se aborda, va en su propia spec.
