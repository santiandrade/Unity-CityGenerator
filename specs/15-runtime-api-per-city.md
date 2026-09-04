# SPEC 15 — Runtime API por instancia de ciudad

> **Estado:** Implementado
> **Depende de:** SPEC 04 (managers no-singleton, cuya regla esta spec extiende a la API), SPEC 07 (Minimap HUD), SPEC 13 (Free Camera), SPEC 14 (Runtime API, cuya superficie estática esta spec sustituye)
> **Fecha:** 2026-09-04
> **Objetivo:** Sustituir la caché estática sin invalidar de `CityGeneratorAPI` por un handle explícito por ciudad (`CityGeneratorCity`) resuelto contra un registro que `CityGeneratorInfo` mantiene en `OnEnable`/`OnDisable`, eliminando la API estática de v2.10 como breaking change, y cubrir con tests de comportamiento la API, el Minimap HUD y la Free Camera.

## Por qué existe esta spec

La SPEC 14 entregó `CityGeneratorAPI` con una decisión explícita: resolver `CityGeneratorInfo` una sola vez con `FindFirstObjectByType` y cachearlo en un campo `static`, sin invalidarlo nunca. Esa misma spec dejó anotado el límite en su sección de decisiones — *"si en el futuro se soporta regenerar en runtime, esa spec futura tendrá que revisar esta decisión"* — y en sus riesgos. Esta es esa spec.

El problema no es teórico y no necesita dos ciudades para aparecer:

- El campo es `static`, así que sobrevive a los cambios de escena y, con *Reload Domain* desactivado, también a entrar y salir de Play Mode.
- `FindFirstObjectByType` devuelve *la primera que Unity encuentre*, y ese orden no está definido. Con una ciudad regenerada, o con dos escenas cargadas aditivamente durante una transición, la API puede quedarse contestando sobre la ciudad equivocada sin lanzar ninguna excepción ni escribir ningún warning.

Además contradice una regla que el propio package ya se había impuesto. La SPEC 04 eliminó los singletons de `TrafficManager` y `PedestrianManager` para que varias ciudades pudieran coexistir, y el código lo documenta como intención deliberada:

> *"Deliberately not a FindAnyObjectByType lookup: with multiple independent cities/networks in the same scene, that could resolve a different city's network and size the pool for the wrong graph."* — `Runtime/PedestrianManager.cs`

Los agentes cumplen esa regla: los builders serializan la referencia a la red en cada `CarAgent`/`PedestrianAgent`, y `CarAgent` resuelve su manager vía `TrafficNetwork.Manager`, nunca por un estático global. La API es hoy la única pieza del package que vuelve a suponer "una ciudad y punto". Esta spec la alinea con el resto.

La corrección se hace **ahora** por una razón de ventana temporal, no de urgencia técnica: la API se publicó en v2.10 y todavía no tiene integradores que proteger. Cambiar su forma hoy cuesta una entrada de CHANGELOG; dentro de varias releases costaría un ciclo de deprecación completo.

## Scope

**Dentro:**

- **`CityGeneratorCity`** (nuevo `readonly struct`, `Packages/com.santiandrade.citygenerator/Runtime/API/`), envoltorio inmutable sobre una referencia a `CityGeneratorInfo`. Expone `IsValid` (`false` una vez destruido el `CityGeneratorInfo` envuelto), `IsActive` (`false` además cuando su raíz está desactivada), `Scene` y los seis módulos de la SPEC 14 como propiedades: `City`, `Player`, `Traffic`, `Pedestrians`, `Minimap`, `Audio`. Cada módulo es a su vez un `readonly struct` anidado que envuelve la misma referencia — no se copia ningún dato, así que un handle nunca puede quedar desincronizado de la ciudad.
- **Estilo de la superficie:** propiedades para lectura (`city.City.BuildingCount`, `city.Traffic.VehicleCount`) y **métodos para toda mutación** (`city.City.SetHour(12f)`, `city.Minimap.SetViewRadiusMeters(120f)`). El conjunto de datos y de mutaciones permitidas es **exactamente el mismo** que fijó la SPEC 14: esta spec cambia cómo se resuelve la ciudad, no qué se puede consultar ni tocar.
- **`CityGeneratorAPI` reescrita** como punto de resolución, sin estado cacheado de ciudad:
  - `Default` → `CityGeneratorCity?`: la ciudad registrada si hay exactamente una; `null` si hay cero o más de una.
  - `All` → `IReadOnlyList<CityGeneratorCity>`, en orden de registro.
  - `InScene(Scene scene)` → `CityGeneratorCity?`.
  - `For(CityGeneratorInfo info)` → `CityGeneratorCity?`.
  - `Count` → número de ciudades registradas.
