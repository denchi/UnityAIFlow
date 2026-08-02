using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public sealed class FlowNodeContentWindow : EditorWindow
    {
        private FlowNodeModel node;
        private FlowGraphAsset graphAsset;
        private FlowCropNodeModel cropNode;
        private Texture2D texture;
        private Texture2D cropSourceTexture;
        private string textContent;
        private Vector2 panOffset;
        private float zoom = 1f;
        private bool isPanning;
        private Vector2 dragStart;
        private Vector2 panStart;
        private CropHandle activeCropHandle;
        private RectInt dragStartCropRect;

        private enum CropHandle
        {
            None,
            Move,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        public static void Open(FlowNodeModel node, FlowGraphAsset graphAsset = null)
        {
            if (node == null)
            {
                return;
            }

            FlowNodeContentWindow window = GetWindow<FlowNodeContentWindow>();
            window.titleContent = new GUIContent(node.Title);
            window.minSize = new Vector2(420f, 320f);
            window.Show();
            window.SetNode(node, graphAsset);
        }

        private void SetNode(FlowNodeModel value, FlowGraphAsset asset)
        {
            node = value;
            graphAsset = asset;
            cropNode = value as FlowCropNodeModel;
            texture = ResolveTexture(node);
            cropSourceTexture = cropNode != null ? ResolveCropSourceTexture(cropNode, asset) : null;
            textContent = ResolveTextContent(node);
            zoom = 1f;
            panOffset = Vector2.zero;
            isPanning = false;
            activeCropHandle = CropHandle.None;
            Repaint();
        }

        private void OnGUI()
        {
            if (node == null)
            {
                EditorGUILayout.HelpBox("No node content available.", MessageType.Info);
                return;
            }

            DrawToolbar();

            Rect contentRect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(contentRect, new Color(0.15f, 0.15f, 0.15f, 1f));

            Texture2D activeTexture = cropNode != null ? cropSourceTexture : texture;
            if (activeTexture != null)
            {
                if (cropNode != null && !node.Locked)
                {
                    HandleCropEvents(contentRect, activeTexture);
                }

                HandleImageEvents(contentRect);

                DrawImage(contentRect, activeTexture);
                if (cropNode != null)
                {
                    DrawCropOverlay(contentRect, activeTexture);
                }

                return;
            }

            if (cropNode != null)
            {
                DrawUnavailableCropContent(contentRect);
                return;
            }

            DrawTextContent(contentRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(node.Title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    if (cropNode != null && cropSourceTexture != null && GUILayout.Button("Reset Crop", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                    {
                        ApplyCropRect(new RectInt(0, 0, cropSourceTexture.width, cropSourceTexture.height));
                        Repaint();
                    }
                }

                using (new EditorGUI.DisabledScope((cropNode == null && texture == null) || (cropNode != null && cropSourceTexture == null)))
                {
                    if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    {
                        zoom = 1f;
                        panOffset = Vector2.zero;
                        Repaint();
                    }
                }
            }
        }

        private void HandleImageEvents(Rect contentRect)
        {
            Event current = Event.current;
            if (!contentRect.Contains(current.mousePosition) && current.type != EventType.MouseUp)
            {
                return;
            }

            switch (current.type)
            {
                case EventType.ScrollWheel:
                {
                    float previousZoom = zoom;
                    zoom = Mathf.Clamp(zoom * (1f - current.delta.y * 0.06f), 0.1f, 8f);
                    Vector2 pivot = current.mousePosition - contentRect.center;
                    panOffset = (panOffset - pivot) * (zoom / previousZoom) + pivot;
                    current.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseDown when ShouldStartPan(current):
                    isPanning = true;
                    dragStart = current.mousePosition;
                    panStart = panOffset;
                    current.Use();
                    break;
                case EventType.MouseDrag when isPanning:
                    panOffset = panStart + (current.mousePosition - dragStart);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    isPanning = false;
                    break;
            }
        }

        private bool ShouldStartPan(Event current)
        {
            if (current == null)
            {
                return false;
            }

            if (current.button == 2)
            {
                return true;
            }

            if (current.button != 0 || activeCropHandle != CropHandle.None)
            {
                return false;
            }

            return cropNode == null;
        }

        private void HandleCropEvents(Rect contentRect, Texture2D activeTexture)
        {
            Event current = Event.current;
            if (activeTexture == null)
            {
                return;
            }

            Rect imageRect = GetImageDrawRect(contentRect, activeTexture);
            Rect cropRect = GetCropRectInWindow(imageRect, activeTexture);

            switch (current.type)
            {
                case EventType.MouseDown when current.button == 0 && contentRect.Contains(current.mousePosition):
                {
                    CropHandle handle = GetCropHandle(current.mousePosition, cropRect);
                    if (handle == CropHandle.None)
                    {
                        return;
                    }

                    activeCropHandle = handle;
                    dragStart = current.mousePosition;
                    dragStartCropRect = GetCurrentCropRect();
                    isPanning = false;
                    current.Use();
                    break;
                }
                case EventType.MouseDrag when activeCropHandle != CropHandle.None:
                {
                    float scale = imageRect.width / Mathf.Max(1f, activeTexture.width);
                    if (scale <= 0f)
                    {
                        return;
                    }

                    Vector2 deltaPixels = (current.mousePosition - dragStart) / scale;
                    RectInt updatedRect = BuildDraggedCropRect(dragStartCropRect, activeCropHandle, deltaPixels);
                    ApplyCropRect(updatedRect);
                    current.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseUp when activeCropHandle != CropHandle.None:
                    activeCropHandle = CropHandle.None;
                    current.Use();
                    break;
            }
        }

        private void DrawImage(Rect contentRect, Texture2D activeTexture)
        {
            Rect drawRect = GetImageDrawRect(contentRect, activeTexture);

            GUI.BeginClip(contentRect);
            Rect localRect = new Rect(drawRect.position - contentRect.position, drawRect.size);
            EditorGUI.DrawRect(new Rect(0f, 0f, contentRect.width, contentRect.height), new Color(0.12f, 0.12f, 0.12f, 1f));
            GUI.DrawTexture(localRect, activeTexture, ScaleMode.StretchToFill, true);
            GUI.EndClip();
        }

        private Rect GetImageDrawRect(Rect contentRect, Texture2D activeTexture)
        {
            float fittedScale = Mathf.Min(contentRect.width / activeTexture.width, contentRect.height / activeTexture.height);
            float drawScale = Mathf.Max(0.01f, fittedScale * zoom);
            Vector2 drawSize = new Vector2(activeTexture.width * drawScale, activeTexture.height * drawScale);
            return new Rect(contentRect.center - drawSize * 0.5f + panOffset, drawSize);
        }

        private void DrawCropOverlay(Rect contentRect, Texture2D activeTexture)
        {
            if (cropNode == null || activeTexture == null)
            {
                return;
            }

            Rect imageRect = GetImageDrawRect(contentRect, activeTexture);
            Rect cropRect = GetCropRectInWindow(imageRect, activeTexture);
            Rect clippedRect = Rect.MinMaxRect(
                Mathf.Max(contentRect.xMin, cropRect.xMin),
                Mathf.Max(contentRect.yMin, cropRect.yMin),
                Mathf.Min(contentRect.xMax, cropRect.xMax),
                Mathf.Min(contentRect.yMax, cropRect.yMax));

            Handles.BeginGUI();
            Color outlineColor = new Color(0.24f, 0.82f, 0.55f, 1f);
            Color shadeColor = new Color(0f, 0f, 0f, 0.45f);

            DrawCropShade(contentRect, cropRect, shadeColor);
            Handles.color = outlineColor;
            Handles.DrawSolidRectangleWithOutline(clippedRect, Color.clear, outlineColor);

            foreach (Vector2 handleCenter in GetCropHandleCenters(cropRect))
            {
                Rect handleRect = new Rect(handleCenter.x - 5f, handleCenter.y - 5f, 10f, 10f);
                EditorGUI.DrawRect(handleRect, outlineColor);
            }

            GUIStyle labelStyle = new GUIStyle(EditorStyles.whiteLabel)
            {
                alignment = TextAnchor.UpperLeft
            };
            GUI.Label(
                new Rect(contentRect.x + 8f, contentRect.y + 8f, 220f, 20f),
                $"x={Mathf.RoundToInt(cropNode.X)} y={Mathf.RoundToInt(cropNode.Y)} w={Mathf.RoundToInt(cropNode.Width)} h={Mathf.RoundToInt(cropNode.Height)}",
                labelStyle);
            Handles.EndGUI();
        }

        private static void DrawCropShade(Rect contentRect, Rect cropRect, Color shadeColor)
        {
            Rect top = Rect.MinMaxRect(contentRect.xMin, contentRect.yMin, contentRect.xMax, Mathf.Min(contentRect.yMax, cropRect.yMin));
            Rect left = Rect.MinMaxRect(contentRect.xMin, Mathf.Max(contentRect.yMin, cropRect.yMin), Mathf.Min(contentRect.xMax, cropRect.xMin), Mathf.Min(contentRect.yMax, cropRect.yMax));
            Rect right = Rect.MinMaxRect(Mathf.Max(contentRect.xMin, cropRect.xMax), Mathf.Max(contentRect.yMin, cropRect.yMin), contentRect.xMax, Mathf.Min(contentRect.yMax, cropRect.yMax));
            Rect bottom = Rect.MinMaxRect(contentRect.xMin, Mathf.Max(contentRect.yMin, cropRect.yMax), contentRect.xMax, contentRect.yMax);

            if (top.width > 0f && top.height > 0f) EditorGUI.DrawRect(top, shadeColor);
            if (left.width > 0f && left.height > 0f) EditorGUI.DrawRect(left, shadeColor);
            if (right.width > 0f && right.height > 0f) EditorGUI.DrawRect(right, shadeColor);
            if (bottom.width > 0f && bottom.height > 0f) EditorGUI.DrawRect(bottom, shadeColor);
        }

        private Rect GetCropRectInWindow(Rect imageRect, Texture2D activeTexture)
        {
            float scale = imageRect.width / Mathf.Max(1f, activeTexture.width);
            return new Rect(
                imageRect.x + (cropNode.X * scale),
                imageRect.y + (cropNode.Y * scale),
                Mathf.Max(1f, cropNode.Width * scale),
                Mathf.Max(1f, cropNode.Height * scale));
        }

        private IEnumerable<Vector2> GetCropHandleCenters(Rect cropRect)
        {
            yield return new Vector2(cropRect.xMin, cropRect.yMin);
            yield return new Vector2(cropRect.xMax, cropRect.yMin);
            yield return new Vector2(cropRect.xMin, cropRect.yMax);
            yield return new Vector2(cropRect.xMax, cropRect.yMax);
        }

        private static CropHandle GetCropHandle(Vector2 point, Rect cropRect)
        {
            const float handleRadius = 10f;
            Vector2 topLeft = new Vector2(cropRect.xMin, cropRect.yMin);
            Vector2 topRight = new Vector2(cropRect.xMax, cropRect.yMin);
            Vector2 bottomLeft = new Vector2(cropRect.xMin, cropRect.yMax);
            Vector2 bottomRight = new Vector2(cropRect.xMax, cropRect.yMax);

            if (Vector2.Distance(point, topLeft) <= handleRadius) return CropHandle.TopLeft;
            if (Vector2.Distance(point, topRight) <= handleRadius) return CropHandle.TopRight;
            if (Vector2.Distance(point, bottomLeft) <= handleRadius) return CropHandle.BottomLeft;
            if (Vector2.Distance(point, bottomRight) <= handleRadius) return CropHandle.BottomRight;
            return cropRect.Contains(point) ? CropHandle.Move : CropHandle.None;
        }

        private RectInt GetCurrentCropRect()
        {
            return new RectInt(
                Mathf.RoundToInt(cropNode.X),
                Mathf.RoundToInt(cropNode.Y),
                Mathf.Max(1, Mathf.RoundToInt(cropNode.Width)),
                Mathf.Max(1, Mathf.RoundToInt(cropNode.Height)));
        }

        private static RectInt BuildDraggedCropRect(RectInt startRect, CropHandle handle, Vector2 deltaPixels)
        {
            int deltaX = Mathf.RoundToInt(deltaPixels.x);
            int deltaY = Mathf.RoundToInt(deltaPixels.y);

            int x = startRect.x;
            int y = startRect.y;
            int width = Mathf.Max(1, startRect.width);
            int height = Mathf.Max(1, startRect.height);

            switch (handle)
            {
                case CropHandle.Move:
                    x += deltaX;
                    y += deltaY;
                    break;
                case CropHandle.TopLeft:
                    x += deltaX;
                    y += deltaY;
                    width -= deltaX;
                    height -= deltaY;
                    if (width < 1)
                    {
                        x = startRect.xMax - 1;
                        width = 1;
                    }

                    if (height < 1)
                    {
                        y = startRect.yMax - 1;
                        height = 1;
                    }

                    break;
                case CropHandle.TopRight:
                    y += deltaY;
                    width += deltaX;
                    height -= deltaY;
                    width = Mathf.Max(1, width);
                    if (height < 1)
                    {
                        y = startRect.yMax - 1;
                        height = 1;
                    }

                    break;
                case CropHandle.BottomLeft:
                    x += deltaX;
                    width -= deltaX;
                    height += deltaY;
                    if (width < 1)
                    {
                        x = startRect.xMax - 1;
                        width = 1;
                    }

                    height = Mathf.Max(1, height);
                    break;
                case CropHandle.BottomRight:
                    width = Mathf.Max(1, width + deltaX);
                    height = Mathf.Max(1, height + deltaY);
                    break;
            }

            return new RectInt(x, y, width, height);
        }

        private void ApplyCropRect(RectInt rect)
        {
            if (cropNode == null || graphAsset == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "Edit Crop Node");
            cropNode.X = rect.x;
            cropNode.Y = rect.y;
            cropNode.Width = Mathf.Max(1, rect.width);
            cropNode.Height = Mathf.Max(1, rect.height);
            graphAsset.MarkNodeAndDependentsDirty(cropNode.Id);
            EditorUtility.SetDirty(graphAsset);
        }

        private void DrawUnavailableCropContent(Rect contentRect)
        {
            Rect paddedRect = new Rect(contentRect.x + 12f, contentRect.y + 12f, contentRect.width - 24f, contentRect.height - 24f);
            EditorGUI.HelpBox(
                paddedRect,
                "The crop editor needs a previewable upstream image. Connect an image source or execute upstream image nodes, then reopen this window.",
                MessageType.Info);
        }

        private void DrawTextContent(Rect contentRect)
        {
            Rect paddedRect = new Rect(contentRect.x + 12f, contentRect.y + 12f, contentRect.width - 24f, contentRect.height - 24f);
            GUIStyle style = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false
            };
            EditorGUI.SelectableLabel(paddedRect, string.IsNullOrWhiteSpace(textContent) ? "No previewable content available for this node." : textContent, style);
        }

        private static Texture2D ResolveCropSourceTexture(FlowCropNodeModel cropNode, FlowGraphAsset graphAsset)
        {
            if (cropNode == null || graphAsset == null)
            {
                return null;
            }

            FlowPortModel imagePort = cropNode.GetPortByKey("image", FlowPortDirection.Input);
            if (imagePort == null)
            {
                return null;
            }

            FlowEdgeModel sourceEdge = graphAsset.Edges.FirstOrDefault(edge =>
                edge.InputNodeId == cropNode.Id &&
                edge.InputPortId == imagePort.Id);
            if (sourceEdge == null)
            {
                return null;
            }

            FlowNodeModel sourceNode = graphAsset.GetNodeById(sourceEdge.OutputNodeId);
            return ResolveTexture(sourceNode);
        }

        private static Texture2D ResolveTexture(FlowNodeModel node)
        {
            if (node is FlowImageSourceNodeModel sourceNode && sourceNode.Image != null)
            {
                return sourceNode.Image;
            }

            if (!string.IsNullOrWhiteSpace(node?.LastPreviewData))
            {
                return FlowPreviewTextureCache.Get(node.LastPreviewData, "FlowGraphContentPreview");
            }

            if (node?.OutputSnapshots != null)
            {
                foreach (FlowNodeOutputSnapshot snapshot in node.OutputSnapshots)
                {
                    string previewData = snapshot.GetPreviewData();
                    if (!string.IsNullOrWhiteSpace(previewData))
                    {
                        Texture2D previewTexture = FlowPreviewTextureCache.Get(previewData, "FlowGraphContentPreview");
                        if (previewTexture != null)
                        {
                            return previewTexture;
                        }
                    }
                }
            }

            return null;
        }

        private static string ResolveTextContent(FlowNodeModel node)
        {
            switch (node)
            {
                case FlowTextInputNodeModel textNode:
                    return textNode.Text;
                case FlowImageSourceNodeModel sourceNode:
                    return sourceNode.DescriptionText;
            }

            if (node.OutputSnapshots != null)
            {
                List<string> lines = new List<string>();
                foreach (FlowNodeOutputSnapshot snapshot in node.OutputSnapshots)
                {
                    if (!string.IsNullOrWhiteSpace(snapshot.StringValue) && snapshot.ValueType == FlowValueType.String)
                    {
                        lines.Add($"{snapshot.Key}\n{snapshot.StringValue}");
                    }
                    else if (!string.IsNullOrWhiteSpace(snapshot.Summary))
                    {
                        lines.Add($"{snapshot.Key}\n{snapshot.Summary}");
                    }
                }

                if (lines.Count > 0)
                {
                    return string.Join("\n\n", lines);
                }
            }

            if (!string.IsNullOrWhiteSpace(node.LastError))
            {
                return node.LastError;
            }

            return node.LastExecutionSummary;
        }

    }
}
