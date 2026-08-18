# 01 — City Generator Tool

## Header

- **Estado:** Approved
- **Dependencias:** Ninguna (proyecto ya contiene la ciudad de referencia `City.unity` y los scripts a generalizar: `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection`)
- **Fecha:** 2026-08-18
- **Objetivo (una frase):** Crear una ventana de Editor de Unity (`Tools > City Generator`) que genere ciudades procedurales completas — suelo, manzanas, plazas, edificios, vegetación, vehículos, props y red de tráfico — en una nueva escena, a partir de listas de prefabs y parámetros configurables por el usuario, de forma totalmente portable a cualquier proyecto Unity.

## Scope

**Dentro del alcance:**

- Nueva ventana de Editor (`EditorWindow`) accesible desde `Tools > City Generator`, con las secciones: Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props, y botón "Build City".
- Estado de la ventana persistente durante la sesión del Editor (se mantiene al cerrar/reabrir la ventana sin recompilar ni reiniciar Unity), mediante campos serializados del propio `EditorWindow`.
- Algoritmo de generación procedural completo:
  - Rejilla de manzanas `ancho × alto`, tamaño de manzana fijo (46 m, igual a la ciudad actual) con calles entre ellas.
  - Colocación de plazas: si hay 1, en el centro de la rejilla; si hay más de 1, distribución aleatoria entre las manzanas.
  - Edificios: hasta 4 slots por manzana no-plaza, selección aleatoria entre los prefabs asignados, garantizando que cada prefab asignado se use al menos una vez en toda la ciudad.
  - Plazas: siempre 4 zonas de césped + 1 elemento central (si está asignado) + 4 bancos (si está asignado el prefab) + vegetación aleatoria, igual que la plaza actual.
  - Vegetación, farolas, paradas de bus y papeleras: colocación por muestreo de puntos candidatos válidos según sus reglas (aceras, esquinas, mirando a la carretera) con densidad 0-1 controlando cuántos de esos candidatos se instancian, y comprobación de solapamiento entre todos los objetos ya colocados vía bounds de `Renderer` en el plano XZ.
  - Vehículos: número total configurable, repartidos según un porcentaje por prefab (debe sumar 100%).
  - Red de tráfico: siempre se genera `TrafficNetwork` + semáforos con ciclo funcionando si "incluir red de tráfico" = sí; si es "no", se genera toda la geometría y los semáforos igual, pero sin vehículos ni componentes `CarAgent`.
- Validación previa a "Build City": prefabs obligatorios asignados, prefab de semáforo con componente `TrafficLight`, porcentajes de vehículos sumando 100%; errores mostrados con diálogo bloqueante y detalle en consola.
- Creación de una nueva escena `Assets/Scenes/City<N>.unity` (creando la carpeta si no existe, `N` = siguiente entero libre), con la misma jerarquía de grupos que `City.unity` actual, más `Directional Light` (valores por defecto de Unity), `Global Volume` (con el perfil asignado si lo hay, o sin perfil si no), `Main Camera` con `ThirdPersonCamera` en valores por defecto (con o sin referencia a Player según se haya asignado), y una instancia de Player solo si su prefab fue asignado.
- Refactor de namespace: `TestAI` → `CityGenerator.Runtime` (scripts de juego: `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection`) y `CityGenerator.Editor` (la ventana de la tool), moviendo los archivos a `Assets/CityGenerator/Runtime/` y `Assets/CityGenerator/Editor/` respectivamente.
- Componentes necesarios (p. ej. `CarAgent`, layer `Vehicle`) se añaden a las **instancias generadas en escena**, nunca modificando el asset de prefab original del usuario.

**Fuera de alcance:**