- **Registro por ciclo de vida en `CityGeneratorInfo`**: alta en `OnEnable`, baja en `OnDisable`, siguiendo el mismo patrón que `TrafficManager`/`PedestrianManager` usan para sus agentes. Sin ninguna búsqueda global en la API.
- **Eliminación completa de la superficie estática de v2.10** (breaking change): desaparecen `CityGeneratorAPI.IsCityAvailable` y los seis módulos estáticos (`CityGeneratorAPI.City.GetBuildingCount()` y equivalentes), junto con el campo `cachedInfo` y su resolución vía `FindFirstObjectByType`.
- **Tests de comportamiento** en `Assets/Tests/PlayMode/`:
  - `CityGeneratorAPITests.cs` — ciclo de vida y resolución: cero, una y dos ciudades; `Default` ambiguo; `InScene`/`For` resolviendo cada una; ciudad destruida; root desactivado; cambio de escena. Las ciudades del test se construyen como `CityGeneratorInfo` sintéticos (un `GameObject` con el componente y los campos rellenados a mano), no generando ciudades reales.
  - `MinimapHUDTests.cs` y `FreeCameraControllerTests.cs` — cobertura de caracterización de las dos features que hoy no tienen ninguna, sin modificar su comportamiento.
  - Añadir `UnityEngine.UI` a las `references` de `Assets/Tests/PlayMode/CityGenerator.Tests.PlayMode.asmdef`, que hoy no está y hace falta para instanciar/inspeccionar el HUD (`MinimapHUD` usa `RawImage`).
- **Documentación**: reescritura de `docs/api-reference.md` y `docs/api-reference.es.md` con la nueva forma y una nota de migración desde v2.10; entrada `## [Unreleased]` en `Packages/com.santiandrade.citygenerator/CHANGELOG.md` marcada como **BREAKING**; actualización de la sección `CityGeneratorInfo`/`CityGeneratorAPI` en `docs/architecture/runtime-and-traffic.md` describiendo el registro por ciclo de vida.

**Fuera de alcance (para futuras specs):**

- **Dos ciudades en posiciones físicas distintas.** Es el objetivo real de una SPEC futura y requiere trabajo que esta spec no toca: la generación produce coordenadas de mundo absolutas y no relativas al root (`TrafficNetwork.IntersectionPosition` construye un `Vector3` directo desde los ejes, sin `TransformPoint`), así que mover el `CityGeneratorRoot` mueve la geometría pero no el grafo. Esta spec deja la API preparada para varias ciudades; el resto del sistema todavía no lo está, y la documentación debe decirlo explícitamente para no prometer lo que no hay.
- **Eliminar las búsquedas globales de la generación**: los `GameObject.Find` de `CityGeneratorSceneBuilder` (Player, Main Camera, Directional Light, Minimap HUD), el `FindObjectsByType<TrafficLight>` de `TrafficNetwork.AssignTrafficLights`, el `FindObjectsByType<TrafficLightIntersection>` de `PedestrianNetwork.Build` y los `FindAnyObjectByType` de `MinimapHUD`. Ninguno afecta a la corrección de la API; todos pertenecen al problema de multi-ciudad real.
- **Que la tool permita generar dos ciudades en una misma escena.** Hoy `RebuildInActiveScene` destruye el primer root con `CityGeneratorRoot` que encuentra, y esta spec no lo cambia.
- **Ciudades con el root desactivado visibles en `All`.** El registro es `OnEnable`/`OnDisable` puro; una ciudad desactivada desaparece de `All` y su handle pasa a `IsActive == false`. Para consultarla hay que haberse guardado su `CityGeneratorInfo` y usar `For(info)`.
- **Sistema de eventos/callbacks** (`OnCityRegistered`, `OnCityDestroyed`, `OnHourChanged`). La API sigue siendo de consulta directa (pull), igual que decidió la SPEC 14.
- **Ampliar el conjunto de datos o de mutaciones de la API.** Ni nuevos getters, ni setters sobre Traffic/Pedestrians/Audio/Player, ni nada que regenere o re-espawnee contenido.
- **Cambios de comportamiento en `MinimapHUD` o `FreeCameraController`.** Sus tests son de caracterización: describen lo que ya hacen. Si alguno resulta no ser testeable sin abrir superficie pública nueva, se documenta como decisión en vez de refactorizar el componente.
- **Subir la versión de `package.json` y publicar el release.** La spec entrega código, tests y documentación; el número de versión lo pone el flujo de release.
- **CI en batchmode.** Es el otro P0 del informe técnico y merece su propia spec.

