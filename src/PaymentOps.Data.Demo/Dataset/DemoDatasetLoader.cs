using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentOps.Platform.Text;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>Reads a dataset strictly and refuses one that doesn't hold together.</summary>
internal static class DemoDatasetLoader
{
    /// <summary>The curated dataset, built into the assembly so the deployed app needs no data files.</summary>
    public const string DefaultResource = "data/datasets/default/dataset.json";

    // The domain records are read directly: every non-nullable constructor parameter is required,
    // and an unknown property is an error, so a typo can't silently drop a field.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new UtcDateTimeOffsetConverter(), new NonNullListConverterFactory() },
    };

    public static DemoDataset LoadDefault()
    {
        using var json = typeof(DemoDatasetLoader).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException($"The embedded dataset '{DefaultResource}' is missing from the build.");
        return Load(json);
    }

    public static DemoDataset Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var text = new StreamReader(json).ReadToEnd();

        // The dataset ships in the public repository, under the same rule as the knowledge sources.
        var errors = new List<string>();
        PublicContentCheck.Check(text, DefaultResource, 1, errors);

        DemoDataset? dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<DemoDataset>(text, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDemoDatasetException([.. errors, $"The dataset doesn't match its format: {ex.Message}"]);
        }

        errors.AddRange(dataset is null ? ["The dataset is empty."] : DemoDatasetIntegrity.Check(dataset));
        return errors.Count == 0 ? DemoDatasetSnapshot.Freeze(dataset!) : throw new InvalidDemoDatasetException(errors);
    }

    /// <summary>
    /// Returns an immutable copy of the dataset if it passes every integrity rule; otherwise throws with all
    /// the problems.
    /// </summary>
    public static DemoDataset Checked(DemoDataset dataset)
    {
        var errors = DemoDatasetIntegrity.Check(dataset);
        return errors.Count == 0 ? DemoDatasetSnapshot.Freeze(dataset) : throw new InvalidDemoDatasetException(errors);
    }
}
