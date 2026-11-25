# SPEC 24 — Guardar y cargar los ajustes de cada ciudad como asset

> **Estado:** Implemented
> **Depende de:** SPEC 01 (City Generator Tool), SPEC 07 (Minimap HUD: carpeta por escena), SPEC 21 (Districts)
> **Fecha:** 2026-11-25
> **Objetivo:** Que los ajustes con los que se generó cada ciudad queden guardados en un asset del proyecto, y que se puedan volver a cargar en la ventana para regenerar esa ciudad igual, sin depender de lo que la ventana recuerde.

---

## Contexto y problema

Hoy los ajustes del generador solo existen en un campo serializado de la propia ventana (`CityGeneratorWindow.settings`). Unity los guarda con el layout del Editor (`UserSettings/Layouts/*.dwlt`), así que sobreviven a cerrar y abrir Unity. Pero:

- **Solo hay un juego de ajustes.** Al configurar y generar una segunda ciudad se pierden los de la primera. Para regenerarla igual hay que volver a introducir a mano semilla, edificios, Districts, Custom Places con su orientación, tráfico, peatones, minimapa y audio. Cualquier diferencia cambia la ciudad.
- **No se comparten.** `UserSettings/` no va en git: otro equipo, otra máquina o un layout reseteado no tienen los ajustes.
- La única forma de persistirlos es *Set Current Selection As Default*, que reescribe código del proyecto demo (`CityGeneratorDefaultAssets`) y sirve para los valores por defecto del paquete, no para cada ciudad.

Caso real (City Races, 2026-11-25): Buenos Aires y Nueva York se generaron con la misma ventana. Ahora solo quedan los ajustes de Nueva York, y para añadir los datos de la SPEC 23 a Buenos Aires hay que reconstruirla configurándola de memoria.

---

## Scope

**Dentro:**

- **Tipo de asset** `CityGeneratorSettingsAsset : ScriptableObject` (`Editor/CityGeneratorSettingsAsset.cs`, ensamblado Editor):
  - Contiene un `CityGeneratorSettings` completo y la versión del paquete con la que se guardó.
  - Se puede crear desde *Assets > Create > City Generator > City Settings* (con los ajustes por defecto del paquete) o desde la ventana.
  - Es solo de Editor: nada en Runtime lo referencia y no entra en las builds.
- **Copia del asset en cada generación** (automática, sin opción para desactivarla):
  - Cada *Build City in New Scene* y cada *Rebuild City in Current Scene* que termine bien escribe los ajustes usados en `<SceneFolder>/<SceneName>/<SceneName>_CityGeneratorSettings.asset`.
  - Es la misma carpeta y la misma convención que `<SceneName>_Minimap.png` (`CityGeneratorMinimapBuilder.SaveSnapshotAsset`), y siempre la misma ruta, así que conserva su GUID entre reconstrucciones.
  - Si la escena no está guardada (sin ruta) no se escribe nada, como el minimapa.
  - Con esto cada ciudad generada tiene siempre su asset al lado, sin depender de que el usuario se acuerde de guardarlo.
- **Barra «Settings» en la ventana**, bajo el banner y antes de las pestañas:
  - Un `ObjectField` con el asset vinculado (o ninguno) y la marca *Modified* cuando la ventana tiene cambios sin guardar respecto a él.
  - **Cargar un asset cualquiera:** elegirlo o arrastrarlo al `ObjectField`. Copia sus ajustes a la ventana y lo vincula. No hay botón *Load*: el propio campo es el selector.
  - **Desvincular:** dejar el `ObjectField` en *None*. La ventana conserva sus ajustes actuales y no carga nada.
  - **Revert:** vuelve a cargar el asset vinculado y descarta los cambios de la ventana. Solo está activo con un asset vinculado y *Modified*.
  - **Load From Scene:** carga `<SceneName>_CityGeneratorSettings.asset` de la escena activa y lo vincula, sin tener que buscarlo en el Project. Solo está activo si ese asset existe.
  - **Confirmación:** cualquier acción que sustituya los ajustes de la ventana (elegir otro asset, *Revert*, *Load From Scene*) pide confirmación si hay cambios sin guardar. Cancelar deja la ventana, el vínculo y el campo como estaban.
  - **Save:** escribe los ajustes de la ventana en el asset vinculado. Solo está activo con un asset vinculado y cambios pendientes.
  - **Save As…:** `SaveFilePanelInProject`. Propone la carpeta de la escena activa si es una ciudad generada, y `Assets/` si no. Después vincula el asset nuevo.