## Modelo de datos

### `CityGeneratorInfo` — solo se le añade el registro

El componente de la SPEC 14 no cambia ni un campo. Únicamente se le añade el alta y baja en el registro, con el mismo patrón de ciclo de vida que `TrafficManager`/`PedestrianManager` usan para sus agentes:

```csharp
// Runtime/CityGeneratorInfo.cs — añadido sobre el componente existente
public sealed class CityGeneratorInfo : MonoBehaviour
{
    // ... todos los campos de la SPEC 14, sin cambios ...

    private void OnEnable() => CityGeneratorAPI.Register(this);
    private void OnDisable() => CityGeneratorAPI.Unregister(this);
}
```

### `CityGeneratorCity` — el handle

```csharp
// Runtime/API/CityGeneratorCity.cs — nuevo fichero, namespace CityGenerator.Runtime

/// <summary>
/// Immutable handle to one generated city. Wraps a CityGeneratorInfo reference; holds no
/// copied data, so a handle can never go stale relative to its city.
/// </summary>
public readonly struct CityGeneratorCity : IEquatable<CityGeneratorCity>
{
    private readonly CityGeneratorInfo info;

    internal CityGeneratorCity(CityGeneratorInfo info);

    /// <summary>False once the underlying city has been destroyed.</summary>
    public bool IsValid { get; }           // info != null

    /// <summary>False when the city's root is deactivated — it is then absent from All/Default too.</summary>
    public bool IsActive { get; }          // IsValid && info.isActiveAndEnabled

    public Scene Scene { get; }            // default(Scene) when !IsValid
    public CityGeneratorInfo Info { get; } // null when !IsValid — escape hatch, keep it a handle away

    public CityModule City { get; }
    public PlayerModule Player { get; }
    public TrafficModule Traffic { get; }
    public PedestriansModule Pedestrians { get; }
    public MinimapModule Minimap { get; }
    public AudioModule Audio { get; }
}
```

### Los seis módulos

Structs anidados en `CityGeneratorCity`, cada uno envolviendo la misma referencia. Mismo conjunto de datos y de mutaciones que la SPEC 14; solo cambia la forma.

```csharp
public readonly struct CityModule
{
    public bool IsCustomGrid { get; }
    public Vector2Int GridSize { get; }
    public int BlockCount { get; }
    public int BuildingCount { get; }
    public int PlazaCount { get; }
    public int CustomPlaceCount { get; }
    public int LampCount { get; }
    public int BinCount { get; }
    public int StreetTreeCount { get; }
    public int TrafficLightCount { get; }
    public bool IsSeeded { get; }
    public int Seed { get; }

    public bool IsDayNightEnabled { get; }
    public float CurrentHour { get; }
    public void SetDayNightEnabled(bool enabled);
    public void SetHour(float hour);            // moves the sun, like the Editor preview
}

public readonly struct PlayerModule
{
    public bool IsEnabled { get; }
    public Vector3 Position { get; }
    public bool IsFreeViewActive { get; }
}

public readonly struct TrafficModule
{
    public bool IsEnabled { get; }
    public int VehicleCount { get; }            // live count, via TrafficManager.AgentCount
}

public readonly struct PedestriansModule
{
    public bool IsEnabled { get; }
    public int Count { get; }                   // live count, via PedestrianManager.AgentCount
    public int CustomCount { get; }
}

public readonly struct MinimapModule
{
    public bool IsEnabled { get; }
    public int PointOfInterestCount { get; }
    public float ViewRadiusMeters { get; }
    public bool IsVisible { get; }
    public void SetViewRadiusMeters(float meters);
    public void SetVisible(bool visible);
}

public readonly struct AudioModule
{
    public bool IsAmbienceEnabled { get; }
    public int AmbienceClipCount { get; }
    public bool IsPlazaAudioEnabled { get; }
    public int PlazaAudioSourceCount { get; }
}
```

**Lectura como propiedad, escritura siempre como método.** No es una preferencia de estilo: los módulos son `readonly struct` devueltos **por valor** desde una propiedad, así que `city.Minimap.ViewRadiusMeters = 120f` no compilaría (error CS1612, *"cannot modify the return value because it is not a variable"*), aunque por dentro el setter escribiese en el `MinimapHUD`. Un setter de propiedad solo sería posible haciendo los módulos `class`, lo que reintroduciría una asignación en heap por acceso.

### `CityGeneratorAPI` — resolución, sin estado cacheado de ciudad

