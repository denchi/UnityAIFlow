using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    [CustomEditor(typeof(FlowGraphAsset))]
    public sealed class FlowGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Open Flow Graph"))
            {
                FlowGraphEditorWindow.Open((FlowGraphAsset)target);
            }

            if (GUILayout.Button("Mark All Nodes Dirty"))
            {
                FlowGraphAsset graphAsset = (FlowGraphAsset)target;
                Undo.RecordObject(graphAsset, "Mark Flow Graph Dirty");
                graphAsset.MarkAllNodesDirty();
                EditorUtility.SetDirty(graphAsset);
            }
        }
    }
}