- Migrar o modificar `City.unity` (sigue funcionando igual, ya que las referencias a los scripts son por GUID y no cambian).
- Persistencia de configuraciones como asset `ScriptableObject` reutilizable/perfiles guardables (se descarta explícitamente a favor de estado de sesión).
- Colocación manual de plazas en celdas específicas elegidas por el usuario.
- Variación de altura/terreno (la ciudad generada es plana, igual que la actual).
- Bake de NavMesh o soporte de IA de navegación.
- Soporte para pipelines de render distintos de URP.
- Publicación real en el Asset Store (empaquetado, documentación de venta, etc.) — el spec deja el código listo para ello, pero no cubre el proceso de publicación.

## Modelo de datos

Todo el estado de la tool vive en una única clase serializable dentro de `CityGeneratorWindow` (así se persiste automáticamente entre aperturas/cierres de la ventana en la misma sesión de Editor, sin necesidad de un asset aparte):

```csharp
namespace CityGenerator.Editor
{
    [Serializable]
    internal class CityGeneratorSettings
    {
        public GeneralSettings general = new();
        public GroundSettings ground = new();
        public PlazaSettings plaza = new();
        public List<GameObject> buildingPrefabs = new();
        public VegetationSettings vegetation = new();
        public List<VehicleEntry> vehicles = new();
        public PropsSettings props = new();
    }

    [Serializable]
    internal class GeneralSettings
    {
        public int gridWidth = 3;
        public int gridHeight = 3;
        public int plazaCount = 1;
        public int buildingsPerBlock = 4; // clamped 0-4
        public bool includeTraffic = true;
        public int vehicleCount = 18;
        public int seed = 0;
        public GameObject playerPrefab;        // optional
        public VolumeProfile globalVolumeProfile; // optional
    }

    [Serializable]
    internal class GroundSettings
    {
        public GameObject roadBasePrefab;   // required
        public GameObject sidewalkPrefab;   // required
        public GameObject roadLinePrefab;   // required
        public GameObject crosswalkLinePrefab; // required
    }

    [Serializable]
    internal class PlazaSettings
    {
        public GameObject centerpiecePrefab; // optional
        public GameObject lawnPrefab;        // required if plazaCount > 0
        public GameObject benchPrefab;       // optional
    }

    [Serializable]
    internal class VegetationSettings
    {
        public List<GameObject> prefabs = new(); // 1+ required if density > 0
        [Range(0f, 1f)] public float density = 0.5f;
    }

    [Serializable]
    internal class VehicleEntry
    {
        public GameObject prefab;
        [Range(0f, 100f)] public float percentage;
    }

    [Serializable]
    internal class PropsSettings
    {
        public GameObject trafficLightPrefab; // required if includeTraffic
        public GameObject lampPrefab;         [Range(0f,1f)] public float lampDensity = 0.5f;
        public GameObject busStopPrefab;      [Range(0f,1f)] public float busStopDensity = 0.3f;
        public GameObject binPrefab;          [Range(0f,1f)] public float binDensity = 0.3f;
    }
}
```

Estructuras internas de generación (no serializadas, viven solo durante `Build City`):

```csharp
internal readonly struct BlockCell
{
    public readonly int gridX, gridY;
    public readonly Vector3 center;
    public readonly bool isPlaza;
}

internal readonly struct PlacementCandidate
{
    public readonly Vector3 position;
    public readonly Quaternion rotation;
    public readonly float footprintRadius; // from Renderer bounds, used for overlap checks
}
```

`CityGeneratorWindow` mantiene `CityGeneratorSettings settings` como campo `[SerializeField]` propio — sin ScriptableObject externo.

## Plan de implementación

