namespace PaymentOps.Tools;

/// <summary>A list as the model sees it: at most the cap, and saying how many there were when it was cut.</summary>
public sealed record ListOf<T>(IReadOnlyList<T> Items, bool Truncated, int Total);

public static class ListOf
{
    public static ListOf<T> Capped<T>(IReadOnlyList<T> items, int cap, string what, out Truncation? truncation)
    {
        ArgumentNullException.ThrowIfNull(items);

        truncation = items.Count > cap ? new Truncation(what, cap, items.Count) : null;
        return new ListOf<T>([.. items.Take(cap)], truncation is not null, items.Count);
    }
}
