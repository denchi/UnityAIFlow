using UnityEditor;

namespace Myrvo.FlowGraph.Editor
{
    [InitializeOnLoad]
    internal static class FlowProviderProjectSettingsBootstrap
    {
        static FlowProviderProjectSettingsBootstrap()
        {
            FlowProviderProjectSettingsLocator.RegisterResolver(() => FlowProviderProjectSettingsStorage.instance.GetOrCreateSettings());
            FlowProviderPluginRegistry.ResetCache();
            MigrateLegacySettingsIfNeeded();
        }

        private static void MigrateLegacySettingsIfNeeded()
        {
            FlowProviderProjectSettingsStorage storage = FlowProviderProjectSettingsStorage.instance;
            storage.GetOrCreateSettings();
            if (storage.HasMigratedLegacySettings)
            {
                return;
            }

            ApplyStoredSettings(LegacyFlowProviderProjectSettingsStorage.instance.Settings, storage.Settings);
            ApplyLegacyAssetSettings(FlowProviderRegistryLocator.Settings, storage.Settings);

            storage.HasMigratedLegacySettings = true;
            storage.SaveSettings();
        }

        private static void ApplyStoredSettings(FlowProviderProjectSettings source, FlowProviderProjectSettings target)
        {
            if (source?.Entries == null || target == null)
            {
                return;
            }

            foreach (FlowProviderProjectSettingsEntry entry in source.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry?.ProviderId) || entry.Values == null)
                {
                    continue;
                }

                foreach (FlowProviderProjectSettingsValue value in entry.Values)
                {
                    if (string.IsNullOrWhiteSpace(value?.Key) || string.IsNullOrWhiteSpace(value.Value))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(target.GetValue(entry.ProviderId, value.Key)))
                    {
                        continue;
                    }

                    target.SetValue(entry.ProviderId, value.Key, value.Value);
                }
            }
        }

        private static void ApplyLegacyAssetSettings(FlowProviderSettingsAsset legacySettings, FlowProviderProjectSettings target)
        {
            if (legacySettings == null || target == null)
            {
                return;
            }

            ApplyLegacyAssetValue(target, "openai", "apiKey", legacySettings.OpenAIApiKey);
            ApplyLegacyAssetValue(target, "openai", "baseUrl", legacySettings.OpenAIBaseUrl);
            ApplyLegacyAssetValue(target, "google", "apiKey", legacySettings.GeminiApiKey);
            ApplyLegacyAssetValue(target, "google", "baseUrl", legacySettings.GeminiBaseUrl);
        }

        private static void ApplyLegacyAssetValue(FlowProviderProjectSettings target, string providerId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.IsNullOrWhiteSpace(target.GetValue(providerId, key)))
            {
                return;
            }

            target.SetValue(providerId, key, value);
        }
    }
}
