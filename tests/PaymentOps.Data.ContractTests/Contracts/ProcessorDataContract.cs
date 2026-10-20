using PaymentOps.Domain.Processors;

using static PaymentOps.Data.ContractTests.Contracts.ContractData;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>What every <see cref="IProcessorData"/> must do. An implementation's tests inherit this class.</summary>
public abstract class ProcessorDataContract
{
    protected abstract IProcessorData Create(ContractData data);

    private IProcessorData Port => Create(new ContractData());

    [Fact]
    public async Task A_processor_is_found_with_its_configuration()
    {
        var processor = await Port.FindAsync("p-b", CancellationToken.None);

        Assert.NotNull(processor);
        Assert.Equal(new TimeOnly(17, 0), processor.Configuration.CutOff);
        Assert.Equal("Europe/London", processor.Configuration.TimeZoneId);
        Assert.Equal(1, processor.Configuration.HoldingSettlementDays);
    }

    [Theory]
    [InlineData("p-z")]
    [InlineData("P-A")]
    public async Task An_unknown_id_finds_nothing_and_ids_match_exactly(string id) =>
        Assert.Null(await Port.FindAsync(id, CancellationToken.None));

    [Fact]
    public async Task An_open_incident_that_started_earlier_overlaps_the_window()
    {
        var incidents = await Port.GetIncidentsAsync("p-a", Utc(2026, 11, 6, 0, 0), Utc(2026, 11, 7, 0, 0), CancellationToken.None);

        Assert.Equal(["INC-1"], incidents.Select(i => i.Id));
        Assert.Null(incidents[0].ResolvedAt);
    }

    // An outage that covered the window's start counts even though it's over: the "outage covered the cut-off" case.
    [Fact]
    public async Task A_resolved_incident_that_started_earlier_overlaps_the_window()
    {
        var incidents = await Port.GetIncidentsAsync("p-a", Utc(2026, 11, 5, 0, 0), Utc(2026, 11, 6, 0, 0), CancellationToken.None);

        Assert.Equal(["INC-1", "INC-5"], incidents.Select(i => i.Id));
    }

    [Fact]
    public async Task Incidents_overlap_a_half_open_window_in_start_order()
    {
        // INC-3 resolved exactly at the window's start, so it doesn't overlap; INC-4 is another processor's.
        var incidents = await Port.GetIncidentsAsync("p-a", Utc(2026, 11, 2, 0, 0), Utc(2026, 11, 3, 10, 0), CancellationToken.None);

        Assert.Equal(["INC-1"], incidents.Select(i => i.Id));

        var later = await Port.GetIncidentsAsync("p-a", Utc(2026, 11, 2, 0, 0), Utc(2026, 11, 3, 10, 1), CancellationToken.None);
        Assert.Equal(["INC-1", "INC-2"], later.Select(i => i.Id));
    }

    [Fact]
    public async Task Incidents_cannot_be_changed_by_the_caller() =>
        ReadOnlyResults.AssertReadOnly(await Port.GetIncidentsAsync("p-a", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), CancellationToken.None));

    [Fact]
    public async Task From_must_be_before_to() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetIncidentsAsync("p-a", Utc(2026, 11, 3, 0, 0), Utc(2026, 11, 2, 0, 0), CancellationToken.None));

    [Fact]
    public async Task There_is_no_maximum_span()
    {
        var incidents = await Port.GetIncidentsAsync("p-a", Utc(2025, 1, 1, 0, 0), Utc(2027, 1, 1, 0, 0), CancellationToken.None);

        Assert.Equal(["INC-3", "INC-1", "INC-2", "INC-5"], incidents.Select(i => i.Id));
    }

    [Fact]
    public async Task A_cancelled_token_throws() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port.GetIncidentsAsync("p-a", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), Cancelled()));
}
