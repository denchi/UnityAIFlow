using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    internal static class FlowGraphProjectSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/Myrvo/Flow Graph", SettingsScope.Project)
            {
                label = "Flow Graph",
                guiHandler = _ => DrawGui()
            };
        }

        private static void DrawGui()
        {
            FlowGraphProjectSettingsStorage storage = FlowGraphProjectSettingsStorage.instance;
            FlowGraphDebugSettings settings = storage.GetOrCreateSettings();

            EditorGUILayout.HelpBox("Enable extra Console logging to explain why nodes executed, why they were skipped, and what error stopped them.", MessageType.Info);

            bool nextValue = EditorGUILayout.Toggle("Enable Node Debug Logging", settings.EnableNodeDebugLogging);
            if (nextValue == settings.EnableNodeDebugLogging)
            {
                return;
            }

            settings.EnableNodeDebugLogging = nextValue;
            storage.SaveSettings();
        }
    }
}
