using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

public sealed class AssessmentTests
{
    private readonly TurnState _turn = Turn();

    private static TurnState Turn()
    {
        var turn = new TurnState();
        turn.Register([
            new RecordEvidence("payment:PAY-1", "Payment PAY-1"),
            new RecordEvidence("processor:p", "Processor"),
            new RecordEvidence("event:EV-1", "Error event EV-1"),
            new RecordEvidence("event:EV-2", "Error event EV-2"),
            new RecordEvidence("event:EV-3", "Error event EV-3"),
            new DerivedEvidence("fact:missing-file:p:2026-11-09", "No accepted file", ["payment:PAY-1", "processor:p"]),
            new DerivedEvidence("fact:event-group:p:*:file_transfer_timeout:T:2026-11-09:2026-11-10", "3 timeouts", ["event:EV-1", "event:EV-2", "event:EV-3"]),
        ]);
        turn.RegisterSection(new RetrievedSection("k1", "d", "Outage runbook", "Runbook > Outage", "text", "General", null));
        turn.StructuredOutcome(answered: true);
        return turn;
    }

    private static SubmittedStatement Said(string text, string[]? evidence = null, string[]? citations = null) => new(text, evidence ?? [], citations ?? []);

    private static AssessmentSubmission Valid() => new(
        "assessed",
        Said("Fabrikam's 9 November file never arrived.", ["fact:missing-file:p:2026-11-09"], ["S1"]),
        [Said("No file arrived for the batch, which the runbook treats as the processor's to resend.", ["fact:missing-file:p:2026-11-09"], ["S1"])],
        Said("A processor outage stopped the file.", ["fact:missing-file:p:2026-11-09"], ["S1"]),
        "high",
        [new SubmittedNextStep("Raise an incident with Fabrikam.", ["S1"])]);

    [Fact]
    public void A_fully_supported_assessment_is_valid() =>
        Assert.Empty(AssessmentValidator.Check(Valid(), _turn));

    [Fact]
    public void An_evidence_id_no_tool_returned_is_rejected_by_rule_without_echoing_text()
    {
        var submission = Valid() with { Claims = [Said("Paid twice.", ["payout:PO-404"])] };

        var failure = Assert.Single(AssessmentValidator.Check(submission, _turn));
        Assert.Equal("claims[0].evidence_ids: 'payout:PO-404' wasn't returned by a tool in this investigation.", failure);
    }

    [Fact]
    public void A_citation_not_retrieved_in_this_turn_is_rejected() =>
        Assert.Contains("summary.citation_ids: 'S9' wasn't retrieved in this investigation.", AssessmentValidator.Check(Valid() with { Summary = Said("x", citations: ["S9"]) }, _turn));

    [Fact]
    public void A_summary_with_no_support_is_rejected() =>
        Assert.Contains("summary: needs at least one evidence id or citation id.", AssessmentValidator.Check(Valid() with { Summary = Said("All good.") }, _turn));

    [Fact]
    public void An_assessed_outcome_needs_a_claim() =>
        Assert.Contains("claims: an assessed outcome needs at least one claim.", AssessmentValidator.Check(Valid() with { Claims = [] }, _turn));

    [Theory]
    [InlineData("claims")]
    [InlineData("root_cause")]
    [InlineData("next_steps")]
    public void A_refusal_carries_no_claims_root_cause_or_next_steps(string field)
    {
        var submission = field switch
        {
            "claims" => Refusal() with { Claims = [Said("y", ["payment:PAY-1"])] },
            "root_cause" => Refusal() with { RootCause = Said("Outage.", ["payment:PAY-1"], ["S1"]) },
            _ => Refusal() with { NextSteps = [new SubmittedNextStep("Raise an incident.", ["S1"])] },
        };

        Assert.Contains("outcome: a refusal carries no claims, no root cause and no next steps.", AssessmentValidator.Check(submission, _turn));
    }

    private static AssessmentSubmission Refusal() => new("refused", Said("I can't refund payments."), [], Said(string.Empty), "high", []);

    // Nothing a refusal says is shown, so it needs no evidence: an out-of-scope question has none to give.
    [Fact]
    public void A_refusal_needs_no_evidence_and_shows_only_the_server_notice()
    {
        var submission = Refusal();

        Assert.Empty(AssessmentValidator.Check(submission, new TurnState()));
        var result = AssessmentPolicy.Apply(submission, new TurnState());

        Assert.Equal(AssessmentOutcome.Refused, result.Outcome);
        Assert.Equal(AssessmentPolicy.RefusalNotice, result.Summary.Text);
        Assert.Empty(result.Summary.Evidence);
        Assert.Empty(result.Summary.Citations);
        Assert.Empty(result.Claims);
        Assert.Null(result.RootCause);
        Assert.Empty(result.NextSteps);
        Assert.Equal(Confidence.Low, result.Confidence);
    }

