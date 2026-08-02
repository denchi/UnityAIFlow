using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    internal static class FlowGraphPreviewMigration
    {
        [MenuItem("Tools/Myrvo/Migrate Flow Graph Preview Storage")]
        private static void MigrateAllGraphAssets()
        {
            string[] assetGuids = AssetDatabase.FindAssets("t:FlowGraphAsset");
            string[] assetPaths = new string[assetGuids.Length];
            int optimizedAssetCount = 0;

            for (int index = 0; index < assetGuids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                assetPaths[index] = assetPath;
                FlowGraphAsset graphAsset = AssetDatabase.LoadAssetAtPath<FlowGraphAsset>(assetPath);
                if (graphAsset == null)
                {
                    continue;
                }

                Undo.RecordObject(graphAsset, "Optimize Flow Graph Preview Storage");
                if (!graphAsset.OptimizePersistedPreviewData())
                {
                    continue;
                }

                EditorUtility.SetDirty(graphAsset);
                optimizedAssetCount++;
            }

            // Reserialization also removes the legacy lastPreviewData fields, which are no
            // longer represented by FlowNodeModel. It is safe to run repeatedly.
            if (assetPaths.Length > 0)
            {
                AssetDatabase.ForceReserializeAssets(assetPaths);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FlowGraph] Preview storage migration completed. Optimized {optimizedAssetCount} of {assetPaths.Length} graph asset(s).");
        }
    }
}
