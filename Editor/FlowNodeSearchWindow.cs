using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public sealed class FlowNodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private FlowGraphView graphView;
        private FlowPortView connectingPort;

        public void Initialize(FlowGraphView ownerGraphView)
        {
            graphView = ownerGraphView;
        }

        internal void BeginConnection(FlowPortView port)
        {
            connectingPort = port;
        }

        public void ClearConnection()
        {
            connectingPort = null;
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
                if (!CanConnectTo(registration.NodeType))
                {
                    continue;
                }

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
                if (connectingPort != null)
                {
                    graphView.CreateNodeAndConnect(nodeType, context.screenMousePosition, connectingPort);
                    ClearConnection();
                }
                else
                {
                    graphView.CreateNode(nodeType, context.screenMousePosition);
                }

                return true;
            }

            return false;
        }

        private bool CanConnectTo(Type nodeType)
        {
            if (connectingPort == null)
            {
                return true;
            }

            if (!(Activator.CreateInstance(nodeType) is FlowNodeModel node))
            {
                return false;
            }

            node.EnsureInitialized();
            IReadOnlyList<FlowPortModel> candidatePorts = connectingPort.direction == Direction.Output
                ? node.InputPorts
                : node.OutputPorts;
            foreach (FlowPortModel candidatePort in candidatePorts)
            {
                if (FlowGraphView.ArePortTypesCompatible(connectingPort.PortModel, candidatePort))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
