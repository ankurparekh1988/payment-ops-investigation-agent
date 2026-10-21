namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>A caller can't change a list a port returned, so it can't change what the next caller sees.</summary>
public static class ReadOnlyResults
{
    // List<T> and arrays both pass as IReadOnlyList<T> and both accept writes through IList<T>.
    public static void AssertReadOnly<T>(IReadOnlyList<T> items)
    {
        Assert.NotEmpty(items);
        if (items is IList<T> list)
        {
            Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
            Assert.Throws<NotSupportedException>(() => list.Add(list[0]));
        }
    }
}
