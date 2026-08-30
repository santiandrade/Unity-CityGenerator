# SPEC 11 — Custom Grid

> **Estado:** Implemented
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 04 (Correcciones críticas y arquitectónicas), SPEC 06 (Custom Places)
> **Fecha:** 2026-08-30
> **Objetivo:** Sustituir, de forma opcional, el rectángulo `Grid Width × Grid Height` por una forma de manzanas arbitraria (poliominó ortogonalmente contiguo, sin islas) editable a mano en un nuevo modo "Customize" de la card "General Options", de modo que calles, aceras, tráfico y red peatonal se generen solo donde realmente hay manzanas.

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
- **`CityGeneratorGroundBuilder`**: nueva vía para `useCustomGrid == true` que genera la losa de asfalto, las líneas discontinuas y los pasos de cebra **solo** donde hay manzanas reales o calle adyacente a ellas (en vez de una única losa rectangular sobre todo `gridWidth × gridHeight`). La vía actual (rectángulo completo) se mantiene intacta y se usa siempre que `useCustomGrid` sea `false` — cero regresión sobre el comportamiento/tests existentes.
- **`Runtime/TrafficNetwork.cs`**: nueva vía de construcción de nodos/aristas sobre el conjunto real de manzanas (calles con final en punto muerto donde una manzana no tiene vecina en una dirección), en vez de `SetAxes(BuildAxes(gridWidth), BuildAxes(gridHeight))` sobre un rectángulo completo. Semáforos solo en intersecciones con las 4 celdas vecinas siendo manzanas reales (igual criterio que hoy); el resto de intersecciones se comportan como las intersecciones de borde de hoy (mecanismo de cruce no semaforizado ya existente en `CarAgent`).
- **`Runtime/PedestrianNetwork.cs`**: extender `Build()` para iterar el conjunto real de manzanas en vez de asumir rectángulo. Ya tolera hoy que una manzana no tenga los 4 vecinos y ya maneja componentes desconectadas en su grafo, así que el riesgo aquí es menor que en Ground/Traffic.
- **`Editor/CityGeneratorMinimapBuilder.cs`**: el bounding box del snapshot se calcula a partir del rectángulo mínimo que envuelve las celdas realmente usadas (min/max de `customBlockCells`), no del lienzo fijo 10×10 completo — evita un minimapa mayoritariamente vacío cuando la forma es pequeña o está en una esquina del lienzo.
- **`Editor/UI/CityGeneratorGridPreview.cs`** en modo `SingleSelectQuadrant` (picker de cada Custom Place): cuando `useCustomGrid` es `true`, también pinta huecos como semitransparentes y no permite seleccionar una celda-hueco (clic ignorado), para que el usuario no elija a ciegas un bloque inexistente.
- **`CityGeneratorValidator`**: nuevo check bloqueante — un `CustomPlaceEntry` cuya `blockCell` ya no existe en `customBlockCells` (cuando `useCustomGrid` es `true`) es un error, mismo mecanismo/card que el check de rango existente. Las entradas de `customPlaces` **no se recolocan ni se limpian automáticamente** al editar la forma — su marcador sigue pintándose en su picker aunque el bloque grande ya no exista, y es el validator quien avisa, igual que hoy con un resize de `gridWidth`/`gridHeight`.
- Documentación: `docs/architecture/editor-tool.md` (sección del grid/pipeline) y `CHANGELOG.md` (`## [Unreleased]`).

**Fuera de alcance:**

- Cambios en `CityGeneratorBuildingBuilder`/`CityGeneratorPlazaBuilder`/`CityGeneratorStreetPropsBuilder`: ya iteran `IReadOnlyList<BlockCell> blocks` en vez de bucles `gridWidth × gridHeight`, así que reciben la lista de manzanas reales sin necesitar lógica nueva propia — se confirma explícitamente durante `/spec-impl`, no se asume sin verificar.
- Redimensionar `MaxGridSize` (el lienzo de Customize seguirá atado a la constante actual, sea cual sea).
- Cualquier integración con el sistema de Undo de Unity más allá de la que ya tiene (o no tiene) `CityGeneratorGridPreview` hoy para el toggle de plazas.
- Un tercer submodo o herramienta de "rellenar/vaciar todo" en Customize — solo +/- celda a celda y el selector Área/Plazas descritos arriba.
- Cualquier nuevo tipo de nodo peatonal o cambio de comportamiento de `CarAgent` más allá de que ambos grafos ahora se construyan sobre una forma arbitraria.
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

7. `TrafficNetwork.BuildFromBlockCells` + cableado en `CityGeneratorTrafficBuilder` (activado por `useCustomGrid`). Test manual: en Play mode, confirmar que los coches navegan la red irregular, se detienen correctamente en los puntos muertos/bordes de la forma, y los semáforos solo aparecen en intersecciones con 4 vecinos reales.

