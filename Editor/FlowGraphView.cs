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

        public FlowGraphView(FlowGraphEditorWindow ownerWindow)
        {
            this.ownerWindow = ownerWindow;
            style.flexGrow = 1f;
            focusable = true;
            Insert(0, new GridBackground());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            miniMap = new MiniMap { anchored = true };
            miniMap.SetPosition(new Rect(0f, 0f, 180f, 110f));
            Add(miniMap);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            searchWindow = ScriptableObject.CreateInstance<FlowNodeSearchWindow>();
            searchWindow.Initialize(this);
            nodeCreationRequest = context => SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), searchWindow);
            graphViewChanged += OnGraphViewChanged;
            viewTransformChanged += OnViewTransformChanged;
            SetupBlackboard();
        }

        public FlowNodeModel SelectedNode { get; private set; }

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
                bool typesCompatible = startPortModel.ValueType == candidatePortModel.ValueType ||
                                       startPortModel.ValueType == FlowValueType.Object ||
                                       candidatePortModel.ValueType == FlowValueType.Object;

                if (typesCompatible)
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
            foreach ((_, FlowNodeView nodeView) in nodeViewsById)
            {
                nodeView.RefreshFromModel();
            }
        }

        public void CreateNode(Type nodeType, Vector2 screenPosition)
        {
            if (graphAsset == null)
            {
                return;
            }

            Vector2 localPosition = contentViewContainer.WorldToLocal(screenPosition - ownerWindow.position.position);
            Undo.RecordObject(graphAsset, "Create Flow Node");
            FlowNodeModel node = graphAsset.CreateNode(nodeType, localPosition);
            EditorUtility.SetDirty(graphAsset);

            FlowNodeView nodeView = new FlowNodeView(node, this);
            nodeViewsById[node.Id] = nodeView;
            AddElement(nodeView);
            ownerWindow.NotifySelectionChanged();
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
            if (graphAsset == null)
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
                    marginLeft = 6f,
                    marginRight = 10f,
                    marginTop = 0f,
                    marginBottom = 0f,
                    alignSelf = Align.Center
                }
            };

            if (titleButtonContainer != null)
            {
                titleButtonContainer.style.marginLeft = 0f;
                titleButtonContainer.style.marginRight = 2f;
                titleButtonContainer.style.alignSelf = Align.Center;
                titleContainer.Remove(titleButtonContainer);
                headerControlsContainer.Add(titleButtonContainer);
            }

            headerControlsContainer.Add(statusIndicator);
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
            RefreshPorts();
            RefreshFromModel();
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

        public void RefreshFromModel()
        {
            title = NodeModel.Title;
            statusIndicator.style.backgroundColor = GetStatusColor(NodeModel);
            statusIndicator.tooltip = NodeModel.Status switch
            {
                FlowNodeExecutionStatus.Error when !string.IsNullOrWhiteSpace(NodeModel.LastError) => $"Error: {NodeModel.LastError}",
                FlowNodeExecutionStatus.Dirty => "Dirty",
                FlowNodeExecutionStatus.Running => "Running",
                FlowNodeExecutionStatus.Success when !string.IsNullOrWhiteSpace(NodeModel.LastExecutionSummary) => NodeModel.LastExecutionSummary,
                _ => NodeModel.Status.ToString()
            };

            Texture2D previewTexture = DecodePreviewTexture(NodeModel.LastPreviewData);
            previewImage.image = previewTexture;
            if (previewTexture != null)
            {
                float aspectRatio = previewTexture.height > 0 ? (float)previewTexture.width / previewTexture.height : 1f;
                float targetWidth = FlowGraphView.NodeWidth - (PreviewHorizontalPadding * 2f);
                bool useLetterbox = aspectRatio >= LetterboxAspectThreshold;
                float targetHeight = useLetterbox
                    ? LetterboxHeight
                    : Mathf.Clamp(targetWidth / Mathf.Max(0.01f, aspectRatio), PreviewMinHeight, PreviewMaxHeight);

                previewImage.scaleMode = useLetterbox ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop;
                previewImage.style.height = targetHeight;
                previewImage.style.display = DisplayStyle.Flex;
            }
            else
            {
                previewImage.style.display = DisplayStyle.None;
            }

            RefreshExpandedState();
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

        private FlowPortView CreatePort(FlowPortModel portModel, Direction direction)
        {
            Port.Capacity capacity = portModel.AllowsMultipleConnections ? Port.Capacity.Multi : Port.Capacity.Single;
            return new FlowPortView(portModel, direction, capacity, edgeConnectorListener);
        }

        private static string BuildPortLookupKey(string portId, FlowPortDirection direction)
        {
            return $"{direction}:{portId}";
        }

        private static Texture2D DecodePreviewTexture(string previewData)
        {
            if (string.IsNullOrWhiteSpace(previewData))
            {
                return null;
            }

            int commaIndex = previewData.IndexOf(',');
            if (commaIndex < 0 || commaIndex >= previewData.Length - 1)
            {
                return null;
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(previewData.Substring(commaIndex + 1));
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(imageBytes);
                texture.name = "FlowGraphNodePreview";
                return texture;
            }
            catch
            {
                return null;
            }
        }

        private static Color GetStatusColor(FlowNodeModel nodeModel)
        {
            return nodeModel.Status switch
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
