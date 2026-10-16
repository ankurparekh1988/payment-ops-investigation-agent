namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// Makes the index hold exactly the desired sections: tighten, confirm, upload, delete, then read
/// every document back and compare every field. Search is near-real-time, so the two checks retry.
/// </summary>
/// <param name="delay">Waits between checks; tests pass one that doesn't wait.</param>
public sealed class KnowledgeIndexPublisher(IKnowledgeIndexClient index, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int Attempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<PublishPlan> PublishAsync(IReadOnlyList<KnowledgeSearchDocument> desired, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Count == 0)
        {
            throw new KnowledgePublishException("There are no sections to publish; refusing to empty the index.");
        }

        await index.EnsureIndexAsync(cancellationToken);
        var plan = PublishPlan.Create(desired, await index.ReadAllAsync(cancellationToken));

        // An empty plan means that read already matched every field, so there's nothing to verify again.
        if (plan.IsEmpty)
        {
            return plan;
        }

        if (plan.Tighten.Count > 0)
        {
            var keys = plan.Tighten.Select(d => d.Id).ToList();
            await index.DeleteAsync(keys, cancellationToken);
            var remaining = await RetryUntilAsync(() => index.FindExistingAsync(keys, cancellationToken), found => found.Count == 0, cancellationToken);
            if (remaining.Count > 0)
            {
                var names = plan.Tighten.Where(d => remaining.Contains(d.Id)).Select(d => d.Describe());
                throw new KnowledgePublishException($"Sections becoming restricted are still retrievable after their delete, so nothing was uploaded: {string.Join("; ", names)}.");
            }
        }

        if (plan.Upload.Count > 0)
        {
            await index.UploadAsync(plan.Upload, cancellationToken);
        }

        if (plan.Delete.Count > 0)
        {
            await index.DeleteAsync([.. plan.Delete.Select(d => d.Id)], cancellationToken);
        }

        var differences = await RetryUntilAsync(async () => Differences(desired, await index.ReadAllAsync(cancellationToken)), d => d.Count == 0, cancellationToken);
        if (differences.Count > 0)
        {
            throw new KnowledgePublishException($"The index doesn't match the corpus after publishing: {string.Join("; ", differences)}.");
        }

        return plan;
    }

    // Keys and section paths only; never the text.
    private static List<string> Differences(IReadOnlyList<KnowledgeSearchDocument> desired, IReadOnlyList<KnowledgeSearchDocument> stored)
    {
        var actual = stored.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var wanted = desired.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. desired.Where(d => !actual.ContainsKey(d.Id)).Select(d => $"missing {d.Describe()}"),
            .. desired.Where(d => actual.TryGetValue(d.Id, out var a) && a != d).Select(d => $"differs {d.Describe()}"),
            .. stored.Where(d => !wanted.Contains(d.Id)).Select(d => $"unexpected {d.Describe()}"),
        ];
    }

    private async Task<T> RetryUntilAsync<T>(Func<Task<T>> check, Func<T, bool> done, CancellationToken cancellationToken)
    {
        var result = await check();
        for (var attempt = 1; attempt < Attempts && !done(result); attempt++)
        {
            await _delay(RetryDelay, cancellationToken);
            result = await check();
        }

        return result;
    }
}