1. **Refactor de namespace y carpetas.** Mover `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection` de `Assets/Scripts/` a `Assets/CityGenerator/Runtime/`, cambiar `namespace TestAI` → `namespace CityGenerator.Runtime`. Abrir `City.unity` y verificar que no hay referencias de script rotas (los `.meta`/GUID no cambian).
2. **Esqueleto de la ventana.** Crear `CityGeneratorWindow` en `Assets/CityGenerator/Editor/`, registrar el ítem de menú `Tools > City Generator`, dibujar las secciones (Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props) sobre `CityGeneratorSettings`, sin lógica de generación aún.
3. **Validación.** Implementar las comprobaciones (prefabs obligatorios asignados, `trafficLightPrefab` con componente `TrafficLight`, porcentajes de vehículos sumando 100, listas con mínimo 1 prefab donde aplique) mostrando diálogo bloqueante + log de errores en consola al pulsar "Build City" sin que aún genere nada.
4. **Grid y suelo.** Generar la rejilla `gridWidth × gridHeight` de manzanas de 46 m, determinar celdas de plaza (centrada si hay 1, aleatoria si hay más), instanciar `RoadBase`, `Sidewalk` por manzana, y las marcas viales (líneas y pasos de cebra) reproduciendo el patrón actual escalado al tamaño de rejilla.
5. **Edificios.** Empaquetar hasta 4 slots por manzana no-plaza, selección aleatoria de prefab garantizando que cada prefab de la lista se use al menos una vez en toda la ciudad.
6. **Plazas.** Componer cada celda de plaza con 4 césped, elemento central (si asignado), 4 bancos (si asignado), y vegetación aleatoria de la lista de vegetación.
7. **Motor de colocación por densidad.** Función genérica de muestreo de puntos candidatos + densidad + comprobación de solapamiento vía bounds de `Renderer` en XZ, reutilizada por farolas (sobre aceras), paradas de bus (sobre aceras, mirando a la carretera), papeleras (cerca de esquinas de manzana) y vegetación de calle.
8. **Tráfico y vehículos.** Generalizar `TrafficNetwork` para construir el grafo de carriles a partir de `gridWidth`/`gridHeight` (en vez de los ejes fijos actuales), instanciar semáforos y `TrafficLightIntersection` en cada cruce de 4 salidas, y repartir el número total de vehículos según el porcentaje por prefab, añadiendo `CarAgent` + layer `Vehicle` a cada instancia (nunca al prefab origen). Si "incluir red de tráfico" = no, se omite la instanciación de vehículos y `CarAgent`.
9. **Ensamblado de escena.** Crear la nueva escena, añadir `Directional Light` (valores por defecto), `Global Volume` (con perfil si se asignó), `Main Camera` + `ThirdPersonCamera` en valores por defecto (con referencia al Player si se instanció), instancia de Player si su prefab fue asignado, y guardar como `Assets/Scenes/City<N>.unity` (creando la carpeta si no existe).
10. **Cableado final del botón "Build City".** Unir validación → pipeline de generación (pasos 4-9) → guardado de escena → log de resumen (nº manzanas, edificios, props, vehículos generados).
11. **QA manual.** Generar varias configuraciones de prueba reutilizando los prefabs ya existentes en este proyecto (con y sin plazas múltiples, con y sin tráfico, con y sin Player, y un caso deliberado con un prefab obligatorio sin asignar) para verificar que la validación y la generación se comportan según lo especificado.

## Criterios de aceptación

