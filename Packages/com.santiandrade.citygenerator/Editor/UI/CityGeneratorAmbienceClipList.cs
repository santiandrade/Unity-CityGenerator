using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;AmbienceClipEntry&gt;</c>: each row is a self-contained clip +
    /// its own volume slider. Rows are built with plain controls (ObjectField/Slider) that read
    /// and write their SerializedProperty directly, instead of PropertyField, since rows are
    /// added to the tree well after CityGeneratorWindow's one-time Bind() call — same reasoning
    /// as <see cref="CityGeneratorCustomPlaceList"/>.
    /// </summary>
    internal class CityGeneratorAmbienceClipList : VisualElement
    {
        private readonly VisualElement rowsContainer;
        private readonly Action onChanged;
        private SerializedProperty listProperty;

        public CityGeneratorAmbienceClipList(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-custom-place-list");

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-custom-place-list__rows");
            Add(rowsContainer);

            var addButton = new Button(AddEntry) { text = "+ Add Ambience Clip", tooltip = "Adds a new ambience clip entry, at volume 1." };
            addButton.AddToClassList("cg-custom-place-list__add-button");
            Add(addButton);
        }

        public void Bind(SerializedProperty property)
        {
            listProperty = property;
            Rebuild();
        }

        private void AddEntry()
        {
            if (listProperty == null)
                return;

            listProperty.serializedObject.Update();
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty entry = listProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("clip").objectReferenceValue = null;
            entry.FindPropertyRelative("volume").floatValue = 1f;
            listProperty.serializedObject.ApplyModifiedProperties();

            Rebuild();
            onChanged?.Invoke();
        }

        private void Rebuild()
        {
            rowsContainer.Clear();
            if (listProperty == null)
                return;

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty entry = listProperty.GetArrayElementAtIndex(i);
                int capturedIndex = i;

                SerializedProperty clipProperty = entry.FindPropertyRelative("clip");
                SerializedProperty volumeProperty = entry.FindPropertyRelative("volume");

                var row = new VisualElement();
                row.AddToClassList("cg-custom-place-list__row");

                var header = new VisualElement();
                header.AddToClassList("cg-custom-place-list__row-header");
                var clipField = new ObjectField("Clip") { objectType = typeof(AudioClip), allowSceneObjects = false, value = clipProperty.objectReferenceValue };
                clipField.AddToClassList("cg-field-row");
                clipField.RegisterValueChangedCallback(evt => SetObjectReference(clipProperty, evt.newValue));
                header.Add(clipField);
                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this entry from the list." };
                removeButton.AddToClassList("cg-custom-place-list__remove");
                header.Add(removeButton);
                row.Add(header);

                var volumeField = new Slider("Volume", 0f, 1f) { value = volumeProperty.floatValue };
                volumeField.AddToClassList("cg-field-row");
                volumeField.RegisterValueChangedCallback(evt => SetFloat(volumeProperty, evt.newValue));
                row.Add(volumeField);

                rowsContainer.Add(row);
            }
        }

        private void SetObjectReference(SerializedProperty property, UnityEngine.Object value)
        {
            property.serializedObject.Update();
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        }

        private void SetFloat(SerializedProperty property, float value)
        {
            property.serializedObject.Update();
            property.floatValue = value;
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
