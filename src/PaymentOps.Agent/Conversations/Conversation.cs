using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Conversations;

internal sealed record ConversationTurn(string Question, AssessmentResult Result);

/// <summary>One person's questions in one circuit. Held in memory only: a refresh or a new investigation starts over.</summary>
internal sealed class Conversation
{
    private readonly List<ConversationTurn> _turns = [];

    public Guid Id { get; private set; } = Guid.NewGuid();

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public void Add(ConversationTurn turn) => _turns.Add(turn);

    // A new id too, so nothing keyed to the old conversation applies to the new one.
    public void Reset()
    {
        _turns.Clear();
        Id = Guid.NewGuid();
    }
}