```csharp
// Runtime/API/CityGeneratorAPI.cs — reescrito, namespace CityGenerator.Runtime

public static class CityGeneratorAPI
{
    private static readonly List<CityGeneratorInfo> registered = new();

    /// <summary>The one registered city, or null when there are zero or more than one.</summary>
    public static CityGeneratorCity? Default { get; }

    public static IReadOnlyList<CityGeneratorCity> All { get; }
    public static int Count { get; }

    public static CityGeneratorCity? InScene(Scene scene);
    public static CityGeneratorCity? For(CityGeneratorInfo info);

    internal static void Register(CityGeneratorInfo info);
    internal static void Unregister(CityGeneratorInfo info);
}
```

Convenciones:

- **Sin ciudad, sin excepción.** Todo getter de un handle con `IsValid == false` devuelve el valor por defecto seguro (`0` / `false` / `Vector2Int.zero` / `Vector3.zero` / `null`) y todo setter es un no-op silencioso. Es la misma garantía que fijó la SPEC 14, movida del nivel de la API estática al nivel del handle.
- **`Default` es `CityGeneratorCity?`**, así que el patrón de una línea es `int n = CityGeneratorAPI.Default?.City.BuildingCount ?? 0;` — el `?.` de `Nullable<T>` encadena sobre los módulos sin `.Value`.
- **`Default` ambiguo escribe un warning una sola vez por sesión**, no en cada llamada: un `Default` que devuelve `null` porque hay dos ciudades es indistinguible, desde el código llamante, de uno que devuelve `null` porque no hay ninguna, y sin ese aviso el integrador no tiene ninguna pista de qué le pasa.
- **`For(info)` resuelve aunque la ciudad esté desactivada** (devuelve `null` solo si `info` es `null` o está destruido), a diferencia de `All`/`Default`/`InScene`, que solo ven ciudades activas. Es la vía para consultar una ciudad precargada y desactivada.
- **El registro es `static` pero no cachea nada entre ciudades**: solo contiene los `CityGeneratorInfo` cuyo `OnEnable` corrió y cuyo `OnDisable` no. Un domain reload lo vacía y los `OnEnable` de la recarga lo repueblan, sin quedar nunca una referencia muerta.
- **`All` devuelve una vista sobre la lista interna**, no una copia; no asigna al consultarse cada frame.

## Plan de implementación

Cada paso deja el proyecto compilando y es commiteable por sí solo. Los pasos 1 a 5 son puramente aditivos: la API estática de v2.10 sigue viva y funcionando durante todos ellos, y solo desaparece en el paso 6.

1. **Registro por ciclo de vida.** Añadir `OnEnable`/`OnDisable` a `Runtime/CityGeneratorInfo.cs` y, en `Runtime/API/CityGeneratorAPI.cs`, la lista estática `registered` con `Register`/`Unregister` internos y las propiedades `All`/`Count` — que de momento devuelven `CityGeneratorInfo`, no handles todavía. La API estática existente se deja intacta. Test manual: entrar en Play sobre la escena de test y comprobar en la consola que `CityGeneratorAPI.Count` es 1; desactivar el GameObject `City` y comprobar que pasa a 0, reactivarlo y que vuelve a 1.

2. **El handle, sin módulos.** Nuevo `Runtime/API/CityGeneratorCity.cs` con el struct, su constructor interno, `IsValid`, `IsActive`, `Scene`, `Info` y la implementación de `IEquatable`. Cambiar `All` para que devuelva `IReadOnlyList<CityGeneratorCity>`. Test manual: `CityGeneratorAPI.All[0].IsValid` es `true` en Play; destruir el GameObject `City` y comprobar que un handle guardado antes pasa a `IsValid == false` sin lanzar.

3. **Módulos `City` y `Player`.** Los dos structs anidados completos, leyendo del `CityGeneratorInfo` envuelto, con la garantía de valor por defecto cuando `!IsValid`. Test manual: los valores de `City` coinciden con los que muestra el Inspector de `CityGeneratorInfo`; `City.SetHour(12f)` mueve la luz igual que el preview del Editor; `Player.Position` cambia al mover al jugador.

4. **Módulos `Traffic`, `Pedestrians`, `Minimap` y `Audio`.** Test manual: `Traffic.VehicleCount` y `Pedestrians.Count` coinciden con lo configurado (o menos, si algún agente se autodesactivó); `Minimap.SetVisible(false)` oculta el HUD y `SetViewRadiusMeters` cambia el radio en el siguiente frame.

