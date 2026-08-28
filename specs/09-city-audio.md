# SPEC 09 — Audio de ciudad (Ambience y Plazas)

> **Estado:** Implementado
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 08 (Day/Night Cycle, como referencia de patrón de card/settings)
> **Fecha:** 2026-08-29
> **Objetivo:** Añadir una nueva tab "Audio" a la tool con dos cards — Ambience (audio 2D en loop, independiente de la posición) y Plazas (audio 3D posicional, uno por plaza generada) — configurables y aplicadas automáticamente al generar o re-generar la ciudad.

## Scope

**Dentro:**

- **Nueva tab "Audio"** en `CityGeneratorWindow`, al final de la barra de tabs (`City`, `Player`, `Pedestrians`, `Minimap`, **`Audio`**).
- **Card "Ambience"**: audio 2D en loop reproducido siempre, independientemente de la posición de la cámara/jugador.
  - `Enabled` (`bool`, activado por defecto).
  - Lista de entradas (`List<AmbienceClipEntry>`), cada una con:
    - `Clip` (`AudioClip`).
    - `Volume` (`float` 0-1, slider, propio de esa entrada).
  - Por defecto, una única entrada: `DefaultAssets/Audio/city-ambiance.wav` a volumen 1.
  - Todas las entradas de la lista suenan en loop de forma simultánea (capas) desde que arranca la escena, cada una a su propio volumen — no hay selección aleatoria ni rotación entre clips.
- **Card "Plazas"**: audio 3D posicional, uno por cada bloque marcado como plaza en la ciudad generada.
  - `Enabled` (`bool`, activado por defecto).
  - Lista de entradas (`List<PlazaAudioClipEntry>`), vacía por defecto, cada una con:
    - `Clip` (`AudioClip`).
    - `Volume` (`float` 0-1, slider, propio de esa entrada).
    - `Min Distance` (`float`, por defecto 10, propio de esa entrada).
    - `Max Distance` (`float`, por defecto 40, propio de esa entrada).
  - Cada plaza generada recibe un `AudioSource` 3D por cada entrada de la lista (mismos valores de volumen/min/max que la entrada), todos en loop simultáneo — no hay selección aleatoria por plaza.
  - Rolloff fijo `AudioRolloffMode.Logarithmic`, no expuesto en la UI.
- **Generación**: los `AudioSource` de Ambience se crean como hijos directos del `cityRoot` (sin un grupo "Audio" intermedio, siguiendo el patrón de otros componentes no visuales colgados directamente del root); los `AudioSource` de Plazas se crean dentro del grupo `Plaza_{gridX}_{gridY}` ya creado por `CityGeneratorPlazaBuilder` para esa plaza, junto al centerpiece/bancos/vegetación.
- **Re-Build City in Current Scene**: reconcilia los `AudioSource` de Ambience y Plazas contra los ajustes actuales (los añade, actualiza o quita según `Enabled` y la lista de entradas haya cambiado), igual que ya hace con el Day/Night Cycle — no forma parte de "lo que se deja intacto" en un Re-Build.
- **Validación**: si `Ambience.Enabled` o `Plazas.Enabled` está activo y su lista de entradas está vacía, o alguna entrada de la lista tiene `Clip == null`, es un error de validación bloqueante (tab/card en rojo, botones de build deshabilitados), igual que otros campos requeridos de la tool.
- **Documentación**: `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance (para futuras specs):**

- Selección aleatoria o rotación entre clips (por plaza o para Ambience): en esta spec todas las entradas de una lista suenan siempre en simultáneo.
- Fade in/out del audio de plazas al entrar/salir de rango: se deja al rolloff logarítmico estándar de Unity, sin curvas de volumen custom.
- Audio direccional o ambisonics; distintos audios de ambiente según la hora del día (integración con el Day/Night Cycle) o según el clima; audio de otras categorías (tráfico, pisadas, edificios, custom places).
- Requerir que la ciudad tenga al menos una plaza cuando `Plazas.Enabled` está activo: si no hay plazas (`plazaCells` vacío), la ciudad se genera igual, simplemente sin ningún `AudioSource` de plaza.
- Mezcla/Audio Mixer, grupos de mezcla o efectos DSP (reverb, low-pass, etc.) sobre estos `AudioSource`.
- Publicación de una nueva versión del package (bump de `version`/tag): esta spec entrega el código; el release es un paso posterior con `Tools > City Generator > Release`.

## Modelo de datos

```csharp
// Editor/CityGeneratorSettings.cs

