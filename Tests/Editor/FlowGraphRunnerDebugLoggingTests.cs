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

        [Test]
        public void ExecuteNode_RunsNonCacheableNodeWhenInputsAreUnchanged()
        {
            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                NonCacheableNodeModel node = graphAsset.CreateNode<NonCacheableNodeModel>(Vector2.zero);
                FlowGraphRunner runner = new FlowGraphRunner(graphAsset);

                LogAssert.Expect(LogType.Log, new Regex(@"\[FlowGraph\] Executing node 'Non-cacheable Node'.*non-cacheable", RegexOptions.Singleline));
                runner.ExecuteNode(node);

                LogAssert.Expect(LogType.Log, new Regex(@"\[FlowGraph\] Executing node 'Non-cacheable Node'.*non-cacheable", RegexOptions.Singleline));
                runner.ExecuteNode(node);

                Assert.That(node.ExecutionCount, Is.EqualTo(2));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void ExecuteNodeForcedAsync_RunsCacheableTargetWhenOutputsAreCached()
        {
            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                FlowTextInputNodeModel node = graphAsset.CreateNode<FlowTextInputNodeModel>(Vector2.zero);
                FlowGraphRunner runner = new FlowGraphRunner(graphAsset);
                runner.ExecuteNode(node);

                FlowNodeExecutionResult result = runner.ExecuteNodeForcedAsync(node).GetAwaiter().GetResult();

                Assert.That(result.Executed, Is.True);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void LockedNode_IgnoresDirtyPropagationAndExecutionUntilUnlocked()
        {
            FlowGraphAsset graphAsset = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                LockableNodeModel node = graphAsset.CreateNode<LockableNodeModel>(Vector2.zero);
                FlowGraphRunner runner = new FlowGraphRunner(graphAsset);

                node.LastExecutionSummary = "Frozen output";
                node.Locked = true;
                graphAsset.MarkAllNodesDirty();
                runner.ExecuteNode(node);

                Assert.That(node.Dirty, Is.False);
                Assert.That(node.Status, Is.EqualTo(FlowNodeExecutionStatus.Success));
                Assert.That(node.LastExecutionSummary, Is.EqualTo("Frozen output"));
                Assert.That(node.ExecutionCount, Is.EqualTo(0));

                node.Locked = false;
                node.Dirty = true;
                runner.ExecuteNode(node);

                Assert.That(node.ExecutionCount, Is.EqualTo(1));
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

        [Serializable]
        private sealed class NonCacheableNodeModel : FlowNodeModel
        {
            public int ExecutionCount { get; private set; }
            public override bool CanReuseCachedOutputs => false;

            public NonCacheableNodeModel()
            {
                SetTitle("Non-cacheable Node");
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
            {
                yield break;
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
            {
                yield return new FlowPortDescriptor("value", "Value", FlowValueType.Number);
            }

            public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
            {
                ExecutionCount++;
                services.SetOutputValue(this, "value", ExecutionCount);
            }
        }

        [Serializable]
        private sealed class LockableNodeModel : FlowNodeModel
        {
            public int ExecutionCount { get; private set; }

            public LockableNodeModel()
            {
                SetTitle("Lockable Node");
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
            {
                yield break;
            }

            protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
            {
                yield return new FlowPortDescriptor("value", "Value", FlowValueType.Number);
            }

            public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
            {
                ExecutionCount++;
                services.SetOutputValue(this, "value", ExecutionCount);
            }
        }
    }
}
