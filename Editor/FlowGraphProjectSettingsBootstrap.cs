using UnityEditor;

namespace Myrvo.FlowGraph.Editor
{
    [InitializeOnLoad]
    internal static class FlowGraphProjectSettingsBootstrap
    {
        static FlowGraphProjectSettingsBootstrap()
        {
            FlowGraphDebugSettingsLocator.RegisterResolver(() => FlowGraphProjectSettingsStorage.instance.GetOrCreateSettings());
        }
    }
}
