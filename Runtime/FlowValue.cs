using System;
using UnityEngine;

namespace Myrvo.FlowGraph
{
    public enum FlowPortDirection
    {
        Input,
        Output
    }

    public enum FlowValueType
    {
        Trigger,
        Boolean,
        Number,
        String,
        Object
    }

    [Serializable]
    public struct FlowSerializedValue
    {
        [SerializeField] private FlowValueType valueType;
        [SerializeField] private bool boolValue;
        [SerializeField] private float numberValue;
        [SerializeField] private string stringValue;
        [SerializeField] private UnityEngine.Object objectValue;

        public FlowValueType ValueType
        {
            get => valueType;
            set => valueType = value;
        }

        public bool BoolValue
        {
            get => boolValue;
            set
            {
                valueType = FlowValueType.Boolean;
                boolValue = value;
            }
        }

        public float NumberValue
        {
            get => numberValue;
            set
            {
                valueType = FlowValueType.Number;
                numberValue = value;
            }
        }

        public string StringValue
        {
            get => stringValue;
            set
            {
                valueType = FlowValueType.String;
                stringValue = value;
            }
        }

        public UnityEngine.Object ObjectValue
        {
            get => objectValue;
            set
            {
                valueType = FlowValueType.Object;
                objectValue = value;
            }
        }

        public object GetValue()
        {
            return valueType switch
            {
                FlowValueType.Boolean => boolValue,
                FlowValueType.Number => numberValue,
                FlowValueType.String => stringValue,
                FlowValueType.Object => objectValue,
                _ => null
            };
        }

        public static FlowSerializedValue FromObject(object value)
        {
            FlowSerializedValue serializedValue = new FlowSerializedValue();

            switch (value)
            {
                case bool boolObject:
                    serializedValue.BoolValue = boolObject;
                    break;
                case int intObject:
                    serializedValue.NumberValue = intObject;
                    break;
                case float floatObject:
                    serializedValue.NumberValue = floatObject;
                    break;
                case double doubleObject:
                    serializedValue.NumberValue = (float)doubleObject;
                    break;
                case string stringObject:
                    serializedValue.StringValue = stringObject;
                    break;
                case UnityEngine.Object unityObject:
                    serializedValue.ObjectValue = unityObject;
                    break;
                default:
                    serializedValue.ValueType = FlowValueType.String;
                    serializedValue.StringValue = value?.ToString() ?? string.Empty;
                    break;
            }

            return serializedValue;
        }
    }
}
