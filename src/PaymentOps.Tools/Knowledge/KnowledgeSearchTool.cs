using Microsoft.Extensions.Options;

using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Tools.Knowledge;

public sealed record KnowledgeSearchRequest(string? Query);

/// <summary>
/// The sections found, for the agent to number. Citation ids belong to the turn, so the tool doesn't assign
/// them; the agent shows the content to the model only after registering each section.
/// </summary>
public sealed record KnowledgeSearchResult(IReadOnlyList<RetrievedSection> Sections);

/// <summary>Runbooks, operating guides and policies the user may read, as the release's retrieval finds them.</summary>
public sealed class KnowledgeSearchTool(IKnowledgeLookup knowledge, IOptions<ToolOptions> options) : InvestigationTool<KnowledgeSearchRequest>(options)
{
    public override string Name => ToolNames.KnowledgeSearch;

    public override string Description =>
        "Searches runbooks, processor operating guides and policies for what explains a finding or says what to do about it. " +
        "Facts about payments, files, payouts and incidents come from the other tools, not from here.";

    public override string Source => DataSources.Knowledge;

    protected override async Task<ToolResult> RunCoreAsync(KnowledgeSearchRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        var query = request.Query?.Trim() ?? string.Empty;
        if (query.Length == 0 || query.Length > Options.MaxQueryLength)
        {
            return new ToolResult.Failed(ToolErrorCode.InvalidArgument, $"query must be 1 to {Options.MaxQueryLength} characters.");
        }

        try
        {
            // Filtered by the user's own access inside Knowledge; nothing in the query can widen it.
            var sections = await knowledge.RetrieveAsync(query, KnowledgeAccess.From(user), cancellationToken);
            return new ToolResult.Ok(new KnowledgeSearchResult(sections), [], []);
        }
        catch (KnowledgeUnavailableException)
        {
            return new ToolResult.Failed(ToolErrorCode.Unavailable, $"{DataSources.Knowledge} is unavailable.", DataSources.Knowledge);
        }
    }
}
