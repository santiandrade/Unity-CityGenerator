# SPEC 11 — Custom Grid

> **Estado:** Implemented
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 04 (Correcciones críticas y arquitectónicas), SPEC 06 (Custom Places)
> **Fecha:** 2026-08-30 (ampliada el 2026-09-01: el contorno exterior termina en acera; y los huecos de la forma se rellenan con suelo)
> **Objetivo:** Sustituir, de forma opcional, el rectángulo `Grid Width × Grid Height` por una forma de manzanas arbitraria (poliominó ortogonalmente contiguo, sin islas) editable a mano en un nuevo modo "Customize" de la card "General Options", de modo que calles, aceras, tráfico y red peatonal se generen solo donde realmente hay manzanas. **Ampliada tras QA manual** (ver "Ampliación — el contorno exterior termina en acera"): el contorno exterior de la ciudad generada termina siempre en acera transitable, nunca en asfalto pelado, en los dos modos de rejilla. **Ampliada de nuevo** (ver "Ampliación — los huecos de la forma se rellenan con suelo"): los huecos que la forma deja dentro de su propio bounding box se rellenan con un suelo por defecto, de modo que una ciudad custom acaba siendo el rectángulo simple de ese bounding box.

## Scope

**Dentro:**

- **`GeneralSettings` (`Editor/CityGeneratorSettings.cs`)** gana dos campos nuevos: `bool useCustomGrid` y `List<Vector2Int> customBlockCells` (qué celdas existen dentro del lienzo fijo `MaxGridSize × MaxGridSize`, hoy 10×10). Cuando `useCustomGrid` es `false`, todo se comporta exactamente igual que hoy (rectángulo `gridWidth × gridHeight`); es un interruptor, no una migración de datos.
- **Botón "Customize"** en la esquina superior derecha del área de grid preview de la card "General Options". Al pulsarlo: diálogo de confirmación simple (`EditorUtility.DisplayDialog`, única excepción deliberada a "sin diálogos bloqueantes" del resto de la tool, por ser una acción destructiva) — "Enter Customize mode? This resets the current grid" (Cancel/Continue). Al continuar: `useCustomGrid = true`, `customBlockCells` se resetea a una única celda en el centro del lienzo 10×10, `plazaCells` se vacía. Las entradas de `customPlaces` existentes **no** se tocan (ver más abajo).
- **Botón "Exit Customize"** (sustituye a "Customize" mientras `useCustomGrid` es `true`): pone `useCustomGrid = false`. El rectángulo `gridWidth`/`gridHeight` que había antes de entrar en Customize se conserva intacto y vuelve a ser el modo activo; la forma personalizada construida se descarta (no se recupera si se vuelve a pulsar "Customize" más tarde — siempre se parte de un bloque central).
- **Campos "Grid Width"/"Grid Height"** ocultos mientras `useCustomGrid` es `true`; reaparecen al salir.
- **Selector de 2 opciones "Define City Area" / "Define Plazas"**, visible solo mientras `useCustomGrid` es `true`, a la izquierda del grid preview:
  - **Define City Area**: el grid preview entra en un nuevo modo de `CityGeneratorGridPreview` (junto a `PlazaMultiToggle`/`SingleSelectQuadrant`) donde cada celda del lienzo 10×10 se pinta como manzana real (opacidad normal) o hueco (semitransparente). Un hueco muestra un icono "+" **solo si** es ortogonalmente adyacente a una manzana real existente; una manzana real muestra un icono "-" **solo si** quitarla no desconecta el resto de la forma (comprobación de conectividad tipo BFS/componentes conexas, recalculada en cada repintado) **y** no es la última manzana restante (mínimo 1 bloque). Clicar un "+"/"−" añade/quita esa celda de `customBlockCells`. El texto de ayuda bajo el grid cambia a un mensaje nuevo explicando este modo (texto exacto a definir en el plan de implementación).
  - **Define Plazas**: mismo comportamiento que el toggle de plazas de hoy (clic alterna `plazaCells`), pero solo sobre celdas presentes en `customBlockCells` (una celda-hueco no es clicable en este submodo). El texto de ayuda se mantiene igual que hoy ("Click a block above to toggle it as a plaza.").
- **`CityGeneratorGrid.BuildBlocks`** gana una vía para `useCustomGrid == true`: construye `List<BlockCell>` solo para las celdas en `customBlockCells` (con `isPlaza` desde `plazaCells` intersecado), usando el lienzo fijo (`MaxGridSize`) para el cálculo de posición mundial de cada celda — así una manzana no cambia de posición en el mundo según crece o encoge la forma a su alrededor.
- **`CityGeneratorGroundBuilder`**: nueva vía para `useCustomGrid == true` que genera la losa de asfalto, las líneas discontinuas y los pasos de cebra **solo** donde hay manzanas reales o calle adyacente a ellas (en vez de una única losa rectangular sobre todo `gridWidth × gridHeight`). La vía rectangular se mantiene para `useCustomGrid == false`. *(Alcance original: "la vía rectangular se mantiene intacta — cero regresión". Deja de ser cierto con la ampliación de aceras perimetrales de más abajo, que cambia deliberadamente la salida de **los dos** modos.)*
- **`Runtime/TrafficNetwork.cs`**: nueva vía de construcción de nodos/aristas sobre el conjunto real de manzanas (calles con final en punto muerto donde una manzana no tiene vecina en una dirección), en vez de `SetAxes(BuildAxes(gridWidth), BuildAxes(gridHeight))` sobre un rectángulo completo. Semáforos en toda intersección con al menos 3 brazos de calle reales (un cruce de 4 vías, o una T — incluida una T en el propio borde de la forma); una esquina con exactamente 2 brazos (perpendiculares, un simple giro de calle) o un tramo recto (2 brazos opuestos) nunca lleva semáforo — ver la corrección de criterio más abajo en "Decisiones tomadas y descartadas".
- **`Runtime/PedestrianNetwork.cs`**: extender `Build()` para iterar el conjunto real de manzanas en vez de asumir rectángulo. Ya tolera hoy que una manzana no tenga los 4 vecinos y ya maneja componentes desconectadas en su grafo, así que el riesgo aquí es menor que en Ground/Traffic.
- **`Editor/CityGeneratorMinimapBuilder.cs`**: el bounding box del snapshot se calcula a partir del rectángulo mínimo que envuelve las celdas realmente usadas (min/max de `customBlockCells`), no del lienzo fijo 10×10 completo — evita un minimapa mayoritariamente vacío cuando la forma es pequeña o está en una esquina del lienzo.
- **`Editor/UI/CityGeneratorGridPreview.cs`** en modo `SingleSelectQuadrant` (picker de cada Custom Place): cuando `useCustomGrid` es `true`, también pinta huecos como semitransparentes y no permite seleccionar una celda-hueco (clic ignorado), para que el usuario no elija a ciegas un bloque inexistente.
- **`CityGeneratorValidator`**: nuevo check bloqueante — un `CustomPlaceEntry` cuya `blockCell` ya no existe en `customBlockCells` (cuando `useCustomGrid` es `true`) es un error, mismo mecanismo/card que el check de rango existente. Las entradas de `customPlaces` **no se recolocan ni se limpian automáticamente** al editar la forma — su marcador sigue pintándose en su picker aunque el bloque grande ya no exista, y es el validator quien avisa, igual que hoy con un resize de `gridWidth`/`gridHeight`.
- Documentación: `docs/architecture/editor-tool.md` (sección del grid/pipeline) y `CHANGELOG.md` (`## [Unreleased]`).

