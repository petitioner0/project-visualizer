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
        private readonly Dictionary<string, int> _labelCounts = new Dictionary<string, int>(); // Mapping from label name to count

        private string _relationType; // Store relation type for subsequent coloring

        public LabeledEdge(string text)
        {
            pickingMode = PickingMode.Position;
            _labelContainer = new VisualElement { pickingMode = PickingMode.Ignore };
            _relationType = text ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                AddOrIncrementLabel(text);
            }
            UpdateLabelDisplay();
            Add(_labelContainer);
        }

        private void SetEdgeColor(string relationType)
        {
            Color edgeColor = relationType.ToLower() switch
            {
                var r when r.Contains("contains") || r.Contains("child_of") => new Color(
                    0.35f,
                    0.80f,
                    0.42f
                ), // Green - Structural relationship
                var r when r.Contains("has_component") => new Color(0.96f, 0.69f, 0.16f), // Orange - Component relationship
                var r when r.Contains("has_property") => new Color(0.60f, 0.80f, 0.90f), // Light blue - Property relationship
                var r when r.Contains("inherits") || r.Contains("implements") => new Color(
                    0.93f,
                    0.51f,
                    0.93f
                ), // Magenta pink - Inheritance relationship
                var r when r.Contains("uses") || r.Contains("method") => new Color(
                    0.65f,
                    0.76f,
                    0.95f
                ), // Light purple-blue - Method usage
                var r when r.Contains("references") || r.Contains("field_reference") => new Color(
                    0.85f,
                    0.85f,
                    0.30f
                ), // Yellow - Reference relationship
                _ => new Color(0.70f, 0.70f, 0.70f), // Gray - Other
            };

            // Set edge color through edgeControl
            if (edgeControl != null)
            {
                edgeControl.inputColor = edgeColor;
                edgeControl.outputColor = edgeColor;
            }
        }

        // Add or increment label count
        public void AddLabel(string text)
        {
            AddOrIncrementLabel(text);
            UpdateLabelDisplay();
        }

        private void AddOrIncrementLabel(string text)
        {
            if (_labelCounts.ContainsKey(text))
            {
                _labelCounts[text]++;
            }
            else
            {
                _labelCounts[text] = 1;
            }
        }

        private void UpdateLabelDisplay()
        {
            // Clear existing labels
            foreach (var label in _labels)
            {
                label.RemoveFromHierarchy();
            }
            _labels.Clear();

            // If it's a structural relationship type, don't create labels
            if (string.IsNullOrEmpty(_relationType))
            {
                return;
            }

            // Create new label display
            foreach (var kvp in _labelCounts)
            {
                string displayText = kvp.Value > 1 ? $"{kvp.Key}×{kvp.Value}" : kvp.Key;
                var label = new Label(displayText)
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        position = Position.Absolute,
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
        }

        public override bool UpdateEdgeControl()
        {
            bool result = base.UpdateEdgeControl();
            var ec = edgeControl;
            if (ec == null || ec.controlPoints == null || ec.controlPoints.Length < 4)
                return result;

            // Set edge color
            SetEdgeColor(_relationType);

            // If no labels, return directly
            if (_labels.Count == 0)
            {
                return result;
            }

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
                float h =
                    label.layout.height > 0 ? label.layout.height : label.resolvedStyle.height;
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
                float h =
                    label.layout.height > 0 ? label.layout.height : label.resolvedStyle.height;
                yOffset += h + 1; // Add spacing
            }

            return result;
        }
    }
}
