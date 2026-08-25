using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.EditMode
{
    internal class CityGeneratorGridTests
    {
        [Test]
        public void BuildBlocks_ReturnsOneCellPerGridPosition()
        {
            List<BlockCell> cells = CityGeneratorGrid.BuildBlocks(3, 2, new List<Vector2Int>());

            Assert.AreEqual(6, cells.Count);
            for (int gx = 0; gx < 3; gx++)
            {
                for (int gy = 0; gy < 2; gy++)
                {
                    Assert.IsTrue(cells.Exists(c => c.gridX == gx && c.gridY == gy),
                        $"Missing cell ({gx},{gy})");
                }
            }
        }

        [Test]
        public void BuildBlocks_MarksOnlyConfiguredCellsAsPlazas()
        {
            var plazaCells = new List<Vector2Int> { new(1, 1), new(2, 0) };
            List<BlockCell> cells = CityGeneratorGrid.BuildBlocks(3, 3, plazaCells);

            foreach (BlockCell cell in cells)
            {
                bool shouldBePlaza = plazaCells.Contains(new Vector2Int(cell.gridX, cell.gridY));
                Assert.AreEqual(shouldBePlaza, cell.isPlaza, $"Cell ({cell.gridX},{cell.gridY})");
            }
        }

        [Test]
        public void BuildBlocks_WithNoPlazaCells_MarksNoBlockAsPlaza()
        {
            List<BlockCell> cells = CityGeneratorGrid.BuildBlocks(2, 2, new List<Vector2Int>());
            Assert.IsFalse(cells.Exists(c => c.isPlaza));
        }

        [Test]
        public void GetBlockCenter_IsSymmetricAroundOrigin()
        {
            // A 4-wide grid has no exact centre column, but opposite blocks must mirror around 0.
            Vector3 left = CityGeneratorGrid.GetBlockCenter(0, 0, 4, 4);
            Vector3 right = CityGeneratorGrid.GetBlockCenter(3, 0, 4, 4);

            Assert.AreEqual(-right.x, left.x, 0.0001f);
        }

        [Test]
        public void GetBlockAxisPosition_AdjacentBlocksAreOneCellPitchApart()
        {
            float a = CityGeneratorGrid.GetBlockAxisPosition(5, 2);
            float b = CityGeneratorGrid.GetBlockAxisPosition(5, 3);

            Assert.AreEqual(CityGeneratorConstants.CellPitch, b - a, 0.0001f);
        }

        [Test]
        public void GetStreetAxisPosition_HasOneMoreAxisThanBlocks()
        {
            // A 3-block grid has 4 street axes (0..3): the first and last bound the grid.
            float first = CityGeneratorGrid.GetStreetAxisPosition(3, 0);
            float last = CityGeneratorGrid.GetStreetAxisPosition(3, 3);

            Assert.AreEqual(-last, first, 0.0001f);
        }
    }
}