**Ampliación — el contorno exterior termina en acera** (añadida tras QA manual de la implementación, a petición explícita del usuario: *"los bordes exteriores de la ciudad generada no tienen acera ni en esa zona se posicionan nodos por los que hacer que los peatones anden [...] Necesito que siempre los bordes acaben en acera"*). Aplica a **los dos** modos de rejilla, no solo al custom — el borde de la rejilla rectangular clásica tampoco tenía acera:

- **`CityGeneratorConstants`**: nueva `PerimeterSidewalkWidth` (6 m), y `RoadBaseMargin` pasa a derivarse de ella (`StreetWidth / 2 + PerimeterSidewalkWidth` = 11 m, antes 6 m fijos). Sigue significando lo mismo — "hasta dónde llega el suelo más allá del último eje de calle" — así que la losa de asfalto, el encuadre del snapshot del minimapa, el check del validator y la vista previa de tamaño de la ventana se ajustan solos, sin tocarlos.
- **`CityGeneratorGroundBuilder.BuildPerimeterSidewalks`** (sobrecarga rectangular + sobrecarga custom): tiende una banda de acera de `PerimeterSidewalkWidth` en el lado exterior de cada calle perimetral, siguiendo el contorno propio de la forma — incluido el contorno interior de un hueco tipo "donut". Esa calle pasa a tener lado lejano transitable, exactamente igual que una interior.
- **`CityGeneratorGroundBuilder.EnumerateBand`**: un único teselado compartido para las dos bandas del contorno (la de asfalto y la de acera), sustituyendo al teselado por manzana real hacia fuera — ver la decisión correspondiente en "Decisiones tomadas y descartadas".
- **`Runtime/PedestrianNetwork.BuildBorderWalkway`**: paseo peatonal sobre esa acera perimetral, con nodos `Ring` (los peatones aparecen y caminan allí como en cualquier otra acera). Sin él la acera existiría pero sería intransitable.
- **`Runtime/PedestrianNetwork.BuildCrossings`**: deja de descartar un brazo de paso de cebra por no haber manzana al otro lado; solo descarta el que no tiene manzana en **ninguno** de los dos lados. Un lado sin manzana se resuelve contra el nodo del paseo perimetral más cercano (`FindBorderNodeNear`). Es exactamente donde las rayas ya estaban pintadas en cada T del contorno desde la implementación original de esta spec — hasta ahora ese paso de cebra no llevaba a ninguna parte.
- **`CityGeneratorWindow.GetPedestrianDensityWarning`**: el denominador del aviso de densidad cuenta también los nodos del paseo perimetral (3 por lado de contorno; las esquinas se omiten, misma tolerancia de aproximación que el resto de esa estimación).

**Ampliación — los huecos de la forma se rellenan con suelo** (añadida a petición explícita del usuario: *"los huecos del grid donde no creamos ningún bloque aparecen en la escena vacíos [...] necesito que todos esos huecos se rellenen con un suelo por defecto"*). Aplica **solo** al modo custom: una rejilla rectangular no tiene huecos que rellenar.

- **`GroundSettings` (`Editor/CityGeneratorSettings.cs`)** gana `GameObject emptyBlockPrefab`, expuesto en la card City > Ground como **"Empty Block Prefab (custom grids only)"** — el paréntesis forma parte de la etiqueta, para que se vea desde la propia ventana que el campo no hace nada en modo rectangular. Su valor por defecto (`CityGeneratorDefaultAssets.ApplyTo`) es el mismo `Lawn.prefab` que usa `plaza.lawnPrefab`, y se posiciona en Y con la misma lógica que las losas de césped de una plaza (`CityGeneratorConstants.GroundDatumY`, es decir, cobertura de suelo a ras de la acera contra la que topa).
- **`CityGeneratorGroundBuilder.BuildEmptyBlocks`**: nueva vía, llamada desde `CityGeneratorContentAssembler` solo en la rama `useCustomGrid`, que instancia ese prefab en un grupo propio `EmptyBlocks` bajo el city root (marcado estático como el resto de geometría generada).
- **Resultado visual: el rectángulo del bounding box de las celdas reales**, no el lienzo 10×10. Una forma cuyas celdas ocupan 6 bloques de ancho y 8 de alto genera una ciudad de 6×8. La región que se rellena es exactamente ese bounding box **crecido en `RoadBaseMargin`** —el mismo rectángulo exterior que produciría una rejilla rectangular del mismo número de bloques, y el mismo que ya encuadran `CityGeneratorMinimapBuilder` y el check de View Radius del validator— **menos la forma dilatada `CellPitch/2 + RoadBaseMargin`**, o sea, todo lo que ya cubren la losa de asfalto y la acera perimetral.
- **`CityGeneratorGroundBuilder.EnumerateEmptyFill`**: el teselado de esa diferencia, por celda **ausente**, por la misma razón que `EnumerateBand` (ver "Decisiones tomadas y descartadas").
- **`CityGeneratorValidator`**: nuevo check bloqueante — `ground.emptyBlockPrefab` sin asignar mientras `useCustomGrid` es `true`.
- Documentación: `docs/architecture/editor-tool.md`, `CHANGELOG.md` y el índice de invariantes de `CLAUDE.md`.


**Fuera de alcance:**

