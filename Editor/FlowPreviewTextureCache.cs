using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    /// <summary>
    /// Owns decoded data-URI preview textures for all Flow Graph editor surfaces.
    /// Textures are editor-only and are released before a domain reload or editor exit.
    /// </summary>
    internal static class FlowPreviewTextureCache
    {
        private static readonly Dictionary<string, Texture2D> TexturesByDataUri = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        internal static int DecodeCount { get; private set; }

        static FlowPreviewTextureCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        public static Texture2D Get(string previewData, string textureName = "FlowGraphPreview")
        {
            if (string.IsNullOrWhiteSpace(previewData))
            {
                return null;
            }

            if (TexturesByDataUri.TryGetValue(previewData, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            int commaIndex = previewData.IndexOf(',');
            if (commaIndex < 0 || commaIndex >= previewData.Length - 1)
            {
                return null;
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(previewData.Substring(commaIndex + 1));
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = textureName,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.LoadImage(imageBytes);
                TexturesByDataUri[previewData] = texture;
                DecodeCount++;
                return texture;
            }
            catch
            {
                return null;
            }
        }

        internal static void Clear()
        {
            foreach (Texture2D texture in TexturesByDataUri.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            TexturesByDataUri.Clear();
        }

        internal static void ResetForTests()
        {
            Clear();
            DecodeCount = 0;
        }
    }
}
