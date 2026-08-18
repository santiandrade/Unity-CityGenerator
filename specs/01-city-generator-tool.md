# 01 — City Generator Tool

## Header

- **Estado:** Completado — los 11 pasos del plan de implementación están hechos, incluido el QA manual (paso 11), confirmado por el usuario. Quedan abiertos únicamente los seguimientos listados en "Riesgos identificados" que no formaban parte del alcance de ese QA (rejillas grandes, tamaños de rejilla distintos de 3×3, ruta fija a `InputSystem_Actions.inputactions`, ausencia de `.asmdef`).
- **Dependencias:** Ninguna (proyecto ya contiene la ciudad de referencia `City.unity` y los scripts a generalizar: `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection`)
- **Fecha:** 2026-08-18
- **Última actualización:** 2026-08-19 — QA manual (paso 11) confirmado por el usuario: spec y criterios de aceptación cerrados. Field of View de la cámara generada fijado a 45°; corrección: con `buildingsPerBlock < 4`, las esquinas ocupadas ahora se sortean por manzana en vez de ser siempre las primeras N.
- **Actualización anterior:** 2026-08-18 — revisión del documento contra el código ya implementado (rama `spec-01-city-generator-tool`); recuperación de la reproducibilidad por `seed` como campo `Custom Seed` opcional; cámara de la escena generada con encuadre fijo verificado a mano; ventana con los prefabs de este proyecto pre-asignados; asteriscos de obligatoriedad condicional; solapamiento de edificios/vehículos cerrado como decisión aceptada tras QA manual adversarial.
- **Objetivo (una frase):** Crear una ventana de Editor de Unity (`Tools > City Generator`) que genere ciudades procedurales completas — suelo, manzanas, plazas, edificios, vegetación, vehículos, props y red de tráfico — en una nueva escena, a partir de listas de prefabs y parámetros configurables por el usuario, de forma totalmente portable a cualquier proyecto Unity.

## Scope

**Dentro del alcance:**

- Nueva ventana de Editor (`EditorWindow`) accesible desde `Tools > City Generator`, con las secciones: Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props, y tres botones: **"Build City in New Scene"**, **"Re-Build City in Current Scene"** (borra el objeto raíz `City` de la escena activa y lo regenera, dejando intactos luz, cámara, volumen y player; pide confirmación y no guarda la escena) y **"Reset to Defaults"**.
- Detalles de UI que forman parte del alcance: miniatura de la herramienta en la cabecera de la ventana, marca de campo obligatorio (asterisco rojo) con su leyenda al pie, y `labelWidth` proporcional al ancho de la ventana.
- Tamaño de rejilla acotado a 1-10 por eje mediante `IntSlider`.
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
- Creación de una nueva escena `Assets/Scenes/City<N>.unity` (creando la carpeta si no existe, `N` = siguiente entero libre), con la misma jerarquía de grupos que `City.unity` actual, más `Directional Light`, `Main Camera` con `ThirdPersonCamera` en valores por defecto (con o sin referencia a Player según se haya asignado), y una instancia de Player solo si su prefab fue asignado. **No se genera `Global Volume`** (ver "Decisiones tomadas durante la implementación").
- Refactor de namespace: `TestAI` → `CityGenerator.Runtime` (scripts de juego: `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection`) y `CityGenerator.Editor` (la ventana de la tool), moviendo los archivos a `Assets/CityGenerator/Runtime/` y `Assets/CityGenerator/Editor/` respectivamente.
- Componentes necesarios (p. ej. `CarAgent`, layer `Vehicle`) se añaden a las **instancias generadas en escena**, nunca modificando el asset de prefab original del usuario.

**Fuera de alcance:**