- Cambios en `CityGeneratorBuildingBuilder`/`CityGeneratorPlazaBuilder`/`CityGeneratorStreetPropsBuilder`: ya iteran `IReadOnlyList<BlockCell> blocks` en vez de bucles `gridWidth × gridHeight`, así que reciben la lista de manzanas reales sin necesitar lógica nueva propia — se confirma explícitamente durante `/spec-impl`, no se asume sin verificar.
- Redimensionar `MaxGridSize` (el lienzo de Customize seguirá atado a la constante actual, sea cual sea).
- Cualquier integración con el sistema de Undo de Unity más allá de la que ya tiene (o no tiene) `CityGeneratorGridPreview` hoy para el toggle de plazas.
- Un tercer submodo o herramienta de "rellenar/vaciar todo" en Customize — solo +/- celda a celda y el selector Área/Plazas descritos arriba.
- Cualquier **nuevo tipo** de nodo peatonal: el grafo sigue teniendo exactamente cuatro (`Ring`, `Curb`, `Crossing`, `Interior`), y el paseo perimetral de la ampliación reutiliza `Ring` en vez de inventar un quinto. Sí entran, en cambio, los dos cambios de comportamiento de tráfico que la QA destapó (punto muerto real y objetivo inicial de `FindNodeAhead`), recogidos como correcciones en "Decisiones tomadas y descartadas".
- Rellenar huecos en el modo rectangular: no los tiene. `BuildEmptyBlocks` no se llama en esa rama.
- Que el suelo de relleno sea transitable por peatones o navegable por coches, o que reciba props/vegetación: es decoración de fondo, no ciudad. Fuera de la forma no hay ni nodos ni manzanas, y eso no cambia.
- Formas con más de una isla (huecos internos permitidos — un "donut" de manzanas alrededor de un hueco no conectado a nada es válido en cuanto a contigüidad de manzanas reales entre sí; lo único prohibido son dos grupos de manzanas reales sin conexión entre ellos).

## Data model

```csharp
// Editor/CityGeneratorSettings.cs — GeneralSettings gana:

[Tooltip("When true, the city footprint is the arbitrary shape in customBlockCells instead of the gridWidth x gridHeight rectangle.")]
public bool useCustomGrid;

[Tooltip("Which cells exist within the fixed CityGeneratorConstants.MaxGridSize x MaxGridSize canvas. Only meaningful/read when useCustomGrid is true. Reset to a single centre cell every time Customize mode is (re)entered.")]
public List<Vector2Int> customBlockCells = new();
```

Notas:

- `gridWidth`/`gridHeight` no se tocan ni se eliminan: siguen siendo la fuente de verdad cuando `useCustomGrid` es `false`, exactamente como hoy. `useCustomGrid` es un interruptor entre dos formas de producir el mismo tipo de dato de salida (`List<BlockCell>`), no una migración.
- `MinGridSize`/`MaxGridSize` (hoy `private const int` de `CityGeneratorWindow`) se promueven a `CityGeneratorConstants.MinGridSize`/`MaxGridSize`, porque `CityGeneratorGrid`, `CityGeneratorGridPreview` y `CityGeneratorMinimapBuilder` necesitan ahora la misma constante que la ventana — mismo criterio que el resto de `CityGeneratorConstants` ("cambiar desde ahí, nunca inline").

```csharp
// Editor/CityGeneratorGrid.cs — nuevas funciones puras (sin acceso a escena), junto a BuildBlocks:

// Overload activo cuando useCustomGrid es true: usa el lienzo fijo MaxGridSize para la
// posición mundial de cada celda (una manzana no se desplaza al crecer/encoger la forma).
public static List<BlockCell> BuildBlocks(IReadOnlyCollection<Vector2Int> customBlockCells, IReadOnlyCollection<Vector2Int> plazaCells);

// True si cell es (0,0)-(Max-1,Max-1) y es ortogonalmente adyacente a alguna celda de existingCells.
public static bool IsValidAddition(IReadOnlyCollection<Vector2Int> existingCells, Vector2Int cell);

// True si existingCells.Except(new[]{ removed }) sigue siendo un único componente conexo
// (ortogonal) y tiene al menos 1 celda. False también si removed no pertenece a existingCells.
public static bool CanRemoveWithoutSplitting(IReadOnlyCollection<Vector2Int> existingCells, Vector2Int removed);
```

```csharp
// Editor/UI/CityGeneratorGridPreview.cs

internal enum CityGeneratorGridPreviewMode
{
    PlazaMultiToggle,
    SingleSelectQuadrant,
    CustomAreaEdit, // NEW — "Define City Area" submode: +/- sobre customBlockCells
}
```

- Nuevo método `BindCustomArea(SerializedProperty customBlockCellsProperty, Action onChanged)`, paralelo a `Bind`/`BindSingleSelection`, para el submodo "Define City Area".
- Nuevo método `SetShapeMask(SerializedProperty customBlockCellsProperty)` (acepta `null` para desactivarlo, comportamiento rectangular de siempre), aplicable **encima** de un `Bind`/`BindSingleSelection` ya existente: cuando no es `null`, cualquier celda fuera de esa máscara se pinta semitransparente y no responde a clics. Usado por:
  - La instancia principal del grid preview de la City tab, en submodo **Define Plazas** (`Bind` normal + `SetShapeMask` apuntando a `general.customBlockCells`).
  - Cada picker `SingleSelectQuadrant` de una entrada de Custom Place, siempre que `general.useCustomGrid` sea `true`.
- `SetGrid` deja de ser la única vía para fijar el tamaño del lienzo: cuando el preview está en modo `CustomAreaEdit` (o cualquier modo con `SetShapeMask` activo), el ancho/alto se fuerzan siempre a `CityGeneratorConstants.MaxGridSize` en vez de a `gridWidth`/`gridHeight`.

```csharp
// Editor/CityGeneratorConstants.cs — ampliación "el contorno termina en acera":

public const float PerimeterSidewalkWidth = 6f;
public const float RoadBaseMargin = StreetWidth / 2f + PerimeterSidewalkWidth; // 11, antes 6 fijos
```

```csharp
// Editor/CityGeneratorGroundBuilder.cs — ampliación "el contorno termina en acera":

public static void BuildPerimeterSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, int gridWidth, int gridHeight);
public static void BuildPerimeterSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, IReadOnlyCollection<Vector2Int> blockCells);

// Teselado compartido por la banda de asfalto (radios CellPitch/2 .. CellPitch/2 + RoadBaseMargin)
// y la de acera (CellPitch/2 + StreetWidth/2 .. CellPitch/2 + RoadBaseMargin). Radios medidos
// desde el centro de una celda REAL, distancia de Chebyshev (anillos cuadrados).
private static IEnumerable<BandRect> EnumerateBand(HashSet<Vector2Int> cells, Func<Vector2Int, Vector3> centreOf, float innerRadius, float outerRadius);
```

