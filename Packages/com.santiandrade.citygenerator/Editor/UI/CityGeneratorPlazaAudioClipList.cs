using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;PlazaAudioClipEntry&gt;</c>: each row is a self-contained clip
    /// plus its own volume (a Slider paired with a FloatField, see
    /// <see cref="CityGeneratorAmbienceClipList.BuildVolumeRow"/>), min distance and max distance.
    /// Rows are built with plain controls (ObjectField/Slider/FloatField) instead of PropertyField,
    /// since rows are added to the tree well after CityGeneratorWindow's one-time Bind() call —
    /// same reasoning as <see cref="CityGeneratorCustomPlaceList"/>.
    ///
    /// Writes re-fetch each SerializedProperty by array index at write time (see
    /// <see cref="SetObjectReference"/>/<see cref="SetFloat"/>) rather than closing over a
    /// property captured once in <see cref="Rebuild"/> — mirroring
    /// <see cref="CityGeneratorWeightedPrefabList.SetPercentage"/>. A captured-property closure
    /// used to go stale after the window's <c>TrackSerializedObjectValue</c> callback (or any
    /// sibling field's own edit) called <c>Update()</c> on the shared <c>SerializedObject</c> in
    /// between the row being built and the field being edited — Min/Max Distance, edited last in
    /// each row, were the fields most likely to hit this and silently keep the entry's previous
    /// value instead of what was typed.
    /// </summary>
    internal class CityGeneratorPlazaAudioClipList : VisualElement
    {
        private const float DefaultMinDistance = 10f;
        private const float DefaultMaxDistance = 40f;

        private readonly VisualElement rowsContainer;
        private readonly Action onChanged;
        private SerializedProperty listProperty;

        public CityGeneratorPlazaAudioClipList(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-custom-place-list");

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-custom-place-list__rows");
            Add(rowsContainer);

            var addButton = new Button(AddEntry) { text = "+ Add Plaza Clip", tooltip = "Adds a new plaza clip entry, at volume 1 with the default 10/40m distance range." };
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
            entry.FindPropertyRelative("minDistance").floatValue = DefaultMinDistance;
            entry.FindPropertyRelative("maxDistance").floatValue = DefaultMaxDistance;
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
                float minDistanceValue = entry.FindPropertyRelative("minDistance").floatValue;
                float maxDistanceValue = entry.FindPropertyRelative("maxDistance").floatValue;

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

                row.Add(CityGeneratorAmbienceClipList.BuildVolumeRow(volumeValue, value => SetFloat(capturedIndex, "volume", value)));

                var minDistanceField = new FloatField("Min Distance") { value = minDistanceValue, tooltip = "AudioSource.minDistance: distance at which attenuation starts." };
                minDistanceField.AddToClassList("cg-field-row");
                minDistanceField.RegisterValueChangedCallback(evt => SetFloat(capturedIndex, "minDistance", evt.newValue));
                row.Add(minDistanceField);

                var maxDistanceField = new FloatField("Max Distance") { value = maxDistanceValue, tooltip = "AudioSource.maxDistance: distance at which the clip stops being audible." };
                maxDistanceField.AddToClassList("cg-field-row");
                maxDistanceField.RegisterValueChangedCallback(evt => SetFloat(capturedIndex, "maxDistance", evt.newValue));
                row.Add(maxDistanceField);

                rowsContainer.Add(row);
            }
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
