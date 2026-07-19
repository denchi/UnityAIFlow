using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Myrvo.FlowGraph
{
    public enum FlowProviderRequestKind
    {
        TextToImage,
        ImageToImage,
        ImageToImageTwoInputs
    }

    public sealed class FlowProviderRequest
    {
        public FlowProviderRequestKind RequestKind { get; set; }
        public FlowModelDefinition Model { get; set; }
        public string Prompt { get; set; }
        public string AspectRatio { get; set; }
        public string ImageSize { get; set; }
        public bool Transparent { get; set; }
        public IReadOnlyList<FlowImageArtifact> Images { get; set; }
        public IReadOnlyDictionary<string, object> Options { get; set; }
    }

    public sealed class FlowProviderExecutionService
    {
        public Task<FlowImageArtifact> ExecuteImageRequestAsync(FlowProviderRequest request)
        {
            if (request?.Model == null)
            {
                throw new InvalidOperationException("Provider execution requires a selected model.");
            }

            IFlowProviderPlugin plugin = FlowProviderPluginRegistry.ResolvePluginForModel(request.Model.Id);
            if (plugin == null)
            {
                throw new InvalidOperationException($"No provider plugin is registered for model '{request.Model.Id}'.");
            }

            return plugin.ExecuteImageRequestAsync(request, FlowProviderProjectSettingsLocator.GetSettings());
        }
    }

    internal static class FlowProviderExecutionHelpers
    {
        public static string NormalizeImageSizeToSquarePixels(string imageSize)
        {
            return imageSize switch
            {
                "0.5K" => "512x512",
                "1K" => "1024x1024",
                "2K" => "1536x1536",
                "4K" => "2048x2048",
                _ => "1024x1024"
            };
        }

        public static string BuildAspectRatioSuffix(string aspectRatio)
        {
            return string.IsNullOrWhiteSpace(aspectRatio) ? string.Empty : $" Aspect ratio: {aspectRatio}.";
        }

        public static string BuildTransparencySuffix(bool transparent)
        {
            return transparent ? " Transparent background requested." : string.Empty;
        }

        public static byte[] DecodeDataUri(string dataUri)
        {
            if (string.IsNullOrWhiteSpace(dataUri))
            {
                return null;
            }

            int commaIndex = dataUri.IndexOf(',');
            if (commaIndex < 0 || commaIndex >= dataUri.Length - 1)
            {
                return null;
            }

            return Convert.FromBase64String(dataUri.Substring(commaIndex + 1));
        }

        public static string DetectMimeType(string dataUri, string fallback = "image/png")
        {
            if (string.IsNullOrWhiteSpace(dataUri) || !dataUri.StartsWith("data:", StringComparison.Ordinal))
            {
                return fallback;
            }

            int semicolonIndex = dataUri.IndexOf(';');
            return semicolonIndex > 5 ? dataUri.Substring(5, semicolonIndex - 5) : fallback;
        }

        public static string MimeTypeToExtension(string mimeType)
        {
            return mimeType switch
            {
                "image/jpeg" => "jpg",
                "image/webp" => "webp",
                "image/gif" => "gif",
                "image/bmp" => "bmp",
                _ => "png"
            };
        }

        public static Texture2D DecodeTexture(string dataUri)
        {
            byte[] bytes = DecodeDataUri(dataUri);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(bytes);
            texture.name = "FlowGraphPreview";
            return texture;
        }

        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        public static Task SendAsync(UnityWebRequest webRequest)
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();

            void CompleteRequest()
            {
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    completion.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(webRequest.downloadHandler?.text)
                        ? webRequest.error
                        : webRequest.downloadHandler.text));
                    return;
                }

                completion.TrySetResult(true);
            }

            if (operation.isDone)
            {
                CompleteRequest();
            }
            else
            {
                operation.completed += _ => CompleteRequest();
            }

            return completion.Task;
        }

        public static FlowModelConstraints CreateImageConstraints(bool supportsTransparency, string[] aspectRatios, string[] imageSizes)
        {
            return new FlowModelConstraints(
                new[]
                {
                    new FlowSelectConstraint("aspectRatio", "Aspect Ratio", aspectRatios.FirstOrDefault() ?? "1:1", true, aspectRatios.Select(value => new FlowOptionChoice(value, value)))
                },
                new[]
                {
                    new FlowBooleanConstraint("transparent", "Transparent Background", supportsTransparency, supportsTransparency, true)
                },
                null).MergeImageSize(imageSizes);
        }

        public static FlowModelConstraints MergeImageSize(this FlowModelConstraints constraints, string[] imageSizes)
        {
            List<FlowSelectConstraint> selects = constraints.SelectConstraints.ToList();
            selects.Add(new FlowSelectConstraint("imageSize", "Quality", imageSizes.FirstOrDefault() ?? "1K", true, imageSizes.Select(value => new FlowOptionChoice(value, value))));
            return new FlowModelConstraints(selects, constraints.BooleanConstraints, constraints.RangeConstraints);
        }
    }

    [FlowProviderPlugin]
    internal sealed class OpenAIFlowProviderPlugin : IFlowProviderPlugin
    {
        private const string ProviderIdValue = "openai";
        private static readonly FlowProviderDescriptor Descriptor = new FlowProviderDescriptor(
            ProviderIdValue,
            "OpenAI",
            new[] { FlowModelCapability.Image, FlowModelCapability.Text, FlowModelCapability.Audio },
            new[]
            {
                new FlowProviderSettingDefinition("apiKey", "API Key", FlowProviderSettingFieldType.Password),
                new FlowProviderSettingDefinition("baseUrl", "Base URL", FlowProviderSettingFieldType.Text, "https://api.openai.com/v1")
            });

        private static readonly IReadOnlyList<FlowModelDefinition> Models = new[]
        {
            new FlowModelDefinition(
                "openai/gpt-image-2",
                ProviderIdValue,
                "GPT Image 2",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(false, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K", "4K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "openai/gpt-image-1.5",
                ProviderIdValue,
                "GPT Image 1.5",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "openai/gpt-image-1",
                ProviderIdValue,
                "GPT Image 1",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "openai/gpt-image-1-mini",
                ProviderIdValue,
                "GPT Image 1 Mini",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "openai/dall-e-3",
                ProviderIdValue,
                "DALL-E 3",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage },
                FlowProviderExecutionHelpers.CreateImageConstraints(false, new[] { "1:1", "16:9", "9:16" }, new[] { "1K", "2K" }),
                Array.Empty<FlowNodeOptionSchema>())
        };

        [Serializable]
        private sealed class OpenAIImageResponse
        {
            public OpenAIImageData[] data;
        }

        [Serializable]
        private sealed class OpenAIImageData
        {
            public string b64_json;
        }

        [Serializable]
        private sealed class OpenAIImageGenerateRequest
        {
            public string model;
            public string prompt;
            public string size;
            public string background;
            public string response_format;
        }

        public string ProviderId => ProviderIdValue;
        public string DisplayName => Descriptor.Label;

        public FlowProviderDescriptor GetDescriptor()
        {
            return Descriptor;
        }

        public IReadOnlyList<FlowModelDefinition> GetModels()
        {
            return Models;
        }

        public bool CanHandleModel(string modelId)
        {
            return Models.Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
        }

        public async Task<FlowImageArtifact> ExecuteImageRequestAsync(FlowProviderRequest request, FlowProviderProjectSettings settings)
        {
            string apiKey = settings.GetValue(ProviderIdValue, "apiKey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is missing in Project Settings > Myrvo > Flow Providers.");
            }

            string baseUrl = settings.GetValue(ProviderIdValue, "baseUrl", "https://api.openai.com/v1").TrimEnd('/');
            bool isEdit = request.RequestKind != FlowProviderRequestKind.TextToImage && request.Images != null && request.Images.Count > 0;
            if (isEdit)
            {
                return await ExecuteEditRequestAsync(request, apiKey, baseUrl);
            }

            OpenAIImageGenerateRequest payload = new OpenAIImageGenerateRequest
            {
                model = request.Model.Id.Replace("openai/", string.Empty),
                prompt = $"{request.Prompt}{FlowProviderExecutionHelpers.BuildAspectRatioSuffix(request.AspectRatio)}{FlowProviderExecutionHelpers.BuildTransparencySuffix(request.Transparent)}",
                size = FlowProviderExecutionHelpers.NormalizeImageSizeToSquarePixels(request.ImageSize),
                background = request.Transparent ? "transparent" : "auto",
                response_format = "b64_json"
            };

            string responseText = await PerformJsonRequestAsync($"{baseUrl}/images/generations", apiKey, JsonUtility.ToJson(payload));
            return CreateImageArtifactFromResponse(request, responseText, "OpenAI did not return image data.");
        }

        private static async Task<FlowImageArtifact> ExecuteEditRequestAsync(FlowProviderRequest request, string apiKey, string baseUrl)
        {
            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model", request.Model.Id.Replace("openai/", string.Empty)),
                new MultipartFormDataSection("prompt", $"{request.Prompt}{FlowProviderExecutionHelpers.BuildAspectRatioSuffix(request.AspectRatio)}{FlowProviderExecutionHelpers.BuildTransparencySuffix(request.Transparent)}"),
                new MultipartFormDataSection("size", FlowProviderExecutionHelpers.NormalizeImageSizeToSquarePixels(request.ImageSize))
            };

            int attachedImageCount = 0;
            string imageFieldName = request.Images.Count > 1 ? "image[]" : "image";
            for (int index = 0; index < request.Images.Count; index++)
            {
                FlowImageArtifact image = request.Images[index];
                string sourceDataUri = image?.GetPreviewDataUri();
                byte[] bytes = FlowProviderExecutionHelpers.DecodeDataUri(sourceDataUri);
                if (bytes == null || bytes.Length == 0)
                {
                    continue;
                }

                string mimeType = FlowProviderExecutionHelpers.DetectMimeType(sourceDataUri);
                string extension = FlowProviderExecutionHelpers.MimeTypeToExtension(mimeType);
                form.Add(new MultipartFormFileSection(imageFieldName, bytes, $"source-{index + 1}.{extension}", mimeType));
                attachedImageCount++;
            }

            if (attachedImageCount == 0)
            {
                throw new InvalidOperationException("Image edit request did not include any readable source images. Check that the source node has an assigned texture and that Unity can encode it.");
            }

            UnityWebRequest webRequest = UnityWebRequest.Post($"{baseUrl}/images/edits", form);
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            await FlowProviderExecutionHelpers.SendAsync(webRequest);
            return CreateImageArtifactFromResponse(request, webRequest.downloadHandler.text, "OpenAI did not return edited image data.");
        }

        private static async Task<string> PerformJsonRequestAsync(string url, string apiKey, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bodyBytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            await FlowProviderExecutionHelpers.SendAsync(webRequest);
            return webRequest.downloadHandler.text;
        }

        private static FlowImageArtifact CreateImageArtifactFromResponse(FlowProviderRequest request, string responseText, string missingDataError)
        {
            OpenAIImageResponse response = JsonUtility.FromJson<OpenAIImageResponse>(responseText);
            string base64 = response?.data != null && response.data.Length > 0 ? response.data[0].b64_json : string.Empty;
            if (string.IsNullOrWhiteSpace(base64))
            {
                throw new InvalidOperationException(missingDataError);
            }

            string dataUri = $"data:image/png;base64,{base64}";
            return new FlowImageArtifact
            {
                Label = request.RequestKind.ToString(),
                Prompt = request.Prompt,
                SourceKind = request.RequestKind.ToString().ToLowerInvariant(),
                Metadata = $"model={request.Model.Id}, aspect={request.AspectRatio}, quality={request.ImageSize}, transparent={request.Transparent}",
                DataUri = dataUri,
                MimeType = "image/png",
                ModelId = request.Model.Id,
                PreviewTexture = FlowProviderExecutionHelpers.DecodeTexture(dataUri)
            };
        }
    }

    [FlowProviderPlugin]
    internal sealed class GeminiFlowProviderPlugin : IFlowProviderPlugin
    {
        private const string ProviderIdValue = "google";
        private static readonly FlowProviderDescriptor Descriptor = new FlowProviderDescriptor(
            ProviderIdValue,
            "Google Gemini",
            new[] { FlowModelCapability.Image, FlowModelCapability.Video, FlowModelCapability.Text },
            new[]
            {
                new FlowProviderSettingDefinition("apiKey", "API Key", FlowProviderSettingFieldType.Password),
                new FlowProviderSettingDefinition("baseUrl", "Base URL", FlowProviderSettingFieldType.Text, "https://generativelanguage.googleapis.com/v1beta")
            });

        private static readonly IReadOnlyList<FlowModelDefinition> Models = new[]
        {
            new FlowModelDefinition(
                "google/gemini-2.5-flash-image",
                ProviderIdValue,
                "Gemini 2.5 Flash Image",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "1K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "google/gemini-3.1-flash-image-preview",
                ProviderIdValue,
                "Gemini 3.1 Flash Image",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "0.5K", "1K", "2K", "4K" }),
                Array.Empty<FlowNodeOptionSchema>()),
            new FlowModelDefinition(
                "google/gemini-3-pro-image-preview",
                ProviderIdValue,
                "Gemini 3 Pro Image",
                new[] { FlowModelCapability.Image },
                new[] { FlowProviderRequestKind.TextToImage, FlowProviderRequestKind.ImageToImage, FlowProviderRequestKind.ImageToImageTwoInputs },
                FlowProviderExecutionHelpers.CreateImageConstraints(true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "1K", "2K", "4K" }),
                Array.Empty<FlowNodeOptionSchema>())
        };

        [Serializable]
        private sealed class GeminiRequestPart
        {
            public string text;
            public GeminiInlineData inlineData;
        }

        [Serializable]
        private sealed class GeminiInlineData
        {
            public string mimeType;
            public string data;
        }

        [Serializable]
        private sealed class GeminiGenerateResponse
        {
            public GeminiCandidate[] candidates;
        }

        [Serializable]
        private sealed class GeminiCandidate
        {
            public GeminiCandidateContent content;
        }

        [Serializable]
        private sealed class GeminiCandidateContent
        {
            public GeminiInlinePart[] parts;
        }

        [Serializable]
        private sealed class GeminiInlinePart
        {
            public GeminiInlineData inlineData;
        }

        public string ProviderId => ProviderIdValue;
        public string DisplayName => Descriptor.Label;

        public FlowProviderDescriptor GetDescriptor()
        {
            return Descriptor;
        }

        public IReadOnlyList<FlowModelDefinition> GetModels()
        {
            return Models;
        }

        public bool CanHandleModel(string modelId)
        {
            return Models.Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
        }

        public async Task<FlowImageArtifact> ExecuteImageRequestAsync(FlowProviderRequest request, FlowProviderProjectSettings settings)
        {
            string apiKey = settings.GetValue(ProviderIdValue, "apiKey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Gemini API key is missing in Project Settings > Myrvo > Flow Providers.");
            }

            string baseUrl = settings.GetValue(ProviderIdValue, "baseUrl", "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');

            List<GeminiRequestPart> parts = new List<GeminiRequestPart>();
            int attachedImageCount = 0;
            if (request.Images != null)
            {
                foreach (FlowImageArtifact image in request.Images)
                {
                    string sourceDataUri = image?.GetPreviewDataUri();
                    byte[] bytes = FlowProviderExecutionHelpers.DecodeDataUri(sourceDataUri);
                    if (bytes == null || bytes.Length == 0)
                    {
                        continue;
                    }

                    parts.Add(new GeminiRequestPart
                    {
                        inlineData = new GeminiInlineData
                        {
                            mimeType = FlowProviderExecutionHelpers.DetectMimeType(sourceDataUri),
                            data = Convert.ToBase64String(bytes)
                        }
                    });
                    attachedImageCount++;
                }
            }

            if (request.RequestKind != FlowProviderRequestKind.TextToImage &&
                request.Images != null &&
                request.Images.Count > 0 &&
                attachedImageCount == 0)
            {
                throw new InvalidOperationException("Gemini image edit request did not include any readable source images. The texture preview exists in Unity, but the source image could not be encoded for upload.");
            }

            parts.Add(new GeminiRequestPart
            {
                text = $"{request.Prompt}{FlowProviderExecutionHelpers.BuildAspectRatioSuffix(request.AspectRatio)} Requested quality preset: {request.ImageSize}.{FlowProviderExecutionHelpers.BuildTransparencySuffix(request.Transparent)}"
            });

            string url = $"{baseUrl}/models/{request.Model.Id.Replace("google/", string.Empty)}:generateContent?key={apiKey}";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(BuildGeminiRequestJson(parts));
            UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bodyBytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            webRequest.SetRequestHeader("Content-Type", "application/json");
            await FlowProviderExecutionHelpers.SendAsync(webRequest);

            GeminiGenerateResponse response = JsonUtility.FromJson<GeminiGenerateResponse>(webRequest.downloadHandler.text);
            GeminiInlineData inlineData = response?.candidates?
                .Where(candidate => candidate?.content?.parts != null)
                .SelectMany(candidate => candidate.content.parts)
                .Select(part => part?.inlineData)
                .FirstOrDefault(data => data != null && !string.IsNullOrWhiteSpace(data.data));

            if (inlineData == null || string.IsNullOrWhiteSpace(inlineData.data))
            {
                throw new InvalidOperationException("Gemini did not return image data.");
            }

            string mimeType = string.IsNullOrWhiteSpace(inlineData.mimeType) ? "image/png" : inlineData.mimeType;
            string dataUri = $"data:{mimeType};base64,{inlineData.data}";
            return new FlowImageArtifact
            {
                Label = request.RequestKind.ToString(),
                Prompt = request.Prompt,
                SourceKind = request.RequestKind.ToString().ToLowerInvariant(),
                Metadata = $"model={request.Model.Id}, aspect={request.AspectRatio}, quality={request.ImageSize}, transparent={request.Transparent}",
                DataUri = dataUri,
                MimeType = mimeType,
                ModelId = request.Model.Id,
                PreviewTexture = FlowProviderExecutionHelpers.DecodeTexture(dataUri)
            };
        }

        private static string BuildGeminiRequestJson(IReadOnlyList<GeminiRequestPart> parts)
        {
            StringBuilder builder = new StringBuilder(1024);
            builder.Append("{\"contents\":[{\"role\":\"user\",\"parts\":[");
            for (int index = 0; index < parts.Count; index++)
            {
                GeminiRequestPart part = parts[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                if (part.inlineData != null)
                {
                    builder.Append("{\"inlineData\":{\"mimeType\":\"");
                    builder.Append(FlowProviderExecutionHelpers.EscapeJson(part.inlineData.mimeType));
                    builder.Append("\",\"data\":\"");
                    builder.Append(part.inlineData.data);
                    builder.Append("\"}}");
                    continue;
                }

                builder.Append("{\"text\":\"");
                builder.Append(FlowProviderExecutionHelpers.EscapeJson(part.text ?? string.Empty));
                builder.Append("\"}");
            }

            builder.Append("]}],\"generationConfig\":{\"responseModalities\":[\"TEXT\",\"IMAGE\"]}}");
            return builder.ToString();
        }
    }
}
