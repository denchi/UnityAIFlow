using System.Collections.Generic;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    [System.Serializable]
    [FlowNode("Debug/Log", "Debug Log", "Debug")]
    public sealed class FlowDebugLogNodeModel : FlowNodeModel
    {
        [SerializeField] private string prefix = "FlowGraph";
        [SerializeField] private FlowSerializedValue fallbackMessage;

        public FlowDebugLogNodeModel()
        {
            SetTitle("Debug Log");
            fallbackMessage.ValueType = FlowValueType.String;
            fallbackMessage.StringValue = "Message";
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("in", "In", FlowValueType.Trigger);
            yield return new FlowPortDescriptor("message", "Message", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("out", "Out", FlowValueType.Trigger);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            object message = services.GetInputValue(this, "message", fallbackMessage.GetValue());
            Debug.Log($"{prefix}: {message}");
            services.RunNext(this, "out");
        }
    }
}
