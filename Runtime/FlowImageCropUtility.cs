using System;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    internal static class FlowImageCropUtility
    {
        public static FlowImageArtifact CropImageArtifact(FlowImageArtifact source, int x, int y, int width, int height, string label)
        {
            if (source == null)
            {
                throw new InvalidOperationException("No image provided");
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Crop width and height must be greater than 0.");
            }

            Texture2D sourceTexture = DecodeArtifactTexture(source);
            if (sourceTexture == null || sourceTexture.width <= 0 || sourceTexture.height <= 0)
            {
                throw new InvalidOperationException("Crop requires an image input.");
            }

            Color32[] sourcePixels = sourceTexture.GetPixels32();
            Color32[] destinationPixels = new Color32[width * height];

            for (int row = 0; row < height; row++)
            {
                int sourceYFromTop = y + row;
                if (sourceYFromTop < 0 || sourceYFromTop >= sourceTexture.height)
                {
                    continue;
                }

                int sourceYFromBottom = sourceTexture.height - 1 - sourceYFromTop;
                int destinationYFromBottom = height - 1 - row;

                for (int column = 0; column < width; column++)
                {
                    int sourceX = x + column;
                    if (sourceX < 0 || sourceX >= sourceTexture.width)
                    {
                        continue;
                    }

                    destinationPixels[(destinationYFromBottom * width) + column] =
                        sourcePixels[(sourceYFromBottom * sourceTexture.width) + sourceX];
                }
            }

            Texture2D outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            outputTexture.SetPixels32(destinationPixels);
            outputTexture.Apply(false, false);

            byte[] pngBytes = outputTexture.EncodeToPNG();
            string dataUri = pngBytes == null || pngBytes.Length == 0
                ? string.Empty
                : $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";

            return new FlowImageArtifact
            {
                Label = string.IsNullOrWhiteSpace(label) ? "Crop" : label,
                Prompt = source.Prompt,
                SourceKind = "crop",
                PreviewTexture = outputTexture,
                Metadata = $"{width}x{height} @ ({x},{y})",
                DataUri = dataUri,
                MimeType = "image/png",
                ModelId = source.ModelId
            };
        }

        public static Texture2D DecodeArtifactTexture(FlowImageArtifact source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.PreviewTexture != null && source.PreviewTexture.isReadable)
            {
                return source.PreviewTexture;
            }

            string dataUri = source.GetPreviewDataUri();
            int commaIndex = string.IsNullOrWhiteSpace(dataUri) ? -1 : dataUri.IndexOf(',');
            if (commaIndex < 0 || commaIndex >= dataUri.Length - 1)
            {
                return null;
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(dataUri.Substring(commaIndex + 1));
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(imageBytes, false))
                {
                    return null;
                }

                texture.name = "FlowCropSource";
                return texture;
            }
            catch
            {
                return null;
            }
        }
    }
}
