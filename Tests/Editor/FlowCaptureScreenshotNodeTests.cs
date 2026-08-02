using System.Linq;
using NUnit.Framework;

namespace Myrvo.FlowGraph.Tests
{
    public sealed class FlowCaptureScreenshotNodeTests
    {
        [Test]
        public void CaptureScreenshotNode_IsRegisteredAsASourceNode()
        {
            FlowNodeRegistration registration = FlowNodeRegistry.Registrations
                .Single(entry => entry.NodeType == typeof(FlowCaptureScreenshotNodeModel));

            Assert.That(registration.MenuPath, Is.EqualTo("Source/Capture Screenshot"));
            Assert.That(registration.DisplayName, Is.EqualTo("Capture Screenshot"));
            Assert.That(registration.Category, Is.EqualTo("Source"));
        }

        [Test]
        public void CaptureScreenshotNode_HasOneImageOutputAndNoInputs()
        {
            FlowCaptureScreenshotNodeModel node = new FlowCaptureScreenshotNodeModel();

            Assert.That(node.CanReuseCachedOutputs, Is.False);
            Assert.That(node.InputPorts, Is.Empty);
            Assert.That(node.OutputPorts, Has.Count.EqualTo(1));
            Assert.That(node.OutputPorts[0].Key, Is.EqualTo("image"));
            Assert.That(node.OutputPorts[0].ValueType, Is.EqualTo(FlowValueType.Object));
        }
    }
}
