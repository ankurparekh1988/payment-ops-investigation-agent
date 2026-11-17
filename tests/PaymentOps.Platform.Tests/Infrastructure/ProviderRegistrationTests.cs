using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>
/// Registering a resource provider is a subscription-level action only the bootstrap can take, and an unregistered one
/// fails only at apply time: plan accepts it. Every AzAPI resource names its provider, so each must be on the list.
/// </summary>
public sealed partial class ProviderRegistrationTests
{
    [Fact]
    public void Every_azapi_resource_provider_is_registered_by_the_bootstrap()
    {
        var infra = Path.Combine(RepositoryRoot.FullPath, "infra");
        var registered = RegisteredList().Match(File.ReadAllText(Path.Combine(infra, "deployments", "bootstrap", "providers.tf")));
        Assert.True(registered.Success, "the bootstrap lists the providers it registers");
        var providers = Quoted().Matches(registered.Groups["list"].Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var used = Directory.EnumerateFiles(infra, "*.tf", SearchOption.AllDirectories)
            .SelectMany(file => AzapiType().Matches(File.ReadAllText(file)).Select(m => m.Groups["provider"].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(used);
        Assert.Empty(used.Except(providers).Order(StringComparer.Ordinal));
    }

    [GeneratedRegex(@"resource_providers_to_register\s*=\s*\[(?<list>[^\]]*)\]")]
    private static partial Regex RegisteredList();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\btype\s*=\s*""(?<provider>Microsoft\.[A-Za-z]+)/")]
    private static partial Regex AzapiType();
}