5. **Resolución: `Default`, `InScene`, `For`.** Incluye el warning único por sesión cuando `Default` es ambiguo. Test manual: con una ciudad, `Default` la resuelve; duplicar el GameObject `City` en la escena y comprobar que `Default` pasa a `null` con un único warning en consola, que `All` tiene dos entradas y que `InScene` y `For` siguen resolviendo cada una por separado.

6. **Eliminar la superficie estática de v2.10.** Borrar `IsCityAvailable`, los seis módulos estáticos, el campo `cachedInfo` y su `FindFirstObjectByType`. Actualizar de paso los dos comentarios del package que nombran la API antigua (`Runtime/MinimapHUD.cs`, que cita `CityGeneratorAPI.Minimap.SetViewRadiusMeters`, y `Editor/CityGeneratorInfoEditor.cs`). Ningún código del repositorio consume la API, así que este paso no rompe ninguna compilación interna. Test manual: el proyecto compila sin errores y la ciudad de test se genera y se juega igual que antes.

7. **Tests de la API.** Nuevo `Assets/Tests/PlayMode/CityGeneratorAPITests.cs`, con `CityGeneratorInfo` sintéticos creados y destruidos por el propio test: cero ciudades, una, dos; `Default` ambiguo; `InScene` y `For` por ciudad; handle de ciudad destruida; root desactivado (fuera de `All`, `For` sigue resolviendo); registro limpio entre tests.

8. **Preparar el asmdef y testear el Minimap HUD.** Añadir `UnityEngine.UI` a las `references` de `Assets/Tests/PlayMode/CityGenerator.Tests.PlayMode.asmdef` y crear `MinimapHUDTests.cs` con la cobertura de caracterización del HUD.

9. **Tests de la Free Camera.** `FreeCameraControllerTests.cs`, cubriendo el estado `IsActive` y la convivencia con `ThirdPersonCamera`.

10. **Documentación.** Reescribir `docs/api-reference.md` y `docs/api-reference.es.md` con la nueva forma, incluyendo una tabla de migración desde v2.10 y una nota explícita de que varias ciudades son consultables pero todavía no coexisten en posiciones distintas. Entrada `## [Unreleased]` marcada como **BREAKING** en `Packages/com.santiandrade.citygenerator/CHANGELOG.md`. Actualizar la sección de `CityGeneratorInfo`/`CityGeneratorAPI` en `docs/architecture/runtime-and-traffic.md` para describir el registro por ciclo de vida en lugar de la caché estática.

## Criterios de aceptación

**Registro y ciclo de vida**

- [x] `CityGeneratorAPI.Count` es 1 tras entrar en Play sobre una escena con una ciudad generada, y 0 en una escena sin ninguna.
- [x] Desactivar el GameObject raíz de una ciudad la saca de `All`, `Default` e `InScene`; reactivarlo la devuelve a los tres.
- [x] Destruir una ciudad hace que un handle obtenido antes pase a `IsValid == false`, y todos sus getters devuelven el valor por defecto sin lanzar ninguna excepción.
- [x] Tras un cambio de escena que descarga la ciudad, `Count` es 0 y `Default` es `null` — sin necesidad de que ningún código llame a nada para invalidar.
- [x] `CityGeneratorAPI` no contiene ninguna llamada a `FindFirstObjectByType`, `FindAnyObjectByType` ni `FindObjectsByType`.

**Resolución**

- [x] Con exactamente una ciudad registrada, `Default` la devuelve.
- [x] Con cero ciudades, `Default` es `null`.
- [x] Con dos ciudades registradas, `Default` es `null` y se escribe exactamente un warning en consola, no uno por llamada.
- [x] Con dos ciudades en escenas distintas, `InScene(a)` e `InScene(b)` devuelven cada una la suya, y `All` tiene dos entradas.
- [x] `For(info)` devuelve el handle de esa ciudad incluso con su raíz desactivada, y `null` si `info` es `null` o está destruido.
- [x] Dos handles a la misma ciudad comparan iguales con `==` y con `Equals`.

**Superficie de datos**

- [x] Todos los datos y mutaciones de la SPEC 14 siguen disponibles a través del handle, con los mismos valores: los seis módulos (`City`, `Player`, `Traffic`, `Pedestrians`, `Minimap`, `Audio`) exponen el mismo conjunto que documentaba `docs/api-reference.md` antes de esta spec.
- [x] `city.City.SetHour(12f)` reposiciona la luz direccional instantáneamente, igual que el preview del Editor.
- [x] `city.City.SetDayNightEnabled(false)` congela el ciclo en la hora actual.
- [x] `city.Minimap.SetVisible(false)` oculta el HUD en Play y `SetVisible(true)` lo restaura.
- [x] `city.Minimap.SetViewRadiusMeters(120f)` cambia el radio visible en el siguiente frame.
- [x] `city.Traffic.VehicleCount` y `city.Pedestrians.Count` reflejan el número vivo de agentes, no un conteo congelado en la generación.
- [x] `CityGeneratorAPI.Default?.City.BuildingCount ?? 0` compila y devuelve 0 sin lanzar en una escena sin ciudad.

