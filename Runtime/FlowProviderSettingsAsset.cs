using UnityEngine;

namespace Myrvo.FlowGraph
{
    [CreateAssetMenu(fileName = "FlowProviderSettings", menuName = "Myrvo/Flow Graph/Legacy Provider Settings")]
    public sealed class FlowProviderSettingsAsset : ScriptableObject
    {
        [SerializeField] private FlowProviderRegistryAsset registry;
        [SerializeField] private string openAIApiKey;
        [SerializeField] private string openAIBaseUrl = "https://api.openai.com/v1";
        [SerializeField] private string geminiApiKey;
        [SerializeField] private string geminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public FlowProviderRegistryAsset Registry => registry;
        public string OpenAIApiKey => openAIApiKey;
        public string OpenAIBaseUrl => string.IsNullOrWhiteSpace(openAIBaseUrl) ? "https://api.openai.com/v1" : openAIBaseUrl.TrimEnd('/');
        public string GeminiApiKey => geminiApiKey;
        public string GeminiBaseUrl => string.IsNullOrWhiteSpace(geminiBaseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : geminiBaseUrl.TrimEnd('/');
    }
}
