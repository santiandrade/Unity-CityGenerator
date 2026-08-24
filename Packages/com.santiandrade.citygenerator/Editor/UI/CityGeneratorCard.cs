using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityGenerator.Editor.UI
{
    /// <summary>
    /// A collapsible section: a clickable header (icon + title + optional summary badge +
    /// chevron) above a content container. Replaces the old plain
    /// <c>EditorGUILayout.LabelField(title, EditorStyles.boldLabel)</c> sections — every field the
    /// window used to draw flat now lives inside one of these.
    /// Open/closed state is persisted per card name in EditorPrefs, so re-opening the window keeps
    /// whichever sections the user last expanded.
    /// </summary>
    internal class CityGeneratorCard : VisualElement
    {
        private const string PrefKeyPrefix = "CityGenerator.Card.";

        private readonly string cardName;
        private readonly VisualElement header;
        private readonly Label titleLabel;
        private readonly Label badgeLabel;
        private readonly VisualElement chevron;
        private readonly VisualElement content;
        private bool expanded;

        public VisualElement ContentContainer => content;

        public CityGeneratorCard(string cardName, string title, string iconName, bool defaultExpanded)
        {
            this.cardName = cardName;
            AddToClassList("cg-card");

            header = new VisualElement();
            header.AddToClassList("cg-card__header");
            Add(header);

            Texture2D icon = CityGeneratorUiIcons.Get(iconName);
            if (icon != null)
            {
                var iconElement = new VisualElement();
                iconElement.AddToClassList("cg-card__icon");
                iconElement.style.backgroundImage = new StyleBackground(icon);
                header.Add(iconElement);
            }

            titleLabel = new Label(title);
            titleLabel.AddToClassList("cg-card__title");
            header.Add(titleLabel);

            badgeLabel = new Label();
            badgeLabel.AddToClassList("cg-card__badge");
            badgeLabel.style.display = DisplayStyle.None;
            header.Add(badgeLabel);

            chevron = new VisualElement();
            chevron.AddToClassList("cg-card__chevron");
            header.Add(chevron);

            content = new VisualElement();
            content.AddToClassList("cg-card__content");
            Add(content);

            header.RegisterCallback<ClickEvent>(_ => SetExpanded(!expanded));

            SetExpanded(EditorPrefs.GetBool(PrefKeyPrefix + cardName, defaultExpanded), notify: false);
        }

        public void SetBadge(string text)
        {
            badgeLabel.text = text;
            badgeLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetHasError(bool hasError)
        {
            EnableInClassList("cg-card--error", hasError);
        }

        private void SetExpanded(bool value, bool notify = true)
        {
            expanded = value;
            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            chevron.EnableInClassList("cg-card__chevron--expanded", expanded);
            if (notify)
                EditorPrefs.SetBool(PrefKeyPrefix + cardName, expanded);
        }
    }
}
