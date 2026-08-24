using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// Paints a top-down miniature of the configured grid (blocks, streets, plaza blocks) so
    /// Grid Width/Height mean something before generating, and doubles as the editor for which
    /// blocks are plazas: clicking a cell toggles it directly against the bound
    /// <c>general.plazaCells</c> list, matching <see cref="CityGeneratorGrid.BuildBlocks"/>
    /// exactly — there is no separate count to fall out of sync with the picture.
    /// </summary>
    internal class CityGeneratorGridPreview : VisualElement
    {
        private int gridWidth = 5;
        private int gridHeight = 5;
        private SerializedProperty plazaCellsProperty;
        private Action onChanged;

        public CityGeneratorGridPreview()
        {
            AddToClassList("cg-grid-preview");
            tooltip = "Click a block to toggle it as a plaza.";
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        /// <summary>Binds the list this preview edits. Call once; <see cref="SetGrid"/> drives repaints/pruning afterwards.</summary>
        public void Bind(SerializedProperty plazaCellsProperty, Action onChanged)
        {
            this.plazaCellsProperty = plazaCellsProperty;
            this.onChanged = onChanged;
        }

        public void SetGrid(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (gridWidth == width && gridHeight == height)
                return;

            gridWidth = width;
            gridHeight = height;
            PruneOutOfRangeCells();
            MarkDirtyRepaint();
        }

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
            if (plazaCellsProperty == null)
                return;

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

            TogglePlazaCell(new Vector2Int(gx, gy));
            evt.StopPropagation();
        }

        private void TogglePlazaCell(Vector2Int cell)
        {
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

        private void OnGenerateVisualContent(MeshGenerationContext context)
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
