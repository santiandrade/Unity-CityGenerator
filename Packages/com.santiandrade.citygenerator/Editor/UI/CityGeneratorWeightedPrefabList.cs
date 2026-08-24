using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// List editor for <c>List&lt;VehicleEntry&gt;</c> / <c>List&lt;PedestrianEntry&gt;</c>: a
    /// thumbnail + percentage slider per entry, a stacked bar showing the live split, a running
    /// total (red when it doesn't sum to 100, matching <see cref="CityGeneratorValidator"/>'s own
    /// tolerance) and a "Normalize to 100%" button. Operates purely on <see cref="SerializedProperty"/>
    /// so it works for both entry types (<c>VehicleEntry</c> is a class, <c>PedestrianEntry</c> a
    /// struct — the property API hides that difference).
    ///
    /// Row/bar-segment elements are only torn down and rebuilt on structural changes (bind, add,
    /// remove, normalize) — see <see cref="Rebuild"/>. A plain percentage edit instead goes
    /// through <see cref="SetPercentage"/>, which only writes the value and nudges the matching
    /// bar segment/totals; rebuilding the row on every tick of a slider drag would destroy the
    /// dragged element mid-drag and break the drag gesture (the new element wouldn't have mouse
    /// capture).
    /// </summary>
    internal class CityGeneratorWeightedPrefabList : VisualElement
    {
        private const float PercentageTolerance = 0.01f;

        private static readonly Color[] BarColors =
        {
            new(0.35f, 0.62f, 0.85f), new(0.85f, 0.55f, 0.35f), new(0.45f, 0.75f, 0.45f),
            new(0.75f, 0.45f, 0.75f), new(0.85f, 0.75f, 0.35f), new(0.4f, 0.75f, 0.75f),
        };

        private readonly VisualElement rowsContainer;
        private readonly VisualElement stackedBar;
        private readonly Label totalLabel;
        private readonly Button normalizeButton;
        private readonly ObjectField addField;
        private readonly Action onChanged;
        private readonly List<VisualElement> barSegments = new();
        private readonly List<Slider> sliders = new();
        private readonly List<FloatField> percentageFields = new();
        private SerializedProperty listProperty;
        private IVisualElementScheduledItem previewPoll;

        public CityGeneratorWeightedPrefabList(Action onChanged = null)
        {
            this.onChanged = onChanged;
            AddToClassList("cg-weighted-list");

            stackedBar = new VisualElement();
            stackedBar.AddToClassList("cg-weighted-list__bar");
            Add(stackedBar);

            var totalsRow = new VisualElement();
            totalsRow.AddToClassList("cg-weighted-list__totals-row");
            totalLabel = new Label();
            totalLabel.AddToClassList("cg-weighted-list__total");
            totalsRow.Add(totalLabel);
            normalizeButton = new Button(Normalize) { text = "Normalize to 100%" };
            normalizeButton.AddToClassList("cg-weighted-list__normalize");
            totalsRow.Add(normalizeButton);
            Add(totalsRow);

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("cg-weighted-list__rows");
            Add(rowsContainer);

            addField = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false };
            addField.AddToClassList("cg-weighted-list__add-field");
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
            SerializedProperty entry = listProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("prefab").objectReferenceValue = evt.newValue;
            entry.FindPropertyRelative("percentage").floatValue = 0f;
            listProperty.serializedObject.ApplyModifiedProperties();

            addField.SetValueWithoutNotify(null);
            Rebuild();
            onChanged?.Invoke();
        }

        private void Rebuild()
        {
            rowsContainer.Clear();
            stackedBar.Clear();
            barSegments.Clear();
            sliders.Clear();
            percentageFields.Clear();
            previewPoll?.Pause();
            if (listProperty == null)
            {
                totalLabel.text = string.Empty;
                return;
            }

            bool anyLoading = false;
            int count = listProperty.arraySize;

            for (int i = 0; i < count; i++)
            {
                SerializedProperty entry = listProperty.GetArrayElementAtIndex(i);
                SerializedProperty prefabProperty = entry.FindPropertyRelative("prefab");
                float percentage = entry.FindPropertyRelative("percentage").floatValue;
                var prefab = prefabProperty.objectReferenceValue as GameObject;
                Color barColor = BarColors[i % BarColors.Length];
                int capturedIndex = i;

                var row = new VisualElement();
                row.AddToClassList("cg-weighted-list__row");

                var swatch = new VisualElement();
                swatch.AddToClassList("cg-weighted-list__swatch");
                swatch.style.backgroundColor = barColor;
                row.Add(swatch);

                var preview = new VisualElement();
                preview.AddToClassList("cg-weighted-list__preview");
                Texture2D previewTexture = prefab != null ? AssetPreview.GetAssetPreview(prefab) : null;
                if (previewTexture == null && prefab != null)
                {
                    previewTexture = AssetPreview.GetMiniThumbnail(prefab);
                    anyLoading |= AssetPreview.IsLoadingAssetPreview(prefab.GetEntityId());
                }
                if (previewTexture != null)
                    preview.style.backgroundImage = new StyleBackground(previewTexture);
                row.Add(preview);

                var label = new Label(prefab != null ? prefab.name : "(missing prefab)");
                label.AddToClassList("cg-weighted-list__label");
                row.Add(label);

                var slider = new Slider(0f, 100f) { value = percentage };
                slider.AddToClassList("cg-weighted-list__slider");
                slider.RegisterValueChangedCallback(evt => SetPercentage(capturedIndex, evt.newValue));
                row.Add(slider);
                sliders.Add(slider);

                var percentageField = new FloatField { value = percentage };
                percentageField.AddToClassList("cg-weighted-list__percentage-field");
                percentageField.RegisterValueChangedCallback(evt => SetPercentage(capturedIndex, evt.newValue));
                row.Add(percentageField);
                percentageFields.Add(percentageField);

                var removeButton = new Button(() => RemoveEntryAt(capturedIndex)) { text = "×" };
                removeButton.AddToClassList("cg-weighted-list__remove");
                row.Add(removeButton);

                rowsContainer.Add(row);

                var segment = new VisualElement();
                segment.AddToClassList("cg-weighted-list__bar-segment");
                segment.style.backgroundColor = barColor;
                segment.style.flexGrow = Mathf.Max(percentage, 0.01f);
                segment.style.display = percentage > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                stackedBar.Add(segment);
                barSegments.Add(segment);
            }

            UpdateTotals();

            if (anyLoading)
            {
                previewPoll ??= schedule.Execute(Rebuild).Every(150);
                previewPoll.Resume();
            }
        }

        /// <summary>Writes one entry's percentage and refreshes just its bar segment plus the shared totals — never rebuilds rows, so it's safe to call on every tick of a slider drag.</summary>
        private void SetPercentage(int index, float value)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            value = Mathf.Clamp(value, 0f, 100f);
            listProperty.serializedObject.Update();
            listProperty.GetArrayElementAtIndex(index).FindPropertyRelative("percentage").floatValue = value;
            listProperty.serializedObject.ApplyModifiedProperties();

            sliders[index].SetValueWithoutNotify(value);
            percentageFields[index].SetValueWithoutNotify(value);
            VisualElement segment = barSegments[index];
            segment.style.flexGrow = Mathf.Max(value, 0.01f);
            segment.style.display = value > 0f ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateTotals();
            onChanged?.Invoke();
        }

        private void UpdateTotals()
        {
            int count = listProperty?.arraySize ?? 0;
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += listProperty.GetArrayElementAtIndex(i).FindPropertyRelative("percentage").floatValue;

            bool balanced = count == 0 || Mathf.Abs(total - 100f) <= PercentageTolerance;
            totalLabel.text = count == 0 ? "No entries" : $"{total:0.##} / 100";
            totalLabel.EnableInClassList("cg-weighted-list__total--error", !balanced);
            normalizeButton.SetEnabled(!balanced && count > 0 && total > 0f);
        }

        private void RemoveEntryAt(int index)
        {
            if (listProperty == null || index < 0 || index >= listProperty.arraySize)
                return;

            listProperty.serializedObject.Update();
            // Unlike a plain List<GameObject> (see CityGeneratorPrefabGrid), the null-then-delete
            // quirk doesn't apply here: the array element type is VehicleEntry/PedestrianEntry
            // (a struct/class), not an object reference itself, so a single delete removes the slot.
            listProperty.DeleteArrayElementAtIndex(index);
            listProperty.serializedObject.ApplyModifiedProperties();
            Rebuild();
            onChanged?.Invoke();
        }

        /// <summary>
        /// Rescales every non-zero percentage proportionally so the total is exactly 100 — plain
        /// proportional scaling, not <see cref="CityGeneratorDistributionUtility.DistributePercentages{T}"/>
        /// (that helper distributes an integer instance *count* across entries; here we're
        /// rebalancing the percentages themselves, which needs float precision, not floor+remainder).
        /// </summary>
        private void Normalize()
        {
            if (listProperty == null || listProperty.arraySize == 0)
                return;

            listProperty.serializedObject.Update();
            float total = 0f;
            var percentageProperties = new List<SerializedProperty>();
            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty percentageProperty = listProperty.GetArrayElementAtIndex(i).FindPropertyRelative("percentage");
                percentageProperties.Add(percentageProperty);
                total += percentageProperty.floatValue;
            }

            if (total <= 0f)
                return;

            float scale = 100f / total;
            for (int i = 0; i < percentageProperties.Count; i++)
                percentageProperties[i].floatValue *= scale;

            listProperty.serializedObject.ApplyModifiedProperties();
            Rebuild();
            onChanged?.Invoke();
        }
    }
}
