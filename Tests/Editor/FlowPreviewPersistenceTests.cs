using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Myrvo.FlowGraph.Tests
{
    public sealed class FlowPreviewPersistenceTests
    {
        private const string OnePixelPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL9GQAAAABJRU5ErkJggg==";

        [Test]
        public void ImageSnapshot_UsesDataUriAsPreviewWithoutDuplicatingIt()
        {
            FlowImageArtifact image = new FlowImageArtifact
            {
                Label = "Generated image",
                DataUri = OnePixelPng,
                MimeType = "image/png"
            };

            FlowNodeOutputSnapshot snapshot = FlowNodeOutputSnapshot.Create("image", image);

            Assert.That(snapshot.PreviewData, Is.Empty);
            Assert.That(snapshot.GetPreviewData(), Is.SameAs(snapshot.StringValue));
            FlowImageArtifact rehydrated = snapshot.ToRuntimeValue() as FlowImageArtifact;
            Assert.That(rehydrated, Is.Not.Null);
            Assert.That(rehydrated.DataUri, Is.EqualTo(OnePixelPng));
        }

        [Test]
        public void ImageSnapshot_EncodesPreviewTextureWhenDataUriIsMissing()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixel(0, 0, Color.magenta);
                texture.Apply();
                FlowImageArtifact image = new FlowImageArtifact
                {
                    Label = "Texture-only image",
                    PreviewTexture = texture,
                    MimeType = "image/png"
                };

                FlowNodeOutputSnapshot snapshot = FlowNodeOutputSnapshot.Create("image", image);

                Assert.That(snapshot.StringValue, Does.StartWith("data:image/png;base64,"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void VideoSnapshot_PreservesItsDistinctPreview()
        {
            FlowVideoArtifact video = new FlowVideoArtifact
            {
                Label = "Generated video",
                DataUri = "data:video/mp4;base64,AA==",
                PreviewData = OnePixelPng,
                MimeType = "video/mp4"
            };

            FlowNodeOutputSnapshot snapshot = FlowNodeOutputSnapshot.Create("video", video);

            Assert.That(snapshot.PreviewData, Is.EqualTo(OnePixelPng));
            Assert.That(snapshot.GetPreviewData(), Is.EqualTo(OnePixelPng));
        }

        [Test]
        public void OptimizePersistedPreviewData_RemovesLegacyImagePreviewOnce()
        {
            FlowGraphAsset graph = ScriptableObject.CreateInstance<FlowGraphAsset>();
            try
            {
                FlowTextInputNodeModel node = graph.CreateNode<FlowTextInputNodeModel>(Vector2.zero);
                FlowImageArtifact image = new FlowImageArtifact
                {
                    DataUri = OnePixelPng,
                    MimeType = "image/png"
                };
                node.CaptureOutputsForPersistence(new Dictionary<string, object> { ["image"] = image });
                FlowNodeOutputSnapshot snapshot = node.OutputSnapshots[0];
                typeof(FlowNodeOutputSnapshot)
                    .GetField("previewData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(snapshot, OnePixelPng);

                Assert.That(graph.OptimizePersistedPreviewData(), Is.True);
                Assert.That(snapshot.PreviewData, Is.Empty);
                Assert.That(snapshot.GetPreviewData(), Is.EqualTo(OnePixelPng));
                Assert.That(graph.OptimizePersistedPreviewData(), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }
    }
}