- **Vínculo y cambios pendientes:**
  - El asset vinculado se guarda en un campo serializado de la ventana, así que sobrevive a recargas de dominio y al layout, igual que los ajustes.
  - *Modified* se recalcula cuando cambian los ajustes (el mismo `TrackSerializedObjectValue` que ya refresca la UI dinámica), comparando el JSON de los ajustes de la ventana con el del asset.
  - Una generación vincula la ventana al asset de la escena que acaba de escribir, y la ventana queda sin cambios pendientes.
  - *Reset To Defaults* no desvincula el asset: los ajustes quedan como *Modified*.
- **Copia sin instancias compartidas:**
  - Cargar y guardar copian los ajustes completos con `EditorJsonUtility.ToJson`/`FromJsonOverwrite` sobre un `CityGeneratorSettings` nuevo. `EditorJsonUtility` guarda las referencias a prefabs, clips y el `InputActionAsset` como referencias de asset.
  - La ventana y el asset nunca comparten listas, así que editar en la ventana no modifica el asset hasta pulsar *Save*.
  - Cargar (desde el campo, *Revert* o *Load From Scene*) y *Save* se pueden deshacer (`Undo.RecordObject` sobre la ventana o sobre el asset).
- **Inspector del asset:**
  - Un resumen de solo lectura: cuadrícula, semilla, número de Districts, de Custom Places y de prefabs, y la versión del paquete que lo guardó.
  - Un botón *Open in City Generator* que abre la ventana y carga el asset.
  - No se editan los ajustes en el inspector: se editan en la ventana.
- **Compatibilidad entre versiones:**
  - Al cargar un asset de una versión anterior, los campos que no existían toman su valor por defecto (los inicializadores de `CityGeneratorSettings`, como hoy con el layout). Los renombrados siguen funcionando con `FormerlySerializedAs`.
  - Un asset guardado con una versión **más nueva** del paquete se carga con un aviso en consola.
  - Una referencia que ya no existe (prefab borrado) aparece vacía y la marca el validador, como si se hubiera quitado a mano.
- **Tests EditMode** (`Assets/Tests/EditMode/`):
  - Ida y vuelta ventana → asset → ventana sin pérdidas: semilla, Custom Grid, plazas, Districts, Custom Places con orientación, listas de prefabs, Custom Pedestrians, minimapa, día/noche y audio.
  - Aislamiento: tras cargar un asset, cambiar la ventana no cambia el asset, y al revés.
  - *Revert* deja la ventana exactamente con los ajustes del asset vinculado y quita *Modified*.
  - `BuildAndSaveScene` escribe el asset en la ruta esperada con los ajustes usados. `RebuildInActiveScene` lo sobrescribe en la misma ruta con el mismo GUID. En una escena sin guardar no escribe nada.
  - Generar la ciudad desde un asset cargado da la misma ciudad que desde la ventana original (mismo número de edificios por pool, mismos Custom Places y mismas posiciones con la misma semilla).
- **Documentación:**
  - `docs/user-manual.md`/`.es.md`: la barra *Settings*, el asset que se escribe junto a cada escena y cómo regenerar una ciudad desde él. Con captura.
  - `docs/architecture/editor-tool.md`: el tipo de asset, la copia y el vínculo.
  - README/README.es, fila de la SPEC 24 en `CLAUDE.md` y `CHANGELOG.md` (`## [Unreleased]`).
- **Publicación:** la versión menor siguiente a la de la SPEC 23.

**Fuera de alcance:**

