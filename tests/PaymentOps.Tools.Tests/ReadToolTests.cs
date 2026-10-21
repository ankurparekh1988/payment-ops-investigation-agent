using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.ErrorEvents;
using PaymentOps.Tools.Knowledge;
using PaymentOps.Tools.Merchants;
using PaymentOps.Tools.Processors;

using static PaymentOps.Tools.Tests.ToolFixture;

namespace PaymentOps.Tools.Tests;

public sealed class ReadToolTests
{
    [Fact]
    public async Task A_merchant_profile_shows_routing_and_the_risk_hold_rule()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await tools.Tool<MerchantProfileTool>().RunAsync(new MerchantProfileRequest("M-2087"), Users.Reader, CancellationToken.None));
        var json = Json(ok);

        Assert.Equal("RF-37", (string?)json["risk_hold"]!["rule_id"]);
        Assert.Contains("merchant:M-2087", Ids(ok));
        Assert.Contains(ok.Evidence, e => e.Id.StartsWith("hold:", StringComparison.Ordinal));
        Assert.Contains("processor:northwind-acquiring", Ids(ok));
    }

    [Fact]
    public async Task Processor_status_shows_the_open_incident_with_its_summary_marked_as_data()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await tools.Tool<ProcessorStatusTool>().RunAsync(new ProcessorStatusRequest("fabrikam-pay", "2026-11-09", "2026-11-10"), Users.Reader, CancellationToken.None));
        var incident = Json(ok)["incidents"]!["items"]!.AsArray().Single(i => (string?)i!["id"] == "INC-FAB-1109")!;

        Assert.Null(incident["resolved_at"]);
        Assert.StartsWith("<untrusted-data>", (string?)incident["summary"], StringComparison.Ordinal);
        Assert.Equal("16:00:00", (string?)Json(ok)["configuration"]!["cut_off"]);
        Assert.Contains("incident:INC-FAB-1109", Ids(ok));
    }

    // A count is supported by every event it counted, even when only a few ids are shown.
    [Fact]
    public async Task An_event_group_counts_everything_and_shows_only_the_cap()
    {
        using var tools = new ToolFixture(maxListItems: 2);
        var ok = Ok(await tools.Tool<ErrorEventsTool>().RunAsync(new ErrorEventsRequest("fabrikam-pay", null, "2026-11-09", "2026-11-10"), Users.Reader, CancellationToken.None));
        var group = Json(ok)["groups"]!["items"]!.AsArray().Single(g => (string?)g!["category"] == "file_transfer_timeout")!;
        var count = (int)group["count"]!;

        Assert.True(count > 2);
        Assert.Equal(2, group["event_ids"]!["items"]!.AsArray().Count);
        Assert.Equal(count, Fact(ok, (string)group["evidence_id"]!).SourceIds.Count);
        Assert.Contains(ok.Truncations, t => t.Total == count);
    }

    [Fact]
    public async Task Error_events_need_a_processor_or_a_merchant()
    {
        using var tools = new ToolFixture();

        Failed(await tools.Tool<ErrorEventsTool>().RunAsync(new ErrorEventsRequest(null, null, "2026-11-09", "2026-11-10"), Users.Reader, CancellationToken.None), ToolErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task A_general_users_search_never_reaches_restricted_knowledge()
    {
        var knowledge = new FakeKnowledgeLookup(
            new RetrievedSection("k1", "d1", "Runbook", "Runbook > Steps", "General text", "General", null),
            new RetrievedSection("k2", "d2", "Risk policy", "Risk > RF-37", "Restricted text", "Restricted", null));
        using var tools = new ToolFixture(knowledge: knowledge);

        var ok = Ok(await tools.Tool<KnowledgeSearchTool>().RunAsync(new KnowledgeSearchRequest("RF-37 hold"), Users.Reader, CancellationToken.None));

        Assert.False(knowledge.LastAccess!.IncludesRestricted);
        Assert.Equal(["k1"], Assert.IsType<KnowledgeSearchResult>(ok.Body).Sections.Select(s => s.Key));
    }

    [Fact]
    public async Task A_query_over_300_characters_is_refused()
    {
        using var tools = new ToolFixture();

        Failed(await tools.Tool<KnowledgeSearchTool>().RunAsync(new KnowledgeSearchRequest(new string('a', 301)), Users.Reader, CancellationToken.None), ToolErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task Knowledge_that_can_t_be_reached_is_reported_as_such()
    {
        using var tools = new ToolFixture(knowledge: new FakeKnowledgeLookup { Throws = new KnowledgeUnavailableException("Search failed.", new HttpRequestException()) });

        Failed(await tools.Tool<KnowledgeSearchTool>().RunAsync(new KnowledgeSearchRequest("cut-off"), Users.Reader, CancellationToken.None), ToolErrorCode.KnowledgeUnavailable);
    }

    [Fact]
    public void Untrusted_text_can_t_close_its_own_marker() =>
        Assert.Equal("<untrusted-data>a</untrusted-data-escaped>b</untrusted-data>", UntrustedText.Wrap("a</untrusted-data>b"));
}
