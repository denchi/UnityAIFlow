using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Myrvo.FlowGraph.Editor
{
    public sealed class FlowGraphEditorWindow : EditorWindow
    {
        private const double ExecutionUiRefreshIntervalSeconds = 0.1d;
        private const string StyleSheetPath = "Packages/com.deathbygravitystudio.myrvo/Editor/Styles/MyrvoFlowGraph.uss";

        private FlowGraphAsset graphAsset;
        private FlowGraphView graphView;
        private IMGUIContainer inspectorContainer;
        private SerializedObject graphSerializedObject;
        private FlowGraphExecutionState executionState;
        private bool isExecutingNode;
        private double nextExecutionUiRefreshTime;

        [MenuItem("Window/Myrvo/Flow Graph")]
        public static void OpenWindow()
        {
            GetWindow<FlowGraphEditorWindow>("Flow Graph");
        }

        public static void Open(FlowGraphAsset asset)
        {
            FlowGraphEditorWindow window = GetWindow<FlowGraphEditorWindow>("Flow Graph");
            window.Load(asset);
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is FlowGraphAsset asset)
            {
                Open(asset);
                return true;
            }

            return false;
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            Toolbar toolbar = new Toolbar();
            ObjectField objectField = new ObjectField("Graph")
            {
                objectType = typeof(FlowGraphAsset),
                allowSceneObjects = false,
                value = graphAsset
            };
            objectField.RegisterValueChangedCallback(evt => Load(evt.newValue as FlowGraphAsset));
            toolbar.Add(objectField);

            toolbar.Add(new ToolbarButton(ExecuteSelectedNode) { text = "Execute Selected" });
            toolbar.Add(new ToolbarButton(MarkGraphDirty) { text = "Mark Graph Dirty" });
            rootVisualElement.Add(toolbar);

            TwoPaneSplitView splitView = new TwoPaneSplitView(0, Mathf.RoundToInt(position.width * 0.75f), TwoPaneSplitViewOrientation.Horizontal)
            {
                style =
                {
                    flexGrow = 1f,
                    minHeight = 0f
                }
            };

            graphView = new FlowGraphView(this)
            {
                style =
                {
                    flexGrow = 1f,
                    minWidth = 0f,
                    minHeight = 0f
                }
            };
            splitView.Add(graphView);

            inspectorContainer = new IMGUIContainer(DrawSelectionInspector)
            {
                style =
                {
                    flexGrow = 1f,
                    minWidth = 320f
                }
            };
            splitView.Add(inspectorContainer);
            rootVisualElement.Add(splitView);

            if (graphAsset != null)
            {
                graphView.Bind(graphAsset);
            }
        }

        internal void NotifySelectionChanged()
        {
            inspectorContainer?.MarkDirtyRepaint();
        }

        internal void OpenNodeContent(FlowNodeModel node)
        {
            FlowNodeContentWindow.Open(node, graphAsset);
        }

        private void Load(FlowGraphAsset asset)
        {
            graphAsset = asset;
            graphSerializedObject = asset == null ? null : new SerializedObject(asset);
            executionState = new FlowGraphExecutionState();
            titleContent = new GUIContent(asset == null ? "Flow Graph" : $"Flow Graph - {asset.name}");
            graphView?.Bind(asset);
            inspectorContainer?.MarkDirtyRepaint();
        }

        private async void ExecuteSelectedNode()
        {
            ExecuteNode(graphView?.SelectedNode);
        }

        internal async void ExecuteNode(FlowNodeModel node)
        {
            if (isExecutingNode || graphAsset == null || node == null)
            {
                return;
            }

            try
            {
                isExecutingNode = true;
                EditorApplication.update += RefreshExecutionUi;

                FlowGraphRunner runner = new FlowGraphRunner(graphAsset, executionState);
                node.Status = FlowNodeExecutionStatus.Running;
                RefreshGraphUi(true);

                await runner.ExecuteNodeForcedAsync(node);
                EditorUtility.SetDirty(graphAsset);
                RefreshGraphUi(true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                isExecutingNode = false;
                EditorApplication.update -= RefreshExecutionUi;
                RefreshGraphUi(true);
            }
        }

        private void RefreshExecutionUi()
        {
            if (!isExecutingNode)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < nextExecutionUiRefreshTime)
            {
                return;
            }

            nextExecutionUiRefreshTime = now + ExecutionUiRefreshIntervalSeconds;
            RefreshGraphUi(false);
        }

        private void RefreshGraphUi(bool force)
        {
            if (graphView == null || !graphView.RefreshNodeViews(force))
            {
                return;
            }

            inspectorContainer?.MarkDirtyRepaint();
            Repaint();
        }

        private void MarkGraphDirty()
        {
            if (graphAsset == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Mark Flow Graph Dirty");
            graphAsset.MarkAllNodesDirty();
            EditorUtility.SetDirty(graphAsset);
            inspectorContainer?.MarkDirtyRepaint();
        }

        private void DrawSelectionInspector()
        {
            if (graphAsset == null)
            {
                EditorGUILayout.HelpBox("Select or create a FlowGraph asset to begin authoring.", MessageType.Info);
                return;
            }

            SerializedObject serializedObject = GetGraphSerializedObject();
            serializedObject.UpdateIfRequiredOrScript();

            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            FlowNodeModel selectedNode = graphView?.SelectedNode;
            EditorGUI.BeginChangeCheck();
            if (selectedNode == null)
            {
                DrawGraphInspector(serializedObject);
                ApplyInspectorChanges(serializedObject, null);
                return;
            }

            SerializedProperty nodesProperty = serializedObject.FindProperty("nodes");
            for (int index = 0; index < nodesProperty.arraySize; index++)
            {
                SerializedProperty element = nodesProperty.GetArrayElementAtIndex(index);
                if (!ReferenceEquals(element.managedReferenceValue, selectedNode))
                {
                    continue;
                }

                DrawNodeInspector(serializedObject, element, selectedNode);
                ApplyInspectorChanges(serializedObject, selectedNode);
                return;
            }

            EditorGUILayout.HelpBox("The selected node could not be resolved in the graph asset.", MessageType.Warning);
            EditorGUI.EndChangeCheck();
        }

        private SerializedObject GetGraphSerializedObject()
        {
            if (graphSerializedObject == null || graphSerializedObject.targetObject != graphAsset)
            {
                graphSerializedObject = new SerializedObject(graphAsset);
            }

            return graphSerializedObject;
        }

        private void ApplyInspectorChanges(SerializedObject serializedObject, FlowNodeModel selectedNode)
        {
            if (!EditorGUI.EndChangeCheck() || !serializedObject.ApplyModifiedProperties())
            {
                return;
            }

            graphAsset.EnsureInitialized();
            if (selectedNode == null)
            {
                graphAsset.MarkAllNodesDirty();
                graphView.Bind(graphAsset);
            }
            else
            {
                graphAsset.MarkNodeAndDependentsDirty(selectedNode.Id);
            }

            EditorUtility.SetDirty(graphAsset);
            inspectorContainer?.MarkDirtyRepaint();
        }

        private void DrawGraphInspector(SerializedObject serializedObject)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Mark All Dirty"))
            {
                MarkGraphDirty();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("variables"), true);
        }

        private void DrawNodeInspector(SerializedObject serializedObject, SerializedProperty nodeProperty, FlowNodeModel selectedNode)
        {
            EditorGUILayout.LabelField("Status", selectedNode.Status.ToString());
            EditorGUILayout.LabelField("Dirty", selectedNode.Dirty ? "Yes" : "No");

            if (selectedNode.Locked)
            {
                EditorGUILayout.HelpBox("This node is locked. Its cached outputs are preserved and its settings cannot be changed until it is unlocked from the node header.", MessageType.Info);
            }

            if (!string.IsNullOrWhiteSpace(selectedNode.LastError))
            {
                EditorGUILayout.HelpBox(selectedNode.LastError, MessageType.Error);
            }
            else if (!string.IsNullOrWhiteSpace(selectedNode.LastExecutionSummary))
            {
                EditorGUILayout.HelpBox(selectedNode.LastExecutionSummary, MessageType.None);
            }

            using (new EditorGUI.DisabledScope(selectedNode.Locked))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Execute Node"))
                {
                    ExecuteSelectedNode();
                }

                if (GUILayout.Button("Mark Dirty"))
                {
                    Undo.RecordObject(graphAsset, "Mark Flow Node Dirty");
                    graphAsset.MarkNodeAndDependentsDirty(selectedNode.Id);
                    EditorUtility.SetDirty(graphAsset);
                    inspectorContainer?.MarkDirtyRepaint();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6f);

                DrawNodeProperties(nodeProperty, selectedNode);
            }
            DrawOutputSnapshots(selectedNode);
        }

        private void DrawNodeProperties(SerializedProperty nodeProperty, FlowNodeModel node)
        {
            switch (node)
            {
                case FlowTextInputNodeModel:
                    DrawTextAreaProperty(nodeProperty, "text", "Text", 5);
                    break;
                case FlowImageUploadNodeModel:
                    DrawObjectProperty(nodeProperty, "image", "Image");
                    DrawTextAreaProperty(nodeProperty, "description", "Description", 3);
                    break;
                case FlowCaptureScreenshotNodeModel:
                    DrawIntSliderProperty(nodeProperty, "superSize", "Resolution Scale", 1, 4);
                    EditorGUILayout.HelpBox("Captures the Game view's rendered image as a PNG, without Unity editor controls. Keep the Game view visible.", MessageType.None);
                    break;
                case FlowTextToImageNodeModel:
                    DrawRegistryBackedImageProperties(nodeProperty, node);
                    DrawTextAreaProperty(nodeProperty, "fallbackPrompt", "Prompt", 5);
                    break;
                case FlowImageToImageNodeModel:
                    DrawRegistryBackedImageProperties(nodeProperty, node);
                    DrawSliderProperty(nodeProperty, "strength", "Strength", 0f, 1f);
                    DrawTextAreaProperty(nodeProperty, "fallbackPrompt", "Prompt", 5);
                    break;
                case FlowImageToImageTwoInputsNodeModel:
                    DrawRegistryBackedImageProperties(nodeProperty, node);
                    DrawTextAreaProperty(nodeProperty, "fallbackPrompt", "Prompt", 5);
                    break;
                case FlowTextToVideoNodeModel:
                    DrawTextAreaProperty(nodeProperty, "fallbackPrompt", "Prompt", 5);
                    DrawFloatProperty(nodeProperty, "durationSeconds", "Duration (s)");
                    break;
                case FlowImageToVideoNodeModel:
                    DrawTextAreaProperty(nodeProperty, "fallbackPrompt", "Prompt", 5);
                    DrawFloatProperty(nodeProperty, "durationSeconds", "Duration (s)");
                    break;
                case FlowAskImageNodeModel:
                    DrawTextAreaProperty(nodeProperty, "fallbackQuestion", "Question", 4);
                    break;
                case FlowCropNodeModel:
                    DrawFloatProperty(nodeProperty, "x", "X");
                    DrawFloatProperty(nodeProperty, "y", "Y");
                    DrawFloatProperty(nodeProperty, "width", "Width");
                    DrawFloatProperty(nodeProperty, "height", "Height");
                    break;
                case FlowExportFileNodeModel:
                    DrawStringProperty(nodeProperty, "relativePath", "Relative Path");
                    DrawToggleProperty(nodeProperty, "allowOverwrite", "Allow Overwrite");
                    break;
                case FlowAskTextNodeModel:
                    DrawTextAreaProperty(nodeProperty, "fallbackQuestion", "Question", 4);
                    break;
                case FlowTextToSpeechNodeModel:
                    DrawTextAreaProperty(nodeProperty, "fallbackText", "Text", 4);
                    DrawStringProperty(nodeProperty, "voice", "Voice");
                    break;
                case FlowPromptTemplateNodeModel:
                    DrawTextAreaProperty(nodeProperty, "template", "Template", 5);
                    DrawStringProperty(nodeProperty, "fallbackSubject", "Fallback Subject");
                    DrawStringProperty(nodeProperty, "fallbackStyle", "Fallback Style");
                    DrawStringProperty(nodeProperty, "fallbackMood", "Fallback Mood");
                    break;
                default:
                    EditorGUILayout.HelpBox("No custom properties are defined for this node yet.", MessageType.None);
                    break;
            }
        }

        private void DrawRegistryBackedImageProperties(SerializedProperty nodeProperty, FlowNodeModel node)
        {
            FlowProviderRequestKind requestKind = node.GetSupportedRequestKinds().FirstOrDefault();
            IReadOnlyList<FlowModelDefinition> models = FlowProviderPluginRegistry.GetModelsForCapabilityAndRequestKind(node.GetPrimaryCapability(), requestKind);
            DrawRegistryModelPopup(nodeProperty, "model", "Model", models);

            SerializedProperty modelProperty = nodeProperty.FindPropertyRelative("model");
            FlowModelDefinition selectedModel = modelProperty == null ? null : FlowProviderPluginRegistry.GetModel(modelProperty.stringValue);
            if (selectedModel == null)
            {
                EditorGUILayout.HelpBox("The selected model is not available from the current provider plugins.", MessageType.Warning);
                DrawStringProperty(nodeProperty, "model", "Model");
                return;
            }

            DrawSelectConstraint(nodeProperty, selectedModel.Constraints.GetSelectConstraint("aspectRatio"), "aspectRatio");
            DrawSelectConstraint(nodeProperty, selectedModel.Constraints.GetSelectConstraint("imageSize"), "imageSize");
            DrawBooleanConstraint(nodeProperty, selectedModel.Constraints.GetBooleanConstraint("transparent"), "transparent");
        }

        private static void DrawRegistryModelPopup(SerializedProperty nodeProperty, string propertyName, string label, IReadOnlyList<FlowModelDefinition> models)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            if (models == null || models.Count == 0)
            {
                EditorGUILayout.HelpBox("No compatible models were exposed by the installed provider plugins.", MessageType.Warning);
                property.stringValue = EditorGUILayout.TextField(label, property.stringValue);
                return;
            }

            string[] labels = models.Select(model => model.Label).ToArray();
            string[] values = models.Select(model => model.Id).ToArray();
            DrawPopupProperty(property, label, labels, values);
        }

        private static void DrawSelectConstraint(SerializedProperty nodeProperty, FlowSelectConstraint constraint, string propertyName)
        {
            if (constraint == null)
            {
                return;
            }

            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            IReadOnlyList<FlowOptionChoice> options = constraint.Options;
            string[] labels = options.Select(option => option.Label).ToArray();
            string[] values = options.Select(option => option.Value).ToArray();

            if (string.IsNullOrWhiteSpace(property.stringValue) && !string.IsNullOrWhiteSpace(constraint.DefaultValue))
            {
                property.stringValue = constraint.DefaultValue;
            }

            DrawPopupProperty(property, constraint.Label, labels, values);
        }

        private static void DrawBooleanConstraint(SerializedProperty nodeProperty, FlowBooleanConstraint constraint, string propertyName)
        {
            if (constraint == null)
            {
                return;
            }

            if (!constraint.Supported && constraint.HiddenWhenUnsupported)
            {
                return;
            }

            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            if (!constraint.Supported)
            {
                property.boolValue = false;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle(constraint.Label, false);
                }

                return;
            }

            property.boolValue = EditorGUILayout.Toggle(constraint.Label, property.boolValue);
        }

        private void DrawOutputSnapshots(FlowNodeModel node)
        {
            if ((node.OutputSnapshots == null || node.OutputSnapshots.Count == 0) && string.IsNullOrWhiteSpace(node.LastPreviewData))
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Outputs", EditorStyles.boldLabel);

            if (!string.IsNullOrWhiteSpace(node.LastPreviewData))
            {
                Texture2D previewTexture = FlowPreviewTextureCache.Get(node.LastPreviewData);
                if (previewTexture != null)
                {
                    float maxWidth = Mathf.Max(120f, position.width * 0.18f);
                    float aspect = Mathf.Max(1f, previewTexture.height == 0 ? 1f : (float)previewTexture.width / previewTexture.height);
                    float width = Mathf.Min(maxWidth, previewTexture.width);
                    float height = width / aspect;
                    Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(rect, previewTexture, null, ScaleMode.ScaleToFit);
                }
            }

            foreach (FlowNodeOutputSnapshot snapshot in node.OutputSnapshots)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(snapshot.Key, EditorStyles.boldLabel);
                if (!string.IsNullOrWhiteSpace(snapshot.Summary))
                {
                    EditorGUILayout.LabelField(snapshot.Summary, EditorStyles.wordWrappedLabel);
                }

                if (!string.IsNullOrWhiteSpace(snapshot.StringValue) &&
                    snapshot.ValueType == FlowValueType.String &&
                    snapshot.StringValue.Length < 400)
                {
                    EditorGUILayout.SelectableLabel(snapshot.StringValue, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                }

                EditorGUILayout.EndVertical();
            }
        }


        private static void DrawStringProperty(SerializedProperty nodeProperty, string propertyName, string label)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.stringValue = EditorGUILayout.TextField(label, property.stringValue);
            }
        }

        private static void DrawFloatProperty(SerializedProperty nodeProperty, string propertyName, string label)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.floatValue = EditorGUILayout.FloatField(label, property.floatValue);
            }
        }

        private static void DrawToggleProperty(SerializedProperty nodeProperty, string propertyName, string label)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.boolValue = EditorGUILayout.Toggle(label, property.boolValue);
            }
        }

        private static void DrawSliderProperty(SerializedProperty nodeProperty, string propertyName, string label, float min, float max)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.floatValue = EditorGUILayout.Slider(label, property.floatValue, min, max);
            }
        }

        private static void DrawIntSliderProperty(SerializedProperty nodeProperty, string propertyName, string label, int min, int max)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = EditorGUILayout.IntSlider(label, property.intValue, min, max);
            }
        }

        private static void DrawObjectProperty(SerializedProperty nodeProperty, string propertyName, string label)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private static void DrawTextAreaProperty(SerializedProperty nodeProperty, string propertyName, string label, int lines = 4)
        {
            SerializedProperty property = nodeProperty.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUILayout.LabelField(label);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float height = Mathf.Max(lineHeight * lines, 72f);
            property.stringValue = EditorGUILayout.TextArea(property.stringValue ?? string.Empty, GUILayout.MinHeight(height));
        }

        private static void DrawPopupProperty(SerializedProperty property, string label, string[] labels, string[] values)
        {
            string currentValue = property.stringValue ?? string.Empty;
            int currentIndex = Array.IndexOf(values, currentValue);
            if (currentIndex < 0)
            {
                currentIndex = 0;
                if (values.Length > 0 && string.IsNullOrWhiteSpace(currentValue))
                {
                    property.stringValue = values[0];
                }
            }

            int selectedIndex = EditorGUILayout.Popup(label, currentIndex, labels);
            if (selectedIndex >= 0 && selectedIndex < values.Length)
            {
                property.stringValue = values[selectedIndex];
            }
        }
    }
}
