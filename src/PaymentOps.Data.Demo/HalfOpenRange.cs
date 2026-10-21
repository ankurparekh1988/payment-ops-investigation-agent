namespace PaymentOps.Data.Demo;

/// <summary>The ports' range rule: [fromInclusive, toExclusive) with from strictly before to, and no maximum span.</summary>
internal static class HalfOpenRange
{
    public static void Require<T>(T fromInclusive, T toExclusive)
        where T : IComparable<T>
    {
        if (fromInclusive.CompareTo(toExclusive) >= 0)
        {
            throw new ArgumentException($"The range start {fromInclusive} must be before its end {toExclusive}.", nameof(fromInclusive));
        }
    }

    public static bool Contains<T>(T value, T fromInclusive, T toExclusive)
        where T : IComparable<T> =>
        value.CompareTo(fromInclusive) >= 0 && value.CompareTo(toExclusive) < 0;
}
