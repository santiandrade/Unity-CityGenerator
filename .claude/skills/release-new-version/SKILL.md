---
name: release-new-version
description: Publica una nueva release del paquete com.santiandrade.citygenerator - actualiza main, analiza los cambios desde la última release publicada en GitHub, actualiza el CHANGELOG y package.json si hace falta, crea el tag y publica la GitHub Release apuntando a él. Úsala cuando el usuario diga cosas como "aplica nueva release", "publica una release", "haz el release de esta versión" o invoque /release-new-version.
disable-model-invocation: true
---

# /release-new-version — Publicar una nueva release del City Generator

Esta skill reproduce (y automatiza de extremo a extremo, incluyendo el push y la
GitHub Release) el mismo proceso que hasta ahora se hacía a mano con
`Tools > City Generator > Release` (`Assets/Editor/CityGeneratorReleaseWindow.cs`)
más los pasos de git/GitHub que esa herramienta deja siempre para un humano.

El usuario, al pedir esta skill, ya autoriza explícitamente el ciclo completo
(commit, push a `main`, tag y GitHub Release incluidos) — no pausar a pedir
confirmación para cada uno de esos pasos salvo que el paso 0 detecte algo
inesperado (ver más abajo).

Fichero de versión: `Packages/com.santiandrade.citygenerator/package.json` (campo `version`).
Fichero de notas: `Packages/com.santiandrade.citygenerator/CHANGELOG.md` (formato Keep a
Changelog, sección `## [Unreleased]` siempre presente encima de la última version).
Repo remoto: `https://github.com/santiandrade/Unity-CityGenerator`.

## Paso 0 — Actualizar main y comprobar consistencia tag ↔ release ↔ CHANGELOG

1. `git status` — si hay cambios sin commitear, para y pregunta al usuario qué hacer
   con ellos (no los pierdas ni los mezcles en la release sin decírselo).
2. `git checkout main && git pull origin main`.
3. Lista los tags locales/remotos (`git tag --list "v*" --sort=-v:refname`) y las
   GitHub Releases publicadas (`gh release list`). Deben coincidir 1:1. Si detectas
   un hueco — una versión que aparece en `CHANGELOG.md`/`package.json` (o en el
   historial de commits, p. ej. un commit `Release vX.Y.Z`) pero no tiene tag y/o no
   tiene GitHub Release — créalos primero para esa versión histórica (tag sobre el
   commit correcto, release con las notas de esa sección del CHANGELOG) antes de
   seguir con la nueva. Esto ya ocurrió una vez (v2.4.0 quedó sin tag/release tras
   mergear el PR del Day/Night Cycle) — no lo des por hecho, compruébalo siempre.

## Paso 1 — Analizar los cambios desde la última release publicada

1. Última versión publicada = el tag de la GitHub Release más reciente
   (`gh release list` o `gh release view --json tagName -q .tagName` sobre la
   última, no necesariamente el tag más alto por SemVer si hay huecos históricos).
2. `git log <último_tag>..origin/main --oneline` y revisa el diff real de los
   ficheros relevantes (no solo los mensajes de commit) para entender qué cambió
   de cara al usuario del paquete: nuevas features, cambios de comportamiento,
   fixes, breaking changes, elementos eliminados. Ignora cambios que sean solo de
   este repo de desarrollo y no afecten al paquete instalado (p. ej. tocar
   `Assets/Scenes/City.unity`, `docs/`, `specs/`, tests) salvo que también haya
   cambios reales en `Packages/com.santiandrade.citygenerator/`.
3. Si no hay ningún cambio relevante desde la última release, dilo y para — no
   crees una release vacía.

## Paso 2 — Comprobar/actualizar el CHANGELOG

1. Si `## [Unreleased]` en `CHANGELOG.md` ya tiene contenido (lo habitual: quedó
   redactado como parte del PR que se mergeó), úsalo tal cual como base de la nueva
   sección — no lo reescribas sin motivo.
2. Si `## [Unreleased]` está vacío pero el paso 1 encontró cambios relevantes,
   redacta las entradas que falten siguiendo el estilo Keep a Changelog ya usado en
   el fichero (subsecciones `### Added` / `### Changed` / `### Fixed` / `### Removed`,
   frases completas y específicas, sin jerga de implementación irrelevante para
   quien instala el paquete).
3. No toques secciones de versiones ya publicadas.

## Paso 3 — Determinar el bump de versión (SemVer) y aplicarlo

Regla usada en el historial real de este proyecto (revisa `CHANGELOG.md` si hay
duda, pero esta es la heurística):

- Si hay algo marcado como **Breaking** o una sección `### Removed` que rompe
  compatibilidad → **major**.
- Si no hay breaking pero hay `### Added` (nueva funcionalidad) → **minor**.
- Si solo hay `### Changed` y/o `### Fixed` (sin `### Added` ni breaking) →
  **patch**. (Precedente: v2.2.1 fue solo `Changed` → patch; v2.4.1 fue
  `Changed`+`Fixed` → patch.)

Con la versión siguiente decidida (`X.Y.Z`):

1. Actualiza `"version"` en `package.json`.
2. En `CHANGELOG.md`, convierte la cabecera `## [Unreleased]` en:
   ```
   ## [Unreleased]

   ## [X.Y.Z] - YYYY-MM-DD
   ```
   (fecha de hoy), dejando el contenido que ya estaba bajo `[Unreleased]` ahora
   bajo la nueva versión, y una sección `[Unreleased]` vacía encima para el
   próximo ciclo — igual que hace `CityGeneratorReleaseWindow.ApplyRelease`.
3. Commit con mensaje `Release vX.Y.Z` (solo esos dos ficheros, salvo que el paso 0
   también haya requerido tocar docs con la versión hardcodeada — en ese caso,
   confirma si van en el mismo commit o en uno aparte, pero no lo dejes sin avisar).

## Paso 4 — Tag y push

1. `git tag vX.Y.Z`
2. `git push origin main`
3. `git push origin vX.Y.Z`

## Paso 5 — GitHub Release

1. Extrae el cuerpo de la nueva sección del `CHANGELOG.md` (desde su cabecera
   `### ...` hasta el siguiente `## [`), tal cual, como notas de la release —
   mismo formato que las releases anteriores del repo (compáralo con
   `gh release view <tag_anterior> --json body -q .body` si tienes dudas de
   formato).
2. `gh release create vX.Y.Z --title vX.Y.Z --notes-file <fichero_con_las_notas> --latest`
   (usa `--latest` porque esta es la versión más reciente; no lo uses si estás
   rellenando un hueco histórico del paso 0).

## Paso 6 — Resumen final

Termina con un resumen breve: versión publicada, tipo de bump y por qué, enlace a
la release, y cualquier hueco histórico que hayas tenido que rellenar en el paso 0.
