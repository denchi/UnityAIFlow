using UnityEditor;

namespace Myrvo.FlowGraph.Editor
{
    [FilePath("ProjectSettings/MyrvoFlowGraphProjectSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FlowGraphProjectSettingsStorage : ScriptableSingleton<FlowGraphProjectSettingsStorage>
    {
        public FlowGraphDebugSettings Settings = new FlowGraphDebugSettings();

        public FlowGraphDebugSettings GetOrCreateSettings()
        {
            Settings ??= new FlowGraphDebugSettings();
            return Settings;
        }

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
