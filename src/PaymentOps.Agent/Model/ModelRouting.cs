using Microsoft.Extensions.AI;

using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Model;

/// <summary>Which model an investigation attempt runs on.</summary>
internal enum ModelTier
{
    Small,
    Chat,
}

/// <summary>The investigation's models by tier, and which one an investigation starts on (ADR 0023).</summary>
internal sealed class ModelRouting
{
    public ModelRouting(RoutingMode mode, IChatClient chat, IChatClient? small)
    {
        ArgumentNullException.ThrowIfNull(chat);
        if (mode == RoutingMode.Cascade && small is null)
        {
            throw new ArgumentException("A cascade needs a small model.", nameof(small));
        }

        Mode = mode;
        Chat = chat;
        Small = small;
    }

    public RoutingMode Mode { get; }

    public IChatClient Chat { get; }

    /// <summary>Null when the release has no small model; then every attempt runs on chat.</summary>
    public IChatClient? Small { get; }

    public ModelTier FirstTier => Mode == RoutingMode.Cascade ? ModelTier.Small : ModelTier.Chat;

    public IChatClient For(ModelTier tier) =>
        tier == ModelTier.Chat ? Chat : Small ?? throw new InvalidOperationException("The release has no small model.");
}
