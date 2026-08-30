using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Custom Grid overload (SPEC 11): builds blocks only for <paramref name="customBlockCells"/>,
        /// using the fixed MaxGridSize x MaxGridSize canvas for world position so a block never
        /// shifts position in the world as the shape grows/shrinks around it.
        /// </summary>
        public static List<BlockCell> BuildBlocks(IReadOnlyCollection<Vector2Int> customBlockCells, IReadOnlyCollection<Vector2Int> plazaCells)
        {
            var plazaLookup = new HashSet<Vector2Int>(plazaCells);
            var cells = new List<BlockCell>(customBlockCells.Count);
            int canvas = CityGeneratorConstants.MaxGridSize;

            foreach (var cell in customBlockCells)
            {
                Vector3 center = GetBlockCenter(cell.x, cell.y, canvas, canvas);
                cells.Add(new BlockCell(cell.x, cell.y, center, plazaLookup.Contains(cell)));
            }

            return cells;
        }

        private static readonly Vector2Int[] OrthogonalOffsets =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        };

        /// <summary>
        /// True if <paramref name="cell"/> lies within the fixed MaxGridSize canvas, is not
        /// already in <paramref name="existingCells"/>, and is orthogonally adjacent to at least
        /// one cell of <paramref name="existingCells"/>.
        /// </summary>
        public static bool IsValidAddition(IReadOnlyCollection<Vector2Int> existingCells, Vector2Int cell)
        {
            int canvas = CityGeneratorConstants.MaxGridSize;
            if (cell.x < 0 || cell.y < 0 || cell.x >= canvas || cell.y >= canvas)
            {
                return false;
            }

            if (existingCells.Contains(cell))
            {
                return false;
            }

            foreach (var offset in OrthogonalOffsets)
            {
                if (existingCells.Contains(cell + offset))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if removing <paramref name="removed"/> from <paramref name="existingCells"/>
        /// leaves at least one cell, all orthogonally connected (a single component). False if
        /// <paramref name="removed"/> is not in <paramref name="existingCells"/>.
        /// </summary>
        public static bool CanRemoveWithoutSplitting(IReadOnlyCollection<Vector2Int> existingCells, Vector2Int removed)
        {
            if (!existingCells.Contains(removed))
            {
                return false;
            }

            var remaining = new HashSet<Vector2Int>(existingCells);
            remaining.Remove(removed);

            if (remaining.Count == 0)
            {
                return false;
            }

            var visited = new HashSet<Vector2Int>();
            var stack = new Stack<Vector2Int>();
            Vector2Int start = default;
            foreach (var cell in remaining)
            {
                start = cell;
                break;
            }

            stack.Push(start);
            visited.Add(start);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var offset in OrthogonalOffsets)
                {
                    var neighbor = current + offset;
                    if (remaining.Contains(neighbor) && visited.Add(neighbor))
                    {
                        stack.Push(neighbor);
                    }
                }
            }

            return visited.Count == remaining.Count;
        }
    }
}
