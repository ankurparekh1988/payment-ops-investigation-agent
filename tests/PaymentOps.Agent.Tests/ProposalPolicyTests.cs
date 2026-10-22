using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

public sealed class ProposalPolicyTests
{
    private const string MissingFile = "fact:missing-file:fabrikam-pay:2026-11-09";
    private const string FabIncident = "incident:INC-FAB-1109";

    private static TurnState Turn()
    {
        var turn = new TurnState();
        turn.Register([
            new RecordEvidence("merchant:M-1042", "Merchant M-1042"),
            new RecordEvidence("processor:fabrikam-pay", "Fabrikam Pay"),
            new DerivedEvidence(MissingFile, "No Fabrikam Pay file for 2026-11-09", ["processor:fabrikam-pay"]),
            new IncidentEvidence(FabIncident, "Incident INC-FAB-1109", "fabrikam-pay", "FAB-7731"),
            new IncidentEvidence("incident:INC-FAB-1110", "Incident INC-FAB-1110", "fabrikam-pay", "FAB-8000"),
            new IncidentEvidence("incident:INC-FAB-1111", "Incident INC-FAB-1111", "fabrikam-pay", "FAB-7731"),
            new IncidentEvidence("incident:INC-NW-1", "Incident INC-NW-1", "northwind-acquiring", "NW-1"),
        ]);
        turn.RegisterSection(new RetrievedSection("k1", "d", "Payout delay", "Payout delay > The batch didn't close", "text", "General", null));
        turn.StructuredOutcome(answered: true);
        return turn;
    }

    private static ProposedTicket Ticket(params string[] evidence) => new(
        "fabrikam-pay", ["M-1042"], "2026-11-09", "2026-11-09", "Fabrikam Pay settlement file missing for 9 November",
        "No settlement file arrived from Fabrikam Pay for the 9 November batch.", evidence.Length > 0 ? evidence : [MissingFile], ["S1"]);

    private static AssessmentSubmission With(ProposedTicket? ticket) => new(
        "assessed",
        new SubmittedStatement("The 9 November file never arrived.", [MissingFile], ["S1"]),
        [new SubmittedStatement("The runbook says to raise it.", [MissingFile], ["S1"])],
        new SubmittedStatement(string.Empty, [], []),
        "medium",
        [],
        ticket);

    [Fact]
    public void One_cited_reference_is_carried() =>
        Assert.Equal("FAB-7731", AssessmentPolicy.Reference(Ticket(MissingFile, FabIncident), Turn()));

    // Two incidents with the same reference are still one reference.
    [Fact]
    public void The_same_reference_twice_is_one_reference() =>
        Assert.Equal("FAB-7731", AssessmentPolicy.Reference(Ticket(MissingFile, FabIncident, "incident:INC-FAB-1111"), Turn()));

    [Fact]
    public void No_cited_incident_means_no_reference() =>
        Assert.Null(AssessmentPolicy.Reference(Ticket(MissingFile), Turn()));

    [Fact]
    public void Two_different_references_mean_none() =>
        Assert.Null(AssessmentPolicy.Reference(Ticket(MissingFile, FabIncident, "incident:INC-FAB-1110"), Turn()));

    [Fact]
    public void An_incident_of_another_processor_is_ignored() =>
        Assert.Equal("FAB-7731", AssessmentPolicy.Reference(Ticket(MissingFile, FabIncident, "incident:INC-NW-1"), Turn()));

    [Fact]
    public void The_reference_is_never_read_from_model_text()
    {
        var ticket = Ticket(MissingFile) with { Summary = "The processor's reference is FAB-9999." };

        Assert.Null(AssessmentPolicy.Reference(ticket, Turn()));
    }

    [Fact]
    public void A_valid_proposal_appears_as_a_draft()
    {
        var assessed = AssessmentPolicy.Apply(With(Ticket(MissingFile, FabIncident)), Turn());

        var proposal = assessed.Proposal!;
        Assert.Equal(ProposalStatus.Draft, proposal.Status);
        Assert.Null(proposal.ProposalId);
        Assert.Null(proposal.ExpiresAt);
        Assert.Equal("fabrikam-pay", proposal.ProcessorId);
        Assert.Equal(["M-1042"], proposal.MerchantIds);
        Assert.Equal(new DateOnly(2026, 11, 9), proposal.StartDate);
        Assert.Equal(new EvidenceReference(MissingFile, "No Fabrikam Pay file for 2026-11-09", true), proposal.Evidence[0]);
        Assert.Equal("Payout delay", Assert.Single(proposal.Citations).Title);
        Assert.Equal("FAB-7731", proposal.ProcessorReference);
        Assert.False(assessed.ProposalWithheld);
    }

    [Fact]
    public void No_proposal_without_a_ticket()
    {
        var assessed = AssessmentPolicy.Apply(With(null), Turn());

        Assert.Null(assessed.Proposal);
        Assert.False(assessed.ProposalWithheld);
    }

    [Fact]
    public void A_forced_incomplete_withholds_the_proposal()
    {
        var turn = Turn();
        turn.Stop();

        var assessed = AssessmentPolicy.Apply(With(Ticket()), turn);

        Assert.Equal(AssessmentOutcome.Incomplete, assessed.Outcome);
        Assert.Null(assessed.Proposal);
        Assert.True(assessed.ProposalWithheld);
    }
}
