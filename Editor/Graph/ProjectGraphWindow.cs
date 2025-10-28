using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YamlDotNet.Serialization;

namespace ProjectStructureVisualizer
{
    public class ProjectGraphWindow : EditorWindow
    {
        private ProjectGraphView graphView;
        private string loadedFilePath = "";
        private TextField searchField;

        [MenuItem("Project Scanner/Project Graph (From YAML)")]
        public static void Open()
        {
            var window = GetWindow<ProjectGraphWindow>("Project Graph");
            window.minSize = new Vector2(1200, 800);
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();

            // Create top toolbar
            CreateToolbar();

            // Initialize empty graph
            graphView = new ProjectGraphView();
            graphView.style.flexGrow = 1;
            rootVisualElement.Add(graphView);
        }

        private void CreateToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f));
            toolbar.style.height = 30;
            toolbar.style.paddingLeft = 5;
            toolbar.style.paddingRight = 5;
            toolbar.style.paddingTop = 3;
            toolbar.style.paddingBottom = 3;
            toolbar.style.alignItems = Align.Center;

            var loadButton = new Button(OnLoadYamlClicked) { text = "📂 choose YAML file" };
            toolbar.Add(loadButton);

            var pathLabel = new Label("No file selected") { name = "pathLabel" };
            pathLabel.style.marginLeft = 10;
            toolbar.Add(pathLabel);

            // Add search field
            CreateSearchField(toolbar);

            rootVisualElement.Add(toolbar);
        }

        private void CreateSearchField(VisualElement parent)
        {
            var searchLabel = new Label("🔍");
            searchLabel.style.marginLeft = 15;
            searchLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            parent.Add(searchLabel);

            searchField = new TextField();
            searchField.value = "";
            searchField.style.width = 200;
            searchField.style.height = 20;
            searchField.RegisterValueChangedCallback(evt =>
            {
                graphView.FilterNodesByName(evt.newValue);
            });
            parent.Add(searchField);

            var clearButton = new Button(() =>
            {
                searchField.value = "";
                graphView.ClearSearchFilter();
            })
            {
                text = "✖",
            };
            clearButton.style.width = 20;
            clearButton.style.height = 20;
            parent.Add(clearButton);

            // Add operation hint
            var hintLabel = new Label("Hint: Hold Alt or middle mouse button to move the view");
            hintLabel.style.marginLeft = 10;
            hintLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            hintLabel.style.fontSize = 11;
            parent.Add(hintLabel);
        }

        private void OnLoadYamlClicked()
        {
            string path = EditorUtility.OpenFilePanel(
                "Select ProjectStructure.yaml",
                Application.dataPath,
                "yaml"
            );
            if (string.IsNullOrEmpty(path))
                return;

            loadedFilePath = path;
            var label = rootVisualElement.Q<Label>("pathLabel");
            label.text = $"Selected file: {Path.GetFileName(path)}";

            LoadAndDisplayYaml(path);
        }

        private void LoadAndDisplayYaml(string path)
        {
            try
            {
                string yaml = File.ReadAllText(path);
                var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                var data = deserializer.Deserialize<ProjectScanner.ProjectStructureData>(yaml);

                graphView.ClearGraph();
                graphView.BuildGraph(data);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to parse YAML: {e.Message}");
            }
        }
    }
}
