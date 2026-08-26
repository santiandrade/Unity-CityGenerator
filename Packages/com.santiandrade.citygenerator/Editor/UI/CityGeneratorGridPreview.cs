using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>Which behaviour a <see cref="CityGeneratorGridPreview"/> instance implements. Kept as
    /// two isolated code paths (never mixed in the same instance) so generalising the picker for
    /// Custom Places can't regress the existing plaza editor.</summary>
    internal enum CityGeneratorGridPreviewMode
    {
        /// <summary>The City tab's plaza editor: clicking a cell toggles it in a multi-value list (general.plazaCells).</summary>
        PlazaMultiToggle,
        /// <summary>A Custom Place entry's own picker: clicking a cell selects it as the single chosen block, and (unless the entry occupies the full block) which quadrant within it was clicked.</summary>
        SingleSelectQuadrant,
    }

    /// <summary>
    /// Paints a top-down miniature of the configured grid (blocks, streets, plaza blocks) so
    /// Grid Width/Height mean something before generating. In <see cref="CityGeneratorGridPreviewMode.PlazaMultiToggle"/>
    /// mode it doubles as the editor for which blocks are plazas: clicking a cell toggles it
    /// directly against the bound <c>general.plazaCells</c> list, matching
    /// <see cref="CityGeneratorGrid.BuildBlocks"/> exactly — there is no separate count to fall
    /// out of sync with the picture. In <see cref="CityGeneratorGridPreviewMode.SingleSelectQuadrant"/>
    /// mode it instead picks a single block (and, unless occupying the full block, one of its 4
    /// corner quadrants) for one Custom Place entry.
    /// </summary>
    internal class CityGeneratorGridPreview : VisualElement
    {
        private int gridWidth = 5;
        private int gridHeight = 5;
        private CityGeneratorGridPreviewMode mode = CityGeneratorGridPreviewMode.PlazaMultiToggle;
        private Action onChanged;

        // PlazaMultiToggle mode.
        private SerializedProperty plazaCellsProperty;

        // SingleSelectQuadrant mode.
        private SerializedProperty blockCellProperty;
        private SerializedProperty cornerSlotProperty;
        private SerializedProperty positionAssignedProperty;
        private Func<bool> occupiesFullBlockGetter;

        public CityGeneratorGridPreview()
        {
            AddToClassList("cg-grid-preview");
            tooltip = "Click a block to toggle it as a plaza.";
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        /// <summary>Binds this preview as the plaza editor. Call once; <see cref="SetGrid"/> drives repaints/pruning afterwards.</summary>
        public void Bind(SerializedProperty plazaCellsProperty, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.PlazaMultiToggle;
            this.plazaCellsProperty = plazaCellsProperty;
            this.onChanged = onChanged;
            tooltip = "Click a block to toggle it as a plaza.";
        }

        /// <summary>
        /// Binds this preview as a single Custom Place entry's picker: clicking a cell writes
        /// <paramref name="blockCellProperty"/> and sets <paramref name="positionAssignedProperty"/>;
        /// when <paramref name="occupiesFullBlockGetter"/> returns false at click time, the clicked
        /// quadrant within that cell is also written to <paramref name="cornerSlotProperty"/> using
        /// the same 0-3 convention as <see cref="CityGeneratorBuildingBuilder.SlotOffsets"/>.
        /// </summary>
        public void BindSingleSelection(SerializedProperty blockCellProperty, SerializedProperty cornerSlotProperty, SerializedProperty positionAssignedProperty, Func<bool> occupiesFullBlockGetter, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.SingleSelectQuadrant;
            this.blockCellProperty = blockCellProperty;
            this.cornerSlotProperty = cornerSlotProperty;
            this.positionAssignedProperty = positionAssignedProperty;
            this.occupiesFullBlockGetter = occupiesFullBlockGetter;
            this.onChanged = onChanged;
            tooltip = "Click a block to place this entry there; click a corner of the block for a quadrant slot.";
        }

        public void SetGrid(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (gridWidth == width && gridHeight == height)
                return;

            gridWidth = width;
            gridHeight = height;
            if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
                PruneOutOfRangeCells();
            MarkDirtyRepaint();
        }

        /// <summary>Forces a repaint after an external change to the bound property (e.g. occupiesFullBlock toggled elsewhere).</summary>
        public void Refresh() => MarkDirtyRepaint();

        // Cells picked while the grid was larger than it is now would otherwise linger as plazas
        // no BlockCell can ever match (CityGeneratorGrid.BuildBlocks only looks at 0..width/height-1).
        private void PruneOutOfRangeCells()
        {
            if (plazaCellsProperty == null)
                return;

            plazaCellsProperty.serializedObject.Update();
            bool changed = false;
            for (int i = plazaCellsProperty.arraySize - 1; i >= 0; i--)
            {
                Vector2Int cell = plazaCellsProperty.GetArrayElementAtIndex(i).vector2IntValue;
                if (cell.x < 0 || cell.x >= gridWidth || cell.y < 0 || cell.y >= gridHeight)
                {
                    plazaCellsProperty.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }

            if (changed)
            {
                plazaCellsProperty.serializedObject.ApplyModifiedProperties();
                onChanged?.Invoke();
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Rect area = contentRect;
            if (area.width <= 0f || area.height <= 0f)
                return;

            float cellSize = Mathf.Min(area.width / gridWidth, area.height / gridHeight);
            float totalWidth = cellSize * gridWidth;
            float totalHeight = cellSize * gridHeight;
            float originX = (area.width - totalWidth) * 0.5f;
            float originY = (area.height - totalHeight) * 0.5f;

            Vector2 local = evt.localPosition;
            int gx = Mathf.FloorToInt((local.x - originX) / cellSize);
            int gy = Mathf.FloorToInt((local.y - originY) / cellSize);
            if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight)
                return;

            if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
            {
                TogglePlazaCell(new Vector2Int(gx, gy));
            }
            else
            {
                float cellLocalX = local.x - (originX + gx * cellSize);
                float cellLocalY = local.y - (originY + gy * cellSize);
                SelectSingleCell(new Vector2Int(gx, gy), cellLocalX, cellLocalY, cellSize);
            }

            evt.StopPropagation();
        }

        private void TogglePlazaCell(Vector2Int cell)
        {
            if (plazaCellsProperty == null)
                return;

            plazaCellsProperty.serializedObject.Update();

            int existingIndex = -1;
            for (int i = 0; i < plazaCellsProperty.arraySize; i++)
            {
                if (plazaCellsProperty.GetArrayElementAtIndex(i).vector2IntValue == cell)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                plazaCellsProperty.DeleteArrayElementAtIndex(existingIndex);
            }
            else
            {
                int index = plazaCellsProperty.arraySize;
                plazaCellsProperty.InsertArrayElementAtIndex(index);
                plazaCellsProperty.GetArrayElementAtIndex(index).vector2IntValue = cell;
            }

            plazaCellsProperty.serializedObject.ApplyModifiedProperties();
            MarkDirtyRepaint();
            onChanged?.Invoke();
        }

        // Quadrant index follows the same 0-3 convention as CityGeneratorBuildingBuilder.SlotOffsets
        // (0 = min/min, 1 = max/min, 2 = min/max, 3 = max/max), reading the click's position within
        // the cell instead of the block's own world offsets.
        private void SelectSingleCell(Vector2Int cell, float cellLocalX, float cellLocalY, float cellSize)
        {
            if (blockCellProperty == null)
                return;

            blockCellProperty.serializedObject.Update();
            blockCellProperty.vector2IntValue = cell;
            if (positionAssignedProperty != null)
                positionAssignedProperty.boolValue = true;

            bool occupiesFullBlock = occupiesFullBlockGetter != null && occupiesFullBlockGetter();
            if (!occupiesFullBlock && cornerSlotProperty != null)
            {
                float half = cellSize * 0.5f;
                int quadrant = (cellLocalX >= half ? 1 : 0) + (cellLocalY >= half ? 2 : 0);
                cornerSlotProperty.intValue = quadrant;
            }

            blockCellProperty.serializedObject.ApplyModifiedProperties();
            MarkDirtyRepaint();
            onChanged?.Invoke();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
                DrawPlazaMultiToggle(context);
            else
                DrawSingleSelection(context);
        }

        private void DrawPlazaMultiToggle(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width <= 0f || area.height <= 0f)
                return;

            Painter2D painter = context.painter2D;

            float cellSize = Mathf.Min(area.width / gridWidth, area.height / gridHeight);
            float totalWidth = cellSize * gridWidth;
            float totalHeight = cellSize * gridHeight;
            float originX = (area.width - totalWidth) * 0.5f;
            float originY = (area.height - totalHeight) * 0.5f;

            Color streetColor = new Color(0f, 0f, 0f, 0.15f);
            Color blockColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            Color plazaColor = new Color(0.35f, 0.75f, 0.4f, 0.7f);

            DrawRect(painter, streetColor, originX, originY, totalWidth, totalHeight);

            HashSet<Vector2Int> plazaCells = ReadPlazaCells();
            float inset = cellSize * 0.08f;

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    bool isPlaza = plazaCells.Contains(new Vector2Int(x, y));

                    float cx = originX + x * cellSize + inset;
                    float cy = originY + y * cellSize + inset;
                    float size = cellSize - inset * 2f;
                    DrawRect(painter, isPlaza ? plazaColor : blockColor, cx, cy, size, size);
                }
            }
        }

        private void DrawSingleSelection(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width <= 0f || area.height <= 0f)
                return;

            Painter2D painter = context.painter2D;

            float cellSize = Mathf.Min(area.width / gridWidth, area.height / gridHeight);
            float totalWidth = cellSize * gridWidth;
            float totalHeight = cellSize * gridHeight;
            float originX = (area.width - totalWidth) * 0.5f;
            float originY = (area.height - totalHeight) * 0.5f;

            Color streetColor = new Color(0f, 0f, 0f, 0.15f);
            Color blockColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            Color selectedColor = new Color(0.85f, 0.6f, 0.2f, 0.75f);
            Color quadrantColor = new Color(0.95f, 0.75f, 0.3f, 0.9f);

            DrawRect(painter, streetColor, originX, originY, totalWidth, totalHeight);

            bool positionAssigned = positionAssignedProperty != null && positionAssignedProperty.boolValue;
            Vector2Int selectedCell = blockCellProperty != null ? blockCellProperty.vector2IntValue : new Vector2Int(-1, -1);
            bool occupiesFullBlock = occupiesFullBlockGetter != null && occupiesFullBlockGetter();
            int cornerSlot = cornerSlotProperty != null ? cornerSlotProperty.intValue : -1;

            float inset = cellSize * 0.08f;

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    bool isSelected = positionAssigned && selectedCell.x == x && selectedCell.y == y;

                    float cx = originX + x * cellSize + inset;
                    float cy = originY + y * cellSize + inset;
                    float size = cellSize - inset * 2f;
                    DrawRect(painter, isSelected ? selectedColor : blockColor, cx, cy, size, size);

                    if (isSelected && !occupiesFullBlock && cornerSlot >= 0)
                    {
                        float half = size * 0.5f;
                        float qx = cx + ((cornerSlot & 1) != 0 ? half : 0f);
                        float qy = cy + ((cornerSlot & 2) != 0 ? half : 0f);
                        DrawRect(painter, quadrantColor, qx, qy, half, half);
                    }
                }
            }
        }

        private HashSet<Vector2Int> ReadPlazaCells()
        {
            var result = new HashSet<Vector2Int>();
            if (plazaCellsProperty == null)
                return result;

            for (int i = 0; i < plazaCellsProperty.arraySize; i++)
                result.Add(plazaCellsProperty.GetArrayElementAtIndex(i).vector2IntValue);
            return result;
        }

        private static void DrawRect(Painter2D painter, Color color, float x, float y, float width, float height)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
