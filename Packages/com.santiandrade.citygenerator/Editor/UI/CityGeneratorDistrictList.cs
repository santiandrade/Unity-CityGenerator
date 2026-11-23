using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;DistrictEntry&gt;</c> (SPEC 21): each row is self-contained (Name,
    /// an Override Buildings Per Block toggle with its own slider, a
    /// <see cref="CityGeneratorPrefabGrid"/> for the district's buildings and its own
    /// <see cref="CityGeneratorGridPreview"/> in DistrictMultiToggle mode for picking blocks),
    /// mirroring <see cref="CityGeneratorCustomPlaceList"/>.
    ///
    /// Rows use plain controls that read and write their SerializedProperty directly, never
    /// <c>PropertyField</c>: rows are added after <c>CityGeneratorWindow</c>'s one-time
    /// <c>rootVisualElement.Bind(...)</c> call, and a <c>PropertyField</c> created at that point
    /// never binds and renders empty. The row styling reuses the Custom Places list's classes.
    /// </summary>
    internal class CityGeneratorDistrictList : VisualElement
    {
        private readonly VisualElement rowsContainer;
        private readonly Action onChanged;
        private readonly Func<int> globalBuildingsPerBlockGetter;
        private readonly List<CityGeneratorGridPreview> gridPreviews = new();
        private SerializedProperty listProperty;
        private SerializedProperty shapeMaskProperty;
        private SerializedProperty plazaMaskProperty;
        private int gridWidth = 1;
        private int gridHeight = 1;

        /// <param name="globalBuildingsPerBlockGetter">General Options' Buildings Per Block, copied into a new row's own value so turning its override on starts from the global one.</param>
        public CityGeneratorDistrictList(Func<int> globalBuildingsPerBlockGetter, Action onChanged = null)
        {
            this.globalBuildingsPerBlockGetter = globalBuildingsPerBlockGetter;
            this.onChanged = onChanged;
            AddToClassList("cg-custom-place-list");

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-custom-place-list__rows");
            Add(rowsContainer);

            var addButton = new Button(AddEntry) { text = "+ Add District", tooltip = "Adds a new empty District entry." };
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

        /// <summary>Custom Grid (SPEC 11): only the shape's real blocks are drawn and clickable. Pass <c>null</c> for the plain rectangle.</summary>
        public void SetShapeMask(SerializedProperty customBlockCellsProperty)
        {
            shapeMaskProperty = customBlockCellsProperty;
            foreach (CityGeneratorGridPreview preview in gridPreviews)
            {
                preview.SetShapeMask(shapeMaskProperty);
                // The preview only switches to the full MaxGridSize canvas inside SetGrid, so it
                // has to be re-applied after the mask changes, not just before.
                preview.SetGrid(gridWidth, gridHeight);
            }
        }

        /// <summary>Paints the configured plazas (<c>general.plazaCells</c>) green on every row's picker, as a reference.</summary>
        public void SetPlazaMask(SerializedProperty plazaCellsProperty)
        {
            plazaMaskProperty = plazaCellsProperty;
            foreach (CityGeneratorGridPreview preview in gridPreviews)
                preview.SetPlazaMask(plazaMaskProperty);
        }

        /// <summary>Repaints every row's picker, so one district's edit shows up as "occupied" on the others right away.</summary>
        public void RefreshPreviews()
        {
            foreach (CityGeneratorGridPreview preview in gridPreviews)
                preview.Refresh();
        }

        private void AddEntry()
        {
            if (listProperty == null)
                return;

            listProperty.serializedObject.Update();
            int index = listProperty.arraySize;
            // InsertArrayElementAtIndex copies the previous element, so every field is reset.
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty entry = listProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("name").stringValue = string.Empty;
            entry.FindPropertyRelative("cells").ClearArray();
            entry.FindPropertyRelative("buildingPrefabs").ClearArray();
            entry.FindPropertyRelative("overrideBuildingsPerBlock").boolValue = false;
            int globalBuildingsPerBlock = globalBuildingsPerBlockGetter != null ? globalBuildingsPerBlockGetter() : CityGeneratorConstants.MaxBuildingSlotsPerBlock;
            entry.FindPropertyRelative("buildingsPerBlock").intValue = Mathf.Clamp(globalBuildingsPerBlock, 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock);
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

                SerializedProperty nameProperty = entry.FindPropertyRelative("name");
                SerializedProperty overrideProperty = entry.FindPropertyRelative("overrideBuildingsPerBlock");
                SerializedProperty buildingsPerBlockProperty = entry.FindPropertyRelative("buildingsPerBlock");

                var row = new VisualElement();
                row.AddToClassList("cg-custom-place-list__row");

                var header = new VisualElement();
                header.AddToClassList("cg-custom-place-list__row-header");
                var nameField = new TextField("Name") { value = nameProperty.stringValue, tooltip = nameProperty.tooltip };
                nameField.AddToClassList("cg-field-row");
                nameField.RegisterValueChangedCallback(evt => SetString(nameProperty, evt.newValue));
                header.Add(nameField);
                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this district from the list." };
                removeButton.AddToClassList("cg-custom-place-list__remove");
                header.Add(removeButton);
                row.Add(header);

                var overrideField = new Toggle("Override Buildings Per Block") { value = overrideProperty.boolValue, tooltip = overrideProperty.tooltip };
                overrideField.AddToClassList("cg-field-row");
                row.Add(overrideField);

                var buildingsPerBlockSlider = new SliderInt("Buildings Per Block", 0, CityGeneratorConstants.MaxBuildingSlotsPerBlock)
                {
                    value = buildingsPerBlockProperty.intValue,
                    showInputField = true,
                    tooltip = buildingsPerBlockProperty.tooltip,
                };
                buildingsPerBlockSlider.AddToClassList("cg-field-row");
                buildingsPerBlockSlider.style.display = overrideProperty.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                buildingsPerBlockSlider.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != buildingsPerBlockProperty.intValue)
                        SetInt(buildingsPerBlockProperty, evt.newValue);
                });
                row.Add(buildingsPerBlockSlider);

                overrideField.RegisterValueChangedCallback(evt =>
                {
                    buildingsPerBlockSlider.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    SetBool(overrideProperty, evt.newValue);
                });

                var buildingsLabel = new Label("Buildings");
                buildingsLabel.AddToClassList("cg-field-row");
                row.Add(buildingsLabel);
                var prefabGrid = new CityGeneratorPrefabGrid(onChanged);
                prefabGrid.Bind(entry.FindPropertyRelative("buildingPrefabs"));
                row.Add(prefabGrid);

                var preview = new CityGeneratorGridPreview();
                preview.BindDistrictCells(entry.FindPropertyRelative("cells"), onChanged);
                preview.SetShapeMask(shapeMaskProperty);
                preview.SetGrid(gridWidth, gridHeight);
                preview.SetPlazaMask(plazaMaskProperty);
                preview.SetOccupiedMask(() => ReadOtherDistrictCells(capturedIndex));
                row.Add(preview);
                gridPreviews.Add(preview);

                var hint = new Label("Click a block to add/remove it. Green: plaza. Grey-blue: another district.");
                hint.AddToClassList("cg-grid-preview__caption");
                row.Add(hint);

                rowsContainer.Add(row);
            }
        }

        private HashSet<Vector2Int> ReadOtherDistrictCells(int excludedIndex)
        {
            var result = new HashSet<Vector2Int>();
            if (listProperty == null)
                return result;

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                if (i == excludedIndex)
                    continue;

                SerializedProperty cells = listProperty.GetArrayElementAtIndex(i).FindPropertyRelative("cells");
                for (int c = 0; c < cells.arraySize; c++)
                    result.Add(cells.GetArrayElementAtIndex(c).vector2IntValue);
            }

            return result;
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

        private void SetInt(SerializedProperty property, int value)
        {
            property.serializedObject.Update();
            property.intValue = value;
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
