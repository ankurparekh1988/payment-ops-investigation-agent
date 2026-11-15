using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>Counting in memory is for Development only; a deployment without its table fails at startup.</summary>
public sealed class UsageRegistrationTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "PaymentOps.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Configuration(string? tableEndpoint = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentOps:Usage:DailyLimitPerUser"] = "25",
            ["PaymentOps:Usage:DailyLimitTotal"] = "150",
            ["PaymentOps:Usage:TableEndpoint"] = tableEndpoint,
        })
        .Build();

    [Fact]
    public void Outside_Development_a_table_endpoint_is_required()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddUsageGuard(Configuration(), new Environment(Environments.Production)));

        Assert.Equal("PaymentOps:Usage:TableEndpoint is required outside Development.", error.Message);
    }

    [Fact]
    public void Development_counts_in_memory_without_a_table()
    {
        using var provider = new ServiceCollection().AddUsageGuard(Configuration(), new Environment(Environments.Development)).BuildServiceProvider();

        Assert.IsType<InMemoryUsageCounter>(provider.GetRequiredService<IUsageCounter>());
    }
}
