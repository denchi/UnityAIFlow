using UnityEditor;
using UnityEngine;

namespace Myrvo.FlowGraph.Editor
{
    public static class FlowProviderAssetUtility
    {
        private const string DefaultRegistryPath = "Assets/Resources/FlowProviderRegistry.asset";
        private const string DefaultSettingsPath = "Assets/Resources/FlowProviderSettings.asset";

        [MenuItem("Assets/Create/Myrvo/Flow Graph Provider Registry", priority = 302)]
        public static void CreateDefaultProviderRegistry()
        {
            CreateDefaultRegistryAsset();
        }

        [MenuItem("Myrvo/Flow Graph/Open Provider Project Settings", priority = 303)]
        public static void OpenProviderProjectSettings()
        {
            SettingsService.OpenProjectSettings("Project/Myrvo/Flow Providers");
        }

        [MenuItem("Assets/Create/Myrvo/Flow Graph Legacy Provider Settings", priority = 304)]
        public static void CreateProviderSettings()
        {
            FlowProviderRegistryAsset registry = CreateDefaultRegistryAsset();
            EnsureFolder(DefaultSettingsPath);

            FlowProviderSettingsAsset settings = AssetDatabase.LoadAssetAtPath<FlowProviderSettingsAsset>(DefaultSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<FlowProviderSettingsAsset>();
                AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
            }

            SerializedObject serializedObject = new SerializedObject(settings);
            serializedObject.FindProperty("registry").objectReferenceValue = registry;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            FlowProviderRegistryLocator.ResetCache();
        }

