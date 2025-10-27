using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectStructureVisualizer
{
    public class LabeledEdge : Edge
    {
        private readonly List<Label> _labels = new List<Label>();
        private readonly VisualElement _labelContainer;

        public LabeledEdge(string text)
        {
            pickingMode = PickingMode.Position; 
            _labelContainer = new VisualElement { pickingMode = PickingMode.Ignore };
            AddLabel(text);
            Add(_labelContainer);
        }

        public void AddLabel(string text)
        {
            var label = new Label(text)
            {
                pickingMode = PickingMode.Ignore, // Label does not block interaction
                style =
                {
                    position = Position.Absolute, // Absolute positioning to Edge local coordinates
                    backgroundColor = new StyleColor(new Color(0, 0, 0, 0.5f)),
                    color = Color.white,
                    fontSize = 11,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 2,
                    paddingBottom = 2,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = 1,
                },
            };
            _labels.Add(label);
            _labelContainer.Add(label);
        }

        public override bool UpdateEdgeControl()
        {
            bool result = base.UpdateEdgeControl();
            var ec = edgeControl;
            if (ec == null || ec.controlPoints == null || ec.controlPoints.Length < 4)
                return result;

            Vector2 mid = (ec.controlPoints[1] + ec.controlPoints[2]) * 0.5f;

            // Calculate total label width (maximum width of all labels)
            float maxWidth = 0;
            foreach (var label in _labels)
            {
                float w = label.layout.width > 0 ? label.layout.width : label.resolvedStyle.width;
                if (w > maxWidth)
                    maxWidth = w;
            }

            // Calculate total label height
            float totalHeight = 0;
            foreach (var label in _labels)
            {
                float h = label.layout.height > 0 ? label.layout.height : label.resolvedStyle.height;
                totalHeight += h;
            }

            // Set container position (centered)
            _labelContainer.style.left = mid.x - (maxWidth * 0.5f);
            _labelContainer.style.top = mid.y - (totalHeight * 0.5f);

            // Set position of each label (top to bottom)
            float yOffset = 0;
            foreach (var label in _labels)
            {
                label.style.left = 0;
                label.style.top = yOffset;
                float h = label.layout.height > 0 ? label.layout.height : label.resolvedStyle.height;
                yOffset += h + 1; // Add spacing
            }

            return result;
        }
    }
}
