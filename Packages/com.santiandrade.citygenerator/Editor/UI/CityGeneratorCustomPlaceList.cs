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
    ///
    /// Rows are constructed with plain controls (TextField/ObjectField/Toggle/EnumField) that read
    /// and write their SerializedProperty directly, instead of <c>PropertyField</c>: rows are added
    /// to the tree well after <c>CityGeneratorWindow</c>'s one-time <c>rootVisualElement.Bind(...)</c>
    /// call (they're created on demand, when the user clicks "+ Add Custom Place"), and a
    /// <c>PropertyField</c> constructed at that point never picks up a binding — it renders with no
    /// content at all. Same reasoning as <see cref="CityGeneratorPrefabGrid"/>/<see cref="CityGeneratorWeightedPrefabList"/>.
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

                SerializedProperty titleProperty = entry.FindPropertyRelative("title");
                SerializedProperty prefabProperty = entry.FindPropertyRelative("prefab");
                SerializedProperty isPointOfInterestProperty = entry.FindPropertyRelative("isPointOfInterest");
                SerializedProperty occupiesFullBlockProperty = entry.FindPropertyRelative("occupiesFullBlock");
                SerializedProperty facingProperty = entry.FindPropertyRelative("facing");

                var row = new VisualElement();
                row.AddToClassList("cg-custom-place-list__row");

                var header = new VisualElement();
                header.AddToClassList("cg-custom-place-list__row-header");
                var titleField = new TextField("Title") { value = titleProperty.stringValue };
                titleField.AddToClassList("cg-field-row");
                titleField.RegisterValueChangedCallback(evt => SetString(titleProperty, evt.newValue));
                header.Add(titleField);
                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this entry from the list." };
                removeButton.AddToClassList("cg-custom-place-list__remove");
                header.Add(removeButton);
                row.Add(header);

                var prefabField = new ObjectField("Prefab") { objectType = typeof(GameObject), allowSceneObjects = false, value = prefabProperty.objectReferenceValue };
                prefabField.AddToClassList("cg-field-row");
                prefabField.RegisterValueChangedCallback(evt => SetObjectReference(prefabProperty, evt.newValue));
                row.Add(prefabField);

                var poiField = new Toggle("Is Point Of Interest") { value = isPointOfInterestProperty.boolValue };
                poiField.AddToClassList("cg-field-row");
                poiField.tooltip = "Marks this place as a Point of Interest shown on the Minimap HUD, labelled with Title.";
                poiField.RegisterValueChangedCallback(evt => SetBool(isPointOfInterestProperty, evt.newValue));
                row.Add(poiField);

                var occupiesField = new Toggle("Occupies Full Block") { value = occupiesFullBlockProperty.boolValue };
                occupiesField.AddToClassList("cg-field-row");
                row.Add(occupiesField);

                var facingField = new EnumField("Facing", (CustomPlaceFacing)facingProperty.enumValueIndex);
                facingField.AddToClassList("cg-field-row");
                facingField.RegisterValueChangedCallback(evt => SetEnum(facingProperty, (CustomPlaceFacing)evt.newValue));
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
                occupiesField.RegisterValueChangedCallback(evt =>
                {
                    SetBool(occupiesFullBlockProperty, evt.newValue);
                    preview.Refresh();
                });

                var hint = new Label("Click a block above to place this entry; click a corner to pick a quadrant (ignored when occupying the full block).");
                hint.AddToClassList("cg-grid-preview__caption");
                row.Add(hint);

                rowsContainer.Add(row);
            }
        }

        private void SetString(SerializedProperty property, string value)
        {
            property.serializedObject.Update();
            property.stringValue = value;
            property.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        }

        private void SetBool(SerializedProperty property, bool value)
        {
            property.serializedObject.Update();
            property.boolValue = value;
            property.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        }

        private void SetObjectReference(SerializedProperty property, UnityEngine.Object value)
        {
            property.serializedObject.Update();
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        }

        private void SetEnum(SerializedProperty property, CustomPlaceFacing value)
        {
            property.serializedObject.Update();
            property.enumValueIndex = (int)value;
            property.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
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
