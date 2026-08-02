using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace Myrvo.FlowGraph
{
    [System.Serializable]
    public abstract class FlowCompanionNodeModel : FlowNodeModel
    {
        protected string ReadString(FlowExecutionServices services, string inputKey, string fallbackValue = "")
        {
            return services.GetInputValue(this, inputKey, fallbackValue)?.ToString() ?? fallbackValue;
        }

        protected float ReadNumber(FlowExecutionServices services, string inputKey, float fallbackValue = 0f)
        {
            object value = services.GetInputValue(this, inputKey, fallbackValue);
            return value switch
            {
                float floatValue => floatValue,
                int intValue => intValue,
                double doubleValue => (float)doubleValue,
                _ => fallbackValue
            };
        }

        protected T ReadObject<T>(FlowExecutionServices services, string inputKey) where T : class
        {
            return services.GetInputValue(this, inputKey) as T;
        }

        protected void StoreOutput(FlowExecutionServices services, string outputKey, object value)
        {
            services.SetOutputValue(this, outputKey, value);
        }

        protected T GetStoredOutput<T>(FlowExecutionServices services, string outputKey) where T : class
        {
            return services.GetOutputValue(this, outputKey) as T;
        }

        public override object EvaluateOutput(string portKey, FlowExecutionContext context, FlowExecutionServices services)
        {
            return services.GetOutputValue(this, portKey);
        }
    }

    [System.Serializable]
    public abstract class FlowImageGeneratorNodeModel : FlowCompanionNodeModel
    {
        [FormerlySerializedAs("modelName")]
        [SerializeField] private string model = "google/gemini-2.5-flash-image";
        [SerializeField] private string aspectRatio = "1:1";
        [SerializeField] private string imageSize = "1K";
        [SerializeField] private bool transparent = true;

        protected string Model => model;
        protected string AspectRatio => aspectRatio;
        protected string ImageSize => imageSize;
        protected bool Transparent => transparent;

        protected abstract FlowProviderRequestKind RequestKind { get; }
        protected abstract string DefaultPrompt { get; }

        public override FlowModelCapability GetPrimaryCapability()
        {
            return FlowModelCapability.Image;
        }

        public override string GetModelId()
        {
            return model;
        }

        public override IReadOnlyList<FlowProviderRequestKind> GetSupportedRequestKinds()
        {
            return new[] { RequestKind };
        }

        protected async Task<FlowImageArtifact> ExecuteImageGenerationAsync(string prompt, IReadOnlyList<FlowImageArtifact> images, IReadOnlyDictionary<string, object> options = null)
        {
            FlowModelDefinition modelDefinition = FlowProviderPluginRegistry.GetModel(model);
            if (modelDefinition == null)
            {
                throw new InvalidOperationException($"Model '{model}' is not available from the active provider plugins.");
            }

            if (!modelDefinition.SupportsRequestKind(RequestKind))
            {
                throw new InvalidOperationException($"Model '{model}' does not support request kind '{RequestKind}'.");
            }

            FlowProviderRequest request = new FlowProviderRequest
            {
                RequestKind = RequestKind,
                Model = modelDefinition,
                Prompt = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt,
                AspectRatio = aspectRatio,
                ImageSize = imageSize,
                Transparent = transparent,
                Images = images,
                Options = options
            };

            FlowProviderExecutionService service = new FlowProviderExecutionService();
            FlowImageArtifact artifact = await service.ExecuteImageRequestAsync(request);
            if (artifact == null)
            {
                throw new InvalidOperationException($"Model '{model}' completed the {RequestKind} request without returning an image artifact.");
            }

            return artifact;
        }
    }

    [System.Serializable]
    [FlowNode("Source/Text Input", "Text Input", "Source")]
    public sealed class FlowTextInputNodeModel : FlowNodeModel
    {
        [SerializeField, TextArea(3, 8)] private string text = "Describe your image here";

        public string Text => text;

        public FlowTextInputNodeModel()
        {
            SetTitle("Text Input");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield break;
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("text", "Text", FlowValueType.String);
        }

        public override object EvaluateOutput(string portKey, FlowExecutionContext context, FlowExecutionServices services)
        {
            return portKey == "text" ? text : null;
        }
    }

    [System.Serializable]
    public abstract class FlowImageSourceNodeModel : FlowNodeModel
    {
        [SerializeField] private Texture2D image;
        [SerializeField] private string description = "Image input";

        protected abstract string SourceKind { get; }

        public string DescriptionText => description;

        public Texture2D Image
        {
            get => image;
            set => image = value;
        }

        protected string Description => description;

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield break;
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override object EvaluateOutput(string portKey, FlowExecutionContext context, FlowExecutionServices services)
        {
            if (portKey != "image")
            {
                return null;
            }

            string dataUri = image != null ? EncodeTextureToDataUri(image) : string.Empty;
            return new FlowImageArtifact
            {
                Label = Title,
                Prompt = description,
                SourceKind = SourceKind,
                PreviewTexture = image,
                Metadata = image != null ? image.name : "No texture assigned",
                DataUri = dataUri,
                MimeType = GetMimeType(dataUri)
            };
        }

        private static string EncodeTextureToDataUri(Texture2D texture)
        {
            if (texture == null)
            {
                return string.Empty;
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
            {
                try
                {
                    byte[] sourceBytes = File.ReadAllBytes(assetPath);
                    if (sourceBytes.Length > 0)
                    {
                        return $"data:{GetMimeTypeFromExtension(Path.GetExtension(assetPath))};base64,{Convert.ToBase64String(sourceBytes)}";
                    }
                }
                catch (IOException)
                {
                    // Fall back to Unity's texture encoding path below.
                }
                catch (UnauthorizedAccessException)
                {
                    // Fall back to Unity's texture encoding path below.
                }
            }
#endif

            try
            {
                byte[] bytes = texture.isReadable
                    ? texture.EncodeToPNG()
                    : EncodeNonReadableTextureToPng(texture);
                return bytes == null || bytes.Length == 0 ? string.Empty : $"data:image/png;base64,{System.Convert.ToBase64String(bytes)}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetMimeType(string dataUri)
        {
            if (!string.IsNullOrWhiteSpace(dataUri) && dataUri.StartsWith("data:image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return "image/jpeg";
            }

            if (!string.IsNullOrWhiteSpace(dataUri) && dataUri.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase))
            {
                return "image/webp";
            }

            return "image/png";
        }

        private static string GetMimeTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "image/png"
            };
        }

        private static byte[] EncodeNonReadableTextureToPng(Texture2D texture)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D readableCopy = null;

            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readableCopy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readableCopy.ReadPixels(new Rect(0f, 0f, temporary.width, temporary.height), 0, 0);
                readableCopy.Apply(false, false);
                return readableCopy.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);

                if (readableCopy != null)
                {
                    UnityEngine.Object.DestroyImmediate(readableCopy);
                }
            }
        }
    }

    [System.Serializable]
    [FlowNode("Source/Image", "Image", "Source")]
    public sealed class FlowImageUploadNodeModel : FlowImageSourceNodeModel
    {
        public FlowImageUploadNodeModel()
        {
            SetTitle("Image");
        }

        protected override string SourceKind => "image-upload";
    }

    [System.Serializable]
    [FlowNode("Source/Capture Screenshot", "Capture Screenshot", "Source")]
    public sealed class FlowCaptureScreenshotNodeModel : FlowCompanionNodeModel
    {
        [SerializeField, Range(1, 4)] private int superSize = 1;

        public int SuperSize
        {
            get => superSize;
            set => superSize = Mathf.Clamp(value, 1, 4);
        }

        public FlowCaptureScreenshotNodeModel()
        {
            SetTitle("Capture Screenshot");
        }

        public override bool CanReuseCachedOutputs => false;

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield break;
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            Texture2D screenshot = CaptureScreenshot(Mathf.Clamp(superSize, 1, 4));
            if (screenshot == null)
            {
                throw new InvalidOperationException("Unable to capture the current game screen.");
            }

            byte[] pngBytes = screenshot.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
            {
                UnityEngine.Object.Destroy(screenshot);
                throw new InvalidOperationException("Unable to encode the captured screenshot as PNG.");
            }

            FlowImageArtifact artifact = new FlowImageArtifact
            {
                Label = Title,
                SourceKind = "screenshot",
                PreviewTexture = screenshot,
                Metadata = $"{screenshot.width} x {screenshot.height} game screenshot",
                DataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}",
                MimeType = "image/png"
            };

            StoreOutput(services, "image", artifact);
        }

        private static Texture2D CaptureScreenshot(int resolutionScale)
        {
#if UNITY_EDITOR
            return CaptureEditorGameView(resolutionScale);
#else
            return ScreenCapture.CaptureScreenshotAsTexture(resolutionScale);
#endif
        }

