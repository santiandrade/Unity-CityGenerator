namespace CityGenerator.Editor
{
    /// <summary>
    /// Layout constants shared by the grid, ground and placement builders.
    /// Sizes/spacing reproduce the geometry of the original hand-built reference city
    /// (46 m blocks on a 56 m pitch), generalised to an arbitrary grid size.
    /// </summary>
    internal static class CityGeneratorConstants
    {
        public const float BlockSize = 46f;
        public const float CellPitch = 56f;
        public const float StreetWidth = CellPitch - BlockSize;

        public const float RoadBaseMargin = 6f;
        public const float RoadBaseY = -0.05f;
        public const float SidewalkY = 0.09f;
        public const float MarkingY = 0.012f;

        public const int DashesPerSegment = 4;
        public const int ZebraStripesPerArm = 5;
        public const float ZebraStripeSpacing = 2f;
        // Distance from the intersection centre to the crosswalk, along the arm: half the
        // street width plus the lane offset (TrafficNetwork.laneOffset), i.e. right at the stop line.
        public const float ZebraArmOffset = StreetWidth / 2f + 2.6f;
        // Radius, along the street, within which a dash is skipped near a signalled
        // intersection: covers the crosswalk stripe spread (arm offset ± half the stripe span).
        public const float DashZebraExclusionRadius = ZebraArmOffset + (ZebraStripesPerArm - 1) / 2f * ZebraStripeSpacing;

        public const float GroundDatumY = 0.18f; // top of sidewalks: where buildings/plaza/props/vegetation sit
        public const int MaxBuildingSlotsPerBlock = 4;
        public const float BuildingSlotPitch = 22f;

        public const float PlazaLawnPitch = 22f;
        public const float PlazaLawnFootprint = 20f;
        // 7.5 m from the plaza centre along each diagonal (7.5 / sqrt(2)) — reproduces the
        // reference plaza's four benches sitting between the lawn quadrants.
        public const float PlazaBenchRadius = 5.3033009f;
        // Candidate grid for a lawn quadrant's own vegetation: kept inset from the lawn's
        // footprint edge so trees never spill onto the sidewalk ring around it.
        public const float PlazaLawnVegetationExtent = 8f;
        public const float PlazaLawnVegetationStep = 4f;
        // Plazas use a fraction of the configured vegetation density: their lawn candidate grid
        // is much denser than the street candidates, so the same density value would otherwise
        // pack noticeably more trees inside plazas than along the streets.
        public const float PlazaVegetationDensityFactor = 0.5f;

        // Sidewalk/corner candidates stay outside the building slot area (max radius ~18 m),
        // in the peripheral ring of every 46 m block.
        public const float StreetEdgeInset = 2f;
        public const int StreetEdgePointsPerSide = 3;
        public const int StreetVegetationPointsPerSide = 4;
        public const float BinCornerInset = 2f;

        // Lamps sit right at the true block edge (closer to the curb than other street
        // furniture) so that, on a plaza block, they clear the lawn footprint (which reaches to
        // BlockSize/2 - 2, i.e. 21 m) instead of overlapping it.
        public const float LampEdgeInset = 1f;
        public const int LampPointsPerSide = 3;

        // Traffic lights stand on the near corner of the sidewalk across the crossing (the far
        // side of the arm, offset sideways like a lane), verified against the reference city:
        // both offsets equal, just past the road's half-width so they never stand on the asphalt.
        public const float TrafficLightCornerOffset = 6.2f;
        public const float TrafficLightStartOffsetMax = 4f;
        public const string VehicleLayerName = "Vehicle";

        // Layers 0-7 are Unity's built-in/reserved slots (Default, TransparentFX, Ignore Raycast,
        // Water, UI, plus a few left unnamed by Unity itself) — auto-creating the Vehicle layer
        // only searches from here up, same convention every other layer-creating tool follows.
        public const int FirstUserLayerIndex = 8;

        // CarAgent has no route planning or congestion avoidance, so once a stretch of road
        // fills up it stays gridlocked. Measured on a 5x5 grid (264 valid spawn nodes): 100
        // vehicles (38%) flowed with the occasional self-resolving crossing conflict, 200 (76%)
        // gridlocked from the first frame. 0.4 keeps a margin below the observed good case.
        public const float VehicleDensityWarningThreshold = 0.4f;

        // Default CharacterController/PlayerController configuration applied by
        // CityGeneratorSceneBuilder to whichever character prefab is assigned as Player Prefab,
        // so every DefaultAssets/Prefabs/Characters/ model can stay a clean, generation-agnostic
        // model+Animator prefab instead of each carrying its own baked movement setup.
        public const float PlayerControllerHeight = 0.72f;
        public const float PlayerControllerRadius = 0.2f;
        public const float PlayerControllerSlopeLimit = 45f;
        public const float PlayerControllerStepOffset = 0.2f;
        public const float PlayerControllerSkinWidth = 0.02f;
        public const float PlayerControllerMinMoveDistance = 0.001f;
        public static readonly UnityEngine.Vector3 PlayerControllerCenter = new(0f, 0.36f, 0f);

        public const string PlayerActionMapName = "Player";
        public const string PlayerMoveActionName = "Move";
        public const string PlayerJumpActionName = "Jump";
        public const string PlayerSprintActionName = "Sprint";
        public const float PlayerWalkSpeed = 4f;
        public const float PlayerRunSpeed = 8f;
        public const float PlayerRotationSmoothTime = 0.1f;
        public const float PlayerGravity = -20f;
        public const float PlayerJumpHeight = 1.2f;

        public const string PedestrianLayerName = "Pedestrian";

        // Ring sits between the building slot edge (~18 m) and street furniture (StreetEdgeInset
        // ring at 21 m): BlockSize/2 (23) - PedestrianRingInset (3.5) = 19.5 m from block centre.
        // Falls in a gap that's free by construction, so the ring needs no obstacle avoidance
        // against street furniture.
        public const float PedestrianRingInset = 3.5f;

        // Sidewalk nodes sit at sidewalk height (GroundDatumY); crossing nodes at the road
        // surface. The agent has no raycast, so it interpolates Y with MoveTowards between the
        // two datums while walking a crosswalk arm.
        public const float PedestrianRoadY = 0f;

        // Per-instance lateral offset from the path centreline (perpendicular to travel
        // direction), so a group of pedestrians walking the same ring segment doesn't render as
        // a single file line.
        public const float PedestrianLaneJitter = 0.4f;

        // Calibration anchors, not a pedestrian's own pace: CharacterAnimator.controller's
        // Locomotion blend tree reaches Speed = 0.5 at PlayerWalkSpeed and Speed = 1 at
        // PlayerRunSpeed (see PlayerController's normalizedSpeed mapping). PedestrianAgent scales
        // its Speed parameter proportionally to its actual pace against these two anchors, so any
        // pace in between (or beyond) still lines up with the blend tree instead of foot-sliding.
        public const float PedestrianWalkReferenceSpeed = PlayerWalkSpeed;
        public const float PedestrianRunReferenceSpeed = PlayerRunSpeed;

        // Most pedestrians stroll at half PlayerWalkSpeed: at the full reference speed the walk
        // cycle reads as a brisk power-walk rather than a casual stroll.
        public const float PedestrianPaceFraction = 0.5f;

        // Fraction of pedestrians that jog/run (simulating exercise or running late) at
        // PedestrianRunReferenceSpeed instead of the usual stroll.
        public const float PedestrianRunnerChance = 0.15f;

        // +-10% per-instance speed jitter, same convention as CarAgent's +-6% maxSpeed jitter,
        // so a spawned crowd doesn't move in mechanical lockstep.
        public const float PedestrianSpeedJitter = 0.1f;

        // Chance, on reaching a destination, of idling in place for a few seconds instead of
        // immediately picking a new one -- keeps pedestrians from reading as being on rails
        // between two fixed points.
        public const float PedestrianIdleStopChance = 0.3f;
        public const float PedestrianIdleStopDurationMin = 2f;
        public const float PedestrianIdleStopDurationMax = 6f;

        // Longer stop range used at a PointOfInterest node (bench/fountain), where lingering
        // reads as sitting/resting rather than a random street pause.
        public const float PedestrianPoiStopDurationMin = 5f;
        public const float PedestrianPoiStopDurationMax = 15f;

        // PedestrianAgent has no jam/gridlock mechanics of its own -- crowding only shows up as
        // local separation (PedestrianManager's boids-style grid), never a stopped agent -- so a
        // much larger fraction of ring nodes can be filled before it reads as overcrowded than
        // the vehicle case (VehicleDensityWarningThreshold, 0.4).
        public const float PedestrianCountWarningThreshold = 0.7f;

        // Short loop of PointOfInterest nodes around a plaza's centerpiece, inside the bench
        // radius (PlazaBenchRadius, ~5.3) so it reads as "approaching the fountain" rather than
        // overlapping the benches' own footprint.
        public const float PlazaCenterpieceRingRadius = 2.5f;
    }
}
