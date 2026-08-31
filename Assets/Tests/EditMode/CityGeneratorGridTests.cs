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

        [Test]
        public void BuildBlocks_CustomShape_ReturnsOnlyListedCells()
        {
            var shape = new List<Vector2Int> { new(4, 4), new(5, 4), new(5, 5) };
            List<BlockCell> cells = CityGeneratorGrid.BuildBlocks(shape, new List<Vector2Int> { new(5, 5) });

            Assert.AreEqual(3, cells.Count);
            foreach (var coord in shape)
            {
                BlockCell cell = cells.Find(c => c.gridX == coord.x && c.gridY == coord.y);
                Assert.AreEqual(coord == new Vector2Int(5, 5), cell.isPlaza, $"Cell {coord}");
            }
        }

        [Test]
        public void BuildBlocks_CustomShape_PositionIndependentOfShapeExtent()
        {
            // A block's world position must not shift when the shape around it grows/shrinks:
            // both use the fixed MaxGridSize canvas, not the shape's own bounding box.
            var smallShape = new List<Vector2Int> { new(4, 4) };
            var largeShape = new List<Vector2Int> { new(4, 4), new(5, 4), new(4, 5), new(3, 4) };

            Vector3 posInSmallShape = CityGeneratorGrid.BuildBlocks(smallShape, new List<Vector2Int>())[0].center;
            BlockCell sameCellInLargeShape = CityGeneratorGrid.BuildBlocks(largeShape, new List<Vector2Int>())
                .Find(c => c.gridX == 4 && c.gridY == 4);

            Assert.AreEqual(posInSmallShape, sameCellInLargeShape.center);
        }

        [Test]
        public void IsValidAddition_SingleCell_OnlyOrthogonalNeighborsAreValid()
        {
            var existing = new List<Vector2Int> { new(5, 5) };

            Assert.IsTrue(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(6, 5)));
            Assert.IsTrue(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(4, 5)));
            Assert.IsTrue(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(5, 6)));
            Assert.IsTrue(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(5, 4)));
        }

        [Test]
        public void IsValidAddition_DiagonalNeighborDoesNotCount()
        {
            var existing = new List<Vector2Int> { new(5, 5) };
            Assert.IsFalse(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(6, 6)));
        }

        [Test]
        public void IsValidAddition_AlreadyExistingCellIsInvalid()
        {
            var existing = new List<Vector2Int> { new(5, 5) };
            Assert.IsFalse(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(5, 5)));
        }

        [Test]
        public void IsValidAddition_OutsideCanvasIsInvalid()
        {
            var existing = new List<Vector2Int> { new(0, 0) };
            Assert.IsFalse(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(-1, 0)));
            Assert.IsFalse(CityGeneratorGrid.IsValidAddition(existing, new Vector2Int(0, CityGeneratorConstants.MaxGridSize)));
        }

        [Test]
        public void CanRemoveWithoutSplitting_LShape_TipIsRemovable()
        {
            // L shape: (0,0)-(1,0)-(1,1). Removing either tip leaves a connected pair.
            var lShape = new List<Vector2Int> { new(0, 0), new(1, 0), new(1, 1) };

            Assert.IsTrue(CityGeneratorGrid.CanRemoveWithoutSplitting(lShape, new Vector2Int(0, 0)));
            Assert.IsTrue(CityGeneratorGrid.CanRemoveWithoutSplitting(lShape, new Vector2Int(1, 1)));
        }

        [Test]
        public void CanRemoveWithoutSplitting_RemovingJointWouldSplitShape()
        {
            // Straight line of 3: (0,0)-(1,0)-(2,0). Removing the middle cell splits it in two.
            var line = new List<Vector2Int> { new(0, 0), new(1, 0), new(2, 0) };
            Assert.IsFalse(CityGeneratorGrid.CanRemoveWithoutSplitting(line, new Vector2Int(1, 0)));
        }

        [Test]
        public void CanRemoveWithoutSplitting_LastRemainingCellCannotBeRemoved()
        {
            var single = new List<Vector2Int> { new(5, 5) };
            Assert.IsFalse(CityGeneratorGrid.CanRemoveWithoutSplitting(single, new Vector2Int(5, 5)));
        }

        [Test]
        public void CanRemoveWithoutSplitting_CellNotInShapeReturnsFalse()
        {
            var shape = new List<Vector2Int> { new(0, 0), new(1, 0) };
            Assert.IsFalse(CityGeneratorGrid.CanRemoveWithoutSplitting(shape, new Vector2Int(9, 9)));
        }
    }
}