8. `PedestrianNetwork.BuildFromBlockCells` + cableado en `CityGeneratorPedestrianBuilder`. Test manual: en Play mode, los peatones recorren el contorno de la forma irregular sin salirse de ella ni intentar cruzar hacia celdas vacías.

9. `CityGeneratorMinimapBuilder`: bounding box calculado desde el rectángulo mínimo que envuelve las celdas realmente usadas (no el lienzo 10×10 completo) cuando `useCustomGrid`. Test manual: el HUD del minimapa encuadra la forma irregular sin márgenes vacíos enormes.

10. `CityGeneratorValidator`: nuevo check bloqueante — `CustomPlaceEntry.blockCell` fuera de `customBlockCells` cuando `useCustomGrid`. Test manual: quitar un bloque con un Custom Place asignado, confirmar que la card "Custom Places" se pone roja con el mensaje correcto y ambos botones de Build quedan deshabilitados.

11. Cada picker `SingleSelectQuadrant` de una entrada de Custom Place llama a `SetShapeMask` cuando `useCustomGrid`. Test manual: clicar un hueco en el mini-grid de una entrada no hace nada mientras `useCustomGrid` está activo.

12. Tests EditMode para `TrafficNetwork.BuildFromBlockCells`/`PedestrianNetwork.BuildFromBlockCells` sobre una forma pequeña irregular (p. ej. un triominó en L): número de nodos/aristas esperado, ninguna arista/nodo fuera de la forma.

13. Documentación: `docs/architecture/editor-tool.md` (sección de grid/pipeline, nota sobre `MaxGridSize` como lienzo fijo) y `CHANGELOG.md` (`## [Unreleased]`).

## Acceptance criteria

- [x] `GeneralSettings` compila con `useCustomGrid: bool` y `customBlockCells: List<Vector2Int>`; `useCustomGrid == false` reproduce exactamente el comportamiento de generación anterior a esta spec (sin regresión en ningún test EditMode/PlayMode/Performance existente).
- [x] La card "General Options" muestra un botón "Customize" en la esquina superior derecha del grid preview; al pulsarlo aparece el diálogo de confirmación "Enter Customize mode? This resets the current grid" con Cancel/Continue.
- [x] Al confirmar, `useCustomGrid` pasa a `true`, `customBlockCells` contiene una única celda central del lienzo `MaxGridSize × MaxGridSize`, `plazaCells` queda vacía, y los campos "Grid Width"/"Grid Height" desaparecen, sustituidos por un botón "Exit Customize".
- [x] Con "Define City Area" seleccionado: un hueco muestra "+" solo si es ortogonalmente adyacente a una manzana real; clicarlo la añade a `customBlockCells`. Una manzana real muestra "-" solo si quitarla no desconecta el resto de la forma y no es la última manzana restante; clicarla la quita. Ninguna combinación de clics permite llegar a 0 bloques ni a dos grupos de manzanas desconectados entre sí.
- [x] Con "Define Plazas" seleccionado: el clic alterna `plazaCells` exactamente como hoy, pero solo sobre celdas presentes en `customBlockCells` — un hueco no responde al clic.
- [x] Al pulsar "Exit Customize", `useCustomGrid` pasa a `false`, reaparecen "Grid Width"/"Grid Height" con los valores que tenían antes de entrar en Customize, y la ciudad vuelve a generarse como el rectángulo de siempre. Volver a pulsar "Customize" después arranca de nuevo desde un único bloque central, sin recordar la forma anterior.
- [x] Generar una ciudad con `useCustomGrid == true` y una forma no rectangular (p. ej. en L o en cruz) produce: asfalto/aceras/marcas viales solo donde hay manzana o calle adyacente a ella (ningún tramo de calle fuera del contorno de la forma); edificios/plazas/props solo en manzanas reales; una red de tráfico navegable en Play mode con coches deteniéndose correctamente en los puntos muertos del contorno y semáforos solo en intersecciones con 4 manzanas vecinas reales; una red peatonal en la que los peatones no salen del contorno de la forma; y un minimapa que encuadra ajustadamente el bounding box de las manzanas reales.
- [x] Un `CustomPlaceEntry` cuyo `blockCell` deja de existir en `customBlockCells` (tras quitar ese bloque) bloquea ambos botones de Build y resalta la tab/card "Custom Places" en rojo con un mensaje explicando el conflicto, sin borrar ni recolocar el dato de la entrada.
- [x] El picker `SingleSelectQuadrant` de cada entrada de Custom Place, mientras `useCustomGrid` es `true`, pinta los huecos semitransparentes y no permite seleccionarlos.
- [x] Nuevos tests EditMode cubren: `CityGeneratorGrid.IsValidAddition`/`CanRemoveWithoutSplitting` (casos de contigüidad, incluyendo que la adyacencia diagonal no cuenta) y `TrafficNetwork.BuildFromBlockCells`/`PedestrianNetwork.BuildFromBlockCells` sobre una forma irregular pequeña.
- [x] `docs/architecture/editor-tool.md` y `CHANGELOG.md` reflejan el nuevo modo Customize y el flag `useCustomGrid`.

