using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    [CreateAssetMenu(fileName = "FlowProviderRegistry", menuName = "Myrvo/Flow Graph/Legacy Provider Registry")]
    public sealed class FlowProviderRegistryAsset : ScriptableObject
    {
        [SerializeField] private List<FlowProviderDefinition> providers = new List<FlowProviderDefinition>();
        [SerializeField] private List<FlowModelDefinition> models = new List<FlowModelDefinition>();

        public IReadOnlyList<FlowProviderDefinition> Providers => providers;
        public IReadOnlyList<FlowModelDefinition> Models => models;

        public FlowProviderDefinition GetProvider(string providerId)
        {
            return providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal));
        }

        public FlowModelDefinition GetModel(string modelId)
        {
            return models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
        }

        public IReadOnlyList<FlowModelDefinition> GetModelsForCapability(FlowModelCapability capability)
        {
            return models.Where(model => model != null && model.SupportsCapability(capability)).ToArray();
        }
    }
}