// CityGeneratorSettings gana:
public AudioSettings audio = AudioSettings.Default();

[Serializable]
internal struct AudioSettings
{
    public AmbienceSettings ambience;
    public PlazaAudioSettings plazaAudio;

    public static AudioSettings Default() => new AudioSettings
    {
        ambience = AmbienceSettings.Default(),
        plazaAudio = PlazaAudioSettings.Default(),
    };
}

[Serializable]
internal struct AmbienceSettings
{
    [Tooltip("Whether ambience audio plays in the generated city. On by default.")]
    public bool enabled;
    [Tooltip("Clips that loop simultaneously as scene ambience, each at its own volume, regardless of camera position.")]
    public List<AmbienceClipEntry> clips;

    // Default: one entry, DefaultAssets/Audio/city-ambiance.wav at volume 1.
    public static AmbienceSettings Default() => new AmbienceSettings { enabled = true, clips = new List<AmbienceClipEntry> { ... } };
}

[Serializable]
internal struct AmbienceClipEntry
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume;
}

[Serializable]
internal struct PlazaAudioSettings
{
    [Tooltip("Whether each generated plaza gets its own positional audio source. On by default.")]
    public bool enabled;
    [Tooltip("Clips that loop simultaneously at every plaza's position, each at its own volume and hearing range.")]
    public List<PlazaAudioClipEntry> clips;

    // Default: enabled = true, clips = empty list (no default plaza audio asset was provided).
    public static PlazaAudioSettings Default() => new PlazaAudioSettings { enabled = true, clips = new List<PlazaAudioClipEntry>() };
}

