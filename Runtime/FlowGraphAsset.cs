using System;
using System.Collections.Generic;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    [CreateAssetMenu(fileName = "FlowGraph", menuName = "Myrvo/Flow Graph")]
    public sealed class FlowGraphAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeReference] private List<FlowNodeModel> nodes = new List<FlowNodeModel>();
        [SerializeField] private List<FlowEdgeModel> edges = new List<FlowEdgeModel>();
        [SerializeField] private List<FlowVariableModel> variables = new List<FlowVariableModel>();
        [SerializeField] private Vector3 viewPosition = Vector3.zero;
        [SerializeField] private Vector3 viewScale = Vector3.one;

        public IReadOnlyList<FlowNodeModel> Nodes => nodes;
        public IReadOnlyList<FlowEdgeModel> Edges => edges;
        public IReadOnlyList<FlowVariableModel> Variables => variables;
        public Vector3 ViewPosition => viewPosition;
        public Vector3 ViewScale => viewScale;

        public T CreateNode<T>(Vector2 position) where T : FlowNodeModel, new()
        {
            T node = new T();
            node.Position = position;
            node.EnsureInitialized();
            nodes.Add(node);
            return node;
        }

        public FlowNodeModel CreateNode(Type nodeType, Vector2 position)
        {
            FlowNodeModel node = Activator.CreateInstance(nodeType) as FlowNodeModel;
            if (node == null)
            {
                throw new InvalidOperationException($"Unable to create flow node of type {nodeType}.");
            }

            node.Position = position;
            node.EnsureInitialized();
            nodes.Add(node);
            return node;
        }

        public void RemoveNode(string nodeId)
        {
            nodes.RemoveAll(node => node.Id == nodeId);
            edges.RemoveAll(edge => edge.InputNodeId == nodeId || edge.OutputNodeId == nodeId);
            MarkAllNodesDirty();
        }

        public FlowEdgeModel AddEdge(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
        {
            FlowNodeModel targetNode = nodes.Find(node => node.Id == targetNodeId);
            FlowPortModel targetPort = targetNode?.GetPortById(targetPortId);
            if (targetPort != null && !targetPort.AllowsMultipleConnections)
            {
                edges.RemoveAll(edge => edge.InputPortId == targetPortId);
            }

            FlowEdgeModel edge = new FlowEdgeModel();
            edge.Initialize(sourceNodeId, sourcePortId, targetNodeId, targetPortId);
            edges.Add(edge);
            MarkNodeAndDependentsDirty(targetNodeId);
            return edge;
        }

        public void RemoveEdge(string edgeId)
        {
            FlowEdgeModel edge = edges.Find(candidate => candidate.Id == edgeId);
            edges.RemoveAll(candidate => candidate.Id == edgeId);
            if (edge != null)
            {
                MarkNodeAndDependentsDirty(edge.InputNodeId);
            }
        }

        public FlowVariableModel AddVariable(string name, FlowSerializedValue initialValue)
        {
            FlowVariableModel variable = new FlowVariableModel
            {
                Name = name,
                InitialValue = initialValue
            };
            variables.Add(variable);
            return variable;
        }

        public void RemoveVariable(string variableId)
        {
            variables.RemoveAll(variable => variable.Id == variableId);
        }

        public void SetViewTransform(Vector3 position, Vector3 scale)
        {
            viewPosition = position;
            viewScale = scale;
        }

        public FlowNodeModel GetNodeById(string nodeId)
        {
            return nodes.Find(node => node.Id == nodeId);
        }

        public void MarkAllNodesDirty()
        {
            foreach (FlowNodeModel node in nodes)
            {
                if (node == null || node.Locked)
                {
                    continue;
                }

                node.Dirty = true;
                node.Status = FlowNodeExecutionStatus.Dirty;
                node.LastError = string.Empty;
                node.LastExecutionSummary = string.Empty;
            }
        }

        public void MarkNodeAndDependentsDirty(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                MarkAllNodesDirty();
                return;
            }

            Queue<string> pending = new Queue<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue(nodeId);

            while (pending.Count > 0)
            {
                string currentNodeId = pending.Dequeue();
                if (!visited.Add(currentNodeId))
                {
                    continue;
                }

                FlowNodeModel currentNode = GetNodeById(currentNodeId);
                if (currentNode == null || currentNode.Locked)
                {
                    continue;
                }

                currentNode.Dirty = true;
                currentNode.Status = FlowNodeExecutionStatus.Dirty;
                currentNode.LastError = string.Empty;
                currentNode.LastExecutionSummary = string.Empty;

                foreach (FlowEdgeModel edge in edges)
                {
                    if (edge.OutputNodeId == currentNodeId)
                    {
                        pending.Enqueue(edge.InputNodeId);
                    }
                }
            }
        }

        public void OnBeforeSerialize()
        {
            EnsureInitialized();
        }

        public void OnAfterDeserialize()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            nodes ??= new List<FlowNodeModel>();
            edges ??= new List<FlowEdgeModel>();
            variables ??= new List<FlowVariableModel>();

            foreach (FlowNodeModel node in nodes)
            {
                node?.EnsureInitialized();
            }

            RepairInvalidEdges();
        }

        /// <summary>
        /// Removes preview payloads duplicated by an output snapshot's canonical artifact data.
        /// Safe to invoke repeatedly; returns true only when this asset changed.
        /// </summary>
        public bool OptimizePersistedPreviewData()
        {
            if (nodes == null)
            {
                return false;
            }

            bool changed = false;
            foreach (FlowNodeModel node in nodes)
            {
                changed |= node?.OptimizePersistedPreviewData() ?? false;
            }

            return changed;
        }

        private void RepairInvalidEdges()
        {
            HashSet<string> validNodeIds = new HashSet<string>(nodes.FindAll(node => node != null).ConvertAll(node => node.Id), StringComparer.Ordinal);
            edges.RemoveAll(edge =>
            {
                if (edge == null)
                {
                    return true;
                }

                if (!validNodeIds.Contains(edge.OutputNodeId) || !validNodeIds.Contains(edge.InputNodeId))
                {
                    return true;
                }

                FlowNodeModel outputNode = GetNodeById(edge.OutputNodeId);
                FlowNodeModel inputNode = GetNodeById(edge.InputNodeId);
                return outputNode?.GetPortById(edge.OutputPortId, FlowPortDirection.Output) == null ||
                       inputNode?.GetPortById(edge.InputPortId, FlowPortDirection.Input) == null;
            });
        }
    }
}
