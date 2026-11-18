using System.Security.Cryptography;
using System.Text.Json;

using PaymentOps.Evaluation.Commands;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Reporting;

/// <summary>
/// What was checked out when the report was made. Two reports are the same source when their trees match: a pull
/// request's evaluation runs on a merge commit and a later measurement on the squash merge, which share a tree when
/// the branch was up to date. Each keeps its own commit for audit.
/// </summary>
/// <param name="Dirty">Tracked files had uncommitted changes, so the tree isn't what ran.</param>
internal sealed record ReportSource(string Commit, string? Tree, bool Dirty)
{
    public static ReportSource Current()
    {
        var commit = RepositoryCommit.Current();
        var head = commit.EndsWith(RepositoryCommit.DirtySuffix, StringComparison.Ordinal) ? commit[..^RepositoryCommit.DirtySuffix.Length] : commit;
        return new ReportSource(head, RepositoryCommit.Git($"rev-parse {head}^{{tree}}"), RepositoryCommit.Git("status --porcelain --untracked-files=no") is not "");
    }
}

/// <summary>A deployment as the report names it: its role's deployment and what calling it gets.</summary>
internal sealed record ModelIdentity(string Deployment, string Model, string Version, string DeploymentType, string? ContentFilterPolicy)
{
    public static ModelIdentity? From(AiRelease release, string? deployment) =>
        deployment is not null && release.Deployments.TryGetValue(deployment, out var d)
            ? new ModelIdentity(deployment, d.Model, d.Version, d.DeploymentType, d.ContentFilterPolicy)
            : null;
}

/// <summary>A prompt the release embeds, by version and the hash of its body.</summary>
internal sealed record PromptHash(string Id, string Version, string Sha256);

/// <summary>
/// The evaluation index as read back in full: a hash over every field of every section, ordered by key. A run is
/// comparable only when the index read whole, consistently, and didn't change between the start and the end.
/// </summary>
/// <param name="Problem">Why the index can't vouch for the run: unread, unreadable, empty, inconsistent or changed.</param>
internal sealed record IndexIdentity(string Name, string? Fingerprint, int Documents, bool Stable, string? Problem)
{
    public const string NotRead = "not read";

    public static IndexIdentity Unread(string name) => new(name, null, 0, false, NotRead);

    /// <summary>The start's identity, stable only if the end read the same index.</summary>
    public IndexIdentity Ended(IndexIdentity end)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (Problem is not null)
        {
            return this;
        }

        return end.Problem is not null ? this with { Stable = false, Problem = $"at the end: {end.Problem}" }
            : end.Fingerprint != Fingerprint ? this with { Stable = false, Problem = "changed during the run" }
            : this with { Stable = true };
    }
}

/// <summary>Everything a run read that decides its results, so two reports can be checked to have measured the same thing.</summary>
/// <param name="Datasets">File name to the SHA-256 of its bytes, for each dataset the run read.</param>
/// <param name="Release">The SHA-256 of the AI release manifest.</param>
/// <param name="Routing">The routing mode the run used: the release's, or the evaluation's override.</param>
/// <param name="Pricing">The SHA-256 of the price list the run's costs came from.</param>
/// <param name="Thresholds">The SHA-256 of the frozen thresholds the run was gated on; null when it ran without them.</param>
internal sealed record ReportInputs(
    IReadOnlyDictionary<string, string> Datasets,
    IReadOnlyList<PromptHash> Prompts,
    string Release,
    ModelIdentity? Chat,
    ModelIdentity? Small,
    ModelIdentity? Screen,
    string Routing,
    IndexIdentity Index,
    string Pricing,
    string? Thresholds)
{
    public static ReportInputs From(AiRelease release, string aiDirectory, IEnumerable<string> datasets, IndexIdentity index, string pricing, string? thresholds)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ReportInputs(
            datasets.ToDictionary(path => Path.GetFileName(path), Sha256Of, StringComparer.Ordinal),
            [.. release.Prompts.Values.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => new PromptHash(p.Id, p.Version, p.Sha256))],
            Sha256Of(Path.Combine(aiDirectory, AiReleaseLoader.ManifestPath)),
            ModelIdentity.From(release, release.ActiveChatDeployment),
            ModelIdentity.From(release, release.SmallDeployment),
            ModelIdentity.From(release, release.ScreenDeployment),
            release.RoutingMode == RoutingMode.Cascade ? "cascade" : "off",
            index,
            Sha256Of(pricing),
            thresholds is null ? null : Sha256Of(thresholds));
    }

    public static string Sha256Of(string path) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}

internal static class IndexFingerprint
{
    private static readonly JsonSerializerOptions Canonical = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>
    /// A hash over a canonical form of every document, all fields in declaration order, sorted by key. The documents
    /// must have been read in key order with no key twice, as the paged read returns them, or the read wasn't one index.
    /// </summary>
    public static IndexIdentity Of(string name, IReadOnlyList<KnowledgeSearchDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            return new IndexIdentity(name, null, 0, false, "empty");
        }

        for (var i = 1; i < documents.Count; i++)
        {
            var order = string.CompareOrdinal(documents[i - 1].Id, documents[i].Id);
            if (order >= 0)
            {
                return new IndexIdentity(name, null, documents.Count, false, order == 0 ? "inconsistent: a key read twice" : "inconsistent: keys out of order");
            }
        }

        return new IndexIdentity(name, Hash(documents), documents.Count, false, null);
    }

    /// <summary>The hash alone, whatever order the documents come in.</summary>
    public static string Hash(IEnumerable<KnowledgeSearchDocument> documents) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(documents.OrderBy(d => d.Id, StringComparer.Ordinal).ToList(), Canonical)));

    public static async Task<IndexIdentity> ReadAsync(string name, IKnowledgeIndexClient? index, CancellationToken cancellationToken)
    {
        if (index is null)
        {
            return IndexIdentity.Unread(name);
        }

        try
        {
            return Of(name, await index.ReadAllAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a service message can hold anything.
            return new IndexIdentity(name, null, 0, false, $"unreadable ({ex.GetType().Name})");
        }
    }
}