    // A refusal states nothing about the data, so failed sources don't turn it into an incomplete assessment.
    [Fact]
    public void A_refusal_stays_a_refusal_when_every_data_source_failed()
    {
        var turn = new TurnState();
        turn.StructuredOutcome(answered: false);
        turn.AddGap(ObservedGap.Unavailable("Settlement data"));

        var result = AssessmentPolicy.Apply(Refusal(), turn);

        Assert.Equal(AssessmentOutcome.Refused, result.Outcome);
        Assert.Equal(AssessmentPolicy.RefusalNotice, result.Summary.Text);
    }

    [Theory]
    [InlineData("The file is missing [S1].")]
    [InlineData("See payout:PO-1 for details.")]
    [InlineData("See fact:missing-file:p:2026-11-09.")]
    public void Ids_written_into_text_are_rejected(string text) =>
        Assert.Contains(AssessmentValidator.Check(Valid() with { Claims = [Said(text, ["payment:PAY-1"])] }, _turn), f => f.StartsWith("claims[0].text:", StringComparison.Ordinal));

    [Fact]
    public void Blank_text_is_rejected() =>
        Assert.Contains("next_steps[0].text: can't be blank.", AssessmentValidator.Check(Valid() with { NextSteps = [new SubmittedNextStep(" ", [])] }, _turn));

    // A count is supported by the group that counted every event, not by the few event ids shown.
    [Fact]
    public void Citing_part_of_a_counted_group_needs_the_group_itself()
    {
        const string Group = "fact:event-group:p:*:file_transfer_timeout:T:2026-11-09:2026-11-10";
        var partOnly = Valid() with { Claims = [Said("There were 3 file transfer timeouts.", ["event:EV-1", "event:EV-2"])] };
        var withGroup = Valid() with { Claims = [Said("There were 3 file transfer timeouts.", ["event:EV-1", Group])] };
        var whole = Valid() with { Claims = [Said("Timeouts were recorded.", ["event:EV-1", "event:EV-2", "event:EV-3"])] };

        Assert.Contains(AssessmentValidator.Check(partOnly, _turn), f => f.Contains(Group, StringComparison.Ordinal));
        Assert.Empty(AssessmentValidator.Check(withGroup, _turn));
        Assert.Empty(AssessmentValidator.Check(whole, _turn));
    }

    [Theory]
    [InlineData("The file is missing [s1].")]
    [InlineData("The file is missing [S1, S2].")]
    public void Near_miss_citation_markers_in_text_are_rejected(string text) =>
        Assert.Contains(AssessmentValidator.Check(Valid() with { Claims = [Said(text, ["payment:PAY-1"])] }, _turn), f => f.StartsWith("claims[0].text:", StringComparison.Ordinal));

    // The wire schema is strict, but the server doesn't rely on it: anything else is rejected, never parsed.
    [Theory]
    [InlineData("Assessed", "high")]
    [InlineData("7", "high")]
    [InlineData("assessed", "very_high")]
    [InlineData("assessed", "High")]
    public void Outcome_and_confidence_must_be_exact_allowed_values(string outcome, string confidence) =>
        Assert.NotEmpty(AssessmentValidator.Check(Valid() with { Outcome = outcome, Confidence = confidence }, _turn));

    // The same events counted over two ranges are two groups; citing either one supports the count.
    [Fact]
    public void Citing_one_of_several_groups_holding_the_events_is_enough()
    {
        _turn.Register([new DerivedEvidence("fact:event-group:p:*:file_transfer_timeout:T:2026-11-08:2026-11-10", "4 timeouts", ["event:EV-0", "event:EV-1", "event:EV-2", "event:EV-3"])]);
        var claim = Valid() with { Claims = [Said("There were 3 file transfer timeouts.", ["event:EV-1", "fact:event-group:p:*:file_transfer_timeout:T:2026-11-09:2026-11-10"])] };

        Assert.Empty(AssessmentValidator.Check(claim, _turn));
    }

    [Fact]
    public void A_stopped_investigation_is_incomplete_and_low_whatever_outcome_was_submitted()
    {
        _turn.Stop();

        var result = AssessmentPolicy.Apply(Valid() with { Outcome = "escalated" }, _turn);

        Assert.Equal(AssessmentOutcome.Incomplete, result.Outcome);
        Assert.Equal(Confidence.Low, result.Confidence);
    }

