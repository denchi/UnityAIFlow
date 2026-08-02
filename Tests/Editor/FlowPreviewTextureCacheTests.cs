using NUnit.Framework;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor.Tests
{
    public sealed class FlowPreviewTextureCacheTests
    {
        private const string OnePixelPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL9GQAAAABJRU5ErkJggg==";

        [SetUp]
        public void SetUp()
        {
            FlowPreviewTextureCache.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            FlowPreviewTextureCache.ResetForTests();
        }

        [Test]
        public void Get_ReusesTheDecodedTextureForUnchangedPreviewData()
        {
            Texture2D first = FlowPreviewTextureCache.Get(OnePixelPng);
            Texture2D second = FlowPreviewTextureCache.Get(OnePixelPng);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(FlowPreviewTextureCache.DecodeCount, Is.EqualTo(1));
        }
    }
}
