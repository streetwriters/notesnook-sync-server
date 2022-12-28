using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Streetwriters.Common.Converters
{
    /// <summary>
    /// Converts simple interface into an object (assumes that there is only one class of TInterface)
    /// </summary>
    /// <typeparam name="TInterface">Interface type</typeparam>
    /// <typeparam name="TClass">Class type</typeparam>
    public class InterfaceConverter<TInterface, TClass> : JsonConverter<TInterface> where TClass : TInterface
    {
        public override TInterface Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<TClass>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, TInterface value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case null:
                    JsonSerializer.Serialize(writer, null, options);
                    break;
                default:
                    {
                        var type = value.GetType();
                        JsonSerializer.Serialize(writer, value, type, options);
                        break;
                    }
            }
        }
    }
}