using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>Rejects any time not written in UTC, for every timestamp field at once.</summary>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    // A local offset would silently shift cut-off and calendar reasoning.
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTimeOffset();
        return value.Offset == TimeSpan.Zero ? value : throw new JsonException($"'{value:O}' must be written in UTC (Z).");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

/// <summary>
/// Rejects a null element in any list. The serializer checks nullable annotations on properties but not on
/// collection elements, so without this a <c>[null]</c> would reach the integrity check as a null id.
/// </summary>
internal sealed class NonNullListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
        && !typeToConvert.GetGenericArguments()[0].IsValueType;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(NonNullListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class NonNullListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? throw new JsonException("A list can't be null.");
            return items.Contains(default!) ? throw new JsonException("A list can't contain null.") : items;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize<IEnumerable<T>>(writer, value, options);
    }
}
