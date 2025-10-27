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
            AddPort(node, "Output", Direction.Output);
            return node;
        }

        public Node CreateGameObjectNode(ProjectScanner.GameObjectStructure go)
        {
            var node = new Node { title = $"GameObject: {go.name}" };
            node.AddToClassList("go-node");
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateComponentNode(ProjectScanner.ComponentStructure comp)
        {
            var node = new Node { title = $"Component: {comp.className}" };
            node.AddToClassList("component-node");
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
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateLibraryNode(string libraryName)
        {
            // 从完整路径中提取库名
            string shortName = ProjectGraphUtils.ExtractLibraryName(libraryName);
            var node = new Node { title = $"Library: {shortName}" };
            node.AddToClassList("library-node");
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
        }

        public Node CreateConstantNode(string name, string value)
        {
            // 限制值的长度
            string displayValue = value.Length > 30 ? value.Substring(0, 27) + "..." : value;
            var node = new Node { title = $" {name}: {displayValue}" };
            node.AddToClassList("constant-node");
            AddPort(node, "Output", Direction.Output);
            AddPort(node, "Input", Direction.Input);
            return node;
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
