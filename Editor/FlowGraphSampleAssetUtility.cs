using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public static class FlowGraphSampleAssetUtility
    {
        public const string DefaultSamplePath = "Assets/FlowGraphs/SampleFlowGraph.asset";

        [MenuItem("Assets/Create/Myrvo/Flow Graph Sample", priority = 301)]
        public static void CreateSampleAsset()
        {
            string assetPath = GetUniqueAssetPath();
            CreateSampleAssetAtPath(assetPath, openWindow: true);
        }

        public static FlowGraphAsset CreateSampleAssetAtPath(string assetPath, bool openWindow)
        {
            if (AssetDatabase.LoadAssetAtPath<FlowGraphAsset>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            string directory = GetAssetDirectory(assetPath);
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                EnsureFolderHierarchy(directory);
            }

            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            graphAsset.name = GetAssetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(graphAsset, assetPath);

            Undo.RecordObject(graphAsset, "Create Sample Flow Graph");

            FlowTextInputNodeModel textInputNode = graphAsset.CreateNode<FlowTextInputNodeModel>(new Vector2(120f, 20f));
            FlowPromptTemplateNodeModel promptTemplateNode = graphAsset.CreateNode<FlowPromptTemplateNodeModel>(new Vector2(420f, 20f));
            FlowAskTextNodeModel askTextNode = graphAsset.CreateNode<FlowAskTextNodeModel>(new Vector2(760f, 20f));
            FlowDebugLogNodeModel debugLogNode = graphAsset.CreateNode<FlowDebugLogNodeModel>(new Vector2(1080f, 20f));
            FlowExportFileNodeModel exportFileNode = graphAsset.CreateNode<FlowExportFileNodeModel>(new Vector2(1080f, 180f));

            graphAsset.AddEdge(
                textInputNode.Id,
                textInputNode.GetPortByKey("text", FlowPortDirection.Output).Id,
                promptTemplateNode.Id,
                promptTemplateNode.GetPortByKey("subject", FlowPortDirection.Input).Id);

            graphAsset.AddEdge(
                promptTemplateNode.Id,
                promptTemplateNode.GetPortByKey("prompt", FlowPortDirection.Output).Id,
                askTextNode.Id,
                askTextNode.GetPortByKey("text", FlowPortDirection.Input).Id);

            graphAsset.AddEdge(
                textInputNode.Id,
                textInputNode.GetPortByKey("text", FlowPortDirection.Output).Id,
                askTextNode.Id,
                askTextNode.GetPortByKey("question", FlowPortDirection.Input).Id);

            graphAsset.AddEdge(
                askTextNode.Id,
                askTextNode.GetPortByKey("answer", FlowPortDirection.Output).Id,
                debugLogNode.Id,
                debugLogNode.GetPortByKey("message", FlowPortDirection.Input).Id);

            graphAsset.AddEdge(
                askTextNode.Id,
                askTextNode.GetPortByKey("answer", FlowPortDirection.Output).Id,
                exportFileNode.Id,
                exportFileNode.GetPortByKey("value", FlowPortDirection.Input).Id);

            EditorUtility.SetDirty(graphAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = graphAsset;
            if (openWindow)
            {
                FlowGraphEditorWindow.Open(graphAsset);
            }

            return graphAsset;
        }

        private static string GetUniqueAssetPath()
        {
            return AssetDatabase.GenerateUniqueAssetPath(DefaultSamplePath);
        }

        private static string GetAssetDirectory(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex <= 0 ? string.Empty : normalizedPath.Substring(0, separatorIndex);
        }

        private static string GetAssetFileNameWithoutExtension(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return "FlowGraph";
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            string fileName = separatorIndex >= 0 ? normalizedPath.Substring(separatorIndex + 1) : normalizedPath;
            int extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : fileName;
        }

        private static void EnsureFolderHierarchy(string assetDirectory)
        {
            string[] folders = assetDirectory.Split('/');
            if (folders.Length == 0 || folders[0] != "Assets")
            {
                return;
            }

            string currentPath = folders[0];
            for (int index = 1; index < folders.Length; index++)
            {
                string folderName = folders[index];
                string nextPath = currentPath + "/" + folderName;
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folderName);
                }

                currentPath = nextPath;
            }
        }
    }
}
