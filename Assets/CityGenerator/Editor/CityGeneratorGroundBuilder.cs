using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds the ground layer of a generated city: the single road base slab, one sidewalk
    /// per block, and the dashed lane lines / zebra crossings that reproduce the reference
    /// city's marking pattern scaled to the grid size.
    /// </summary>
    internal static class CityGeneratorGroundBuilder
    {
        public static void BuildRoadBase(GameObject roadBasePrefab, Transform roadsGroup, int gridWidth, int gridHeight)
        {
            float width = gridWidth * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;
            float depth = gridHeight * CityGeneratorConstants.CellPitch + 2f * CityGeneratorConstants.RoadBaseMargin;

            GameObject instance = InstantiatePrefab(roadBasePrefab, roadsGroup, "Road_Base");
            instance.transform.localPosition = new Vector3(0f, CityGeneratorConstants.RoadBaseY, 0f);
            CityGeneratorBoundsUtility.ScaleToFootprint(instance, width, depth);
        }

        public static void BuildSidewalks(GameObject sidewalkPrefab, Transform sidewalksGroup, IReadOnlyList<BlockCell> blocks)
        {
            foreach (BlockCell block in blocks)
            {
                string name = $"Sidewalk_{block.gridX}_{block.gridY}";
                GameObject instance = InstantiatePrefab(sidewalkPrefab, sidewalksGroup, name);
                instance.transform.localPosition = new Vector3(block.center.x, CityGeneratorConstants.SidewalkY, block.center.z);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, CityGeneratorConstants.BlockSize, CityGeneratorConstants.BlockSize);
            }
        }

        public static void BuildRoadMarkings(GameObject dashPrefab, GameObject zebraPrefab, Transform markingsGroup, int gridWidth, int gridHeight)
        {
            BuildDashes(dashPrefab, markingsGroup, gridWidth, gridHeight);
            BuildZebraCrossings(zebraPrefab, markingsGroup, gridWidth, gridHeight);
        }

        // Horizontal streets (constant Z, running along X): gridHeight + 1 axis lines, each split
        // into gridWidth segments of one cell pitch. Vertical streets: the transposed case.
        private static void BuildDashes(GameObject dashPrefab, Transform group, int gridWidth, int gridHeight)
        {
            int dashIndex = 0;

            for (int j = 0; j <= gridHeight; j++)
            {
                float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                for (int k = 0; k < gridWidth; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, k);
                    PlaceDashSegment(dashPrefab, group, segmentStart, z, isVertical: false, ref dashIndex);
                }
            }

            for (int i = 0; i <= gridWidth; i++)
            {
                float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                for (int k = 0; k < gridHeight; k++)
                {
                    float segmentStart = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, k);
                    PlaceDashSegment(dashPrefab, group, segmentStart, x, isVertical: true, ref dashIndex);
                }
            }
        }

        private static void PlaceDashSegment(GameObject dashPrefab, Transform group, float segmentStart, float crossAxisPosition, bool isVertical, ref int dashIndex)
        {
            float spacing = CityGeneratorConstants.CellPitch / CityGeneratorConstants.DashesPerSegment;
            Quaternion rotation = isVertical ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

            for (int d = 0; d < CityGeneratorConstants.DashesPerSegment; d++)
            {
                float alongAxis = segmentStart + spacing * (d + 0.5f);
                Vector3 position = isVertical
                    ? new Vector3(crossAxisPosition, CityGeneratorConstants.MarkingY, alongAxis)
                    : new Vector3(alongAxis, CityGeneratorConstants.MarkingY, crossAxisPosition);

                string name = (isVertical ? "Dash_V_" : "Dash_H_") + dashIndex;
                GameObject instance = InstantiatePrefab(dashPrefab, group, name);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                dashIndex++;
            }
        }

        // Zebra crossings only mark intersections fully surrounded by blocks (both axis indices
        // strictly interior), matching the reference city's 4 signalled inner crossings.
        private static void BuildZebraCrossings(GameObject zebraPrefab, Transform group, int gridWidth, int gridHeight)
        {
            int zebraIndex = 0;
            for (int i = 1; i < gridWidth; i++)
            {
                for (int j = 1; j < gridHeight; j++)
                {
                    float x = CityGeneratorGrid.GetStreetAxisPosition(gridWidth, i);
                    float z = CityGeneratorGrid.GetStreetAxisPosition(gridHeight, j);
                    Vector3 intersection = new Vector3(x, CityGeneratorConstants.MarkingY, z);

                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.right, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.left, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.forward, ref zebraIndex);
                    PlaceZebraArm(zebraPrefab, group, intersection, Vector3.back, ref zebraIndex);
                }
            }
        }

        private static void PlaceZebraArm(GameObject zebraPrefab, Transform group, Vector3 intersection, Vector3 direction, ref int zebraIndex)
        {
            bool isEastWestArm = Mathf.Abs(direction.x) > 0f;
            Quaternion rotation = isEastWestArm ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

            for (int s = 0; s < CityGeneratorConstants.ZebraStripesPerArm; s++)
            {
                float distance = CityGeneratorConstants.ZebraStripeSpacing * (s + 1);
                Vector3 position = intersection + direction * distance;
                position.y = CityGeneratorConstants.MarkingY;

                string name = "Zebra_" + zebraIndex;
                GameObject instance = InstantiatePrefab(zebraPrefab, group, name);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                zebraIndex++;
            }
        }

        private static GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            return instance;
        }
    }
}
