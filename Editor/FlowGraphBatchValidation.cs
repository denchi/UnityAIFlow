using UnityEditor;
using UnityEngine;
using System.IO;

namespace Myrvo.FlowGraph.Editor
{
    public static class FlowGraphBatchValidation
    {
        public static void CreateAndRunSampleGraph()
        {
            const string assetPath = FlowGraphSampleAssetUtility.DefaultSamplePath;

            FlowGraphAsset graphAsset = FlowGraphSampleAssetUtility.CreateSampleAssetAtPath(assetPath, openWindow: false);

            FlowExportFileNodeModel targetNode = null;
            foreach (FlowNodeModel node in graphAsset.Nodes)
            {
                if (node is FlowExportFileNodeModel exportFileNode)
                {
                    targetNode = exportFileNode;
                    break;
                }
            }

            if (targetNode == null)
            {
                throw new System.InvalidOperationException("Sample graph is missing the Export File node.");
            }

            FlowGraphRunner runner = new FlowGraphRunner(graphAsset, new FlowGraphExecutionState());
            FlowNodeExecutionResult executionResult = runner.ExecuteNode(targetNode);
            object exportedPath = executionResult.Outputs.TryGetValue("path", out object outputValue)
                ? outputValue
                : null;

            if (exportedPath is not string exportedAssetPath || string.IsNullOrWhiteSpace(exportedAssetPath))
            {
                throw new System.InvalidOperationException("Export File node did not produce an asset path.");
            }

            string absolutePath = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, exportedAssetPath));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException($"Export File node did not create the expected file: {absolutePath}", absolutePath);
            }

            Debug.Log($"FlowGraph batch validation completed. exportedPath={exportedAssetPath}");
            AssetDatabase.SaveAssets();
        }
    }
}