## Decisiones tomadas y descartadas

- **Una sola spec, no dividida en varias.** Es conceptualmente una única feature indivisible: el editor de forma sin que la generación la respete (o viceversa) no sería verificable ni usable por separado. La complejidad se gestiona con un plan de 13 pasos ordenados, cada uno dejando el sistema en un estado conocido (aunque temporalmente inconsistente en los pasos 4-5, señalado explícitamente), en vez de trocear en specs sin valor independiente.
- **Silueta real no rectangular, no "rectángulo de calles fijo con manzanas vacías por dentro".** Es la lectura literal de "sin tener que ser un grid cuadrado o rectangular" del usuario; la alternativa más barata (mantener el rectángulo de calles siempre) no habría cumplido el objetivo declarado, solo lo habría parecido desde la card de manzanas.
- **`useCustomGrid` es un interruptor entre dos modos mutuamente excluyentes, no una migración de datos.** `gridWidth`/`gridHeight` y `customBlockCells` coexisten siempre en `GeneralSettings`; cuál de los dos manda depende solo del flag. Evita tener que decidir cómo "convertir" un rectángulo en una forma personalizada de forma automática (relleno, recorte, etc.) — decisión que el usuario no pidió y que habría añadido ambigüedad.
- **"Exit Customize" descarta la forma personalizada; "Customize" siempre arranca de un único bloque central.** Confirmado explícitamente por el usuario. Alternativa descartada: conservar la forma en memoria tras salir, que habría necesitado un tercer estado ("tengo datos guardados" vs. "modo activo") no pedido y sin mecanismo claro para "abandonar del todo" una forma guardada.
- **Contigüidad y mínimo 1 bloque se imponen en la UI (solo se pintan +/- válidos), no como error de validación posterior.** Confirmado explícitamente por el usuario para el caso de islas. Mantiene consistente que nunca se puede llegar, editando desde la UI, a un estado de forma estructuralmente inválido — a diferencia del conflicto de Custom Place huérfano (ver debajo), que sí se deja como validación.
- **Un Custom Place huérfano (bloque eliminado bajo él) se permite y se resuelve como error de validación bloqueante, sin recolocar ni limpiar el dato.** Confirmado explícitamente por el usuario, replicando el mecanismo ya existente para cuando un resize de `gridWidth`/`gridHeight` deja una entrada fuera de rango (spec 06). Se prefiere este criterio, distinto del de islas, porque bloquear el "-" también sobre cualquier bloque con un Custom Place habría acoplado dos conceptos (forma del área vs. contenido de una manzana) que hoy son independientes en el resto de la tool.
- **Plazas se pierden siempre al entrar en Customize** (no se preservan por coordenada). Confirmado explícitamente por el usuario: coherente con "no hay manzanas, luego no puede haber plazas" al arrancar desde un único bloque.
- **El lienzo de edición de Customize es siempre `MaxGridSize × MaxGridSize` (hoy 10×10), fijo, sin posibilidad de ampliarlo desde dentro del modo.** Confirmado explícitamente por el usuario. Descartado un lienzo que arrancara del tamaño `gridWidth × gridHeight` previo (propuesta inicial): más simple de razonar, pero limitaría artificialmente el tamaño máximo de una forma personalizada a lo que tuviera el rectángulo antes de entrar.
- **Semáforos solo en intersecciones con 4 manzanas vecinas reales**, igual que hoy. Confirmado explícitamente por el usuario; una intersección con 1-3 vecinos se trata como una intersección de borde de hoy (mecanismo de cruce no semaforizado ya existente), sin inventar un tercer tipo de intersección.
- **El minimapa encuadra el bounding box de las celdas realmente usadas, no el lienzo 10×10 completo.** Evita un minimapa mayoritariamente vacío para una forma pequeña o desplazada hacia una esquina del lienzo — coherente con que el minimapa siempre ha reflejado el tamaño real de la ciudad generada, nunca un tamaño de lienzo/edición.
- **`BuildingBuilder`/`PlazaBuilder`/`StreetPropsBuilder` no cambian.** Ya iteran `IReadOnlyList<BlockCell> blocks` (nunca `gridWidth × gridHeight` en bucle), así que reciben la lista de manzanas reales sin lógica nueva — mismo patrón que spec 06 al introducir `reservedSlots`. Se confirma explícitamente en el paso 5 del plan antes de darlo por hecho.
- **Diagonal no cuenta como adyacencia/contigüidad**, solo N/S/E/O. Consistente con que las calles del proyecto son siempre ortogonales (nunca diagonales) en todo el resto del pipeline.
- **Un hueco interior no conectado a nada (rodeado de manzanas reales, tipo "donut") es válido** — la única prohibición es que las manzanas reales entre sí formen un único componente conexo, no que no existan huecos. No se pidió lo contrario, y prohibirlo añadiría una comprobación extra sin ningún beneficio declarado.