#if UNITY_EDITOR
        private static Texture2D CaptureEditorGameView(int resolutionScale)
        {
            EditorWindow gameView = FindGameView();
            if (gameView == null)
            {
                throw new InvalidOperationException("Capture Screenshot requires an open Game view.");
            }

            Rect targetInView = GetGameViewTargetRect(gameView);
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(targetInView.width * pixelsPerPoint);
            int height = Mathf.RoundToInt(targetInView.height * pixelsPerPoint);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("The Game view does not have a valid drawable size.");
            }

            Vector2 screenPosition = (gameView.position.position + targetInView.position) * pixelsPerPoint;
            Color[] pixels = InternalEditorUtility.ReadScreenPixel(screenPosition, width, height);
            if (pixels == null || pixels.Length != width * height)
            {
                throw new InvalidOperationException("Unable to read pixels from the Game view. Ensure the Game view is visible.");
            }

            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            screenshot.SetPixels(pixels);
            screenshot.Apply(false, false);
            return ScaleEditorScreenshot(screenshot, resolutionScale);
        }

        private static Rect GetGameViewTargetRect(EditorWindow gameView)
        {
            PropertyInfo targetInView = gameView.GetType().GetProperty("targetInView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetInView?.GetValue(gameView) is Rect targetRect && targetRect.width > 0f && targetRect.height > 0f)
            {
                return targetRect;
            }

            throw new InvalidOperationException("Unity could not determine the Game view's rendered image area.");
        }

        private static EditorWindow FindGameView()
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window != null && window.GetType().FullName == "UnityEditor.GameView")
                {
                    return window;
                }
            }

            return null;
        }

        private static Texture2D ScaleEditorScreenshot(Texture2D source, int resolutionScale)
        {
            if (resolutionScale <= 1)
            {
                return source;
            }

            int width = source.width * resolutionScale;
            int height = source.height * resolutionScale;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D scaled = null;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                scaled = new Texture2D(width, height, TextureFormat.RGBA32, false);
                scaled.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                scaled.Apply(false, false);
                return scaled;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
#endif
    }

    [System.Serializable]
    [FlowNode("Generator/Text to Image", "Text to Image", "Generator")]
    public sealed class FlowTextToImageNodeModel : FlowImageGeneratorNodeModel
    {
        [SerializeField] private string fallbackPrompt = "Concept art scene";

        protected override FlowProviderRequestKind RequestKind => FlowProviderRequestKind.TextToImage;
        protected override string DefaultPrompt => fallbackPrompt;

        public FlowTextToImageNodeModel()
        {
            SetTitle("Text to Image");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override async Task ExecuteAsync(FlowExecutionContext context, FlowExecutionServices services)
        {
            string prompt = ReadString(services, "prompt", fallbackPrompt);
            FlowImageArtifact artifact = await ExecuteImageGenerationAsync(prompt, Array.Empty<FlowImageArtifact>());
            StoreOutput(services, "image", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Generator/Image to Image", "Image to Image", "Generator")]
    public sealed class FlowImageToImageNodeModel : FlowImageGeneratorNodeModel
    {
        [SerializeField] private string fallbackPrompt = "Refine the input image";
        [SerializeField, Range(0f, 1f)] private float strength = 0.65f;

        protected override FlowProviderRequestKind RequestKind => FlowProviderRequestKind.ImageToImage;
        protected override string DefaultPrompt => fallbackPrompt;

        public FlowImageToImageNodeModel()
        {
            SetTitle("Image to Image");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override async Task ExecuteAsync(FlowExecutionContext context, FlowExecutionServices services)
        {
            FlowImageArtifact source = ReadObject<FlowImageArtifact>(services, "image");
            string prompt = ReadString(services, "prompt", fallbackPrompt);
            FlowImageArtifact artifact = await ExecuteImageGenerationAsync(
                prompt,
                source != null ? new[] { source } : Array.Empty<FlowImageArtifact>(),
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["strength"] = strength
                });
            StoreOutput(services, "image", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Generator/Image to Image (2 Inputs)", "Image to Image (2 Inputs)", "Generator")]
    public sealed class FlowImageToImageTwoInputsNodeModel : FlowImageGeneratorNodeModel
    {
        [SerializeField] private string fallbackPrompt = "Blend source image with style reference";

        protected override FlowProviderRequestKind RequestKind => FlowProviderRequestKind.ImageToImageTwoInputs;
        protected override string DefaultPrompt => fallbackPrompt;

        public FlowImageToImageTwoInputsNodeModel()
        {
            SetTitle("Image to Image (2 Inputs)");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("sourceImage", "Source Image", FlowValueType.Object);
            yield return new FlowPortDescriptor("styleImage", "Style Image", FlowValueType.Object);
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override async Task ExecuteAsync(FlowExecutionContext context, FlowExecutionServices services)
        {
            FlowImageArtifact source = ReadObject<FlowImageArtifact>(services, "sourceImage");
            FlowImageArtifact style = ReadObject<FlowImageArtifact>(services, "styleImage");
            string prompt = ReadString(services, "prompt", fallbackPrompt);
            List<FlowImageArtifact> images = new List<FlowImageArtifact>();
            if (source != null)
            {
                images.Add(source);
            }

            if (style != null)
            {
                images.Add(style);
            }

            FlowImageArtifact artifact = await ExecuteImageGenerationAsync(prompt, images);
            StoreOutput(services, "image", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Generator/Text to Video", "Text to Video", "Generator")]
    public sealed class FlowTextToVideoNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string fallbackPrompt = "Animated cinematic shot";
        [SerializeField] private float durationSeconds = 4f;

        public FlowTextToVideoNodeModel()
        {
            SetTitle("Text to Video");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("video", "Video", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            string prompt = ReadString(services, "prompt", fallbackPrompt);
            FlowVideoArtifact artifact = new FlowVideoArtifact
            {
                Label = "Text to Video",
                Prompt = prompt,
                SourceKind = "text-to-video",
                DurationSeconds = durationSeconds
            };

            StoreOutput(services, "video", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Generator/Image to Video", "Image to Video", "Generator")]
    public sealed class FlowImageToVideoNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string fallbackPrompt = "Animate the subject with cinematic movement";
        [SerializeField] private float durationSeconds = 4f;

        public FlowImageToVideoNodeModel()
        {
            SetTitle("Image to Video");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("video", "Video", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            FlowImageArtifact source = ReadObject<FlowImageArtifact>(services, "image");
            string prompt = ReadString(services, "prompt", fallbackPrompt);
            FlowVideoArtifact artifact = new FlowVideoArtifact
            {
                Label = "Image to Video",
                Prompt = prompt,
                SourceKind = $"image-to-video:{source?.Label ?? "none"}",
                DurationSeconds = durationSeconds
            };

            StoreOutput(services, "video", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Ask Image", "Ask Image", "Utility")]
    public sealed class FlowAskImageNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string fallbackQuestion = "What stands out in this image?";

        public FlowAskImageNodeModel()
        {
            SetTitle("Ask Image");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
            yield return new FlowPortDescriptor("question", "Question", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("answer", "Answer", FlowValueType.String);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            FlowImageArtifact image = ReadObject<FlowImageArtifact>(services, "image");
            string question = ReadString(services, "question", fallbackQuestion);
            string answer = $"Image analysis for '{image?.Label ?? "Unknown Image"}': {question}";
            StoreOutput(services, "answer", answer);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Crop", "Crop", "Utility")]
    public sealed class FlowCropNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private float x;
        [SerializeField] private float y;
        [SerializeField] private float width = 256f;
        [SerializeField] private float height = 256f;

        public float X
        {
            get => x;
            set => x = value;
        }

        public float Y
        {
            get => y;
            set => y = value;
        }

        public float Width
        {
            get => width;
            set => width = value;
        }

        public float Height
        {
            get => height;
            set => height = value;
        }

        public FlowCropNodeModel()
        {
            SetTitle("Crop");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("image", "Image", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            object imageInput = services.GetInputValue(this, "image");
            if (imageInput == null)
            {
                throw new InvalidOperationException("No image provided");
            }

            if (imageInput is not FlowImageArtifact imageArtifact)
            {
                throw new InvalidOperationException("Crop requires an image input.");
            }

            FlowImageArtifact result = FlowImageCropUtility.CropImageArtifact(
                imageArtifact,
                Mathf.FloorToInt(x),
                Mathf.FloorToInt(y),
                Mathf.FloorToInt(width),
                Mathf.FloorToInt(height),
                Title);

            StoreOutput(services, "image", result);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Export File", "Export File", "Utility")]
    public sealed class FlowExportFileNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string relativePath = "Generated/output";
        [SerializeField] private bool allowOverwrite = true;

        public FlowExportFileNodeModel()
        {
            SetTitle("Export File");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("value", "Value", FlowValueType.Object);
            yield return new FlowPortDescriptor("path", "Path", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("path", "Path", FlowValueType.String);
            yield return new FlowPortDescriptor("value", "Value", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            object inputValue = services.GetInputValue(this, "value");
            FlowExportPayload payload = FlowExportUtility.CreatePayload(inputValue);
            string destinationPath = ReadString(services, "path", relativePath);
            string absolutePath = FlowExportUtility.ResolveAbsolutePathFromAssetsRelative(destinationPath, payload);

            FlowExportUtility.WritePayload(absolutePath, payload, allowOverwrite);
            FlowExportUtility.RefreshAsset(absolutePath);

            string assetPath = FlowExportUtility.AbsolutePathToAssetPath(absolutePath);
            StoreOutput(services, "path", assetPath);
            StoreOutput(services, "value", inputValue);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Ask Text", "Ask Text", "Utility")]
    public sealed class FlowAskTextNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string fallbackQuestion = "Summarize this text.";

        public FlowAskTextNodeModel()
        {
            SetTitle("Ask Text");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("text", "Text", FlowValueType.String);
            yield return new FlowPortDescriptor("question", "Question", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("answer", "Answer", FlowValueType.String);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            string text = ReadString(services, "text");
            string question = ReadString(services, "question", fallbackQuestion);
            string answer = $"Text answer: {question} | Source='{text}'";
            StoreOutput(services, "answer", answer);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Text to Speech", "Text to Speech", "Utility")]
    public sealed class FlowTextToSpeechNodeModel : FlowCompanionNodeModel
    {
        [SerializeField] private string fallbackText = "Hello from FlowGraph";
        [SerializeField] private string voice = "Alloy";

        public FlowTextToSpeechNodeModel()
        {
            SetTitle("Text to Speech");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("text", "Text", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("audio", "Audio", FlowValueType.Object);
        }

        public override void Execute(FlowExecutionContext context, FlowExecutionServices services)
        {
            string text = ReadString(services, "text", fallbackText);
            FlowAudioArtifact artifact = new FlowAudioArtifact
            {
                Transcript = text,
                VoiceName = voice,
                SourceKind = "text-to-speech"
            };

            StoreOutput(services, "audio", artifact);
        }
    }

    [System.Serializable]
    [FlowNode("Utility/Prompt Template", "Prompt Template", "Utility")]
    public sealed class FlowPromptTemplateNodeModel : FlowNodeModel
    {
        [SerializeField] private string template = "{subject}, {style}, {mood}";
        [SerializeField] private string fallbackSubject = "floating island";
        [SerializeField] private string fallbackStyle = "concept art";
        [SerializeField] private string fallbackMood = "dramatic lighting";

        public FlowPromptTemplateNodeModel()
        {
            SetTitle("Prompt Template");
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeInputs()
        {
            yield return new FlowPortDescriptor("subject", "Subject", FlowValueType.String);
            yield return new FlowPortDescriptor("mood", "Mood", FlowValueType.String);
        }

        protected override IEnumerable<FlowPortDescriptor> DescribeOutputs()
        {
            yield return new FlowPortDescriptor("prompt", "Prompt", FlowValueType.String);
        }

        public override object EvaluateOutput(string portKey, FlowExecutionContext context, FlowExecutionServices services)
        {
            if (portKey != "prompt")
            {
                return null;
            }

            string subject = services.GetInputValue(this, "subject", fallbackSubject)?.ToString() ?? fallbackSubject;
            string mood = services.GetInputValue(this, "mood", fallbackMood)?.ToString() ?? fallbackMood;

            return template
                .Replace("{subject}", subject)
                .Replace("{style}", fallbackStyle)
                .Replace("{mood}", mood);
        }
    }

}