        public static FlowProviderRegistryAsset CreateDefaultRegistryAsset()
        {
            EnsureFolder(DefaultRegistryPath);
            FlowProviderRegistryAsset registry = AssetDatabase.LoadAssetAtPath<FlowProviderRegistryAsset>(DefaultRegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<FlowProviderRegistryAsset>();
                AssetDatabase.CreateAsset(registry, DefaultRegistryPath);
            }

            SerializedObject serializedObject = new SerializedObject(registry);
            SerializedProperty providersProperty = serializedObject.FindProperty("providers");
            providersProperty.ClearArray();
            AddProvider(providersProperty, "openai", "OpenAI", "https://api.openai.com/v1", FlowModelCapability.Image, FlowModelCapability.Text, FlowModelCapability.Audio);
            AddProvider(providersProperty, "google", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta", FlowModelCapability.Image, FlowModelCapability.Video, FlowModelCapability.Text);

            SerializedProperty modelsProperty = serializedObject.FindProperty("models");
            modelsProperty.ClearArray();
            AddImageModel(modelsProperty, "google/gemini-2.5-flash-image", "google", "Gemini 2.5 Flash Image", true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "1K" });
            AddImageModel(modelsProperty, "google/gemini-3.1-flash-image-preview", "google", "Gemini 3.1 Flash Image", true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "0.5K", "1K", "2K", "4K" });
            AddImageModel(modelsProperty, "google/gemini-3-pro-image-preview", "google", "Gemini 3 Pro Image", true, new[] { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" }, new[] { "1K", "2K", "4K" });
            AddImageModel(modelsProperty, "openai/gpt-image-2", "openai", "OpenAI GPT Image 2", false, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K", "4K" });
            AddImageModel(modelsProperty, "openai/gpt-image-1.5", "openai", "OpenAI GPT Image 1.5", true, new[] { "1:1", "3:2", "2:3", "16:9", "9:16" }, new[] { "1K", "2K" });
            AddImageModel(modelsProperty, "openai/dall-e-3", "openai", "OpenAI DALL-E 3", false, new[] { "1:1", "16:9", "9:16" }, new[] { "1K", "2K" });

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            Selection.activeObject = registry;
            FlowProviderRegistryLocator.ResetCache();
            return registry;
        }

        private static void AddProvider(SerializedProperty providersProperty, string id, string label, string baseUrl, params FlowModelCapability[] capabilities)
        {
            int index = providersProperty.arraySize;
            providersProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = providersProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = id;
            element.FindPropertyRelative("label").stringValue = label;
            element.FindPropertyRelative("baseUrl").stringValue = baseUrl;

            SerializedProperty capabilitiesProperty = element.FindPropertyRelative("supportedCapabilities");
            capabilitiesProperty.ClearArray();
            for (int capabilityIndex = 0; capabilityIndex < capabilities.Length; capabilityIndex++)
            {
                capabilitiesProperty.InsertArrayElementAtIndex(capabilityIndex);
                capabilitiesProperty.GetArrayElementAtIndex(capabilityIndex).enumValueIndex = (int)capabilities[capabilityIndex];
            }
        }

        private static void AddImageModel(SerializedProperty modelsProperty, string id, string providerId, string label, bool supportsTransparency, string[] aspectRatios, string[] qualities)
        {
            int index = modelsProperty.arraySize;
            modelsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = modelsProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = id;
            element.FindPropertyRelative("providerId").stringValue = providerId;
            element.FindPropertyRelative("label").stringValue = label;

            SerializedProperty capabilitiesProperty = element.FindPropertyRelative("capabilities");
            capabilitiesProperty.ClearArray();
            capabilitiesProperty.InsertArrayElementAtIndex(0);
            capabilitiesProperty.GetArrayElementAtIndex(0).enumValueIndex = (int)FlowModelCapability.Image;

            SerializedProperty constraintsProperty = element.FindPropertyRelative("constraints");
            AddSelectConstraint(constraintsProperty.FindPropertyRelative("selectConstraints"), "aspectRatio", "Aspect Ratio", aspectRatios[0], aspectRatios);
            AddSelectConstraint(constraintsProperty.FindPropertyRelative("selectConstraints"), "imageSize", "Quality", qualities[0], qualities);
            AddBooleanConstraint(constraintsProperty.FindPropertyRelative("booleanConstraints"), "transparent", "Transparent Background", supportsTransparency, supportsTransparency);
        }

        private static void AddSelectConstraint(SerializedProperty selectConstraintsProperty, string key, string label, string defaultValue, string[] values)
        {
            int index = selectConstraintsProperty.arraySize;
            selectConstraintsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = selectConstraintsProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("key").stringValue = key;
            element.FindPropertyRelative("label").stringValue = label;
            element.FindPropertyRelative("defaultValue").stringValue = defaultValue;
            element.FindPropertyRelative("hiddenWhenUnsupported").boolValue = true;
            SerializedProperty optionsProperty = element.FindPropertyRelative("options");
            optionsProperty.ClearArray();
            for (int optionIndex = 0; optionIndex < values.Length; optionIndex++)
            {
                optionsProperty.InsertArrayElementAtIndex(optionIndex);
                SerializedProperty option = optionsProperty.GetArrayElementAtIndex(optionIndex);
                option.FindPropertyRelative("label").stringValue = values[optionIndex];
                option.FindPropertyRelative("value").stringValue = values[optionIndex];
            }
        }

        private static void AddBooleanConstraint(SerializedProperty booleanConstraintsProperty, string key, string label, bool supported, bool defaultValue)
        {
            int index = booleanConstraintsProperty.arraySize;
            booleanConstraintsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = booleanConstraintsProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("key").stringValue = key;
            element.FindPropertyRelative("label").stringValue = label;
            element.FindPropertyRelative("supported").boolValue = supported;
            element.FindPropertyRelative("defaultValue").boolValue = defaultValue;
            element.FindPropertyRelative("hiddenWhenUnsupported").boolValue = true;
        }

        private static void EnsureFolder(string assetPath)
        {
            string directory = GetAssetDirectory(assetPath);
            if (string.IsNullOrWhiteSpace(directory) || AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            EnsureFolderHierarchy(directory);
        }

        private static string GetAssetDirectory(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex <= 0 ? string.Empty : normalizedPath.Substring(0, separatorIndex);
        }

        private static void EnsureFolderHierarchy(string assetDirectory)
        {
            string[] folders = assetDirectory.Split('/');
            if (folders.Length == 0 || folders[0] != "Assets")
            {
                return;
            }

            string currentPath = folders[0];
            for (int index = 1; index < folders.Length; index++)
            {
                string folderName = folders[index];
                string nextPath = currentPath + "/" + folderName;
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folderName);
                }

                currentPath = nextPath;
            }
        }
    }
}