[Serializable]
internal struct PlazaAudioClipEntry
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume;
    [Tooltip("AudioSource.minDistance: distance at which attenuation starts.")]
    public float minDistance; // default 10
    [Tooltip("AudioSource.maxDistance: distance at which the clip stops being audible.")]
    public float maxDistance; // default 40
}
```

Notas:

- Sigue el mismo patrón de lista de entradas que `VehicleEntry`/`PedestrianEntry`/`CustomPlaceEntry`: cada fila de la UI edita una entrada completa (clip + sus propios parámetros), no un clip suelto más un slider compartido.
- **`CityGeneratorAudioBuilder`** (nuevo, `Editor/`): `BuildAmbience(Transform cityRoot, AmbienceSettings ambience)` crea un `GameObject` hijo de `cityRoot` por cada entrada (`AudioSource` con `spatialBlend = 0`, `loop = true`, `playOnAwake = true`, `volume = entry.volume`); `BuildPlazaAudio(Transform blockGroup, Vector3 center, PlazaAudioSettings plazaAudio)` crea un `GameObject` hijo de `blockGroup` (el `Plaza_{gridX}_{gridY}` que ya crea `CityGeneratorPlazaBuilder`) por cada entrada, posicionado en `center`, con `AudioSource` 3D (`spatialBlend = 1`, `loop = true`, `playOnAwake = true`, `volume`/`minDistance`/`maxDistance` de la entrada, `rolloffMode = AudioRolloffMode.Logarithmic`).
- **No hace falta lógica de reconciliación tipo Day/Night Cycle**: a diferencia de la Directional Light (que sobrevive a un Re-Build), tanto el audio de Ambience como el de Plazas cuelgan de `cityRoot`/de los grupos de plaza, que ya se reconstruyen enteros en cada Build/Re-Build (igual que Buildings, Props, etc.) — `CityGeneratorAudioBuilder` simplemente se invoca desde `CityGeneratorContentAssembler.Assemble` como un builder más del pipeline, sin necesidad de buscar/actualizar/borrar instancias previas por nombre.
- **`CityGeneratorDefaultAssets.ApplyTo`** añade la carga del clip por defecto de Ambience desde `Packages/com.santiandrade.citygenerator/DefaultAssets/Audio/city-ambiance.wav`, siguiendo el mismo patrón hardcoded-path que el resto de assets por defecto.

## Plan de implementación

1. **Modelo de datos base.** Añadir `AudioSettings`, `AmbienceSettings`, `AmbienceClipEntry`, `PlazaAudioSettings`, `PlazaAudioClipEntry` a `Editor/CityGeneratorSettings.cs`, y el campo `audio` a `CityGeneratorSettings`. El proyecto compila; sin builder ni UI todavía, `audio` no tiene ningún efecto observable.

2. **`Editor/CityGeneratorAudioBuilder.cs`.** Nuevo builder estático con `BuildAmbience(Transform cityRoot, AmbienceSettings ambience)` y `BuildPlazaAudio(Transform blockGroup, Vector3 center, PlazaAudioSettings plazaAudio)`, cada uno creando un `GameObject`+`AudioSource` por entrada de la lista según lo descrito en el modelo de datos. Ninguno se invoca todavía desde el pipeline.

3. **Cablear `CityGeneratorAudioBuilder` en el pipeline.** `CityGeneratorContentAssembler.Assemble` llama a `CityGeneratorAudioBuilder.BuildAmbience(cityRoot, settings.audio.ambience)` cuando `enabled`; `CityGeneratorPlazaBuilder.BuildPlazas` recibe `PlazaAudioSettings` y llama a `CityGeneratorAudioBuilder.BuildPlazaAudio(blockGroup, block.center, plazaAudioSettings)` para cada bloque de plaza, cuando `enabled`. Generar una ciudad ya reproduce el audio de ambiente y, si hay plazas, el audio posicional de cada una.

4. **`CityGeneratorDefaultAssets.ApplyTo`.** Añade la entrada por defecto de Ambience (`city-ambiance.wav`, volumen 1) cargada desde `DefaultAssets/Audio/`. Una ventana recién abierta u tras "Reset to Defaults" ya trae el audio de ambiente listo para generar sin configuración manual.

5. **Validación.** `CityGeneratorValidator` (y `ValidateDetailed`) añade las comprobaciones: `audio.ambience.enabled` con `clips` vacía o con algún `clip == null` es error; lo mismo para `audio.plazaAudio`. Generar con la lista vacía y `Enabled` activo queda bloqueado, igual que otros campos requeridos.

6. **Nueva tab "Audio" y sus dos cards en `CityGeneratorWindow`.** Card "Ambience" (`Enabled`, lista de `AmbienceClipEntry` con `Clip`+`Volume` por fila) y card "Plazas" (`Enabled`, lista de `PlazaAudioClipEntry` con `Clip`+`Volume`+`Min Distance`+`Max Distance` por fila), con badge resumiendo cada card (p. ej. número de clips o "Off") igual que el resto de tabs. Las filas se construyen con controles planos (no `PropertyField`) si se añaden/eliminan dinámicamente, siguiendo el patrón ya establecido para listas post-`Bind()`.

7. **Verificación manual.** Generar la escena de test (`Assets/Scenes/City.unity`) con Ambience y Plazas activos (varias entradas en cada lista, con plazas configuradas en `plazaCells`), entrar en Play y comprobar: el ambiente suena siempre en loop a los volúmenes configurados; el audio de cada plaza solo se oye al acercarse/entrar en ella, con la atenuación esperada según `Min Distance`/`Max Distance` de cada entrada. Repetir con "Re-Build City in Current Scene" para confirmar que el audio se regenera correctamente.

8. **Documentación.** `CHANGELOG.md` (`## [Unreleased]`): añadir la tab de Audio (Ambience y Plazas).

## Criterios de aceptación

