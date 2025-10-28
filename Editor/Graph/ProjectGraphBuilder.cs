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

        // Layout constants
        private const float X_EXTERNAL = -400f; // X-axis start position for external class nodes (leftmost side)
        private const float X_SCENE = 0f; // X-axis start position for Scene nodes (centered)
        private const float Y_START = 0f; // Y-axis start position for all nodes
        private const float EXTERNAL_Y_GAP = 100f; // Vertical spacing between external class nodes

        private float currentY = Y_START;
        private float externalY = Y_START;
        private float currentX = Y_START;

        // Cluster layout parameters
        private const float GRID_SPACING = 250f; // Horizontal spacing between nodes
        private const float COMPONENT_OFFSET_Y = 150f; // Distance from Component to GameObject
        private const float CIRCLE_RADIUS = 120f; // Circle layout radius
        private const float CONSTANT_OFFSET_Y = 100f; // Distance from Constant to Component

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
            currentX = X_SCENE;
            currentY = Y_START;
            foreach (var scene in data.scenes)
            {
                var sceneNode = nodeFactory.CreateSceneNode(scene);
                RegisterNode(scene.sceneName, sceneNode);
                graphView.AddElement(sceneNode);

                // Arrange scenes horizontally
                sceneNode.SetPosition(new Rect(currentX, currentY, 250, 100));

                // Horizontal cluster layout for GameObjects
                float maxY = BuildGameObjectsHorizontal(scene, sceneNode, currentX, currentY + 200);

                // Move to next Scene position
                currentX += GRID_SPACING;
                currentY = Mathf.Max(currentY, maxY);
            }
        }

        private float BuildGameObjectsHorizontal(
            ProjectScanner.SceneStructure scene,
            Node parentNode,
            float baseX,
            float baseY
        )
        {
            float maxY = baseY;
            foreach (var go in scene.gameObjects)
            {
                float currentGoY = BuildGameObjectCluster(go, parentNode, baseX, baseY, 0);
                maxY = Mathf.Max(maxY, currentGoY);
                baseX += GRID_SPACING * 2; // Each GameObject occupies more horizontal space
            }
            return maxY;
        }

        private void BuildExternalClassLayer(ProjectScanner.ProjectStructureData data)
        {
            externalY = Y_START;
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
                node.SetPosition(new Rect(X_EXTERNAL, externalY, 250, 100));
                externalY += EXTERNAL_Y_GAP;
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
                    failedEdges++;
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
                    // Determine if this relation type should show a label
                    string labelText = ShouldShowLabelForRelation(structRel.relationType)
                        ? structRel.relationType
                        : null;

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
                    structureEdgesCreated++;
                }
            }
        }

        private float BuildGameObjectCluster(
            ProjectScanner.GameObjectStructure go,
            Node parentNode,
            float baseX,
            float baseY,
            int depth
        )
        {
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

            // GameObject position at base position
            goNode.SetPosition(new Rect(baseX, baseY, 250, 80));

            float bottomY = baseY + 80; // GameObject bottom edge

            // Process components in cluster layout
            if (go.components.Count > 0)
            {
                bottomY = baseY + COMPONENT_OFFSET_Y;

                // Get positions for components in circular arrangement
                var componentPositions = GetClusterPositions(go.components.Count, baseX, bottomY);

                for (int i = 0; i < go.components.Count; i++)
                {
                    var comp = go.components[i];
                    var compPos = componentPositions[i];
                    bottomY = Mathf.Max(bottomY, compPos.y + 80);

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
                    compNode.SetPosition(new Rect(compPos.x, compPos.y, 250, 80));

                    // Create constant nodes around this component
                    List<Node> constNodes = new List<Node>();
                    var constantPositions = GetClusterPositions(
                        comp.properties.Count(p =>
                            !p.Value?.ToString().StartsWith("Ref →") ?? false
                        ),
                        compPos.x,
                        compPos.y + CONSTANT_OFFSET_Y
                    );

                    int constIndex = 0;
                    foreach (var prop in comp.properties)
                    {
                        string valueStr = prop.Value?.ToString() ?? "null";
                        if (!valueStr.StartsWith("Ref →") && !string.IsNullOrEmpty(valueStr))
                        {
                            var constNode = nodeFactory.CreateConstantNode(prop.Key, valueStr);
                            RegisterNode($"{comp.instanceId}.{prop.Key}", constNode);

                            var constantPos =
                                constIndex < constantPositions.Count
                                    ? constantPositions[constIndex]
                                    : new Vector2(
                                        compPos.x,
                                        compPos.y + CONSTANT_OFFSET_Y + constIndex * 60
                                    );

                            constNode.SetPosition(new Rect(constantPos.x, constantPos.y, 200, 50));
                            constNodes.Add(constNode);
                            constIndex++;
                        }
                    }

                    if (constNodes.Count > 0)
                    {
                        componentToConstants[compNode] = constNodes;
                    }
                }
            }

            // Process child objects below components
            float childY = bottomY + 100;
            foreach (var child in go.children)
            {
                childY = BuildGameObjectCluster(child, goNode, baseX, childY, depth + 1);
            }

            return Mathf.Max(bottomY, childY);
        }

        private List<Vector2> GetClusterPositions(int count, float centerX, float centerY)
        {
            var positions = new List<Vector2>();

            if (count == 0)
                return positions;

            // Place single node directly in center
            if (count == 1)
            {
                positions.Add(new Vector2(centerX, centerY));
                return positions;
            }

            // Calculate circle layout radius, adaptively based on count
            float radius = CIRCLE_RADIUS;
            if (count > 3)
                radius += (count - 3) * 20; // Increase radius when there are more nodes

            // Arrange all nodes in a circle
            for (int i = 0; i < count; i++)
            {
                // Starting from -π/2, arrange uniformly counterclockwise
                float angle = (float)i / count * 2 * Mathf.PI - Mathf.PI / 2;
                float x = centerX + Mathf.Cos(angle) * radius;
                float y = centerY + Mathf.Sin(angle) * radius;
                positions.Add(new Vector2(x, y));
            }

            return positions;
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

        private bool ShouldShowLabelForRelation(string relationType)
        {
            // These structure relation types don't need to show labels
            if (string.IsNullOrEmpty(relationType))
                return false;

            string lowerRelationType = relationType.ToLower();
            return !lowerRelationType.Contains("contains")
                && !lowerRelationType.Contains("child_of")
                && !lowerRelationType.Contains("has_component")
                && !lowerRelationType.Contains("has_property");
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

            graphView.AddElement(node);
            node.SetPosition(new Rect(X_EXTERNAL, externalY, 250, 80));

            // Increment Y position to reserve space for next library node
            externalY += EXTERNAL_Y_GAP;

            // Register both full name and short name for convenient lookup
            RegisterNode(libraryName, node);
            RegisterNode(shortName, node);

            return node;
        }
    }
}
