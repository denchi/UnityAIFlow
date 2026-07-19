using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public sealed class FlowNodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private FlowGraphView graphView;

        public void Initialize(FlowGraphView ownerGraphView)
        {
            graphView = ownerGraphView;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Node"), 0)
            };
            HashSet<string> emittedGroups = new HashSet<string>();

            foreach (FlowNodeRegistration registration in FlowNodeRegistry.AuthoringRegistrations)
            {
                string[] parts = registration.MenuPath.Split('/');
                string groupPath = string.Empty;
                for (int index = 0; index < parts.Length - 1; index++)
                {
                    groupPath = string.IsNullOrEmpty(groupPath) ? parts[index] : $"{groupPath}/{parts[index]}";
                    if (emittedGroups.Add(groupPath))
                    {
                        entries.Add(new SearchTreeGroupEntry(new GUIContent(parts[index]), index + 1));
                    }
                }

                entries.Add(new SearchTreeEntry(new GUIContent(parts[^1]))
                {
                    level = parts.Length,
                    userData = registration.NodeType
                });
            }

            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            if (searchTreeEntry.userData is Type nodeType)
            {
                graphView.CreateNode(nodeType, context.screenMousePosition);
                return true;
            }

            return false;
        }
    }
}