- [x] `CityGeneratorSettings` compila con el campo `audio: AudioSettings` tal como se definió, con `ambience.enabled = true`, `ambience.clips` con una entrada por defecto (`city-ambiance.wav`, volumen 1), `plazaAudio.enabled = true` y `plazaAudio.clips` vacía. Verificado con `ResetToDefaults` sobre la ventana real: `ambience.clips[0].clip = city-ambiance`, `plazaAudio.clips.Count = 0`.
- [x] `Editor/CityGeneratorAudioBuilder.cs` existe con `BuildAmbience` y `BuildPlazaAudio`, cada uno creando un `GameObject`+`AudioSource` por entrada de la lista correspondiente, con los parámetros (volumen, spatial blend, loop, min/max distance, rolloff) descritos en el modelo de datos.
- [x] Generar una ciudad nueva ("Build City in New Scene") con `ambience.enabled = true` y al menos una entrada crea un `AudioSource` 2D en loop por entrada, hijo de `cityRoot`, reproduciéndose en Play Mode desde el inicio, al volumen configurado por entrada. Verificado vía `CityGeneratorAudioBuilderTests`: `spatialBlend=0`, `loop=true`, `playOnAwake=true`, `volume` de la entrada, padre = `cityRoot`.
- [x] Generar una ciudad nueva con `plazaAudio.enabled = true`, al menos una entrada y al menos una plaza en `plazaCells` crea, dentro de cada `Plaza_{gridX}_{gridY}`, un `AudioSource` 3D en loop por entrada, centrado en el bloque, con `minDistance`/`maxDistance`/`volume` propios de esa entrada y `rolloffMode = Logarithmic`. Verificado vía test: todos los valores coinciden, posición = centro del bloque.
- [x] Generar una ciudad nueva con `plazaAudio.enabled = true` pero sin ninguna plaza en `plazaCells` no falla ni genera ningún `AudioSource` de plaza. Verificado: `Assert.DoesNotThrow` + grupo `Plaza` con 0 hijos.
- [x] En Play Mode, alejar la cámara/jugador de una plaza atenúa su audio hasta dejar de oírse pasado `maxDistance`; acercarse vuelve a subirlo, sin afectar al audio de Ambience (que suena igual en toda la ciudad). Es el comportamiento estándar de Unity para `AudioRolloffMode.Logarithmic` sobre un `AudioSource` 3D con `minDistance`/`maxDistance` propios; no hay lógica de atenuación propia que probar más allá de que esos valores se apliquen correctamente (verificado arriba).
- [x] Activar `Ambience.Enabled` o `Plazas.Enabled` con la lista de entradas vacía, o con alguna entrada sin `Clip` asignado, bloquea ambos botones de "Build" y marca en rojo la card/tab correspondiente, igual que otros campos requeridos de la tool. Verificado sobre la ventana real vía reflexión: con `Plazas.clips` vacía, `plazaAudioCard` tiene la clase `cg-card--error` y `buildNewSceneButton.enabledSelf = false`; al desactivar `Plazas.Enabled`, ambos se limpian.
- [x] `CityGeneratorDefaultAssets.ApplyTo` deja `audio.ambience.clips` con la entrada por defecto (`DefaultAssets/Audio/city-ambiance.wav`) al abrir la ventana por primera vez o tras "Reset to Defaults". Verificado sobre la ventana real.
- [x] La nueva tab "Audio" aparece al final de la barra de tabs, con las cards "Ambience" y "Plazas" editables (añadir/quitar entradas, editar `Clip`/`Volume`/`Min Distance`/`Max Distance` por fila). `tabBar.AddTab` se registra en orden City/Player/Pedestrians/Minimap/Audio; ventana abierta sin excepciones y cards creadas/con badge correcto verificado sobre la instancia real.
- [x] "Re-Build City in Current Scene" sobre una escena ya generada regenera correctamente el audio de Ambience y Plazas según los ajustes actuales (sin necesitar lógica de reconciliación especial, al formar parte del `cityRoot` reconstruido). `RebuildInActiveScene` llama al mismo `CityGeneratorContentAssembler.Assemble`/`CityGeneratorPlazaBuilder.BuildPlazas` que ya incluyen `CityGeneratorAudioBuilder`.
- [x] La suite EditMode/PlayMode/Performance de `Assets/Tests/` sigue pasando en su totalidad. 66 EditMode + 21 PlayMode/Performance = 87/87.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` describiendo la tab de Audio.

## Decisiones tomadas y descartadas

- **Tab propia "Audio", no una card más en la tab City.** A diferencia del Day/Night Cycle (una sola card de 4-5 parámetros), aquí hay dos cards con listas de entradas cada una — volumen suficiente para justificar una tab dedicada, siguiendo el mismo criterio que separó Minimap en su propia tab.
- **Lista de entradas (`clip` + parámetros propios) en vez de una lista de clips más un volumen/rango compartido.** Decisión explícita del usuario tras la primera propuesta (una lista simple + un slider de volumen global): da control independiente por audio, siguiendo el patrón ya usado por `VehicleEntry`/`PedestrianEntry`/`CustomPlaceEntry`.
- **Todas las entradas de una lista suenan siempre en simultáneo (capas), sin selección aleatoria ni rotación.** Más simple de razonar y de depurar (lo que se configura es exactamente lo que suena) y encaja con el caso de uso descrito (capas de ambiente tipo tráfico + pájaros); una selección aleatoria por plaza/ciudad queda como posible spec futura si hace falta variedad entre plazas.
- **Ambience como `GameObject`s hijos directos de `cityRoot`, sin un grupo "Audio" intermedio; Plazas dentro del `Plaza_{gridX}_{gridY}` ya existente.** Evita una capa de jerarquía extra para Ambience (pocas entradas esperadas) y reutiliza el grupo que `CityGeneratorPlazaBuilder` ya crea por plaza en vez de duplicar la agrupación por bloque.
- **Sin lógica de reconciliación por nombre en Re-Build (a diferencia del Day/Night Cycle).** El audio vive enteramente dentro de `cityRoot`/los grupos de plaza, que el Re-Build transaccional ya reconstruye de cero (igual que Buildings, Props, etc.); solo la Directional Light necesita ese patrón porque sobrevive al Re-Build.
- **Rolloff fijo `Logarithmic`, no expuesto en la UI.** Es el comportamiento por defecto de Unity para audio 3D realista y evita una tercera dimensión de configuración (lineal vs. logarítmico) que el usuario no pidió; `minDistance`/`maxDistance` por entrada ya cubren la duda original sobre el rango.
- **Sin plaza configurada pero `Plazas.Enabled = true` no es error de validación.** La ciudad se genera igual, simplemente sin `AudioSource` de plaza — tratarlo como error acoplaría innecesariamente dos configuraciones independientes (layout de plazas vs. audio).
- **Volumen como slider 0-1 (`AudioSource.volume` nativo), no un campo libre para amplificar por encima de 1.** Consistente con otros sliders normalizados de la tool y evita clipping accidental.
- **`Min Distance`/`Max Distance` por defecto 10/40.** Calculados sobre el tamaño real del bloque (46 m sobre rejilla de 56 m, `CityGeneratorConstants`): la atenuación empieza dentro del propio bloque de la plaza y el audio deja de oírse justo antes del bloque adyacente.
- **No:** requerir una `AudioListener` adicional o gestionar el existente. La cámara generada (`CreateMainCamera`) ya añade un `AudioListener`; esta spec no lo toca.

## Riesgos identificados

- **Tamaño del package.** `city-ambiance.wav` pasa a formar parte de `DefaultAssets/`, que se distribuye dentro del package instalable — un WAV sin comprimir puede pesar bastante más que los demás assets por defecto (prefabs, texturas). Mitigación: revisar en el paso 4 del plan el tamaño real del fichero y, si es significativo, ajustar sus `AudioImporter` settings (compresión, mono) antes de darlo por definitivo — no se re-graba el propio audio, ya lo aportó el usuario.
- **Número de `AudioSource` 3D simultáneos con muchas plazas y varias entradas por plaza.** Una rejilla grande con muchas plazas, cada una con varias entradas de audio, multiplica el número de `AudioSource` activos en todo momento (todas en loop desde el inicio, no solo la más cercana). Mitigación: fuera de alcance de esta spec optimizarlo (p. ej. activar/desactivar por proximidad); si en QA resulta un problema real de rendimiento o de audio saturado, es candidato a una spec futura.
