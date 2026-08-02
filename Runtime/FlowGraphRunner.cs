using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    public sealed class FlowExecutionContext
    {
        private readonly Dictionary<string, object> variables = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> nodeOutputs = new Dictionary<string, object>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object> Variables => variables;

        public object GetVariable(string variableName)
        {
            return string.IsNullOrWhiteSpace(variableName) || !variables.TryGetValue(variableName, out object value) ? null : value;
        }

        public void SetVariable(string variableName, object value)
        {
            if (!string.IsNullOrWhiteSpace(variableName))
            {
                variables[variableName] = value;
            }
        }

        public void ReplaceVariables(IReadOnlyDictionary<string, object> sourceVariables)
        {
            variables.Clear();
            if (sourceVariables == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> pair in sourceVariables)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        public void SetNodeOutput(string nodeId, string portKey, object value)
        {
            if (!string.IsNullOrWhiteSpace(nodeId) && !string.IsNullOrWhiteSpace(portKey))
            {
                nodeOutputs[$"{nodeId}:{portKey}"] = value;
            }
        }

        public object GetNodeOutput(string nodeId, string portKey)
        {
            return TryGetNodeOutput(nodeId, portKey, out object value) ? value : null;
        }

        public bool TryGetNodeOutput(string nodeId, string portKey, out object value)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(portKey))
            {
                value = null;
                return false;
            }

            return nodeOutputs.TryGetValue($"{nodeId}:{portKey}", out value);
        }

        public void ClearNodeOutputs(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            List<string> keys = nodeOutputs.Keys.Where(key => key.StartsWith(nodeId + ":", StringComparison.Ordinal)).ToList();
            foreach (string key in keys)
            {
                nodeOutputs.Remove(key);
            }
        }
    }

    public sealed class FlowExecutionServices
    {
        private readonly FlowGraphRunner runner;
        private readonly FlowExecutionContext context;

        internal FlowExecutionServices(FlowGraphRunner runner, FlowExecutionContext context)
        {
            this.runner = runner;
            this.context = context;
        }

        public object GetInputValue(FlowNodeModel node, string inputPortKey, object fallbackValue = null)
        {
            return runner.ResolveInputValue(node, inputPortKey, fallbackValue, context);
        }

        public void RunNext(FlowNodeModel node, string outputPortKey)
        {
            runner.RunConnectedNodes(node, outputPortKey, context);
        }

        public void SetOutputValue(FlowNodeModel node, string outputPortKey, object value)
        {
            context.SetNodeOutput(node.Id, outputPortKey, value);
        }

        public object GetOutputValue(FlowNodeModel node, string outputPortKey)
        {
            return context.GetNodeOutput(node.Id, outputPortKey);
        }
    }

    public sealed class FlowGraphValidationResult
    {
        private readonly List<string> errors = new List<string>();

        public IReadOnlyList<string> Errors => errors;
        public bool IsValid => errors.Count == 0;

        public void AddError(string message)
        {
            errors.Add(message);
        }
    }

    public sealed class FlowGraphExecutionState
    {
        private readonly Dictionary<string, object> variables = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, object>> outputsByNodeId = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        public bool HasCachedOutputs(string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) && outputsByNodeId.ContainsKey(nodeId);
        }

        public void RestoreInto(FlowExecutionContext context)
        {
            context.ReplaceVariables(variables);
            foreach (KeyValuePair<string, Dictionary<string, object>> pair in outputsByNodeId)
            {
                foreach (KeyValuePair<string, object> output in pair.Value)
                {
                    context.SetNodeOutput(pair.Key, output.Key, output.Value);
                }
            }
        }

        public void CaptureVariables(FlowExecutionContext context)
        {
            variables.Clear();
            foreach (KeyValuePair<string, object> pair in context.Variables)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        public Dictionary<string, object> CaptureNodeOutputs(FlowNodeModel node, FlowExecutionContext context)
        {
            Dictionary<string, object> outputs = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (FlowPortModel outputPort in node.OutputPorts)
            {
                if (context.TryGetNodeOutput(node.Id, outputPort.Key, out object value))
                {
                    outputs[outputPort.Key] = value;
                }
            }

            outputsByNodeId[node.Id] = outputs;
            return outputs;
        }

        public void InvalidateNode(string nodeId)
        {
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                outputsByNodeId.Remove(nodeId);
            }
        }
    }

    public sealed class FlowNodeExecutionResult
    {
        public FlowNodeExecutionResult(FlowExecutionContext context, IReadOnlyDictionary<string, object> outputs, bool executed)
        {
            Context = context;
            Outputs = outputs;
            Executed = executed;
        }

        public FlowExecutionContext Context { get; }
        public IReadOnlyDictionary<string, object> Outputs { get; }
        public bool Executed { get; }
    }

    public sealed class FlowGraphRunner
    {
        private const string DebugLogPrefix = "[FlowGraph]";
        private readonly FlowGraphAsset graphAsset;
        private readonly FlowGraphExecutionState executionState;
        private readonly Dictionary<string, FlowNodeModel> nodesById = new Dictionary<string, FlowNodeModel>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FlowEdgeModel>> edgesByInputPort = new Dictionary<string, List<FlowEdgeModel>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FlowEdgeModel>> edgesByOutputPort = new Dictionary<string, List<FlowEdgeModel>>(StringComparer.Ordinal);
        private readonly HashSet<string> activeValueStack = new HashSet<string>(StringComparer.Ordinal);
        private readonly bool enableNodeDebugLogging;

        public FlowGraphRunner(FlowGraphAsset graphAsset, FlowGraphExecutionState executionState = null)
        {
            this.graphAsset = graphAsset;
            this.executionState = executionState ?? new FlowGraphExecutionState();
            enableNodeDebugLogging = FlowGraphDebugSettingsLocator.IsNodeDebugLoggingEnabled();
            RebuildCaches();
        }

        public FlowGraphValidationResult Validate()
        {
            FlowGraphValidationResult result = new FlowGraphValidationResult();

            if (graphAsset == null)
            {
                result.AddError("Graph asset is null.");
                return result;
            }

            foreach (FlowEdgeModel edge in graphAsset.Edges)
            {
                if (!nodesById.TryGetValue(edge.OutputNodeId, out FlowNodeModel outputNode))
                {
                    result.AddError($"Missing source node for edge {edge.Id}.");
                    continue;
                }

                if (!nodesById.TryGetValue(edge.InputNodeId, out FlowNodeModel inputNode))
                {
                    result.AddError($"Missing target node for edge {edge.Id}.");
                    continue;
                }

                FlowPortModel outputPort = outputNode.GetPortById(edge.OutputPortId, FlowPortDirection.Output);
                FlowPortModel inputPort = inputNode.GetPortById(edge.InputPortId, FlowPortDirection.Input);
                if (outputPort == null || inputPort == null)
                {
                    result.AddError($"Broken port reference on edge {edge.Id}.");
                    continue;
                }

                if (outputPort.ValueType != inputPort.ValueType &&
                    outputPort.ValueType != FlowValueType.Object &&
                    inputPort.ValueType != FlowValueType.Object)
                {
                    result.AddError($"Port type mismatch between {outputNode.Title}.{outputPort.DisplayName} and {inputNode.Title}.{inputPort.DisplayName}.");
                }
            }

            string cycle = FindCycleDescription();
            if (!string.IsNullOrWhiteSpace(cycle))
            {
                result.AddError($"Cycle detected: {cycle}");
            }

            return result;
        }

        public FlowNodeExecutionResult ExecuteNode(string nodeId)
        {
            if (!nodesById.TryGetValue(nodeId, out FlowNodeModel node))
            {
                throw new InvalidOperationException($"Node {nodeId} was not found in the graph.");
            }

            return ExecuteNodeAsync(node).GetAwaiter().GetResult();
        }

        public FlowNodeExecutionResult ExecuteNode(FlowNodeModel targetNode)
        {
            return ExecuteNodeAsync(targetNode).GetAwaiter().GetResult();
        }

        public async Task<FlowNodeExecutionResult> ExecuteNodeAsync(string nodeId)
        {
            if (!nodesById.TryGetValue(nodeId, out FlowNodeModel node))
            {
                throw new InvalidOperationException($"Node {nodeId} was not found in the graph.");
            }

            return await ExecuteNodeAsync(node);
        }

        public async Task<FlowNodeExecutionResult> ExecuteNodeAsync(FlowNodeModel targetNode)
        {
            return await ExecuteNodeAsync(targetNode, false);
        }

        /// <summary>
        /// Executes the requested node even when its cached outputs are valid.
        /// Dependencies still use normal cache rules, and locked nodes always skip.
        /// </summary>
        public async Task<FlowNodeExecutionResult> ExecuteNodeForcedAsync(FlowNodeModel targetNode)
        {
            return await ExecuteNodeAsync(targetNode, true);
        }

        private async Task<FlowNodeExecutionResult> ExecuteNodeAsync(FlowNodeModel targetNode, bool forceTargetNode)
        {
            FlowGraphValidationResult validation = Validate();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
            }

            FlowExecutionContext context = new FlowExecutionContext();
            SeedVariables(context);
            executionState.RestoreInto(context);

            HashSet<string> neededNodes = CollectNeededNodes(targetNode.Id);
            List<string> orderedNodeIds = TopologicalSort(neededNodes);
            bool executedAny = false;

            foreach (string nodeId in orderedNodeIds)
            {
                FlowNodeModel node = nodesById[nodeId];
                executedAny |= await ExecuteNodeIfNeededAsync(node, context, forceTargetNode && ReferenceEquals(node, targetNode));
            }

            executionState.CaptureVariables(context);
            Dictionary<string, object> outputs = executionState.CaptureNodeOutputs(targetNode, context);
            return new FlowNodeExecutionResult(context, outputs, executedAny);
        }

        internal object ResolveInputValue(FlowNodeModel node, string inputPortKey, object fallbackValue, FlowExecutionContext context)
        {
            FlowPortModel inputPort = node.GetPortByKey(inputPortKey, FlowPortDirection.Input);
            if (inputPort == null || !edgesByInputPort.TryGetValue(inputPort.Id, out List<FlowEdgeModel> incomingEdges) || incomingEdges.Count == 0)
            {
                return fallbackValue;
            }

            FlowEdgeModel edge = incomingEdges[0];
            if (!nodesById.TryGetValue(edge.OutputNodeId, out FlowNodeModel sourceNode))
            {
                return fallbackValue;
            }

            FlowPortModel sourcePort = sourceNode.GetPortById(edge.OutputPortId, FlowPortDirection.Output);
            if (sourcePort == null)
            {
                return fallbackValue;
            }

            string stackToken = $"{sourceNode.Id}:{sourcePort.Key}";
            if (!activeValueStack.Add(stackToken))
            {
                throw new InvalidOperationException($"Cycle detected while evaluating {sourceNode.Title}.{sourcePort.DisplayName}.");
            }

            try
            {
                if (context.TryGetNodeOutput(sourceNode.Id, sourcePort.Key, out object cached))
                {
                    return cached;
                }

                FlowExecutionServices services = new FlowExecutionServices(this, context);
                object evaluatedValue = sourceNode.EvaluateOutput(sourcePort.Key, context, services);
                context.SetNodeOutput(sourceNode.Id, sourcePort.Key, evaluatedValue);
                return evaluatedValue ?? fallbackValue;
            }
            finally
            {
                activeValueStack.Remove(stackToken);
            }
        }

        internal void RunConnectedNodes(FlowNodeModel node, string outputPortKey, FlowExecutionContext context)
        {
            FlowPortModel outputPort = node.GetPortByKey(outputPortKey, FlowPortDirection.Output);
            if (outputPort == null || !edgesByOutputPort.TryGetValue(outputPort.Id, out List<FlowEdgeModel> edges))
            {
                return;
            }

            foreach (FlowEdgeModel edge in edges)
            {
                if (!nodesById.TryGetValue(edge.InputNodeId, out FlowNodeModel nextNode))
                {
                    continue;
                }

                ExecuteNodeIfNeededAsync(nextNode, context, true).GetAwaiter().GetResult();
            }
        }

        private async Task<bool> ExecuteNodeIfNeededAsync(FlowNodeModel node, FlowExecutionContext context, bool forceExecute)
        {
            if (node.Locked)
            {
                RestoreOutputsFromSnapshots(node, context);
                node.Dirty = false;
                node.Status = FlowNodeExecutionStatus.Success;
                return false;
            }

            string inputHash = ComputeInputHash(node, context);
            bool inputChanged = !string.Equals(node.LastRunHash, inputHash, StringComparison.Ordinal);
            bool hasCachedExecutionOutputs = executionState.HasCachedOutputs(node.Id);
            bool hasPersistedSnapshots = node.OutputSnapshots.Count > 0;
            bool hasAvailableOutputs = HasUsableCachedOutputs(node, context);
            bool shouldExecute = forceExecute || !node.CanReuseCachedOutputs || node.Dirty || node.Status == FlowNodeExecutionStatus.Error || inputChanged || !hasAvailableOutputs;
            if (!shouldExecute)
            {
                RestoreOutputsFromSnapshots(node, context);
                LogNodeSkipped(node, inputHash, hasCachedExecutionOutputs, hasPersistedSnapshots);

                if (node.Status == FlowNodeExecutionStatus.Dirty || node.Status == FlowNodeExecutionStatus.Running)
                {
                    node.Status = FlowNodeExecutionStatus.Success;
                }

                return false;
            }

            LogNodeExecuting(node, inputHash, forceExecute, inputChanged, hasAvailableOutputs);
            await ExecuteCurrentNodeAsync(node, context, inputHash);
            return true;
        }

        private static void RestoreOutputsFromSnapshots(FlowNodeModel node, FlowExecutionContext context)
        {
            foreach (FlowNodeOutputSnapshot snapshot in node.OutputSnapshots)
            {
                context.SetNodeOutput(node.Id, snapshot.Key, snapshot.ToRuntimeValue());
            }
        }

        private static bool HasUsableCachedOutputs(FlowNodeModel node, FlowExecutionContext context)
        {
            if (node.OutputPorts.Count == 0)
            {
                return true;
            }

            foreach (FlowPortModel outputPort in node.OutputPorts)
            {
                if (context.TryGetNodeOutput(node.Id, outputPort.Key, out object runtimeValue) && IsUsableOutputValue(runtimeValue))
                {
                    continue;
                }

                FlowNodeOutputSnapshot snapshot = node.OutputSnapshots.FirstOrDefault(candidate => candidate.Key == outputPort.Key);
                if (!IsUsableOutputValue(snapshot?.ToRuntimeValue()))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUsableOutputValue(object value)
        {
            if (value is not FlowImageArtifact imageArtifact)
            {
                return value != null;
            }

            try
            {
                byte[] bytes = FlowProviderExecutionHelpers.DecodeDataUri(imageArtifact.GetPreviewDataUri());
                return bytes != null && bytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private async Task ExecuteCurrentNodeAsync(FlowNodeModel node, FlowExecutionContext context, string inputHash)
        {
            node.Status = FlowNodeExecutionStatus.Running;
            node.LastError = string.Empty;

            string previousOutputsHash = FlowExecutionHashUtility.ComputeHash(string.Join("|", node.OutputSnapshots.Select(snapshot => $"{snapshot.Key}:{snapshot.Summary}:{snapshot.StringValue}")));

            try
            {
                executionState.InvalidateNode(node.Id);
                context.ClearNodeOutputs(node.Id);

                FlowExecutionServices services = new FlowExecutionServices(this, context);
                await node.ExecuteAsync(context, services);

                foreach (FlowPortModel outputPort in node.OutputPorts)
                {
                    if (context.TryGetNodeOutput(node.Id, outputPort.Key, out _))
                    {
                        continue;
                    }

                    object outputValue = node.EvaluateOutput(outputPort.Key, context, services);
                    context.SetNodeOutput(node.Id, outputPort.Key, outputValue);
                }

                Dictionary<string, object> outputs = executionState.CaptureNodeOutputs(node, context);
                node.CaptureOutputsForPersistence(outputs);
                node.LastRunHash = inputHash;
                node.LastExecutionSummary = BuildExecutionSummary(outputs);
                node.Status = FlowNodeExecutionStatus.Success;
                node.Dirty = false;

                string nextOutputsHash = FlowExecutionHashUtility.ComputeHash(string.Join("|", node.OutputSnapshots.Select(snapshot => $"{snapshot.Key}:{snapshot.Summary}:{snapshot.StringValue}")));
                if (!string.Equals(previousOutputsHash, nextOutputsHash, StringComparison.Ordinal))
                {
                    MarkDownstreamNodesDirty(node.Id);
                }
            }
            catch (Exception exception)
            {
                node.Dirty = true;
                node.Status = FlowNodeExecutionStatus.Error;
                node.LastError = exception.Message;
                node.LastExecutionSummary = exception.Message;
                LogNodeFailed(node, exception);
                throw;
            }
        }

        private void LogNodeExecuting(FlowNodeModel node, string inputHash, bool forceExecute, bool inputChanged, bool hasAvailableOutputs)
        {
            if (!enableNodeDebugLogging)
            {
                return;
            }

            List<string> reasons = new List<string>();
            if (forceExecute)
            {
                reasons.Add("forced");
            }

            if (!node.CanReuseCachedOutputs)
            {
                reasons.Add("non-cacheable");
            }

            if (node.Dirty)
            {
                reasons.Add("dirty");
            }

            if (node.Status == FlowNodeExecutionStatus.Error)
            {
                reasons.Add("previous error");
            }

            if (inputChanged)
            {
                reasons.Add("inputs changed");
            }

            if (!hasAvailableOutputs)
            {
                reasons.Add("no cached outputs");
            }

            Debug.Log($"{DebugLogPrefix} Executing node '{node.Title}' ({node.Id}). Reasons: {string.Join(", ", reasons)}. inputHash={inputHash}.");
        }

        private void LogNodeSkipped(FlowNodeModel node, string inputHash, bool hasCachedExecutionOutputs, bool hasPersistedSnapshots)
        {
            if (!enableNodeDebugLogging)
            {
                return;
            }

            List<string> cacheSources = new List<string>();
            if (hasCachedExecutionOutputs)
            {
                cacheSources.Add("execution state");
            }

            if (hasPersistedSnapshots)
            {
                cacheSources.Add("persisted snapshots");
            }

            string cacheDescription = cacheSources.Count > 0 ? string.Join(", ", cacheSources) : "none";
            Debug.Log($"{DebugLogPrefix} Skipping node '{node.Title}' ({node.Id}) because inputs unchanged and cached outputs available. Cache sources: {cacheDescription}. inputHash={inputHash}.");
        }

        private void LogNodeFailed(FlowNodeModel node, Exception exception)
        {
            if (!enableNodeDebugLogging)
            {
                return;
            }

            Debug.LogError($"{DebugLogPrefix} Node '{node.Title}' ({node.Id}) failed: {exception.Message}");
        }

        private string ComputeInputHash(FlowNodeModel node, FlowExecutionContext context)
        {
            List<string> parts = new List<string>
            {
                node.BuildCacheSignature()
            };

            foreach (FlowPortModel inputPort in node.InputPorts.OrderBy(port => port.Key, StringComparer.Ordinal))
            {
                object inputValue = ResolveInputValue(node, inputPort.Key, null, context);
                parts.Add($"{inputPort.Key}={FlowExecutionHashUtility.DescribeValueForHash(inputValue)}");
            }

            return FlowExecutionHashUtility.ComputeHash(parts.ToArray());
        }

        private HashSet<string> CollectNeededNodes(string targetNodeId)
        {
            HashSet<string> needed = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(targetNodeId);

            while (pending.Count > 0)
            {
                string currentNodeId = pending.Dequeue();
                if (!needed.Add(currentNodeId))
                {
                    continue;
                }

                FlowNodeModel node = graphAsset.GetNodeById(currentNodeId);
                if (node == null)
                {
                    continue;
                }

                foreach (FlowPortModel inputPort in node.InputPorts)
                {
                    if (!edgesByInputPort.TryGetValue(inputPort.Id, out List<FlowEdgeModel> incomingEdges))
                    {
                        continue;
                    }

                    foreach (FlowEdgeModel edge in incomingEdges)
                    {
                        pending.Enqueue(edge.OutputNodeId);
                    }
                }
            }

            return needed;
        }

        private List<string> TopologicalSort(HashSet<string> subset)
        {
            Dictionary<string, int> inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (string nodeId in subset)
            {
                inDegree[nodeId] = 0;
                adjacency[nodeId] = new List<string>();
            }

            foreach (FlowEdgeModel edge in graphAsset.Edges)
            {
                if (!subset.Contains(edge.OutputNodeId) || !subset.Contains(edge.InputNodeId))
                {
                    continue;
                }

                adjacency[edge.OutputNodeId].Add(edge.InputNodeId);
                inDegree[edge.InputNodeId] = inDegree[edge.InputNodeId] + 1;
            }

            Queue<string> queue = new Queue<string>(inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            List<string> ordered = new List<string>();

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                ordered.Add(current);
                foreach (string next in adjacency[current])
                {
                    inDegree[next]--;
                    if (inDegree[next] == 0)
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return ordered.Count == subset.Count ? ordered : subset.ToList();
        }

        private string FindCycleDescription()
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> stack = new HashSet<string>(StringComparer.Ordinal);
            List<string> path = new List<string>();

            foreach (FlowNodeModel node in graphAsset.Nodes)
            {
                if (node == null || visited.Contains(node.Id))
                {
                    continue;
                }

                string cycle = FindCycleFromNode(node.Id, visited, stack, path);
                if (!string.IsNullOrWhiteSpace(cycle))
                {
                    return cycle;
                }
            }

            return string.Empty;
        }

        private string FindCycleFromNode(string nodeId, HashSet<string> visited, HashSet<string> stack, List<string> path)
        {
            visited.Add(nodeId);
            stack.Add(nodeId);
            path.Add(nodeId);

            foreach (FlowEdgeModel edge in graphAsset.Edges.Where(candidate => candidate.OutputNodeId == nodeId))
            {
                string nextNodeId = edge.InputNodeId;
                if (!visited.Contains(nextNodeId))
                {
                    string cycle = FindCycleFromNode(nextNodeId, visited, stack, path);
                    if (!string.IsNullOrWhiteSpace(cycle))
                    {
                        return cycle;
                    }
                }
                else if (stack.Contains(nextNodeId))
                {
                    int startIndex = path.IndexOf(nextNodeId);
                    return string.Join(" -> ", path.Skip(startIndex).Concat(new[] { nextNodeId }).Select(id => nodesById.TryGetValue(id, out FlowNodeModel cycleNode) ? cycleNode.Title : id));
                }
            }

            stack.Remove(nodeId);
            path.RemoveAt(path.Count - 1);
            return string.Empty;
        }

        private void MarkDownstreamNodesDirty(string nodeId)
        {
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

                foreach (FlowEdgeModel edge in graphAsset.Edges)
                {
                    if (!string.Equals(edge.OutputNodeId, currentNodeId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    FlowNodeModel downstream = graphAsset.GetNodeById(edge.InputNodeId);
                    if (downstream == null)
                    {
                        continue;
                    }

                    if (downstream.Locked)
                    {
                        continue;
                    }

                    downstream.Dirty = true;
                    downstream.Status = FlowNodeExecutionStatus.Dirty;
                    pending.Enqueue(edge.InputNodeId);
                }
            }
        }

        private void RebuildCaches()
        {
            nodesById.Clear();
            edgesByInputPort.Clear();
            edgesByOutputPort.Clear();

            if (graphAsset == null)
            {
                return;
            }

            graphAsset.EnsureInitialized();
            foreach (FlowNodeModel node in graphAsset.Nodes)
            {
                if (node != null)
                {
                    nodesById[node.Id] = node;
                }
            }

            foreach (FlowEdgeModel edge in graphAsset.Edges)
            {
                if (!edgesByInputPort.TryGetValue(edge.InputPortId, out List<FlowEdgeModel> inputEdges))
                {
                    inputEdges = new List<FlowEdgeModel>();
                    edgesByInputPort[edge.InputPortId] = inputEdges;
                }

                inputEdges.Add(edge);

                if (!edgesByOutputPort.TryGetValue(edge.OutputPortId, out List<FlowEdgeModel> outputEdges))
                {
                    outputEdges = new List<FlowEdgeModel>();
                    edgesByOutputPort[edge.OutputPortId] = outputEdges;
                }

                outputEdges.Add(edge);
            }
        }

        private void SeedVariables(FlowExecutionContext context)
        {
            foreach (FlowVariableModel variable in graphAsset.Variables)
            {
                context.SetVariable(variable.Name, variable.InitialValue.GetValue());
            }
        }

        private static string BuildExecutionSummary(IReadOnlyDictionary<string, object> outputs)
        {
            if (outputs == null || outputs.Count == 0)
            {
                return "Executed";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, object> pair in outputs)
            {
                parts.Add($"{pair.Key}: {DescribeValue(pair.Value)}");
            }

            return string.Join(" | ", parts);
        }

        private static string DescribeValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            return value switch
            {
                string stringValue => stringValue,
                Texture texture => texture.name,
                FlowImageArtifact imageArtifact => imageArtifact.Label,
                FlowVideoArtifact videoArtifact => videoArtifact.Label,
                FlowAudioArtifact audioArtifact => audioArtifact.VoiceName,
                _ => value.ToString()
            };
        }
    }
}
