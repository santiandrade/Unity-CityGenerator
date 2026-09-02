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

        // CustomAreaEdit mode (data source), and shared as the shape-mask overlay applied on top
        // of PlazaMultiToggle/SingleSelectQuadrant by SetShapeMask.
        private SerializedProperty shapeCellsProperty;

        // NodeGraphPicker mode.
        private CityGeneratorPedestrianPreview pedestrianPreview;
        private SerializedProperty selectedNodeIndicesProperty;
        private SerializedProperty graphFingerprintProperty;
        private List<GraphZone> zoneCache;
        private CityGeneratorPedestrianPreview zoneCacheSource;

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
            tooltip = "Click a \"+\" to add a block, or a \"-\" to remove one.";
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
            tooltip = "Click a line to add/remove the zone it represents from this entry's route. Only a zone sharing a node with the current selection can be added, except the first.";

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

        /// <summary>
        /// SingleSelectQuadrant mode only: overlays the General Options grid's configured plazas
        /// (<c>general.plazaCells</c>) as a reference so the user can see where plazas already sit
        /// while placing a Custom Place. Pass <c>null</c> to disable.
        /// </summary>
        public void SetPlazaMask(SerializedProperty plazaCellsProperty)
        {
            this.plazaCellsProperty = plazaCellsProperty;
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

            if (mode == CityGeneratorGridPreviewMode.NodeGraphPicker)
            {
                OnZonePointerDown(evt, area);
                evt.StopPropagation();
                return;
            }

            float cellSize = Mathf.Min(area.width / gridWidth, area.height / gridHeight);
            float totalWidth = cellSize * gridWidth;
            float totalHeight = cellSize * gridHeight;
            float originX = (area.width - totalWidth) * 0.5f;
            float originY = (area.height - totalHeight) * 0.5f;

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
            if (mode == CityGeneratorGridPreviewMode.CustomAreaEdit)
                DrawCustomAreaEdit(context);
            else if (mode == CityGeneratorGridPreviewMode.PlazaMultiToggle)
                DrawPlazaMultiToggle(context);
            else if (mode == CityGeneratorGridPreviewMode.NodeGraphPicker)
                DrawZoneGraph(context);
            else
                DrawSingleSelection(context);
        }

        // NodeGraphPicker geometry: world XZ -> screen mapping shared by drawing and hit-testing,
        // fit to the node bounds (there is no fixed block grid to size against in this mode).
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

            float spanX = Mathf.Max(0.01f, maxX - minX);
            float spanZ = Mathf.Max(0.01f, maxZ - minZ);
            const float padding = 0.92f;
            float scale = Mathf.Min(area.width / spanX, area.height / spanZ) * padding;
            float originX = (area.width - spanX * scale) * 0.5f;
            float originY = (area.height - spanZ * scale) * 0.5f;

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

            Vector2 local = evt.localPosition;
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

            if (hitZone >= 0)
                ToggleZone(hitZone);
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

        private void DrawCustomAreaEdit(MeshGenerationContext context)
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
            Color holeColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);
            Color iconColor = new Color(1f, 1f, 1f, 0.9f);

            DrawRect(painter, streetColor, originX, originY, totalWidth, totalHeight);

            HashSet<Vector2Int> existing = ReadShapeCells();
            float inset = cellSize * 0.08f;

            for (int y = 0; y < gridHeight; y++)
            {
                int gy = gridHeight - 1 - y;
                for (int x = 0; x < gridWidth; x++)
                {
                    var cell = new Vector2Int(x, gy);
                    bool isReal = existing.Contains(cell);

                    float cx = originX + x * cellSize + inset;
                    float cy = originY + y * cellSize + inset;
                    float size = cellSize - inset * 2f;
                    DrawRect(painter, isReal ? blockColor : holeColor, cx, cy, size, size);

                    if (isReal)
                    {
                        if (CityGeneratorGrid.CanRemoveWithoutSplitting(existing, cell))
                            DrawMinusIcon(painter, iconColor, cx, cy, size);
                    }
                    else if (CityGeneratorGrid.IsValidAddition(existing, cell))
                    {
                        DrawPlusIcon(painter, iconColor, cx, cy, size);
                    }
                }
            }
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
            Color holeColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);

            DrawRect(painter, streetColor, originX, originY, totalWidth, totalHeight);

            HashSet<Vector2Int> plazaCells = ReadPlazaCells();
            HashSet<Vector2Int> shapeCells = shapeCellsProperty != null ? ReadShapeCells() : null;
            float inset = cellSize * 0.08f;

            // Screen row y=0 is the top of the picture; it must show the block that renders at
            // the top of Unity's own top-down view, which is the highest gy (largest +Z), so the
            // row read from plazaCells is flipped relative to the screen row being painted.
            for (int y = 0; y < gridHeight; y++)
            {
                int gy = gridHeight - 1 - y;
                for (int x = 0; x < gridWidth; x++)
                {
                    var cell = new Vector2Int(x, gy);
                    float cx = originX + x * cellSize + inset;
                    float cy = originY + y * cellSize + inset;
                    float size = cellSize - inset * 2f;

                    if (shapeCells != null && !shapeCells.Contains(cell))
                    {
                        DrawRect(painter, holeColor, cx, cy, size, size);
                        continue;
                    }

                    bool isPlaza = plazaCells.Contains(cell);
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
            Color plazaColor = new Color(0.35f, 0.75f, 0.4f, 0.7f);
            Color selectedColor = new Color(0.85f, 0.6f, 0.2f, 0.75f);
            Color quadrantColor = new Color(0.95f, 0.75f, 0.3f, 0.9f);
            Color holeColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);

            DrawRect(painter, streetColor, originX, originY, totalWidth, totalHeight);

            bool positionAssigned = positionAssignedProperty != null && positionAssignedProperty.boolValue;
            Vector2Int selectedCell = blockCellProperty != null ? blockCellProperty.vector2IntValue : new Vector2Int(-1, -1);
            bool occupiesFullBlock = occupiesFullBlockGetter != null && occupiesFullBlockGetter();
            int cornerSlot = cornerSlotProperty != null ? cornerSlotProperty.intValue : -1;
            HashSet<Vector2Int> shapeCells = shapeCellsProperty != null ? ReadShapeCells() : null;
            HashSet<Vector2Int> plazaCells = plazaCellsProperty != null ? ReadPlazaCells() : null;

            float inset = cellSize * 0.08f;

            // Same row flip as DrawPlazaMultiToggle: screen row y=0 (top) must show gy = gridHeight-1
            // (largest +Z), matching Unity's own top-down view.
            for (int y = 0; y < gridHeight; y++)
            {
                int gy = gridHeight - 1 - y;
                for (int x = 0; x < gridWidth; x++)
                {
                    var cell = new Vector2Int(x, gy);
                    float cx = originX + x * cellSize + inset;
                    float cy = originY + y * cellSize + inset;
                    float size = cellSize - inset * 2f;

                    if (shapeCells != null && !shapeCells.Contains(cell))
                    {
                        DrawRect(painter, holeColor, cx, cy, size, size);
                        continue;
                    }

                    bool isSelected = positionAssigned && selectedCell.x == x && selectedCell.y == gy;
                    bool isPlaza = plazaCells != null && plazaCells.Contains(cell);
                    Color cellColor = isSelected ? selectedColor : (isPlaza ? plazaColor : blockColor);
                    DrawRect(painter, cellColor, cx, cy, size, size);

                    if (isSelected && !occupiesFullBlock && cornerSlot >= 0)
                    {
                        float half = size * 0.5f;
                        // Bit 2 (+Z slots 2/3) draws in the top half of the cell, matching the
                        // click-side flip in SelectSingleCell.
                        float qx = cx + ((cornerSlot & 1) != 0 ? half : 0f);
                        float qy = cy + ((cornerSlot & 2) != 0 ? 0f : half);
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
    }
}
