using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectStructureVisualizer
{
    public class ProjectGraphBuilder
    {
        private readonly ProjectGraphView graphView;
        private readonly Dictionary<string, Node> nodeById;
        private readonly Dictionary<string, Node> libraryNodes;
        private readonly Dictionary<string, LabeledEdge> edgesByPorts;
        private readonly ProjectGraphNodeFactory nodeFactory;

        // Track the mapping from component node to its constant node list
        private readonly Dictionary<Node, List<Node>> componentToConstants;

        private float currentLibraryY = 0;

        public ProjectGraphBuilder(
            ProjectGraphView graphView,
            Dictionary<string, Node> nodeById,
            Dictionary<string, Node> libraryNodes,
            Dictionary<string, LabeledEdge> edgesByPorts
        )
        {
            this.graphView = graphView;
            this.nodeById = nodeById;
            this.libraryNodes = libraryNodes;
            this.edgesByPorts = edgesByPorts;
            this.nodeFactory = new ProjectGraphNodeFactory();
            this.componentToConstants = graphView.GetComponentToConstantsMapping();
        }

        public void BuildGraph(ProjectScanner.ProjectStructureData data)
        {
            // Clean up the error prefix in the class name
            foreach (var cls in data.externalClasses)
            {
                if (cls.className.StartsWith("Class:"))
                    cls.className = cls.className.Replace("Class:", "").Trim();
            }

            // Build the mapping from nodeId to class name
            var nodeIdToClassName = BuildNodeIdToClassNameMapping(data);

            // Build the scene layer (Scene → GameObject → Component)
            BuildSceneLayer(data, nodeIdToClassName);

            // Build the external class layer
            BuildExternalClassLayer(data);

            // Record the Y position of the external class nodes, the library nodes will start from here
            currentLibraryY = nodeById.Count * 150;

            // Build the call edge layer (methodName as label)
            BuildCallEdges(data, nodeIdToClassName);

            // Build the structure relation edge layer (inheritance, implementation, usage relations)
            BuildStructureEdges(data, nodeIdToClassName);
        }

        private Dictionary<string, string> BuildNodeIdToClassNameMapping(
            ProjectScanner.ProjectStructureData data
        )
        {
            var nodeIdToClassName = new Dictionary<string, string>();
            foreach (var cls in data.externalClasses)
            {
                string clsKey = ProjectGraphUtils.NormalizeFullName(
                    $"{cls.namespaceName}.{cls.className}"
                );
                foreach (var method in cls.methods)
                {
                    if (!string.IsNullOrEmpty(method.nodeId))
                    {
                        nodeIdToClassName[method.nodeId] = clsKey;
                    }
                }
            }
            return nodeIdToClassName;
        }

        private void BuildSceneLayer(
            ProjectScanner.ProjectStructureData data,
            Dictionary<string, string> nodeIdToClassName
        )
        {
            float sceneY = 0;
            foreach (var scene in data.scenes)
            {
                var sceneNode = nodeFactory.CreateSceneNode(scene);
                RegisterNode(scene.sceneName, sceneNode);
                graphView.AddElement(sceneNode);

                sceneNode.SetPosition(new Rect(0, sceneY, 250, 100));

                float goY = sceneY + 50;
                foreach (var go in scene.gameObjects)
                {
                    goY = BuildGameObjectRecursively(go, sceneNode, 350, goY, 0);
                }

                sceneY = goY + 100;
            }
        }

        private void BuildExternalClassLayer(ProjectScanner.ProjectStructureData data)
        {
            float externalX = 1050;
            float externalY = 0;
            foreach (var cls in data.externalClasses)
            {
                if (
                    cls.methods.Count == 0
                    && !cls.isMonoBehaviour
                    && cls.type != "interface"
                    && cls.type != "enum"
                    && cls.type != "struct"
                )
                    continue;

                if (cls.namespaceName.StartsWith("ProjectStructureVisualizer"))
                    continue;

                string clsKey = ProjectGraphUtils.NormalizeFullName(
                    $"{cls.namespaceName}.{cls.className}"
                );

                var node = nodeFactory.CreateExternalClassNode(cls);
                RegisterNode(clsKey, node);
                graphView.AddElement(node);
                node.SetPosition(new Rect(externalX, externalY, 250, 100));
                externalY += 150;
            }
        }

        private void BuildCallEdges(
            ProjectScanner.ProjectStructureData data,
            Dictionary<string, string> nodeIdToClassName
        )
        {
            int successEdges = 0;
            int failedEdges = 0;
            int libraryNodesCreated = 0;
            int methodCallsCreated = 0;
            int fieldRefsCreated = 0;
            int sceneInstancesCreated = 0;

            foreach (var call in data.calls)
            {
                string fromKey = ProjectGraphUtils.ResolveNodeId(
                    call.fromNodeId,
                    nodeIdToClassName
                );
                string toKey = ProjectGraphUtils.ResolveNodeId(call.toNodeId, nodeIdToClassName);

                Node fromNode = FindNearestNode(fromKey);
                Node toNode = FindNearestNode(toKey);

                if (
                    fromNode == null
                    && ProjectGraphUtils.IsExternalLibrary(fromKey)
                    && !nodeById.ContainsKey(fromKey)
                )
                {
                    fromNode = GetOrCreateLibraryNode(fromKey);
                    if (fromNode != null)
                    {
                        libraryNodesCreated++;
                    }
                }

                if (
                    toNode == null
                    && ProjectGraphUtils.IsExternalLibrary(toKey)
                    && !nodeById.ContainsKey(toKey)
                )
                {
                    toNode = GetOrCreateLibraryNode(toKey);
                    if (toNode != null)
                    {
                        libraryNodesCreated++;
                    }
                }

                if (fromNode == null || toNode == null)
                {
                    bool isSystemType =
                        ProjectGraphUtils.IsSystemNamespaceType(fromKey)
                        || ProjectGraphUtils.IsUnitySystemType(fromKey);
                    if (!isSystemType && fromNode == null)
                    {
                        failedEdges++;
                    }
                    if (!isSystemType && toNode == null)
                    {
                        failedEdges++;
                    }
                    continue;
                }

                if (fromNode == toNode)
                {
                    failedEdges++;
                    continue;
                }

                // Find output port
                Port outputPort = null;
                foreach (var port in fromNode.outputContainer.Children())
                {
                    if (port is Port p)
                    {
                        outputPort = p;
                        break;
                    }
                }

                // Find input port
                Port inputPort = null;
                foreach (var port in toNode.inputContainer.Children())
                {
                    if (port is Port p)
                    {
                        inputPort = p;
                        break;
                    }
                }

                if (outputPort == null || inputPort == null)
                {
                    failedEdges++;
                    continue;
                }

                // Select appropriate label text based on call type
                string labelText = "";
                if (call.callType == "field_reference" && !string.IsNullOrEmpty(call.fieldName))
                {
                    labelText = call.fieldName;
                }
                else if (!string.IsNullOrEmpty(call.methodName))
                {
                    labelText = call.methodName;
                }
                else if (!string.IsNullOrEmpty(call.callType))
                {
                    labelText = call.callType;
                }

                // Generate unique identifier for edge (using node title and port type)
                string edgeKey =
                    $"{fromNode.title}_{toNode.title}_{outputPort.portName}_{inputPort.portName}";

                // Check if same edge already exists
                if (edgesByPorts.TryGetValue(edgeKey, out var existingEdge))
                {
                    // If exists, add additional label
                    existingEdge.AddLabel(labelText);
                }
                else
                {
                    // Create new edge
                    var edge = new LabeledEdge(labelText)
                    {
                        output = outputPort,
                        input = inputPort,
                    };
                    outputPort.Connect(edge);
                    inputPort.Connect(edge);
                    graphView.AddElement(edge);
                    edgesByPorts[edgeKey] = edge;
                    successEdges++;
                }

                // Statistics of successful edge types
                if (call.callType == "method")
                    methodCallsCreated++;
                else if (call.callType == "field_reference")
                    fieldRefsCreated++;
                else if (call.callType == "scene_instance")
                    sceneInstancesCreated++;
            }
        }

        private void BuildStructureEdges(
            ProjectScanner.ProjectStructureData data,
            Dictionary<string, string> nodeIdToClassName
        )
        {
            int structureEdgesCreated = 0;
            int structureEdgesFailed = 0;

            foreach (var structRel in data.structureRelations)
            {
                string fromKey = ProjectGraphUtils.ResolveNodeId(
                    structRel.fromNodeId,
                    nodeIdToClassName
                );
                string toKey = ProjectGraphUtils.ResolveNodeId(
                    structRel.toNodeId,
                    nodeIdToClassName
                );

                Node fromNode = FindNearestNode(fromKey);
                Node toNode = FindNearestNode(toKey);

                // Try to create target node that doesn't exist (struct/enum)
                if (
                    toNode == null
                    && ProjectGraphUtils.IsExternalLibrary(toKey)
                    && !nodeById.ContainsKey(toKey)
                )
                {
                    toNode = GetOrCreateLibraryNode(toKey);
                }

                // Try to find struct/enum nodes
                if (toNode == null)
                {
                    toNode = FindNearestNode(structRel.toNodeId);
                }

                if (fromNode == null)
                {
                    structureEdgesFailed++;
                    continue;
                }

                if (toNode == null)
                {
                    structureEdgesFailed++;
                    continue;
                }

                if (fromNode == toNode)
                {
                    continue;
                }

                // Find output and input ports
                Port outputPort = null;
                foreach (var port in fromNode.outputContainer.Children())
                {
                    if (port is Port p)
                    {
                        outputPort = p;
                        break;
                    }
                }

                Port inputPort = null;
                foreach (var port in toNode.inputContainer.Children())
                {
                    if (port is Port p)
                    {
                        inputPort = p;
                        break;
                    }
                }

                if (outputPort == null || inputPort == null)
                {
                    continue;
                }

                // Generate unique identifier for edge
                string edgeKey = $"{fromNode.title}_{toNode.title}_{structRel.relationType}";

                // Check if same edge already exists
                if (!edgesByPorts.ContainsKey(edgeKey))
                {
                    // Create new edge
                    var edge = new LabeledEdge(structRel.relationType)
                    {
                        output = outputPort,
                        input = inputPort,
                    };
                    outputPort.Connect(edge);
                    inputPort.Connect(edge);
                    graphView.AddElement(edge);
                    edgesByPorts[edgeKey] = edge;
                    structureEdgesCreated++;
                }
            }
        }

        private float BuildGameObjectRecursively(
            ProjectScanner.GameObjectStructure go,
            Node parentNode,
            float startX,
            float startY,
            int depth
        )
        {
            // Layout rules: each depth occupies two columns
            float goX = 350 + depth * 600;
            float componentX = 650 + depth * 600;

            // Create GameObject node
            var goNode = nodeFactory.CreateGameObjectNode(go);
            RegisterNode(go.instanceId, goNode);
            graphView.AddElement(goNode);

            // Establish relationship with parent node
            if (depth == 0)
            {
                // Root GameObject connects to Scene
                AddEdge(parentNode, goNode, "contains");
            }
            else
            {
                // Child GameObject connects to parent GameObject
                AddEdge(parentNode, goNode, "child_of");
            }

            // GameObject in odd-numbered column
            goNode.SetPosition(new Rect(goX, startY, 220, 80));

            // Process all components
            float compY = startY;
            float constantX = componentX + 300;
            // Component in even-numbered column (next column at this level)
            foreach (var comp in go.components)
            {
                string compKey = ProjectGraphUtils.NormalizeFullName(comp.className);

                var compNode = nodeFactory.CreateComponentNode(comp);

                // Register Component: use both className and instanceId
                RegisterNode(compKey, compNode);
                if (!string.IsNullOrEmpty(comp.instanceId))
                {
                    RegisterNode(comp.instanceId, compNode);
                }

                graphView.AddElement(compNode);
                AddEdge(goNode, compNode, "has_component");

                compNode.SetPosition(new Rect(componentX, compY, 250, 80));

                // Create nodes for component's constants
                float constantY = compY;
                int constantCount = 0;
                List<Node> constNodes = new List<Node>(); // Track all constant nodes of this component
                foreach (var prop in comp.properties)
                {
                    // Filter out values of reference type
                    string valueStr = prop.Value?.ToString() ?? "null";
                    if (!valueStr.StartsWith("Ref →") && !string.IsNullOrEmpty(valueStr))
                    {
                        var constNode = nodeFactory.CreateConstantNode(prop.Key, valueStr);
                        RegisterNode($"{comp.instanceId}.{prop.Key}", constNode);

                        // Not added to graphView, initial state is hidden (will be added to componentToConstants and displayed only when needed)

                        constNode.SetPosition(new Rect(constantX, constantY, 200, 50));
                        constantY += 60;
                        constantCount++;
                        constNodes.Add(constNode);
                    }
                }

                // Record mapping from component to its constant nodes
                if (constNodes.Count > 0)
                {
                    componentToConstants[compNode] = constNodes;
                }

                // Each component occupies fixed height, regardless of constant nodes (constant nodes are dynamically displayed)
                compY += 110;
            }

            // Calculate total height occupied by components
            float componentHeight = Mathf.Max(0, compY - startY);

            // Recursively process all child objects
            float childStartY = startY;
            foreach (var child in go.children)
            {
                childStartY = BuildGameObjectRecursively(
                    child,
                    goNode,
                    startX,
                    childStartY + Mathf.Max(componentHeight, 120),
                    depth + 1
                );
            }

            // Return the Y position where the next GameObject should be placed
            return Mathf.Max(childStartY, compY) + 20;
        }

        private void RegisterNode(string id, Node node)
        {
            if (!nodeById.ContainsKey(id))
                nodeById[id] = node;
        }

        private void AddEdge(Node from, Node to, string relation)
        {
            if (from == null || to == null)
                return;

            var outputPort = from.outputContainer.Children().OfType<Port>().FirstOrDefault();
            var inputPort = to.inputContainer.Children().OfType<Port>().FirstOrDefault();

            if (outputPort == null || inputPort == null)
                return;

            var edge = new LabeledEdge(relation) { output = outputPort, input = inputPort };
            outputPort.Connect(edge);
            inputPort.Connect(edge);
            graphView.AddElement(edge);
        }

        private Node FindNearestNode(string id)
        {
            return ProjectGraphUtils.FindNearestNode(id, nodeById);
        }

        private Node GetOrCreateLibraryNode(string libraryName)
        {
            // Get library's short name as key
            string shortName = ProjectGraphUtils.ExtractLibraryName(libraryName);

            // First search in libraryNodes
            if (libraryNodes.ContainsKey(shortName))
            {
                return libraryNodes[shortName];
            }

            // Create new library node
            var node = nodeFactory.CreateLibraryNode(libraryName);
            libraryNodes[shortName] = node;

            // Place library nodes below external class nodes, arranged in order
            float libraryX = 1050;

            graphView.AddElement(node);
            node.SetPosition(new Rect(libraryX, currentLibraryY, 250, 80));

            // Increment Y position to reserve space for next library node
            currentLibraryY += 110;

            // Register both full name and short name for convenient lookup
            RegisterNode(libraryName, node);
            RegisterNode(shortName, node);

            return node;
        }
    }
}
