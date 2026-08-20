# SPEC 02 — City Generator como package instalable por git URL

> **Estado:** Aprobado
> **Depende de:** SPEC 01 (City Generator Tool)
> **Fecha:** 2026-08-20
> **Objetivo:** Convertir `Assets/CityGenerator/` en un package embebido `com.santiandrade.citygenerator` instalable desde Unity con "Install package from git URL", con sus assets demo incluidos, documentación bilingüe en la raíz del repo y un flujo de versionado por tags SemVer que permita actualizar reinstalando la URL con la nueva versión.

## Por qué existe esta spec

La SPEC 01 dejó la herramienta funcionalmente completa y con `.asmdef` propios, pero seguía viviendo dentro de `Assets/` y distribuyéndose copiando una carpeta a mano. Esta spec cierra el último tramo: que cualquiera pueda instalarla desde el Package Manager con una URL y actualizarla cuando publiques una versión nueva.

Un dato técnico condiciona todo el diseño del versionado: **Unity no ofrece actualización real para paquetes instalados por git URL.** Al instalar, el Package Manager fija el commit resuelto en `Packages/packages-lock.json`. Con `#v1.2.0` en la URL, actualizar significa volver a añadir la URL con `#v1.3.0`; sin revisión en la URL, volver a añadirla re-resuelve al último commit de la rama por defecto. El botón "Update to X" sólo existe para paquetes de un registro (scoped registry / OpenUPM). Por eso el flujo documentado es "reinstalar la URL con el tag nuevo" y no "pulsar Update".

## Scope

**Dentro:**

- **Creación del package embebido** `Packages/com.santiandrade.citygenerator/` con su `package.json`: `name` `com.santiandrade.citygenerator`, `displayName` `City Generator`, `version` `1.0.0`, `unity` `6000.0`, `license` `MIT`, `author`, `documentationUrl`/`changelogUrl` apuntando al repo, y `dependencies`: `com.unity.inputsystem` y `com.unity.cloud.gltfast`.
- **Movimiento de la tool** desde `Assets/CityGenerator/` a `Packages/com.santiandrade.citygenerator/{Runtime,Editor}`, con sus `.meta` (los GUID se conservan, `City.unity` no se rompe). Los dos `.asmdef` mantienen nombre, namespace y referencias actuales.
- **Movimiento del contenido demo referenciado** a `Packages/com.santiandrade.citygenerator/DefaultAssets/{Prefabs,Materials,Meshes,Models,Animations,Input}`: los 22 prefabs, los 14 materiales de `Materials/City/`, las 17 mallas extraídas, `PlayerAnimator.controller`, `InputSystem_Actions.inputactions` y únicamente los modelos de `Assets/Models/` que algún prefab usa.
- **Los modelos huérfanos se quedan en `Assets/Models/`** del repo de desarrollo, fuera del package (CLAUDE.md los conserva a propósito para uso futuro).
- **`CityGeneratorDefaultAssets` reescrito** para cargar por rutas `Packages/com.santiandrade.citygenerator/DefaultAssets/...`. Deja de ser el fichero no portable de la herramienta: en cualquier proyecto que instale el package, esas rutas existen y la ventana abre con todos los prefabs asignados.
- **Documentación bilingüe en la raíz del repo**: `README.md` (inglés, por defecto) y `README.es.md` (traducción íntegra), con enlace mutuo en la primera línea. Contenido: qué es la tool, instalación por git URL, cómo actualizar, requisitos, requisitos de tus propios prefabs, qué no hace la tool, ajustes de proyecto recomendados y escalado de tráfico. Absorben el `README.md` actual de `Assets/CityGenerator/`.
- **README corto dentro del package** que apunta al del repo, para quien lo lea desde `Packages/`.
- **Sistema de versionado**: `CHANGELOG.md` en la raíz del package en formato Keep a Changelog, versiones SemVer, un tag git `vX.Y.Z` por release. `LICENSE.md` (MIT) en la raíz del repo y dentro del package.
- **Script de release en el Editor**, en `Assets/Editor/` del repo de desarrollo (fuera del package): menú `Tools > City Generator > Release`, elige major/minor/patch, actualiza `version` en `package.json`, abre la entrada nueva del `CHANGELOG.md` y muestra el comando `git tag` a ejecutar.
- **Publicación de `v1.0.0`**: commit, tag, push y GitHub Release con las notas del CHANGELOG.
- **Verificación en un proyecto Unity limpio**: instalar por `https://github.com/santiandrade/Unity-CityGenerator.git?path=/Packages/com.santiandrade.citygenerator#v1.0.0` y generar una ciudad completa desde el package instalado.

