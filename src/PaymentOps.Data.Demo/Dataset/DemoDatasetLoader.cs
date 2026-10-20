using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>Reads a dataset strictly and refuses one that doesn't hold together.</summary>
internal static class DemoDatasetLoader
{
    // The domain records are read directly: every non-nullable constructor parameter is required,
    // and an unknown property is an error, so a typo can't silently drop a field.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>The curated dataset, built into the assembly so the deployed app needs no data files.</summary>
    public const string DefaultResource = "data/datasets/default/dataset.json";

    public static DemoDataset LoadDefault()
    {
        using var json = typeof(DemoDatasetLoader).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException($"The embedded dataset '{DefaultResource}' is missing from the build.");
        return Load(json);
    }

    public static DemoDataset Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        DemoDataset? dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<DemoDataset>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDemoDatasetException([$"The dataset isn't valid JSON for its format: {ex.Message}"]);
        }

        return Checked(dataset ?? throw new InvalidDemoDatasetException(["The dataset is empty."]));
    }

    /// <summary>Returns the dataset if it passes every integrity rule; otherwise throws with all the problems.</summary>
    public static DemoDataset Checked(DemoDataset dataset)
    {
        var errors = DemoDatasetIntegrity.Check(dataset);
        return errors.Count == 0 ? dataset : throw new InvalidDemoDatasetException(errors);
    }
}
