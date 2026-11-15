using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>
/// The app calls the model on the account's OpenAI endpoint and Content Safety on its generic one, which are
/// different hosts, so the throttling alert has to watch both or it never sees a throttled model call.
/// </summary>
public sealed partial class ThrottlingAlertTests
{
    [Fact]
    public void The_throttling_alert_watches_the_model_and_content_safety_hosts()
    {
        var stack = File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "infra", "stacks", "platform", "main.tf"));
        var hosts = HostsArgument().Match(stack);

        Assert.True(hosts.Success, "the stack passes ai_account_hosts to monitoring");
        Assert.Contains("module.foundry.openai_endpoint", hosts.Value, StringComparison.Ordinal);
        Assert.Contains("module.foundry.endpoint", hosts.Value, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"ai_account_hosts\s*=\s*\[[^\]]*\]", RegexOptions.Singleline)]
    private static partial Regex HostsArgument();
}