```csharp
// Editor/CityGeneratorSettings.cs — ampliación "los huecos se rellenan con suelo":

// GroundSettings gana, junto a los cuatro prefabs de suelo existentes:
[Tooltip("Ground slab filling every gap of a Custom Grid shape, so the generated city still ends up as the plain rectangle of its bounding box. Ignored unless Customize mode is on. Required while it is.")]
public GameObject emptyBlockPrefab; // required if useCustomGrid
```

```csharp
// Editor/CityGeneratorGroundBuilder.cs — ampliación "los huecos se rellenan con suelo":

public static void BuildEmptyBlocks(GameObject emptyBlockPrefab, Transform emptyBlocksGroup, IReadOnlyCollection<Vector2Int> blockCells);

// Bounding box de las celdas reales crecido en RoadBaseMargin, menos la forma dilatada
// CellPitch/2 + RoadBaseMargin. Teselado por celda AUSENTE, como EnumerateBand.
private static IEnumerable<BandRect> EnumerateEmptyFill(HashSet<Vector2Int> cells, Func<Vector2Int, Vector3> centreOf);
```

```csharp
// Runtime/PedestrianNetwork.cs — ampliación "el contorno termina en acera":

// Llamado desde Build() ANTES de la pasada de crossings: BuildCrossings resuelve contra estos nodos.
private void BuildBorderWalkway(int blocksX, int blocksZ);

// El nodo del paseo perimetral sobre el que aterriza un paso de cebra que mira fuera de la
// ciudad, o -1 si no hay ninguno dentro de perimeterLinkRadius.
private int FindBorderNodeNear(Vector3 position);
```

```csharp
// Runtime/TrafficNetwork.cs — vía alternativa de construcción, junto a la existente SetAxes(...):
public void BuildFromBlockCells(IReadOnlyCollection<Vector2Int> blockCells);

// Runtime/PedestrianNetwork.cs — misma idea, junto al Build() existente:
public void BuildFromBlockCells(IReadOnlyCollection<Vector2Int> blockCells, /* mismos parámetros por-bloque que ya recibe hoy (isPlaza, isFullyReserved, etc.), indexados por Vector2Int en vez de por [gx,gy] de un rectángulo */);
```

La forma exacta del grafo interno (qué arista se salta cuando falta un vecino, cómo se detecta cada intersección) se decide en el plan de implementación, apoyándose en que `PedestrianNetwork` ya tolera vecinos ausentes y componentes desconectadas.

## Implementation plan

1. Promover `MinGridSize`/`MaxGridSize` de `CityGeneratorWindow` a `CityGeneratorConstants`; añadir `useCustomGrid`/`customBlockCells` a `GeneralSettings`. Compila, sin UI ni comportamiento nuevo (`useCustomGrid` por defecto `false` en todas partes). Test manual: abrir la ventana, nada cambia.

2. `CityGeneratorGrid` gana el overload `BuildBlocks(customBlockCells, plazaCells)` y las funciones puras `IsValidAddition`/`CanRemoveWithoutSplitting`, sin ningún caller todavía. Test: nuevos tests EditMode cubriendo casos de contigüidad (1 celda sola, forma en L, intento de desconectar, adyacencia diagonal no cuenta).

3. `CityGeneratorGridPreview` gana el modo `CustomAreaEdit`, `BindCustomArea` y `SetShapeMask`, como pieza autocontenida sin cablear aún en `CityGeneratorWindow`. Sin test manual visible todavía (la ventana no lo usa aún).

4. `CityGeneratorWindow`: botón "Customize"/"Exit Customize" con el diálogo de confirmación, el selector "Define City Area"/"Define Plazas", ocultar Grid Width/Height en modo custom, y el cableado al grid preview vía `BindCustomArea`/`SetShapeMask`/`Bind` según submodo. **Estado intermedio deliberado**: en este paso, clicar +/- ya edita `customBlockCells`/`plazaCells` correctamente, pero Build/Re-Build **todavía generan el rectángulo de siempre** (Grid/Ground/Traffic/Pedestrian no miran `useCustomGrid` aún) — se señala explícitamente como inconsistencia esperada, resuelta en los pasos 5-8. Test manual: verificar que añadir/quitar bloques, cambiar de submodo y salir de Customize actualizan los datos subyacentes correctamente (inspector), sin generar todavía.

5. `CityGeneratorContentAssembler` → `Grid`: cuando `useCustomGrid`, llama al nuevo overload de `BuildBlocks` en vez del rectangular. Test manual: generar una ciudad con una forma en L — edificios/plazas (sin cambios, ya iteran `BlockCell`) aparecen correctamente solo en manzanas reales, pero calles/tráfico/peatones siguen mostrando el rectángulo completo por debajo — visiblemente incorrecto, esperado hasta los pasos 6-8.

6. `CityGeneratorGroundBuilder`: implementar la vía de losa/marcas por-celda para `useCustomGrid`, activada desde `CityGeneratorContentAssembler`. La vía rectangular existente queda intacta para `useCustomGrid == false`. Test manual: generar la ciudad en L, confirmar que asfalto/acera/marcas terminan exactamente en el contorno de la forma, sin fuga de calle en celdas vacías del lienzo.

7. `TrafficNetwork.BuildFromBlockCells` + cableado en `CityGeneratorTrafficBuilder` (activado por `useCustomGrid`). Test manual: en Play mode, confirmar que los coches navegan la red irregular, se detienen correctamente en los puntos muertos/bordes de la forma, y los semáforos aparecen en toda intersección con al menos 3 brazos reales (ver corrección de criterio en "Decisiones tomadas y descartadas").

8. `PedestrianNetwork.BuildFromBlockCells` + cableado en `CityGeneratorPedestrianBuilder`. Test manual: en Play mode, los peatones recorren el contorno de la forma irregular sin salirse de ella ni intentar cruzar hacia celdas vacías.

9. `CityGeneratorMinimapBuilder`: bounding box calculado desde el rectángulo mínimo que envuelve las celdas realmente usadas (no el lienzo 10×10 completo) cuando `useCustomGrid`. Test manual: el HUD del minimapa encuadra la forma irregular sin márgenes vacíos enormes.

10. `CityGeneratorValidator`: nuevo check bloqueante — `CustomPlaceEntry.blockCell` fuera de `customBlockCells` cuando `useCustomGrid`. Test manual: quitar un bloque con un Custom Place asignado, confirmar que la card "Custom Places" se pone roja con el mensaje correcto y ambos botones de Build quedan deshabilitados.

11. Cada picker `SingleSelectQuadrant` de una entrada de Custom Place llama a `SetShapeMask` cuando `useCustomGrid`. Test manual: clicar un hueco en el mini-grid de una entrada no hace nada mientras `useCustomGrid` está activo.