- [ ] `Tools > City Generator` abre la ventana del editor con las 7 secciones (Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props) y el botón "Build City".
- [ ] Cerrar y reabrir la ventana dentro de la misma sesión de Editor conserva todos los valores introducidos previamente.
- [ ] Pulsar "Build City" sin un prefab obligatorio asignado (`roadBasePrefab`, `sidewalkPrefab`, `roadLinePrefab`, `crosswalkLinePrefab`, o `lawnPrefab` si `plazaCount > 0`) muestra un diálogo bloqueante y un error en consola, y no genera ninguna escena.
- [ ] Pulsar "Build City" con `includeTraffic = true` y un `trafficLightPrefab` sin componente `TrafficLight` muestra error y no genera nada.
- [ ] Pulsar "Build City" con porcentajes de vehículos que no suman 100 muestra error y no genera nada.
- [ ] Con una configuración válida, "Build City" crea `Assets/Scenes/City<N>.unity` (N = siguiente entero libre) con la carpeta `Scenes` creada si no existía.
- [ ] La escena generada reproduce la jerarquía de grupos de `City.unity` (`Roads`, `Sidewalks`, `RoadMarkings`, `Buildings`, `Plaza`, `Trees`, `StreetLights`, `TrafficLights`, `Props`, `Vehicles`, `TrafficNetwork`).
- [ ] Con `plazaCount = 1`, la plaza queda en el centro de la rejilla; con `plazaCount > 1`, las plazas quedan repartidas aleatoriamente entre las manzanas.
- [ ] Cada manzana no-plaza contiene entre 0 y `buildingsPerBlock` edificios, sin superposición entre ellos, y cada prefab de la lista de edificios aparece al menos una vez en la ciudad completa (si hay suficientes slots).
- [ ] Cada plaza contiene 4 zonas de césped, el elemento central si fue asignado, 4 bancos si el prefab fue asignado, y vegetación aleatoria.
- [ ] Con `density = 0` para farolas/paradas de bus/papeleras/vegetación no se genera ninguna instancia de esa categoría; con `density = 1` se generan en (casi) todos los puntos candidatos válidos.
- [ ] Ninguna instancia generada (edificio, prop, vegetación, vehículo) se solapa con otra, verificado por bounds de `Renderer` en XZ.
- [ ] Todas las farolas quedan sobre acera; todas las paradas de bus quedan sobre acera y orientadas hacia la carretera; todas las papeleras quedan cerca de una esquina de manzana.
- [ ] Todo cruce de 4 salidas queda regulado por semáforos que ciclan correctamente, incluso con `includeTraffic = false` o `vehicleCount = 0`.
- [ ] Con `vehicleCount > 0` y `includeTraffic = true`, el número de vehículos instanciados por prefab respeta el porcentaje configurado (±1 por redondeo) y cada vehículo tiene `CarAgent` + layer `Vehicle` añadidos a su instancia (el asset de prefab original queda sin modificar).
- [ ] Si `playerPrefab` no está asignado, la escena generada no contiene instancia de Player y la `Main Camera` no tiene referencia a Player asignada en `ThirdPersonCamera`.
- [ ] Si `globalVolumeProfile` no está asignado, el `Global Volume` de la escena generada no tiene perfil asignado.
- [ ] Dos ciudades generadas con el mismo `seed` y la misma configuración producen resultados idénticos (mismas posiciones, mismos prefabs elegidos).
- [ ] `City.unity` (la escena actual) sigue abriendo y funcionando sin errores de scripts perdidos tras el refactor de namespace y la reubicación de archivos.

## Decisiones tomadas y descartadas

