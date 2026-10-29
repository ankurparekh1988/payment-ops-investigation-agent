using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

using YamlDotNet.RepresentationModel;

namespace PaymentOps.Evaluation.Tests;

/// <summary>The weekly checks plan the bootstrap as the operator would, so they need the operator's settings.</summary>
public sealed partial class ScheduledChecksTests
{
    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot.FullPath, .. path]));

    [Fact]
    public void Every_bootstrap_setting_the_operator_sets_reaches_the_drift_check()
    {
        var declared = BootstrapVariable().Matches(Read("infra", "deployments", "bootstrap", "variables.tf")).Select(m => "TF_VAR_" + m.Groups["name"].Value);
        var operatorSet = EnvLine().Matches(Read(".env.example")).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var settings = SettingsList().Match(Read("scripts", "configure-github.sh")).Groups["names"].Value
            .Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        var yaml = new YamlStream();
        using (var reader = new StringReader(Read(".github", "workflows", "scheduled-checks.yml")))
        {
            yaml.Load(reader);
        }

        var job = (YamlMappingNode)((YamlMappingNode)((YamlMappingNode)yaml.Documents[0].RootNode)["jobs"])["bootstrap-drift"];
        var env = ((YamlMappingNode)job["env"]).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToHashSet(StringComparer.Ordinal);

        // A setting the operator has but the check lacks makes Terraform plan to undo it, which reads as drift.
        var missing = declared.Where(operatorSet.Contains).Where(v => !env.Contains(v) && !settings.Contains(v)).ToList();
        Assert.Empty(missing);
    }

    [GeneratedRegex(@"^variable ""(?<name>\w+)""", RegexOptions.Multiline)]
    private static partial Regex BootstrapVariable();

    [GeneratedRegex(@"^(?<name>TF_VAR_\w+)=", RegexOptions.Multiline)]
    private static partial Regex EnvLine();

    [GeneratedRegex(@"required_settings=\((?<names>[^)]*)\)")]
    private static partial Regex SettingsList();
}
