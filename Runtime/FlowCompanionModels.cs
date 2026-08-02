using System;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    public enum FlowNodeExecutionStatus
    {
        Idle,
        Dirty,
        Running,
        Success,
        Error
    }

    [Serializable]
    public sealed class FlowNodeOutputSnapshot
    {
        [SerializeField] private string key;
        [SerializeField] private FlowValueType valueType;
        [SerializeField] private string stringValue;
        [SerializeField] private float numberValue;
        [SerializeField] private bool boolValue;
        [SerializeField] private UnityEngine.Object objectReference;
        [SerializeField] private string summary;
        [SerializeField] private string previewData;
        [SerializeField] private string mimeType;

        public string Key => key;
        public FlowValueType ValueType => valueType;
        public string StringValue => stringValue;
        public float NumberValue => numberValue;
        public bool BoolValue => boolValue;
        public UnityEngine.Object ObjectReference => objectReference;
        public string Summary => summary;
        public string PreviewData => previewData;
        public string MimeType => mimeType;

        /// <summary>
        /// Gets the best displayable preview without duplicating image data in the snapshot.
        /// Image snapshots use their persisted DataUri as the preview when no distinct preview exists.
        /// </summary>
        public string GetPreviewData()
        {
            if (!string.IsNullOrWhiteSpace(previewData))
            {
                return previewData;
            }

            return !string.IsNullOrWhiteSpace(mimeType) && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? stringValue
                : string.Empty;
        }

        public bool RemoveRedundantPreviewData()
        {
            if (string.IsNullOrWhiteSpace(previewData) ||
                string.IsNullOrWhiteSpace(stringValue) ||
                string.IsNullOrWhiteSpace(mimeType) ||
                !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previewData, stringValue, StringComparison.Ordinal))
            {
                return false;
            }

            previewData = string.Empty;
            return true;
        }

        public object ToRuntimeValue()
        {
            return valueType switch
            {
                FlowValueType.Boolean => boolValue,
                FlowValueType.Number => numberValue,
                FlowValueType.String => stringValue,
                FlowValueType.Object => RehydrateObjectValue(),
                _ => null
            };
        }

        public static FlowNodeOutputSnapshot Create(string outputKey, object value)
        {
            FlowNodeOutputSnapshot snapshot = new FlowNodeOutputSnapshot
            {
                key = outputKey,
                summary = value?.ToString() ?? "null"
            };

            switch (value)
            {
                case null:
                    snapshot.valueType = FlowValueType.Object;
                    snapshot.summary = "null";
                    return snapshot;
                case bool boolValue:
                    snapshot.valueType = FlowValueType.Boolean;
                    snapshot.boolValue = boolValue;
                    snapshot.summary = boolValue ? "true" : "false";
                    return snapshot;
                case int intValue:
                    snapshot.valueType = FlowValueType.Number;
                    snapshot.numberValue = intValue;
                    snapshot.summary = intValue.ToString();
                    return snapshot;
                case float floatValue:
                    snapshot.valueType = FlowValueType.Number;
                    snapshot.numberValue = floatValue;
                    snapshot.summary = floatValue.ToString("0.###");
                    return snapshot;
                case double doubleValue:
                    snapshot.valueType = FlowValueType.Number;
                    snapshot.numberValue = (float)doubleValue;
                    snapshot.summary = doubleValue.ToString("0.###");
                    return snapshot;
                case string stringValue:
                    snapshot.valueType = FlowValueType.String;
                    snapshot.stringValue = stringValue;
                    snapshot.summary = stringValue;
                    return snapshot;
                case UnityEngine.Object unityObject:
                    snapshot.valueType = FlowValueType.Object;
                    snapshot.objectReference = unityObject;
                    snapshot.summary = unityObject != null ? unityObject.name : "null";
                    return snapshot;
                case FlowImageArtifact imageArtifact:
                    snapshot.valueType = FlowValueType.Object;
                    snapshot.stringValue = imageArtifact.GetPreviewDataUri();
                    snapshot.summary = imageArtifact.Label;
                    // The image DataUri is the canonical persisted payload and also the preview.
                    // Keeping it in previewData triples large image payloads in graph YAML.
                    snapshot.previewData = string.Empty;
                    snapshot.mimeType = imageArtifact.MimeType;
                    return snapshot;
                case FlowVideoArtifact videoArtifact:
                    snapshot.valueType = FlowValueType.Object;
                    snapshot.stringValue = videoArtifact.DataUri;
                    snapshot.summary = videoArtifact.Label;
                    snapshot.previewData = videoArtifact.PreviewData;
                    snapshot.mimeType = videoArtifact.MimeType;
                    return snapshot;
                case FlowAudioArtifact audioArtifact:
                    snapshot.valueType = FlowValueType.Object;
                    snapshot.stringValue = audioArtifact.DataUri;
                    snapshot.summary = audioArtifact.VoiceName;
                    snapshot.mimeType = audioArtifact.MimeType;
                    return snapshot;
                default:
                    snapshot.valueType = FlowValueType.String;
                    snapshot.stringValue = value.ToString();
                    snapshot.summary = snapshot.stringValue;
                    return snapshot;
            }
        }

        private object RehydrateObjectValue()
        {
            if (objectReference != null)
            {
                return objectReference;
            }

            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return new FlowImageArtifact
                    {
                        Label = summary,
                        DataUri = stringValue,
                        MimeType = mimeType
                    };
                }

                if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    return new FlowVideoArtifact
                    {
                        Label = summary,
                        DataUri = stringValue,
                        MimeType = mimeType,
                        PreviewData = previewData
                    };
                }

                if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    return new FlowAudioArtifact
                    {
                        VoiceName = summary,
                        DataUri = stringValue,
                        MimeType = mimeType
                    };
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class FlowImageArtifact
    {
        [SerializeField] private string label;
        [SerializeField] private string prompt;
        [SerializeField] private string sourceKind;
        [SerializeField] private Texture2D previewTexture;
        [SerializeField] private string metadata;
        [SerializeField] private string dataUri;
        [SerializeField] private string mimeType = "image/png";
        [SerializeField] private string modelId;

        public string Label
        {
            get => label;
            set => label = value;
        }

        public string Prompt
        {
            get => prompt;
            set => prompt = value;
        }

        public string SourceKind
        {
            get => sourceKind;
            set => sourceKind = value;
        }

        public Texture2D PreviewTexture
        {
            get => previewTexture;
            set => previewTexture = value;
        }

        public string Metadata
        {
            get => metadata;
            set => metadata = value;
        }

        public string DataUri
        {
            get => dataUri;
            set => dataUri = value;
        }

        public string MimeType
        {
            get => mimeType;
            set => mimeType = value;
        }

        public string ModelId
        {
            get => modelId;
            set => modelId = value;
        }

        public string GetPreviewDataUri()
        {
            if (!string.IsNullOrWhiteSpace(dataUri))
            {
                return dataUri;
            }

            if (previewTexture == null)
            {
                return string.Empty;
            }

            try
            {
                byte[] pngBytes = previewTexture.isReadable
                    ? previewTexture.EncodeToPNG()
                    : EncodeNonReadableTextureToPng(previewTexture);
                return pngBytes == null || pngBytes.Length == 0
                    ? string.Empty
                    : $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
            }
            catch
            {
                return string.Empty;
            }
        }

        public override string ToString()
        {
            return $"{label} [{sourceKind}] - {prompt}";
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

    [Serializable]
    public sealed class FlowVideoArtifact
    {
        [SerializeField] private string label;
        [SerializeField] private string prompt;
        [SerializeField] private string sourceKind;
        [SerializeField] private float durationSeconds;
        [SerializeField] private string dataUri;
        [SerializeField] private string mimeType = "video/mp4";
        [SerializeField] private string previewData;

        public string Label
        {
            get => label;
            set => label = value;
        }

        public string Prompt
        {
            get => prompt;
            set => prompt = value;
        }

        public string SourceKind
        {
            get => sourceKind;
            set => sourceKind = value;
        }

        public float DurationSeconds
        {
            get => durationSeconds;
            set => durationSeconds = value;
        }

        public string DataUri
        {
            get => dataUri;
            set => dataUri = value;
        }

        public string MimeType
        {
            get => mimeType;
            set => mimeType = value;
        }

        public string PreviewData
        {
            get => previewData;
            set => previewData = value;
        }

        public override string ToString()
        {
            return $"{label} [{sourceKind}] - {prompt} ({durationSeconds:0.#}s)";
        }
    }

    [Serializable]
    public sealed class FlowAudioArtifact
    {
        [SerializeField] private string transcript;
        [SerializeField] private string voiceName;
        [SerializeField] private string sourceKind;
        [SerializeField] private string dataUri;
        [SerializeField] private string mimeType = "audio/mpeg";

        public string Transcript
        {
            get => transcript;
            set => transcript = value;
        }

        public string VoiceName
        {
            get => voiceName;
            set => voiceName = value;
        }

        public string SourceKind
        {
            get => sourceKind;
            set => sourceKind = value;
        }

        public string DataUri
        {
            get => dataUri;
            set => dataUri = value;
        }

        public string MimeType
        {
            get => mimeType;
            set => mimeType = value;
        }

        public override string ToString()
        {
            return $"{voiceName}: {transcript}";
        }
    }
}
