using Azure;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// Proves the evaluation publisher can't write the live index before it writes anything: it deletes a
/// key that doesn't exist, which changes nothing if allowed, and must be refused with 403.
/// </summary>
public static class PublisherScope
{
    public const string ProbeKey = "publisher-scope-probe";

    public static async Task RequireNoWriteAsync(IKnowledgeIndexClient liveIndex, string liveIndexName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(liveIndex);
        try
        {
            await liveIndex.DeleteAsync([ProbeKey], cancellationToken);
        }
        catch (KnowledgePublishException ex) when (ex.InnerException is RequestFailedException { Status: 403 })
        {
            return;
        }

        throw new PublisherScopeException(liveIndexName);
    }
}

/// <summary>The publisher could write the live index. Kept apart from publish failures, so CI reports it as a scope violation.</summary>
public sealed class PublisherScopeException(string liveIndexName)
    : Exception($"The publisher can write the live index '{liveIndexName}'; its role must be scoped to the evaluation index only.");