**Breaking change**

- [x] `CityGeneratorAPI.IsCityAvailable` y los seis módulos estáticos ya no existen en el código, y el proyecto compila sin errores ni warnings nuevos.
- [x] Ningún comentario del package sigue citando la API estática eliminada.

**Tests**

- [x] `Assets/Tests/PlayMode/CityGeneratorAPITests.cs` existe y todos sus casos pasan en el Test Runner.
- [x] Los tests de la API construyen sus ciudades como `CityGeneratorInfo` sintéticos, sin invocar el pipeline de generación.
- [x] `CityGeneratorAPI.Count` es 0 al terminar cada test de la API: ningún caso deja ciudades registradas que contaminen el siguiente.
- [x] `MinimapHUDTests.cs` y `FreeCameraControllerTests.cs` existen y pasan.
- [x] `Assets/Tests/PlayMode/CityGenerator.Tests.PlayMode.asmdef` referencia `UnityEngine.UI`.
- [x] La suite completa (EditMode, PlayMode y Performance) pasa igual que antes de esta spec: ningún test existente se rompe.

**Comportamiento existente**

- [x] Generar la ciudad de test con los valores por defecto produce el mismo resultado que antes de esta spec: misma semilla, misma ciudad.
- [x] Build en escena nueva y Re-Build en escena activa siguen funcionando, y en ambos casos la ciudad resultante queda registrada y resoluble por `Default`.

**Documentación**

- [x] `docs/api-reference.md` y `docs/api-reference.es.md` documentan la nueva forma e incluyen una tabla de migración desde v2.10.
- [x] Ambos documentos dicen explícitamente que varias ciudades son consultables pero que la tool todavía no soporta dos ciudades en posiciones físicas distintas.
- [x] `CHANGELOG.md` tiene una entrada en `## [Unreleased]` marcada como BREAKING.
- [x] `docs/architecture/runtime-and-traffic.md` describe el registro por ciclo de vida en lugar de la caché estática.
- [x] `package.json` sigue en la versión que tenía antes de esta spec.

## Decisiones tomadas y descartadas

**Forma del handle**

- **`readonly struct` que envuelve una referencia a `CityGeneratorInfo`**, en vez de una `class` o de exponer la API desde el propio `CityGeneratorInfo`. No asigna en heap, así que consultarlo cada frame no genera basura de GC, y al no copiar ningún dato un handle nunca puede quedar desincronizado de su ciudad.
- **Descartado: exponer los módulos desde `CityGeneratorInfo`** (que el handle *sea* el componente). Mezclaría los campos serializados del componente con la superficie pública de la API y se alejaría más de la forma de la v2.10.
- **`CityGeneratorCity?` (nullable) para expresar la ausencia**, en vez de devolver siempre un handle con `IsValid == false`. Decisión explícita del usuario. Es más explícito y el compilador lo detecta con nullable habilitado; el coste aceptado es que debilita la garantía "nunca lanza" de la SPEC 14 — un `.Value` sobre `null` sí lanza — mitigado porque el `?.` de `Nullable<T>` encadena sobre los módulos sin necesidad de `.Value`.
- **Descartado: `TryGetDefault(out city)`.** Es el patrón que la SPEC 14 ya descartó explícitamente por verboso; no hay razón para reintroducirlo ahora.
- **`IsValid` e `IsActive` como dos propiedades separadas**, en vez de una sola que mezclara "destruido" y "desactivado". Sin esa separación, `For(info)` sobre una ciudad precargada y desactivada devolvería ceros y no serviría para su único caso de uso.
- **Seis módulos simétricos (`city.City.BuildingCount`), sin aplanar el módulo `City`.** Decisión explícita del usuario: conserva la correspondencia 1-a-1 con las tabs de la ventana que fijó la SPEC 14, a cambio de un `city.City` que se lee algo redundante.
- **Lectura como propiedad, escritura siempre como método.** No es preferencia de estilo: los módulos son `readonly struct` devueltos por valor desde una propiedad, así que una propiedad settable (`city.Minimap.ViewRadiusMeters = 120f`) no compilaría — error CS1612, *"cannot modify the return value because it is not a variable"*. La alternativa sería hacer los módulos `class`, reintroduciendo una asignación en heap por acceso.
- **`Info` expuesto como escotilla de escape**, aceptando que permite saltarse la API y escribir directamente en los campos serializados. Sin él no hay forma de guardarse una referencia para un `For(info)` posterior, que es lo único que hace consultable una ciudad desactivada.

