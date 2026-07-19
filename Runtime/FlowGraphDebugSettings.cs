using System;

namespace Myrvo.FlowGraph
{
    [Serializable]
    public sealed class FlowGraphDebugSettings
    {
        public bool EnableNodeDebugLogging;
    }

    public static class FlowGraphDebugSettingsLocator
    {
        private static Func<FlowGraphDebugSettings> resolver;

        public static void RegisterResolver(Func<FlowGraphDebugSettings> settingsResolver)
        {
            resolver = settingsResolver;
        }

        public static FlowGraphDebugSettings GetSettings()
        {
            return resolver?.Invoke() ?? new FlowGraphDebugSettings();
        }

        public static bool IsNodeDebugLoggingEnabled()
        {
            return GetSettings().EnableNodeDebugLogging;
        }
    }
}