- Editar los ajustes en el inspector del asset.
- Cargar o aplicar solo una parte de los ajustes (por pestaña o por card), o fusionar dos assets.
- Que una ciudad en runtime sepa qué asset la generó. La relación es por ruta, en Editor.
- Detectar que una escena se ha regenerado con ajustes distintos de los de su asset: el asset siempre refleja la última generación.
- Cambiar *Set Current Selection As Default* o `CityGeneratorDefaultAssets`.
- Recuperar los ajustes de una ciudad generada antes de esta spec: no se pueden deducir de la escena.

---

## Modelo de datos

```csharp
// Editor/CityGeneratorSettingsAsset.cs (new)
[CreateAssetMenu(menuName = "City Generator/City Settings", fileName = "CityGeneratorSettings")]
public sealed class CityGeneratorSettingsAsset : ScriptableObject
{
    [SerializeField] internal CityGeneratorSettings settings = new();
    [Tooltip("Package version that last wrote this asset.")]
    [SerializeField] internal string packageVersion;

    internal CityGeneratorSettings CopySettings();          // deep copy via EditorJsonUtility
    internal void Store(CityGeneratorSettings source);      // deep copy in, records packageVersion
}

// Editor/CityGeneratorSettingsAssetPaths.cs (new) — the per-scene path, shared with the minimap
internal static class CityGeneratorSettingsAssetPaths
{
    internal static string ForScene(string scenePath); // <SceneFolder>/<SceneName>/<SceneName>_CityGeneratorSettings.asset, null if unsaved
}

// CityGeneratorWindow gains:
[SerializeField] private CityGeneratorSettingsAsset linkedSettingsAsset;
```

Notas:

- **Un `ScriptableObject` y no un JSON suelto:** las referencias a prefabs y clips quedan como referencias de asset con GUID, sobreviven a mover o renombrar los prefabs, y el `.asset` se versiona como YAML de texto.
- **Por qué está en el ensamblado Editor:** `CityGeneratorSettings` es interno al ensamblado Editor. Sacarlo a Runtime arrastraría a las builds tipos que solo sirven para generar. Un asset cuyo tipo está en Editor es válido en cualquier carpeta de `Assets/`, salvo `Resources/`, que forzaría incluirlo en la build. El manual lo dice.
- **La ruta por escena va en un solo sitio:** la carpeta `<SceneFolder>/<SceneName>/` deja de calcularse dentro de `SaveSnapshotAsset` y pasa a un helper común, para que el minimapa y los ajustes nunca acaben en carpetas distintas.

---

## Plan de implementación

1. `CityGeneratorSettingsAsset` con la copia y su test de ida y vuelta.
2. Helper de rutas por escena, compartido con el minimapa. Escritura del asset al final de `BuildAndSaveScene` y `RebuildInActiveScene`. Tests de ruta, GUID y escena sin guardar.
3. Barra *Settings* en la ventana: vínculo desde el `ObjectField`, *Modified*, *Revert*, *Load From Scene*, *Save*, *Save As…*, confirmación y Undo.
4. Inspector del asset con su resumen y *Open in City Generator*.
5. Test de equivalencia: la misma ciudad desde la ventana y desde el asset.
6. Documentación, `CHANGELOG`, regenerar la escena demo (que deja su asset al lado) y release.

---

## Criterios de aceptación

- Tras *Build City in New Scene*, junto a `City<N>.unity` aparecen `City<N>/City<N>_Minimap.png` y `City<N>/City<N>_CityGeneratorSettings.asset`, y la ventana queda vinculada a ese asset sin *Modified*.
- Cambiar cualquier ajuste marca *Modified*. *Save* lo quita, y el asset (YAML) refleja el cambio.
- Con dos ciudades generadas, *Load From Scene* en cada escena devuelve exactamente sus ajustes. *Rebuild City in Current Scene* sin tocar nada genera la misma ciudad (mismos edificios en las mismas manzanas, mismos Custom Places) y no cambia el GUID del asset.
- Elegir otro asset en el campo, *Revert* o *Load From Scene* con cambios pendientes piden confirmación, y cancelar no cambia nada (tampoco el asset que muestra el campo).
- *Revert* descarta los cambios y vuelve a los ajustes del asset vinculado. Sin cambios pendientes está desactivado.
- Dejar el campo en *None* desvincula sin tocar los ajustes de la ventana.
- Cargar y *Save* se deshacen con Ctrl+Z.
- Cerrar y abrir Unity, o resetear el layout, no pierde ningún ajuste que esté guardado en un asset.
- Un asset con un prefab borrado se carga, el validador marca la entrada vacía y no hay excepciones.
- El inspector del asset muestra su resumen, y *Open in City Generator* lo carga en la ventana.
- Una build de un proyecto con estos assets no incluye ninguno ni da avisos de scripts que faltan.
- Toda la suite EditMode/PlayMode pasa.

