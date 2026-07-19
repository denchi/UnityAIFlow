using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    public enum FlowModelCapability
    {
        None,
        Text,
        Image,
        Video,
        Audio
    }

    public enum FlowNodeOptionFieldType
    {
        Text,
        TextArea,
        Number,
        Select,
        Slider,
        Toggle,
        Checkbox
    }

    [Serializable]
    public sealed class FlowOptionChoice
    {
        [SerializeField] private string label;
        [SerializeField] private string value;

        public string Label => label;
        public string Value => value;

        public FlowOptionChoice()
        {
        }

        public FlowOptionChoice(string label, string value)
        {
            this.label = label;
            this.value = value;
        }
    }

    [Serializable]
    public sealed class FlowSelectConstraint
    {
        [SerializeField] private string key;
        [SerializeField] private string label;
        [SerializeField] private string defaultValue;
        [SerializeField] private bool hiddenWhenUnsupported;
        [SerializeField] private List<FlowOptionChoice> options = new List<FlowOptionChoice>();

        public string Key => key;
        public string Label => string.IsNullOrWhiteSpace(label) ? key : label;
        public string DefaultValue => defaultValue;
        public bool HiddenWhenUnsupported => hiddenWhenUnsupported;
        public IReadOnlyList<FlowOptionChoice> Options => options;

        public FlowSelectConstraint()
        {
        }

        public FlowSelectConstraint(string key, string label, string defaultValue, bool hiddenWhenUnsupported, IEnumerable<FlowOptionChoice> options)
        {
            this.key = key;
            this.label = label;
            this.defaultValue = defaultValue;
            this.hiddenWhenUnsupported = hiddenWhenUnsupported;
            this.options = options == null ? new List<FlowOptionChoice>() : new List<FlowOptionChoice>(options);
        }
    }

    [Serializable]
    public sealed class FlowBooleanConstraint
    {
        [SerializeField] private string key;
        [SerializeField] private string label;
        [SerializeField] private bool supported = true;
        [SerializeField] private bool defaultValue;
        [SerializeField] private bool hiddenWhenUnsupported = true;

        public string Key => key;
        public string Label => string.IsNullOrWhiteSpace(label) ? key : label;
        public bool Supported => supported;
        public bool DefaultValue => defaultValue;
        public bool HiddenWhenUnsupported => hiddenWhenUnsupported;

        public FlowBooleanConstraint()
        {
        }

        public FlowBooleanConstraint(string key, string label, bool supported, bool defaultValue, bool hiddenWhenUnsupported)
        {
            this.key = key;
            this.label = label;
            this.supported = supported;
            this.defaultValue = defaultValue;
            this.hiddenWhenUnsupported = hiddenWhenUnsupported;
        }
    }

    [Serializable]
    public sealed class FlowRangeConstraint
    {
        [SerializeField] private string key;
        [SerializeField] private string label;
        [SerializeField] private float min;
        [SerializeField] private float max = 1f;
        [SerializeField] private float step = 0.05f;
        [SerializeField] private float defaultValue;

        public string Key => key;
        public string Label => string.IsNullOrWhiteSpace(label) ? key : label;
        public float Min => min;
        public float Max => max;
        public float Step => step;
        public float DefaultValue => defaultValue;

        public FlowRangeConstraint()
        {
        }

        public FlowRangeConstraint(string key, string label, float min, float max, float step, float defaultValue)
        {
            this.key = key;
            this.label = label;
            this.min = min;
            this.max = max;
            this.step = step;
            this.defaultValue = defaultValue;
        }
    }

    [Serializable]
    public sealed class FlowNodeOptionSchema
    {
        [SerializeField] private string key;
        [SerializeField] private string label;
        [SerializeField] private FlowNodeOptionFieldType fieldType;
        [SerializeField] private string defaultStringValue;
        [SerializeField] private float defaultNumberValue;
        [SerializeField] private bool defaultBooleanValue;
        [SerializeField] private float min;
        [SerializeField] private float max = 1f;
        [SerializeField] private float step = 1f;
        [SerializeField] private List<FlowOptionChoice> options = new List<FlowOptionChoice>();

        public string Key => key;
        public string Label => string.IsNullOrWhiteSpace(label) ? key : label;
        public FlowNodeOptionFieldType FieldType => fieldType;
        public string DefaultStringValue => defaultStringValue;
        public float DefaultNumberValue => defaultNumberValue;
        public bool DefaultBooleanValue => defaultBooleanValue;
        public float Min => min;
        public float Max => max;
        public float Step => step;
        public IReadOnlyList<FlowOptionChoice> Options => options;

        public FlowNodeOptionSchema()
        {
        }

        public FlowNodeOptionSchema(string key, string label, FlowNodeOptionFieldType fieldType, string defaultStringValue, float defaultNumberValue, bool defaultBooleanValue, float min, float max, float step, IEnumerable<FlowOptionChoice> options)
        {
            this.key = key;
            this.label = label;
            this.fieldType = fieldType;
            this.defaultStringValue = defaultStringValue;
            this.defaultNumberValue = defaultNumberValue;
            this.defaultBooleanValue = defaultBooleanValue;
            this.min = min;
            this.max = max;
            this.step = step;
            this.options = options == null ? new List<FlowOptionChoice>() : new List<FlowOptionChoice>(options);
        }
    }

    [Serializable]
    public sealed class FlowModelConstraints
    {
        [SerializeField] private List<FlowSelectConstraint> selectConstraints = new List<FlowSelectConstraint>();
        [SerializeField] private List<FlowBooleanConstraint> booleanConstraints = new List<FlowBooleanConstraint>();
        [SerializeField] private List<FlowRangeConstraint> rangeConstraints = new List<FlowRangeConstraint>();

        public IReadOnlyList<FlowSelectConstraint> SelectConstraints => selectConstraints;
        public IReadOnlyList<FlowBooleanConstraint> BooleanConstraints => booleanConstraints;
        public IReadOnlyList<FlowRangeConstraint> RangeConstraints => rangeConstraints;

        public FlowSelectConstraint GetSelectConstraint(string key)
        {
            return selectConstraints.FirstOrDefault(constraint => string.Equals(constraint.Key, key, StringComparison.Ordinal));
        }

        public FlowBooleanConstraint GetBooleanConstraint(string key)
        {
            return booleanConstraints.FirstOrDefault(constraint => string.Equals(constraint.Key, key, StringComparison.Ordinal));
        }

        public FlowRangeConstraint GetRangeConstraint(string key)
        {
            return rangeConstraints.FirstOrDefault(constraint => string.Equals(constraint.Key, key, StringComparison.Ordinal));
        }

        public FlowModelConstraints()
        {
        }

        public FlowModelConstraints(IEnumerable<FlowSelectConstraint> selectConstraints, IEnumerable<FlowBooleanConstraint> booleanConstraints, IEnumerable<FlowRangeConstraint> rangeConstraints)
        {
            this.selectConstraints = selectConstraints == null ? new List<FlowSelectConstraint>() : new List<FlowSelectConstraint>(selectConstraints);
            this.booleanConstraints = booleanConstraints == null ? new List<FlowBooleanConstraint>() : new List<FlowBooleanConstraint>(booleanConstraints);
            this.rangeConstraints = rangeConstraints == null ? new List<FlowRangeConstraint>() : new List<FlowRangeConstraint>(rangeConstraints);
        }
    }

    [Serializable]
    public sealed class FlowProviderDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private string baseUrl;
        [SerializeField] private List<FlowModelCapability> supportedCapabilities = new List<FlowModelCapability>();

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public string BaseUrl => baseUrl;
        public IReadOnlyList<FlowModelCapability> SupportedCapabilities => supportedCapabilities;

        public FlowProviderDefinition()
        {
        }

        public FlowProviderDefinition(string id, string label, string baseUrl, IEnumerable<FlowModelCapability> supportedCapabilities)
        {
            this.id = id;
            this.label = label;
            this.baseUrl = baseUrl;
            this.supportedCapabilities = supportedCapabilities == null ? new List<FlowModelCapability>() : new List<FlowModelCapability>(supportedCapabilities);
        }
    }

    [Serializable]
    public sealed class FlowModelDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string providerId;
        [SerializeField] private string label;
        [SerializeField] private List<FlowModelCapability> capabilities = new List<FlowModelCapability>();
        [SerializeField] private List<FlowProviderRequestKind> supportedRequestKinds = new List<FlowProviderRequestKind>();
        [SerializeField] private FlowModelConstraints constraints = new FlowModelConstraints();
        [SerializeField] private List<FlowNodeOptionSchema> optionSchemas = new List<FlowNodeOptionSchema>();

        public string Id => id;
        public string ProviderId => providerId;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public IReadOnlyList<FlowModelCapability> Capabilities => capabilities;
        public IReadOnlyList<FlowProviderRequestKind> SupportedRequestKinds => supportedRequestKinds;
        public FlowModelConstraints Constraints => constraints;
        public IReadOnlyList<FlowNodeOptionSchema> OptionSchemas => optionSchemas;

        public bool SupportsCapability(FlowModelCapability capability)
        {
            return capabilities.Contains(capability);
        }

        public bool SupportsRequestKind(FlowProviderRequestKind requestKind)
        {
            return supportedRequestKinds.Count == 0 || supportedRequestKinds.Contains(requestKind);
        }

        public FlowModelDefinition()
        {
        }

        public FlowModelDefinition(string id, string providerId, string label, IEnumerable<FlowModelCapability> capabilities, IEnumerable<FlowProviderRequestKind> supportedRequestKinds, FlowModelConstraints constraints, IEnumerable<FlowNodeOptionSchema> optionSchemas)
        {
            this.id = id;
            this.providerId = providerId;
            this.label = label;
            this.capabilities = capabilities == null ? new List<FlowModelCapability>() : new List<FlowModelCapability>(capabilities);
            this.supportedRequestKinds = supportedRequestKinds == null ? new List<FlowProviderRequestKind>() : new List<FlowProviderRequestKind>(supportedRequestKinds);
            this.constraints = constraints ?? new FlowModelConstraints();
            this.optionSchemas = optionSchemas == null ? new List<FlowNodeOptionSchema>() : new List<FlowNodeOptionSchema>(optionSchemas);
        }
    }

    public static class FlowProviderRegistryLocator
    {
        private static FlowProviderSettingsAsset cachedSettings;

        public static FlowProviderSettingsAsset Settings
        {
            get
            {
                if (cachedSettings == null)
                {
                    cachedSettings = Resources.LoadAll<FlowProviderSettingsAsset>(string.Empty).FirstOrDefault();
                }

                return cachedSettings;
            }
        }

        public static FlowProviderRegistryAsset Registry => Settings != null ? Settings.Registry : null;

        public static void ResetCache()
        {
            cachedSettings = null;
        }
    }
}