    [Fact]
    public void A_root_cause_with_data_and_knowledge_is_an_assessment()
    {
        var result = AssessmentPolicy.Apply(Valid(), _turn);

        Assert.Equal(RootCauseLabel.Assessment, result.RootCause!.Label);
        Assert.Equal(Confidence.High, result.Confidence);
        Assert.Equal(AssessmentOutcome.Assessed, result.Outcome);
        Assert.True(Assert.Single(result.RootCause.Statement.Evidence).IsDerived);
        Assert.Equal("Outage runbook", Assert.Single(result.RootCause.Statement.Citations).Title);
    }

    [Theory]
    [InlineData(new string[0], new[] { "S1" })]
    [InlineData(new[] { "payment:PAY-1" }, new string[0])]
    public void A_root_cause_missing_either_kind_of_support_is_a_hypothesis(string[] evidence, string[] citations) =>
        Assert.Equal(RootCauseLabel.Hypothesis, AssessmentPolicy.Apply(Valid() with { RootCause = Said("Outage.", evidence, citations) }, _turn).RootCause!.Label);

    [Fact]
    public void No_root_cause_text_means_none() =>
        Assert.Null(AssessmentPolicy.Apply(Valid() with { RootCause = Said(string.Empty) }, _turn).RootCause);

    // The model can't keep high confidence by leaving a failure out: the server saw it.
    [Fact]
    public void An_observed_gap_caps_confidence_at_medium_and_is_shown_in_fixed_words()
    {
        _turn.AddGap(ObservedGap.Timeout("Processor data"));

        var result = AssessmentPolicy.Apply(Valid(), _turn);

        Assert.Equal(Confidence.Medium, result.Confidence);
        Assert.Equal(["Processor data didn't respond in time."], result.Gaps);
    }

    // Each statement is judged on its own: a data-only claim and a knowledge-only claim don't add up to High.
    [Fact]
    public void A_data_only_claim_beside_a_knowledge_only_claim_caps_confidence_at_medium()
    {
        var mixed = Valid() with { Claims = [Said("No file arrived.", ["fact:missing-file:p:2026-11-09"]), Said("The runbook says to raise it.", citations: ["S1"])] };

        Assert.Equal(Confidence.Medium, AssessmentPolicy.Apply(mixed, _turn).Confidence);
    }

    [Fact]
    public void A_summary_resting_on_one_kind_of_evidence_caps_confidence_at_medium() =>
        Assert.Equal(Confidence.Medium, AssessmentPolicy.Apply(Valid() with { Summary = Said("x", ["payment:PAY-1"]) }, _turn).Confidence);

    [Fact]
    public void A_hypothesis_root_cause_caps_confidence_at_medium()
    {
        var result = AssessmentPolicy.Apply(Valid() with { RootCause = Said("Outage.", citations: ["S1"]) }, _turn);

        Assert.Equal(RootCauseLabel.Hypothesis, result.RootCause!.Label);
        Assert.Equal(Confidence.Medium, result.Confidence);
    }

    [Fact]
    public void Claims_resting_on_one_kind_of_evidence_cap_confidence_at_medium()
    {
        var dataOnly = Valid() with { Summary = Said("x", ["payment:PAY-1"]), Claims = [Said("y", ["payment:PAY-1"])], RootCause = Said(string.Empty), NextSteps = [] };

        Assert.Equal(Confidence.Medium, AssessmentPolicy.Apply(dataOnly, _turn).Confidence);
    }

    [Fact]
    public void A_turn_stopped_by_a_limit_is_incomplete_and_low()
    {
        _turn.Stop();

        var result = AssessmentPolicy.Apply(Valid(), _turn);

        Assert.Equal(AssessmentOutcome.Incomplete, result.Outcome);
        Assert.Equal(Confidence.Low, result.Confidence);
    }

    [Fact]
    public void When_every_structured_source_failed_the_outcome_is_incomplete_with_no_root_cause()
    {
        var turn = new TurnState();
        turn.Register([new RecordEvidence("payment:PAY-1", "Payment")]);
        turn.RegisterSection(new RetrievedSection("k1", "d", "Runbook", "R", "t", "General", null));
        turn.StructuredOutcome(answered: false);
        turn.AddGap(ObservedGap.Unavailable("Settlement data"));

        var result = AssessmentPolicy.Apply(Valid() with { Summary = Said("x", citations: ["S1"]), Claims = [Said("y", citations: ["S1"])], RootCause = Said("Outage.", citations: ["S1"]) }, turn);

        Assert.Equal(AssessmentOutcome.Incomplete, result.Outcome);
        Assert.Null(result.RootCause);
        Assert.Equal(Confidence.Low, result.Confidence);
    }
}
