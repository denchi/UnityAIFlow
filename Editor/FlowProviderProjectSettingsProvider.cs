using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    internal static class FlowProviderProjectSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            SettingsProvider provider = new SettingsProvider("Project/Myrvo/Flow Providers", SettingsScope.Project)
            {
                label = "Flow Providers",
                guiHandler = _ => DrawGui()
            };
            return provider;
        }

        private static void DrawGui()
        {
            FlowProviderProjectSettingsStorage storage = FlowProviderProjectSettingsStorage.instance;
            FlowProviderProjectSettings settings = storage.GetOrCreateSettings();
            EditorGUILayout.HelpBox("Provider credentials are stored in UserSettings and are not written into flow graph assets.", MessageType.Info);

            foreach (FlowProviderDescriptor descriptor in FlowProviderPluginRegistry.GetProviders())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(descriptor.Label, EditorStyles.boldLabel);

                foreach (FlowProviderSettingDefinition definition in descriptor.SettingDefinitions)
                {
                    string currentValue = settings.GetValue(descriptor.Id, definition.Key, definition.DefaultValue);
                    string nextValue = definition.FieldType == FlowProviderSettingFieldType.Password
                        ? EditorGUILayout.PasswordField(definition.Label, currentValue)
                        : EditorGUILayout.TextField(definition.Label, currentValue);

                    if (!string.IsNullOrWhiteSpace(definition.HelpText))
                    {
                        EditorGUILayout.LabelField(definition.HelpText, EditorStyles.wordWrappedMiniLabel);
                    }

                    if (nextValue == currentValue)
                    {
                        continue;
                    }

                    settings.SetValue(descriptor.Id, definition.Key, nextValue);
                    storage.SaveSettings();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }
        }
    }
}
