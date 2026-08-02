using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Myrvo.FlowGraph.Editor
{
    public sealed class FlowGraphView : GraphView
    {
        internal const float NodeWidth = 240f;
        private readonly FlowGraphEditorWindow ownerWindow;
        private readonly FlowNodeSearchWindow searchWindow;
        private readonly Dictionary<string, FlowNodeView> nodeViewsById = new Dictionary<string, FlowNodeView>();
        private readonly MiniMap miniMap;
        private Blackboard blackboard;
        private FlowGraphAsset graphAsset;
        private bool isSpacePanning;
        private bool isPointerPanning;
        private Vector2 panPointerPosition;

        public FlowGraphView(FlowGraphEditorWindow ownerWindow)
        {
            this.ownerWindow = ownerWindow;
            style.flexGrow = 1f;
            focusable = true;
            // This disables GraphView's built-in keyboard shortcut set, including
            // Space-to-create-node. Canvas panning is handled below instead.
            isReframable = false;
            Insert(0, new GridBackground());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            // Space is reserved for the familiar canvas-pan gesture. GraphView
            // otherwise uses it as a node-creation shortcut.
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);

            miniMap = new MiniMap { anchored = true };
            miniMap.SetPosition(new Rect(0f, 0f, 180f, 110f));
            Add(miniMap);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            searchWindow = ScriptableObject.CreateInstance<FlowNodeSearchWindow>();
            searchWindow.Initialize(this);
            nodeCreationRequest = context =>
            {
                searchWindow.ClearConnection();
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), searchWindow);
            };
            graphViewChanged += OnGraphViewChanged;
            viewTransformChanged += OnViewTransformChanged;
            SetupBlackboard();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Space)
            {
                return;
            }

            isSpacePanning = true;
            evt.StopImmediatePropagation();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode != KeyCode.Space)
            {
                return;
            }

            isSpacePanning = false;
            EndSpacePan();
            evt.StopImmediatePropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!isSpacePanning || evt.button != 0)
            {
                return;
            }

            isPointerPanning = true;
            panPointerPosition = new Vector2(evt.position.x, evt.position.y);
            evt.StopImmediatePropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isPointerPanning)
            {
                return;
            }

            Vector2 pointerPosition = new Vector2(evt.position.x, evt.position.y);
            Vector2 delta = pointerPosition - panPointerPosition;
            panPointerPosition = pointerPosition;
            UpdateViewTransform(viewTransform.position + new Vector3(delta.x, delta.y, 0f), viewTransform.scale);
            evt.StopImmediatePropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!isPointerPanning)
            {
                return;
            }

            EndSpacePan();
            evt.StopImmediatePropagation();
        }

        private void EndSpacePan()
        {
            isPointerPanning = false;
        }

        public FlowNodeModel SelectedNode { get; private set; }

        internal void ExecuteNode(FlowNodeModel node)
        {
            ownerWindow.ExecuteNode(node);
        }

        internal void ToggleNodeLock(FlowNodeModel node)
        {
            if (graphAsset == null || node == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, node.Locked ? "Unlock Flow Node" : "Lock Flow Node");
            node.Locked = !node.Locked;
            EditorUtility.SetDirty(graphAsset);
            RefreshNodeViews(true);
            ownerWindow.NotifySelectionChanged();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatiblePorts = new List<Port>();
            ports.ForEach(port =>
            {
                if (port == startPort || port.node == startPort.node || port.direction == startPort.direction)
                {
                    return;
                }

                FlowPortModel startPortModel = ((FlowPortView)startPort).PortModel;
                FlowPortModel candidatePortModel = ((FlowPortView)port).PortModel;
                if (ArePortTypesCompatible(startPortModel, candidatePortModel))
                {
                    compatiblePorts.Add(port);
                }
            });

            return compatiblePorts;
        }

        public void Bind(FlowGraphAsset asset)
        {
            graphAsset = asset;
            SelectedNode = null;
            List<GraphElement> removableElements = graphElements.Where(element => element != miniMap && element != blackboard).ToList();
            foreach (GraphElement removableElement in removableElements)
            {
                RemoveElement(removableElement);
            }
            nodeViewsById.Clear();
            RebuildBlackboard();

            if (graphAsset == null)
            {
                return;
            }

            graphAsset.EnsureInitialized();

            foreach (FlowNodeModel node in graphAsset.Nodes)
            {
                FlowNodeView nodeView = new FlowNodeView(node, this);
                nodeViewsById[node.Id] = nodeView;
                AddElement(nodeView);
            }

            foreach (FlowEdgeModel edge in graphAsset.Edges)
            {
                CreateEdgeView(edge);
            }

            RebuildBlackboard();
            UpdateViewTransform(graphAsset.ViewPosition, graphAsset.ViewScale);
        }

        public void RefreshNodeViews()
        {
            RefreshNodeViews(false);
        }

        public bool RefreshNodeViews(bool force)
        {
            bool changed = false;
            foreach ((_, FlowNodeView nodeView) in nodeViewsById)
            {
                changed |= nodeView.RefreshFromModel(force);
            }

            return changed;
        }

        public void CreateNode(Type nodeType, Vector2 screenPosition)
        {
            if (graphAsset == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Create Flow Node");
            CreateNodeView(nodeType, screenPosition);
            ownerWindow.NotifySelectionChanged();
        }

        internal void OpenNodePickerForPort(FlowPortView port, Vector2 graphPosition)
        {
            if (port == null)
            {
                return;
            }

            searchWindow.BeginConnection(port);
            Vector2 screenPosition = ownerWindow.position.position + graphPosition;
            SearchWindow.Open(new SearchWindowContext(screenPosition), searchWindow);
        }

        internal void CreateNodeAndConnect(Type nodeType, Vector2 screenPosition, FlowPortView existingPort)
        {
            if (graphAsset == null || existingPort == null || existingPort.NodeView.NodeModel.Locked)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Create and Connect Flow Node");
            FlowNodeView nodeView = CreateNodeView(nodeType, screenPosition);
            FlowPortView createdPort = FindFirstCompatiblePort(nodeView, existingPort);
            if (createdPort == null)
            {
                return;
            }

            FlowPortView output = existingPort.direction == Direction.Output ? existingPort : createdPort;
            FlowPortView input = existingPort.direction == Direction.Input ? existingPort : createdPort;
            RemoveSingleInputConnection(input);
            FlowEdgeModel edgeModel = graphAsset.AddEdge(
                output.NodeView.NodeModel.Id,
                output.PortModel.Id,
                input.NodeView.NodeModel.Id,
                input.PortModel.Id);
            graphAsset.MarkAllNodesDirty();
            EditorUtility.SetDirty(graphAsset);
            CreateEdgeView(edgeModel);
            ownerWindow.NotifySelectionChanged();
        }

        internal static bool ArePortTypesCompatible(FlowPortModel first, FlowPortModel second)
        {
            return first.ValueType == second.ValueType;
        }

        private FlowNodeView CreateNodeView(Type nodeType, Vector2 screenPosition)
        {
            if (graphAsset == null)
            {
                return null;
            }

            Vector2 localPosition = contentViewContainer.WorldToLocal(screenPosition - ownerWindow.position.position);
            FlowNodeModel node = graphAsset.CreateNode(nodeType, localPosition);
            EditorUtility.SetDirty(graphAsset);
            FlowNodeView nodeView = new FlowNodeView(node, this);
            nodeViewsById[node.Id] = nodeView;
            AddElement(nodeView);
            return nodeView;
        }

        private static FlowPortView FindFirstCompatiblePort(FlowNodeView nodeView, FlowPortView existingPort)
        {
            IEnumerable<FlowPortModel> candidateModels = existingPort.direction == Direction.Output
                ? nodeView.NodeModel.InputPorts
                : nodeView.NodeModel.OutputPorts;
            FlowPortDirection direction = existingPort.direction == Direction.Output ? FlowPortDirection.Input : FlowPortDirection.Output;
            foreach (FlowPortModel candidateModel in candidateModels)
            {
                if (ArePortTypesCompatible(existingPort.PortModel, candidateModel))
                {
                    return nodeView.GetPort(candidateModel.Id, direction);
                }
            }

            return null;
        }

        private void RemoveSingleInputConnection(FlowPortView input)
        {
            if (input.PortModel.AllowsMultipleConnections)
            {
                return;
            }

            foreach (Edge connection in input.connections.ToList())
            {
                if (connection.userData is string edgeId)
                {
                    graphAsset.RemoveEdge(edgeId);
                }

                RemoveElement(connection);
            }
        }

        internal void SelectNode(FlowNodeModel node)
        {
            SelectedNode = node;
            ownerWindow.NotifySelectionChanged();
        }

        internal void OpenNodeContent(FlowNodeModel node)
        {
            ownerWindow.OpenNodeContent(node);
        }

        internal void SyncNodePosition(FlowNodeModel node, Rect rect)
        {
            if (graphAsset == null || node.Locked)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Move Flow Node");
            node.Position = rect.position;
            EditorUtility.SetDirty(graphAsset);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphAsset == null)
            {
                return graphViewChange;
            }

            if (graphViewChange.edgesToCreate != null)
            {
                graphViewChange.edgesToCreate = graphViewChange.edgesToCreate
                    .Where(edge => !((FlowPortView)edge.output).NodeView.NodeModel.Locked &&
                                   !((FlowPortView)edge.input).NodeView.NodeModel.Locked)
                    .ToList();

                if (graphViewChange.edgesToCreate.Count == 0)
                {
                    return graphViewChange;
                }

                Undo.RecordObject(graphAsset, "Connect Flow Nodes");
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    FlowPortView output = (FlowPortView)edge.output;
                    FlowPortView input = (FlowPortView)edge.input;
                    FlowEdgeModel edgeModel = graphAsset.AddEdge(output.NodeView.NodeModel.Id, output.PortModel.Id, input.NodeView.NodeModel.Id, input.PortModel.Id);
                    edge.userData = edgeModel.Id;
                }

                graphAsset.MarkAllNodesDirty();
                EditorUtility.SetDirty(graphAsset);
            }

            if (graphViewChange.elementsToRemove != null)
            {
                graphViewChange.elementsToRemove = graphViewChange.elementsToRemove
                    .Where(graphElement => !IsLockedGraphElement(graphElement))
                    .ToList();

                if (graphViewChange.elementsToRemove.Count == 0)
                {
                    return graphViewChange;
                }

                Undo.RecordObject(graphAsset, "Remove Flow Graph Elements");
                foreach (GraphElement graphElement in graphViewChange.elementsToRemove)
                {
                    switch (graphElement)
                    {
                        case FlowNodeView nodeView:
                            graphAsset.RemoveNode(nodeView.NodeModel.Id);
                            nodeViewsById.Remove(nodeView.NodeModel.Id);
                            if (ReferenceEquals(SelectedNode, nodeView.NodeModel))
                            {
                                SelectedNode = null;
                            }

                            break;
                        case Edge edge:
                            if (edge.userData is string edgeId)
                            {
                                graphAsset.RemoveEdge(edgeId);
                            }

                            break;
                    }
                }

                graphAsset.MarkAllNodesDirty();
                EditorUtility.SetDirty(graphAsset);
                ownerWindow.NotifySelectionChanged();
            }

            return graphViewChange;
        }

        private static bool IsLockedGraphElement(GraphElement graphElement)
        {
            return graphElement switch
            {
                FlowNodeView nodeView => nodeView.NodeModel.Locked,
                Edge edge => ((FlowPortView)edge.output).NodeView.NodeModel.Locked || ((FlowPortView)edge.input).NodeView.NodeModel.Locked,
                _ => false
            };
        }

        private void CreateEdgeView(FlowEdgeModel edgeModel)
        {
            if (!nodeViewsById.TryGetValue(edgeModel.OutputNodeId, out FlowNodeView outputNode) ||
                !nodeViewsById.TryGetValue(edgeModel.InputNodeId, out FlowNodeView inputNode))
            {
                return;
            }

            FlowPortView outputPort = outputNode.GetPort(edgeModel.OutputPortId, FlowPortDirection.Output);
            FlowPortView inputPort = inputNode.GetPort(edgeModel.InputPortId, FlowPortDirection.Input);
            if (outputPort == null || inputPort == null || outputPort.direction != Direction.Output || inputPort.direction != Direction.Input)
            {
                return;
            }

            Edge edge = outputPort.ConnectTo(inputPort);
            edge.userData = edgeModel.Id;
            AddElement(edge);
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            if (graphAsset == null)
            {
                return;
            }

            graphAsset.SetViewTransform(viewTransform.position, viewTransform.scale);
            EditorUtility.SetDirty(graphAsset);
        }

        private void SetupBlackboard()
        {
            blackboard = new Blackboard(this)
            {
                subTitle = "Exposed Variables",
                title = "Variables",
                scrollable = true
            };
            blackboard.style.display = DisplayStyle.None;
            blackboard.addItemRequested = _ => AddVariable();
            blackboard.editTextRequested = (_, element, newName) =>
            {
                if (element.userData is FlowVariableModel variable)
                {
                    Undo.RecordObject(graphAsset, "Rename Flow Variable");
                    variable.Name = newName;
                    graphAsset.MarkAllNodesDirty();
                    EditorUtility.SetDirty(graphAsset);
                    RebuildBlackboard();
                    ownerWindow.NotifySelectionChanged();
                }
            };

            blackboard.SetPosition(new Rect(16f, 200f, 260f, 300f));
            Add(blackboard);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            PositionMiniMap();
        }

        private void PositionMiniMap()
        {
            if (miniMap == null || layout.width <= 0f || layout.height <= 0f)
            {
                return;
            }

            const float margin = 12f;
            const float topOffset = 28f;
            Rect currentRect = miniMap.GetPosition();
            float width = currentRect.width > 0f ? currentRect.width : 180f;
            float height = currentRect.height > 0f ? currentRect.height : 110f;
            float x = Mathf.Max(margin, layout.width - width - margin);
            float y = Mathf.Max(topOffset, layout.height - height - margin);
            miniMap.SetPosition(new Rect(x, y, width, height));
        }

        private void RebuildBlackboard()
        {
            if (blackboard == null)
            {
                return;
            }

            blackboard.Clear();
            if (graphAsset == null)
            {
                return;
            }

            foreach (FlowVariableModel variable in graphAsset.Variables)
            {
                BlackboardField field = new BlackboardField
                {
                    text = string.IsNullOrWhiteSpace(variable.Name) ? "Variable" : variable.Name,
                    typeText = variable.InitialValue.ValueType.ToString()
                };
                field.userData = variable;

                VisualElement propertyView = new VisualElement();
                propertyView.Add(new Label($"Default: {variable.InitialValue.GetValue() ?? "null"}"));

                BlackboardRow row = new BlackboardRow(field, propertyView)
                {
                    userData = variable
                };

                blackboard.Add(row);
            }
        }

        private void AddVariable()
        {
            if (graphAsset == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Add Flow Variable");
            FlowSerializedValue initialValue = new FlowSerializedValue();
            initialValue.ValueType = FlowValueType.String;
            initialValue.StringValue = string.Empty;
            graphAsset.AddVariable($"Variable {graphAsset.Variables.Count + 1}", initialValue);
            graphAsset.MarkAllNodesDirty();
            EditorUtility.SetDirty(graphAsset);
            RebuildBlackboard();
            ownerWindow.NotifySelectionChanged();
        }

        internal sealed class FlowEdgeConnectorListener : IEdgeConnectorListener
        {
            private readonly GraphView graphView;

            public FlowEdgeConnectorListener(GraphView graphView)
            {
                this.graphView = graphView;
            }

            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                Port draggedPort = edge.output ?? edge.input;
                if (!(draggedPort is FlowPortView port))
                {
                    return;
                }

                FlowGraphView flowGraphView = graphView as FlowGraphView;
                if (flowGraphView == null)
                {
                    return;
                }

                flowGraphView.OpenNodePickerForPort(port, position);
            }

            public void OnDrop(GraphView _, Edge edge)
            {
                List<Edge> edgesToCreate = new List<Edge> { edge };
                List<GraphElement> elementsToRemove = new List<GraphElement>();

                if (edge.input.capacity == Port.Capacity.Single)
                {
                    foreach (Edge connection in edge.input.connections)
                    {
                        if (connection != edge)
                        {
                            elementsToRemove.Add(connection);
                        }
                    }
                }

                if (edge.output.capacity == Port.Capacity.Single)
                {
                    foreach (Edge connection in edge.output.connections)
                    {
                        if (connection != edge)
                        {
                            elementsToRemove.Add(connection);
                        }
                    }
                }

                if (graphView.graphViewChanged != null)
                {
                    GraphViewChange graphViewChange = new GraphViewChange
                    {
                        edgesToCreate = edgesToCreate,
                        elementsToRemove = elementsToRemove
                    };
                    graphViewChange = graphView.graphViewChanged(graphViewChange);
                    edgesToCreate = graphViewChange.edgesToCreate;
                    elementsToRemove = graphViewChange.elementsToRemove;
                }

                foreach (GraphElement element in elementsToRemove)
                {
                    if (element is Edge connection)
                    {
                        connection.output?.Disconnect(connection);
                        connection.input?.Disconnect(connection);
                    }

                    graphView.RemoveElement(element);
                }

                foreach (Edge newEdge in edgesToCreate)
                {
                    newEdge.output?.Connect(newEdge);
                    newEdge.input?.Connect(newEdge);
                    graphView.AddElement(newEdge);
                }
            }
        }
    }

    internal sealed class FlowPortView : Port
    {
        public FlowPortView(FlowPortModel portModel, Direction direction, Port.Capacity capacity, IEdgeConnectorListener edgeConnectorListener) : base(Orientation.Horizontal, direction, capacity, typeof(object))
        {
            PortModel = portModel;
            m_EdgeConnector = new EdgeConnector<Edge>(edgeConnectorListener);
            this.AddManipulator(m_EdgeConnector);
            portName = portModel.DisplayName;
        }

        public FlowPortModel PortModel { get; }
        public FlowNodeView NodeView => (FlowNodeView)node;
    }

    internal sealed class FlowNodeView : Node
    {
        private readonly Dictionary<string, FlowPortView> portsByLookupKey = new Dictionary<string, FlowPortView>();
        private readonly FlowGraphView graphView;
        private readonly IEdgeConnectorListener edgeConnectorListener;
        private readonly Image previewImage;
        private readonly VisualElement statusIndicator;
        private readonly VisualElement headerControlsContainer;
        private readonly Button expandButton;
        private readonly Button executeButton;
        private readonly Button lockButton;
        private Texture2D previewTexture;
        private string displayedTitle;
        private string displayedTooltip;
        private string displayedPreviewData;
        private FlowNodeExecutionStatus displayedStatus;
        private bool displayedLocked;
        private bool hasDisplayedState;
        private const float PreviewHorizontalPadding = 8f;
        private const float PreviewMinHeight = 90f;
        private const float PreviewMaxHeight = 180f;
        private const float LetterboxAspectThreshold = 2.4f;
        private const float LetterboxHeight = 96f;

        public FlowNodeView(FlowNodeModel nodeModel, FlowGraphView graphView)
        {
            NodeModel = nodeModel;
            this.graphView = graphView;
            edgeConnectorListener = new FlowGraphView.FlowEdgeConnectorListener(graphView);
            title = nodeModel.Title;
            viewDataKey = nodeModel.Id;
            SetPosition(new Rect(nodeModel.Position, new Vector2(FlowGraphView.NodeWidth, 120f)));
            style.width = FlowGraphView.NodeWidth;
            style.minWidth = FlowGraphView.NodeWidth;
            style.maxWidth = FlowGraphView.NodeWidth;
            capabilities |= Capabilities.Deletable | Capabilities.Movable | Capabilities.Selectable | Capabilities.Copiable;
            titleContainer.style.alignItems = Align.Center;
            Label titleLabelElement = titleContainer.Q<Label>();
            if (titleLabelElement != null)
            {
                titleLabelElement.style.flexGrow = 1f;
                titleLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                titleLabelElement.style.flexShrink = 1f;
            }

            foreach (FlowPortModel portModel in nodeModel.InputPorts)
            {
                FlowPortView port = CreatePort(portModel, Direction.Input);
                inputContainer.Add(port);
                portsByLookupKey[BuildPortLookupKey(portModel.Id, FlowPortDirection.Input)] = port;
            }

            foreach (FlowPortModel portModel in nodeModel.OutputPorts)
            {
                FlowPortView port = CreatePort(portModel, Direction.Output);
                outputContainer.Add(port);
                portsByLookupKey[BuildPortLookupKey(portModel.Id, FlowPortDirection.Output)] = port;
            }

            headerControlsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    flexShrink = 0f,
                    marginLeft = 6f
                }
            };

            statusIndicator = new VisualElement
            {
                style =
                {
                    width = 10f,
                    height = 10f,
                    minWidth = 10f,
                    minHeight = 10f,
                    borderTopLeftRadius = 5f,
                    borderTopRightRadius = 5f,
                    borderBottomLeftRadius = 5f,
                    borderBottomRightRadius = 5f,
                    marginLeft = 10f,
                    marginRight = 6f,
                    marginTop = 0f,
                    marginBottom = 0f,
                    alignSelf = Align.Center
                }
            };

            // The built-in GraphView expander is styled as a direct child of the
            // title container. Moving it into our custom header made its icon
            // invisible, and GraphView only creates it for expandable nodes.
            // Use an explicit toggle so every flow node has the same control.
            if (titleButtonContainer != null)
            {
                titleButtonContainer.style.display = DisplayStyle.None;
            }

            expandButton = CreateHeaderButton(string.Empty, "Collapse node", ToggleExpanded);
            executeButton = CreateHeaderButton(string.Empty, "Execute this node", ExecuteNode);
            lockButton = CreateHeaderButton(string.Empty, "Lock node outputs", ToggleLock);
            expandButton.AddToClassList("myrvo-flow-node-icon");
            executeButton.AddToClassList("myrvo-flow-node-icon");
            lockButton.AddToClassList("myrvo-flow-node-icon");
            headerControlsContainer.Add(expandButton);
            headerControlsContainer.Add(executeButton);
            headerControlsContainer.Add(lockButton);
            titleContainer.Insert(0, statusIndicator);
            titleContainer.Add(headerControlsContainer);

            previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleAndCrop,
                style =
                {
                    marginLeft = PreviewHorizontalPadding,
                    marginRight = PreviewHorizontalPadding,
                    marginBottom = 8f,
                    width = FlowGraphView.NodeWidth - (PreviewHorizontalPadding * 2f),
                    maxWidth = FlowGraphView.NodeWidth - (PreviewHorizontalPadding * 2f),
                    height = PreviewMinHeight,
                    display = DisplayStyle.None,
                    backgroundColor = new Color(0.11f, 0.11f, 0.11f, 1f),
                    borderTopLeftRadius = 4f,
                    borderTopRightRadius = 4f,
                    borderBottomLeftRadius = 4f,
                    borderBottomRightRadius = 4f
                }
            };
            extensionContainer.Add(previewImage);

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RefreshExpandedState();
            RefreshExpandButton();
            RefreshExecuteButton();
            RefreshLockButton();
            RefreshPorts();
            RefreshFromModel(true);
        }

        public FlowNodeModel NodeModel { get; }

        public FlowPortView GetPort(string portId, FlowPortDirection direction)
        {
            return portsByLookupKey.TryGetValue(BuildPortLookupKey(portId, direction), out FlowPortView port) ? port : null;
        }

        public override void OnSelected()
        {
            base.OnSelected();
            graphView.SelectNode(NodeModel);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            if (NodeModel.Position != newPos.position)
            {
                graphView.SyncNodePosition(NodeModel, newPos);
            }
        }

        public bool RefreshFromModel(bool force = false)
        {
            string nextTitle = NodeModel.Title;
            FlowNodeExecutionStatus nextStatus = NodeModel.Status;
            bool nextLocked = NodeModel.Locked;
            string nextTooltip = GetStatusTooltip(NodeModel);
            string nextPreviewData = NodeModel.LastPreviewData;
            bool previewChanged = !hasDisplayedState && !string.IsNullOrWhiteSpace(nextPreviewData) ||
                                  (hasDisplayedState && !ReferenceEquals(displayedPreviewData, nextPreviewData));
            bool chromeChanged = force || !hasDisplayedState ||
                                 !string.Equals(displayedTitle, nextTitle, StringComparison.Ordinal) ||
                                 displayedStatus != nextStatus ||
                                 displayedLocked != nextLocked ||
                                 !string.Equals(displayedTooltip, nextTooltip, StringComparison.Ordinal);
            if (!previewChanged && !chromeChanged)
            {
                return false;
            }

            if (chromeChanged)
            {
                title = nextTitle;
                statusIndicator.style.backgroundColor = GetStatusColor(nextStatus);
                statusIndicator.tooltip = nextTooltip;
                RefreshExecuteButton();
                RefreshLockButton();
            }

            if (previewChanged)
            {
                previewTexture = FlowPreviewTextureCache.Get(nextPreviewData, "FlowGraphNodePreview");
                previewImage.image = previewTexture;
                ApplyPreviewLayout(previewTexture);
            }

            displayedTitle = nextTitle;
            displayedStatus = nextStatus;
            displayedLocked = nextLocked;
            displayedTooltip = nextTooltip;
            displayedPreviewData = nextPreviewData;
            hasDisplayedState = true;
            if (previewChanged)
            {
                RefreshExpandedState();
                RefreshExpandButton();
            }

            return true;
        }

        private void ApplyPreviewLayout(Texture2D texture)
        {
            if (texture == null)
            {
                previewImage.style.display = DisplayStyle.None;
                return;
            }

            float aspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            float targetWidth = FlowGraphView.NodeWidth - (PreviewHorizontalPadding * 2f);
            bool useLetterbox = aspectRatio >= LetterboxAspectThreshold;
            float targetHeight = useLetterbox
                ? LetterboxHeight
                : Mathf.Clamp(targetWidth / Mathf.Max(0.01f, aspectRatio), PreviewMinHeight, PreviewMaxHeight);

            previewImage.scaleMode = useLetterbox ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop;
            previewImage.style.height = targetHeight;
            previewImage.style.display = DisplayStyle.Flex;
        }

        private static string GetStatusTooltip(FlowNodeModel nodeModel)
        {
            return nodeModel.Status switch
            {
                FlowNodeExecutionStatus.Error when !string.IsNullOrWhiteSpace(nodeModel.LastError) => $"Error: {nodeModel.LastError}",
                FlowNodeExecutionStatus.Dirty => "Dirty",
                FlowNodeExecutionStatus.Running => "Running",
                FlowNodeExecutionStatus.Success when !string.IsNullOrWhiteSpace(nodeModel.LastExecutionSummary) => nodeModel.LastExecutionSummary,
                _ => nodeModel.Status.ToString()
            };
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2)
            {
                return;
            }

            graphView.SelectNode(NodeModel);
            graphView.OpenNodeContent(NodeModel);
            evt.StopPropagation();
        }

        private Button CreateHeaderButton(string text, string tooltip, Action onClick)
        {
            Button button = new Button(onClick)
            {
                text = text,
                tooltip = tooltip,
                focusable = false
            };
            // Keep these as icon controls, not default Unity buttons.
            button.style.backgroundColor = Color.clear;
            button.style.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            button.style.fontSize = 12f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            return button;
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            RefreshExpandedState();
            RefreshExpandButton();
        }

        private void RefreshExpandButton()
        {
            expandButton.EnableInClassList("myrvo-flow-node-chevron--expanded", expanded);
            expandButton.EnableInClassList("myrvo-flow-node-chevron--collapsed", !expanded);
            expandButton.tooltip = expanded ? "Collapse node" : "Expand node";
        }

        private void ExecuteNode()
        {
            graphView.SelectNode(NodeModel);
            graphView.ExecuteNode(NodeModel);
        }

        private void RefreshExecuteButton()
        {
            bool isRunning = NodeModel.Status == FlowNodeExecutionStatus.Running;
            executeButton.EnableInClassList("myrvo-flow-node-play", !isRunning);
            executeButton.EnableInClassList("myrvo-flow-node-stop", isRunning);
            executeButton.tooltip = isRunning
                ? "Execution in progress (cancellation is not available)"
                : "Execute this node";
        }

        private void ToggleLock()
        {
            graphView.ToggleNodeLock(NodeModel);
        }

        private void RefreshLockButton()
        {
            lockButton.EnableInClassList("myrvo-flow-node-lock--locked", NodeModel.Locked);
            lockButton.EnableInClassList("myrvo-flow-node-lock--unlocked", !NodeModel.Locked);
            lockButton.tooltip = NodeModel.Locked ? "Unlock node outputs" : "Lock node outputs";
            if (NodeModel.Locked)
            {
                capabilities &= ~Capabilities.Movable;
                capabilities &= ~Capabilities.Deletable;
            }
            else
            {
                capabilities |= Capabilities.Movable;
                capabilities |= Capabilities.Deletable;
            }
        }

        private FlowPortView CreatePort(FlowPortModel portModel, Direction direction)
        {
            Port.Capacity capacity = portModel.AllowsMultipleConnections ? Port.Capacity.Multi : Port.Capacity.Single;
            return new FlowPortView(portModel, direction, capacity, edgeConnectorListener);
        }

        private static string BuildPortLookupKey(string portId, FlowPortDirection direction)
        {
            return $"{direction}:{portId}";
        }

        private static Color GetStatusColor(FlowNodeExecutionStatus status)
        {
            return status switch
            {
                FlowNodeExecutionStatus.Success => new Color(0.34f, 0.78f, 0.36f, 1f),
                FlowNodeExecutionStatus.Error => new Color(0.89f, 0.28f, 0.26f, 1f),
                FlowNodeExecutionStatus.Dirty => new Color(0.96f, 0.78f, 0.22f, 1f),
                FlowNodeExecutionStatus.Running => new Color(0.3f, 0.62f, 0.95f, 1f),
                _ => new Color(0.55f, 0.55f, 0.55f, 1f)
            };
        }
    }
}
