using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>
/// Cost Management refuses an anomaly alert whose email message is longer than 100 characters, and only at apply
/// time: validate and plan both accept it.
/// </summary>
public sealed partial class CostAnomalyAlertTests
{
    [Fact]
    public void The_anomaly_alert_message_fits_the_service_limit()
    {
        var stack = File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "infra", "stacks", "bootstrap", "main.tf"));
        var message = AlertMessage().Match(stack);

        Assert.True(message.Success, "the bootstrap defines the anomaly alert's message");
        Assert.InRange(message.Groups["text"].Value.Length, 1, 100);
    }

    [GeneratedRegex(@"resource ""azurerm_cost_anomaly_alert"".*?\bmessage\s*=\s*""(?<text>[^""]*)""", RegexOptions.Singleline)]
    private static partial Regex AlertMessage();
}
