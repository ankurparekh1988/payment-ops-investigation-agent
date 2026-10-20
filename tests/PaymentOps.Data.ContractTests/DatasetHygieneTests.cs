using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Platform.Text;

namespace PaymentOps.Data.ContractTests;

public sealed class DatasetHygieneTests
{
    // The dataset ships in a public repository, under the same rules as the knowledge sources.
    [Fact]
    public void The_dataset_has_no_card_like_numbers_or_real_domains()
    {
        using var stream = typeof(DemoDataset).Assembly.GetManifestResourceStream(DemoDatasetLoader.DefaultResource)!;
        var json = new StreamReader(stream).ReadToEnd();
        var errors = new List<string>();

        PublicContentCheck.Check(json, DemoDatasetLoader.DefaultResource, 1, errors);

        Assert.Empty(errors);
    }
}