**Resolución**

- **`Default` devuelve `null` cuando hay más de una ciudad**, en vez de la primera registrada. Falla de forma ruidosa en lugar de adivinar, que es exactamente el defecto que esta spec corrige; devolver "la primera" solo cambiaría un orden indefinido por otro definido, sin arreglar que la respuesta puede no ser la ciudad que el llamante quería.
- **Descartado: `Default` como "la ciudad de la escena activa".** La escena activa no es necesariamente la que el jugador está jugando, así que seguiría siendo una heurística.
- **Un único warning por sesión cuando `Default` es ambiguo**, no uno por llamada. Desde el código llamante, un `null` por ambigüedad es indistinguible de un `null` por ausencia; sin ese aviso el integrador no tiene ninguna pista, y con uno por llamada la consola sería inusable en un getter consultado cada frame.
- **`For(info)` resuelve ciudades desactivadas, el resto de vías no.** Excepción deliberada y única a la regla del registro, para el caso de varias ciudades precargadas con solo una activa.

**Registro**

- **Alta en `OnEnable` y baja en `OnDisable` de `CityGeneratorInfo`**, el mismo patrón que `TrafficManager`/`PedestrianManager` usan para sus agentes. Coherente con el resto del package y sin ninguna búsqueda global.
- **Descartado: registro en `Awake`/`OnDestroy`** para que las ciudades desactivadas siguieran apareciendo en `All`. Se aparta del patrón de los managers y obliga a decidir si un handle de ciudad inactiva es válido; `For(info)` cubre ese caso sin esa complicación.
- **El registro es `static` pero no cachea nada.** Solo contiene los `CityGeneratorInfo` cuyo `OnEnable` corrió y cuyo `OnDisable` no, así que un domain reload lo vacía y los `OnEnable` de la recarga lo repueblan. Es la diferencia de fondo con la caché de la SPEC 14: aquí el estado estático lo mantiene el ciclo de vida de Unity, no una resolución perezosa que nadie invalida.

**Ruptura de compatibilidad**

- **Se elimina la superficie estática de v2.10 en vez de mantenerla delegando a `Default`.** Decisión explícita del usuario. La API se publicó hace una release y ningún código del repositorio la consume, así que romperla hoy cuesta una entrada de CHANGELOG; mantener dos superficies en paralelo obligaría a documentar ambas y a un ciclo de deprecación posterior.
- **Descartado: marcarla `[Obsolete]` y conservarla.** Llenaría la consola de warnings a quien acabe de integrarla, por una API de una semana de vida, sin ahorrarle la migración.
- **La spec no toca `package.json` ni publica el release.** Mismo criterio que la SPEC 14: el número de versión lo pone el flujo de release. El CHANGELOG sí marca la entrada como BREAKING para que la major quede justificada cuando se publique.
- **El conjunto de datos y mutaciones queda congelado en el de la SPEC 14.** Ni getters nuevos, ni setters sobre Traffic/Pedestrians/Audio/Player, ni eventos. Esta spec cambia cómo se resuelve la ciudad, no qué se puede consultar; ampliar la superficie mientras se reescribe es la vía más rápida a que una spec de días se convierta en una de semanas.

**Tests**

- **Con tests automatizados, a diferencia de las SPEC 13 y 14.** Aquellas cubrían superficie sobre comportamiento runtime ya verificado; esta introduce lógica de ciclo de vida (registro, invalidación, ambigüedad) que es precisamente lo que peor se verifica a mano y lo que la spec existe para arreglar.
- **Ciudades de test como `CityGeneratorInfo` sintéticos**, no generadas con el pipeline. Lo que se prueba es la resolución y el ciclo de vida, no la generación — que ya tiene sus propios tests en `Assets/Tests/EditMode/Generation`. Además el pipeline es Editor-only y tarda segundos por ciudad, lo que haría inviable un test con dos.
- **Se incluyen también `MinimapHUDTests` y `FreeCameraControllerTests`.** Decisión explícita del usuario, ampliando el alcance propuesto: cierra de una vez el P1 del informe técnico del 3 de septiembre de 2026, que pedía cubrir API, minimapa y cámara libre.
- **Los dos componentes se testean tal cual, sin refactorizarlos.** Si alguno resulta no ser testeable sin abrir superficie pública nueva, se documenta como limitación en vez de cambiarlo: no están rotos, y esta spec no es la que debe rediseñarlos.