**Fuera de alcance (para futuras specs):**

- Publicación en OpenUPM o cualquier scoped registry — es la única vía para tener botón "Update to X" nativo en el Package Manager, y se descarta por ahora.
- Workflow de GitHub Actions que valide el tag o publique la Release automáticamente.
- Publicación en el Asset Store (empaquetado `.unitypackage`, ficha de venta, revisión).
- Cambios de comportamiento de la herramienta: esta spec no toca algoritmos de generación, builders, ni el runtime de tráfico. Sólo mueve ficheros, reescribe rutas y añade metadatos.
- Resolver los seguimientos abiertos de la SPEC 01 (rejillas grandes, rejillas no 3×3, rendimiento O(n²)).
- Eliminar la dependencia de glTFast sustituyendo `Fountain.glb` por una malla extraída.
- Hacer los materiales demo independientes del pipeline de render: siguen siendo URP/Lit y se verán magenta en Built-in/HDRP. El README lo advierte.
- **Escena demo dentro del package.** Se valoró incluir una `DefaultAssets/Scenes/DemoCity.unity` de escaparate; se descarta para no congelar en cada release una escena que hay que regenerar cada vez que cambie la generación. El usuario genera su primera ciudad con un clic desde la ventana, que ya abre con todos los prefabs demo asignados.
- Traducciones a otros idiomas más allá de inglés y español.

## Modelo de datos

Esta spec **no introduce ni modifica ninguna estructura C#**: `CityGeneratorSettings` y todas las clases de la SPEC 01 se quedan exactamente como están. Lo que aparece es un manifiesto de package, un árbol de carpetas nuevo y un mapa de movimientos.

### `Packages/com.santiandrade.citygenerator/package.json`

```json
{
  "name": "com.santiandrade.citygenerator",
  "version": "1.0.0",
  "displayName": "City Generator",
  "description": "Editor tool that procedurally generates a full city — roads, sidewalks, markings, buildings, plazas, street furniture, traffic lights and autonomous traffic — into a new or existing scene.",
  "unity": "6000.0",
  "license": "MIT",
  "author": { "name": "Santi Andrade", "url": "https://github.com/santiandrade" },
  "documentationUrl": "https://github.com/santiandrade/Unity-CityGenerator#readme",
  "changelogUrl": "https://github.com/santiandrade/Unity-CityGenerator/blob/main/Packages/com.santiandrade.citygenerator/CHANGELOG.md",
  "keywords": ["city", "generator", "procedural", "traffic", "editor-tool"],
  "dependencies": {
    "com.unity.inputsystem": "1.20.0",
    "com.unity.cloud.gltfast": "6.19.0"
  }
}
```

Las versiones de dependencia son las que ya usa este proyecto (`Packages/manifest.json`) y actúan como mínimo, no como pin: Unity resuelve hacia arriba si el proyecto destino tiene una más nueva.

### Árbol del package

```
Packages/com.santiandrade.citygenerator/
├── package.json
├── README.md              (corto, apunta al README del repo)
├── CHANGELOG.md           (Keep a Changelog; Unity lo muestra en el Package Manager)
├── LICENSE.md             (MIT)
├── Runtime/
│   ├── CityGenerator.Runtime.asmdef
│   └── CarAgent.cs, PlayerController.cs, ThirdPersonCamera.cs,
│       TrafficNetwork.cs, TrafficManager.cs, TrafficLight.cs,
│       TrafficLightIntersection.cs, PerformanceBootstrap.cs
├── Editor/
│   ├── CityGenerator.Editor.asmdef
│   ├── ToolThumbnail.png
│   └── CityGenerator*.cs  (los 17 ficheros de la tool)
└── DefaultAssets/
    ├── Prefabs/       Buildings/ Characters/ Floors/ Props/ Vegetation/ Vehicles/
    ├── Materials/     (los 14 de Materials/City/)
    ├── Meshes/        (las 17 mallas extraídas)
    ├── Models/        (sólo los referenciados + sus Textures/)
    ├── Animations/    PlayerAnimator.controller
    └── Input/         InputSystem_Actions.inputactions
```

