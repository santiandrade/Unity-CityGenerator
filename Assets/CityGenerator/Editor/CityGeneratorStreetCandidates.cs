using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Generates candidate placement points on a block's sidewalk: along its 4 edges (facing
    /// outward toward the street, for lamps/bus stops/street vegetation) and near its 4
    /// corners (for bins). All points stay in the peripheral ring outside the building slot area.
    /// </summary>
    internal static class CityGeneratorStreetCandidates
    {
        public static List<PlacementCandidate> EdgeCandidates(Vector3 blockCenter, int pointsPerSide)
        {
            return EdgeCandidates(blockCenter, pointsPerSide, CityGeneratorConstants.StreetEdgeInset);
        }

        /// <summary>
        /// Lamp positions: 3 per side, evenly spaced, right at the true block edge (a smaller
        /// inset than other street furniture) so that on a plaza block they clear the lawn
        /// footprint instead of standing on the grass.
        /// </summary>
        public static List<PlacementCandidate> LampCandidates(Vector3 blockCenter)
        {
            return EdgeCandidates(blockCenter, CityGeneratorConstants.LampPointsPerSide, CityGeneratorConstants.LampEdgeInset);
        }

        private static List<PlacementCandidate> EdgeCandidates(Vector3 blockCenter, int pointsPerSide, float edgeInset)
        {
            float edgeDistance = CityGeneratorConstants.BlockSize / 2f - edgeInset;
            float sideSpan = CityGeneratorConstants.BlockSize / 2f - edgeInset * 2f;

            var candidates = new List<PlacementCandidate>();
            AddSide(candidates, blockCenter, pointsPerSide, edgeDistance, sideSpan, Vector3.forward);
            AddSide(candidates, blockCenter, pointsPerSide, edgeDistance, sideSpan, Vector3.back);
            AddSide(candidates, blockCenter, pointsPerSide, edgeDistance, sideSpan, Vector3.right);
            AddSide(candidates, blockCenter, pointsPerSide, edgeDistance, sideSpan, Vector3.left);
            return candidates;
        }

        public static List<PlacementCandidate> CornerCandidates(Vector3 blockCenter)
        {
            float d = CityGeneratorConstants.BlockSize / 2f - CityGeneratorConstants.BinCornerInset;
            var candidates = new List<PlacementCandidate>
            {
                MakeCorner(blockCenter, d, d),
                MakeCorner(blockCenter, d, -d),
                MakeCorner(blockCenter, -d, d),
                MakeCorner(blockCenter, -d, -d),
            };
            return candidates;
        }

        // Points are pulled in from the true corners (by CornerMarginFactor) so that the
        // outermost point of one side doesn't sit right next to the outermost point of the
        // perpendicular side, which would make wide prefabs overlap across the corner.
        private const float CornerMarginFactor = 0.7f;

        private static void AddSide(List<PlacementCandidate> candidates, Vector3 blockCenter, int pointsPerSide, float edgeDistance, float sideSpan, Vector3 outward)
        {
            Vector3 along = new(outward.z, 0f, -outward.x); // perpendicular axis running along the side
            Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);

            for (int i = 0; i < pointsPerSide; i++)
            {
                float t = pointsPerSide == 1 ? 0f : ((i / (float)(pointsPerSide - 1)) * 2f - 1f) * CornerMarginFactor; // -0.7..0.7
                Vector3 position = blockCenter + outward * edgeDistance + along * (t * sideSpan);
                position.y = CityGeneratorConstants.GroundDatumY;
                candidates.Add(new PlacementCandidate(position, rotation));
            }
        }

        private static PlacementCandidate MakeCorner(Vector3 blockCenter, float x, float z)
        {
            Vector3 position = blockCenter + new Vector3(x, CityGeneratorConstants.GroundDatumY, z);
            return new PlacementCandidate(position, Quaternion.identity);
        }
    }
}
