using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// The four scenarios end to end on the shipped dataset and corpus: real tools, loop, guard and validation,
/// with the model's trajectory scripted. A trajectory a model might take, not the only one.
/// </summary>
public sealed class InvestigationScenarioTests
{
    private const string Submit = ToolNames.SubmitAssessment;
    private const string Fabrikam = "fabrikam-pay";
    private const string MissingFile = "fact:missing-file:fabrikam-pay:2026-11-09";
    private const string Timeouts = "fact:event-group:fabrikam-pay:*:file_transfer_timeout:6a40626c31a3:2026-11-09:2026-11-10";

    private static readonly Dictionary<string, string?> AllSourcesDown = new()
    {
        ["DemoData:Faults:Merchants:Mode"] = "Unavailable",
        ["DemoData:Faults:Settlement:Mode"] = "Unavailable",
        ["DemoData:Faults:Processors:Mode"] = "Unavailable",
        ["DemoData:Faults:ErrorEvents:Mode"] = "Unavailable",
    };

    private static object Statement(string text, string[] evidence, string[] citations) => new { text, evidence_ids = evidence, citation_ids = citations };

    private static object Assessment(object rootCause, string[] claimEvidence, string[] claimCitations, string confidence = "high", string summaryEvidence = "merchant:M-1042") => new
    {
        outcome = "assessed",
        summary = Statement("The payout was delayed.", [summaryEvidence], ["S1"]),
        claims = new[] { Statement("What the data and the runbook show.", claimEvidence, claimCitations) },
        root_cause = rootCause,
        confidence,
        next_steps = new[] { new { text = "Follow the runbook.", citation_ids = new[] { "S1" } } },
        proposed_ticket = (object?)null,
    };