- **Persistencia de configuración: estado de sesión del `EditorWindow`, no `ScriptableObject`.** Se descartó un asset de perfil reutilizable por simplicidad; el usuario prioriza que la tool "recuerde" la última ciudad generada dentro de la misma sesión, no gestionar múltiples perfiles guardados.
- **Rejilla `ancho × alto` en vez de "número total de manzanas".** Se descartó forzar cuadrados perfectos o rellenar huecos vacíos: dar control directo de filas/columnas es más simple y predecible tanto para el usuario como para el algoritmo de layout.
- **Colocación de plazas: centrada si es 1, aleatoria si son varias.** Se descartó dar control manual de celda por plaza para mantener la UI simple; es coherente con que la ciudad de referencia ya tiene su única plaza en el centro.
- **Límite de 4 edificios por manzana.** Se descartó un packing dinámico de slots de tamaño variable; reutiliza el mismo patrón de 22 m por slot que ya existe en la ciudad actual, evitando lógica de subdivisión adicional.
- **Bancos exclusivos de plazas, sin densidad propia en PROPS.** Se corrigió una contradicción del planteamiento inicial: los 4 bancos por plaza son fijos y el campo "densidad de bancos" en PROPS se eliminó por no tener sentido de uso.
- **Selección aleatoria de edificios con garantía de uso de cada prefab al menos una vez.** Se descartó selección puramente aleatoria sin garantía (podría dejar prefabs asignados sin usar en ciudades pequeñas) y selección determinista round-robin (menos variedad visual); se reutiliza la misma garantía que ya implementa el generador actual.
- **Densidad de props/vegetación interpretada como fracción de puntos candidatos válidos rellenados**, no como una fórmula de "número absoluto de objetos". Mantiene la semántica intuitiva 0 = nada, 1 = máximo posible, independiente del tamaño de la ciudad generada.
- **Solapamiento evitado vía bounds de `Renderer` en XZ**, no radios fijos por categoría configurados a mano. Se eligió porque los prefabs los aporta el usuario (tamaños arbitrarios, incluso de otros asset packs), así que un radio fijo sería incorrecto para prefabs de tamaño no estándar.
- **Componentes de vehículo (`CarAgent`, layer) añadidos a la instancia en escena, nunca al asset de prefab origen.** Se descartó replicar el patrón actual de modificar el prefab vía `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`, porque en una tool genérica de terceros no es aceptable mutar los assets del usuario de forma permanente.
- **Refactor de namespace `TestAI` → `CityGenerator.Runtime` / `CityGenerator.Editor`, con reubicación de archivos a `Assets/CityGenerator/`.** Necesario para que la tool sea distribuible como paquete independiente de cara al Asset Store; se confirmó que no rompe `City.unity` porque las referencias de Unity son por GUID, no por namespace/ruta.
- **`TrafficNetwork` se genera siempre (incluso sin tráfico), pero sin instanciar vehículos ni `CarAgent`.** Los semáforos siguen ciclando en todos los cruces regulados; se descartó omitir por completo la generación de la red porque el requisito explícito es que todo cruce de 4 salidas quede regulado por semáforos, tráfico o no.
- **Vehículos repartidos por porcentaje configurable por prefab (debe sumar 100), no por peso relativo libre.** Se descartó el peso relativo (1, 2, 3...) porque el porcentaje es más explícito y fácil de validar.

## Riesgos identificados

- **Generalizar `TrafficNetwork` de ejes fijos (`{-84,-28,28,84}`) a una rejilla `gridWidth × gridHeight` arbitraria** es el punto más delicado del plan: el algoritmo actual de generación del grafo de carriles, coincidencia geométrica de semáforos (`Dot(light.forward, direction) < -0.9`) y el sistema de reservas anti-deadlock en cruces están ajustados para esa disposición concreta. Un error aquí podría reproducir el bloqueo total de tráfico que ya ocurrió una vez en el proyecto original. Mitigación: mantener intactas las tres reglas del sistema de reservas documentadas en `CLAUDE.md` y probar exhaustivamente con varias configuraciones de rejilla (no solo 3×3) antes de dar el paso por cerrado.
- **Prefabs de terceros sin `Renderer` en la raíz o con pivotes/bounds inusuales** pueden hacer que el cálculo de radio de solapamiento (paso 7) sea impreciso, generando huecos excesivos o solapamientos residuales. Mitigación: calcular bounds combinando todos los `Renderer` en hijos, con un radio mínimo de seguridad por si el prefab no tiene ninguno.
- **Reubicar `Assets/Scripts/*.cs` a `Assets/CityGenerator/`** implica que Unity debe recompilar y puede haber una ventana en la que `Assembly-CSharp` quede en un estado inconsistente (ver la nota de `CLAUDE.md` sobre que el Editor no recompila solo en segundo plano). Mitigación: forzar `CompilationPipeline.RequestScriptCompilation()` y verificar consola sin errores antes de continuar con el resto del plan.
- **Rendimiento con rejillas grandes** (p. ej. 10×10 manzanas): la generación de decenas de miles de puntos candidatos y comprobaciones de solapamiento O(n²) podría volverse lenta. No es bloqueante para el spec (no hay requisito de rendimiento explícito), pero conviene anotarlo si en el futuro se prueban ciudades grandes.
