using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public enum FlowGraphEditorBackend
    {
        GraphView,
        GraphToolsFoundation
    }

    public static class FlowGraphEditorCompatibility
    {
        public static string TargetUnityVersion => Application.unityVersion;

#if UNITY_2023_2_OR_NEWER
        public static FlowGraphEditorBackend ActiveBackend => HasPublicGraphToolsFoundationAuthoringApi
            ? FlowGraphEditorBackend.GraphToolsFoundation
            : FlowGraphEditorBackend.GraphView;
        public static string BackendReason =>
            HasPublicGraphToolsFoundationAuthoringApi
                ? "Unity 2023.2 or newer exposes a usable public GraphTools Foundation authoring API."
                : "Unity 2023.2 exposes GraphTools Foundation modules, but the key custom-authoring editor types are not publicly accessible from this package, so GraphView remains the usable backend.";
#else
        public static FlowGraphEditorBackend ActiveBackend => FlowGraphEditorBackend.GraphView;
        public static string BackendReason =>
            "Older Unity versions in this workspace expose GraphView but not a usable GraphTools Foundation editor stack for this package.";
#endif

#if UNITY_2023_2_OR_NEWER
        public static bool HasPublicGraphToolsFoundationAuthoringApi =>
            IsPublicType("Unity.GraphToolsFoundation.Editor.GraphViewEditorWindow, UnityEditor.GraphToolsFoundationModule") &&
            IsPublicType("Unity.GraphToolsFoundation.Editor.BaseGraphTool, UnityEditor.GraphToolsFoundationModule") &&
            IsPublicType("Unity.GraphToolsFoundation.Editor.GraphAsset, UnityEditor.GraphToolsFoundationModule");

        private static bool IsPublicType(string assemblyQualifiedName)
        {
            System.Type type = System.Type.GetType(assemblyQualifiedName);
            return type != null && (type.IsPublic || type.IsNestedPublic);
        }
#endif
    }

    internal static class FlowGraphBackendDiagnostics
    {
        [MenuItem("Window/Myrvo/Log Flow Graph Backend")]
        public static void LogBackend()
        {
            Debug.Log(
                $"FlowGraph backend: {FlowGraphEditorCompatibility.ActiveBackend} | Unity={FlowGraphEditorCompatibility.TargetUnityVersion} | Reason={FlowGraphEditorCompatibility.BackendReason}");
        }
    }
}
