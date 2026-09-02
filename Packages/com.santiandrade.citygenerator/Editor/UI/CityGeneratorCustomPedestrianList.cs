using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;CustomPedestrianEntry&gt;</c> (SPEC 12): each row is
    /// self-contained (title, prefab, count and its own node-graph <see cref="CityGeneratorGridPreview"/>
    /// picker), mirroring <see cref="CityGeneratorCustomPlaceList"/>. Every row's picker shares one
    /// <see cref="CityGeneratorPedestrianPreview"/> instance, rebuilt only when the settings that
    /// determine the pedestrian graph actually change (see <see cref="RefreshPreview"/>) rather than
    /// on every call -- building it is comparatively expensive (it runs real generation code).
    /// </summary>
    internal class CityGeneratorCustomPedestrianList : VisualElement
    {
        private readonly VisualElement rowsContainer;
        private readonly Action onChanged;
        private readonly List<CityGeneratorGridPreview> gridPreviews = new();
        private SerializedProperty listProperty;
        private CityGeneratorPedestrianPreview pedestrianPreview;
        private int cachedSettingsSignature;
        private bool hasCachedSignature;

        public CityGeneratorCustomPedestrianList(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-custom-place-list");

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-custom-place-list__rows");
            Add(rowsContainer);

            var addButton = new Button(AddEntry) { text = "+ Add Custom Pedestrian", tooltip = "Adds a new empty Custom Pedestrian entry." };
            addButton.AddToClassList("cg-custom-place-list__add-button");
            Add(addButton);
        }

        public void Bind(SerializedProperty property)
        {
            listProperty = property;
            Rebuild();
        }

        /// <summary>The shared preview last built by <see cref="RefreshPreview"/>, or null before the first call -- reused by <c>CityGeneratorValidator</c> so it never has to build a second one for the same settings.</summary>
        public CityGeneratorPedestrianPreview CurrentPreview => pedestrianPreview;

        /// <summary>
        /// Rebuilds the shared pedestrian graph preview only if the grid/Custom Grid/plazas/Custom
        /// Places/traffic light prefab settings that determine it actually changed since the last
        /// call, then rebinds every row's picker to whatever preview is current (cheap even when
        /// nothing changed: no graph rebuild, just a property read + staleness check per row).
        /// </summary>
        public void RefreshPreview(CityGeneratorSettings settings)
        {
            int signature = CityGeneratorPedestrianPreview.ComputeSettingsSignature(settings);
            if (!hasCachedSignature || signature != cachedSettingsSignature || pedestrianPreview == null)
            {
                pedestrianPreview?.Dispose();
                pedestrianPreview = CityGeneratorPedestrianPreview.Build(settings);
                cachedSettingsSignature = signature;
                hasCachedSignature = true;
            }

            if (listProperty == null)
                return;

            for (int i = 0; i < gridPreviews.Count && i < listProperty.arraySize; i++)
            {
                SerializedProperty entry = listProperty.GetArrayElementAtIndex(i);
                gridPreviews[i].BindNodeGraph(
                    pedestrianPreview,
                    entry.FindPropertyRelative("selectedNodeIndices"),
                    entry.FindPropertyRelative("graphFingerprint"),
                    onChanged);
            }
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
            entry.FindPropertyRelative("count").intValue = 1;
            entry.FindPropertyRelative("selectedNodeIndices").ClearArray();
            entry.FindPropertyRelative("graphFingerprint").intValue = 0;
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
                SerializedProperty countProperty = entry.FindPropertyRelative("count");
                SerializedProperty selectedNodeIndicesProperty = entry.FindPropertyRelative("selectedNodeIndices");
                SerializedProperty fingerprintProperty = entry.FindPropertyRelative("graphFingerprint");

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
                prefabField.tooltip = "Prefab instantiated at each spawn node.";
                prefabField.RegisterValueChangedCallback(evt => SetObjectReference(prefabProperty, evt.newValue));
                row.Add(prefabField);

                var countField = new IntegerField("Count") { value = countProperty.intValue };
                countField.AddToClassList("cg-field-row");
                countField.tooltip = "Number of agents of this prefab spawned across this entry's node network. Must be at least 1.";
                countField.RegisterValueChangedCallback(evt => SetInt(countProperty, Mathf.Max(1, evt.newValue)));
                row.Add(countField);

                var preview = new CityGeneratorGridPreview();
                if (pedestrianPreview != null)
                    preview.BindNodeGraph(pedestrianPreview, selectedNodeIndicesProperty, fingerprintProperty, onChanged);
                row.Add(preview);
                gridPreviews.Add(preview);

                var hint = new Label("Click a line above to add/remove that zone from this entry's route. Only a zone sharing a point with the current selection can be added, except the first. Ring edge = green, crossing = orange, interior spoke = blue; selected zones are highlighted in yellow.");
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

        private void SetInt(SerializedProperty property, int value)
        {
            property.serializedObject.Update();
            property.intValue = value;
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

        /// <summary>Destroys the shared preview's disposable GameObject. Call when the owning window closes.</summary>
        public void Dispose()
        {
            pedestrianPreview?.Dispose();
            pedestrianPreview = null;
            hasCachedSignature = false;
        }
    }
}
