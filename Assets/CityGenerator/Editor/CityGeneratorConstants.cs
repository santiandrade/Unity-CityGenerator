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
        public const float ZebraStripeSpacing = StreetWidth / (ZebraStripesPerArm + 1);

        public const float GroundDatumY = 0.18f; // top of sidewalks: where buildings/plaza/props/vegetation sit
        public const int MaxBuildingSlotsPerBlock = 4;
        public const float BuildingSlotPitch = 22f;

        public const float PlazaLawnPitch = 22f;
        public const float PlazaLawnFootprint = 20f;
        public const float PlazaBenchOffset = 16f;
        public const float PlazaVegetationGridExtent = 18f;
        public const float PlazaVegetationGridStep = 9f;
    }
}
