using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Myrvo.FlowGraph.Tests
{
    public sealed class FlowProviderProjectSettingsStorageTests
    {
        [Test]
        public void Storage_UsesUserSettingsPath()
        {
            Type storageType = GetEditorType("Myrvo.FlowGraph.Editor.FlowProviderProjectSettingsStorage");
            FieldInfo pathField = storageType.GetField("CurrentStoragePath", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(pathField, Is.Not.Null);
            Assert.That(pathField.GetValue(null) as string, Is.EqualTo("UserSettings/MyrvoFlowProviderProjectSettings.asset"));
        }

        [Test]
        public void ApplyLegacySettings_CopiesOnlyNonEmptyValues()
        {
            Type bootstrapType = GetEditorType("Myrvo.FlowGraph.Editor.FlowProviderProjectSettingsBootstrap");
            MethodInfo method = bootstrapType.GetMethod("ApplyLegacyAssetSettings", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "Expected bootstrap helper method to exist for migration.");

            FlowProviderSettingsAsset legacySettings = ScriptableObject.CreateInstance<FlowProviderSettingsAsset>();
            try
            {
                SetPrivateField(legacySettings, "openAIApiKey", "openai-key");
                SetPrivateField(legacySettings, "openAIBaseUrl", "https://openai.example/v1");
                SetPrivateField(legacySettings, "geminiApiKey", "google-key");
                SetPrivateField(legacySettings, "geminiBaseUrl", "");

                FlowProviderProjectSettings settings = new FlowProviderProjectSettings();
                method.Invoke(null, new object[] { legacySettings, settings });

                Assert.That(settings.GetValue("openai", "apiKey"), Is.EqualTo("openai-key"));
                Assert.That(settings.GetValue("openai", "baseUrl"), Is.EqualTo("https://openai.example/v1"));
                Assert.That(settings.GetValue("google", "apiKey"), Is.EqualTo("google-key"));
                Assert.That(settings.GetValue("google", "baseUrl"), Is.EqualTo(string.Empty));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(legacySettings);
            }
        }

        [Test]
        public void ApplyStoredSettings_CopiesOnlyNonEmptyValues()
        {
            Type bootstrapType = GetEditorType("Myrvo.FlowGraph.Editor.FlowProviderProjectSettingsBootstrap");
            MethodInfo method = bootstrapType.GetMethod("ApplyStoredSettings", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "Expected bootstrap helper method to exist for migration.");

            FlowProviderProjectSettings source = new FlowProviderProjectSettings();
            source.SetValue("openai", "apiKey", "openai-key");
            source.SetValue("openai", "baseUrl", "https://openai.example/v1");
            source.SetValue("google", "apiKey", "");
            source.SetValue("google", "baseUrl", "https://google.example/v1");

            FlowProviderProjectSettings target = new FlowProviderProjectSettings();
            method.Invoke(null, new object[] { source, target });

            Assert.That(target.GetValue("openai", "apiKey"), Is.EqualTo("openai-key"));
            Assert.That(target.GetValue("openai", "baseUrl"), Is.EqualTo("https://openai.example/v1"));
            Assert.That(target.GetValue("google", "apiKey"), Is.EqualTo(string.Empty));
            Assert.That(target.GetValue("google", "baseUrl"), Is.EqualTo("https://google.example/v1"));
        }

        private static Type GetEditorType(string fullName)
        {
            Assembly editorAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Myrvo.FlowGraph.Editor");

            return editorAssembly.GetType(fullName, throwOnError: true);
        }

        private static void SetPrivateField(object instance, string fieldName, string value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(instance, value);
        }
    }
}
