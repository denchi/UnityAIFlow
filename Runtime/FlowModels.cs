using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    [Serializable]
    public sealed class FlowPortModel
    {
        [SerializeField] private string id;
        [SerializeField] private string key;
        [SerializeField] private string displayName;
        [SerializeField] private FlowPortDirection direction;
        [SerializeField] private FlowValueType valueType;
        [SerializeField] private bool allowsMultipleConnections;

        public string Id => id;
        public string Key => key;
        public string DisplayName => displayName;
        public FlowPortDirection Direction => direction;
        public FlowValueType ValueType => valueType;
        public bool AllowsMultipleConnections => allowsMultipleConnections;

        public void Initialize(string nodeId, string portKey, string portName, FlowPortDirection portDirection, FlowValueType portValueType, bool multiConnection)
        {
            key = portKey;
            displayName = portName;
            direction = portDirection;
            valueType = portValueType;
            allowsMultipleConnections = multiConnection;
            id = $"{nodeId}:{portKey}";
        }
    }

    [Serializable]
    public sealed class FlowEdgeModel
    {
        [SerializeField] private string id;
        [SerializeField] private string outputNodeId;
        [SerializeField] private string outputPortId;
        [SerializeField] private string inputNodeId;
        [SerializeField] private string inputPortId;

        public string Id => id;
        public string OutputNodeId => outputNodeId;
        public string OutputPortId => outputPortId;
        public string InputNodeId => inputNodeId;
        public string InputPortId => inputPortId;

        public void Initialize(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
        {
            id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
            outputNodeId = sourceNodeId;
            outputPortId = sourcePortId;
            inputNodeId = targetNodeId;
            inputPortId = targetPortId;
        }
    }

    [Serializable]
    public sealed class FlowVariableModel
    {
        [SerializeField] private string id;
        [SerializeField] private string name;
        [SerializeField] private FlowSerializedValue initialValue;

        public string Id
        {
            get
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }

                return id;
            }
        }

        public string Name
        {
            get => name;
            set => name = value;
        }

        public FlowSerializedValue InitialValue
        {
            get => initialValue;
            set => initialValue = value;
        }
    }

    public readonly struct FlowPortDescriptor
    {
        public FlowPortDescriptor(string key, string displayName, FlowValueType valueType, bool allowsMultipleConnections = false)
        {
            Key = key;
            DisplayName = displayName;
            ValueType = valueType;
            AllowsMultipleConnections = allowsMultipleConnections;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public FlowValueType ValueType { get; }
        public bool AllowsMultipleConnections { get; }
    }

    [Serializable]
    public abstract class FlowNodeModel
    {
        private static readonly string[] NonConfigFieldNames =
        {
            "id",
            "title",
            "position",
            "dirty",
            "lastExecutionSummary",
            "inputPorts",
            "outputPorts",
            "status",
            "lastError",
            "lastRunHash",
            "lastPreviewData",
            "lastPreviewMimeType",
            "outputSnapshots"
        };

        [SerializeField] private string id;
        [SerializeField] private string title;
        [SerializeField] private Vector2 position;
        [SerializeField] private bool dirty = true;
        [SerializeField] private string lastExecutionSummary;
        [SerializeField] private FlowNodeExecutionStatus status = FlowNodeExecutionStatus.Dirty;
        [SerializeField] private string lastError;
        [SerializeField] private string lastRunHash;
        [SerializeField] private string lastPreviewData;
        [SerializeField] private string lastPreviewMimeType;
        [SerializeField] private List<FlowNodeOutputSnapshot> outputSnapshots = new List<FlowNodeOutputSnapshot>();
        [SerializeField] private List<FlowPortModel> inputPorts = new List<FlowPortModel>();
        [SerializeField] private List<FlowPortModel> outputPorts = new List<FlowPortModel>();

        public string Id
        {
            get
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }

                return id;
            }
        }

        public string Title => title;
        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        public IReadOnlyList<FlowPortModel> InputPorts => inputPorts;
        public IReadOnlyList<FlowPortModel> OutputPorts => outputPorts;
        public bool Dirty
        {
            get => dirty;
            set
            {
                dirty = value;
                if (dirty && status != FlowNodeExecutionStatus.Running)
                {
                    status = FlowNodeExecutionStatus.Dirty;
                }
            }
        }
        public string LastExecutionSummary
        {
            get => lastExecutionSummary;
            set => lastExecutionSummary = value;
        }
        public FlowNodeExecutionStatus Status
        {
            get => status;
            set => status = value;
        }
        public string LastError
        {
            get => lastError;
            set => lastError = value;
        }
        public string LastRunHash
        {
            get => lastRunHash;
            set => lastRunHash = value;
        }
        public string LastPreviewData
        {
            get => lastPreviewData;
            set => lastPreviewData = value;
        }
        public string LastPreviewMimeType
        {
            get => lastPreviewMimeType;
            set => lastPreviewMimeType = value;
        }
        public IReadOnlyList<FlowNodeOutputSnapshot> OutputSnapshots => outputSnapshots;

        protected void SetTitle(string newTitle)
        {
            title = newTitle;
        }

        public void EnsureInitialized()
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                title = GetType().Name;
            }

            RebuildPorts();
        }

        public FlowPortModel GetPortById(string portId)
        {
            foreach (FlowPortModel port in inputPorts)
            {
                if (port.Id == portId)
                {
                    return port;
                }
            }

            foreach (FlowPortModel port in outputPorts)
            {
                if (port.Id == portId)
                {
                    return port;
                }
            }

            return null;
        }

        public FlowPortModel GetPortById(string portId, FlowPortDirection direction)
        {
            IReadOnlyList<FlowPortModel> collection = direction == FlowPortDirection.Input ? inputPorts : outputPorts;
            foreach (FlowPortModel port in collection)
            {
                if (port.Id == portId)
                {
                    return port;
                }
            }

            return null;
        }

        public FlowPortModel GetPortByKey(string portKey, FlowPortDirection direction)
        {
            IReadOnlyList<FlowPortModel> collection = direction == FlowPortDirection.Input ? inputPorts : outputPorts;
            foreach (FlowPortModel port in collection)
            {
                if (port.Key == portKey)
                {
                    return port;
                }
            }

            return null;
        }

        protected abstract IEnumerable<FlowPortDescriptor> DescribeInputs();
        protected abstract IEnumerable<FlowPortDescriptor> DescribeOutputs();

        public virtual void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
        }

        public virtual Task ExecuteAsync(FlowExecutionContext context, FlowExecutionServices services)
        {
            Execute(context, services);
            return Task.CompletedTask;
        }

        public virtual object EvaluateOutput(string portKey, FlowExecutionContext context, FlowExecutionServices services)
        {
            return null;
        }

        public virtual FlowModelCapability GetPrimaryCapability()
        {
            return FlowModelCapability.None;
        }

        public virtual string GetModelId()
        {
            return string.Empty;
        }

        public virtual IReadOnlyList<FlowProviderRequestKind> GetSupportedRequestKinds()
        {
            return Array.Empty<FlowProviderRequestKind>();
        }

        public virtual void CaptureOutputsForPersistence(IReadOnlyDictionary<string, object> outputs)
        {
            outputSnapshots.Clear();
            LastPreviewData = string.Empty;
            LastPreviewMimeType = string.Empty;

            if (outputs == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> pair in outputs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                FlowNodeOutputSnapshot snapshot = FlowNodeOutputSnapshot.Create(pair.Key, pair.Value);
                outputSnapshots.Add(snapshot);
                if (string.IsNullOrWhiteSpace(LastPreviewData) && !string.IsNullOrWhiteSpace(snapshot.PreviewData))
                {
                    LastPreviewData = snapshot.PreviewData;
                    LastPreviewMimeType = snapshot.MimeType;
                }
            }
        }

        public virtual string BuildCacheSignature()
        {
            StringBuilder builder = new StringBuilder();
            foreach (FieldInfo field in EnumerateSerializableConfigFields().OrderBy(info => info.Name, StringComparer.Ordinal))
            {
                object value = field.GetValue(this);
                builder.Append(field.Name);
                builder.Append('=');
                builder.Append(FlowExecutionHashUtility.DescribeValueForHash(value));
                builder.Append(';');
            }

            return builder.ToString();
        }

        private IEnumerable<FieldInfo> EnumerateSerializableConfigFields()
        {
            Type currentType = GetType();
            while (currentType != null && currentType != typeof(object))
            {
                FieldInfo[] fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                    {
                        continue;
                    }

                    if (NonConfigFieldNames.Contains(field.Name))
                    {
                        continue;
                    }

                    yield return field;
                }

                currentType = currentType.BaseType;
            }
        }

        private void RebuildPorts()
        {
            inputPorts = BuildPorts(DescribeInputs(), FlowPortDirection.Input);
            outputPorts = BuildPorts(DescribeOutputs(), FlowPortDirection.Output);
        }

        private List<FlowPortModel> BuildPorts(IEnumerable<FlowPortDescriptor> descriptors, FlowPortDirection direction)
        {
            List<FlowPortModel> ports = new List<FlowPortModel>();
            foreach (FlowPortDescriptor descriptor in descriptors)
            {
                FlowPortModel port = new FlowPortModel();
                port.Initialize(Id, descriptor.Key, descriptor.DisplayName, direction, descriptor.ValueType, descriptor.AllowsMultipleConnections);
                ports.Add(port);
            }

            return ports;
        }
    }
}