---

## Decisiones tomadas y descartadas

- **Tomada: guardar el asset automáticamente en cada generación,** en vez de confiar en un *Save* manual. El problema que resuelve es justo olvidarse de guardar. Cuesta un archivo más por escena, en la carpeta que ya crea el minimapa.
- **Tomada: el asset por escena refleja siempre la última generación,** aunque se haya generado con cambios experimentales. Es «la receta de esta escena». Si se quiere conservar una variante, se guarda con *Save As…* en otra ruta.
- **Descartada: una opción para desactivar el guardado automático.** No hay ningún caso en el que perder la receta de una ciudad sea preferible, y una opción más en *General Options* no aporta nada.
- **Descartada: guardar los ajustes dentro de la escena** (un componente en la raíz de la ciudad). El componente tendría que estar en Runtime, que no puede referenciar `CityGeneratorSettings`, y metería datos de generación en las builds.
- **Descartada: JSON en `ProjectSettings/` o `UserSettings/`.** Seguiría habiendo un solo juego de ajustes por proyecto (o por usuario), y no se vería en el Project junto a su escena.
- **Descartada: que la ventana edite el asset directamente** (sin copia). Cada clic en la ventana ensuciaría el asset y no habría forma de descartar cambios. Además el `SerializedObject` de la ventana, del que dependen todas las cards, apuntaría a otro objeto según el asset cargado.

---

## Riesgos identificados

- **Archivos nuevos en el proyecto del usuario.** Es generado, en la carpeta por escena del minimapa, y con ruta y GUID estables. Hay que documentarlo para que se haga commit del archivo y su `.meta` junto a la escena.
- **Campos nuevos en futuras versiones:** un asset antiguo carga los valores por defecto de esos campos, que pueden no ser los que tenía la ventana del usuario cuando generó la ciudad. Cada spec futura que añada ajustes debe elegir un valor por defecto que reproduzca el comportamiento anterior, como ya hizo la SPEC 21 con los Districts vacíos.
- **Comparar JSON para *Modified*** en cada cambio puede costar algo con listas grandes (cientos de prefabs). Hay que medirlo en la escena demo. Si se nota, se compara solo al soltar el control o con un retardo.

---

## Lo que **no** entra en esta spec

- Ninguna recuperación de ajustes de ciudades ya generadas.
- Cambios en la generación: con los mismos ajustes, la ciudad es idéntica a la de antes.
- Uso por parte de proyectos consumidores. En City Races bastará con regenerar cada ciudad una vez tras actualizar el paquete y hacer commit de su `<Escena>_CityGeneratorSettings.asset` con su `.meta`.

---

## Implementation notes