- Migrar o modificar `City.unity` (sigue funcionando igual, ya que las referencias a los scripts son por GUID y no cambian).
- Persistencia de configuraciones como asset `ScriptableObject` reutilizable/perfiles guardables (se descarta explícitamente a favor de estado de sesión).
- Colocación manual de plazas en celdas específicas elegidas por el usuario.
- Variación de altura/terreno (la ciudad generada es plana, igual que la actual).
- Bake de NavMesh o soporte de IA de navegación.
- Materiales, shaders o ajustes de render propios: la herramienta solo instancia los prefabs del usuario, con el aspecto que ya traigan. Tras eliminar el `Global Volume` (ver decisiones) el código no depende de URP, pero tampoco se ha probado en otros pipelines.
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
        public bool useCustomSeed = false; // if false, generation uses an unseeded System.Random
        public int seed = 0;               // only applied when useCustomSeed is true
        public GameObject playerPrefab; // optional
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
        [Range(0f, 1f)] public float density = 0.3f;
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
        public GameObject lampPrefab; // optional — placed 3 per sidewalk side when assigned, no density
        public GameObject busStopPrefab;
        [Range(0f, 1f)] public float busStopDensity = 0.3f;
        public GameObject binPrefab;
        [Range(0f, 1f)] public float binDensity = 0.3f;
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
}
```

El candidato **no lleva `footprintRadius`**: la comprobación de solapamiento no usa radios, sino el `Rect` en XZ de los bounds combinados de los `Renderer` de la instancia ya creada (`CityGeneratorBoundsUtility.GetWorldBounds` + `Rect.Overlaps`). Si solapa, la instancia se destruye con `DestroyImmediate`.

`CityGeneratorWindow` mantiene `CityGeneratorSettings settings` como campo `[SerializeField]` propio — sin ScriptableObject externo.

Constantes de layout (46 m de manzana, paso de 56 m, `GroundDatumY` 0.18, slots de 22 m, insets de aceras y esquinas, offsets de semáforo y paso de cebra…) viven centralizadas en `CityGeneratorConstants`, cada una con el comentario de por qué tiene ese valor. Varias se ajustaron contra bugs reales de solapamiento durante la implementación.

## Plan de implementación

Los 11 pasos están completados (commits `b4f091a`..`bc83e87`). El paso 11 (QA manual), confirmado por el usuario, encontró y corrigió un bug (esquinas de edificio siempre en el mismo orden; ver "Decisiones tomadas durante la implementación") y cerró la duda sobre solapamiento de edificios/vehículos como decisión aceptada, no como gap.

1. **Refactor de namespace y carpetas.** Mover `PlayerController`, `ThirdPersonCamera`, `TrafficNetwork`, `CarAgent`, `TrafficLight`, `TrafficLightIntersection` de `Assets/Scripts/` a `Assets/CityGenerator/Runtime/`, cambiar `namespace TestAI` → `namespace CityGenerator.Runtime`. Abrir `City.unity` y verificar que no hay referencias de script rotas (los `.meta`/GUID no cambian).
2. **Esqueleto de la ventana.** Crear `CityGeneratorWindow` en `Assets/CityGenerator/Editor/`, registrar el ítem de menú `Tools > City Generator`, dibujar las secciones (Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props) sobre `CityGeneratorSettings`, sin lógica de generación aún.
3. **Validación.** Implementar las comprobaciones (prefabs obligatorios asignados, `trafficLightPrefab` con componente `TrafficLight`, porcentajes de vehículos sumando 100, listas con mínimo 1 prefab donde aplique) mostrando diálogo bloqueante + log de errores en consola al pulsar "Build City" sin que aún genere nada.
4. **Grid y suelo.** Generar la rejilla `gridWidth × gridHeight` de manzanas de 46 m, determinar celdas de plaza (centrada si hay 1, aleatoria si hay más), instanciar `RoadBase`, `Sidewalk` por manzana, y las marcas viales (líneas y pasos de cebra) reproduciendo el patrón actual escalado al tamaño de rejilla.
5. **Edificios.** Empaquetar hasta 4 slots por manzana no-plaza, con las esquinas realmente ocupadas sorteadas por manzana cuando `buildingsPerBlock < 4` (no siempre las primeras N), y selección aleatoria de prefab garantizando que cada prefab de la lista se use al menos una vez en toda la ciudad.
6. **Plazas.** Componer cada celda de plaza con 4 césped, elemento central (si asignado), 4 bancos (si asignado), y vegetación aleatoria de la lista de vegetación.
7. **Motor de colocación por densidad.** Función genérica de muestreo de puntos candidatos + densidad + comprobación de solapamiento vía bounds de `Renderer` en XZ, reutilizada por farolas (sobre aceras), paradas de bus (sobre aceras, mirando a la carretera), papeleras (cerca de esquinas de manzana) y vegetación de calle.
8. **Tráfico y vehículos.** Generalizar `TrafficNetwork` para construir el grafo de carriles a partir de `gridWidth`/`gridHeight` (en vez de los ejes fijos actuales), instanciar semáforos y `TrafficLightIntersection` en cada cruce de 4 salidas, y repartir el número total de vehículos según el porcentaje por prefab, añadiendo `CarAgent` + layer `Vehicle` a cada instancia (nunca al prefab origen). Si "incluir red de tráfico" = no, se omite la instanciación de vehículos y `CarAgent`.
9. **Ensamblado de escena.** Crear la nueva escena, añadir `Directional Light`, `Main Camera` + `ThirdPersonCamera` en valores por defecto (con referencia al Player si se instanció), instancia de Player si su prefab fue asignado, y guardar como `Assets/Scenes/City<N>.unity` (creando la carpeta si no existe).
10. **Cableado final de los botones.** Unir validación → pipeline de generación (pasos 4-9) → guardado de escena → log de resumen (nº manzanas, edificios, props, vegetación, semáforos y vehículos generados), tanto para "Build City in New Scene" como para "Re-Build City in Current Scene".
11. **QA manual.** ✅ Confirmado por el usuario. Generadas varias configuraciones de prueba reutilizando los prefabs ya existentes en este proyecto (con y sin plazas múltiples, con y sin tráfico, con y sin Player, y un caso deliberado con un prefab obligatorio sin asignar), más una prueba adversarial adicional no prevista en el plan original (edificios deliberadamente muy anchos, para forzar el caso límite de solapamiento). Encontrado y corregido el bug de las esquinas de edificio siempre en el mismo orden.

## Criterios de aceptación

Todos verificados: por inspección del código y, tras el QA manual del paso 11, en ejecución en el Editor.

- [x] `Tools > City Generator` abre la ventana del editor con las 7 secciones (Opciones Generales, Suelo, Plazas, Edificios, Vegetación, Vehículos, Props) y los botones "Build City in New Scene", "Re-Build City in Current Scene" y "Reset to Defaults".
- [x] Cerrar y reabrir la ventana dentro de la misma sesión de Editor conserva todos los valores introducidos previamente.
- [x] Pulsar un botón de generación sin un prefab obligatorio asignado (`roadBasePrefab`, `sidewalkPrefab`, `roadLinePrefab`, `crosswalkLinePrefab`, o `lawnPrefab` si `plazaCount > 0`) muestra un diálogo bloqueante y un error en consola, y no genera nada.
- [x] Pulsar un botón de generación con `includeTraffic = true` y un `trafficLightPrefab` sin componente `TrafficLight` muestra error y no genera nada.
- [x] Pulsar un botón de generación con porcentajes de vehículos que no suman 100 muestra error y no genera nada.
- [x] Con una configuración válida, "Build City in New Scene" crea `Assets/Scenes/City<N>.unity` (N = siguiente entero libre) con la carpeta `Scenes` creada si no existía.
- [x] "Re-Build City in Current Scene" pide confirmación, borra el objeto raíz `City` de la escena activa y lo regenera, dejando intactos luz, cámara, volumen y player, y marca la escena como modificada sin guardarla.
- [x] La escena generada reproduce la jerarquía de grupos de `City.unity` (`Roads`, `Sidewalks`, `RoadMarkings`, `Buildings`, `Plaza`, `Trees`, `StreetLights`, `TrafficLights`, `Props`, `Vehicles`, `TrafficNetwork`).
- [x] Con `plazaCount = 1`, la plaza queda en el centro de la rejilla; con `plazaCount > 1`, las plazas quedan repartidas aleatoriamente entre las manzanas.
- [x] Cada manzana no-plaza contiene entre 0 y `buildingsPerBlock` edificios, y cada prefab de la lista de edificios aparece al menos una vez en la ciudad completa (si hay suficientes slots).
- [x] Cada plaza contiene 4 zonas de césped, el elemento central si fue asignado, 4 bancos si el prefab fue asignado, y vegetación aleatoria.
- [x] Con `density = 0` para paradas de bus/papeleras/vegetación no se genera ninguna instancia de esa categoría; con `density = 1` se generan en (casi) todos los puntos candidatos válidos. (Las farolas ya no tienen densidad: se colocan siempre 3 por lado de manzana si su prefab está asignado.)
- [x] Ninguna instancia colocada por densidad (props, vegetación) se solapa con otra ni con los edificios, plazas y césped ya colocados, verificado por bounds de `Renderer` en XZ.
- [x] Edificios y vehículos **no pasan** por la comprobación de solapamiento — decisión aceptada, no un gap pendiente. Ver "Edificios y vehículos sin comprobación de solapamiento" en decisiones.
- [x] Todas las farolas quedan sobre acera; todas las paradas de bus quedan sobre acera y orientadas hacia la carretera; todas las papeleras quedan cerca de una esquina de manzana.
- [x] Todo cruce de 4 salidas (los estrictamente interiores de la rejilla) queda regulado por semáforos, incluso con `includeTraffic = false` o `vehicleCount = 0`.
- [x] Con `vehicleCount > 0` y `includeTraffic = true`, el número de vehículos instanciados por prefab respeta el porcentaje configurado (±1 por redondeo, reparto por resto mayor) y cada vehículo tiene `CarAgent` + layer `Vehicle` añadidos a su instancia (el asset de prefab original queda sin modificar).
- [x] Si `playerPrefab` no está asignado, la escena generada no contiene instancia de Player y la `Main Camera` no tiene referencia a Player asignada en `ThirdPersonCamera`.
- [x] Con `Custom Seed` activado, dos ciudades generadas con el mismo `seed` y la misma configuración producen resultados idénticos (mismas posiciones, mismos prefabs elegidos): `Assemble` usa `new System.Random(seed)` y todos los builders reciben esa misma instancia por parámetro. Con `Custom Seed` desactivado (por defecto), se usa `new System.Random()` sin semilla, como antes.
- [x] `City.unity` (la escena actual) sigue abriendo sin errores de scripts perdidos tras el refactor de namespace y la reubicación de archivos (referencias por GUID, `.meta` conservados).

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

### Decisiones tomadas durante la implementación (no estaban en el planteamiento inicial)

- **`Global Volume` y `globalVolumeProfile` eliminados** (commit `a094fef`). Razón según el propio commit: la herramienta no debe depender de ningún pipeline de render concreto; el usuario puede añadir su volumen y activar el post-procesado de cámara después si lo quiere. Se eliminaron el campo `VolumeProfile` de los ajustes, su control en la ventana y el `CreateGlobalVolume` del ensamblador de escena, junto con los `using UnityEngine.Rendering[.Universal]`. `City.unity` conserva su volumen, que es anterior a la herramienta.
- **Farolas sin densidad, colocación fija de 3 por lado de manzana.** Se eliminó `lampDensity`: el alumbrado con huecos aleatorios se veía mal y no se comporta como los demás props, que sí admiten dispersión. Usan `PlaceAll` en vez de `PlaceByDensity`, con un inset menor al del resto del mobiliario (`LampEdgeInset`) para que en manzanas-plaza no pisen el césped.
- **Solapamiento por `Rect` de bounds en XZ, sin `footprintRadius` en el candidato.** El planteamiento original preveía guardar un radio en `PlacementCandidate`; en la práctica es más simple y más exacto instanciar el objeto, medir sus bounds reales combinados y destruirlo si solapa, porque el radio se calcula igual sobre bounds y un rectángulo ajusta mejor que un círculo en prefabs alargados (paradas de bus, bancos).
- **Botón "Re-Build City in Current Scene".** Añadido durante la implementación: iterar sobre parámetros generando una escena nueva cada vez llenaba `Assets/Scenes/` de ciudades desechables. Regenera solo el objeto raíz `City` y deja el resto de la escena intacto.
- **Las plazas usan una fracción de la densidad de vegetación configurada** (`PlazaVegetationDensityFactor` 0.5), porque su rejilla de candidatos es mucho más densa que la de las calles y con el mismo valor quedaban notablemente más pobladas.
- **`TrafficNetwork` con dos arrays de ejes independientes (`axesX`/`axesZ`) en lugar de uno solo**, más `SetAxes()` y `Build()` público. Necesario para rejillas no cuadradas. El orden es obligatorio: colocar todos los semáforos y **después** llamar a `Build()`, porque el emparejamiento de semáforos escanea la escena.
- **Tamaño de rejilla acotado a 1-10 por eje.** Límite pragmático frente al coste O(n²) de las comprobaciones de solapamiento, que era el riesgo de rendimiento anotado abajo.
- **`seed` recuperado como `useCustomSeed` (bool, `false` por defecto) + `seed` (int, `0` por defecto), en vez de un `seed` siempre activo.** El planteamiento inicial tenía un campo `seed` que se usaba siempre; se prefirió hacerlo opcional porque el caso de uso habitual (explorar variaciones rápidamente) quiere aleatoriedad real, y solo cuando se busca reproducir una ciudad concreta tiene sentido fijar la semilla. Con `useCustomSeed = false`, `Assemble` sigue creando `new System.Random()` sin semilla, igual que antes; con `true`, usa `new System.Random(seed)`. En la ventana, el campo `Custom Seed` se dibuja tras Player Prefab, y `Seed` solo se muestra (indentado) cuando está activado.
- **Edificios y vehículos quedan fuera de la comprobación de solapamiento, por dos razones distintas — confirmado con QA manual deliberadamente adversarial (edificios muy anchos en los 4 slots de una manzana).**
  - *Edificios entre sí*: sí se solapan visiblemente si el prefab excede el slot de 22 m (comprobado). Se acepta como responsabilidad del usuario dimensionar sus prefabs de edificio al slot, igual que ya se asume para el resto de la herramienta (los prefabs los aporta el usuario). No se añade validación de tamaño ni comprobación de bounds entre slots.
  - *Vehículos vs. edificios*: no es un caso de solapamiento sin cubrir, es estructuralmente imposible que lo sea del modo en que preocupaba — el sensor de `CarAgent` (`SphereCastNonAlloc`) solo consulta la layer `Vehicle`, así que un coche nunca "ve" un edificio aunque invada el carril; el margen real viene de la geometría (calle de 10 m, carril a 2,6 m del eje, manzana a 23 m del centro), no de una comprobación de solapamiento. Confirmado sin colisiones visibles incluso con edificios muy anchos.
- **Esquinas ocupadas sorteadas por manzana, no fijas.** Bug encontrado en QA manual: con `buildingsPerBlock < 4`, siempre se rellenaban las esquinas `0..buildingsPerBlock-1` en ese orden (con 1 edificio, siempre esquina 0 en todas las manzanas; con 2, siempre 0 y 1). `CityGeneratorBuildingBuilder.BuildBuildings` ahora baraja los 4 índices de esquina con `CityGeneratorRandomUtility.Shuffle` **por cada manzana** antes de tomar los primeros `buildingsPerBlock`, así que qué esquinas quedan ocupadas y cuáles vacías varía manzana a manzana. Verificado en ejecución vía `Unity_RunCommand`: con `buildingsPerBlock = 1` en una rejilla 3×3, la esquina ocupada varió entre manzanas en vez de ser siempre la misma.
- **Cámara de la escena generada con posición, rotación y Field of View fijos (36, 28, -36) / (27°, -45°, 0°) / FOV 45°, en vez de la vista cenital original (0, 150, -100) / (-300°, 0, 0) / FOV por defecto (60°).** El encuadre original era una vista de pájaro poco útil como punto de partida; los valores actuales son un plano en ángulo sobre la ciudad, verificados a mano en `City.unity` antes de trasladarlos al generador.
- **La ventana abre con los prefabs de este mismo proyecto ya asignados** (`CityGeneratorDefaultAssets.ApplyTo`, invocado desde `OnEnable` la primera vez que se crea la ventana en la sesión, y de nuevo desde "Reset to Defaults"), en vez de abrir con todos los campos vacíos. Es la única pieza de la herramienta que no es portable a otro proyecto — deliberadamente aislada en un solo fichero (`CityGeneratorDefaultAssets.cs`) para poder sustituirla o retirarla al empaquetar la tool. Los campos escalares (rejilla, nº de vehículos, etc.) mantienen sus valores por defecto propios de la tool, ajenos al proyecto.
- **Los asteriscos rojos de campo obligatorio ahora son condicionales**: cuando el requisito depende de otro valor (`lawnPrefab` de `plazaCount > 0`, `vegetation.prefabs` de `density > 0`, `vehicles` de `vehicleCount > 0`, `trafficLightPrefab` de `includeTraffic`), el asterisco solo se pinta mientras esa condición se cumple. `DrawRequiredField` ganó un parámetro `isRequired` (por defecto `true`, así que los campos de Suelo, siempre obligatorios, no cambiaron). La etiqueta sigue mostrando la condición en texto ("if Plaza Count > 0", etc.) independientemente del asterisco.

## Riesgos identificados

Los tres primeros están resueltos y verificados. Los cuatro siguientes son **seguimientos abiertos, no bloqueantes**: quedan fuera del alcance concreto que cubrió el QA manual del paso 11 (que usó la rejilla 3×3 por defecto y los escenarios listados en ese paso), y conviene revisarlos antes de dar la herramienta por lista para distribuir o para usarla con configuraciones más agresivas que las probadas.

- ~~**Generalizar `TrafficNetwork` de ejes fijos a una rejilla arbitraria.**~~ *Resuelto:* se hizo con `axesX`/`axesZ` independientes y `SetAxes()`/`Build()`. Las tres reglas del sistema de reservas anti-deadlock documentadas en `CLAUDE.md` se mantuvieron intactas. Verificado en ejecución con la rejilla 3×3 por defecto durante el QA manual.
- ~~**Prefabs de terceros sin `Renderer` en la raíz.**~~ *Mitigado:* `CityGeneratorBoundsUtility.GetWorldBounds` combina todos los `Renderer` de los hijos y, si no hay ninguno, avisa por consola y usa una huella mínima de seguridad.
- ~~**Reubicar `Assets/Scripts/*.cs` a `Assets/CityGenerator/`.**~~ *Resuelto:* el movimiento se completó sin referencias rotas (Unity serializa por GUID y los `.meta` viajaron con los archivos). No queda ninguna referencia al namespace `TestAI`.
- **Sin verificar en ejecución con rejillas distintas de 3×3** (rectangulares, o más grandes). El QA manual confirmó el comportamiento con la rejilla por defecto; el código soporta 1×1 hasta 10×10 por diseño (`axesX`/`axesZ` independientes, límites del `IntSlider`), pero no se ha generado ni inspeccionado visualmente ninguna configuración fuera de 3×3.
- **Rendimiento con rejillas grandes**: la comprobación de solapamiento es O(n²) sobre la lista acumulada de obstáculos, que crece con toda la ciudad. Acotado de momento limitando la rejilla a 10×10, pero no medido. Conviene cronometrar una generación 10×10.
- **`CityGeneratorSceneBuilder` referencia `Assets/InputSystem_Actions.inputactions` por ruta fija.** En cualquier otro proyecto esa ruta no existirá y `ThirdPersonCamera` se quedará sin `inputActions`, silenciosamente y sin aviso. Contradice el objetivo de portabilidad y debería resolverse antes de empaquetar la herramienta (exponer el asset como campo de la ventana, o al menos avisar por consola cuando no se encuentre).
- **La herramienta no tiene `.asmdef`.** Todo compila en `Assembly-CSharp`/`Assembly-CSharp-Editor`, y `Editor/` solo es ensamblado de editor por el nombre mágico de la carpeta. Para distribuirla como paquete independiente harán falta ensamblados propios.
