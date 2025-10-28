using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace ProjectStructureVisualizer
{
    public class ProjectGraphNodeFactory
    {
        public Node CreateSceneNode(ProjectScanner.SceneStructure scene)
        {
            var node = new Node { title = $"Scene: {scene.sceneName}" };
            node.AddToClassList("scene-node");
            SetNodeColor(node, new Color(0.28f, 0.51f, 0.96f)); // Blue
            AddPort(node, "Output", Direction.Output);
            return node;
        }

        public Node CreateGameObjectNode(ProjectScanner.GameObjectStructure go)
        {
            var node = new Node { title = $"GameObject: {go.name}" };
            node.AddToClassList("go-node");
            SetNodeColor(node, new Color(0.38f, 0.81f, 0.47f)); // Green
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateComponentNode(ProjectScanner.ComponentStructure comp)
        {
            var node = new Node { title = $"Component: {comp.className}" };
            node.AddToClassList("component-node");
            SetNodeColor(node, new Color(0.96f, 0.69f, 0.16f)); // Orange
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateExternalClassNode(ProjectScanner.ExternalClass cls)
        {
            string typePrefix = cls.type switch
            {
                "interface" => "Interface",
                "enum" => "Enum",
                "struct" => "Struct",
                _ => "Class",
            };

            var node = new Node { title = $"{typePrefix}: {cls.namespaceName}.{cls.className}" };
            node.AddToClassList("external-node");

            // Set different colors based on different types
            Color color = cls.type switch
            {
                "interface" => new Color(0.93f, 0.51f, 0.93f), // Magenta - Interface
                "enum" => new Color(0.61f, 0.40f, 0.91f), // Purple - Enum
                "struct" => new Color(0.96f, 0.47f, 0.47f), // Pink - Struct
                _ => new Color(0.65f, 0.65f, 0.65f), // Gray - Class
            };
            SetNodeColor(node, color);

            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateLibraryNode(string libraryName)
        {
            // Extract library name from full path
            string shortName = ProjectGraphUtils.ExtractLibraryName(libraryName);
            var node = new Node { title = $"Library: {shortName}" };
            node.AddToClassList("library-node");
            SetNodeColor(node, new Color(0.85f, 0.85f, 0.30f)); // Yellow
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateConstantNode(string name, string value)
        {
            // Limit the length of the value
            string displayValue = value.Length > 30 ? value.Substring(0, 27) + "..." : value;
            var node = new Node { title = $" {name}: {displayValue}" };
            node.AddToClassList("constant-node");
            SetNodeColor(node, new Color(0.60f, 0.80f, 0.90f)); // Light blue
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        private void SetNodeColor(Node node, Color color)
        {
            node.style.backgroundColor = color;
        }

        private void AddPort(Node node, string portName, Direction direction)
        {
            var port = node.InstantiatePort(
                Orientation.Horizontal,
                direction,
                Port.Capacity.Multi,
                typeof(bool)
            );
            port.portName = portName;

            if (direction == Direction.Output)
                node.outputContainer.Add(port);
            else
                node.inputContainer.Add(port);
        }
    }
}