### Mapa de movimientos

| Origen | Destino |
|---|---|
| `Assets/CityGenerator/Runtime/` | `Packages/com.santiandrade.citygenerator/Runtime/` |
| `Assets/CityGenerator/Editor/` | `Packages/com.santiandrade.citygenerator/Editor/` |
| `Assets/CityGenerator/README.md` | absorbido por el `README.md` de la raíz del repo (se borra) |
| `Assets/Prefabs/` | `…/DefaultAssets/Prefabs/` |
| `Assets/Materials/City/` | `…/DefaultAssets/Materials/` |
| `Assets/Meshes/` | `…/DefaultAssets/Meshes/` |
| `Assets/Animations/` | `…/DefaultAssets/Animations/` |
| `Assets/InputSystem_Actions.inputactions` | `…/DefaultAssets/Input/` |
| modelos de `Assets/Models/` **referenciados** + sus texturas | `…/DefaultAssets/Models/` |
| modelos de `Assets/Models/` **huérfanos** | se quedan en `Assets/Models/` |
| `Assets/Scenes/City.unity`, `Assets/Settings/` | sin cambios |

Todos los movimientos van **con su `.meta`**, así que los GUID se conservan y `City.unity` no pierde ninguna referencia.

**Cómo se decide qué modelo es huérfano:** no a ojo — con `AssetDatabase.GetDependencies` sobre los 22 prefabs y `PlayerAnimator.controller`. Lo que aparezca en esa lista se mueve; el resto se queda. El resultado se anota en el CHANGELOG de `v1.0.0`.

### Ficheros nuevos en la raíz del repo

| Fichero | Contenido |
|---|---|
| `README.md` | Documentación principal en inglés. Primera línea: enlace a `README.es.md` |
| `README.es.md` | Traducción íntegra. Primera línea: enlace a `README.md` |
| `LICENSE.md` | MIT (copia idéntica dentro del package) |
| `Assets/Editor/CityGeneratorReleaseWindow.cs` | Script de release. Fuera del package, en `Assembly-CSharp-Editor` |

### Formato del `CHANGELOG.md`

Keep a Changelog + SemVer, una sección por versión con encabezado `## [1.0.0] - 2026-08-20` y las categorías `Added` / `Changed` / `Fixed` / `Removed`. El script de release inserta la sección nueva vacía bajo `## [Unreleased]`.

## Plan de implementación

Cada paso deja el proyecto compilando y la herramienta funcionando, y es commitable por sí solo.

1. **Esqueleto del package.** Crear `Packages/com.santiandrade.citygenerator/` con `package.json` (contenido de la sección anterior), `LICENSE.md` (MIT), `CHANGELOG.md` con sólo la sección `## [Unreleased]` y un `README.md` corto que enlaza al del repo. Todavía sin código dentro.
   *Verificación:* abrir Unity → el Package Manager lista "City Generator 1.0.0" en **In Project > Custom**, sin errores.

2. **Mover el código de la herramienta.** Con Unity cerrado, `git mv` de `Assets/CityGenerator/Runtime` y `Assets/CityGenerator/Editor` (con sus `.meta`) a la raíz del package. Borrar `Assets/CityGenerator/README.md` y la carpeta vacía con su `.meta`.
   *Verificación:* Unity recompila sin errores; `Tools > City Generator` abre la ventana con la miniatura y los prefabs demo asignados (siguen en `Assets/Prefabs`, aún válidos); `City.unity` abre sin *missing script*.

