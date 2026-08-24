using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    internal readonly struct BlockCell
    {
        public readonly int gridX;
        public readonly int gridY;
        public readonly Vector3 center;
        public readonly bool isPlaza;

        public BlockCell(int gridX, int gridY, Vector3 center, bool isPlaza)
        {
            this.gridX = gridX;
            this.gridY = gridY;
            this.center = center;
            this.isPlaza = isPlaza;
        }
    }

    /// <summary>
    /// Computes the block grid layout: block centers, street axis positions and which
    /// blocks are plazas. Pure logic, no scene/GameObject access, so it is deterministic
    /// given the same settings and seeded <see cref="System.Random"/>.
    /// </summary>
    internal static class CityGeneratorGrid
    {
        /// <summary>Position of block index <paramref name="index"/> (0..count-1) along one axis.</summary>
        public static float GetBlockAxisPosition(int count, int index)
        {
            return (index - (count - 1) / 2f) * CityGeneratorConstants.CellPitch;
        }

        /// <summary>Position of street axis <paramref name="index"/> (0..count) along one dimension of a <paramref name="count"/>-block grid.</summary>
        public static float GetStreetAxisPosition(int count, int index)
        {
            return (index - count / 2f) * CityGeneratorConstants.CellPitch;
        }

        public static Vector3 GetBlockCenter(int gridX, int gridY, int gridWidth, int gridHeight)
        {
            return new Vector3(GetBlockAxisPosition(gridWidth, gridX), 0f, GetBlockAxisPosition(gridHeight, gridY));
        }

        public static List<BlockCell> BuildBlocks(int gridWidth, int gridHeight, IReadOnlyCollection<Vector2Int> plazaCells)
        {
            var plazaLookup = new HashSet<Vector2Int>(plazaCells);
            var cells = new List<BlockCell>(gridWidth * gridHeight);

            for (int gy = 0; gy < gridHeight; gy++)
            {
                for (int gx = 0; gx < gridWidth; gx++)
                {
                    Vector3 center = GetBlockCenter(gx, gy, gridWidth, gridHeight);
                    cells.Add(new BlockCell(gx, gy, center, plazaLookup.Contains(new Vector2Int(gx, gy))));
                }
            }

            return cells;
        }
    }
}
