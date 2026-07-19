using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Myrvo.FlowGraph.Tests
{
    public sealed class FlowGraphRunnerDebugLoggingTests
    {
        [SetUp]
        public void SetUp()
        {
            FlowGraphDebugSettingsLocator.RegisterResolver(() => new FlowGraphDebugSettings
            {
                EnableNodeDebugLogging = true
            });
        }

        [TearDown]
        public void TearDown()
        {
            FlowGraphDebugSettingsLocator.RegisterResolver(null);
        }

        [Test]
        public void ExecuteNode_LogsWhenNodeRunsAndWhenNodeIsSkipped()
        {
            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                FlowTextInputNodeModel node = graphAsset.CreateNode<FlowTextInputNodeModel>(Vector2.zero);
                FlowGraphExecutionState executionState = new FlowGraphExecutionState();
                FlowGraphRunner runner = new FlowGraphRunner(graphAsset, executionState);

                LogAssert.Expect(LogType.Log, new Regex(@"\[FlowGraph\] Executing node 'Text Input'.*dirty.*inputs changed.*no cached outputs", RegexOptions.Singleline));
                runner.ExecuteNode(node);

                LogAssert.Expect(LogType.Log, new Regex(@"\[FlowGraph\] Skipping node 'Text Input'.*inputs unchanged.*cached outputs available", RegexOptions.Singleline));
                runner.ExecuteNode(node);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void ExecuteNode_LogsNodeFailureReason()
        {
            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                ThrowingNodeModel node = graphAsset.CreateNode<ThrowingNodeModel>(Vector2.zero);
                FlowGraphRunner runner = new FlowGraphRunner(graphAsset);

                LogAssert.Expect(LogType.Log, new Regex(@"\[FlowGraph\] Executing node 'Throwing Node'.*dirty.*inputs changed.*no cached outputs", RegexOptions.Singleline));
                LogAssert.Expect(LogType.Error, new Regex(@"\[FlowGraph\] Node 'Throwing Node'.*failed: Boom", RegexOptions.Singleline));
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.ExecuteNode(node));
                Assert.That(exception.Message, Is.EqualTo("Boom"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(graphAsset);
            }
        }

        [Serializable]
        private sealed class ThrowingNodeModel : FlowNodeModel
        {
            public ThrowingNodeModel()
            {
                SetTitle("Throwing Node");
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
            {
                yield break;
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
            {
                yield break;
            }

            public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
            {
                throw new InvalidOperationException("Boom");
            }
        }
    }
}