- **Confirmación sin asset vinculado.** El spec pide confirmar "si hay cambios sin guardar", pero sin asset vinculado no hay contra qué comparar. Se decidió que, sin vínculo, los ajustes de la ventana cuentan como sin guardar cuando difieren de los valores por defecto del paquete (`CityGeneratorDefaultAssets.ApplyTo`): no están en ningún asset, así que perderlos es justo el problema que resuelve esta spec, y una ventana recién abierta (con los valores por defecto) carga un asset sin preguntar. *Open in City Generator* (inspector del asset) también pide confirmación, porque sustituye los ajustes igual que las otras tres acciones.
- **`BuildAndSaveScene` gana una sobrecarga `internal` con ruta de escena explícita**, que es la que usan los tests para generar en una carpeta desechable; la pública sigue calculando el siguiente `City<N>.unity` libre. El asset se escribe tras `SaveScene` en `BuildAndSaveScene` y al final de `RebuildInActiveScene` (después de destruir las ciudades anteriores), no dentro del `try` de la generación.
- **Helpers añadidos más allá del modelo de datos:** `CityGeneratorSettingsAssetPaths` también expone `SceneAssetFolder`, `MinimapForScene` y `EnsureSceneAssetFolder` (lo que usa el minimapa), y `CityGeneratorSettingsAsset` añade `ToJson`/`Clone` (la copia y la comparación de *Modified* pasan por el mismo JSON), `WriteForScene`/`WriteAt` (escritura que reutiliza el asset existente para conservar el GUID) y la comparación de versiones. La ventana expone `LoadSettingsAsset`, `RevertToLinkedAsset`, `SaveSettingsToLinkedAsset` y `SaveSettingsAsAt` como `internal` sin diálogos, para los tests.
- **El inspector es IMGUI** (`CityGeneratorSettingsAssetEditor`, `OnInspectorGUI`), como `CityGeneratorInfoEditor`. El resumen separa los prefabs en Building/Vehicle/Pedestrian Prefabs.
- **Undo de una carga.** Cargar sustituye `settings` entero; para que un Ctrl+Z que cruce una carga reconstruya la UI (las cards están ligadas a la estructura anterior), la ventana guarda un contador serializado `settingsLoadRevision` que entra en el Undo y se compara en `Undo.undoRedoPerformed`.
- **Coste de *Modified*:** el JSON del asset vinculado se cachea por asset y `EditorUtility.GetDirtyCount`, así que cada cambio en la ventana cuesta un solo `ToJson` (el de la ventana). Medido en este proyecto con los ajustes por defecto: el JSON ocupa unos 11 KB y `EditorJsonUtility.ToJson` tarda ~0,08 ms, así que no hizo falta comparar con retardo ni al soltar el control.
- **`Reset()` del asset** aplica los valores por defecto del paquete, así que *Create > City Generator > City Settings* crea un asset con ellos. Comprobado en el Editor: Unity también llama a `Reset()` en `CreateInstance`, y `WriteAt`/`WriteForScene` lo sobrescriben acto seguido con `Store`.
- **Tests de Re-Build en escena desechable.** `RebuildInActiveScene` busca "Minimap HUD" y "Directional Light" por nombre en todas las escenas cargadas, así que los tests abren una escena desechable en modo `Single` (y se saltan con `Assert.Ignore` si hay una escena abierta con cambios sin guardar) y restauran la configuración de escenas en el TearDown. Igualmente, `BuildAndSaveScene` no puede crear su escena aditiva con una escena *Untitled* sin guardar abierta (limitación de Unity previa a esta spec), por lo que su test también abre antes una escena desechable guardada.
- **Release fuera del commit de la spec.** Como en las versiones anteriores, la publicación (`Tools > City Generator > Release`, que sube `package.json` y cierra `## [Unreleased]`) va en un commit «Release vX.Y.Z» aparte en `main` tras el merge, y el tag lo crea una persona. El `CHANGELOG` queda con la entrada en `## [Unreleased]`.
- **Tests PlayMode previos inestables.** En esta máquina, con el Editor en segundo plano, `CarAgentPhysicsRecoveryTests.AfterSettling_RecoveringExits_AndReattachesToAValidNode` y `Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt` fallan igual en `main` sin los cambios de esta spec (dependen de `Time.time` y del ritmo de frames); no tienen relación con esta spec, que solo toca el ensamblado Editor.
- **Escena demo regenerada.** `Assets/Scenes/City.unity` se regeneró con los ajustes por defecto del paquete (los que tenía la ventana) mediante `RebuildInActiveScene`, y dejó `Assets/Scenes/City/City_CityGeneratorSettings.asset` al lado. La captura del manual (`docs/images/manual/settings-bar.png`) muestra la barra vinculada a ese asset con *Modified*.
- **Build sin ejecutar.** El criterio de que una build no incluye los assets se comprobó por inspección, no con una build de player: ninguna escena, prefab ni asset referencia el GUID del asset de la escena demo, nada en `Runtime/` referencia el tipo, y no está bajo `Resources/`.