3. **Mover el contenido demo y reescribir las rutas.** Calcular con `AssetDatabase.GetDependencies` qué modelos y texturas usan los 22 prefabs y `PlayerAnimator.controller`. `git mv` de esos modelos, más `Prefabs/`, `Materials/City/`, `Meshes/`, `Animations/` e `InputSystem_Actions.inputactions`, a `DefaultAssets/` según el mapa de movimientos. Reescribir las 20 rutas de `CityGeneratorDefaultAssets.ApplyTo` a `Packages/com.santiandrade.citygenerator/DefaultAssets/...` y sustituir su comentario de cabecera: ya no es el fichero no portable, ahora carga assets del propio package.
   *Verificación:* `City.unity` sigue abriendo con todos los prefabs resueltos (GUID intactos); "Reset to Defaults" rellena los 20 campos; "Build City in New Scene" genera una ciudad completa.

4. **`README.md` en la raíz (inglés).** Absorbe el contenido del antiguo `Assets/CityGenerator/README.md` (requisitos, requisitos de tus prefabs, qué no hace la tool, ajustes recomendados, escalado de tráfico) y añade las secciones nuevas: **Installation** (la git URL con `?path=` y `#vX.Y.Z`, y la variante sin tag), **Updating** (reinstalar la URL con el tag nuevo; nota explícita de que Unity no ofrece botón "Update" para paquetes git), **Requirements** (Unity 6000.0+, Input System, glTFast, layer `Vehicle`), **Demo content** (viene incluido y es read-only al instalarse), **Render pipeline** (materiales URP/Lit, magenta en Built-in/HDRP) y **License**.
   *Verificación:* renderiza bien en GitHub; los enlaces internos funcionan.

5. **`README.es.md`.** Traducción íntegra del anterior, con enlace mutuo en la primera línea de ambos ficheros.
   *Verificación:* mismas secciones, mismo orden, sin secciones huérfanas.

6. **Script de release.** `Assets/Editor/CityGeneratorReleaseWindow.cs`, menú `Tools > City Generator > Release`: lee la `version` actual del `package.json`, ofrece major/minor/patch con vista previa del número resultante, escribe la nueva `version`, convierte `## [Unreleased]` en `## [X.Y.Z] - YYYY-MM-DD` y crea un `## [Unreleased]` vacío encima, y muestra el comando `git tag vX.Y.Z && git push origin vX.Y.Z` con un botón de copiar. No ejecuta git.
   *Verificación:* ejecutarlo en seco sobre una versión de prueba, comprobar el resultado y revertir con git.

7. **Actualizar la documentación del repo.** `CLAUDE.md`: la sección "Structure" describe rutas `Assets/` que dejan de existir — reescribirla con la estructura de package, y añadir cómo se versiona y publica. `docs/technical-review.md`: actualizar las rutas citadas. `specs/01-city-generator-tool.md`: añadir una línea en su cabecera apuntando a esta spec, sin reescribir su contenido histórico.
   *Verificación:* ninguna ruta `Assets/CityGenerator/` ni `Assets/Prefabs/` superviviente en la documentación (`grep`).

8. **Release `v1.0.0`.** Escribir la sección `## [1.0.0]` del CHANGELOG (contenido: primera publicación como package; lista de qué modelos quedaron fuera por huérfanos). Commit, `git tag v1.0.0`, `git push --tags`, y crear la GitHub Release con esas notas.
   *Verificación:* el tag existe en GitHub y la Release está publicada.

9. **Verificación en un proyecto Unity limpio.** Proyecto Unity 6 nuevo → Package Manager → *Install package from git URL* → `https://github.com/santiandrade/Unity-CityGenerator.git?path=/Packages/com.santiandrade.citygenerator#v1.0.0`. Crear el layer `Vehicle`, abrir la ventana, generar una ciudad 3×3 con tráfico y entrar en Play.
   *Verificación:* la ventana abre con los 20 campos rellenos; la ciudad se genera sin errores; los coches circulan y los semáforos ciclan; el jugador se mueve. Sin la layer `Vehicle`, el aviso de consola aparece y la generación continúa.

## Criterios de aceptación

