using System;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Myrvo.FlowGraph
{
    internal readonly struct FlowExportPayload
    {
        public FlowExportPayload(byte[] bytes, string mimeType, object originalValue)
        {
            Bytes = bytes;
            MimeType = mimeType;
            OriginalValue = originalValue;
        }

        public byte[] Bytes { get; }
        public string MimeType { get; }
        public object OriginalValue { get; }
    }

    internal static class FlowExportUtility
    {
        public static FlowExportPayload CreatePayload(object value)
        {
            return value switch
            {
                null => throw new InvalidOperationException("Export File requires a non-null input value."),
                FlowImageArtifact imageArtifact => CreateBinaryPayload(imageArtifact.GetPreviewDataUri(), imageArtifact.MimeType, imageArtifact),
                FlowVideoArtifact videoArtifact => CreateBinaryPayload(videoArtifact.DataUri, string.IsNullOrWhiteSpace(videoArtifact.MimeType) ? "video/mp4" : videoArtifact.MimeType, videoArtifact),
                FlowAudioArtifact audioArtifact => CreateBinaryPayload(audioArtifact.DataUri, string.IsNullOrWhiteSpace(audioArtifact.MimeType) ? "audio/mpeg" : audioArtifact.MimeType, audioArtifact),
                string textValue => new FlowExportPayload(Encoding.UTF8.GetBytes(textValue), "text/plain", textValue),
                _ => new FlowExportPayload(Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty), "text/plain", value)
            };
        }

        public static string ResolveAbsolutePathFromAssetsRelative(string relativePath, FlowExportPayload payload)
        {
            string trimmedPath = (relativePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                throw new InvalidOperationException("Export File requires a destination path relative to Application.dataPath.");
            }

            string assetsRoot = Path.GetFullPath(Application.dataPath);
            string combinedPath = Path.GetFullPath(Path.Combine(assetsRoot, trimmedPath));
            string finalPath = Path.HasExtension(combinedPath)
                ? combinedPath
                : combinedPath + "." + GetExtensionWithoutDot(payload.MimeType);

            string normalizedAssetsRoot = EnsureTrailingDirectorySeparator(assetsRoot);
            if (!finalPath.StartsWith(normalizedAssetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Export File destination must stay inside this Unity project's Assets folder.");
            }

            return finalPath;
        }

        public static void WritePayload(string absolutePath, FlowExportPayload payload, bool allowOverwrite)
        {
            if (File.Exists(absolutePath) && !allowOverwrite)
            {
                throw new InvalidOperationException($"Export File will not overwrite existing file: {AbsolutePathToAssetPath(absolutePath)}");
            }

            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(absolutePath, payload.Bytes);
        }

        public static string AbsolutePathToAssetPath(string absolutePath)
        {
            string normalizedAssetsRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(Application.dataPath));
            string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
            if (!normalizedAbsolutePath.StartsWith(normalizedAssetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Exported file is outside the Unity Assets folder.");
            }

            string suffix = normalizedAbsolutePath.Substring(normalizedAssetsRoot.Length)
                .Replace('\\', '/');
            return $"Assets/{suffix}";
        }

        public static void RefreshAsset(string absolutePath)
        {
#if UNITY_EDITOR
            string assetPath = AbsolutePathToAssetPath(absolutePath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
#endif
        }

        private static FlowExportPayload CreateBinaryPayload(string dataUri, string mimeType, object originalValue)
        {
            byte[] bytes = FlowProviderExecutionHelpers.DecodeDataUri(dataUri);
            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidOperationException("Export File could not read binary data from the input artifact.");
            }

            return new FlowExportPayload(bytes, mimeType, originalValue);
        }

        private static string GetExtensionWithoutDot(string mimeType)
        {
            return mimeType switch
            {
                "image/jpeg" => "jpg",
                "image/webp" => "webp",
                "image/gif" => "gif",
                "image/bmp" => "bmp",
                "video/quicktime" => "mov",
                "video/webm" => "webm",
                "video/mp4" => "mp4",
                "audio/wav" => "wav",
                "audio/x-wav" => "wav",
                "audio/ogg" => "ogg",
                "audio/mpeg" => "mp3",
                "text/plain" => "txt",
                _ when !string.IsNullOrWhiteSpace(mimeType) && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "png",
                _ when !string.IsNullOrWhiteSpace(mimeType) && mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "mp4",
                _ when !string.IsNullOrWhiteSpace(mimeType) && mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "wav",
                _ => "txt"
            };
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }
}
