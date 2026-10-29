using Microsoft.Extensions.Configuration;

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
public static class ContentSafetyOptions
{
    public const string SectionName = "PaymentOps:ContentSafety";
    public const string EndpointKey = SectionName + ":Endpoint";

    /// <summary>The configured endpoint, or null unless it's an absolute https address; startup and registration both read it here.</summary>
    public static Uri? Endpoint(IConfiguration configuration) =>
        Uri.TryCreate(configuration?[EndpointKey], UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps ? endpoint : null;
}

/// <summary>Used when no Content Safety endpoint is configured: nothing can be checked, so everything is withheld.</summary>
internal sealed class UnavailableScreen : IUntrustedTextScreen
{
    public Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScreenVerdict>>(AllUnavailable(segments.Count));

    /// <summary>The verdicts when nothing could be checked.</summary>
    public static ScreenVerdict[] AllUnavailable(int count) => [.. Enumerable.Repeat(ScreenVerdict.Unavailable, count)];
}
