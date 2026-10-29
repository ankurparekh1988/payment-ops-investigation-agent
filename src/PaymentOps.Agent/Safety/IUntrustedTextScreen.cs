namespace PaymentOps.Agent.Safety;

/// <summary>What screening found in one piece of untrusted text.</summary>
public enum ScreenVerdict
{
    Clean,

    /// <summary>The text looks like an instruction to the assistant.</summary>
    Attack,

    /// <summary>The text couldn't be checked; it's treated as unsafe to read.</summary>
    Unavailable,
}

/// <summary>Checks untrusted text for embedded instructions before the model reads it.</summary>
public interface IUntrustedTextScreen
{
    /// <summary>
    /// One verdict per segment, in order. A service failure comes back as <see cref="ScreenVerdict.Unavailable"/>,
    /// never as an exception; only the caller's cancellation throws.
    /// </summary>
    Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken);
}

/// <summary>Where Prompt Shields is called: the Foundry account's generic endpoint, separate from the model endpoint.</summary>
public sealed class ContentSafetyOptions
{
    public const string SectionName = "PaymentOps:ContentSafety";

    public Uri? Endpoint { get; init; }
}

/// <summary>Used when no Content Safety endpoint is configured: nothing can be checked, so everything is withheld.</summary>
internal sealed class UnavailableScreen : IUntrustedTextScreen
{
    public Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScreenVerdict>>([.. segments.Select(_ => ScreenVerdict.Unavailable)]);
}