- [ ] `Packages/com.santiandrade.citygenerator/package.json` existe con `name` `com.santiandrade.citygenerator`, `version` `1.0.0`, `unity` `6000.0`, `license` `MIT` y las dependencias `com.unity.inputsystem` y `com.unity.cloud.gltfast`.
- [ ] El Package Manager de este proyecto lista "City Generator" en **In Project > Custom** con su versión, descripción, README y CHANGELOG visibles.
- [ ] No queda ningún fichero en `Assets/CityGenerator/` ni la carpeta misma.
- [ ] `Assets/` contiene únicamente `Scenes/City.unity`, `Settings/`, `Editor/CityGeneratorReleaseWindow.cs` y los modelos huérfanos de `Models/`.
- [ ] `Assets/Scenes/City.unity` abre sin *missing script* y sin ninguna referencia de prefab, material o malla rota.
- [ ] `Tools > City Generator > Open` abre la ventana con la miniatura y los 20 campos de prefab rellenos por `CityGeneratorDefaultAssets`, cargados desde rutas `Packages/com.santiandrade.citygenerator/DefaultAssets/...`.
- [ ] "Build City in New Scene" genera una ciudad completa sin errores de consola, con el package como única fuente de assets.
- [ ] `grep -r "Assets/CityGenerator\|Assets/Prefabs" --include=*.cs --include=*.md` no devuelve ninguna ruta viva (fuera de `specs/01`, que es histórico).
- [ ] `README.md` y `README.es.md` existen en la raíz del repo, cada uno enlaza al otro en su primera línea, y ambos contienen las mismas secciones en el mismo orden.
- [ ] `README.md` documenta la git URL de instalación exacta, incluyendo `?path=/Packages/com.santiandrade.citygenerator` y el sufijo `#vX.Y.Z`.
- [ ] `README.md` explica que Unity no ofrece botón "Update" para paquetes git y que actualizar consiste en reinstalar la URL con el tag nuevo.
- [ ] `LICENSE.md` (MIT) existe en la raíz del repo y dentro del package, con el mismo texto.
- [ ] `CHANGELOG.md` del package tiene una sección `## [1.0.0] - 2026-08-20` y una `## [Unreleased]` vacía encima.
- [ ] `Tools > City Generator > Release` sube `version` en `package.json`, cierra la sección `[Unreleased]` con el número y la fecha nuevos, crea una `[Unreleased]` vacía, y muestra el comando `git tag` sin ejecutarlo.
- [ ] El tag `v1.0.0` existe en el remoto y su GitHub Release está publicada con las notas del CHANGELOG.
- [ ] En un proyecto Unity 6 limpio, instalar por la git URL con `#v1.0.0` deja el package en `Packages/` y la ventana abre con los 20 campos rellenos.
- [ ] En ese proyecto limpio, una ciudad 3×3 con tráfico se genera sin errores, los coches circulan, los semáforos ciclan y el jugador se mueve en Play.
- [ ] En ese proyecto limpio, sin layer `Vehicle`, la generación no falla y el aviso aparece en consola.
- [ ] `CLAUDE.md` describe la estructura de package y no menciona rutas `Assets/CityGenerator/`.

## Decisiones tomadas y descartadas