12. Tests EditMode para `TrafficNetwork.BuildFromBlockCells`/`PedestrianNetwork.BuildFromBlockCells` sobre una forma pequeña irregular (p. ej. un triominó en L): número de nodos/aristas esperado, ninguna arista/nodo fuera de la forma.

13. Documentación: `docs/architecture/editor-tool.md` (sección de grid/pipeline, nota sobre `MaxGridSize` como lienzo fijo) y `CHANGELOG.md` (`## [Unreleased]`).

Pasos añadidos con la ampliación "el contorno exterior termina en acera" (posteriores a la QA manual de los 13 anteriores):

14. `CityGeneratorConstants`: `PerimeterSidewalkWidth` y `RoadBaseMargin` derivada. `CityGeneratorGroundBuilder`: `EnumerateBand` compartido (sustituyendo el teselado por manzana real hacia fuera, con su relleno de esquina) y `BuildPerimeterSidewalks` en sus dos sobrecargas, cableadas desde `CityGeneratorContentAssembler` en ambas ramas. Test: `Assets/Tests/EditMode/Generation/PerimeterSidewalkTests.cs` reconstruye la banda por fuerza bruta (3×3 rectangular y triominó en L) y comprueba que no hay huecos ni solapes y que nunca invade la calzada.

15. `PedestrianNetwork.BuildBorderWalkway` + el cambio en `BuildCrossings`/`FindBorderNodeNear`, y la estimación de densidad de `CityGeneratorWindow`. Test: un nodo del paseo perimetral y el anillo de una manzana están en la misma componente conexa; se actualizan los recuentos esperados de los tests de nodos existentes.

16. Corrección de `TrafficNetwork.FindNodeAhead` destapada por la QA de esta ampliación (coches aparcados contra la acera nueva) — ver la decisión correspondiente. Test: `Assets/Tests/EditMode/TrafficNetworkFindNodeAheadTests.cs`.

Paso añadido con la ampliación "los huecos de la forma se rellenan con suelo":

17. `GroundSettings.emptyBlockPrefab` + su fila en la card Ground + su default (`CityGeneratorDefaultAssets`/`CityGeneratorDefaultAssetsWriter`) + el check del validator; `CityGeneratorGroundBuilder.BuildEmptyBlocks`/`EnumerateEmptyFill` cableados desde `CityGeneratorContentAssembler` (grupo `EmptyBlocks`, marcado estático). Test: `Assets/Tests/EditMode/Generation/EmptyBlockFillTests.cs` reconstruye el relleno por fuerza bruta (forma en L, donut y rectángulo completo) y comprueba que no hay huecos ni solapes, que nunca tapa suelo pavimentado y que nunca sale del bounding box.

## Acceptance criteria

- [x] `GeneralSettings` compila con `useCustomGrid: bool` y `customBlockCells: List<Vector2Int>`; `useCustomGrid == false` reproduce exactamente el comportamiento de generación anterior a esta spec (sin regresión en ningún test EditMode/PlayMode/Performance existente).
- [x] La card "General Options" muestra un botón "Customize" en la esquina superior derecha del grid preview; al pulsarlo aparece el diálogo de confirmación "Enter Customize mode? This resets the current grid" con Cancel/Continue.
- [x] Al confirmar, `useCustomGrid` pasa a `true`, `customBlockCells` contiene una única celda central del lienzo `MaxGridSize × MaxGridSize`, `plazaCells` queda vacía, y los campos "Grid Width"/"Grid Height" desaparecen, sustituidos por un botón "Exit Customize".
- [x] Con "Define City Area" seleccionado: un hueco muestra "+" solo si es ortogonalmente adyacente a una manzana real; clicarlo la añade a `customBlockCells`. Una manzana real muestra "-" solo si quitarla no desconecta el resto de la forma y no es la última manzana restante; clicarla la quita. Ninguna combinación de clics permite llegar a 0 bloques ni a dos grupos de manzanas desconectados entre sí.
- [x] Con "Define Plazas" seleccionado: el clic alterna `plazaCells` exactamente como hoy, pero solo sobre celdas presentes en `customBlockCells` — un hueco no responde al clic.
- [x] Al pulsar "Exit Customize", `useCustomGrid` pasa a `false`, reaparecen "Grid Width"/"Grid Height" con los valores que tenían antes de entrar en Customize, y la ciudad vuelve a generarse como el rectángulo de siempre. Volver a pulsar "Customize" después arranca de nuevo desde un único bloque central, sin recordar la forma anterior.
- [x] Generar una ciudad con `useCustomGrid == true` y una forma no rectangular (p. ej. en L o en cruz) produce: asfalto/aceras/marcas viales solo donde hay manzana o calle adyacente a ella (ningún tramo de calle fuera del contorno de la forma, incluidas las esquinas convexas — ver corrección de criterio); edificios/plazas/props solo en manzanas reales; una red de tráfico navegable en Play mode con coches deteniéndose correctamente en los puntos muertos del contorno y semáforos en toda intersección con al menos 3 brazos reales; una red peatonal en la que los peatones no salen del contorno de la forma; y un minimapa que encuadra ajustadamente el bounding box de las manzanas reales.
- [x] Un `CustomPlaceEntry` cuyo `blockCell` deja de existir en `customBlockCells` (tras quitar ese bloque) bloquea ambos botones de Build y resalta la tab/card "Custom Places" en rojo con un mensaje explicando el conflicto, sin borrar ni recolocar el dato de la entrada.
- [x] El picker `SingleSelectQuadrant` de cada entrada de Custom Place, mientras `useCustomGrid` es `true`, pinta los huecos semitransparentes y no permite seleccionarlos.
- [x] Nuevos tests EditMode cubren: `CityGeneratorGrid.IsValidAddition`/`CanRemoveWithoutSplitting` (casos de contigüidad, incluyendo que la adyacencia diagonal no cuenta) y `TrafficNetwork.BuildFromBlockCells`/`PedestrianNetwork.BuildFromBlockCells` sobre una forma irregular pequeña.
- [x] `docs/architecture/editor-tool.md` y `CHANGELOG.md` reflejan el nuevo modo Customize y el flag `useCustomGrid`.

Criterios de la ampliación "el contorno exterior termina en acera":

