using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        public List<PedestrianEntry> pedestrians = new();
        public PlayerSettings player = new();
        public CameraSettings camera = new();
        public PedestrianBehaviourSettings pedestrianBehaviour = new();
        public CrowdSettings crowd = new();
        public List<CustomPlaceEntry> customPlaces = new();
        public MinimapSettings minimap = MinimapSettings.Default();
        public DayNightSettings dayNight = DayNightSettings.Default();
        public AudioSettings audio = AudioSettings.Default();
    }

    [Serializable]
    internal class GeneralSettings
    {
        [Tooltip("Number of blocks along the X axis. Each block is 46 m, on a 56 m pitch (10 m streets).")]
        public int gridWidth = 5;
        [Tooltip("Number of blocks along the Z axis. Each block is 46 m, on a 56 m pitch (10 m streets).")]
        public int gridHeight = 5;
        [Tooltip("Block (x, y) coordinates picked by clicking the grid preview above; a plaza block gets the Plaza layout (lawn/centerpiece/benches) instead of buildings. Actual default filled by CityGeneratorDefaultAssets.ApplyTo.")]
        public List<Vector2Int> plazaCells = new();
        [Tooltip("How many of the 4 fixed 22 m corner slots each non-plaza block fills (0-4). Below 4, which corners are filled is shuffled per block.")]
        public int buildingsPerBlock = 4; // clamped 0-4
        [Tooltip("Generate the traffic network, lights and vehicles.")]
        public bool includeTraffic = true;
        [Tooltip("Number of vehicles to spawn, distributed across the Vehicles list by percentage. Too high relative to the grid's spawn points tends to gridlock — see the warning above.")]
        public int vehicleCount = 80;
        [Tooltip("Spawn pedestrian NPCs. The pedestrian network itself (crossings wired to the traffic lights) is always generated regardless of this toggle.")]
        public bool includePedestrians = true;
        [Tooltip("Number of pedestrian NPCs to spawn, distributed across the Pedestrians list by percentage. Too high relative to the grid's sidewalk ring nodes reads as overcrowded — see the warning above.")]
        public int pedestrianCount = 90;
        [Tooltip("Use a fixed seed so the same settings always generate the same layout, prefab choices and placement. Unchecked, every generation is different.")]
        public bool useCustomSeed = false;
        [Tooltip("Seed value used when Custom Seed is enabled.")]
        public int seed = 0;
        [Tooltip("Optional character prefab controlled by the player. If set, it's spawned inside a plaza (or a random block) and Input Actions becomes required. Its tuning comes from the Player tab, applied to the generated instance regardless of what the prefab already carries.")]
        public GameObject playerPrefab; // optional
        [Tooltip("Input Actions asset driving the Player Prefab's Move/Sprint/Jump and the generated camera's Look input. Required if Player Prefab is set.")]
        public InputActionAsset inputActions; // required if playerPrefab is set (drives the generated camera's Look input)
    }

    [Serializable]
    internal class GroundSettings
    {
        [Tooltip("Asphalt slab covering the street area. Required.")]
        public GameObject roadBasePrefab; // required
        [Tooltip("Sidewalk slab around each block. Required.")]
        public GameObject sidewalkPrefab; // required
        [Tooltip("Dashed centreline marking placed along streets. Required.")]
        public GameObject roadLinePrefab; // required
        [Tooltip("Zebra crossing stripe placed at signalled intersections. Required.")]
        public GameObject crosswalkLinePrefab; // required
    }

    [Serializable]
    internal class PlazaSettings
    {
        [Tooltip("Optional centrepiece (e.g. a fountain) placed in the middle of every plaza block.")]
        public GameObject centerpiecePrefab; // optional
        [Tooltip("Lawn slab filling a plaza block. Required if at least one plaza block is selected in the grid preview above.")]
        public GameObject lawnPrefab; // required if plazaCells is non-empty
        [Tooltip("Optional bench placed around each plaza's lawn quadrants.")]
        public GameObject benchPrefab; // optional
    }

    [Serializable]
    internal class VegetationSettings
    {
        [Tooltip("Tree/plant prefabs placed along streets and inside plazas. At least one is required when Density > 0.")]
        public List<GameObject> prefabs = new(); // 1+ required if density > 0
        [Tooltip("Fraction of valid candidate points filled with vegetation (0 = none, 1 = every candidate point).")]
        [Range(0f, 1f)] public float density = 0.2f;
    }

    [Serializable]
    internal class VehicleEntry
    {
        [Tooltip("Vehicle prefab for this entry.")]
        public GameObject prefab;
        [Tooltip("Share of Vehicle Count spawned from this prefab. All entries' percentages must sum to 100.")]
        [Range(0f, 100f)] public float percentage;
    }

    [Serializable]
    internal struct PedestrianEntry
    {
        [Tooltip("Pedestrian character prefab for this entry.")]
        public GameObject prefab;
        [Tooltip("Share of Pedestrian Count spawned from this prefab. All entries' percentages must sum to 100.")]
        [Range(0f, 100f)] public float percentage;
    }

    [Serializable]
    internal class PropsSettings
    {
        [Tooltip("Traffic light prefab placed at every signalled intersection. Required when Include Traffic is enabled; must have a TrafficLight component.")]
        public GameObject trafficLightPrefab; // required if includeTraffic
        [Tooltip("Optional street lamp prefab placed along sidewalks.")]
        public GameObject lampPrefab; // optional
        [Tooltip("Fraction of candidate lamp points filled along each block side (1 = all 3 candidate points per side).")]
        [Range(0f, 1f)] public float lampDensity = 1f; // 1 = every candidate point along the sidewalk (3 per side)
        [Tooltip("Optional bin prefab placed at street corners.")]
        public GameObject binPrefab;
        [Tooltip("Fraction of candidate corner points filled with a bin.")]
        [Range(0f, 1f)] public float binDensity = 0.3f;
    }

    // Applied to the generated Player instance by CityGeneratorSceneBuilder.ConfigurePlayer —
    // always onto the instance, never onto the Player Prefab asset, so these values are the
    // single source of truth regardless of whether the prefab already carries its own
    // PlayerController/CharacterController.
    [Serializable]
    internal class PlayerSettings
    {
        [Header("Movement")]
        [Tooltip("Normal walking speed, in metres/second.")]
        public float walkSpeed = 4f;
        [Tooltip("Sprint speed while holding the Sprint action, in metres/second.")]
        public float runSpeed = 8f;
        [Tooltip("How quickly the player rotates to face the movement direction.")]
        public float rotationSmoothTime = 0.1f;
        [Tooltip("Downward acceleration applied while airborne.")]
        public float gravity = -20f;
        [Tooltip("Peak height of a jump, in metres.")]
        public float jumpHeight = 1.2f;

        [Header("Character Controller")]
        [Tooltip("CharacterController.height — the capsule's total height.")]
        public float controllerHeight = 0.72f;
        [Tooltip("CharacterController.radius — the capsule's radius.")]
        public float controllerRadius = 0.2f;
        [Tooltip("CharacterController.center — capsule offset from the prefab's pivot; tune this to match where your character model's feet actually are.")]
        public Vector3 controllerCenter = new(0f, 0.4f, 0f);
        [Tooltip("CharacterController.slopeLimit — steepest slope, in degrees, the player can walk up.")]
        public float controllerSlopeLimit = 45f;
        [Tooltip("CharacterController.stepOffset — tallest step the player can climb without jumping.")]
        public float controllerStepOffset = 0.2f;
        [Tooltip("CharacterController.skinWidth — small overlap kept with obstacles to avoid getting stuck; usually a small fraction of the radius.")]
        public float controllerSkinWidth = 0.02f;
        [Tooltip("CharacterController.minMoveDistance — movements smaller than this are ignored, to avoid jitter.")]
        public float controllerMinMoveDistance = 0.001f;

        [Header("Input Actions")]
        [Tooltip("Name of the action map in Input Actions that contains the player's actions.")]
        public string actionMapName = "Player";
        [Tooltip("Name of the Move (Vector2) action within the action map.")]
        public string moveActionName = "Move";
        [Tooltip("Name of the Jump (button) action within the action map.")]
        public string jumpActionName = "Jump";
        [Tooltip("Name of the Sprint (button) action within the action map.")]
        public string sprintActionName = "Sprint";
        [Tooltip("Name of the Look (Vector2) action within the action map, consumed by the generated camera.")]
        public string lookActionName = "Look"; // consumed by ThirdPersonCamera, kept here alongside the rest of the Player action map
    }

    // Applied to the generated Main Camera's ThirdPersonCamera by CityGeneratorSceneBuilder.CreateMainCamera.
    [Serializable]
    internal class CameraSettings
    {
        [Tooltip("Vertical field of view, in degrees.")]
        public float fieldOfView = 45f;
        [Tooltip("How far above the player the orbit pivot sits.")]
        public float verticalOffset = 1f;
        [Tooltip("Lateral offset of the orbit pivot along its local right axis (over-the-shoulder framing). 0 keeps the pivot centred on the player.")]
        public float horizontalOffset = 0f; // along the orbit's local right axis, i.e. over-the-shoulder

        [Header("Orbit")]
        [Tooltip("Default distance from the camera to the orbit pivot.")]
        public float distance = 5f;
        [Tooltip("Closest the camera can get to the pivot, e.g. when pulled in by a collision.")]
        public float minDistance = 1f;
        [Tooltip("Look-input sensitivity — how fast the camera orbits per unit of Look input.")]
        public float sensitivity = 0.12f;
        [Tooltip("Lowest pitch angle (looking up), in degrees.")]
        public float minPitch = -20f;
        [Tooltip("Highest pitch angle (looking down), in degrees.")]
        public float maxPitch = 60f;
        [Tooltip("Smooths only the tracking of the player's position, never the camera's rotation.")]
        public float followSmoothTime = 0.08f;

        [Header("Collision")]
        [Tooltip("Layers the camera collides against, pulling itself closer to the pivot to avoid clipping through geometry.")]
        public LayerMask collisionMask = -1;
        [Tooltip("Radius of the sphere used for the camera's own collision check.")]
        public float collisionRadius = 0.3f;

        [Header("Cursor")]
        [Tooltip("Lock and hide the mouse cursor while playing, so mouse movement always orbits the camera.")]
        public bool lockCursor = true;
    }

    // Applied to every generated PedestrianAgent instance by CityGeneratorPedestrianBuilder.BuildPedestrians —
    // always onto the instance, mirroring PlayerSettings.
    [Serializable]
    internal class PedestrianBehaviourSettings
    {
        [Header("Animation reference speeds")]
        [Tooltip("Speed at which CharacterAnimator.controller's Locomotion blend tree reaches Speed = 0.5 — should match Player > Walk Speed, or pedestrians foot-slide.")]
        public float walkReferenceSpeed = 4f;
        [Tooltip("Speed at which the blend tree reaches Speed = 1 — should match Player > Run Speed.")]
        public float runReferenceSpeed = 8f;

        [Header("Pace")]
        [Tooltip("Most pedestrians stroll at this fraction of Walk Reference Speed, not a full player-paced walk.")]
        [Range(0f, 1f)] public float paceFraction = 0.5f;
        [Tooltip("Chance a pedestrian is a 'runner' (jogging, or late) instead of a regular stroller, moving at Run Reference Speed.")]
        [Range(0f, 1f)] public float runnerChance = 0.15f;
        [Tooltip("Per-instance +-fraction speed jitter, rolled once at spawn.")]
        [Range(0f, 1f)] public float speedJitter = 0.1f;
        [Tooltip("Per-instance +-lateral offset from the path centreline, rolled once at spawn, so parallel walkers don't render as a single file line.")]
        public float lateralJitter = 0.4f;
        [Tooltip("How quickly a pedestrian turns to face its walking direction, in degrees/second.")]
        public float rotationSpeed = 360f;
        [Tooltip("Distance to a destination node at which it's considered reached.")]
        public float arriveRadius = 0.3f;

        [Header("Stops")]
        [Tooltip("Chance, on reaching a destination, of idling in place for a few seconds instead of immediately picking a new one.")]
        [Range(0f, 1f)] public float idleStopChance = 0.3f;
        [Tooltip("Shortest random idle stop duration, in seconds.")]
        public float idleStopDurationMin = 2f;
        [Tooltip("Longest random idle stop duration, in seconds.")]
        public float idleStopDurationMax = 6f;
    }

    // Applied to the generated PedestrianManager by CityGeneratorPedestrianBuilder.AddManagerComponent.
    [Serializable]
    internal class CrowdSettings
    {
        [Header("Local separation")]
        [Tooltip("Size of the spatial grid cell used to find nearby pedestrians for the separation nudge.")]
        public float separationCellSize = 8f;
        [Tooltip("Distance within which two pedestrians push apart from each other.")]
        public float separationRadius = 0.6f;
        [Tooltip("Strength of the push-apart nudge between nearby pedestrians.")]
        public float separationStrength = 2f;

        [Header("Player avoidance")]
        [Tooltip("Distance within which pedestrians steer away from the player.")]
        public float playerAvoidanceRadius = 1f;
        [Tooltip("Strength of the steer-away nudge from the player.")]
        public float playerAvoidanceStrength = 6f;

        [Header("Performance staggering")]
        [Tooltip("Below this agent count every pedestrian's forward sensor runs every frame. Above it, agents far from the camera are staggered — see PedestrianManager.")]
        public int staggerMinAgentCount = 60;
        [Tooltip("Distance from the camera beyond which a pedestrian's sensor is staggered instead of running every frame.")]
        public float staggerDistance = 60f;
        [Tooltip("A staggered pedestrian's sensor runs 1 out of every this many frames, reusing the previous clearance in between.")]
        public int staggerFrames = 4;
    }

    [Serializable]
    internal enum CustomPlaceFacing { North, East, South, West } // 90-degree steps, same axis as BuildingBuilder's Euler(0, 90*n, 0)

    // Instantiated by CityGeneratorCustomPlaceBuilder at a fixed block/slot/orientation instead of
    // a random building. cornerSlot reuses CityGeneratorBuildingBuilder.SlotOffsets' 0-3 indices so
    // the picker, this builder and the building builder share one geometric source of truth.
    [Serializable]
    internal struct CustomPlaceEntry
    {
        [Tooltip("Display name for this entry in the tool UI and in validation messages. Required.")]
        public string title;
        [Tooltip("Prefab instantiated at the chosen position. Required.")]
        public GameObject prefab; // required
        [Tooltip("Marks this place as a Point of Interest shown on the Minimap HUD, labelled with Title.")]
        public bool isPointOfInterest;
        [Tooltip("If true, occupies the whole block (all 4 corner slots) instead of a single 22 m corner slot.")]
        public bool occupiesFullBlock;
        [Tooltip("Block (x, y) chosen by clicking this entry's grid preview. Must be within the grid and not a plaza block.")]
        public Vector2Int blockCell;
        [Tooltip("Corner slot within the block (0-3, same convention as CityGeneratorBuildingBuilder.SlotOffsets), chosen by clicking a quadrant. Ignored when occupiesFullBlock is true.")]
        public int cornerSlot;
        [Tooltip("Fixed orientation, in 90-degree steps. Never randomised, unlike normal buildings.")]
        public CustomPlaceFacing facing;
        // Internal bookkeeping: whether blockCell/cornerSlot were ever set via the grid preview
        // (distinguishes "not placed yet" from a legitimate (0,0) selection), read by the validator.
        public bool positionAssigned;
    }

    // Consumed by CityGeneratorMinimapBuilder (snapshot capture) and CityGeneratorSceneBuilder
    // (instantiates the MinimapHUD prefab and writes viewRadiusMeters onto it, mirroring how
    // CreateMainCamera applies the Camera tab to the generated ThirdPersonCamera).
    [Serializable]
    internal struct MinimapSettings
    {
        [Tooltip("Whether a minimap HUD is generated and added to the scene. On by default.")]
        public bool enabled;
        [Tooltip("Resolution (width and height, in pixels) of the top-down snapshot texture. Higher values cost more texture memory and disk space for the generated PNG asset.")]
        public int textureResolution;
        [Tooltip("Radius, in meters, of the world area visible in the minimap HUD around the player. Must not exceed the snapshot's covered world size.")]
        public float viewRadiusMeters;

        public static MinimapSettings Default() => new MinimapSettings
        {
            enabled = true,
            textureResolution = 2048,
            viewRadiusMeters = 60f,
        };
    }

    // Consumed by CityGeneratorSceneBuilder.CreateDirectionalLight/RebuildInActiveScene, which
    // add/update a Runtime.DayNightCycle on the generated Directional Light and project these
    // fields onto it, mirroring how MinimapSettings feeds MinimapHUD. The component itself always
    // ends up on the light; `enabled` only maps onto the component's own MonoBehaviour.enabled, so
    // startHour is always reflected on the light even when the cycle is off.
    [Serializable]
    internal struct DayNightSettings
    {
        [Tooltip("Whether the Directional Light auto-advances through a 24h day/night cycle in Play Mode. Off by default. Start Hour is always applied to the light regardless of this toggle.")]
        public bool enabled;
        [Tooltip("Hour of day (0-24) the light is set to, both in Play Mode and as an Editor preview right after generation — applied even when Enabled is off.")]
        [Range(0f, 24f)] public float startHour;
        [Tooltip("How fast simulated time passes relative to real time. 1 = real time, 2 = twice real time, etc.")]
        [Range(1f, 1000f)] public float speedMultiplier;
        [Tooltip("Light color over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public Gradient lightColorOverTime;
        [Tooltip("Light intensity over the course of a day, sampled at time 0-1 (0 = midnight, 0.5 = noon).")]
        public AnimationCurve lightIntensityOverTime;

        public static DayNightSettings Default() => new DayNightSettings
        {
            enabled = true,
            startHour = 10f,
            speedMultiplier = 30f,
            lightColorOverTime = DefaultColorGradient(),
            lightIntensityOverTime = DefaultIntensityCurve(),
        };

        // Cool/dark blue by night, warm orange at sunrise/sunset, white at noon.
        private static Gradient DefaultColorGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.12f, 0.16f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.3f), 0.25f),
                    new GradientColorKey(Color.white, 0.5f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0.75f),
                    new GradientColorKey(new Color(0.12f, 0.16f, 0.35f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            return gradient;
        }

        // Low at night (never zero, so the light stays visibly on), rising toward sunrise, peaking
        // at noon, falling toward sunset.
        private static AnimationCurve DefaultIntensityCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.1f),
                new Keyframe(0.25f, 0.6f),
                new Keyframe(0.5f, 1.2f),
                new Keyframe(0.75f, 0.6f),
                new Keyframe(1f, 0.1f));
        }
    }

    // Consumed by CityGeneratorAudioBuilder: BuildAmbience creates one 2D AudioSource per
    // AmbienceClipEntry as a direct child of cityRoot; BuildPlazaAudio creates one 3D
    // AudioSource per PlazaAudioClipEntry inside every plaza block's own group. Ambience has no
    // default plaza-audio counterpart in DefaultAssets — plazaAudio.clips starts empty.
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

        public static AmbienceSettings Default() => new AmbienceSettings
        {
            enabled = true,
            clips = new List<AmbienceClipEntry> { new AmbienceClipEntry { clip = null, volume = 1f } },
        };
    }

    [Serializable]
    internal struct AmbienceClipEntry
    {
        [Tooltip("Ambience clip for this entry. Required.")]
        public AudioClip clip;
        [Tooltip("This entry's own volume, independent of the other entries in the list.")]
        [Range(0f, 1f)] public float volume;
    }

    [Serializable]
    internal struct PlazaAudioSettings
    {
        [Tooltip("Whether each generated plaza gets its own positional audio source. On by default.")]
        public bool enabled;
        [Tooltip("Clips that loop simultaneously at every plaza's position, each at its own volume and hearing range.")]
        public List<PlazaAudioClipEntry> clips;

        public static PlazaAudioSettings Default() => new PlazaAudioSettings
        {
            enabled = true,
            clips = new List<PlazaAudioClipEntry>(),
        };
    }

    [Serializable]
    internal struct PlazaAudioClipEntry
    {
        [Tooltip("Plaza clip for this entry. Required.")]
        public AudioClip clip;
        [Tooltip("This entry's own volume, independent of the other entries in the list.")]
        [Range(0f, 1f)] public float volume;
        [Tooltip("AudioSource.minDistance: distance at which attenuation starts.")]
        public float minDistance;
        [Tooltip("AudioSource.maxDistance: distance at which the clip stops being audible.")]
        public float maxDistance;
    }
}