- **Sí: package embebido en `Packages/com.santiandrade.citygenerator/`.** Es el patrón estándar de desarrollo de packages en Unity: este mismo proyecto consume la herramienta *como package*, con las mismas rutas y los mismos límites que verá quien la instale, así que una rotura de portabilidad se detecta aquí y no en el proyecto del usuario.
- **No: dejarla en `Assets/CityGenerator/` con un `package.json` y `?path=/Assets/CityGenerator`.** Funciona y no obliga a mover nada, pero el proyecto de desarrollo seguiría compilando la tool desde `Assets/`, sin ejercitar nunca el camino real de instalación.
- **No: repositorio aparte cuya raíz sea el package.** Da la URL más limpia (sin `?path=`), a costa de mantener dos repos sincronizados para un proyecto de una sola persona.
- **Sí: assets demo dentro del package como contenido normal.** Decisión revisada durante la definición: se planteó primero `Samples~` importable y se cambió a contenido normal. Motivo: `Samples~` obliga a duplicar el contenido (Unity ignora las carpetas terminadas en `~`, así que este proyecto no podría usar los prefabs desde ahí) y a mantener un script de sincronización. Con contenido normal hay una única fuente de verdad y la ventana abre lista para generar nada más instalar.
- **Consecuencia aceptada: los assets demo son read-only en el proyecto del usuario.** Podrá instanciarlos pero no editar un prefab demo sin copiarlo antes a su `Assets/`. Aquí no aplica, porque un package embebido sí es editable. Queda documentado en el README.
- **Consecuencia aceptada: `CityGeneratorDefaultAssets` deja de ser el fichero no portable.** La SPEC 01 lo aisló deliberadamente para poder borrarlo al empaquetar; con los assets dentro del package, sus rutas son válidas en cualquier proyecto y el fichero pasa a ser parte normal de la herramienta.
- **Sí: sólo los modelos referenciados entran en el package.** Los huérfanos de `Assets/Models/` que CLAUDE.md conserva a propósito para uso futuro se quedan en el repo: no tiene sentido que cada instalación arrastre modelos que ningún prefab usa. El criterio no es a ojo, es `AssetDatabase.GetDependencies`.
- **Sí: `com.unity.cloud.gltfast` como dependencia declarada.** `Fountain.prefab` se apoya en un `.glb` que sin glTFast no importa, y ahora ese prefab viaja siempre dentro del package. La alternativa —sustituir el `.glb` por una malla extraída y quitar la dependencia— queda fuera de alcance.
- **No: declarar URP como dependencia.** Los 14 materiales demo son URP/Lit y se verán magenta en Built-in o HDRP, pero forzar la instalación de un pipeline de render a todo el mundo contradice la decisión de la SPEC 01 de eliminar el `Global Volume` precisamente para no depender de ninguno. Se documenta en el README en vez de imponerlo.
- **Sí: `unity: "6000.0"` y no `6000.5`.** Fijar la versión exacta de desarrollo excluiría versiones de Unity 6 LTS anteriores sin motivo técnico conocido; nada del código usa API posterior a 6000.0.
- **Sí: tags SemVer `vX.Y.Z` + `CHANGELOG.md`, actualizando por reinstalación de la URL.** Es lo único que Unity permite de verdad con paquetes git: al instalar, fija el commit resuelto en `packages-lock.json`, y no existe botón "Update to X" salvo para paquetes de un registro.
- **No: publicar en OpenUPM o en un scoped registry.** Es la única vía para tener actualización nativa desde el Package Manager, pero cambia la forma de instalar (deja de ser "Install package from git URL", que es el requisito de partida) y añade infraestructura externa. Reevaluable en otra spec.
- **No: GitHub Actions que valide el tag o publique la Release.** Para un repo de un solo mantenedor con releases esporádicas, el script de Editor cubre el trabajo repetitivo sin CI que mantener.
- **Sí: script de release como ventana de Editor, fuera del package.** El bump y el CHANGELOG se hacen sin salir de Unity, y la herramienta de release no viaja al proyecto del usuario, que no tiene nada que versionar. No ejecuta git: enseña el comando y lo ejecuta la persona, para que el tag nunca se cree por accidente.
- **Sí: `Tools > City Generator > Open` en vez de dejar `Tools > City Generator` como ítem hoja.** Descubierto durante la implementación del paso 6: Unity no permite que una ruta de menú sea a la vez un comando (`Tools/City Generator`, abre la ventana) y un contenedor de submenú (`Tools/City Generator/Release`); al añadir el segundo, el primero deja de aparecer en el menú. Se resuelve anidando también la ventana principal bajo el mismo submenú (`Tools/City Generator/Open`) en vez de sacar `Release` a una ruta sin anidar, para que `Tools > City Generator` sea un único punto de entrada con todas las acciones de la herramienta agrupadas debajo.
- **Sí: `README.md` completo en la raíz del repo y README corto dentro del package.** La raíz es la portada que GitHub enseña y lo primero que lee quien descubre el proyecto; duplicar el texto íntegro en ambos sitios crearía dos copias que se desincronizan en cada release.
- **Sí: `README.es.md` como traducción íntegra, no como resumen.** El README es también la única documentación de la herramienta, así que un resumen dejaría al lector en español sin la mitad de la información técnica.
- **No: escena demo dentro del package.** Se valoró incluir una `DefaultAssets/Scenes/DemoCity.unity` de escaparate (1,3 MB, ya sin mallas de ProBuilder embebidas). Se descarta porque congelaría en cada release una escena que habría que regenerar cada vez que cambie la generación, y CLAUDE.md define `City.unity` como banco de pruebas desechable. La ventana abre con todos los prefabs asignados, así que la primera ciudad está a un clic.
- **Sí: mover con `git mv` y Unity cerrado, llevando los `.meta`.** Conserva los GUID, que es lo que impide que `City.unity` pierda referencias; el historial de git sigue el rastro de los ficheros.
- **Sí: esta spec no toca el comportamiento de la herramienta.** Sólo mueve ficheros, reescribe rutas y añade metadatos, para que cualquier regresión sea atribuible al empaquetado y no a un cambio de generación colado por el camino.