- [x] Generar una ciudad en cualquiera de los dos modos de rejilla produce una banda de acera de `PerimeterSidewalkWidth` en el lado exterior de toda calle perimetral: el último elemento del contorno es siempre acera, nunca asfalto pelado. Verificado además de forma exacta (no solo a ojo) reconstruyendo la banda por fuerza bruta: sin huecos, sin losas solapadas y sin invadir la calzada, tanto en la rejilla rectangular como en una forma en L.
- [x] Los peatones aparecen y caminan por esa acera perimetral, y pueden llegar a ella desde el anillo de una manzana cruzando por el paso de cebra que ya estaba pintado en cada T del contorno (misma componente conexa del grafo).
- [x] Ningún coche queda varado apuntando fuera de la ciudad: `FindNodeAhead` no devuelve nunca un nodo sin salidas propias.
- [x] La suite EditMode no tiene ninguna regresión respecto al estado previo a la ampliación (los únicos fallos que quedan son los 9 preexistentes: 8 de `CityGeneratorValidatorTests` por settings mínimos sin Player Prefab/Input Actions y 1 de `CityGeneratorAudioBuilderTests`).

Criterios de la ampliación "los huecos de la forma se rellenan con suelo":

- [x] La card City > Ground muestra un campo nuevo etiquetado literalmente **"Empty Block Prefab (custom grids only)"**, con `Lawn.prefab` (el mismo del césped de las plazas) asignado por defecto, y bloquea ambos botones de Build si se deja vacío mientras Customize está activo.
- [x] Generar una ciudad con `useCustomGrid == true` y una forma no rectangular deja el resultado visual como un rectángulo completo: cada hueco de la forma —incluido el hueco interior de un donut— queda cubierto por el suelo "empty", posicionado en Y a la misma altura que el césped de una plaza.
- [x] El tamaño de ese rectángulo es el del bounding box de las celdas reales, no el del lienzo 10×10: una forma de 6 bloques de ancho por 8 de alto produce una ciudad de 6×8.
- [x] El relleno no tapa nada pavimentado: termina exactamente en el borde exterior de la acera perimetral, así que la ciudad sigue terminando en acera. Verificado de forma exacta (no a ojo) reconstruyendo el relleno por fuerza bruta sobre una forma en L, un donut y un rectángulo completo: sin huecos, sin losas solapadas, sin invadir el suelo pavimentado y sin salirse del bounding box. Una forma custom que ya es un rectángulo completo genera **cero** losas de relleno.
- [x] Ninguna regresión en la suite EditMode respecto al estado previo (siguen fallando los 9 preexistentes, y solo esos).

## Decisiones tomadas y descartadas

- **Una sola spec, no dividida en varias.** Es conceptualmente una única feature indivisible: el editor de forma sin que la generación la respete (o viceversa) no sería verificable ni usable por separado. La complejidad se gestiona con un plan de pasos ordenados (13 iniciales, 16 tras la ampliación de aceras perimetrales), cada uno dejando el sistema en un estado conocido (aunque temporalmente inconsistente en los pasos 4-5, señalado explícitamente), en vez de trocear en specs sin valor independiente.
- **Silueta real no rectangular, no "rectángulo de calles fijo con manzanas vacías por dentro".** Es la lectura literal de "sin tener que ser un grid cuadrado o rectangular" del usuario; la alternativa más barata (mantener el rectángulo de calles siempre) no habría cumplido el objetivo declarado, solo lo habría parecido desde la card de manzanas.
- **`useCustomGrid` es un interruptor entre dos modos mutuamente excluyentes, no una migración de datos.** `gridWidth`/`gridHeight` y `customBlockCells` coexisten siempre en `GeneralSettings`; cuál de los dos manda depende solo del flag. Evita tener que decidir cómo "convertir" un rectángulo en una forma personalizada de forma automática (relleno, recorte, etc.) — decisión que el usuario no pidió y que habría añadido ambigüedad.
- **"Exit Customize" descarta la forma personalizada; "Customize" siempre arranca de un único bloque central.** Confirmado explícitamente por el usuario. Alternativa descartada: conservar la forma en memoria tras salir, que habría necesitado un tercer estado ("tengo datos guardados" vs. "modo activo") no pedido y sin mecanismo claro para "abandonar del todo" una forma guardada.
- **Contigüidad y mínimo 1 bloque se imponen en la UI (solo se pintan +/- válidos), no como error de validación posterior.** Confirmado explícitamente por el usuario para el caso de islas. Mantiene consistente que nunca se puede llegar, editando desde la UI, a un estado de forma estructuralmente inválido — a diferencia del conflicto de Custom Place huérfano (ver debajo), que sí se deja como validación.
- **Un Custom Place huérfano (bloque eliminado bajo él) se permite y se resuelve como error de validación bloqueante, sin recolocar ni limpiar el dato.** Confirmado explícitamente por el usuario, replicando el mecanismo ya existente para cuando un resize de `gridWidth`/`gridHeight` deja una entrada fuera de rango (spec 06). Se prefiere este criterio, distinto del de islas, porque bloquear el "-" también sobre cualquier bloque con un Custom Place habría acoplado dos conceptos (forma del área vs. contenido de una manzana) que hoy son independientes en el resto de la tool.
- **Plazas se pierden siempre al entrar en Customize** (no se preservan por coordenada). Confirmado explícitamente por el usuario: coherente con "no hay manzanas, luego no puede haber plazas" al arrancar desde un único bloque.
- **El lienzo de edición de Customize es siempre `MaxGridSize × MaxGridSize` (hoy 10×10), fijo, sin posibilidad de ampliarlo desde dentro del modo.** Confirmado explícitamente por el usuario. Descartado un lienzo que arrancara del tamaño `gridWidth × gridHeight` previo (propuesta inicial): más simple de razonar, pero limitaría artificialmente el tamaño máximo de una forma personalizada a lo que tuviera el rectángulo antes de entrar.
- **Semáforos solo en intersecciones con 4 manzanas vecinas reales**, igual que hoy. Confirmado explícitamente por el usuario en la spec original; una intersección con 1-3 vecinos se trataría como una intersección de borde de hoy (mecanismo de cruce no semaforizado ya existente), sin inventar un tercer tipo de intersección.
  **Corregido tras QA manual, revocando esta decisión**: el criterio de "4 manzanas vecinas reales" dejaba sin semáforo ni paso de cebra cualquier cruce en T del contorno de la forma (la mayoría de los cruces de una forma no rectangular), y un criterio intermedio probado primero ("un brazo real en cada eje") sobreseñalizaba las esquinas de 2 brazos perpendiculares (un simple giro de calle, sin conflicto real de tráfico que arbitrar). El criterio final, confirmado por el usuario tras ver ambos fallos en la escena generada: semáforo + paso de cebra en toda intersección con **al menos 3 brazos de calle reales** (4 vías o T, incluida una T en el propio borde/contorno), nunca en un tramo recto (2 brazos opuestos) ni en una esquina (2 brazos perpendiculares). El mismo criterio de 3 brazos se aplicó también a la rejilla rectangular clásica (`useCustomGrid == false`), a petición explícita del usuario: antes tampoco tenía semáforos/pasos de peatones en sus propios cruces en T de borde, y ahora sí — un cambio de comportamiento deliberado sobre la salida no-custom de la tool, no solo una corrección de consistencia del modo custom.