    private static ScriptedChatClient Model(params ChatResponse[] responses) =>
        new([.. responses.Select(r => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => r))]);

    private static ChatResponse[] S1Trajectory(object submission) =>
    [
        Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
        Call(ToolNames.SettlementBatches, new { merchant_id = "M-1042", start_date = "2026-11-09", end_date = "2026-11-10" }),
        Call(ToolNames.ProcessorStatus, new { processor_id = Fabrikam, start_date = "2026-11-09", end_date = "2026-11-10" }),
        Call(ToolNames.ErrorEvents, new { processor_id = Fabrikam, start_date = "2026-11-09", end_date = "2026-11-10" }),
        Call(ToolNames.KnowledgeSearch, new { query = "payout-delay-processor-outage" }),
        Call(Submit, submission),
    ];

    private static object S1Assessment(string confidence = "high") => Assessment(
        Statement("A Fabrikam settlement outage covering the cut-off stopped the 9 November file.", [MissingFile, "incident:INC-FAB-1109", Timeouts], ["S1"]),
        [MissingFile, Timeouts],
        ["S1"],
        confidence);

    private static async Task<AssessmentResult> RunAsync(ScriptedChatClient model, Dictionary<string, string?>? faults = null, IUserContext? user = null, int maxListItems = 20)
    {
        using var fixture = new AgentFixture(faults, new CorpusKnowledge(), maxListItems);
        return await fixture.Runner(model, user).InvestigateAsync("Why were M-1042's payouts delayed yesterday?", CancellationToken.None);
    }

    [Fact]
    public async Task S1_an_outage_with_data_and_runbook_support_is_an_assessed_root_cause()
    {
        var result = await RunAsync(Model(S1Trajectory(S1Assessment())));

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal(RootCauseLabel.Assessment, assessed.RootCause!.Label);
        Assert.Equal(Confidence.High, assessed.Confidence);
        Assert.Contains(assessed.RootCause.Statement.Evidence, e => e.Id == MissingFile && e.IsDerived);
        Assert.StartsWith("payout-delay-processor-outage", assessed.RootCause.Statement.Citations[0].SectionKey, StringComparison.Ordinal);
        Assert.Empty(assessed.Gaps);
    }

    [Fact]
    public async Task S1_a_payout_no_tool_returned_is_corrected_once_then_unverified()
    {
        var bad = Assessment(Statement("Outage.", ["payout:PO-404"], ["S1"]), ["payout:PO-404"], ["S1"]);
        var model = Model([.. S1Trajectory(bad), Call(Submit, bad)]);

        var result = await RunAsync(model);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    // With two event ids shown out of five, a count can only rest on the group that counted all five.
    [Fact]
    public async Task S1_a_count_resting_on_the_shown_event_ids_is_rejected()
    {
        static object CountClaim(string[] evidence) => new
        {
            outcome = "assessed",
            summary = Statement("Fabrikam's file didn't arrive.", [MissingFile], []),
            claims = new[] { Statement("There were 5 file transfer timeouts from Fabrikam.", evidence, []) },
            root_cause = Statement(string.Empty, [], []),
            confidence = "medium",
            next_steps = Array.Empty<object>(),
            proposed_ticket = (object?)null,
        };
        var shownOnly = CountClaim(["event:EV-1109-01", "event:EV-1109-02"]);
        var model = Model([.. S1Trajectory(shownOnly), Call(Submit, shownOnly)]);

        var result = await RunAsync(model, maxListItems: 2);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    [Fact]
    public async Task S1_with_settlement_down_keeps_the_gap_and_caps_confidence_whatever_the_model_says()
    {
        var submission = Assessment(Statement("An outage.", ["incident:INC-FAB-1109", Timeouts], ["S1"]), ["incident:INC-FAB-1109"], ["S1"]);
        var model = Model(S1Trajectory(submission));

        var result = await RunAsync(model, new() { ["DemoData:Faults:Settlement:Mode"] = "Unavailable" });

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal(Confidence.Medium, assessed.Confidence);
        Assert.Contains("Settlement data was unavailable.", assessed.Gaps);
    }

    [Fact]
    public async Task With_every_data_source_down_the_outcome_is_incomplete_with_no_root_cause()
    {
        var submission = new
        {
            outcome = "assessed",
            summary = Statement("Outages delay payouts.", [], ["S1"]),
            claims = new[] { Statement("The runbook describes outage handling.", [], ["S1"]) },
            root_cause = Statement("A processor outage.", [], ["S1"]),
            confidence = "high",
            next_steps = Array.Empty<object>(),
            proposed_ticket = (object?)null,
        };

        var result = await RunAsync(Model(S1Trajectory(submission)), AllSourcesDown);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal(AssessmentOutcome.Incomplete, assessed.Outcome);
        Assert.Null(assessed.RootCause);
        Assert.Equal(Confidence.Low, assessed.Confidence);
    }

    // A ticket naming nothing this investigation returned: it would fail every proposal check that applies.
    private static readonly object UnsupportedTicket = new
    {
        processor_id = Fabrikam,
        merchant_ids = new[] { "M-9999" },
        start_date = "2026-11-09",
        end_date = "2026-11-09",
        title = "Fabrikam Pay settlement file missing",
        summary = "No settlement file arrived from Fabrikam Pay.",
        evidence_ids = new[] { MissingFile },
        citation_ids = new[] { "S9" },
    };

    // The server will force incomplete and withhold the ticket, so the ticket isn't checked and costs no correction.
    [Fact]
    public async Task With_every_data_source_down_an_unsupported_ticket_is_withheld_without_a_correction()
    {
        var submission = new
        {
            outcome = "assessed",
            summary = Statement("Outages delay payouts.", [], ["S1"]),
            claims = new[] { Statement("The runbook describes outage handling.", [], ["S1"]) },
            root_cause = Statement(string.Empty, [], []),
            confidence = "high",
            next_steps = Array.Empty<object>(),
            proposed_ticket = UnsupportedTicket,
        };
        var model = Model(S1Trajectory(submission));

        var result = await RunAsync(model, AllSourcesDown);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal(AssessmentOutcome.Incomplete, assessed.Outcome);
        Assert.Equal(Confidence.Low, assessed.Confidence);
        Assert.Null(assessed.Proposal);
        Assert.True(assessed.ProposalWithheld);
        Assert.Equal(6, model.Requests.Count);
    }

    // A request no read-only tool can serve is refused without manufacturing evidence, and people see the server's notice.
    [Fact]
    public async Task A_request_to_change_data_is_refused_with_no_tool_calls_and_the_server_notice()
    {
        var refusal = new
        {
            outcome = "refused",
            summary = Statement("I can't issue refunds.", [], []),
            claims = Array.Empty<object>(),
            root_cause = Statement(string.Empty, [], []),
            confidence = "high",
            next_steps = Array.Empty<object>(),
            proposed_ticket = (object?)null,
        };
        var model = Model(Call(Submit, refusal));
        using var fixture = new AgentFixture(null, new CorpusKnowledge(), 20);

        var result = await fixture.Runner(model, null).InvestigateAsync("Refund M-1042's last payout.", CancellationToken.None);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal(AssessmentOutcome.Refused, assessed.Outcome);
        Assert.Equal(AssessmentPolicy.RefusalNotice, assessed.Summary.Text);
        Assert.Empty(assessed.Claims);
        Assert.Null(assessed.RootCause);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task S2_a_capture_after_the_cut_off_before_a_closure_day_is_explained_from_computed_facts()
    {
        var submission = Assessment(
            Statement("Captured after Northwind's cut-off before a closure day and a weekend, so it settled in Monday's batch.", ["fact:expected-batch:PAY-6230-04", "payout:PO-6230-NW-1109"], ["S1"]),
            ["fact:expected-batch:PAY-6230-04", "fact:non-settlement-day:2026-11-06"],
            ["S1"],
            summaryEvidence: "payout:PO-6230-NW-1109");
        var model = Model(
            Call(ToolNames.SettlementBatches, new { merchant_id = "M-6230", start_date = "2026-11-05", end_date = "2026-11-10" }),
            Call(ToolNames.KnowledgeSearch, new { query = "settlement-closure-days-policy" }),
            Call(Submit, submission));

        var assessed = Assert.IsType<AssessmentResult.Assessed>(await RunAsync(model));

        Assert.Equal(RootCauseLabel.Assessment, assessed.RootCause!.Label);
    }

    // An empty settlement source shows no batches; it must never read as a missing file.
    [Fact]
    public async Task S2_with_an_empty_settlement_source_registers_no_missing_file()
    {
        var claim = Assessment(Statement("A missing file.", ["fact:missing-file:northwind-acquiring:2026-11-09"], ["S1"]), ["fact:missing-file:northwind-acquiring:2026-11-09"], ["S1"], summaryEvidence: "fact:missing-file:northwind-acquiring:2026-11-09");
        var model = Model(
            Call(ToolNames.SettlementBatches, new { merchant_id = "M-6230", start_date = "2026-11-05", end_date = "2026-11-10" }),
            Call(ToolNames.KnowledgeSearch, new { query = "missing-settlement-batch" }),
            Call(Submit, claim),
            Call(Submit, claim));

        var result = await RunAsync(model, new() { ["DemoData:Faults:Settlement:Mode"] = "Empty" });

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    // A General user's search never returns the Restricted policy, so it can't be cited or even mentioned.
    [Fact]
    public async Task S3_a_general_user_never_receives_or_cites_the_restricted_policy()
    {
        var submission = Assessment(Statement("The RF-37 rule placed the hold.", ["hold:RH-2087"], ["S1"]), ["hold:RH-2087"], ["S1"], summaryEvidence: "hold:RH-2087");
        var model = Model(
            Call(ToolNames.MerchantProfile, new { merchant_id = "M-2087" }),
            Call(ToolNames.KnowledgeSearch, new { query = "risk-hold-review-policy" }),
            Call(Submit, submission),
            Call(Submit, submission));

        var result = await RunAsync(model, user: TestUser.Reader);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.DoesNotContain(model.Requests.SelectMany(r => ToolResults(r.Messages)), r => r.Contains("restricted", StringComparison.OrdinalIgnoreCase) || r.Contains("\"citation_id\":", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S3_a_risk_and_compliance_user_gets_the_policy_and_an_assessment()
    {
        var submission = Assessment(Statement("The RF-37 rule placed the hold.", ["hold:RH-2087"], ["S1"]), ["hold:RH-2087"], ["S1"], summaryEvidence: "hold:RH-2087");
        var model = Model(
            Call(ToolNames.MerchantProfile, new { merchant_id = "M-2087" }),
            Call(ToolNames.KnowledgeSearch, new { query = "risk-hold-review-policy" }),
            Call(Submit, submission));

        var assessed = Assert.IsType<AssessmentResult.Assessed>(await RunAsync(model, user: TestUser.RestrictedReader));

        Assert.StartsWith("risk-hold-review-policy", assessed.RootCause!.Statement.Citations[0].SectionKey, StringComparison.Ordinal);
    }

    // The tool states how the files relate; concluding "duplicate" is the agent's, from the relation, the resend and the runbook.
    [Fact]
    public async Task S4_the_tools_report_identical_payment_sets_and_the_agent_draws_the_conclusion()
    {
        var submission = Assessment(
            Statement("Fabrikam resent the 5 November batch after our acknowledgement failed, so it was settled twice.", ["fact:payment-sets:fabrikam-pay:2026-11-05", "event:EV-1106-01"], ["S1"]),
            ["fact:payment-sets:fabrikam-pay:2026-11-05", "payout:PO-3310-FAB-1105-R"],
            ["S1"],
            summaryEvidence: "fact:payment-sets:fabrikam-pay:2026-11-05");
        var model = Model(
            Call(ToolNames.SettlementBatches, new { merchant_id = "M-3310", start_date = "2026-11-02", end_date = "2026-11-06" }),
            Call(ToolNames.ErrorEvents, new { processor_id = Fabrikam, start_date = "2026-11-05", end_date = "2026-11-06" }),
            Call(ToolNames.KnowledgeSearch, new { query = "duplicate-settlement-batch" }),
            Call(Submit, submission));

        var assessed = Assert.IsType<AssessmentResult.Assessed>(await RunAsync(model));
        var batches = Assert.Single(ToolResults(model.Requests[^1].Messages), r => r.Contains("\"merchant_id\":\"M-3310\"", StringComparison.Ordinal));

        Assert.Equal(RootCauseLabel.Assessment, assessed.RootCause!.Label);
        Assert.Contains("\"relation\":\"identical\"", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate", batches, StringComparison.OrdinalIgnoreCase);
    }
}
