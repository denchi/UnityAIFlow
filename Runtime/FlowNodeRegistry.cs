using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Myrvo.FlowGraph
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FlowNodeAttribute : Attribute
    {
        public FlowNodeAttribute(string menuPath, string displayName = null, string category = null)
        {
            MenuPath = menuPath;
            DisplayName = displayName ?? menuPath;
            Category = category ?? string.Empty;
        }

        public string MenuPath { get; }
        public string DisplayName { get; }
        public string Category { get; }
    }

    public readonly struct FlowNodeRegistration
    {
        public FlowNodeRegistration(Type nodeType, string menuPath, string displayName, string category)
        {
            NodeType = nodeType;
            MenuPath = menuPath;
            DisplayName = displayName;
            Category = category;
        }

        public Type NodeType { get; }
        public string MenuPath { get; }
        public string DisplayName { get; }
        public string Category { get; }
    }

    public static class FlowNodeRegistry
    {
        private static IReadOnlyList<FlowNodeRegistration> registrations;
        private static IReadOnlyList<FlowNodeRegistration> authoringRegistrations;

        public static IReadOnlyList<FlowNodeRegistration> Registrations => registrations ??= BuildRegistrations();
        public static IReadOnlyList<FlowNodeRegistration> AuthoringRegistrations => authoringRegistrations ??= Registrations
            .Where(entry => IsAuthoringCategory(entry.Category))
            .ToArray();

        private static bool IsAuthoringCategory(string category)
        {
            return string.Equals(category, "Source", StringComparison.Ordinal) ||
                   string.Equals(category, "Generator", StringComparison.Ordinal) ||
                   string.Equals(category, "Utility", StringComparison.Ordinal);
        }

        private static IReadOnlyList<FlowNodeRegistration> BuildRegistrations()
        {
            List<FlowNodeRegistration> discovered = new List<FlowNodeRegistration>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(FlowNodeModel).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    FlowNodeAttribute attribute = type.GetCustomAttribute<FlowNodeAttribute>();
                    if (attribute == null)
                    {
                        continue;
                    }

                    discovered.Add(new FlowNodeRegistration(type, attribute.MenuPath, attribute.DisplayName, attribute.Category));
                }
            }

            return discovered
                .OrderBy(entry => entry.MenuPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