- **Corrección de QA — hueco de `RoadBaseMargin` en las esquinas convexas del contorno.** Las tiras de margen de `CityGeneratorGroundBuilder.BuildRoadBase` (una por lado abierto de cada manzana) no cubrían el cuadrado diagonal donde se encuentran dos tiras perpendiculares en una esquina convexa (dos lados abiertos), dejando un hueco visible del tamaño del margen en cada esquina saliente de la forma. Corregido añadiendo una tesela `RoadBaseMargin × RoadBaseMargin` extra en cada esquina convexa.
  **Revocada por la ampliación de aceras perimetrales**: ese teselado por manzana real hacia fuera tenía además un segundo fallo que esta QA no vio — dos tiras perpendiculares cubren el mismo cuadrado en cada esquina **cóncava** (z-fighting entre losas coplanares; no cantaba porque era asfalto sobre asfalto) — y el relleno cuadrado de esquina solo es la forma correcta cuando la banda es tan ancha como el margen entero. Ya no existe ni la tira por lado abierto ni la tesela de esquina que describe esta decisión: ambas bandas del contorno se generan ahora con `EnumerateBand` (decisión siguiente).
- **Corrección de QA — persistencia del estado de forma custom en `TrafficNetwork`/`PedestrianNetwork`.** `useCustomShape` y los `HashSet<Vector2Int>` de celdas eran campos privados normales, no `[SerializeField]`: sobrevivían mientras el objeto seguía vivo en memoria, pero un domain reload o una recarga de escena (recompilar, volver a entrar en Play, reabrir la escena) los reiniciaba a sus valores por defecto (`false`/`null`) antes de que `Awake()` reconstruyera el grafo, tratando entonces todo el lienzo `MaxGridSize × MaxGridSize` como transitable — los coches acababan circulando por huecos sin nada construido. Corregido serializando `useCustomShape` y una lista plana `List<Vector2Int>` espejo de cada conjunto (un `HashSet` no es serializable), reconstruyendo el `HashSet` en tiempo de ejecución dentro de `Awake()` antes de llamar a `Build()`.
- **Corrección de QA — `CarAgent` en un punto muerto real.** `AdvanceToNextNode` caía a una búsqueda global (`FindNodeAhead`) del nodo más cercano en esa dirección en **toda** la red cuando el nodo actual no tenía salidas, bajo el supuesto (válido solo en la rejilla rectangular completa) de que eso "no debería pasar nunca". Una forma custom sí tiene puntos muertos reales (una calle que termina en el borde de la forma), y esa búsqueda podía enganchar el coche a un nodo fantasma desconectado en cualquier otra parte del lienzo de 10×10, sacándolo de la carretera. Corregido: ante un punto muerto real, el coche se desactiva (mismo criterio ya usado cuando no se encuentra ningún nodo delante al generarse), en vez de intentar reubicarse a ciegas.
  **Premisa corregida en la ampliación de aceras perimetrales**: el supuesto entre paréntesis era falso también para la rejilla rectangular. El **enrutado** nunca alcanza un nodo sin salidas, pero el objetivo **inicial** de cada coche (`CarAgent.Awake`) no viene del grafo sino de ese mismo `FindNodeAhead`, una búsqueda espacial ciega: un coche generado en una entrada del perímetro mirando hacia fuera tomaba como objetivo el nodo de salida sin salidas pasada la intersección y se autodesactivaba al llegar, quedándose aparcado para toda la sesión. Observado en vivo en una 5×5, con 5 de 80 coches varados. `FindNodeAhead` ya no devuelve nunca un nodo sin salidas, y un vehículo parado exactamente sobre un nodo se toma **ese** nodo como objetivo (llega en su primer tick y `PickNextNode` lo encamina, giros incluidos). Cubierto por `Assets/Tests/EditMode/TrafficNetworkFindNodeAheadTests.cs`.
- **El minimapa encuadra el bounding box de las celdas realmente usadas, no el lienzo 10×10 completo.** Evita un minimapa mayoritariamente vacío para una forma pequeña o desplazada hacia una esquina del lienzo — coherente con que el minimapa siempre ha reflejado el tamaño real de la ciudad generada, nunca un tamaño de lienzo/edición.
- **`BuildingBuilder`/`PlazaBuilder`/`StreetPropsBuilder` no cambian.** Ya iteran `IReadOnlyList<BlockCell> blocks` (nunca `gridWidth × gridHeight` en bucle), así que reciben la lista de manzanas reales sin lógica nueva — mismo patrón que spec 06 al introducir `reservedSlots`. Se confirma explícitamente en el paso 5 del plan antes de darlo por hecho.
- **Diagonal no cuenta como adyacencia/contigüidad**, solo N/S/E/O. Consistente con que las calles del proyecto son siempre ortogonales (nunca diagonales) en todo el resto del pipeline.
- **La acera perimetral se añadió a los dos modos de rejilla, no solo al custom.** El usuario reportó el borde sin acera como un único problema presente *"tanto en la generación de ciudades con cuadrícula 'no custom' como con 'custom'"*. Es, por tanto, un cambio deliberado de la salida no-custom de la tool — el mismo criterio ya aplicado en esta spec al llevar la regla de semáforos de 3 brazos a la rejilla rectangular.
- **`RoadBaseMargin` se deriva de `PerimeterSidewalkWidth` en vez de fijar un valor nuevo a mano.** Su significado no cambia ("hasta dónde llega el suelo más allá del último eje de calle"), solo su valor: `StreetWidth / 2 + PerimeterSidewalkWidth` = 11 m. Todo lo que ya la usaba —losa de asfalto, encuadre del snapshot del minimapa, check de View Radius del validator, vista previa de tamaño de la ventana— sigue siendo correcto sin tocarlo. Descartado dejar `RoadBaseMargin` en 6 m y tender la acera por fuera: habría dejado la mitad exterior de la calle perimetral (5 m) como asfalto sin suelo debajo del borde.
- **Ancho de 6 m para la acera perimetral.** Es comparable al ancho transitable real de una acera interior: en una manzana, entre el borde de las parcelas de edificio (radio ~18 m) y el bordillo (23 m) quedan ~5 m. No se buscó un valor "bonito" sino la paridad con lo que el peatón ya recorre dentro de la ciudad.
- **Las dos bandas del contorno (asfalto y acera) se teselan por celda AUSENTE, como diferencia exacta de dilataciones**, no por manzana real hacia fuera. `EnumerateBand(cells, centreOf, innerRadius, outerRadius)` calcula la diferencia entre la forma dilatada por cada radio (Chebyshev, anillos cuadrados): el cuadrado de 56 m de cada celda ausente se corta por las seis coordenadas donde puede caer el borde de cualquiera de las dos dilataciones, dando como mucho 5×5 subrectángulos enteramente dentro o enteramente fuera de la banda, que después se fusionan en X. Dos teselados más obvios se probaron y son incorrectos — **no volver a ninguno de los dos**:
  - **Por manzana real hacia fuera** (una tira por lado abierto más un relleno cuadrado de esquina): solapa dos tiras perpendiculares en cada esquina cóncava, y losas coplanares a la misma Y hacen z-fighting. Es el teselado que tenía la implementación original de esta spec.
  - **Por celda ausente pero por aristas, con relleno cuadrado de esquina**: el cuadrado solo vale cuando la banda es tan ancha como el margen entero. La banda de acera (6 m de los 11) necesita una pieza en **L** en cada esquina convexa; con un cuadrado queda una franja de suelo sin pavimentar justo donde la calle perimetral termina. Detectado por el test de cobertura, no a ojo.
