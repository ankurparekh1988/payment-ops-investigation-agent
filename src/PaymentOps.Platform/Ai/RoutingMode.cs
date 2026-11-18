namespace PaymentOps.Platform.Ai;

/// <summary>Which model an investigation starts on. The release chooses; requests never do.</summary>
public enum RoutingMode
{
    /// <summary>Every investigation runs on the active chat deployment.</summary>
    Off,

    /// <summary>The small deployment first; one attempt on the chat deployment after an eligible failure (ADR 0023).</summary>
    Cascade,
}
