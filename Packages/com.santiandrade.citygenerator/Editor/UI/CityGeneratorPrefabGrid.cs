using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// A wrapping grid of asset-preview tiles for a plain <c>List&lt;GameObject&gt;</c> property
    /// (Building Prefabs, Vegetation Prefabs) — replaces the default list drawer Unity would
    /// otherwise use for <c>EditorGUILayout.PropertyField(..., includeChildren: true)</c>, which
    /// showed prefabs as a vertical list of bare object fields with no thumbnail.
    /// </summary>
    internal class CityGeneratorPrefabGrid : VisualElement
    {
        private readonly VisualElement tilesContainer;
        private readonly ObjectField addField;
        private readonly Action onChanged;
        private SerializedProperty listProperty;
        private IVisualElementScheduledItem previewPoll;

        public CityGeneratorPrefabGrid(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-prefab-grid");

            tilesContainer = new VisualElement();
            tilesContainer.AddToClassList("cg-prefab-grid__tiles");
            Add(tilesContainer);

            addField = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false };
            addField.tooltip = "Assign a prefab here to add it to the list below.";
            addField.AddToClassList("cg-prefab-grid__add-field");
            addField.RegisterValueChangedCallback(OnAddFieldChanged);
            Add(addField);
        }

        public void Bind(SerializedProperty property)
        {
            listProperty = property;
            Rebuild();
        }

        private void OnAddFieldChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            if (evt.newValue == null || listProperty == null)
                return;

            listProperty.serializedObject.Update();
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            listProperty.GetArrayElementAtIndex(index).objectReferenceValue = evt.newValue;
            listProperty.serializedObject.ApplyModifiedProperties();

            addField.SetValueWithoutNotify(null);
            Rebuild();
            onChanged?.Invoke();
        }

        private void Rebuild()
        {
            tilesContainer.Clear();
            previewPoll?.Pause();
            if (listProperty == null)
                return;

            bool anyLoading = false;

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty element = listProperty.GetArrayElementAtIndex(i);
                var prefab = element.objectReferenceValue as GameObject;
                int capturedIndex = i;

                var tile = new VisualElement();
                tile.AddToClassList("cg-prefab-grid__tile");
                tile.tooltip = prefab != null ? prefab.name : "(missing prefab)";

                var preview = new VisualElement();
                preview.AddToClassList("cg-prefab-grid__preview");
                Texture2D previewTexture = prefab != null ? AssetPreview.GetAssetPreview(prefab) : null;
                if (previewTexture == null && prefab != null)
                {
                    previewTexture = AssetPreview.GetMiniThumbnail(prefab);
                    anyLoading |= AssetPreview.IsLoadingAssetPreview(prefab.GetEntityId());
                }
                if (previewTexture != null)
                    preview.style.backgroundImage = new StyleBackground(previewTexture);
                tile.Add(preview);

                var label = new Label(prefab != null ? prefab.name : "(missing)");
                label.AddToClassList("cg-prefab-grid__label");
                tile.Add(label);

                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×", tooltip = "Remove this prefab from the list." };
                removeButton.AddToClassList("cg-prefab-grid__remove");
                tile.Add(removeButton);

                tilesContainer.Add(tile);
            }

            if (anyLoading)
            {
                previewPoll ??= schedule.Execute(Rebuild).Every(150);
                previewPoll.Resume();
            }
        }

        private void RemoveEntryAt(int index)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            listProperty.serializedObject.Update();
            // For an object-reference array element, Unity's first DeleteArrayElementAtIndex call
            // only clears the reference to null rather than removing the slot — a second call is
            // needed to actually shrink the array. Harmless no-op on an already-null element.
            if (listProperty.GetArrayElementAtIndex(index).objectReferenceValue != null)
                listProperty.DeleteArrayElementAtIndex(index);
            listProperty.DeleteArrayElementAtIndex(index);
            listProperty.serializedObject.ApplyModifiedProperties();
            Rebuild();
            onChanged?.Invoke();
        }
    }
}
