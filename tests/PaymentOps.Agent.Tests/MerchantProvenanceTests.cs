using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// A merchant is looked up only when the user named it: an id that reached the model some other way, such as in a
/// record or a runbook, is refused before any tool runs.
/// </summary>
public sealed class MerchantProvenanceTests
{
    private const string Refused = "\"code\":\"invalid_argument\"";
    private const string Found = "\"status\":\"ok\"";

    private readonly IReadOnlyDictionary<string, IInvestigationTool> _tools = DemoTools.Build();

    private Task<string> CallAsync(MerchantSources? merchants, string tool, Dictionary<string, object?> arguments) =>
        CallAsync(new ToolGuard(new TurnState(), new InvestigationLimits(), new FakeTimeProvider(), TestUser.Engineer, null, ScriptedScreen.AllClean, merchants: merchants), tool, arguments);

    private async Task<string> CallAsync(ToolGuard guard, string tool, Dictionary<string, object?> arguments) =>
        (string)(await guard.Wrap(_tools[tool]).InvokeAsync(new AIFunctionArguments(arguments)))!;

    [Fact]
    public async Task A_merchant_the_question_names_is_looked_up()
    {
        var result = await CallAsync(new MerchantSources(["Why is M-1042's payout late?"]), ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-1042" });

        Assert.Contains(Found, result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ToolNames.MerchantProfile, false)]
    [InlineData(ToolNames.SettlementBatches, true)]
    [InlineData(ToolNames.ErrorEvents, true)]
    public async Task A_merchant_the_user_never_named_is_refused_before_the_tool_runs(string tool, bool dated)
    {
        Dictionary<string, object?> arguments = dated
            ? new() { ["merchant_id"] = "M-2087", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" }
            : new() { ["merchant_id"] = "M-2087" };

        var result = await CallAsync(new MerchantSources(["Why is M-1042's payout late?"]), tool, arguments);

        Assert.Contains(Refused, result, StringComparison.Ordinal);
        Assert.Contains("named in the user's questions", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_merchant_an_earlier_question_named_is_looked_up()
    {
        var result = await CallAsync(new MerchantSources(["And its batches this week?", "Why is M-2087's money held?"]), ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-2087" });

        Assert.Contains(Found, result, StringComparison.Ordinal);
    }

    // Written as people write them: at the end of a sentence, or in another case than the records use.
    [Theory]
    [InlineData("Check the payout for M-1042.")]
    [InlineData("Is m-1042 paid?")]
    [InlineData("(M-1042) wants to know")]
    public async Task A_named_merchant_is_found_however_the_question_punctuates_it(string question)
    {
        var result = await CallAsync(new MerchantSources([question]), ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-1042" });

        Assert.Contains(Found, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Part_of_a_longer_id_is_not_a_named_merchant()
    {
        var result = await CallAsync(new MerchantSources(["Is M-10421 paid?"]), ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-1042" });

        Assert.Contains(Refused, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_without_a_merchant_isnt_checked()
    {
        var result = await CallAsync(new MerchantSources(["Did Fabrikam have errors?"]), ToolNames.ErrorEvents,
            new() { ["processor_id"] = "fabrikam-pay", ["start_date"] = "2026-11-04", ["end_date"] = "2026-11-10" });

        Assert.Contains(Found, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_merchant_counts_as_a_correction()
    {
        var turn = new TurnState();
        var guard = new ToolGuard(turn, new InvestigationLimits { MaxCorrections = 1 }, new FakeTimeProvider(), TestUser.Engineer, null, ScriptedScreen.AllClean,
            merchants: new MerchantSources(["Why is M-1042's payout late?"]));

        await CallAsync(guard, ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-2087" });
        await CallAsync(guard, ToolNames.MerchantProfile, new() { ["merchant_id"] = "M-6230" });

        Assert.True(turn.Stopped);
    }
}
