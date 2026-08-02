using NUnit.Framework;
using UnityEngine;

namespace Myrvo.FlowGraph.Tests
{
    public sealed class FlowExecutionHashUtilityTests
    {
        [Test]
        public void DescribeValueForHash_DestroyedTexture_ReturnsNullObjectMarker()
        {
            Texture2D texture = new Texture2D(2, 2);
            Object.DestroyImmediate(texture);

            Assert.That(FlowExecutionHashUtility.DescribeValueForHash(texture), Is.EqualTo("<null-object>"));
        }
    }
}