## Riesgos identificados

| Riesgo | Mitigación |
|---|---|
| El movimiento masivo de assets rompe referencias y deja `City.unity` con prefabs perdidos. | Mover con `git mv` y Unity cerrado, siempre con el `.meta` al lado: Unity serializa por GUID, no por ruta. Paso 3 del plan verifica la escena explícitamente antes de commitear. |
| Un modelo se clasifica mal como huérfano y un prefab demo llega roto al usuario. | La lista sale de `AssetDatabase.GetDependencies` sobre los 22 prefabs y el controller, no de inspección manual. El paso 9 (proyecto limpio) es la red de seguridad: un prefab con el modelo ausente se ve a la primera generación. |
| El usuario no puede editar los prefabs demo porque el package es read-only, y lo interpreta como un fallo. | Documentado en el README (sección *Demo content*), con la salida indicada: copiar el prefab a su propio `Assets/` y asignarlo en la ventana. |
| Los materiales demo salen magenta en Built-in o HDRP. | Advertencia explícita en el README (sección *Render pipeline*). No se declara URP como dependencia a propósito; el código de la herramienta sigue siendo agnóstico. |
| El usuario espera un botón "Update" en el Package Manager y no lo encuentra. | Sección *Updating* del README con el procedimiento real y la razón. Es el punto que más fricción va a generar y por eso ocupa sección propia, no una nota al pie. |
| `package.json` y el tag se desincronizan (tag `v1.2.0` apuntando a un `package.json` que dice `1.1.0`). | El script de release escribe la versión y muestra el comando de tag correspondiente en el mismo paso. No hay validación automática — es el precio de descartar el workflow de CI, y queda como seguimiento si llega a pasar. |
| `PerformanceBootstrap` fuerza `targetFrameRate = 60` y `vSyncCount = 0` en el proyecto del usuario nada más instalar el package, aunque no genere ninguna ciudad. | Comportamiento heredado de la SPEC 01, ahora más visible al distribuirse. Se documenta en el README; cambiarlo queda fuera de alcance de esta spec. |
| El proyecto limpio de verificación no reproduce condiciones del usuario real (otra versión de Unity, otro pipeline). | La verificación cubre el camino principal: Unity 6, URP, tráfico y jugador. Otras combinaciones quedan sin probar y así se anota. |

## Lo que **no** entra en esta spec

- Publicación en OpenUPM o cualquier scoped registry (la única vía para actualización nativa desde el Package Manager).
- Workflow de GitHub Actions para validar el tag o publicar la Release.
- Publicación en el Asset Store.
- Cualquier cambio de comportamiento de la herramienta: algoritmos de generación, builders o runtime de tráfico.
- Los seguimientos abiertos de la SPEC 01: rejillas grandes, rejillas distintas de 3×3, rendimiento O(n²) del solapamiento.
- Eliminar la dependencia de glTFast sustituyendo `Fountain.glb` por una malla extraída.
- Hacer los materiales demo independientes del pipeline de render.
- Escena demo dentro del package.
- Traducciones más allá de inglés y español.

Cada una de ellas, si llega, va en su propia spec.
