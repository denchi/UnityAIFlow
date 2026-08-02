using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    [CustomEditor(typeof(FlowGraphAsset))]
    public sealed class FlowGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            FlowGraphAsset graphAsset = (FlowGraphAsset)target;
            EditorGUILayout.LabelField("Nodes", graphAsset.Nodes.Count.ToString());
            EditorGUILayout.LabelField("Connections", graphAsset.Edges.Count.ToString());
            EditorGUILayout.LabelField("Variables", graphAsset.Variables.Count.ToString());
            EditorGUILayout.HelpBox("Edit nodes, connections, and graph variables in the Flow Graph window. The full serialized graph is intentionally not drawn here to keep large image graphs responsive.", MessageType.Info);

            if (GUILayout.Button("Open Flow Graph"))
            {
                FlowGraphEditorWindow.Open(graphAsset);
            }

            if (GUILayout.Button("Mark All Nodes Dirty"))
            {
                Undo.RecordObject(graphAsset, "Mark Flow Graph Dirty");
                graphAsset.MarkAllNodesDirty();
                EditorUtility.SetDirty(graphAsset);
            }
        }
    }
}
