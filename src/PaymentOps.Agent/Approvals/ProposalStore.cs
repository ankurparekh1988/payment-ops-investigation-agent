using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Approvals;

/// <summary>
/// Pending and recently decided proposals, in process memory: lost on restart and not shared between instances.
/// Every state change happens under one lock, so a proposal is executed or rejected at most once.
/// </summary>
internal sealed class ProposalStore(TimeProvider time, IOptions<ApprovalOptions> options, AiRelease release)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ProposalEntry> _entries = [];
    private readonly Dictionary<Guid, Guid> _byInvestigation = [];
    private readonly ApprovalOptions _options = options.Value;

    private readonly string _model = Model(release);

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                var now = time.GetUtcNow();
                return _entries.Values.Count(e => State(e, now) == ProposalState.Pending);
            }
        }
    }

    /// <summary>
    /// Keeps the draft as a pending proposal and returns it as the owner sees it, or null when the store is full.
    /// The conversation's earlier pending proposal is superseded, so an old card can't approve.
    /// </summary>
    public TicketProposal? Open(TicketProposal draft, Guid investigationId, Guid conversationId, string ownerId)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            Purge(now);
            var pending = _entries.Values.Where(e => State(e, now) == ProposalState.Pending).ToList();
            var superseded = pending.FirstOrDefault(e => e.Proposal.ConversationId == conversationId);
            if (pending.Count - (superseded is null ? 0 : 1) >= _options.MaxPendingProposals)
            {
                return null;
            }

            if (superseded is not null)
            {
                _entries[superseded.Proposal.Id] = superseded with { State = ProposalState.Superseded };
            }

            var id = Guid.NewGuid();
            var shown = draft with { ProposalId = id, Status = ProposalStatus.Pending, ExpiresAt = now + _options.ProposalLifetime };
            _entries[id] = new ProposalEntry(new Proposal(id, investigationId, conversationId, ownerId, now, now + _options.ProposalLifetime, _model, shown), ProposalState.Pending, null, null, null);
            _byInvestigation[investigationId] = id;
            return shown;
        }
    }

    /// <summary>
    /// Moves the owner's proposal from <paramref name="from"/> in one step. On failure, <paramref name="entry"/> is where
    /// it stands now, or null when there's no such proposal for this owner, so nothing about anyone else's leaks.
    /// </summary>
    public bool TryMove(Guid id, string ownerId, ProposalState from, Func<ProposalEntry, ProposalEntry> move, out ProposalEntry? entry)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var stored) || stored.Proposal.OwnerId != ownerId)
            {
                entry = null;
                return false;
            }

            entry = stored with { State = State(stored, time.GetUtcNow()) };
            if (entry.State != from)
            {
                return false;
            }

            _entries[id] = entry = move(entry);
            return true;
        }
    }

    /// <summary>What the agent is told about this investigation's proposal in a later turn, if anything.</summary>
    public string? NoteFor(Guid investigationId)
    {
        lock (_lock)
        {
            if (!_byInvestigation.TryGetValue(investigationId, out var id) || !_entries.TryGetValue(id, out var entry))
            {
                return null;
            }

            return State(entry, time.GetUtcNow()) switch
            {
                ProposalState.Executed => $"Ticket {entry.TicketId} was created.",
                ProposalState.Rejected => $"The proposed ticket was rejected: {RejectionReasons.Describe(entry.Reason!.Value)}.",
                ProposalState.Expired => "The proposed ticket expired unapproved.",
                ProposalState.Superseded => "The proposed ticket was replaced by a later one.",
                _ => "The proposed ticket is awaiting an Operations Engineer's decision.",
            };
        }
    }

    // Expiry is judged when read, so a pending proposal stops being approvable the moment its time is up.
    private static ProposalState State(ProposalEntry entry, DateTimeOffset now) =>
        entry.State == ProposalState.Pending && now >= entry.Proposal.ExpiresAt ? ProposalState.Expired : entry.State;

    // Under the lock, on each new proposal. Settled entries are kept one more lifetime past expiry, so a late click
    // or the next turn still learns the outcome, then they go.
    private void Purge(DateTimeOffset now)
    {
        foreach (var entry in _entries.Values.Where(e => e.State != ProposalState.Executing && now >= e.Proposal.ExpiresAt + _options.ProposalLifetime).ToList())
        {
            _entries.Remove(entry.Proposal.Id);
            _byInvestigation.Remove(entry.Proposal.InvestigationId);
        }
    }

    private static string Model(AiRelease release)
    {
        var prompt = release.Prompts[InvestigationAgentRunner.PromptId];
        var model = release.Deployments[release.ActiveChatDeployment];
        return $"{prompt.Id} {prompt.Version}, {model.Model} {model.Version}";
    }
}
