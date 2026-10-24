using System.Text.Json;

using PaymentOps.Agent;

namespace PaymentOps.Evaluation;

/// <summary>
/// How the harness reads its own input files: every field required, nothing unknown, and any malformed file
/// reported as invalid input naming the file, so every command treats a bad file the same way.
/// </summary>
internal static class StrictFiles
{
    /// <summary>camelCase, as the dataset and price files are written.</summary>
    public static JsonSerializerOptions CamelCase { get; } = Finished(StrictJson.Create(JsonNamingPolicy.CamelCase));

    /// <summary>snake_case, as the files the harness writes itself are.</summary>
    public static JsonSerializerOptions SnakeCase { get; } = Finished(StrictJson.Create(JsonNamingPolicy.SnakeCaseLower));

    public static T Read<T>(string path, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), options) ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    // Read-only with its resolver, so structured-output schema generation can use it from any thread.
    private static JsonSerializerOptions Finished(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
