🇪🇸 [Leer en español](user-manual.es.md) · ⬅️ [Back to the README](../README.md)

# City Generator — User Manual

This is the complete manual for the **City Generator** Editor window: what every tab, card
and parameter does, and what the process of generating a city looks like from start to
finish.

If you only need install/update instructions, requirements, or the rules your own prefabs
must follow, those live in the [README](../README.md). This document assumes the package is
already installed.

> **About the values in the screenshots.** The screenshots show the tool as it ships, with
> the demo content assigned. Numbers visible in them are just the shipped defaults, not
> recommendations — this manual deliberately explains what each parameter *means* rather
> than what value it should have.

---

## Table of contents

- [1. Opening the tool](#1-opening-the-tool)
- [2. Quick start: your first city](#2-quick-start-your-first-city)
- [3. Anatomy of the window](#3-anatomy-of-the-window)
- [4. City tab](#4-city-tab)
  - [4.1 General Options](#41-general-options)
  - [4.2 Custom Grid (Customize mode)](#42-custom-grid-customize-mode)
  - [4.3 Ground](#43-ground)
  - [4.4 Plazas](#44-plazas)
  - [4.5 Buildings](#45-buildings)
  - [4.6 Vegetation](#46-vegetation)
  - [4.7 Props](#47-props)
  - [4.8 Custom Places](#48-custom-places)
  - [4.9 Day/Night Cycle](#49-daynight-cycle)
- [5. Player tab](#5-player-tab)
  - [5.1 Player](#51-player)
  - [5.2 Player Settings](#52-player-settings)
  - [5.3 Camera](#53-camera)
  - [5.4 Free Camera](#54-free-camera)
- [6. Traffic tab](#6-traffic-tab)
  - [6.1 Traffic](#61-traffic)
  - [6.2 Vehicles](#62-vehicles)
- [7. Pedestrians tab](#7-pedestrians-tab)
  - [7.1 Pedestrian Settings](#71-pedestrian-settings)
  - [7.2 Pedestrians](#72-pedestrians)
  - [7.3 Custom Pedestrians](#73-custom-pedestrians)
  - [7.4 Behaviour](#74-behaviour)
  - [7.5 Crowd](#75-crowd)
- [8. Minimap tab](#8-minimap-tab)
- [9. Audio tab](#9-audio-tab)
  - [9.1 Ambience](#91-ambience)
  - [9.2 Plazas (plaza audio)](#92-plazas-plaza-audio)
- [10. Generating: buttons, validation and warnings](#10-generating-buttons-validation-and-warnings)
- [11. What ends up in your scene](#11-what-ends-up-in-your-scene)
- [12. Playing the generated city](#12-playing-the-generated-city)
- [13. After generating](#13-after-generating)
- [14. Multiple cities in a scene](#14-multiple-cities-in-a-scene)
- [15. Troubleshooting](#15-troubleshooting)

---

## 1. Opening the tool

Menu: **Tools > City Generator > Open**.

The window opens with every required field already filled in with the package's demo
content, so a complete city is one click away before you change anything.

A second menu entry, **Tools > City Generator > Rebuild Pedestrian Network**, is explained
in [section 13](#13-after-generating).

---

## 2. Quick start: your first city

1. Open **Tools > City Generator > Open**.
2. On the **City** tab, in **General Options**, set **Grid Width** and **Grid Height** to the
   number of blocks you want, and click any block in the preview to turn it into a plaza.
3. Optionally swap the demo prefabs for your own in the **Ground**, **Buildings**,
   **Vegetation** and **Props** cards.
4. Check the bottom of the window: if the **Build City in New Scene** button is greyed out,
   the panel just above it lists the problems that block generation.
5. Click **Build City in New Scene**. The city is generated and saved as the next free
   `Assets/Scenes/City<N>.unity`, leaving whatever scene you had open untouched.
6. Press Play and walk around.

To iterate on the result, use **Re-Build City in Current Scene** instead: it deletes the
`City` object in the open scene and regenerates it, keeping the camera and player.

---

## 3. Anatomy of the window

![The City Generator window](images/manual/window-overview.png)

The window has four parts, top to bottom:

| Part | What it is |
|---|---|
| **Banner** | The tool's header image. Hovering it shows the installed package version. |
| **Tab bar** | Six tabs: City, Player, Traffic, Pedestrians, Minimap, Audio. A tab whose settings contain a blocking error is highlighted. |
| **Cards** | The parameters of the selected tab, grouped into collapsible cards. Click a card's header to expand/collapse it; each card remembers its state between sessions. |
| **Footer** | Summary line, validation panel and the three action buttons. |

**Card badges.** The right-hand side of each card header shows a live summary of what's
inside it — grid size, number of prefabs, whether a feature is enabled — so you can read the
configuration without expanding everything.

**Required fields.** Fields that are required *in the current configuration* are marked, and
turn red while empty. The mark is conditional: for example the Lawn Prefab is only required
once at least one plaza block is selected.

**Nothing is applied to your project until you press a Build button.** Editing settings in
this window never touches your scene or your assets.

---

## 4. City tab

Everything about the city's layout and static content.

### 4.1 General Options

![General Options card](images/manual/card-general.png)

The grid preview at the top *is* the city's floor plan: each square is one block. **Click a
block to toggle it as a plaza** — a plaza block gets the plaza layout (lawn, centrepiece,
benches) instead of buildings, and appears green.

Under the preview, a caption reports the number of blocks, how many are plazas, and the
resulting world size in metres.

| Parameter | What it does |
|---|---|
| **Customize** (button, top-right) | Switches the grid preview to Custom Grid mode, where you draw an arbitrary city shape instead of a rectangle. See [4.2](#42-custom-grid-customize-mode). |
| **Grid Width** | Number of blocks along the X axis. Hidden while Customize mode is on. |
| **Grid Height** | Number of blocks along the Z axis. Hidden while Customize mode is on. |
| **Buildings Per Block** | How many of the four fixed corner slots of every non-plaza block get a building (0–4). Below 4, which corners stay empty is shuffled per block, so the city doesn't look uniform. |
| **Custom Seed** | When enabled, generation uses a fixed seed: the same settings always produce exactly the same city (layout, prefab choices, placement). When disabled, every generation is different. |
| **Seed** | The seed value. Only shown while Custom Seed is enabled. |

> Which blocks are plazas is **not** part of the seed — it is your explicit choice in the
> preview, so the generated city always matches what the preview shows.

Each block is 46 m wide, laid out on a 56 m pitch (10 m streets), and each block holds four
22 m building slots. Those are fixed dimensions: your building prefabs should be authored to
fit a 22 m slot.

### 4.2 Custom Grid (Customize mode)

![General Options in Customize mode](images/manual/card-general-customize.png)

Clicking **Customize** replaces the width × height rectangle with a hand-drawn footprint. It
asks for confirmation first, because entering the mode **resets the current grid**: the shape
restarts from a single central block and your plaza selection is cleared.

Two sub-modes appear above the preview:

| Sub-mode | What you do |
|---|---|
| **Define City Area** | Draw the shape. Click a **+** to add a block adjacent to the current shape; click a **−** to remove a block, as long as removing it wouldn't split the shape in two or empty it. |
| **Define Plazas** | Same click-to-toggle plaza behaviour as the rectangular grid, but restricted to the blocks that exist in your shape. |

The shape is always drawn on a fixed 10 × 10 canvas, and a block's world position never
shifts as the shape grows or shrinks around it.

**The result is still a rectangle.** Every gap inside the shape's bounding box is filled with
the Ground card's **Empty Block Prefab**, so a shape spanning 6 × 8 blocks generates a 6 × 8
city with ground cover where the missing blocks are — not a canvas full of holes. And, as in
the rectangular mode, the city always ends in walkable sidewalk rather than bare asphalt.

**Exit Customize** discards the custom shape and restores the rectangular grid. Re-entering
Customize always restarts from a single centre block; the previous shape is not remembered.

### 4.3 Ground

![Ground card](images/manual/card-ground.png)

The slabs and markings the city's floor is tiled with. All of these are required (the last
one conditionally).

| Parameter | What it does |
|---|---|
| **Road Base Prefab** | Asphalt slab covering the street area. Required. |
| **Sidewalk Prefab** | Sidewalk slab placed around each block, and around the city's outer contour. Required. |
| **Road Line Prefab** | Dashed centreline marking along the streets. Required. |
| **Crosswalk Line Prefab** | Zebra crossing stripe placed at signalled intersections. Required. |
| **Empty Block Prefab (custom grids only)** | Ground slab filling every gap of a Custom Grid shape. Ignored in rectangular mode; required while Customize mode is on. |

### 4.4 Plazas

![Plazas card](images/manual/card-plazas.png)

Content placed in the blocks you marked as plazas in the grid preview.

| Parameter | What it does |
|---|---|
| **Centerpiece Prefab** | Optional. Placed in the middle of every plaza block (the demo uses a fountain). |
| **Lawn Prefab (if any plaza block is selected)** | Lawn slab tiling a plaza block. Required as soon as at least one plaza block exists. |
| **Bench Prefab** | Optional. Placed around each plaza's lawn quadrants. |

Plaza blocks also get vegetation (see [4.6](#46-vegetation)) and, if configured, their own
positional audio source (see [section 9](#9-audio-tab)).

### 4.5 Buildings

![Buildings card](images/manual/card-buildings.png)

The pool of building prefabs. Drop a prefab into the field at the bottom to add it; every
entry is drawn as a tile with an **×** to remove it.

Buildings are picked at random from this list for each of a block's corner slots. There are
no per-prefab weights here — for weighted distribution, see Vehicles and Pedestrians.

> Buildings are the one category that is **not** overlap-checked against its neighbours or
> against the block edge. A prefab larger than the 22 m slot will visibly clip into the
> building next to it. Size your own building prefabs to the slot.

### 4.6 Vegetation

![Vegetation card](images/manual/card-vegetation.png)

| Parameter | What it does |
|---|---|
| **Prefabs** | Tree/plant prefabs placed along the streets and inside plazas. At least one is required when Density is above 0. |
| **Density** | Fraction of the valid candidate points that get vegetation: 0 = none, 1 = every candidate point. |

Plazas use a fraction of this density on their own denser candidate grid, so the same value
doesn't pack noticeably more trees inside a plaza than along a street.

### 4.7 Props

![Props card](images/manual/card-props.png)

Street furniture, plus the traffic light prefab used by the traffic network.

| Parameter | What it does |
|---|---|
| **Traffic Light Prefab** | Placed at every signalled intersection — one with at least three real street arms, so a full 4-way crossroads or a T-intersection along the city's own border. Must carry a `TrafficLight` component. Required whenever the city has at least one of those, which includes a 1 × N or N × 1 grid and most custom shapes; only a single-block city has none. Required **even when Include Traffic is off**, because the network and its lights are always generated so pedestrian crossings stay wired to a real light. |
| **Lamp Prefab** | Optional street lamp, placed along the sidewalks. |
| **Lamp Density** | Fraction of the candidate lamp points filled along each block side (there are 3 candidate points per side). |
| **Bin Prefab** | Optional bin, placed at street corners. |
| **Bin Density** | Fraction of the candidate corner points that get a bin. |

### 4.8 Custom Places

![Custom Places card](images/manual/card-custom-places.png)

A Custom Place is a prefab you place **by hand at an exact spot**, instead of leaving it to
the random building placement. Use it for a landmark: a hospital, a police station, your own
hero building.

Click **+ Add Custom Place** to add an entry. Each entry has its own grid preview: click a
block to place the entry there, and click a quadrant of that block to choose which of the
four corner slots it occupies. Plaza blocks are shown in green for reference — a Custom Place
cannot be placed on one.

| Parameter | What it does |
|---|---|
| **Title** | Name of the entry, shown in the UI, in validation messages, and as the label on the minimap when it is a Point of Interest. Required, and must be unique. |
| **Prefab** | The prefab to instantiate. Required. |
| **Is Point Of Interest** | Marks the place as a Point of Interest on the Minimap HUD, labelled with its Title. |
| **Occupies Full Block** | The place takes the whole block (all four corner slots) instead of a single 22 m slot. The quadrant selection is ignored while this is on. |
| **Facing** | Fixed orientation (North / East / South / West), in 90° steps. Unlike normal buildings, a Custom Place's rotation is never randomised. |
| **×** (button) | Removes the entry. |

Two entries cannot claim the same slot, and a full-block entry conflicts with any other entry
in the same block — both cases are reported as blocking errors.

### 4.9 Day/Night Cycle

![Day/Night Cycle card](images/manual/card-day-night.png)

Optional 24-hour cycle applied to the generated Directional Light.

| Parameter | What it does |
|---|---|
| **Enabled** | Whether the light auto-advances through the cycle in Play Mode. When off, the light simply stays fixed at Start Hour. |
| **Start Hour** | Hour of the day (0–24) the light starts at. Applied to the light **even when the cycle is disabled**, and previewed in the Editor right after generation. |
| **Speed Multiplier** | How fast simulated time passes relative to real time (1 = real time). |
| **Light Color Over Time** | Gradient sampled over the day: 0 = midnight, 0.5 = noon, 1 = midnight again. |
| **Light Intensity Over Time** | Curve sampled over the same 0–1 range. |

The generated light's yaw is always fixed so the sun rises east-north-east and sets
west-south-west, roughly matching the minimap's orientation. Both **Build** and **Re-Build**
apply this, so re-building an old scene corrects a stale sun direction too.

---

## 5. Player tab

### 5.1 Player

![Player card](images/manual/card-player.png)

| Parameter | What it does |
|---|---|
| **Enabled** | Spawn a player-controlled character in the generated city. When off, no player, no camera rig tuning and no Free Camera are added. |
| **Player Prefab (if Enabled)** | The character prefab. It is spawned inside a plaza, or in a random block if there is none. Required when Player is enabled. |
| **Input Actions (if Enabled)** | The Input Actions asset driving the player's Move/Sprint/Jump and the camera's Look. Required when Player is enabled — without it the generated camera would silently receive no input. |

The tool validates the asset: the action map named below must exist, and the Move/Look
actions must be Value actions while Jump/Sprint must be Button actions. A typo here is
reported before generating instead of failing silently at runtime.

### 5.2 Player Settings

![Player Settings card](images/manual/card-player-settings.png)

These values are written onto the **generated instance**, never onto your prefab asset — so
they are the single source of truth regardless of what the prefab already carries.

**Movement**

| Parameter | What it does |
|---|---|
| **Walk Speed** | Normal walking speed, in m/s. |
| **Run Speed** | Speed while holding Sprint, in m/s. |
| **Rotation Smooth Time** | How quickly the character turns to face the movement direction. |
| **Gravity** | Downward acceleration while airborne. |
| **Jump Height** | Peak height of a jump, in metres. |

**Character Controller** — these map one-to-one onto Unity's `CharacterController`:

| Parameter | What it does |
|---|---|
| **Controller Height** | Total height of the capsule. |
| **Controller Radius** | Radius of the capsule. |
| **Controller Center** | Capsule offset from the prefab's pivot. Tune it to match where your model's feet actually are. |
| **Controller Slope Limit** | Steepest slope, in degrees, the player can walk up. |
| **Controller Step Offset** | Tallest step the player can climb without jumping. Must be smaller than Controller Height. |
| **Controller Skin Width** | Small overlap kept with obstacles so the character doesn't get stuck. Must be smaller than Controller Radius. |
| **Controller Min Move Distance** | Movements smaller than this are ignored, to avoid jitter. |

> If you use a very small character (a pet-sized prefab, a scaled-down model), remember the
> Step Offset is in world units and must stay below the capsule height — a controller
> configured out of those bounds is silently disabled by Unity.

**Input Actions** — the *names* looked up inside the asset assigned above:

| Parameter | What it does |
|---|---|
| **Action Map Name** | Name of the action map containing the player's actions. |
| **Move / Jump / Sprint Action Name** | Names of those actions inside the map. |
| **Look Action Name** | Name of the Look action, consumed by the generated camera. |

### 5.3 Camera

![Camera card](images/manual/card-camera.png)

Tuning for the third-person camera added to the generated Main Camera.

| Parameter | What it does |
|---|---|
| **Field Of View** | Vertical FOV, in degrees. |
| **Vertical Offset** | How far above the player the orbit pivot sits. |
| **Horizontal Offset** | Lateral offset of the pivot along its local right axis (over-the-shoulder framing); 0 keeps it centred. |
| **Distance** | Default distance from the camera to the pivot. |
| **Min Distance** | Closest the camera may get to the pivot, e.g. when pulled in by a collision. Must not exceed Distance. |
| **Sensitivity** | How fast the camera orbits per unit of Look input. |
| **Min Pitch / Max Pitch** | Lowest (looking up) and highest (looking down) pitch angles, in degrees. Max must be greater than Min. |
| **Follow Smooth Time** | Smooths the tracking of the player's *position* only. |
| **Collision Mask** | Layers the camera collides against, pulling itself closer to the pivot instead of clipping through geometry. |
| **Collision Radius** | Radius of the sphere used for that collision check. |
| **Lock Cursor** | Lock and hide the mouse cursor while playing, so mouse movement always orbits the camera. |

> The camera's **rotation** is deliberately never smoothed — rotation smoothing was tried and
> caused visible motion sickness. Follow Smooth Time affects position tracking only.

### 5.4 Free Camera

![Free Camera card](images/manual/card-free-camera.png)

An optional free-flying camera that takes over the same Main Camera at runtime, so you can
fly around and inspect the generated city.

| Parameter | What it does |
|---|---|
| **Enabled** | Add the Free View camera to the generated player. Ignored (with no error) when Player is disabled. |
| **Move Speed** | Base flying speed on all three axes, in m/s. |
| **Sprint Multiplier** | Multiplier applied while holding the Free View Sprint action. |
| **Rotation Smooth Time** | Smoothing for yaw/pitch reaching the Look-driven target angle. Unlike the third-person camera, the Free Camera does smooth its own rotation. |

Free View uses its own action map (see [section 12](#12-playing-the-generated-city)); the
Toggle action exists in both maps, which is what lets you switch back and forth.

---

## 6. Traffic tab

### 6.1 Traffic

![Traffic card](images/manual/card-traffic.png)

| Parameter | What it does |
|---|---|
| **Enabled** | Generate the traffic network, its lights and the vehicles. |
| **Vehicle Count** | How many vehicles to spawn, split across the Vehicles list by percentage. |

The traffic network and its traffic lights are generated **regardless of this toggle**, so
pedestrian crossings always have a real light to obey. What the toggle controls is the
vehicles themselves.

A warning appears under Vehicle Count as soon as the count exceeds a safe fraction of the
grid's spawn points. Vehicles have no route planning or congestion avoidance, so beyond that
point traffic tends to gridlock rather than flow; the message tells you the recommended
maximum for your current grid. It is a warning, not a blocking error.

### 6.2 Vehicles

![Vehicles card](images/manual/card-vehicles.png)

The weighted pool the Vehicle Count is drawn from. Drop a prefab in the field at the bottom
to add it at 0 %.

| Control | What it does |
|---|---|
| **Stacked bar** (top) | Live view of how the count splits across entries. |
| **Total** (`x / 100`) | Sum of every entry's percentage. It must be exactly 100 for generation to be valid. |
| **Normalize to 100 %** | Rescales every non-zero percentage proportionally so the total is exactly 100. |
| **Slider / number per row** | That prefab's share of the total vehicle count. |
| **×** | Removes the entry. |

Vehicle prefabs need no `Rigidbody` — they are moved by transform every frame — and the tool
adds the collider each instance needs for sensor detection if the root has none.

---

## 7. Pedestrians tab

### 7.1 Pedestrian Settings

![Pedestrian Settings card](images/manual/card-pedestrian-settings.png)

| Parameter | What it does |
|---|---|
| **Enabled** | Spawn pedestrian NPCs. The pedestrian network itself is always generated regardless of this toggle. |
| **Pedestrian Count** | How many pedestrians to spawn, split across the Pedestrians list by percentage. |

Two non-blocking warnings can appear here:

- **Crowding.** Past a large fraction of the graph's walkable points, the crowd starts reading
  as overcrowded. Pedestrians never gridlock the way cars do, so the threshold is much more
  permissive than the vehicle one.
- **Isolated blocks.** A city with no signalled intersection at all — a single block, or a
  custom shape whose cells never form one — gets no zebra crossings and no traffic lights:
  its pedestrians stay confined to their own sidewalk ring, unable to reach a neighbouring
  block. A 1 × N or N × 1 grid is *not* in this situation: its border T-intersections are
  signalled and do get crossings.

### 7.2 Pedestrians

![Pedestrians card](images/manual/card-pedestrians.png)

The weighted pool of pedestrian prefabs. It works exactly like the Vehicles list: percentages
must sum to 100, with a **Normalize to 100 %** button to fix them in one click.

A pedestrian prefab wants an `Animator` driving `Speed`/`Grounded` parameters if you want
walk/idle animation; without one they still walk, just unanimated.

### 7.3 Custom Pedestrians

![Custom Pedestrians card](images/manual/card-custom-pedestrians.png)

Custom Pedestrians are a **separate budget** of pedestrians confined to a route you trace by
hand, instead of roaming the whole city. Typical use: a group of visitors that only ever
circles one park, or a pet walking a fixed loop.

They are independent of the Pedestrians card: they are generated even when **Pedestrian
Settings > Enabled** is off, and they don't count towards Pedestrian Count.

| Parameter | What it does |
|---|---|
| **Title** | Name of the entry, used in the UI and in validation messages. Required. |
| **Prefab** | The prefab spawned at the entry's spawn nodes. Required. |
| **Count** | Number of agents of that prefab spread across the traced route. Must be at least 1. |
| **×** | Removes the entry. |

Below the fields, each entry gets a **node-graph picker**: a preview of the real pedestrian
graph for your current settings, colour-coded — **green** for a sidewalk ring edge, **orange**
for a crossing, **blue** for an interior spoke through a block. Click a segment to add it to
the route (it turns yellow), click it again to remove it. Only a segment sharing a point with
the current selection can be added, so the traced route always stays connected.

The entry in the screenshot is outlined in red because it has no route traced yet: an entry
needs **at least two connected nodes** selected, otherwise generation is blocked. If you
change the grid, plazas or Custom Places after tracing a route, the picker detects that the
underlying graph changed and asks you to re-trace it.

### 7.4 Behaviour

![Behaviour card](images/manual/card-pedestrian-behaviour.png)

Applied to every generated pedestrian instance.

**Animation reference speeds**

| Parameter | What it does |
|---|---|
| **Walk Reference Speed** | The speed at which the animation blend tree reaches its walk pose. |
| **Run Reference Speed** | The speed at which it reaches its run pose. |

These two are calibration anchors, not speed settings. They should match **Player Settings >
Walk Speed / Run Speed**; the card shows a warning while they don't, because a mismatch makes
pedestrians foot-slide. Both must be greater than zero and different from each other.

**Pace**

| Parameter | What it does |
|---|---|
| **Pace Fraction** | Most pedestrians stroll at this fraction of the walk reference speed, rather than a full player-paced walk. |
| **Runner Chance** | Chance that a pedestrian is a "runner" moving at the run reference speed instead. |
| **Speed Jitter** | Per-instance ± speed variation, rolled once at spawn. |
| **Lateral Jitter** | Per-instance ± offset from the path centreline, so parallel walkers don't render as a single file. |
| **Rotation Speed** | How fast a pedestrian turns to face its walking direction, in degrees/second. |
| **Arrive Radius** | Distance at which a destination node counts as reached. |

**Stops**

| Parameter | What it does |
|---|---|
| **Idle Stop Chance** | Chance of idling in place on reaching a destination, instead of immediately picking a new one. |
| **Idle Stop Duration Min / Max** | Range of the random idle duration, in seconds. Max must be ≥ Min. |

### 7.5 Crowd

![Crowd card](images/manual/card-crowd.png)

Applied to the generated pedestrian manager — how the crowd behaves as a whole.

| Parameter | What it does |
|---|---|
| **Separation Cell Size** | Size of the spatial grid cell used to find nearby pedestrians for the separation nudge. |
| **Separation Radius** | Distance within which two pedestrians push apart. |
| **Separation Strength** | Strength of that push. |
| **Player Avoidance Radius** | Distance within which pedestrians steer away from the player. |
| **Player Avoidance Strength** | Strength of that steer-away nudge. |
| **Stagger Min Agent Count** | Below this many agents, every pedestrian's forward sensor runs every frame. Above it, distant agents get staggered. |
| **Stagger Distance** | Distance from the camera beyond which a pedestrian's sensor is staggered. |
| **Stagger Frames** | A staggered pedestrian's sensor runs 1 frame out of every this many, reusing the previous result in between. |

The three stagger parameters are a performance/accuracy trade-off: higher Stagger Frames
means cheaper crowds and slightly later reactions far from the camera.

---

## 8. Minimap tab

![Minimap card](images/manual/card-minimap.png)

The minimap is a top-down snapshot of the generated city, saved as a PNG asset next to the
scene and shown in-game by a HUD that follows the player.

| Parameter | What it does |
|---|---|
| **Enabled** | Whether a minimap HUD is generated and added to the scene. |
| **Texture Resolution** | Width and height, in pixels, of the snapshot texture. A warning appears above 4096 px: a large snapshot costs noticeable texture memory and disk space. |
| **View Radius (m)** | Radius, in metres, of the world area the HUD shows around the player. A warning appears if it exceeds the area the snapshot actually covers, since the HUD could never show that much. |

Custom Places flagged as **Is Point Of Interest** appear on the minimap labelled with their
Title.

The snapshot is captured under its own neutral daytime light, so it never bakes in the
Day/Night Cycle's current hour, and it excludes vehicles and pedestrians.

---

## 9. Audio tab

### 9.1 Ambience

![Ambience card](images/manual/card-ambience.png)

2D looping ambience for the whole city — heard everywhere, independent of camera position.

| Control | What it does |
|---|---|
| **Enabled** | Whether ambience plays in the generated city. |
| **+ Add Ambience Clip** | Adds an entry. |
| **Clip** | The audio clip for that entry. Required. |
| **Volume** | That entry's own volume, independent of the other entries. |
| **×** | Removes the entry. |

All entries loop simultaneously, each at its own volume, so you can layer several beds.
Leaving the card enabled with no entries — or an entry with no clip — is a blocking error,
since it would silently play nothing.

### 9.2 Plazas (plaza audio)

![Plaza audio card](images/manual/card-plaza-audio.png)

3D positional audio placed at every generated plaza.

| Control | What it does |
|---|---|
| **Enabled** | Whether each generated plaza gets its own positional source. |
| **+ Add Plaza Clip** | Adds an entry. |
| **Clip** | The audio clip. Required. |
| **Volume** | That entry's own volume. |
| **Min Distance** | Distance at which attenuation starts. |
| **Max Distance** | Distance at which the clip stops being audible. |
| **×** | Removes the entry. |

Every entry is created inside every plaza block's group, so the same set of clips plays at
each plaza's own position.

---

## 10. Generating: buttons, validation and warnings

![Footer](images/manual/footer.png)

The footer, bottom to top:

| Element | What it does |
|---|---|
| **Build City in New Scene** | Generates a city and saves it as the next free `Assets/Scenes/City<N>.unity`. Whatever scene you had open is left untouched. |
| **Re-Build City in Current Scene** | Deletes the `City` object in the current scene and regenerates it with the current settings. Asks for confirmation first. |
| **Reset to Defaults** | Discards every change and restores the tool's shipped defaults. |
| **Summary line** | Live estimate of what will be generated: buildings, vehicles, pedestrians, custom places. |
| **Validation panel** | Every problem found in the current settings, live. |

**What Re-Build keeps.** The camera and the player are left untouched. The Directional Light
keeps its position and shadow settings, but its yaw is corrected and its Day/Night Cycle is
updated to match the current settings. If generation fails partway through, the previous city
is left intact — the rebuild is transactional.

**Blocking errors vs warnings.** Both are listed in the validation panel; the tab and the card
containing the problem are highlighted so you can find it. Errors disable both Build buttons
(the button's tooltip tells you how many problems are pending). Warnings never block
generation — they flag a consequence you may not expect, such as gridlock-prone traffic
density, an oversized minimap texture, or a prefab with no `Renderer` whose footprint can't be
measured.

**While generating**, a progress bar reports each phase. When it finishes, a result panel
appears above the buttons with the scene path and the real counts of what was generated, plus
a **Ping Scene** button that highlights the generated scene asset in the Project window. The
same summary is logged to the Console.

Common blocking errors and their fix:

| Message | Fix |
|---|---|
| *Ground: … prefab is required* | Assign the missing prefab in the Ground card. |
| *Ground: Empty Block prefab is required while Customize mode is on* | Assign it, or leave Customize mode. |
| *Plaza: Lawn prefab is required when at least one plaza cell is selected* | Assign a Lawn Prefab, or unselect the plaza blocks. |
| *Props: Traffic Light prefab is required…* / *…must have a TrafficLight component* | Assign a prefab carrying the `TrafficLight` component. |
| *Vehicles / Pedestrians: percentages must sum to 100* | Click **Normalize to 100 %**. |
| *Player: Player Prefab / Input Actions is required when Player is enabled* | Assign them, or disable the Player card. |
| *General: … action was not found in the … action map* | Fix the action name in Player Settings > Input Actions, or the asset itself. |
| *Custom Places: … has no position assigned yet* | Click a block (and a quadrant) in that entry's grid preview. |
| *Custom Pedestrians: … needs at least 2 connected nodes* | Trace a route in that entry's node-graph picker. |
| *Audio: … is enabled but has no clip entries* | Add a clip, or disable that audio card. |

---

## 11. What ends up in your scene

Generation creates a single root object named **`City`**, with one group per content type:

`Roads`, `EmptyBlocks`, `Sidewalks`, `RoadMarkings`, `CustomPlaces`, `Buildings`,
`TrafficLights`, `StreetLights`, `Plaza`, `Trees`, `Props`, `Vehicles`, `TrafficNetwork`,
`Pedestrians`, `PedestrianNetwork`.

Alongside it, a new scene also gets a `Directional Light`, a `Main Camera` (carrying the
third-person and, if enabled, the Free Camera controller), the `Player`, and the minimap HUD.

Everything except `Vehicles` and `Pedestrians` is flagged static for batching and occlusion,
so lightmap and occlusion bakes are ready to run — the tool just doesn't run them for you.

---

## 12. Playing the generated city

Controls come from the Input Actions asset you assigned, so the exact keys are yours to
change. With the demo asset:

| Action | Default binding | What it does |
|---|---|---|
| Move | WASD / left stick | Walk |
| Sprint | Left Shift | Run |
| Jump | Space | Jump |
| Look | Mouse / right stick | Orbit the camera |
| Toggle | The Toggle action in both maps | Switch between the third-person camera and Free View |

In **Free View**, Move flies horizontally, the Vertical action (Q/E in the demo asset) flies
up and down, and Sprint multiplies the flying speed.

---

## 13. After generating

A few things are deliberately left to you, per generated scene:

- **Bake lightmaps and occlusion culling.** The geometry is already flagged for both.
- **Add `LODGroup`s** to your own prefabs if you are generating a large city.
- **Adjust lighting to taste.** The scene ships with a single directional light and no
  post-processing volume, to stay render-pipeline-agnostic.

**Tools > City Generator > Rebuild Pedestrian Network** recalculates the pedestrian graph
against the scene as it currently stands, without regenerating the city. Use it after moving
or adding an obstacle by hand. The same repair also runs automatically every time you enter
Play.

> Note that a pedestrian obstacle is detected purely by physics: an object with **no
> `Collider`** anywhere in its hierarchy never blocks pedestrians, and they will walk through
> it. If something you placed should block them, give it a `Collider`.

Finally: **fixes belong in the tool, not in the scene.** Hand-editing a generated scene fixes
exactly one city and is lost on the next generation.

---

## 14. Multiple cities in a scene

The tool has no button to generate a second city alongside an existing one — you generate each
city in its own scene as usual, then **copy its root GameObject** (named `City`, marked
internally by a `CityGeneratorRoot` component) into whichever scene should hold both. Once
copied, the pasted city works like any other: its own traffic, its own pedestrians, its own
minimap.

**Only moving (translating) a copied city is supported.** Dragging its root to a new position
in the Hierarchy or Scene view is fine — its traffic and pedestrian networks, and its minimap,
all follow the move correctly. **Rotating or scaling a copied city's root is not supported**:
the tool does not stop you from doing it, but the result is wrong — sensor ranges, lane
offsets, and the minimap's fixed north all assume the city is at its original orientation and
1:1 scale.

Two limitations, currently by design:

- **Ambience audio isn't scoped per city.** If more than one city has 2D Ambience enabled, both
  will play at once. Disable it on all but one if that's not what you want (City tab → Audio,
  or the Audio tab's Ambience card, per city).
- **Traffic and pedestrians never cross between cities.** Each city's vehicles and pedestrians
  stay confined to their own network, even if two cities are placed right next to each other.

**Tools > City Generator > Rebuild Minimap** recaptures the minimap snapshot for the city or
cities currently in the scene, at their *current* position — use it after moving a city's root
by hand, since neither city's minimap updates on its own when you do that. With a single city
in the scene it recaptures immediately, no confirmation needed. With two or more, it asks you to
confirm (showing how many cities it found and the combined area it will cover, with an editable
texture resolution) and then captures **one** snapshot covering all of them, pointing every
city's minimap at it — Points of Interest from every city show up on all of their minimaps.

**Re-Build City in Current Scene**, when the active scene holds two or more cities, asks you to
confirm before it destroys all of them and generates the one new city in their place — copy
elsewhere first anything you want to keep.

---

## 15. Troubleshooting

| Symptom | Likely cause |
|---|---|
| **Both Build buttons are greyed out** | There is at least one blocking error; the validation panel above them lists it, and the tab/card containing it is highlighted. |
| **Everything renders magenta** | The demo materials are URP/Lit. Under Built-in or HDRP you need to supply your own materials — the tool itself has no pipeline dependency. |
| **A building clips into its neighbour** | Buildings are not overlap-checked. Size your building prefabs to the 22 m corner slot. |
| **Traffic is stopped everywhere** | Vehicle Count is too high for the grid; the warning under the field gives a recommended maximum. |
| **Pedestrians can't cross the street** | The city has no signalled intersection at all (a single block, or a custom shape with no 4-way and no T), so there are no crossings. |
| **Pedestrians walk through an object** | It has no `Collider` anywhere in its hierarchy. Give it one and rebuild the pedestrian network. |
| **Vehicles don't detect each other or the pedestrians** | Every layer slot in your project is taken, so the `Vehicle`/`Pedestrian` layers couldn't be created. The Console reports it. Free a layer slot. |
| **Pedestrians foot-slide** | Walk/Run Reference Speed no longer match Player Settings > Walk/Run Speed; the Behaviour card warns about it. |
| **A pedestrian's animation freezes** | Its rig is not skinned, or its Locomotion clips don't loop. See the pedestrian requirements in the [README](../README.md). |
| **I want the exact same city again** | Enable **Custom Seed** and keep the seed value. |
