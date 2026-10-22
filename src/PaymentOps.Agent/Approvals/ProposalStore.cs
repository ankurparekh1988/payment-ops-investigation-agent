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
                Purge();
                return _entries.Values.Count(e => e.State == ProposalState.Pending);
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
            Purge();
            var superseded = _entries.Values.FirstOrDefault(e => e.State == ProposalState.Pending && e.Proposal.ConversationId == conversationId);
            var pending = _entries.Values.Count(e => e.State == ProposalState.Pending) - (superseded is null ? 0 : 1);
            if (pending >= _options.MaxPendingProposals)
            {
                return null;
            }

            if (superseded is not null)
            {
                _entries[superseded.Proposal.Id] = superseded with { State = ProposalState.Superseded };
            }

            var now = time.GetUtcNow();
            var id = Guid.NewGuid();
            var shown = draft with { ProposalId = id, Status = ProposalStatus.Pending, ExpiresAt = now + _options.ProposalLifetime };
            var proposal = new Proposal(id, investigationId, conversationId, ownerId, now, now + _options.ProposalLifetime, _model, shown);
            _entries[id] = new ProposalEntry(proposal, ProposalState.Pending, null, null, null);
            _byInvestigation[investigationId] = id;
            return shown;
        }
    }

    public ProposalEntry? Find(Guid id)
    {
        lock (_lock)
        {
            Purge();
            return _entries.GetValueOrDefault(id);
        }
    }

    /// <summary>Pending and unexpired becomes executing; anything else is left as it is.</summary>
    public bool TryBeginExecute(Guid id) => TryMove(id, ProposalState.Pending, e => e with { State = ProposalState.Executing });

    public void Complete(Guid id, string ticketId) => TryMove(id, ProposalState.Executing, e => e with { State = ProposalState.Executed, TicketId = ticketId });

    /// <summary>The ticket store failed: back to pending, so the same key can be tried again.</summary>
    public void Revert(Guid id) => TryMove(id, ProposalState.Executing, e => e with { State = ProposalState.Pending });

    public bool TryReject(Guid id, RejectionReason reason, string? note) =>
        TryMove(id, ProposalState.Pending, e => e with { State = ProposalState.Rejected, Reason = reason, Note = note });

    /// <summary>What the agent is told about this investigation's proposal in a later turn, if anything.</summary>
    public string? NoteFor(Guid investigationId)
    {
        lock (_lock)
        {
            Purge();
            if (!_byInvestigation.TryGetValue(investigationId, out var id) || !_entries.TryGetValue(id, out var entry))
            {
                return null;
            }

            return entry.State switch
            {
                ProposalState.Executed => $"Ticket {entry.TicketId} was created.",
                ProposalState.Rejected => $"The proposed ticket was rejected: {Words(entry.Reason!.Value)}.",
                ProposalState.Expired => "The proposed ticket expired unapproved.",
                ProposalState.Superseded => "The proposed ticket was replaced by a later one.",
                _ => "The proposed ticket is awaiting an Operations Engineer's decision.",
            };
        }
    }

    private bool TryMove(Guid id, ProposalState from, Func<ProposalEntry, ProposalEntry> move)
    {
        lock (_lock)
        {
            Purge();
            if (!_entries.TryGetValue(id, out var entry) || entry.State != from)
            {
                return false;
            }

            _entries[id] = move(entry);
            return true;
        }
    }

    // Under the lock. Pending proposals past their expiry stop counting; decided ones are kept one more lifetime
    // so a late click or the next turn still learns the outcome, then they go.
    private void Purge()
    {
        var now = time.GetUtcNow();
        foreach (var entry in _entries.Values.ToList())
        {
            if (entry.State == ProposalState.Pending && now >= entry.Proposal.ExpiresAt)
            {
                _entries[entry.Proposal.Id] = entry with { State = ProposalState.Expired };
            }
            else if (entry.State is not (ProposalState.Pending or ProposalState.Executing) && now >= entry.Proposal.ExpiresAt + _options.ProposalLifetime)
            {
                _entries.Remove(entry.Proposal.Id);
                _byInvestigation.Remove(entry.Proposal.InvestigationId);
            }
        }
    }

    private static string Words(RejectionReason reason) => reason switch
    {
        RejectionReason.EvidenceDoesNotSupportIt => "the evidence doesn't support it",
        RejectionReason.WrongMerchantsOrWindow => "wrong merchants or dates",
        RejectionReason.NotAnIncident => "not an incident",
        RejectionReason.AlreadyRaised => "already raised",
        _ => "other",
    };

    private static string Model(AiRelease release)
    {
        var prompt = release.Prompts[InvestigationAgentRunner.PromptId];
        var model = release.Deployments[release.ActiveChatDeployment];
        return $"{prompt.Id} {prompt.Version}, {model.Model} {model.Version}";
    }
}