- **El paseo peatonal perimetral reutiliza el tipo de nodo `Ring`, no uno nuevo.** Mantiene el invariante de los cuatro tipos de nodo (`Ring`, `Curb`, `Crossing`, `Interior`) y hace que los peatones aparezcan y elijan destino en el perímetro exactamente igual que en cualquier otra acera, sin lógica de spawn/destino propia. Se teselan también desde las celdas ausentes, cosiendo las tiras en un contorno único **por posición** (diccionario redondeado al decímetro), de modo que una esquina interior comparte un nodo entre sus dos tiras y una exterior recibe el suyo propio.
- **El paseo perimetral se conecta a la ciudad por los pasos de cebra que ya existían, sin inventar cruces nuevos a mitad de manzana.** `BuildCrossings` solo exigía manzana a ambos lados; ahora exige manzana en al menos uno, y el lado del contorno aterriza en el nodo del paseo más cercano. Como un brazo sigue necesitando un `TrafficLightIntersection` emparejado, esto solo añade cruces en las T reales de 3 brazos del borde — justo donde esta misma spec ya pintaba las rayas. Descartado un cruce no semaforizado en el centro de cada lado de manzana: habría metido peatones cruzando donde no hay paso de cebra pintado.
- **Una ciudad sin ninguna intersección semaforizada deja el paseo perimetral como componente conexa aparte.** Ocurre solo en el caso mínimo (rejilla 1×1, o forma custom de una sola celda), donde no hay ninguna intersección de 3 brazos. Se acepta: degrada exactamente igual que el caso ya existente de anillos de manzana aislados en una rejilla 1×N, y `PickRandomDestination(requiredComponent)` impide que un peatón elija un destino inalcanzable. Descartado forzar un cruce sin semáforo solo para ese caso.
- **El rectángulo que se rellena es el bounding box de las celdas reales crecido en `RoadBaseMargin`, no el bounding box "pelado".** Con el bounding box pelado, la ciudad no sería un rectángulo: allí donde una manzana real toca el borde del bounding box, su banda de margen de 11 m sobresaldría del relleno, dejando salientes irregulares en el contorno. Crecerlo en `RoadBaseMargin` da exactamente el mismo rectángulo exterior que produciría una rejilla rectangular del mismo número de bloques (`gridWidth * CellPitch + 2 * RoadBaseMargin`), que es además el que ya encuadran el snapshot del minimapa y el check de View Radius del validator — o sea, el relleno no introduce una tercera definición de "tamaño de la ciudad", reutiliza la que ya existía.
- **El relleno se resta de lo ya pavimentado en vez de taparlo.** Una losa de 56 m por celda ausente habría sido trivial de escribir, pero se comería la banda de asfalto y la acera perimetral que asoman dentro de esa celda (el relleno va a `GroundDatumY`, 0.18, por encima de la acera a 0.09), rompiendo el invariante "la ciudad siempre termina en acera" justo donde más se nota. Por eso `EnumerateEmptyFill` calcula la diferencia exacta contra la dilatación `CellPitch/2 + RoadBaseMargin`.
- **`EnumerateEmptyFill` tesela por celda AUSENTE, igual que `EnumerateBand`, y por los mismos dos motivos.** Un teselado por manzana real hacia fuera solapa en las esquinas cóncavas (z-fighting entre losas coplanares) y deja una muesca sin pavimentar en las convexas. Se reutiliza el mismo `BandRect`/`Reaches` y la misma idea de cortar el cuadrado de 56 m de la celda ausente por las coordenadas donde puede caer el borde de la dilatación (aquí solo dos, ±17, que dan como mucho 3×3 subrectángulos) y fusionar en X. El recorte contra el bounding box cae exactamente sobre esa misma coordenada ±17, así que no hace falta un corte extra para el anillo de celdas que rodea al bounding box. Cubierto por `EmptyBlockFillTests`.
- **El prefab por defecto es el mismo `Lawn.prefab` de las plazas, no un asset nuevo.** Confirmado explícitamente por el usuario, y coherente con la regla de portabilidad: no se añade contenido nuevo al paquete para algo que ya tiene una pieza equivalente.
- **El campo es obligatorio (error bloqueante) mientras Customize está activo**, en vez de opcional con relleno omitido si está vacío. Es lo que hace que el resultado prometido —"cualquier custom grid acaba siendo un rectángulo"— sea una garantía y no un "si lo rellenas". Mismo mecanismo que el resto de prefabs condicionalmente obligatorios de la tool (p. ej. `plaza.lawnPrefab` cuando hay plazas).
- **El suelo de relleno es decoración, no ciudad**: no lleva nodos peatonales, ni red de tráfico, ni props, ni entra en `obstacles`. Fuera de la forma no hay manzanas, y esta ampliación no cambia dónde puede haber contenido — solo qué se ve en el hueco.
- **Un hueco interior no conectado a nada (rodeado de manzanas reales, tipo "donut") es válido** — la única prohibición es que las manzanas reales entre sí formen un único componente conexo, no que no existan huecos. No se pidió lo contrario, y prohibirlo añadiría una comprobación extra sin ningún beneficio declarado.
