using UnityEditor;

namespace Myrvo.FlowGraph.Editor
{
    [FilePath(CurrentStoragePath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FlowProviderProjectSettingsStorage : ScriptableSingleton<FlowProviderProjectSettingsStorage>
    {
        internal const string CurrentStoragePath = "UserSettings/MyrvoFlowProviderProjectSettings.asset";

        public FlowProviderProjectSettings Settings = new FlowProviderProjectSettings();
        public bool HasMigratedLegacySettings;

        public FlowProviderProjectSettings GetOrCreateSettings()
        {
            Settings ??= new FlowProviderProjectSettings();
            return Settings;
        }

        public void SaveSettings()
        {
            Save(true);
        }
    }

    [FilePath(LegacyStoragePath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class LegacyFlowProviderProjectSettingsStorage : ScriptableSingleton<LegacyFlowProviderProjectSettingsStorage>
    {
        internal const string LegacyStoragePath = "ProjectSettings/MyrvoFlowProviderProjectSettings.asset";

        public FlowProviderProjectSettings Settings = new FlowProviderProjectSettings();
    }
}
