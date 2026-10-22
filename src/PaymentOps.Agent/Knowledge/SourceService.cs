using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Knowledge;

/// <summary>A cited section, read again with the current user's access, under the same timeout as a knowledge tool call.</summary>
internal sealed class SourceService(IKnowledgeLookup knowledge, IUserContext user, IOptions<InvestigationLimits> limits, ILogger<SourceService> logger)
{
    public async Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken)
    {
        // Same answer as a missing section: no other authorization state is revealed.
        if (Access() is not { } access)
        {
            return new SourceResult.NotFound();
        }

        using var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        searchTimeout.CancelAfter(limits.Value.ToolTimeout);
        try
        {
            var section = await knowledge.FindAsync(sectionKey, access, searchTimeout.Token);
            return section is not null ? new SourceResult.Found(section.Title, section.SectionPath, section.Content) : new SourceResult.NotFound();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            SourceLog.SearchFailed(logger, "timeout", null, null, null, ex);
        }
        catch (KnowledgeUnavailableException ex)
        {
            SourceLog.SearchFailed(logger, ex.IsAuthenticationFailure ? "authentication" : FailureClass(ex.Status), ex.Status, ex.ErrorCode, ex.RequestId, ex);
        }

        return new SourceResult.Unavailable();
    }

    private static string FailureClass(int? status) => status switch
    {
        401 => "authentication",
        403 => "authorization",
        429 => "throttling",
        >= 500 => "server error",
        null => "network",
        _ => "request",
    };

    private KnowledgeAccess? Access()
    {
        try
        {
            return KnowledgeAccess.From(user);
        }
        catch (KnowledgeAccessDeniedException)
        {
            return null;
        }
    }
}
