using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectStructureVisualizer
{
    public class ProjectGraphView : GraphView
    {
        private readonly Dictionary<string, Node> nodeById = new();
        private readonly Dictionary<string, Node> libraryNodes = new(); // External library node mapping
        private readonly Dictionary<string, LabeledEdge> edgesByPorts = new(); // Track edges (key is fromPortId_toPortId)
        private readonly Dictionary<Node, List<Node>> componentToConstants = new(); // Component node to its constant node list mapping

        // Mouse interaction
        private bool isPanning = false;
        private Vector2 panStart;

        public ProjectGraphView()
        {
            // Operation interaction
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Background grid
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // Support mouse middle key and pan
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<WheelEvent>(OnWheel);

            // Listen to node click events
            RegisterCallback<MouseDownEvent>(OnNodeMouseDown, TrickleDown.TrickleDown);
        }

        public Dictionary<Node, List<Node>> GetComponentToConstantsMapping()
        {
            return componentToConstants;
        }

        private Node lastClickedNode = null;
        private float lastClickTime = -1f;
        private const float DOUBLE_CLICK_INTERVAL = 0.3f; // Double click interval (seconds)

        private void OnNodeMouseDown(MouseDownEvent evt)
        {
            // Only handle left click (not middle key or Alt key)
            if (evt.button == 0 && !evt.altKey)
            {
                // Check if the component node (node with constant nodes) is clicked
                var target = evt.target as VisualElement;
                if (target != null)
                {
                    // Find the node upwards
                    var node = target.GetFirstAncestorOfType<Node>();
                    if (node != null && componentToConstants.ContainsKey(node))
                    {
                        float currentTime = (float)EditorApplication.timeSinceStartup;

                        // Check if it's a double click
                        if (
                            node == lastClickedNode
                            && (currentTime - lastClickTime) < DOUBLE_CLICK_INTERVAL
                        )
                        {
                            ToggleConstantNodesVisibility(node);
                            lastClickedNode = null;
                            lastClickTime = -1f;
                        }
                        else
                        {
                            lastClickedNode = node;
                            lastClickTime = currentTime;
                        }
                    }
                }
            }
        }

        private void ToggleConstantNodesVisibility(Node componentNode)
        {
            if (componentToConstants.TryGetValue(componentNode, out var constNodes))
            {
                // Check if the current is visible: check if the first constant node is in graphView
                bool currentlyVisible = false;
                if (constNodes.Count > 0)
                {
                    var firstNode = constNodes[0];
                    // Check if the node has a parent node (in graphView)
                    currentlyVisible = firstNode.parent != null;
                }

                // Toggle display state
                if (currentlyVisible)
                {
                    // Current visible, need to hide: remove all constant nodes and their edges from graphView
                    foreach (var constNode in constNodes)
                    {
                        // Remove all edges connected to this constant node
                        var edgesToRemove = new List<Edge>();
                        foreach (var edge in edges.OfType<Edge>())
                        {
                            if (edge.input?.node == constNode || edge.output?.node == constNode)
                            {
                                edgesToRemove.Add(edge);
                            }
                        }

                        foreach (var edge in edgesToRemove)
                        {
                            RemoveElement(edge);
                        }

                        // Remove constant node
                        RemoveElement(constNode);
                    }
                }
                else
                {
                    // Current hidden, need to display: add to graphView and create edge
                    foreach (var constNode in constNodes)
                    {
                        // Add constant node to graphView
                        AddElement(constNode);

                        // Create and add edge (componentNode -> constNode)
                        AddEdge(componentNode, constNode, "has_property");
                    }
                }
            }
        }

        private void AddEdge(Node fromNode, Node toNode, string relation)
        {
            if (fromNode == null || toNode == null)
                return;

            var outputPort = fromNode.outputContainer.Children().OfType<Port>().FirstOrDefault();
            var inputPort = toNode.inputContainer.Children().OfType<Port>().FirstOrDefault();

            if (outputPort == null || inputPort == null)
                return;

            var edge = new LabeledEdge(relation) { output = outputPort, input = inputPort };
            outputPort.Connect(edge);
            inputPort.Connect(edge);
            AddElement(edge);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            // Mouse middle key or holding Alt+left key to start panning
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                isPanning = true;
                panStart = evt.mousePosition;
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (isPanning)
            {
                var delta = evt.mousePosition - panStart;
                panStart = evt.mousePosition;
                UpdateViewTransform(
                    viewTransform.position + new Vector3(delta.x, delta.y, 0),
                    viewTransform.scale
                );
                evt.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 2 || evt.altKey)
            {
                isPanning = false;
                evt.StopPropagation();
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            // Support wheel zoom
            var zoom = Mathf.Pow(1.15f, evt.delta.y > 0 ? -1f : 1f);
            UpdateViewTransform(viewTransform.position, viewTransform.scale * zoom);
            evt.StopPropagation();
        }

        public void ClearGraph()
        {
            DeleteElements(graphElements.ToList());

            // Remove all possible MiniMap
            var miniMaps = this.Query<MiniMap>().ToList();
            foreach (var miniMap in miniMaps)
            {
                RemoveElement(miniMap);
            }

            nodeById.Clear();
            libraryNodes.Clear();
            edgesByPorts.Clear();
            componentToConstants.Clear();
        }

        public void BuildGraph(ProjectScanner.ProjectStructureData data)
        {
            ClearGraph();

            // Use ProjectGraphBuilder to build the graph
            var builder = new ProjectGraphBuilder(this, nodeById, libraryNodes, edgesByPorts);
            builder.BuildGraph(data);
        }

        public void FilterNodesByName(string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                ClearSearchFilter();
                return;
            }

            searchText = searchText.ToLower();

            foreach (var element in graphElements.ToList())
            {
                if (element is Node node)
                {
                    // Check if the node title contains the search text
                    bool isMatch = node.title != null && node.title.ToLower().Contains(searchText);

                    if (isMatch)
                    {
                        // Highlight the matched nodes
                        node.style.opacity = 1.0f;
                        node.style.borderLeftWidth = 3;
                        node.style.borderRightWidth = 3;
                        node.style.borderTopWidth = 3;
                        node.style.borderBottomWidth = 3;
                        node.style.borderLeftColor = new Color(1f, 0.84f, 0f); // 金色边框
                        node.style.borderRightColor = new Color(1f, 0.84f, 0f);
                        node.style.borderTopColor = new Color(1f, 0.84f, 0f);
                        node.style.borderBottomColor = new Color(1f, 0.84f, 0f);
                        // Move the node to the front to ensure it is visible
                        element.RemoveFromHierarchy();
                        AddElement(element);
                    }
                    else
                    {
                        // Lower the opacity of the unmatched nodes
                        node.style.opacity = 0.15f;
                        node.style.borderLeftWidth = 0;
                        node.style.borderRightWidth = 0;
                        node.style.borderTopWidth = 0;
                        node.style.borderBottomWidth = 0;
                    }
                }
                else if (element is Edge edge)
                {
                    // Check if the nodes connected by the edge are all visible
                    bool shouldShow = ShouldShowEdge(edge, searchText);

                    if (shouldShow)
                    {
                        edge.style.opacity = 1.0f;
                    }
                    else
                    {
                        edge.style.opacity = 0.15f;
                    }
                }
            }
        }

        public void ClearSearchFilter()
        {
            foreach (var element in graphElements.ToList())
            {
                if (element is Node node)
                {
                    // Restore the default style of the node
                    node.style.opacity = 1.0f;
                    node.style.borderLeftWidth = 0;
                    node.style.borderRightWidth = 0;
                    node.style.borderTopWidth = 0;
                    node.style.borderBottomWidth = 0;
                }
                else if (element is Edge edge)
                {
                    // Restore the default style of the edge
                    edge.style.opacity = 1.0f;
                }
            }
        }

        private bool ShouldShowEdge(Edge edge, string searchText)
        {
            // If the input or output node matches, show this edge
            if (edge.input != null && edge.input.node is Node inputNode)
            {
                if (inputNode.title != null && inputNode.title.ToLower().Contains(searchText))
                {
                    return true;
                }
            }

            if (edge.output != null && edge.output.node is Node outputNode)
            {
                if (outputNode.title != null && outputNode.title.ToLower().Contains(searchText))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
