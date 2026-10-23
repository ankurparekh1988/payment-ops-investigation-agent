using Microsoft.Extensions.Time.Testing;

using PaymentOps.Domain.Incidents;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>What every <see cref="IIncidentStore"/> must do. An implementation's tests inherit this class.</summary>
public abstract class IncidentStoreContract
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 14, 17, 0, TimeSpan.Zero);

    protected FakeTimeProvider Time { get; } = new(Now);

    protected abstract IIncidentStore Create(int maxTickets);

    private static IncidentTicketContent Content(string title = "Fabrikam Pay settlement file missing") => new(
        "fabrikam-pay", ["M-1042"], new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), title, "No file arrived for 9 Nov.",
        ["fact:missing-file:fabrikam-pay:2026-11-09"], "FAB-7731", Guid.NewGuid(), "00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Create_returns_a_ticket_findable_by_id()
    {
        var store = Create(1000);
        var content = Content();

        var ticket = await store.CreateAsync("key-1", content, CancellationToken.None);

        Assert.Equal("INC-0001", ticket.Id);
        Assert.Equal(Now, ticket.CreatedAt);
        Assert.Same(content, ticket.Content);
        Assert.Equal(ticket, await store.FindAsync("INC-0001", CancellationToken.None));
    }

    [Fact]
    public async Task The_same_key_returns_the_same_ticket_and_creates_nothing()
    {
        var store = Create(1000);
        var first = await store.CreateAsync("key-1", Content("first"), CancellationToken.None);

        var second = await store.CreateAsync("key-1", Content("second"), CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal("first", second.Content.Title);
        Assert.Null(await store.FindAsync("INC-0002", CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_creates_with_one_key_make_one_ticket()
    {
        var store = Create(1000);

        var tickets = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => store.CreateAsync("key-1", Content(), CancellationToken.None))));

        Assert.Single(tickets.Select(t => t.Id).Distinct());
        Assert.Null(await store.FindAsync("INC-0002", CancellationToken.None));
    }

    [Fact]
    public async Task Beyond_the_bound_creation_is_refused()
    {
        var store = Create(2);
        var first = await store.CreateAsync("key-1", Content(), CancellationToken.None);
        await store.CreateAsync("key-2", Content(), CancellationToken.None);

        await Assert.ThrowsAsync<IncidentStoreFullException>(() => store.CreateAsync("key-3", Content(), CancellationToken.None));
        Assert.Equal(first, await store.CreateAsync("key-1", Content(), CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_id_is_not_found() =>
        Assert.Null(await Create(1000).FindAsync("INC-9999", CancellationToken.None));
}
