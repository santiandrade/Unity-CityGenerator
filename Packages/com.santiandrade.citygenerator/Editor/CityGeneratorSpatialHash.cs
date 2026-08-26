using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Uniform spatial hash over XZ <see cref="Rect"/>s, used by <see cref="CityGeneratorPlacementEngine"/>
    /// to avoid a linear scan of every already-placed obstacle on each overlap check. Purely an
    /// index: it never owns the obstacle list itself, so it can't drift out of sync with it as
    /// long as every insertion mirrors what gets added to that list (see
    /// <see cref="ObstacleCache.SyncSpatialHash"/>).
    /// </summary>
    internal sealed class CityGeneratorSpatialHash
    {
        private readonly float cellSize;
        private readonly Dictionary<(int x, int z), List<Rect>> cells = new();

        public CityGeneratorSpatialHash(float cellSize)
        {
            this.cellSize = cellSize;
        }

        /// <summary>Registers <paramref name="bounds"/> in every cell it overlaps.</summary>
        public void Insert(Rect bounds)
        {
            (int minX, int minZ) = CellOf(bounds.xMin, bounds.yMin);
            (int maxX, int maxZ) = CellOf(bounds.xMax, bounds.yMax);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var key = (x, z);
                    if (!cells.TryGetValue(key, out List<Rect> bucket))
                    {
                        bucket = new List<Rect>();
                        cells[key] = bucket;
                    }
                    bucket.Add(bounds);
                }
            }
        }

        /// <summary>True if any previously-inserted rect overlaps <paramref name="candidate"/>. Only
        /// queries the candidate's own cells and their neighbours, not every inserted rect.</summary>
        public bool Overlaps(Rect candidate)
        {
            (int minX, int minZ) = CellOf(candidate.xMin, candidate.yMin);
            (int maxX, int maxZ) = CellOf(candidate.xMax, candidate.yMax);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!cells.TryGetValue((x, z), out List<Rect> bucket))
                        continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        if (bucket[i].Overlaps(candidate))
                            return true;
                    }
                }
            }

            return false;
        }

        private (int x, int z) CellOf(float x, float z)
            => (Mathf.FloorToInt(x / cellSize), Mathf.FloorToInt(z / cellSize));
    }
}
