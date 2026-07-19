using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Myrvo.FlowGraph
{
    public enum FlowProviderSettingFieldType
    {
        Text,
        Password
    }

    [Serializable]
    public sealed class FlowProviderSettingDefinition
    {
        public FlowProviderSettingDefinition(string key, string label, FlowProviderSettingFieldType fieldType, string defaultValue = "", string helpText = "")
        {
            Key = key;
            Label = label;
            FieldType = fieldType;
            DefaultValue = defaultValue ?? string.Empty;
            HelpText = helpText ?? string.Empty;
        }

        public string Key { get; }
        public string Label { get; }
        public FlowProviderSettingFieldType FieldType { get; }
        public string DefaultValue { get; }
        public string HelpText { get; }
    }

    [Serializable]
    public sealed class FlowProviderDescriptor
    {
        public FlowProviderDescriptor(string id, string label, IEnumerable<FlowModelCapability> supportedCapabilities, IEnumerable<FlowProviderSettingDefinition> settingDefinitions = null)
        {
            Id = id;
            Label = label;
            SupportedCapabilities = supportedCapabilities == null
                ? Array.Empty<FlowModelCapability>()
                : supportedCapabilities.ToArray();
            SettingDefinitions = settingDefinitions == null
                ? Array.Empty<FlowProviderSettingDefinition>()
                : settingDefinitions.ToArray();
        }

        public string Id { get; }
        public string Label { get; }
        public IReadOnlyList<FlowModelCapability> SupportedCapabilities { get; }
        public IReadOnlyList<FlowProviderSettingDefinition> SettingDefinitions { get; }
    }

    [Serializable]
    public sealed class FlowProviderProjectSettingsValue
    {
        public FlowProviderProjectSettingsValue()
        {
        }

        public FlowProviderProjectSettingsValue(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key;
        public string Value;
    }

    [Serializable]
    public sealed class FlowProviderProjectSettingsEntry
    {
        public string ProviderId;
        public List<FlowProviderProjectSettingsValue> Values = new List<FlowProviderProjectSettingsValue>();
    }

    [Serializable]
    public sealed class FlowProviderProjectSettings
    {
        public List<FlowProviderProjectSettingsEntry> Entries = new List<FlowProviderProjectSettingsEntry>();

        public string GetValue(string providerId, string key, string fallbackValue = "")
        {
            FlowProviderProjectSettingsEntry entry = Entries.FirstOrDefault(candidate => string.Equals(candidate.ProviderId, providerId, StringComparison.Ordinal));
            FlowProviderProjectSettingsValue value = entry?.Values?.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            return value == null ? fallbackValue : value.Value ?? fallbackValue;
        }

        public void SetValue(string providerId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            FlowProviderProjectSettingsEntry entry = Entries.FirstOrDefault(candidate => string.Equals(candidate.ProviderId, providerId, StringComparison.Ordinal));
            if (entry == null)
            {
                entry = new FlowProviderProjectSettingsEntry
                {
                    ProviderId = providerId
                };
                Entries.Add(entry);
            }

            FlowProviderProjectSettingsValue existingValue = entry.Values.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            if (existingValue == null)
            {
                entry.Values.Add(new FlowProviderProjectSettingsValue(key, value));
                return;
            }

            existingValue.Value = value;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FlowProviderPluginAttribute : Attribute
    {
    }

    public interface IFlowProviderPlugin
    {
        string ProviderId { get; }
        string DisplayName { get; }
        FlowProviderDescriptor GetDescriptor();
        IReadOnlyList<FlowModelDefinition> GetModels();
        bool CanHandleModel(string modelId);
        Task<FlowImageArtifact> ExecuteImageRequestAsync(FlowProviderRequest request, FlowProviderProjectSettings settings);
    }

    public static class FlowProviderProjectSettingsLocator
    {
        private static Func<FlowProviderProjectSettings> resolver;

        public static void RegisterResolver(Func<FlowProviderProjectSettings> settingsResolver)
        {
            resolver = settingsResolver;
        }

        public static FlowProviderProjectSettings GetSettings()
        {
            return resolver?.Invoke() ?? new FlowProviderProjectSettings();
        }
    }

    public static class FlowProviderPluginRegistry
    {
        private static readonly object SyncRoot = new object();
        private static bool isInitialized;
        private static List<IFlowProviderPlugin> plugins = new List<IFlowProviderPlugin>();
        private static Dictionary<string, IFlowProviderPlugin> pluginsByProviderId = new Dictionary<string, IFlowProviderPlugin>(StringComparer.Ordinal);
        private static Dictionary<string, FlowModelDefinition> modelsById = new Dictionary<string, FlowModelDefinition>(StringComparer.Ordinal);

        public static IReadOnlyList<IFlowProviderPlugin> GetPlugins()
        {
            EnsureInitialized();
            return plugins;
        }

        public static IReadOnlyList<FlowProviderDescriptor> GetProviders()
        {
            EnsureInitialized();
            return plugins.Select(plugin => plugin.GetDescriptor()).OrderBy(descriptor => descriptor.Label, StringComparer.Ordinal).ToArray();
        }

        public static IFlowProviderPlugin GetPlugin(string providerId)
        {
            EnsureInitialized();
            return string.IsNullOrWhiteSpace(providerId) || !pluginsByProviderId.TryGetValue(providerId, out IFlowProviderPlugin plugin) ? null : plugin;
        }

        public static FlowModelDefinition GetModel(string modelId)
        {
            EnsureInitialized();
            return string.IsNullOrWhiteSpace(modelId) || !modelsById.TryGetValue(modelId, out FlowModelDefinition model) ? null : model;
        }

        public static IReadOnlyList<FlowModelDefinition> GetModelsForCapability(FlowModelCapability capability)
        {
            EnsureInitialized();
            return modelsById.Values
                .Where(model => model != null && model.SupportsCapability(capability))
                .OrderBy(model => model.Label, StringComparer.Ordinal)
                .ToArray();
        }

        public static IReadOnlyList<FlowModelDefinition> GetModelsForCapabilityAndRequestKind(FlowModelCapability capability, FlowProviderRequestKind requestKind)
        {
            EnsureInitialized();
            return modelsById.Values
                .Where(model => model != null && model.SupportsCapability(capability) && model.SupportsRequestKind(requestKind))
                .OrderBy(model => model.Label, StringComparer.Ordinal)
                .ToArray();
        }

        public static IFlowProviderPlugin ResolvePluginForModel(string modelId)
        {
            EnsureInitialized();
            return plugins.FirstOrDefault(plugin => plugin.CanHandleModel(modelId));
        }

        public static void ResetCache()
        {
            lock (SyncRoot)
            {
                isInitialized = false;
                plugins = new List<IFlowProviderPlugin>();
                pluginsByProviderId = new Dictionary<string, IFlowProviderPlugin>(StringComparer.Ordinal);
                modelsById = new Dictionary<string, FlowModelDefinition>(StringComparer.Ordinal);
            }
        }

        private static void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (isInitialized)
                {
                    return;
                }

                List<IFlowProviderPlugin> discoveredPlugins = new List<IFlowProviderPlugin>();
                Dictionary<string, IFlowProviderPlugin> discoveredProviders = new Dictionary<string, IFlowProviderPlugin>(StringComparer.Ordinal);
                Dictionary<string, FlowModelDefinition> discoveredModels = new Dictionary<string, FlowModelDefinition>(StringComparer.Ordinal);

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] assemblyTypes;
                    try
                    {
                        assemblyTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        assemblyTypes = exception.Types.Where(type => type != null).ToArray();
                    }

                    foreach (Type type in assemblyTypes)
                    {
                        if (type == null || type.IsAbstract || type.IsInterface)
                        {
                            continue;
                        }

                        if (type.GetCustomAttribute<FlowProviderPluginAttribute>() == null || !typeof(IFlowProviderPlugin).IsAssignableFrom(type))
                        {
                            continue;
                        }

                        if (!(Activator.CreateInstance(type) is IFlowProviderPlugin plugin))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(plugin.ProviderId) || discoveredProviders.ContainsKey(plugin.ProviderId))
                        {
                            continue;
                        }

                        discoveredPlugins.Add(plugin);
                        discoveredProviders[plugin.ProviderId] = plugin;

                        foreach (FlowModelDefinition model in plugin.GetModels() ?? Array.Empty<FlowModelDefinition>())
                        {
                            if (model == null || string.IsNullOrWhiteSpace(model.Id) || discoveredModels.ContainsKey(model.Id))
                            {
                                continue;
                            }

                            discoveredModels[model.Id] = model;
                        }
                    }
                }

                plugins = discoveredPlugins.OrderBy(plugin => plugin.DisplayName, StringComparer.Ordinal).ToList();
                pluginsByProviderId = discoveredProviders;
                modelsById = discoveredModels;
                isInitialized = true;
            }
        }
    }
}
