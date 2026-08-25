using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// A small hand-rolled tab strip (not <c>UnityEngine.UIElements.TabView</c>, which ships its
    /// own <c>unity-tab*</c> stylesheet that would have to be overridden to match the <c>--cg-*</c>
    /// theme, and offers no per-tab error marker): a row of clickable labels above N content
    /// containers, only one of which is visible at a time. Selected tab is persisted in
    /// EditorPrefs, same convention as <see cref="CityGeneratorCard"/>'s open/closed state.
    /// </summary>
    internal class CityGeneratorTabBar
    {
        private const string PrefKey = "CityGenerator.Tab";

        private readonly VisualElement tabsContainer;
        private readonly Dictionary<string, Label> headersById = new();
        private readonly Dictionary<string, VisualElement> contentsById = new();
        private string selectedId;

        public CityGeneratorTabBar(VisualElement tabsContainer)
        {
            this.tabsContainer = tabsContainer;
        }

        /// <summary>Registers a tab whose header lives in the tab bar and whose body is <paramref name="content"/> (already parented under the scroll view).</summary>
        public void AddTab(string id, string label, VisualElement content)
        {
            var header = new Label(label);
            header.AddToClassList("cg-tabs__tab");
            header.RegisterCallback<ClickEvent>(_ => SetSelected(id));
            tabsContainer.Add(header);

            headersById[id] = header;
            contentsById[id] = content;
        }

        /// <summary>Selects <paramref name="id"/>'s tab (defaulting to the first added tab if it was never set) and persists the choice.</summary>
        public void RestoreSelection(string defaultId)
        {
            SetSelected(EditorPrefs.GetString(PrefKey, defaultId));
        }

        public void SetSelected(string id)
        {
            if (!headersById.ContainsKey(id))
                return;

            selectedId = id;
            EditorPrefs.SetString(PrefKey, id);

            foreach (KeyValuePair<string, Label> pair in headersById)
                pair.Value.EnableInClassList("cg-tabs__tab--selected", pair.Key == id);
            foreach (KeyValuePair<string, VisualElement> pair in contentsById)
                pair.Value.style.display = pair.Key == id ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Marks (or clears) the tab's header as containing a validation error — used when the erroring card sits on a tab that isn't currently selected.</summary>
        public void SetHasError(string id, bool hasError)
        {
            if (headersById.TryGetValue(id, out Label header))
                header.EnableInClassList("cg-tabs__tab--error", hasError);
        }
    }
}
