using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;AmbienceClipEntry&gt;</c>: each row is a self-contained clip +
    /// its own volume (a Slider paired with a FloatField, kept in sync). Rows are built with plain
    /// controls (ObjectField/Slider/FloatField) instead of PropertyField, since rows are added to
    /// the tree well after CityGeneratorWindow's one-time Bind() call — same reasoning as
    /// <see cref="CityGeneratorCustomPlaceList"/>.
    ///
    /// Writes re-fetch each SerializedProperty by array index at write time (see
    /// <see cref="SetObjectReference"/>/<see cref="SetFloat"/>) rather than closing over a
    /// property captured once in <see cref="Rebuild"/> — mirroring
    /// <see cref="CityGeneratorWeightedPrefabList.SetPercentage"/>. A captured-property closure
    /// used to go stale after the window's <c>TrackSerializedObjectValue</c> callback (or any
    /// sibling field's own edit) called <c>Update()</c> on the shared <c>SerializedObject</c> in
    /// between the row being built and the field being edited, silently dropping the edit.
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

                UnityEngine.Object clipValue = entry.FindPropertyRelative("clip").objectReferenceValue;
                float volumeValue = entry.FindPropertyRelative("volume").floatValue;

                var row = new VisualElement();
                row.AddToClassList("cg-custom-place-list__row");

                var header = new VisualElement();
                header.AddToClassList("cg-custom-place-list__row-header");
                var clipField = new ObjectField("Clip") { objectType = typeof(AudioClip), allowSceneObjects = false, value = clipValue };
                clipField.AddToClassList("cg-field-row");
                clipField.RegisterValueChangedCallback(evt => SetObjectReference(capturedIndex, "clip", evt.newValue));
                header.Add(clipField);
                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this entry from the list." };
                removeButton.AddToClassList("cg-custom-place-list__remove");
                header.Add(removeButton);
                row.Add(header);

                row.Add(BuildVolumeRow(volumeValue, value => SetFloat(capturedIndex, "volume", value)));

                rowsContainer.Add(row);
            }
        }

        /// <summary>A Volume slider paired with a numeric FloatField, kept in sync in both directions without a full Rebuild.</summary>
        internal static VisualElement BuildVolumeRow(float volumeValue, Action<float> onVolumeChanged)
        {
            const string tooltip = "This entry's own volume, independent of the other entries in the list.";

            var volumeRow = new VisualElement();
            volumeRow.AddToClassList("cg-field-row");
            volumeRow.AddToClassList("cg-value-row");

            var volumeSlider = new Slider("Volume", 0f, 1f) { value = volumeValue, tooltip = tooltip };
            volumeSlider.AddToClassList("cg-value-row__slider");
            volumeRow.Add(volumeSlider);

            var volumeField = new FloatField { value = volumeValue, tooltip = tooltip };
            volumeField.AddToClassList("cg-value-row__field");
            volumeRow.Add(volumeField);

            volumeSlider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp01(evt.newValue);
                volumeField.SetValueWithoutNotify(clamped);
                onVolumeChanged(clamped);
            });
            volumeField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp01(evt.newValue);
                volumeSlider.SetValueWithoutNotify(clamped);
                onVolumeChanged(clamped);
            });

            return volumeRow;
        }

        private void SetObjectReference(int index, string fieldName, UnityEngine.Object value)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            listProperty.serializedObject.Update();
            listProperty.GetArrayElementAtIndex(index).FindPropertyRelative(fieldName).objectReferenceValue = value;
            listProperty.serializedObject.ApplyModifiedProperties();
            onChanged?.Invoke();
        }

        private void SetFloat(int index, string fieldName, float value)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            listProperty.serializedObject.Update();
            listProperty.GetArrayElementAtIndex(index).FindPropertyRelative(fieldName).floatValue = value;
            listProperty.serializedObject.ApplyModifiedProperties();
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
