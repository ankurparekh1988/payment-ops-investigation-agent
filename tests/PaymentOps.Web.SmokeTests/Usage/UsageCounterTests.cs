using Azure;
using Azure.Data.Tables;

using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>
/// A reservation counts the user and the day's total together or not at all, and never past either limit; only an
/// admitted attempt counts. Storage that fails, or keeps conflicting, refuses rather than guessing.
/// </summary>
public sealed class UsageCounterTests
{
    private static readonly DateOnly Day = new(2026, 11, 14);
    private const string Partition = "2026-11-14";

    private static TableUsageCounter Counter(FakeTableClient table, int maxAttempts = TableUsageCounter.DefaultMaxAttempts) =>
        new(table, maxAttempts, _ => TimeSpan.Zero);

    [Fact]
    public async Task Exactly_the_limit_is_admitted()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);

        var results = new List<UsageReservation>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await counter.TryReserveAsync("user-a", Day, perUserLimit: 3, totalLimit: 100, CancellationToken.None));
        }

        Assert.Equal([UsageReservation.Admitted, UsageReservation.Admitted, UsageReservation.Admitted, UsageReservation.UserLimitReached], results);
        Assert.Equal(3, table.Count(Partition, TableUsageCounter.UserRow("user-a")));
        Assert.Equal(3, table.Count(Partition, TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task A_refused_attempt_consumes_nothing()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);
        await counter.TryReserveAsync("user-a", Day, 1, 100, CancellationToken.None);
        var transactions = table.Transactions;

        Assert.Equal(UsageReservation.UserLimitReached, await counter.TryReserveAsync("user-a", Day, 1, 100, CancellationToken.None));

        Assert.Equal(transactions, table.Transactions);
        Assert.Equal(1, table.Count(Partition, TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task The_total_limit_applies_to_everyone()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);

        Assert.Equal(UsageReservation.Admitted, await counter.TryReserveAsync("user-a", Day, 10, totalLimit: 2, CancellationToken.None));
        Assert.Equal(UsageReservation.Admitted, await counter.TryReserveAsync("user-b", Day, 10, totalLimit: 2, CancellationToken.None));
        Assert.Equal(UsageReservation.TotalLimitReached, await counter.TryReserveAsync("user-c", Day, 10, totalLimit: 2, CancellationToken.None));

        Assert.Equal(0, table.Count(Partition, TableUsageCounter.UserRow("user-c")));
    }

    // With enough attempts every reservation resolves, so the admitted count is exact.
    [Fact]
    public async Task Same_user_concurrency_never_exceeds_the_limit()
    {
        var table = new FakeTableClient();
        var counter = Counter(table, maxAttempts: 50);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => counter.TryReserveAsync("user-a", Day, perUserLimit: 3, totalLimit: 100, CancellationToken.None))));

        Assert.Equal(3, results.Count(r => r == UsageReservation.Admitted));
        Assert.Equal(17, results.Count(r => r == UsageReservation.UserLimitReached));
        Assert.Equal(3, table.Count(Partition, TableUsageCounter.UserRow("user-a")));
        Assert.Equal(3, table.Count(Partition, TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task Different_user_concurrency_never_exceeds_the_total()
    {
        var table = new FakeTableClient();
        var counter = Counter(table, maxAttempts: 50);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => counter.TryReserveAsync($"user-{i}", Day, perUserLimit: 10, totalLimit: 5, CancellationToken.None))));

        Assert.Equal(5, results.Count(r => r == UsageReservation.Admitted));
        Assert.Equal(5, table.Count(Partition, TableUsageCounter.TotalRow));
        Assert.Equal(5, table.Rows.Where(r => r.Key.Row != TableUsageCounter.TotalRow).Sum(r => r.Value));
    }

    // At the default attempts, heavy contention may refuse some, but the counts always agree and stay within the limit.
    [Fact]
    public async Task Under_contention_the_counts_always_agree()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            try
            {
                return await counter.TryReserveAsync($"user-{i}", Day, perUserLimit: 10, totalLimit: 5, CancellationToken.None);
            }
            catch (UsageUnavailableException)
            {
                return (UsageReservation?)null;
            }
        })));

        var admitted = outcomes.Count(r => r == UsageReservation.Admitted);
        Assert.InRange(admitted, 1, 5);
        Assert.Equal(admitted, table.Count(Partition, TableUsageCounter.TotalRow));
        Assert.Equal(admitted, table.Rows.Where(r => r.Key.Row != TableUsageCounter.TotalRow).Sum(r => r.Value));
    }

    [Fact]
    public async Task A_failed_transaction_changes_neither_row()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);
        await counter.TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None);
        table.FailTransactionsWith = 500;

        await Assert.ThrowsAsync<UsageUnavailableException>(() => counter.TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None));

        Assert.Equal(1, table.Count(Partition, TableUsageCounter.UserRow("user-a")));
        Assert.Equal(1, table.Count(Partition, TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task Persistent_conflicts_fail_closed_after_the_attempts_run_out()
    {
        var table = new FakeTableClient { FailTransactionsWith = 412 };

        await Assert.ThrowsAsync<UsageUnavailableException>(() => Counter(table).TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None));

        Assert.Equal(TableUsageCounter.DefaultMaxAttempts, table.Transactions);
    }

    [Fact]
    public async Task Unavailable_storage_fails_closed()
    {
        var table = new FakeTableClient { ReadsThrow = new Azure.RequestFailedException(503, "read failed") };

        await Assert.ThrowsAsync<UsageUnavailableException>(() => Counter(table).TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None));

        Assert.Equal(0, table.Transactions);
    }

    // An outage is rarely a single service response: retries give up in an AggregateException, a network timeout is a
    // TaskCanceledException nobody asked for, and a sign-in failure isn't a RequestFailedException at all.
    public static TheoryData<string> Outages() => ["retries exhausted", "network timeout", "sign-in failed"];

    private static Exception Outage(string kind) => kind switch
    {
        "retries exhausted" => new AggregateException("Retry failed after 4 tries.", new Azure.RequestFailedException(503, "unavailable")),
        "network timeout" => new TaskCanceledException("The request timed out."),
        _ => new Azure.Identity.AuthenticationFailedException("token"),
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task Any_outage_fails_closed(string kind)
    {
        var table = new FakeTableClient { ReadsThrow = Outage(kind) };

        await Assert.ThrowsAsync<UsageUnavailableException>(() => Counter(table).TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None));
    }

    // A row that exists but can't be read as a count isn't a fresh day: counting it as zero would reset the limits.
    public static TheoryData<string, string> MalformedRows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var row in new[] { "user", "total" })
        {
            foreach (var damage in new[] { "count missing", "count null", "count negative", "count not a number", "no etag" })
            {
                rows.Add(row, damage);
            }
        }

        return rows;
    }

    private static TableEntity Malformed(string row, string damage)
    {
        // Setting ETag to default stores "<null>", so a row without one is built without it.
        var entity = damage == "no etag" ? new TableEntity(Partition, row) : new TableEntity(Partition, row) { ETag = new ETag("W/\"planted\"") };
        switch (damage)
        {
            case "count null":
                entity["Count"] = null;
                break;
            case "count negative":
                entity["Count"] = -1;
                break;
            case "count not a number":
                entity["Count"] = "7";
                break;
            case "no etag":
                entity["Count"] = 1;
                break;
        }

        return entity;
    }

    [Theory]
    [MemberData(nameof(MalformedRows))]
    public async Task A_malformed_row_refuses_and_changes_nothing(string row, string damage)
    {
        var table = new FakeTableClient();
        var counter = Counter(table);
        await counter.TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None);
        var key = row == "user" ? TableUsageCounter.UserRow("user-a") : TableUsageCounter.TotalRow;
        var other = row == "user" ? TableUsageCounter.TotalRow : TableUsageCounter.UserRow("user-a");
        var planted = Malformed(key, damage);
        table.Plant(planted);
        var transactions = table.Transactions;

        await Assert.ThrowsAsync<UsageUnavailableException>(() => counter.TryReserveAsync("user-a", Day, 10, 10, CancellationToken.None));

        Assert.Equal(transactions, table.Transactions);
        Assert.Same(planted, table.Planted(Partition, key));
        Assert.Equal(1, table.Count(Partition, other));
    }

    [Fact]
    public async Task A_cancelled_request_stays_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var table = new FakeTableClient { ReadsThrow = new OperationCanceledException(cancel.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Counter(table).TryReserveAsync("user-a", Day, 10, 10, cancel.Token));
    }

    [Fact]
    public async Task Each_day_has_its_own_counts()
    {
        var table = new FakeTableClient();
        var counter = Counter(table);

        Assert.Equal(UsageReservation.Admitted, await counter.TryReserveAsync("user-a", Day, 1, 1, CancellationToken.None));
        Assert.Equal(UsageReservation.Admitted, await counter.TryReserveAsync("user-a", Day.AddDays(1), 1, 1, CancellationToken.None));

        Assert.Equal(1, table.Count("2026-11-14", TableUsageCounter.TotalRow));
        Assert.Equal(1, table.Count("2026-11-15", TableUsageCounter.TotalRow));
    }

    [Fact]
    public void A_user_row_holds_no_user_id() =>
        Assert.DoesNotContain("zzq-user", TableUsageCounter.UserRow("zzq-user"), StringComparison.Ordinal);

    [Fact]
    public async Task The_in_memory_counter_keeps_the_same_rules()
    {
        var counter = new InMemoryUsageCounter();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => counter.TryReserveAsync($"user-{i % 2}", Day, perUserLimit: 3, totalLimit: 5, CancellationToken.None)));

        Assert.Equal(5, results.Count(r => r == UsageReservation.Admitted));
    }
}
