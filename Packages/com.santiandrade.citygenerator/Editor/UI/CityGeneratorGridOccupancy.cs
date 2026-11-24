using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor.UI
{
    /// <summary>Which of the occupancy layers a <see cref="CityGeneratorGridPreview"/> is itself the editor for, so that layer is highlighted as "own" instead of painted as a reference.</summary>
    internal enum CityGeneratorGridOccupancyOwner
    {
        /// <summary>The board edits none of the layers (Define City Area, the Custom Pedestrians graph).</summary>
        None,
        /// <summary>The main plaza editor.</summary>
        Plazas,
        /// <summary>One District entry's picker; the owner index is its position in <c>districts</c>.</summary>
        District,
        /// <summary>One Custom Place entry's picker; the owner index is its position in <c>customPlaces</c>.</summary>
        CustomPlace,
    }

    /// <summary>One positioned Custom Place, as drawn on a board: the whole block or one of its 4 corner slots (same 0-3 convention as <c>CityGeneratorBuildingBuilder.SlotOffsets</c>).</summary>
    internal readonly struct CityGeneratorCustomPlaceMark
    {
        public readonly int Index;
        public readonly Vector2Int Cell;
        public readonly bool OccupiesFullBlock;
        public readonly int CornerSlot;
        /// <summary>The entry's title, or "entry N" when it has none -- same naming as the validator's messages.</summary>
        public readonly string Label;

        public CityGeneratorCustomPlaceMark(int index, Vector2Int cell, bool occupiesFullBlock, int cornerSlot, string label)
        {
            Index = index;
            Cell = cell;
            OccupiesFullBlock = occupiesFullBlock;
            CornerSlot = cornerSlot;
            Label = label;
        }
    }

    /// <summary>
    /// Snapshot of everything that claims a block -- plazas, every District and every positioned
    /// Custom Place -- read straight from the window's serialized <c>CityGeneratorSettings</c> so
    /// every board paints the same picture. Read at paint time rather than cached, so a board never shows
    /// a stale layer after an edit made on a different board.
    /// </summary>
    internal sealed class CityGeneratorGridOccupancy
    {
        public readonly HashSet<Vector2Int> PlazaCells = new();
        public readonly List<HashSet<Vector2Int>> DistrictCells = new();
        /// <summary>Each District's name, or "entry N" when it has none, indexed like <see cref="DistrictCells"/>.</summary>
        public readonly List<string> DistrictLabels = new();
        public readonly List<CityGeneratorCustomPlaceMark> CustomPlaces = new();

        /// <summary>The <c>CityGeneratorSettings</c> property that holds <paramref name="settingsChild"/> (e.g. <c>settings</c> for <c>settings.districts</c>), so a list editor can hand its boards the whole settings without knowing the window's property path.</summary>
        public static SerializedProperty SettingsRootOf(SerializedProperty settingsChild)
        {
            if (settingsChild == null)
                return null;

            string path = settingsChild.propertyPath;
            int separator = path.LastIndexOf('.');
            return separator < 0 ? null : settingsChild.serializedObject.FindProperty(path.Substring(0, separator));
        }

        /// <param name="settings">The serialized <c>CityGeneratorSettings</c>; its <c>SerializedObject</c> must already be up to date.</param>
        public static CityGeneratorGridOccupancy Read(SerializedProperty settings)
        {
            var occupancy = new CityGeneratorGridOccupancy();
            if (settings == null)
                return occupancy;

            SerializedProperty plazaCells = settings.FindPropertyRelative("general.plazaCells");
            if (plazaCells != null)
            {
                for (int i = 0; i < plazaCells.arraySize; i++)
                    occupancy.PlazaCells.Add(plazaCells.GetArrayElementAtIndex(i).vector2IntValue);
            }

            SerializedProperty districts = settings.FindPropertyRelative("districts");
            if (districts != null)
            {
                for (int d = 0; d < districts.arraySize; d++)
                {
                    var cells = new HashSet<Vector2Int>();
                    SerializedProperty district = districts.GetArrayElementAtIndex(d);
                    occupancy.DistrictLabels.Add(EntryLabel(district.FindPropertyRelative("name").stringValue, d));
                    SerializedProperty cellsProperty = district.FindPropertyRelative("cells");
                    for (int c = 0; c < cellsProperty.arraySize; c++)
                        cells.Add(cellsProperty.GetArrayElementAtIndex(c).vector2IntValue);
                    occupancy.DistrictCells.Add(cells);
                }
            }

            SerializedProperty customPlaces = settings.FindPropertyRelative("customPlaces");
            if (customPlaces != null)
            {
                for (int i = 0; i < customPlaces.arraySize; i++)
                {
                    SerializedProperty entry = customPlaces.GetArrayElementAtIndex(i);
                    if (!entry.FindPropertyRelative("positionAssigned").boolValue)
                        continue;

                    occupancy.CustomPlaces.Add(new CityGeneratorCustomPlaceMark(
                        i,
                        entry.FindPropertyRelative("blockCell").vector2IntValue,
                        entry.FindPropertyRelative("occupiesFullBlock").boolValue,
                        entry.FindPropertyRelative("cornerSlot").intValue,
                        EntryLabel(entry.FindPropertyRelative("title").stringValue, i)));
                }
            }

            return occupancy;
        }

        private static string EntryLabel(string name, int index) =>
            string.IsNullOrWhiteSpace(name) ? $"entry {index + 1}" : $"'{name.Trim()}'";

        /// <summary>First District (lowest index, skipping <paramref name="excludedIndex"/>) that claims <paramref name="cell"/>. Two districts claiming the same block is a validator error, so the first one is enough to paint.</summary>
        public bool TryGetDistrict(Vector2Int cell, int excludedIndex, out int districtIndex)
        {
            for (int d = 0; d < DistrictCells.Count; d++)
            {
                if (d != excludedIndex && DistrictCells[d].Contains(cell))
                {
                    districtIndex = d;
                    return true;
                }
            }

            districtIndex = -1;
            return false;
        }
    }
}
