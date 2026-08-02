using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    public static class FlowExecutionHashUtility
    {
        public static string ComputeHash(params string[] parts)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string part in parts)
            {
                builder.Append(part ?? string.Empty);
                builder.Append('|');
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            StringBuilder hashBuilder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                hashBuilder.Append(value.ToString("x2"));
            }

            return hashBuilder.ToString();
        }

        public static string DescribeValueForHash(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            // Unity keeps a managed wrapper after the native object has been
            // destroyed. Check its overloaded null state before reading any
            // properties such as Texture2D.name.
            if (value is UnityEngine.Object unityValue && unityValue == null)
            {
                return "<null-object>";
            }

            switch (value)
            {
                case string stringValue:
                    return stringValue;
                case bool boolValue:
                    return boolValue ? "true" : "false";
                case int intValue:
                    return intValue.ToString();
                case float floatValue:
                    return floatValue.ToString("R");
                case double doubleValue:
                    return doubleValue.ToString("R");
                case Texture2D texture:
                    return $"{texture.name}:{texture.width}x{texture.height}";
                case FlowImageArtifact imageArtifact:
                    return $"{imageArtifact.ModelId}|{imageArtifact.Prompt}|{imageArtifact.SourceKind}|{imageArtifact.Metadata}|{imageArtifact.DataUri}|{DescribeValueForHash(imageArtifact.PreviewTexture)}";
                case FlowVideoArtifact videoArtifact:
                    return $"{videoArtifact.Label}|{videoArtifact.Prompt}|{videoArtifact.SourceKind}|{videoArtifact.DurationSeconds}|{videoArtifact.DataUri}";
                case FlowAudioArtifact audioArtifact:
                    return $"{audioArtifact.VoiceName}|{audioArtifact.Transcript}|{audioArtifact.SourceKind}|{audioArtifact.DataUri}";
                case IEnumerable enumerable when value is not string:
                    List<string> items = new List<string>();
                    foreach (object item in enumerable)
                    {
                        items.Add(DescribeValueForHash(item));
                    }

                    return $"[{string.Join(",", items)}]";
                case UnityEngine.Object unityObject:
                    return unityObject != null ? unityObject.name : "<null-object>";
                case IDictionary dictionary:
                    List<string> pairs = new List<string>();
                    foreach (object key in dictionary.Keys)
                    {
                        pairs.Add($"{DescribeValueForHash(key)}={DescribeValueForHash(dictionary[key])}");
                    }

                    return $"{{{string.Join(",", pairs.OrderBy(entry => entry, StringComparer.Ordinal))}}}";
                default:
                    return value.ToString();
            }
        }
    }
}
