using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>
/// Which knowledge a user may retrieve, derived only from their server-established identity. The
/// Search filter is built from this inside Knowledge; nothing a user types can reach it.
/// </summary>
public sealed class KnowledgeAccess
{
    private static readonly KnowledgeAccess GeneralOnly = new(includesRestricted: false);
    private static readonly KnowledgeAccess GeneralAndRestricted = new(includesRestricted: true);

    private KnowledgeAccess(bool includesRestricted)
    {
        IncludesRestricted = includesRestricted;
        Predicate = includesRestricted
            ? $"(classification eq '{KnowledgeClassification.General}' or classification eq '{KnowledgeClassification.Restricted}')"
            : $"classification eq '{KnowledgeClassification.General}'";
    }

    public bool IncludesRestricted { get; }

    /// <summary>The OData classification predicate; never leaves this assembly.</summary>
    internal string Predicate { get; }

    public static KnowledgeAccess From(IUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.IsAuthenticated)
        {
            throw new KnowledgeAccessDeniedException();
        }

        return user.CanAccessRestrictedKnowledge ? GeneralAndRestricted : GeneralOnly;
    }
}
