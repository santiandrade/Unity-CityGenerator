using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;CustomPlaceEntry&gt;</c>: each row is self-contained (title,
    /// prefab, Is Point Of Interest / Occupies Full Block toggles, a facing selector and its own
    /// single-selection <see cref="CityGeneratorGridPreview"/> for picking the block/quadrant),
    /// mirroring how each row of <see cref="CityGeneratorWeightedPrefabList"/> is self-contained —
    /// there is no shared "which entry am I editing" state to keep in sync.
    /// </summary>
    internal class CityGeneratorCustomPlaceList : VisualElement
    {
        private readonly VisualElement rowsContainer;
        private readonly Action onChanged;
        private readonly List<CityGeneratorGridPreview> gridPreviews = new();
        private SerializedProperty listProperty;
        private int gridWidth = 1;
        private int gridHeight = 1;

        public CityGeneratorCustomPlaceList(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-custom-place-list");

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-custom-place-list__rows");
            Add(rowsContainer);

            var addButton = new Button(AddEntry) { text = "+ Add Custom Place", tooltip = "Adds a new empty Custom Place entry." };
            addButton.AddToClassList("cg-custom-place-list__add-button");
            Add(addButton);
        }

        public void Bind(SerializedProperty property)
        {
            listProperty = property;
            Rebuild();
        }

        /// <summary>Propagates the current grid size to every row's own picker, so they render the right number of cells.</summary>
        public void SetGrid(int width, int height)
        {
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);
            foreach (CityGeneratorGridPreview preview in gridPreviews)
                preview.SetGrid(gridWidth, gridHeight);
        }

        private void AddEntry()
        {
            if (listProperty == null)
                return;

            listProperty.serializedObject.Update();
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty entry = listProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("title").stringValue = string.Empty;
            entry.FindPropertyRelative("prefab").objectReferenceValue = null;
            entry.FindPropertyRelative("isPointOfInterest").boolValue = false;
            entry.FindPropertyRelative("occupiesFullBlock").boolValue = false;
            entry.FindPropertyRelative("blockCell").vector2IntValue = Vector2Int.zero;
            entry.FindPropertyRelative("cornerSlot").intValue = 0;
            entry.FindPropertyRelative("facing").enumValueIndex = 0;
            entry.FindPropertyRelative("positionAssigned").boolValue = false;
            listProperty.serializedObject.ApplyModifiedProperties();

            Rebuild();
            onChanged?.Invoke();
        }

        private void Rebuild()
        {
            rowsContainer.Clear();
            gridPreviews.Clear();
            if (listProperty == null)
                return;

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty entry = listProperty.GetArrayElementAtIndex(i);
                int capturedIndex = i;

                var row = new VisualElement();
                row.AddToClassList("cg-custom-place-list__row");

                var header = new VisualElement();
                header.AddToClassList("cg-custom-place-list__row-header");
                var titleField = new PropertyField(entry.FindPropertyRelative("title"), "Title");
                titleField.AddToClassList("cg-field-row");
                header.Add(titleField);
                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this entry from the list." };
                removeButton.AddToClassList("cg-custom-place-list__remove");
                header.Add(removeButton);
                row.Add(header);

                var prefabField = new PropertyField(entry.FindPropertyRelative("prefab"), "Prefab");
                prefabField.AddToClassList("cg-field-row");
                row.Add(prefabField);

                var poiField = new PropertyField(entry.FindPropertyRelative("isPointOfInterest"), "Is Point Of Interest");
                poiField.AddToClassList("cg-field-row");
                poiField.tooltip = "Reserved for a future minimap/POI system. No functional effect yet.";
                row.Add(poiField);

                SerializedProperty occupiesFullBlockProperty = entry.FindPropertyRelative("occupiesFullBlock");
                var occupiesField = new PropertyField(occupiesFullBlockProperty, "Occupies Full Block");
                occupiesField.AddToClassList("cg-field-row");
                row.Add(occupiesField);

                var facingField = new PropertyField(entry.FindPropertyRelative("facing"), "Facing");
                facingField.AddToClassList("cg-field-row");
                row.Add(facingField);

                var preview = new CityGeneratorGridPreview();
                preview.SetGrid(gridWidth, gridHeight);
                preview.BindSingleSelection(
                    entry.FindPropertyRelative("blockCell"),
                    entry.FindPropertyRelative("cornerSlot"),
                    entry.FindPropertyRelative("positionAssigned"),
                    () => occupiesFullBlockProperty.boolValue,
                    onChanged);
                row.Add(preview);
                gridPreviews.Add(preview);

                // Occupying the full block hides the quadrant highlight in this same picker, so it
                // must repaint the moment that toggle flips, not just on the next full Rebuild.
                occupiesField.TrackPropertyValue(occupiesFullBlockProperty, _ => preview.Refresh());

                var hint = new Label("Click a block above to place this entry; click a corner to pick a quadrant (ignored when occupying the full block).");
                hint.AddToClassList("cg-grid-preview__caption");
                row.Add(hint);

                rowsContainer.Add(row);
            }
        }

        private void RemoveEntryAt(int index)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            listProperty.serializedObject.Update();
            listProperty.DeleteArrayElementAtIndex(index);
            listProperty.serializedObject.ApplyModifiedProperties();
            Rebuild();
            onChanged?.Invoke();
        }
    }
}
