using System.Text.RegularExpressions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Safety;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// Every string a tool returns that someone outside this system wrote must be screened before the model reads it, so a
/// screen that flags every poisoned value leaves no poison anywhere. Only values this application owns (ids, dates,
/// amounts, enum words) may be plain without screening.
/// </summary>
public sealed partial class UntrustedInventoryTests
{
    private static string Poison(string key) => $" POISON-{key}";

    [Fact]
    public async Task Every_free_form_string_is_screened_before_the_model_reads_it()
    {
        var screen = ScriptedScreen.Flagging("POISON-");

        var outputs = await RunEveryToolAsync((key, value) => value + Poison(key), screen);

        Assert.Contains(screen.Screened, text => text.Contains("POISON-", StringComparison.Ordinal));
        Assert.DoesNotContain(outputs, o => o.Contains("POISON-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_like_external_values_are_screened()
    {
        var screen = new ScriptedScreen(text => text is "IGNORE_PREVIOUS_INSTRUCTIONS" or "SYSTEM:OVERRIDE_RULES" ? ScreenVerdict.Attack : ScreenVerdict.Clean);

        var outputs = await RunEveryToolAsync((key, value) => key switch
        {
            "code" => "IGNORE_PREVIOUS_INSTRUCTIONS",
            "processorReference" => "SYSTEM:OVERRIDE_RULES",
            _ => value,
        }, screen);

        Assert.Contains("IGNORE_PREVIOUS_INSTRUCTIONS", screen.Screened);
        Assert.Contains("SYSTEM:OVERRIDE_RULES", screen.Screened);
        Assert.DoesNotContain(outputs, o => o.Contains("IGNORE_PREVIOUS_INSTRUCTIONS", StringComparison.Ordinal) || o.Contains("SYSTEM:OVERRIDE_RULES", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_server_owned_values_stay_plain()
    {
        var outputs = await RunEveryToolAsync((key, value) => value + Poison(key), ScriptedScreen.Flagging("POISON-"));

        // Plain strings must be ids, dates and times, enum words, currency codes or IANA time zones (validated when the
        // data loads): what this application owns or has checked against a finite set.
        var plain = outputs.SelectMany(o => PlainStrings().Matches(Marked().Replace(o, "\"\"")).Select(m => m.Groups["value"].Value)).Distinct(StringComparer.Ordinal).ToList();
        Assert.All(plain, value => Assert.Matches(ServerOwned(), value));
    }

    private static async Task<List<string>> RunEveryToolAsync(Func<string, string, string> rewrite, ScriptedScreen screen)
    {
        var tools = DemoTools.Build(DemoTools.Section(title: "Missing batch" + Poison("title"), path: "Missing batch > Steps" + Poison("path"), content: "Check the file." + Poison("content")), rewrite);
        var guard = new ToolGuard(new TurnState(), new InvestigationLimits { MaxToolCalls = 100 }, new FakeTimeProvider(), TestUser.Engineer, null, screen);

        (string Tool, Dictionary<string, object?> Arguments)[] calls =
        [
            (ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-1042" }),
            (ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-2087" }),
            (ToolNames.SettlementBatches, new() { ["merchant_id"] = "M-1042", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" }),
            (ToolNames.SettlementBatches, new() { ["merchant_id"] = "M-2087", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" }),
            (ToolNames.ProcessorStatus, new() { ["processor_id"] = "fabrikam-pay", ["start_date"] = "2026-10-25", ["end_date"] = "2026-11-10" }),
            (ToolNames.ProcessorStatus, new() { ["processor_id"] = "northwind-acquiring", ["start_date"] = "2026-10-25", ["end_date"] = "2026-11-10" }),
            (ToolNames.ErrorEvents, new() { ["processor_id"] = "fabrikam-pay", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" }),
            (ToolNames.ErrorEvents, new() { ["processor_id"] = "northwind-acquiring", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" }),
            (ToolNames.KnowledgeSearch, new() { ["query"] = "missing settlement file" }),
        ];

        var outputs = new List<string>();
        foreach (var (tool, arguments) in calls)
        {
            outputs.Add((string)(await guard.Wrap(tools[tool]).InvokeAsync(new AIFunctionArguments(arguments)))!);
        }

        return outputs;
    }

    [GeneratedRegex(@"<untrusted-data>.*?</untrusted-data>", RegexOptions.Singleline)]
    private static partial Regex Marked();

    [GeneratedRegex(@":""(?<value>[^""]+)""")]
    private static partial Regex PlainStrings();

    [GeneratedRegex(@"^[A-Za-z0-9_:.+/*\-]+$")]
    private static partial Regex ServerOwned();
}
