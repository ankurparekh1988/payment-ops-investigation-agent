using System.Security.Claims;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;
using PaymentOps.Web.Investigating;
using PaymentOps.Web.Security;
using PaymentOps.Web.SmokeTests.Investigating;

namespace PaymentOps.Web.SmokeTests.Security;

/// <summary>
/// Approving creates a ticket, so it needs a recent sign-in, checked inside the resolved approval service whoever calls
/// it. Ownership, expiry and idempotency stay with the approval service behind it.
/// </summary>
public sealed class FreshApprovalServiceTests : IDisposable
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(T);
    private readonly PaymentOpsWebApplicationFactory _factory = new();
    private readonly List<WebApplicationFactory<Program>> _hosts = [];
    private int _approvalRegistrations = -1;

    [Fact]
    public async Task Stale_approval_is_refused_and_inner_not_called()
    {
        var inner = new CountingApprovalService(new ApprovalResult.Created("INC-0001"));
        var services = new ServiceCollection();
        services.AddScoped<IApprovalService>(_ => inner);
        services.AddScoped<IRecentAuthentication>(_ => new TestRecentAuthentication { Recent = false });
        await using (var provider = services.RequireRecentAuthenticationForApproval().BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var refused = await scope.ServiceProvider.GetRequiredService<IApprovalService>().ApproveAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.IsType<ApprovalResult.NotAuthorized>(refused);
            Assert.Equal(0, inner.Approvals);
        }

        // The same through the real host's service: no ticket, and the proposal stays pending.
        var host = Configured();
        var engineer = SignIn(host);
        MakeStale(host, engineer);
        var proposal = OpenProposal(host, engineer.Oid);

        var result = await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None);

        Assert.IsType<ApprovalResult.NotAuthorized>(result);
        Assert.Empty(await Tickets(host));
        StepUp(host, engineer);
        Assert.IsType<ApprovalResult.Created>(await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task Fresh_approval_creates_one_ticket()
    {
        var host = Configured();
        var engineer = SignIn(host);
        var proposal = OpenProposal(host, engineer.Oid);

        var result = await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None);

        var created = Assert.IsType<ApprovalResult.Created>(result);
        Assert.Equal(created.TicketId, Assert.Single(await Tickets(host)).Id);
    }

    [Fact]
    public async Task Fifty_parallel_fresh_approvals_create_one_ticket()
    {
        var host = Configured();
        var engineer = SignIn(host);
        var proposal = OpenProposal(host, engineer.Oid);
        var circuits = Enumerable.Range(0, 50).Select(_ => InScope(host, engineer.Sid, engineer.Oid)).ToList();

        var results = await Task.WhenAll(circuits.Select(circuit =>
            Task.Run(() => circuit.ServiceProvider.GetRequiredService<IApprovalService>().ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None))));

        var ticket = Assert.Single(await Tickets(host));
        Assert.Equal(ticket.Id, Assert.IsType<ApprovalResult.Created>(Assert.Single(results, r => r is ApprovalResult.Created)).TicketId);
        Assert.All(results, r => Assert.True(
            r is ApprovalResult.Created || r is ApprovalResult.Unavailable || r is ApprovalResult.AlreadyDecided { TicketId: var id } && id == ticket.Id,
            $"Unexpected {r}."));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decorator_registration_in_both_hosts(bool configured)
    {
        var host = configured ? Configured() : Development();

        using var first = host.Services.CreateScope();
        using var second = host.Services.CreateScope();
        var resolved = first.ServiceProvider.GetRequiredService<IApprovalService>();

        var fresh = Assert.IsType<FreshApprovalService>(resolved);
        if (configured)
        {
            Assert.Equal("PaymentOps.Agent.Approvals.ApprovalService", fresh.Inner.GetType().FullName);
        }
        else
        {
            Assert.IsType<UnconfiguredApprovalService>(fresh.Inner);
        }

        Assert.Same(resolved, first.ServiceProvider.GetRequiredService<IApprovalService>());
        Assert.NotSame(resolved, second.ServiceProvider.GetRequiredService<IApprovalService>());
        Assert.Equal(1, _approvalRegistrations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Zero_or_two_registrations_stop_startup(int registrations)
    {
        var services = new ServiceCollection();
        for (var i = 0; i < registrations; i++)
        {
            services.AddScoped<IApprovalService, UnconfiguredApprovalService>();
        }

        var failure = Assert.Throws<InvalidOperationException>(() => services.RequireRecentAuthenticationForApproval());

        Assert.Equal("Expected exactly one IApprovalService registration.", failure.Message);
        Assert.Equal(registrations, services.Count);
    }

    [Fact]
    public async Task Step_up_does_not_replay()
    {
        var host = Configured();
        var engineer = SignIn(host);
        MakeStale(host, engineer);
        var proposal = OpenProposal(host, engineer.Oid);
        var investigation = new ScriptedInvestigationService { Answer = _ => Results.WithPendingTicket() with { Proposal = proposal } };
        var services = engineer.Scope.ServiceProvider;
        await using var session = new InvestigationSession(
            investigation, Approvals(engineer), services.GetRequiredService<IUserContext>(), services.GetRequiredService<IRecentAuthentication>(),
            _time, NullLogger<InvestigationSession>.Instance);
        await session.StartAsync("Why was M-1042 not paid?");

        await session.ApproveAsync();
        Assert.True(session.NeedsConfirmation);
        StepUp(host, engineer);

        // The step-up only records the sign-in: the click before it is not carried out.
        Assert.Empty(await Tickets(host));
        Assert.Equal(ProposalStatus.Pending, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);

        await session.ApproveAsync();

        var ticket = Assert.Single(await Tickets(host));
        Assert.Equal(ticket.Id, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.TicketId);
        Assert.False(session.NeedsConfirmation);
    }

    [Fact]
    public async Task Proposal_expired_during_step_up_reports_expired()
    {
        var host = Configured();
        var engineer = SignIn(host);
        MakeStale(host, engineer);
        var proposal = OpenProposal(host, engineer.Oid);
        Assert.IsType<ApprovalResult.NotAuthorized>(await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));

        PassProposalLifetime(host, engineer);
        StepUp(host, engineer);
        var result = await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None);

        Assert.IsType<ApprovalResult.Expired>(result);
        Assert.Empty(await Tickets(host));
    }

    [Fact]
    public async Task Approval_keeps_owner_expiry_and_idempotency()
    {
        var host = Configured();
        var engineer = SignIn(host);

        var someoneElses = OpenProposal(host, Guid.NewGuid().ToString());
        Assert.IsType<ApprovalResult.NotFound>(await Approvals(engineer).ApproveAsync(someoneElses.ProposalId!.Value, CancellationToken.None));

        var expiring = OpenProposal(host, engineer.Oid);
        PassProposalLifetime(host, engineer);
        StepUp(host, engineer);
        Assert.IsType<ApprovalResult.Expired>(await Approvals(engineer).ApproveAsync(expiring.ProposalId!.Value, CancellationToken.None));

        var proposal = OpenProposal(host, engineer.Oid);
        var created = Assert.IsType<ApprovalResult.Created>(await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
        var again = await Approvals(engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None);

        Assert.Equal(created.TicketId, Assert.IsType<ApprovalResult.AlreadyDecided>(again).TicketId);
        Assert.Single(await Tickets(host));
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        _factory.Dispose();
    }

    private sealed record Engineer(IServiceScope Scope, string Sid, string Oid);

    private WebApplicationFactory<Program> Configured() => Host(b => b
        .UseSetting("PaymentOps:Search:Endpoint", "https://search.example")
        .UseSetting("PaymentOps:Foundry:Endpoint", "https://foundry.example/"));

    private WebApplicationFactory<Program> Development() => Host(_ => { });

    private WebApplicationFactory<Program> Host(Action<IWebHostBuilder> configure)
    {
        var host = _factory.WithWebHostBuilder(b =>
        {
            configure(b);
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_time);
                _approvalRegistrations = services.Count(d => d.ServiceType == typeof(IApprovalService));
            });
        });
        _hosts.Add(host);
        return host;
    }

    private Engineer SignIn(WebApplicationFactory<Program> host)
    {
        var (sid, oid) = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        var access = host.Services.GetRequiredService<IOptions<AccessOptions>>().Value;
        Registry(host).Add(TestSessions.Started(sid, oid, _time.GetUtcNow(), access, [OpsRoles.Engineer]));
        return new Engineer(InScope(host, sid, oid), sid, oid);
    }

    // One circuit: its own scope, with the signed-in user set as the circuit handler does.
    private static IServiceScope InScope(WebApplicationFactory<Program> host, string sid, string oid)
    {
        var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().User = TestPrincipal.From(
        [
            new Claim("oid", oid),
            new Claim("name", "Elliot Engineer"),
            new Claim(SessionClaims.Sid, sid),
            new Claim("roles", OpsRoles.Engineer),
        ]);
        return scope;
    }

    private static IApprovalService Approvals(Engineer engineer) => engineer.Scope.ServiceProvider.GetRequiredService<IApprovalService>();

    private static SessionRegistry Registry(WebApplicationFactory<Program> host) => host.Services.GetRequiredService<SessionRegistry>();

    // Live, but signed in longer ago than the recent-sign-in window.
    private void MakeStale(WebApplicationFactory<Program> host, Engineer engineer)
    {
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.True(Registry(host).Touch(engineer.Sid, engineer.Oid));
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(Registry(host).Status(engineer.Sid, engineer.Oid).IsLive);
    }

    private void PassProposalLifetime(WebApplicationFactory<Program> host, Engineer engineer)
    {
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(Registry(host).Touch(engineer.Sid, engineer.Oid));
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(Registry(host).Touch(engineer.Sid, engineer.Oid));
    }

    private void StepUp(WebApplicationFactory<Program> host, Engineer engineer) =>
        Assert.True(Registry(host).RecordAuthentication(engineer.Sid, engineer.Oid, _time.GetUtcNow()));

    private static Task<IReadOnlyList<IncidentTicket>> Tickets(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IIncidentStore>().GetRecentAsync(100, CancellationToken.None);

    // The proposal store is the agent's own; only an investigation opens proposals, so the test reaches it by name.
    private static TicketProposal OpenProposal(WebApplicationFactory<Program> host, string ownerId)
    {
        var storeType = typeof(IApprovalService).Assembly.GetType("PaymentOps.Agent.Approvals.ProposalStore", throwOnError: true)!;
        var store = host.Services.GetRequiredService(storeType);
        var draft = Results.WithPendingTicket().Proposal! with { ProposalId = null };
        return (TicketProposal)storeType.GetMethod("Open")!.Invoke(store, [draft, Guid.NewGuid(), Guid.NewGuid(), ownerId])!;
    }
}
