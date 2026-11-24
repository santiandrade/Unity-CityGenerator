using System;
using System.Collections.Generic;
using CityGenerator.Runtime;
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
        /// <summary>Custom Grid's "Define City Area" submode (SPEC 11): the fixed MaxGridSize canvas is painted as real blocks/holes; clicking a valid "+" hole or "-" removable block edits general.customBlockCells directly.</summary>
        CustomAreaEdit,
        /// <summary>A Custom Pedestrian entry's own picker (SPEC 12): draws the real pedestrian graph (Ring/Curb/Crossing/Interior) grouped into clickable line zones instead of blocks or raw nodes; clicking a zone adds/removes all of its underlying nodes from the entry's selectedNodeIndices subgraph.</summary>
        NodeGraphPicker,
        /// <summary>A District entry's own picker (SPEC 21): clicking a cell toggles it in that district's cells list. Plazas, other districts and Custom Places are only painted as a reference (see SetOccupancy) and stay clickable -- an overlap is the validator's to report.</summary>
        DistrictMultiToggle,
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
    ///
    /// Every mode shares two overlays: the occupancy layer (<see cref="SetOccupancy"/>: plazas,
    /// every District in its own colour and every positioned Custom Place as a mark over its slot),
    /// so each board shows what all the others have claimed, and 1-based row/column axis labels
    /// (rows top-down, the same numbers <see cref="CityGeneratorGrid.FormatCell"/> prints in the
    /// validator's messages). The labels are child <see cref="Label"/>s placed in a gutter left of
    /// and above the board, and every mode's drawing and hit-testing is laid out to leave that
    /// gutter free, through <see cref="TryComputeCellLayout"/> / <see cref="TryComputeNodeGraphLayout"/>.
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

        // CustomAreaEdit mode (data source), and shared as the shape-mask overlay applied on top
        // of PlazaMultiToggle/SingleSelectQuadrant by SetShapeMask.
        private SerializedProperty shapeCellsProperty;

        // NodeGraphPicker mode.
        private CityGeneratorPedestrianPreview pedestrianPreview;
        private SerializedProperty selectedNodeIndicesProperty;
        private SerializedProperty graphFingerprintProperty;
        private List<GraphZone> zoneCache;
        private CityGeneratorPedestrianPreview zoneCacheSource;

        // DistrictMultiToggle mode.
        private SerializedProperty districtCellsProperty;

        // Occupancy overlay, shared by every mode.
        private SerializedProperty occupancySource;
        private CityGeneratorGridOccupancyOwner occupancyOwner = CityGeneratorGridOccupancyOwner.None;
        private int occupancyOwnerIndex = -1;

        // Axis labels: pooled child Labels, repositioned only when their layout key changes.
        private readonly List<Label> rowLabels = new();
        private readonly List<Label> columnLabels = new();
        private AxisLabelKey lastAxisLabelKey;
        private bool hasAxisLabelKey;

        // Shown when the pointer is over no cell (gutter, street). Deliberately NOT the element's own
        // `tooltip` property: VisualElement answers a TooltipEvent with that property itself and it
        // overrides whatever OnTooltip wrote, even after StopImmediatePropagation, so a non-empty
        // `tooltip` hides every per-cell tooltip.
        private string instructionTooltip;

        // Last pointer position over this board, for the per-cell tooltip (TooltipEvent carries none).
        private Vector2 lastPointerLocal;
        private bool hasPointer;

        // Gutter reserved left of (row numbers) and above (column numbers) the board for the axis labels.
        private const float RowLabelGutter = 16f;
        private const float ColumnLabelGutter = 13f;

        private static readonly Color StreetColor = new(0f, 0f, 0f, 0.15f);
        private static readonly Color BlockColor = new(0.5f, 0.5f, 0.5f, 0.35f);
        private static readonly Color HoleColor = new(0.5f, 0.5f, 0.5f, 0.08f);
        private static readonly Color PlazaColor = new(0.35f, 0.75f, 0.4f, 0.7f);
        private static readonly Color IconColor = new(1f, 1f, 1f, 0.9f);
        private static readonly Color OwnDistrictOutlineColor = new(1f, 1f, 1f, 0.9f);
        private static readonly Color OwnCustomPlaceFill = new(0.95f, 0.6f, 0.15f, 0.95f);
        private static readonly Color OwnCustomPlaceOutline = new(1f, 1f, 1f, 0.95f);
        private static readonly Color OtherCustomPlaceFill = new(1f, 1f, 1f, 0.45f);
        private static readonly Color OtherCustomPlaceOutline = new(0.1f, 0.1f, 0.1f, 0.8f);
        private const float OwnDistrictAlpha = 0.85f;
        private const float OtherDistrictAlpha = 0.5f;

        // One colour per District, by its index in the list (cycled past the end). Deliberately
        // avoids green (plazas) and orange (the Custom Place being edited).
        private static readonly Color[] DistrictPalette =
        {
            new(0.30f, 0.55f, 0.95f), // blue
            new(0.70f, 0.40f, 0.90f), // purple
            new(0.90f, 0.35f, 0.60f), // magenta
            new(0.20f, 0.75f, 0.85f), // cyan
            new(0.85f, 0.25f, 0.25f), // red
            new(0.60f, 0.45f, 0.30f), // brown
            new(0.45f, 0.55f, 0.70f), // slate
            new(0.15f, 0.50f, 0.55f), // dark teal
        };

        /// <summary>Caption sentence every board's hint ends with, explaining the axis labels.</summary>
        public const string AxisHint = "Rows are numbered top-down and columns left to right; validation messages name a block as (row, column). Hover a block to see what is on it.";

        /// <summary>The colour every board paints District <paramref name="districtIndex"/> with, so a district row can show a matching swatch.</summary>
        public static Color DistrictColor(int districtIndex, float alpha = 1f)
        {
            Color color = DistrictPalette[Mathf.Abs(districtIndex) % DistrictPalette.Length];
            color.a = alpha;
            return color;
        }

        public CityGeneratorGridPreview()
        {
            AddToClassList("cg-grid-preview");
            instructionTooltip = "Click a block to toggle it as a plaza.";
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<GeometryChangedEvent>(_ => UpdateAxisLabels());
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                lastPointerLocal = evt.localPosition;
                hasPointer = true;
            });
            RegisterCallback<PointerLeaveEvent>(_ => hasPointer = false);
            RegisterCallback<TooltipEvent>(OnTooltip);
        }

        /// <summary>Binds this preview as the plaza editor. Call once; <see cref="SetGrid"/> drives repaints/pruning afterwards.</summary>
        public void Bind(SerializedProperty plazaCellsProperty, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.PlazaMultiToggle;
            this.plazaCellsProperty = plazaCellsProperty;
            this.onChanged = onChanged;
            instructionTooltip = "Click a block to toggle it as a plaza.";
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
            instructionTooltip = "Click a block to place this entry there; click a corner of the block for a quadrant slot.";
        }

        /// <summary>
        /// Binds this preview as Custom Grid's "Define City Area" submode: the fixed MaxGridSize
        /// canvas is painted as real blocks/holes; clicking a "+" hole adds it and clicking a "-"
        /// removable block removes it, both written directly to <paramref name="customBlockCellsProperty"/>.
        /// </summary>
        public void BindCustomArea(SerializedProperty customBlockCellsProperty, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.CustomAreaEdit;
            shapeCellsProperty = customBlockCellsProperty;
            this.onChanged = onChanged;
            instructionTooltip = "Click a \"+\" to add a block, or a \"-\" to remove one.";
        }

        /// <summary>
        /// Binds this preview as a Custom Pedestrian entry's node-graph picker (SPEC 12): draws the
        /// real graph grouped into clickable line zones (a Ring edge, an Interior spoke, a crossing
        /// -- see <see cref="BuildZones"/>) instead of one point per node, since a normal block's 13+
        /// individual nodes were too small/dense to click reliably. <paramref name="preview"/>
        /// supplies the real node positions/adjacency the zones are derived from and hit-test
        /// against; clicking a zone adds/removes all of its underlying nodes in
        /// <paramref name="selectedNodeIndicesProperty"/> (only a zone sharing a node with the
        /// current selection can be added, except the first), and every edit updates
        /// <paramref name="graphFingerprintProperty"/> to <paramref name="preview"/>'s current
        /// fingerprint. If the entry already holds a selection whose stored fingerprint no longer
        /// matches <paramref name="preview"/>'s (the grid/plaza/Custom Places settings changed since
        /// it was traced), the stale selection is cleared right away instead of silently pointing at
        /// the wrong nodes.
        /// </summary>
        public void BindNodeGraph(CityGeneratorPedestrianPreview preview, SerializedProperty selectedNodeIndicesProperty, SerializedProperty graphFingerprintProperty, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.NodeGraphPicker;
            pedestrianPreview = preview;
            this.selectedNodeIndicesProperty = selectedNodeIndicesProperty;
            this.graphFingerprintProperty = graphFingerprintProperty;
            this.onChanged = onChanged;
            instructionTooltip = "Click a line to add/remove the zone it represents from this entry's route. Only a zone sharing a node with the current selection can be added, except the first.";

            if (preview != null && selectedNodeIndicesProperty != null && graphFingerprintProperty != null)
            {
                selectedNodeIndicesProperty.serializedObject.Update();
                int currentFingerprint = preview.Fingerprint();
                if (selectedNodeIndicesProperty.arraySize > 0 && graphFingerprintProperty.intValue != currentFingerprint)
                {
                    selectedNodeIndicesProperty.ClearArray();
                    graphFingerprintProperty.intValue = currentFingerprint;
                    selectedNodeIndicesProperty.serializedObject.ApplyModifiedProperties();
                    onChanged?.Invoke();
                }
            }

            UpdateAxisLabels();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Binds this preview as one District entry's block picker (SPEC 21): clicking a cell
        /// adds it to, or removes it from, <paramref name="districtCellsProperty"/>. Never prunes
        /// that list when the grid shrinks -- a block that stops existing is reported by the
        /// validator instead, so the user never loses data without knowing.
        /// </summary>
        public void BindDistrictCells(SerializedProperty districtCellsProperty, Action onChanged)
        {
            mode = CityGeneratorGridPreviewMode.DistrictMultiToggle;
            this.districtCellsProperty = districtCellsProperty;
            this.onChanged = onChanged;
            instructionTooltip = "Click a block to add it to, or remove it from, this district.";
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Overlays everything that claims a block -- plazas (green), every District (its own
        /// colour, <see cref="DistrictColor"/>) and every positioned Custom Place (a mark over its
        /// slot) -- read from <paramref name="source"/> at paint time, so it never goes stale between
        /// repaints. <paramref name="settings"/> is the serialized <c>CityGeneratorSettings</c>.
        /// <paramref name="owner"/>/<paramref name="ownerIndex"/> name the layer this board
        /// itself edits, which is highlighted instead of painted as a reference. Reference layers
        /// never block a click: an overlap is the validator's to report. Pass a <c>null</c>
        /// <paramref name="settings"/> to disable.
        /// </summary>
        public void SetOccupancy(SerializedProperty settings, CityGeneratorGridOccupancyOwner owner = CityGeneratorGridOccupancyOwner.None, int ownerIndex = -1)
        {
            occupancySource = settings;
            occupancyOwner = owner;
            occupancyOwnerIndex = ownerIndex;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Overlays a shape mask on top of an already-bound PlazaMultiToggle/SingleSelectQuadrant
        /// preview: any cell outside <paramref name="customBlockCellsProperty"/> is painted
        /// semi-transparent and ignores clicks. Pass <c>null</c> to disable (plain rectangular
        /// behaviour). Does not change <see cref="mode"/>.
        /// </summary>
        public void SetShapeMask(SerializedProperty customBlockCellsProperty)
        {
            shapeCellsProperty = customBlockCellsProperty;
            MarkDirtyRepaint();
        }

        public void SetGrid(int width, int height)
        {
            if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit || shapeCellsProperty != null)
            {
                width = CityGeneratorConstants.MaxGridSize;
                height = CityGeneratorConstants.MaxGridSize;
            }
            else
            {
                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);
            }

            if (gridWidth == width && gridHeight == height)
                return;

            gridWidth = width;
            gridHeight = height;
            if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
                PruneOutOfRangeCells();
            UpdateAxisLabels();
            MarkDirtyRepaint();
        }

        /// <summary>Forces a repaint after an external change to the bound property or to any occupancy layer (e.g. occupiesFullBlock toggled, or another board edited).</summary>
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

            if (mode == CityGeneratorGridPreviewMode.NodeGraphPicker)
            {
                OnZonePointerDown(evt, area);
                evt.StopPropagation();
                return;
            }

            if (!TryComputeCellLayout(area, out CellLayout layout))
                return;

            float cellSize = layout.CellSize;
            float originX = layout.OriginX;
            float originY = layout.OriginY;

            Vector2 local = evt.localPosition;
            int gx = Mathf.FloorToInt((local.x - originX) / cellSize);
            int row = Mathf.FloorToInt((local.y - originY) / cellSize);
            if (gx < 0 || gx >= gridWidth || row < 0 || row >= gridHeight)
                return;

            // The picker's top row must match +Z (the row Unity's own top-down Scene/Game view
            // draws at the top of the screen), while pointer-local Y still grows downward like
            // every other screen coordinate. Without this flip, the block a Custom Place/plaza
            // cell is clicked on here would land one Z row away from where it visually sits once
            // generated and viewed from above.
            int gy = gridHeight - 1 - row;
            var cell = new Vector2Int(gx, gy);

            if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit)
            {
                EditCustomAreaCell(cell);
            }
            else if (shapeCellsProperty != null && !ReadShapeCells().Contains(cell))
            {
                // Outside the shape mask: a hole is not clickable in PlazaMultiToggle/SingleSelectQuadrant.
            }
            else if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
            {
                TogglePlazaCell(cell);
            }
            else if (mode == CityGeneratorGridPreviewMode.DistrictMultiToggle)
            {
                ToggleDistrictCell(cell);
            }
            else
            {
                float cellLocalX = local.x - (originX + gx * cellSize);
                float cellLocalY = local.y - (originY + row * cellSize);
                SelectSingleCell(cell, cellLocalX, cellLocalY, cellSize);
            }

            evt.StopPropagation();
        }

        private void EditCustomAreaCell(Vector2Int cell)
        {
            if (shapeCellsProperty == null)
                return;

            shapeCellsProperty.serializedObject.Update();
            var existing = ReadShapeCells();

            if (existing.Contains(cell))
            {
                if (!CityGeneratorGrid.CanRemoveWithoutSplitting(existing, cell))
                    return;

                for (int i = 0; i < shapeCellsProperty.arraySize; i++)
                {
                    if (shapeCellsProperty.GetArrayElementAtIndex(i).vector2IntValue == cell)
                    {
                        shapeCellsProperty.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }
            else
            {
                if (!CityGeneratorGrid.IsValidAddition(existing, cell))
                    return;

                int index = shapeCellsProperty.arraySize;
                shapeCellsProperty.InsertArrayElementAtIndex(index);
                shapeCellsProperty.GetArrayElementAtIndex(index).vector2IntValue = cell;
            }

            shapeCellsProperty.serializedObject.ApplyModifiedProperties();
            MarkDirtyRepaint();
            onChanged?.Invoke();
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

        private void ToggleDistrictCell(Vector2Int cell)
        {
            if (districtCellsProperty == null)
                return;

            districtCellsProperty.serializedObject.Update();

            int existingIndex = -1;
            for (int i = 0; i < districtCellsProperty.arraySize; i++)
            {
                if (districtCellsProperty.GetArrayElementAtIndex(i).vector2IntValue == cell)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                districtCellsProperty.DeleteArrayElementAtIndex(existingIndex);
            }
            else
            {
                int index = districtCellsProperty.arraySize;
                districtCellsProperty.InsertArrayElementAtIndex(index);
                districtCellsProperty.GetArrayElementAtIndex(index).vector2IntValue = cell;
            }

            districtCellsProperty.serializedObject.ApplyModifiedProperties();
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
                // cellLocalY still grows downward (screen space); flipped so the top half of the
                // cell (smaller Y) maps to the +Z slots (2/3), matching the row flip above.
                int quadrant = (cellLocalX >= half ? 1 : 0) + (cellLocalY < half ? 2 : 0);
                cornerSlotProperty.intValue = quadrant;
            }

            blockCellProperty.serializedObject.ApplyModifiedProperties();
            MarkDirtyRepaint();
            onChanged?.Invoke();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (mode == CityGeneratorGridPreviewMode.NodeGraphPicker)
                DrawZoneGraph(context);
            else
                DrawCellBoard(context);
        }

        // Cell-board geometry (every mode except NodeGraphPicker): shared by drawing, hit-testing
        // and the axis labels, so the three can never disagree about where a cell is.
        private readonly struct CellLayout
        {
            public readonly float CellSize;
            public readonly float OriginX;
            public readonly float OriginY;
            public readonly float TotalWidth;
            public readonly float TotalHeight;

            public CellLayout(float cellSize, float originX, float originY, float totalWidth, float totalHeight)
            {
                CellSize = cellSize;
                OriginX = originX;
                OriginY = originY;
                TotalWidth = totalWidth;
                TotalHeight = totalHeight;
            }
        }

        private bool TryComputeCellLayout(Rect area, out CellLayout layout)
        {
            layout = default;
            float availableWidth = area.width - RowLabelGutter;
            float availableHeight = area.height - ColumnLabelGutter;
            if (availableWidth <= 0f || availableHeight <= 0f)
                return false;

            float cellSize = Mathf.Min(availableWidth / gridWidth, availableHeight / gridHeight);
            float totalWidth = cellSize * gridWidth;
            float totalHeight = cellSize * gridHeight;
            float originX = RowLabelGutter + (availableWidth - totalWidth) * 0.5f;
            float originY = ColumnLabelGutter + (availableHeight - totalHeight) * 0.5f;

            layout = new CellLayout(cellSize, originX, originY, totalWidth, totalHeight);
            return true;
        }

        // NodeGraphPicker geometry: world XZ -> screen mapping shared by drawing and hit-testing,
        // fit to the node and block bounds (there is no fixed block grid to size against in this mode).
        private readonly struct NodeGraphLayout
        {
            public readonly float minX;
            public readonly float maxZ;
            public readonly float scale;
            public readonly float originX;
            public readonly float originY;

            public NodeGraphLayout(float minX, float maxZ, float scale, float originX, float originY)
            {
                this.minX = minX;
                this.maxZ = maxZ;
                this.scale = scale;
                this.originX = originX;
                this.originY = originY;
            }
        }

        private bool TryComputeNodeGraphLayout(Rect area, out NodeGraphLayout layout)
        {
            layout = default;
            if (pedestrianPreview == null)
                return false;

            int count = pedestrianPreview.NodeCount;
            if (count == 0)
                return false;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = pedestrianPreview.GetNode(i).Position;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }

            // The blocks painted under the graph reach past its outermost Ring nodes (inset
            // PedestrianRingInset into each block), so they widen the fitted bounds too.
            IReadOnlyList<BlockCell> blocks = pedestrianPreview.Blocks;
            if (blocks != null)
            {
                float half = CityGeneratorConstants.BlockSize * 0.5f;
                for (int i = 0; i < blocks.Count; i++)
                {
                    Vector3 c = blocks[i].center;
                    minX = Mathf.Min(minX, c.x - half);
                    maxX = Mathf.Max(maxX, c.x + half);
                    minZ = Mathf.Min(minZ, c.z - half);
                    maxZ = Mathf.Max(maxZ, c.z + half);
                }
            }

            float availableWidth = area.width - RowLabelGutter;
            float availableHeight = area.height - ColumnLabelGutter;
            if (availableWidth <= 0f || availableHeight <= 0f)
                return false;

            float spanX = Mathf.Max(0.01f, maxX - minX);
            float spanZ = Mathf.Max(0.01f, maxZ - minZ);
            const float padding = 0.96f;
            float scale = Mathf.Min(availableWidth / spanX, availableHeight / spanZ) * padding;
            float originX = RowLabelGutter + (availableWidth - spanX * scale) * 0.5f;
            float originY = ColumnLabelGutter + (availableHeight - spanZ * scale) * 0.5f;

            layout = new NodeGraphLayout(minX, maxZ, scale, originX, originY);
            return true;
        }

        // Same +Z-is-up flip every other mode's row math applies.
        private static Vector2 NodeGraphScreenPoint(NodeGraphLayout layout, Vector3 position)
        {
            float x = layout.originX + (position.x - layout.minX) * layout.scale;
            float y = layout.originY + (layout.maxZ - position.z) * layout.scale;
            return new Vector2(x, y);
        }

        /// <summary>
        /// One clickable line in the NodeGraphPicker: an ordered chain of real node indices whose
        /// endpoints are the only points where it can touch another zone (see <see cref="BuildZones"/>).
        /// A zone counts as selected when every node in <see cref="NodeChain"/> is in the entry's
        /// selectedNodeIndices.
        /// </summary>
        private readonly struct GraphZone
        {
            public readonly int[] NodeChain;
            public readonly PedestrianNodeKind Kind;

            public GraphZone(int[] nodeChain, PedestrianNodeKind kind)
            {
                NodeChain = nodeChain;
                Kind = kind;
            }
        }

        /// <summary>
        /// Groups the preview's real graph into clickable zones, purely from each node's
        /// <see cref="PedestrianNodeKind"/> and degree (never from block bookkeeping, which this
        /// picker has no access to) so the same logic covers both grid modes:
        /// <list type="bullet">
        /// <item>Ring edge: any single real edge between two Ring nodes (2 nodes).</item>
        /// <item>Interior spoke: a spoke-centre node (Interior, 4 Interior neighbours) paired with
        /// one arm and that arm's non-centre neighbour (3 nodes; 4 spokes per centre).</item>
        /// <item>Crossing: a Crossing node (2 Curb neighbours) expanded one more hop each side to
        /// the two Ring nodes it connects (5 nodes; one per signalled crossing arm).</item>
        /// </list>
        /// </summary>
        private static List<GraphZone> BuildZones(CityGeneratorPedestrianPreview preview)
        {
            var zones = new List<GraphZone>();
            int count = preview.NodeCount;
            var visitedRingEdges = new HashSet<long>();

            for (int i = 0; i < count; i++)
            {
                PedestrianNode node = preview.GetNode(i);

                if (node.Kind == PedestrianNodeKind.Ring)
                {
                    for (int n = 0; n < node.Neighbours.Count; n++)
                    {
                        int neighbour = node.Neighbours[n];
                        if (preview.GetNode(neighbour).Kind != PedestrianNodeKind.Ring)
                            continue;

                        long key = RingEdgeKey(i, neighbour);
                        if (!visitedRingEdges.Add(key))
                            continue;

                        zones.Add(new GraphZone(new[] { i, neighbour }, PedestrianNodeKind.Ring));
                    }
                }
                else if (node.Kind == PedestrianNodeKind.Interior && node.Neighbours.Count == 4 && AllNeighboursAreKind(preview, node, PedestrianNodeKind.Interior))
                {
                    for (int n = 0; n < node.Neighbours.Count; n++)
                    {
                        int arm = node.Neighbours[n];
                        int far = OtherNeighbour(preview, arm, i);
                        if (far < 0)
                            continue;

                        zones.Add(new GraphZone(new[] { i, arm, far }, PedestrianNodeKind.Interior));
                    }
                }
                else if (node.Kind == PedestrianNodeKind.Crossing && node.Neighbours.Count == 2)
                {
                    int curbA = node.Neighbours[0];
                    int curbB = node.Neighbours[1];
                    int ringA = OtherNeighbour(preview, curbA, i);
                    int ringB = OtherNeighbour(preview, curbB, i);
                    if (ringA >= 0 && ringB >= 0)
                        zones.Add(new GraphZone(new[] { ringA, curbA, i, curbB, ringB }, PedestrianNodeKind.Crossing));
                }
            }

            return zones;
        }

        private static long RingEdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private static bool AllNeighboursAreKind(CityGeneratorPedestrianPreview preview, PedestrianNode node, PedestrianNodeKind kind)
        {
            for (int i = 0; i < node.Neighbours.Count; i++)
            {
                if (preview.GetNode(node.Neighbours[i]).Kind != kind)
                    return false;
            }

            return true;
        }

        private static int OtherNeighbour(CityGeneratorPedestrianPreview preview, int nodeIndex, int exclude)
        {
            List<int> neighbours = preview.GetNode(nodeIndex).Neighbours;
            for (int i = 0; i < neighbours.Count; i++)
            {
                if (neighbours[i] != exclude)
                    return neighbours[i];
            }

            return -1;
        }

        private List<GraphZone> GetZones()
        {
            if (pedestrianPreview == null)
                return null;

            if (zoneCache == null || !ReferenceEquals(zoneCacheSource, pedestrianPreview))
            {
                zoneCache = BuildZones(pedestrianPreview);
                zoneCacheSource = pedestrianPreview;
            }

            return zoneCache;
        }

        private const float ZoneHitDistance = 9f;

        private void OnZonePointerDown(PointerDownEvent evt, Rect area)
        {
            if (pedestrianPreview == null || !TryComputeNodeGraphLayout(area, out NodeGraphLayout layout))
                return;

            List<GraphZone> zones = GetZones();
            if (zones == null)
                return;

            int hitZone = FindZoneAt(zones, layout, evt.localPosition);
            if (hitZone >= 0)
                ToggleZone(hitZone);
        }

        // Closest zone within ZoneHitDistance of a board-local point, or -1. Shared by clicking and the hover tooltip.
        private int FindZoneAt(List<GraphZone> zones, NodeGraphLayout layout, Vector2 local)
        {
            int hitZone = -1;
            float bestDistance = ZoneHitDistance;
            for (int z = 0; z < zones.Count; z++)
            {
                int[] chain = zones[z].NodeChain;
                for (int s = 0; s < chain.Length - 1; s++)
                {
                    Vector2 a = NodeGraphScreenPoint(layout, pedestrianPreview.GetNode(chain[s]).Position);
                    Vector2 b = NodeGraphScreenPoint(layout, pedestrianPreview.GetNode(chain[s + 1]).Position);
                    float distance = DistancePointToSegment(local, a, b);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        hitZone = z;
                    }
                }
            }

            return hitZone;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 0.0001f)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            Vector2 projection = a + ab * t;
            return Vector2.Distance(point, projection);
        }

        private static bool IsZoneSelected(GraphZone zone, HashSet<int> selectedSet)
        {
            for (int i = 0; i < zone.NodeChain.Length; i++)
            {
                if (!selectedSet.Contains(zone.NodeChain[i]))
                    return false;
            }

            return true;
        }

        private static bool CanAddZone(GraphZone zone, HashSet<int> selectedSet)
        {
            if (selectedSet.Count == 0)
                return true;

            for (int i = 0; i < zone.NodeChain.Length; i++)
            {
                if (selectedSet.Contains(zone.NodeChain[i]))
                    return true;
            }

            return false;
        }

        private void ToggleZone(int zoneIndex)
        {
            if (selectedNodeIndicesProperty == null)
                return;

            List<GraphZone> zones = GetZones();
            if (zones == null || zoneIndex < 0 || zoneIndex >= zones.Count)
                return;

            selectedNodeIndicesProperty.serializedObject.Update();
            List<int> selected = ReadSelectedNodeIndices();
            var selectedSet = new HashSet<int>(selected);
            GraphZone zone = zones[zoneIndex];

            if (IsZoneSelected(zone, selectedSet))
            {
                // A node stays if some OTHER still-fully-selected zone also needs it, so removing
                // this zone never visually breaks a neighbouring zone the user didn't touch.
                var protectedNodes = new HashSet<int>();
                for (int z = 0; z < zones.Count; z++)
                {
                    if (z == zoneIndex || !IsZoneSelected(zones[z], selectedSet))
                        continue;

                    int[] otherChain = zones[z].NodeChain;
                    for (int i = 0; i < otherChain.Length; i++)
                        protectedNodes.Add(otherChain[i]);
                }

                for (int i = 0; i < zone.NodeChain.Length; i++)
                {
                    int nodeIndex = zone.NodeChain[i];
                    if (!protectedNodes.Contains(nodeIndex))
                        selected.Remove(nodeIndex);
                }

                selected = KeepLargestConnectedComponent(selected);
            }
            else
            {
                if (!CanAddZone(zone, selectedSet))
                    return;

                for (int i = 0; i < zone.NodeChain.Length; i++)
                {
                    int nodeIndex = zone.NodeChain[i];
                    if (!selected.Contains(nodeIndex))
                        selected.Add(nodeIndex);
                }
            }

            WriteSelectedNodeIndices(selected);
            if (graphFingerprintProperty != null && pedestrianPreview != null)
                graphFingerprintProperty.intValue = pedestrianPreview.Fingerprint();

            selectedNodeIndicesProperty.serializedObject.ApplyModifiedProperties();
            MarkDirtyRepaint();
            onChanged?.Invoke();
        }

        private List<int> ReadSelectedNodeIndices()
        {
            var result = new List<int>(selectedNodeIndicesProperty.arraySize);
            for (int i = 0; i < selectedNodeIndicesProperty.arraySize; i++)
                result.Add(selectedNodeIndicesProperty.GetArrayElementAtIndex(i).intValue);
            return result;
        }

        private void WriteSelectedNodeIndices(List<int> values)
        {
            selectedNodeIndicesProperty.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                selectedNodeIndicesProperty.GetArrayElementAtIndex(i).intValue = values[i];
        }

        /// <summary>
        /// Flood fill restricted to <paramref name="candidateNodes"/> (following the real graph's
        /// edges), keeping only the largest resulting component -- SPEC 12: removing a bridge node
        /// from the selection must never leave it split across several disconnected pieces.
        /// </summary>
        private List<int> KeepLargestConnectedComponent(List<int> candidateNodes)
        {
            if (candidateNodes.Count <= 1)
                return candidateNodes;

            var candidateSet = new HashSet<int>(candidateNodes);
            var visited = new HashSet<int>();
            List<int> best = new();

            foreach (int start in candidateNodes)
            {
                if (visited.Contains(start))
                    continue;

                var component = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start);
                visited.Add(start);

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    component.Add(current);
                    List<int> neighbours = pedestrianPreview.GetNode(current).Neighbours;
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        int next = neighbours[i];
                        if (candidateSet.Contains(next) && !visited.Contains(next))
                        {
                            visited.Add(next);
                            stack.Push(next);
                        }
                    }
                }

                if (component.Count > best.Count)
                    best = component;
            }

            return best;
        }

        private void DrawZoneGraph(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width <= 0f || area.height <= 0f || pedestrianPreview == null || !TryComputeNodeGraphLayout(area, out NodeGraphLayout layout))
                return;

            List<GraphZone> zones = GetZones();
            if (zones == null)
                return;

            Painter2D painter = context.painter2D;
            DrawGraphBlocks(painter, layout);

            List<int> selected = selectedNodeIndicesProperty != null ? ReadSelectedNodeIndices() : new List<int>();
            var selectedSet = new HashSet<int>(selected);

            // Unselected zones first so the highlighted ones draw on top.
            for (int z = 0; z < zones.Count; z++)
            {
                if (!IsZoneSelected(zones[z], selectedSet))
                    DrawZonePolyline(painter, layout, zones[z], ZoneKindColor(zones[z].Kind), 2f);
            }

            Color selectedColor = new(1f, 0.9f, 0.15f, 0.95f);
            for (int z = 0; z < zones.Count; z++)
            {
                if (IsZoneSelected(zones[z], selectedSet))
                    DrawZonePolyline(painter, layout, zones[z], selectedColor, 4f);
            }
        }

        // Graph-picker blocks are painted fainter than on the cell boards, so the zone lines drawn
        // over them (green/blue/orange) stay the most visible thing on the board.
        private const float GraphBlockAlphaScale = 0.5f;

        /// <summary>
        /// Paints the preview's real blocks under the graph with the same occupancy layers as the
        /// cell boards (plaza, District colour, Custom Place marks), at reduced alpha.
        /// </summary>
        private void DrawGraphBlocks(Painter2D painter, NodeGraphLayout layout)
        {
            IReadOnlyList<BlockCell> blocks = pedestrianPreview.Blocks;
            if (blocks == null)
                return;

            CityGeneratorGridOccupancy occupancy = occupancySource != null ? ReadOccupancy() : null;
            float half = CityGeneratorConstants.BlockSize * 0.5f;
            float size = CityGeneratorConstants.BlockSize * layout.scale;
            var blockOrigins = new Dictionary<Vector2Int, Vector2>(blocks.Count);

            for (int i = 0; i < blocks.Count; i++)
            {
                BlockCell block = blocks[i];
                var cell = new Vector2Int(block.gridX, block.gridY);
                Vector2 topLeft = NodeGraphScreenPoint(layout, block.center + new Vector3(-half, 0f, half));
                blockOrigins[cell] = topLeft;

                bool isPlaza = occupancy != null ? occupancy.PlazaCells.Contains(cell) : block.isPlaza;
                Color fill = BlockColor;
                if (isPlaza)
                    fill = PlazaColor;
                else if (occupancy != null && occupancy.TryGetDistrict(cell, -1, out int districtIndex))
                    fill = DistrictColor(districtIndex, OtherDistrictAlpha);

                fill.a *= GraphBlockAlphaScale;
                DrawRect(painter, fill, topLeft.x, topLeft.y, size, size);
            }

            if (occupancy == null)
                return;

            foreach (CityGeneratorCustomPlaceMark mark in occupancy.CustomPlaces)
            {
                if (blockOrigins.TryGetValue(mark.Cell, out Vector2 topLeft))
                    DrawCustomPlaceMark(painter, topLeft.x, topLeft.y, size, mark.OccupiesFullBlock, mark.CornerSlot, own: false, alphaScale: GraphBlockAlphaScale);
            }
        }

        private void DrawZonePolyline(Painter2D painter, NodeGraphLayout layout, GraphZone zone, Color color, float lineWidth)
        {
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            for (int i = 0; i < zone.NodeChain.Length; i++)
            {
                Vector2 screen = NodeGraphScreenPoint(layout, pedestrianPreview.GetNode(zone.NodeChain[i]).Position);
                if (i == 0)
                    painter.MoveTo(screen);
                else
                    painter.LineTo(screen);
            }
            painter.Stroke();
        }

        private static Color ZoneKindColor(PedestrianNodeKind kind) => kind switch
        {
            PedestrianNodeKind.Ring => new Color(0.2f, 0.75f, 0.35f, 0.6f),
            PedestrianNodeKind.Interior => new Color(0.3f, 0.55f, 0.9f, 0.6f),
            PedestrianNodeKind.Crossing => new Color(0.95f, 0.5f, 0.15f, 0.6f),
            _ => new Color(1f, 1f, 1f, 0.5f)
        };

        /// <summary>
        /// Every cell-based mode's drawing (CustomAreaEdit, PlazaMultiToggle, DistrictMultiToggle,
        /// SingleSelectQuadrant), in three passes: each cell's fill (own layer first, then plaza,
        /// then another District, else a plain block -- or a hole outside the Custom Grid shape),
        /// then every Custom Place mark on top, then the "+"/"-" icons of Define City Area.
        /// </summary>
        private void DrawCellBoard(MeshGenerationContext context)
        {
            Rect area = contentRect;
            if (area.width <= 0f || area.height <= 0f || !TryComputeCellLayout(area, out CellLayout layout))
                return;

            Painter2D painter = context.painter2D;
            DrawRect(painter, StreetColor, layout.OriginX, layout.OriginY, layout.TotalWidth, layout.TotalHeight);

            CityGeneratorGridOccupancy occupancy = occupancySource != null ? ReadOccupancy() : null;
            HashSet<Vector2Int> shapeCells = shapeCellsProperty != null ? ReadShapeCells() : null;
            // The plaza editor always paints its own bound list, so it stays right even without an occupancy source.
            HashSet<Vector2Int> plazaCells = mode == CityGeneratorGridPreviewMode.PlazaMultiToggle || occupancy == null
                ? ReadPlazaCells()
                : occupancy.PlazaCells;
            HashSet<Vector2Int> ownDistrictCells = mode == CityGeneratorGridPreviewMode.DistrictMultiToggle ? ReadDistrictCells() : null;
            int excludedDistrict = occupancyOwner == CityGeneratorGridOccupancyOwner.District ? occupancyOwnerIndex : -1;
            float inset = layout.CellSize * 0.08f;
            float size = layout.CellSize - inset * 2f;

            // Screen row y=0 is the top of the picture; it must show the block that renders at
            // the top of Unity's own top-down view, which is the highest gy (largest +Z), so the
            // row read from the data is flipped relative to the screen row being painted.
            for (int y = 0; y < gridHeight; y++)
            {
                int gy = gridHeight - 1 - y;
                for (int x = 0; x < gridWidth; x++)
                {
                    var cell = new Vector2Int(x, gy);
                    float cx = layout.OriginX + x * layout.CellSize + inset;
                    float cy = layout.OriginY + y * layout.CellSize + inset;

                    if (shapeCells != null && !shapeCells.Contains(cell))
                    {
                        DrawRect(painter, HoleColor, cx, cy, size, size);
                        if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit && CityGeneratorGrid.IsValidAddition(shapeCells, cell))
                            DrawPlusIcon(painter, IconColor, cx, cy, size);
                        continue;
                    }

                    bool isOwnDistrict = ownDistrictCells != null && ownDistrictCells.Contains(cell);
                    Color fill = BlockColor;
                    if (isOwnDistrict)
                        fill = DistrictColor(occupancyOwnerIndex, OwnDistrictAlpha);
                    else if (plazaCells.Contains(cell))
                        fill = PlazaColor;
                    else if (occupancy != null && occupancy.TryGetDistrict(cell, excludedDistrict, out int districtIndex))
                        fill = DistrictColor(districtIndex, OtherDistrictAlpha);

                    DrawRect(painter, fill, cx, cy, size, size);
                    if (isOwnDistrict)
                        StrokeRect(painter, OwnDistrictOutlineColor, 1.5f, cx, cy, size, size);
                }
            }

            if (occupancy != null)
            {
                int ownCustomPlace = mode == CityGeneratorGridPreviewMode.SingleSelectQuadrant ? occupancyOwnerIndex : -1;
                foreach (CityGeneratorCustomPlaceMark mark in occupancy.CustomPlaces)
                {
                    if (mark.Index == ownCustomPlace)
                        continue;
                    if (TryGetCellScreenRect(layout, mark.Cell, shapeCells, inset, out float cx, out float cy))
                        DrawCustomPlaceMark(painter, cx, cy, size, mark.OccupiesFullBlock, mark.CornerSlot, own: false);
                }
            }

            // The entry being edited is drawn from its own bound properties, last, so it is on top
            // and reflects the click immediately even without an occupancy source.
            if (mode == CityGeneratorGridPreviewMode.SingleSelectQuadrant
                && positionAssignedProperty != null && positionAssignedProperty.boolValue
                && blockCellProperty != null
                && TryGetCellScreenRect(layout, blockCellProperty.vector2IntValue, shapeCells, inset, out float ownX, out float ownY))
            {
                bool occupiesFullBlock = occupiesFullBlockGetter != null && occupiesFullBlockGetter();
                int cornerSlot = cornerSlotProperty != null ? cornerSlotProperty.intValue : 0;
                DrawCustomPlaceMark(painter, ownX, ownY, size, occupiesFullBlock, cornerSlot, own: true);
            }

            if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit && shapeCells != null)
            {
                foreach (Vector2Int cell in shapeCells)
                {
                    if (CityGeneratorGrid.CanRemoveWithoutSplitting(shapeCells, cell)
                        && TryGetCellScreenRect(layout, cell, shapeCells, inset, out float cx, out float cy))
                        DrawMinusIcon(painter, IconColor, cx, cy, size);
                }
            }
        }

        // Top-left corner (after the per-cell inset) of a real, in-range cell; false for a cell
        // outside the board or, with a shape mask, outside the shape.
        private bool TryGetCellScreenRect(CellLayout layout, Vector2Int cell, HashSet<Vector2Int> shapeCells, float inset, out float cx, out float cy)
        {
            cx = cy = 0f;
            if (cell.x < 0 || cell.x >= gridWidth || cell.y < 0 || cell.y >= gridHeight)
                return false;
            if (shapeCells != null && !shapeCells.Contains(cell))
                return false;

            int row = gridHeight - 1 - cell.y;
            cx = layout.OriginX + cell.x * layout.CellSize + inset;
            cy = layout.OriginY + row * layout.CellSize + inset;
            return true;
        }

        /// <summary>
        /// A Custom Place over the block whose (inset) square starts at <paramref name="cx"/>,<paramref name="cy"/>:
        /// the whole block, or the quadrant of <paramref name="cornerSlot"/> (bit 0 = +X/right,
        /// bit 1 = +Z/top, matching the click-side flip in SelectSingleCell). Drawn as a translucent
        /// mark with an outline so the District/plaza fill underneath stays readable.
        /// </summary>
        private static void DrawCustomPlaceMark(Painter2D painter, float cx, float cy, float size, bool occupiesFullBlock, int cornerSlot, bool own, float alphaScale = 1f)
        {
            float x = cx, y = cy, width = size;
            if (!occupiesFullBlock)
            {
                width = size * 0.5f;
                x = cx + ((cornerSlot & 1) != 0 ? width : 0f);
                y = cy + ((cornerSlot & 2) != 0 ? 0f : width);
            }

            float padding = Mathf.Min(2f, width * 0.12f);
            x += padding;
            y += padding;
            width -= padding * 2f;

            Color fill = own ? OwnCustomPlaceFill : OtherCustomPlaceFill;
            Color outline = own ? OwnCustomPlaceOutline : OtherCustomPlaceOutline;
            fill.a *= alphaScale;
            outline.a *= alphaScale;
            DrawRect(painter, fill, x, y, width, width);
            StrokeRect(painter, outline, own ? 1.5f : 1f, x, y, width, width);
        }

        private CityGeneratorGridOccupancy ReadOccupancy()
        {
            occupancySource.serializedObject.Update();
            return CityGeneratorGridOccupancy.Read(occupancySource);
        }

        private static void DrawPlusIcon(Painter2D painter, Color color, float cx, float cy, float size)
        {
            float barLength = size * 0.5f;
            float barThickness = size * 0.12f;
            float centerX = cx + size * 0.5f;
            float centerY = cy + size * 0.5f;

            DrawRect(painter, color, centerX - barLength * 0.5f, centerY - barThickness * 0.5f, barLength, barThickness);
            DrawRect(painter, color, centerX - barThickness * 0.5f, centerY - barLength * 0.5f, barThickness, barLength);
        }

        private static void DrawMinusIcon(Painter2D painter, Color color, float cx, float cy, float size)
        {
            float barLength = size * 0.5f;
            float barThickness = size * 0.12f;
            float centerX = cx + size * 0.5f;
            float centerY = cy + size * 0.5f;

            DrawRect(painter, color, centerX - barLength * 0.5f, centerY - barThickness * 0.5f, barLength, barThickness);
        }

        /// <summary>
        /// Per-cell tooltip: whatever sits under the pointer right now (the block's (row, column),
        /// plaza, District(s), Custom Place(s) and, on the graph picker, the zone line), read from the
        /// same occupancy snapshot the board paints. <see cref="TooltipEvent.rect"/> is set to the
        /// hovered cell (a small square around the pointer on the graph picker, where zones and
        /// blocks overlap), so moving onto another cell asks for a fresh tooltip. Over a gutter or a
        /// street the board's instruction text (<see cref="instructionTooltip"/>) shows instead.
        /// </summary>
        private void OnTooltip(TooltipEvent evt)
        {
            string text = null;
            Rect localRect = default;
            bool described = hasPointer && (mode == CityGeneratorGridPreviewMode.NodeGraphPicker
                ? TryDescribeGraphAt(lastPointerLocal, out text, out localRect)
                : TryDescribeCellAt(lastPointerLocal, out text, out localRect));

            if (described)
            {
                evt.tooltip = text;
                evt.rect = this.LocalToWorld(localRect);
            }
            else if (!string.IsNullOrEmpty(instructionTooltip))
            {
                evt.tooltip = instructionTooltip;
                evt.rect = worldBound;
            }
            else
            {
                return;
            }

            evt.StopImmediatePropagation();
        }

        private bool TryDescribeCellAt(Vector2 local, out string text, out Rect localRect)
        {
            text = null;
            localRect = default;
            if (!TryComputeCellLayout(contentRect, out CellLayout layout))
                return false;

            int gx = Mathf.FloorToInt((local.x - layout.OriginX) / layout.CellSize);
            int row = Mathf.FloorToInt((local.y - layout.OriginY) / layout.CellSize);
            if (gx < 0 || gx >= gridWidth || row < 0 || row >= gridHeight)
                return false;

            var cell = new Vector2Int(gx, gridHeight - 1 - row);
            localRect = new Rect(layout.OriginX + gx * layout.CellSize, layout.OriginY + row * layout.CellSize, layout.CellSize, layout.CellSize);
            text = DescribeCell(cell, gridHeight, shapeCellsProperty != null ? ReadShapeCells() : null);
            return true;
        }

        private bool TryDescribeGraphAt(Vector2 local, out string text, out Rect localRect)
        {
            text = null;
            localRect = new Rect(local.x - 3f, local.y - 3f, 6f, 6f);
            if (pedestrianPreview == null || !TryComputeNodeGraphLayout(contentRect, out NodeGraphLayout layout))
                return false;

            var builder = new System.Text.StringBuilder();
            List<GraphZone> zones = GetZones();
            int zone = zones != null ? FindZoneAt(zones, layout, local) : -1;
            if (zone >= 0)
            {
                var selectedSet = new HashSet<int>(selectedNodeIndicesProperty != null ? ReadSelectedNodeIndices() : new List<int>());
                bool selected = IsZoneSelected(zones[zone], selectedSet);
                builder.Append(ZoneKindName(zones[zone].Kind)).Append(selected ? ": in this entry's route" : ": not in this entry's route");
            }

            IReadOnlyList<BlockCell> blocks = pedestrianPreview.Blocks;
            if (blocks != null)
            {
                float half = CityGeneratorConstants.BlockSize * 0.5f;
                float size = CityGeneratorConstants.BlockSize * layout.scale;
                foreach (BlockCell block in blocks)
                {
                    Vector2 topLeft = NodeGraphScreenPoint(layout, block.center + new Vector3(-half, 0f, half));
                    if (!new Rect(topLeft.x, topLeft.y, size, size).Contains(local))
                        continue;

                    if (builder.Length > 0)
                        builder.Append('\n');
                    builder.Append(DescribeCell(new Vector2Int(block.gridX, block.gridY), pedestrianPreview.RowCount, null));
                    break;
                }
            }

            text = builder.ToString();
            return builder.Length > 0;
        }

        private static string ZoneKindName(PedestrianNodeKind kind) => kind switch
        {
            PedestrianNodeKind.Ring => "Sidewalk ring edge",
            PedestrianNodeKind.Interior => "Interior spoke",
            PedestrianNodeKind.Crossing => "Crossing",
            _ => "Zone",
        };

        // Same 0-3 convention as CityGeneratorBuildingBuilder.SlotOffsets: bit 0 = +X (right), bit 1 = +Z (top).
        private static string QuadrantName(int cornerSlot) =>
            ((cornerSlot & 2) != 0 ? "top" : "bottom") + "-" + ((cornerSlot & 1) != 0 ? "right" : "left") + " quadrant";

        /// <summary>
        /// One line per thing that claims <paramref name="cell"/>, under a "Block (row, column)"
        /// header: plaza, every District (more than one is a conflict the validator reports, so all
        /// are listed), every Custom Place with its slot, and what an unclaimed block gets. The layer
        /// this board edits is tagged as "this district"/"this entry". Define City Area adds what a
        /// click there would do, matching its "+"/"-" icons.
        /// </summary>
        private string DescribeCell(Vector2Int cell, int rowCount, HashSet<Vector2Int> shapeCells)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("Block ").Append(CityGeneratorGrid.FormatCell(cell, rowCount));

            if (shapeCells != null && !shapeCells.Contains(cell))
            {
                builder.Append("\nNot part of the city.");
                if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit && CityGeneratorGrid.IsValidAddition(shapeCells, cell))
                    builder.Append("\nClick to add it to the city.");
                return builder.ToString();
            }

            CityGeneratorGridOccupancy occupancy = occupancySource != null ? ReadOccupancy() : null;
            HashSet<Vector2Int> plazaCells = mode == CityGeneratorGridPreviewMode.PlazaMultiToggle || occupancy == null
                ? ReadPlazaCells()
                : occupancy.PlazaCells;
            bool isPlaza = plazaCells.Contains(cell);
            if (isPlaza)
                builder.Append("\nPlaza");

            bool inDistrict = false;
            bool fullBlockPlace = false;
            if (occupancy != null)
            {
                for (int d = 0; d < occupancy.DistrictCells.Count; d++)
                {
                    if (!occupancy.DistrictCells[d].Contains(cell))
                        continue;

                    inDistrict = true;
                    bool own = occupancyOwner == CityGeneratorGridOccupancyOwner.District && d == occupancyOwnerIndex;
                    builder.Append("\nDistrict ").Append(occupancy.DistrictLabels[d]).Append(own ? " (this district)" : string.Empty);
                }

                foreach (CityGeneratorCustomPlaceMark mark in occupancy.CustomPlaces)
                {
                    if (mark.Cell != cell)
                        continue;

                    fullBlockPlace |= mark.OccupiesFullBlock;
                    bool own = occupancyOwner == CityGeneratorGridOccupancyOwner.CustomPlace && mark.Index == occupancyOwnerIndex;
                    builder.Append("\nCustom Place ").Append(mark.Label)
                        .Append(", ").Append(mark.OccupiesFullBlock ? "full block" : QuadrantName(mark.CornerSlot))
                        .Append(own ? " (this entry)" : string.Empty);
                }
            }

            // A full-block Custom Place leaves no slot for buildings; a quadrant one leaves the other three.
            if (!isPlaza && !fullBlockPlace)
                builder.Append(inDistrict ? "\nBuildings from its district." : "\nBuildings from the Buildings card.");

            if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit && shapeCells != null)
            {
                builder.Append(CityGeneratorGrid.CanRemoveWithoutSplitting(shapeCells, cell)
                    ? "\nClick to remove it from the city."
                    : "\nCan't be removed: the city would split in two or be left empty.");
            }

            return builder.ToString();
        }

        // What the axis labels' placement depends on; they are only re-placed when this changes.
        private readonly struct AxisLabelKey : IEquatable<AxisLabelKey>
        {
            private readonly bool isGraph;
            private readonly int gridWidth;
            private readonly int gridHeight;
            private readonly Vector2 areaSize;
            private readonly CityGeneratorPedestrianPreview graphSource;

            public AxisLabelKey(bool isGraph, int gridWidth, int gridHeight, Vector2 areaSize, CityGeneratorPedestrianPreview graphSource)
            {
                this.isGraph = isGraph;
                this.gridWidth = gridWidth;
                this.gridHeight = gridHeight;
                this.areaSize = areaSize;
                this.graphSource = graphSource;
            }

            public bool Equals(AxisLabelKey other) =>
                isGraph == other.isGraph && gridWidth == other.gridWidth && gridHeight == other.gridHeight
                && areaSize == other.areaSize && ReferenceEquals(graphSource, other.graphSource);
        }

        /// <summary>
        /// Places the 1-based row numbers (left gutter, top-down) and column numbers (top
        /// gutter, left to right) -- the numbers <see cref="CityGeneratorGrid.FormatCell"/> uses.
        /// Cell boards label every row/column of the board; the graph picker labels each row/column
        /// that holds at least one real block, at that block's centre.
        /// </summary>
        private void UpdateAxisLabels()
        {
            Rect area = contentRect;
            bool isGraph = mode == CityGeneratorGridPreviewMode.NodeGraphPicker;
            var key = new AxisLabelKey(isGraph, gridWidth, gridHeight, area.size, isGraph ? pedestrianPreview : null);
            if (hasAxisLabelKey && key.Equals(lastAxisLabelKey))
                return;

            lastAxisLabelKey = key;
            hasAxisLabelKey = true;

            int usedRows = 0;
            int usedColumns = 0;
            if (isGraph)
                PlaceGraphAxisLabels(area, ref usedRows, ref usedColumns);
            else
                PlaceCellAxisLabels(area, ref usedRows, ref usedColumns);

            HideUnusedLabels(rowLabels, usedRows);
            HideUnusedLabels(columnLabels, usedColumns);
        }

        private void PlaceCellAxisLabels(Rect area, ref int usedRows, ref int usedColumns)
        {
            if (area.width <= 0f || area.height <= 0f || !TryComputeCellLayout(area, out CellLayout layout))
                return;

            for (int y = 0; y < gridHeight; y++)
            {
                int gy = gridHeight - 1 - y;
                float centerY = layout.OriginY + (y + 0.5f) * layout.CellSize;
                PlaceRowLabel(usedRows++, CityGeneratorGrid.DisplayRow(gy, gridHeight), layout.OriginX, centerY);
            }

            for (int x = 0; x < gridWidth; x++)
            {
                float centerX = layout.OriginX + (x + 0.5f) * layout.CellSize;
                PlaceColumnLabel(usedColumns++, CityGeneratorGrid.DisplayColumn(x), centerX, layout.OriginY);
            }
        }

        private void PlaceGraphAxisLabels(Rect area, ref int usedRows, ref int usedColumns)
        {
            if (area.width <= 0f || area.height <= 0f || pedestrianPreview == null || pedestrianPreview.Blocks == null
                || !TryComputeNodeGraphLayout(area, out NodeGraphLayout layout))
                return;

            var rowCenters = new SortedDictionary<int, float>();
            var columnCenters = new SortedDictionary<int, float>();
            foreach (BlockCell block in pedestrianPreview.Blocks)
            {
                rowCenters[block.gridY] = block.center.z;
                columnCenters[block.gridX] = block.center.x;
            }

            float left = layout.originX;
            foreach (KeyValuePair<int, float> row in rowCenters)
            {
                float centerY = NodeGraphScreenPoint(layout, new Vector3(0f, 0f, row.Value)).y;
                PlaceRowLabel(usedRows++, CityGeneratorGrid.DisplayRow(row.Key, pedestrianPreview.RowCount), left, centerY);
            }

            foreach (KeyValuePair<int, float> column in columnCenters)
            {
                float centerX = NodeGraphScreenPoint(layout, new Vector3(column.Value, 0f, 0f)).x;
                PlaceColumnLabel(usedColumns++, CityGeneratorGrid.DisplayColumn(column.Key), centerX, layout.originY);
            }
        }

        // Row numbers sit right-aligned in the gutter just left of the board, centred on the row.
        private void PlaceRowLabel(int poolIndex, int number, float boardLeft, float centerY)
        {
            Label label = GetPooledLabel(rowLabels, poolIndex, TextAnchor.MiddleRight);
            label.text = number.ToString();
            label.style.left = boardLeft - RowLabelGutter;
            label.style.top = centerY - RowLabelGutter * 0.5f;
            label.style.width = RowLabelGutter - 2f;
            label.style.height = RowLabelGutter;
            label.style.display = DisplayStyle.Flex;
        }

        // Column numbers sit just above the board, centred on the column.
        private void PlaceColumnLabel(int poolIndex, int number, float centerX, float boardTop)
        {
            const float width = 20f;
            Label label = GetPooledLabel(columnLabels, poolIndex, TextAnchor.LowerCenter);
            label.text = number.ToString();
            label.style.left = centerX - width * 0.5f;
            label.style.top = boardTop - ColumnLabelGutter;
            label.style.width = width;
            label.style.height = ColumnLabelGutter;
            label.style.display = DisplayStyle.Flex;
        }

        private Label GetPooledLabel(List<Label> pool, int index, TextAnchor alignment)
        {
            while (pool.Count <= index)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("cg-grid-preview__axis-label");
                label.style.unityTextAlign = alignment;
                Add(label);
                pool.Add(label);
            }

            return pool[index];
        }

        private static void HideUnusedLabels(List<Label> pool, int usedCount)
        {
            for (int i = usedCount; i < pool.Count; i++)
                pool[i].style.display = DisplayStyle.None;
        }

        private HashSet<Vector2Int> ReadDistrictCells()
        {
            var result = new HashSet<Vector2Int>();
            if (districtCellsProperty == null)
                return result;

            for (int i = 0; i < districtCellsProperty.arraySize; i++)
                result.Add(districtCellsProperty.GetArrayElementAtIndex(i).vector2IntValue);
            return result;
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

        private HashSet<Vector2Int> ReadShapeCells()
        {
            var result = new HashSet<Vector2Int>();
            if (shapeCellsProperty == null)
                return result;

            for (int i = 0; i < shapeCellsProperty.arraySize; i++)
                result.Add(shapeCellsProperty.GetArrayElementAtIndex(i).vector2IntValue);
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

        private static void StrokeRect(Painter2D painter, Color color, float lineWidth, float x, float y, float width, float height)
        {
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