**Límites heredados a la SPEC siguiente**

- **La generación sigue produciendo coordenadas de mundo absolutas.** `TrafficNetwork.IntersectionPosition` construye su `Vector3` directamente desde los ejes, sin `TransformPoint`, así que mover un `CityGeneratorRoot` mueve la geometría pero no el grafo. Es el motivo de que esta spec deje la API preparada para varias ciudades sin prometer que puedan coexistir en posiciones distintas.
- **Las búsquedas globales de la generación se quedan como están** (`GameObject.Find` en `CityGeneratorSceneBuilder`, `FindObjectsByType` en `TrafficNetwork.AssignTrafficLights` y `PedestrianNetwork.Build`, `FindAnyObjectByType` en `MinimapHUD`). No afectan a la corrección de la API y pertenecen al problema de multi-ciudad real.

## Riesgos identificados

- **Consultar la API desde `Awake`/`OnEnable` de otro componente puede no encontrar la ciudad.** El registro ocurre en el `OnEnable` de `CityGeneratorInfo`, y Unity no garantiza ningún orden entre los `OnEnable` de objetos distintos: un script del integrador que llame a `CityGeneratorAPI.Default` en su propio `Awake` puede recibir `null` aunque la ciudad exista. La API antigua disimulaba esto porque resolvía perezosamente con `FindFirstObjectByType`, que sí encuentra objetos ya instanciados aunque su `OnEnable` no haya corrido. Mitigación: documentarlo explícitamente en `docs/api-reference.md` — la consulta es segura a partir de `Start`, no antes — y anotarlo en la tabla de migración, porque es el único cambio de comportamiento capaz de romper código que hoy funciona.

- **Migrar mecánicamente de v2.10 puede introducir `NullReferenceException` donde antes había un 0.** La API antigua garantizaba que ningún getter lanzaba nunca; con `CityGeneratorCity?`, un `.Value` sobre `null` sí lanza. Mitigación: la tabla de migración documenta el patrón `?.` con `??` como forma recomendada, y no `.Value`.

- **El registro es estático y NUnit no recarga el dominio entre casos.** Un test que deje un `CityGeneratorInfo` sin destruir contamina el siguiente, y el fallo aparecerá en el test equivocado — el síntoma más caro de depurar de toda la suite. Mitigación: cada test de la API verifica en su `TearDown` que `CityGeneratorAPI.Count` ha vuelto a 0, y es un criterio de aceptación explícito.

- **Destruir o desactivar una ciudad mientras se itera `All` invalida la iteración.** `All` es una vista sobre la lista interna, no una copia, así que un `OnDisable` disparado dentro de un `foreach` sobre `All` lanzaría `InvalidOperationException`. Mitigación: documentarlo; la alternativa (devolver una copia en cada acceso) asignaría en cada consulta, que es justo lo que el `readonly struct` evita.

- **Los tests del Minimap HUD y la Free Camera pueden resultar poco testeables.** `MinimapHUD` depende de UGUI y de referencias serializadas (`RawImage`, marcadores), `FreeCameraController` del Input System y de un `InputActionAsset` asignado. Es posible que la cobertura útil sin tocar los componentes sea menor de lo esperado. Mitigación: el Scope ya fija la regla — se documenta la limitación, no se refactoriza el componente — y es preferible entregar un test corto y honesto que uno artificial que no verifique nada real.

- **La API parece soportar varias ciudades antes de que el sistema lo haga.** `All`, `InScene` y `For` invitan a asumir que dos ciudades pueden convivir, cuando la generación sigue produciendo coordenadas de mundo absolutas y las redes resuelven sus semáforos globalmente. Un integrador podría construir sobre esa suposición y encontrarse coches circulando por el grafo de la otra ciudad. Mitigación: nota explícita en ambos `api-reference` y en la sección de decisiones de esta spec; es un criterio de aceptación.

- **`Info` permite saltarse la API y escribir en los campos serializados de `CityGeneratorInfo`.** Esos campos son un snapshot de la generación, no una fuente de verdad viva: escribirlos no cambia la ciudad, solo hace que la API mienta. Mitigación: documentar `Info` como escotilla de escape de solo lectura por convención. El Inspector del componente ya avisa de lo mismo (`CityGeneratorInfoEditor`), así que el mensaje es coherente con lo que el usuario ya ve en el Editor.
