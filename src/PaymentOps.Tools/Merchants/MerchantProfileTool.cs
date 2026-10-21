using Microsoft.Extensions.Options;

using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;

namespace PaymentOps.Tools.Merchants;

public sealed record MerchantProfileRequest(string? MerchantId);

public sealed record MerchantProfile(string Id, string Name, string EvidenceId, IReadOnlyList<RoutedProcessor> Processors, RiskHoldView? RiskHold);

public sealed record RoutedProcessor(string Id, string Name, string EvidenceId);

/// <param name="RuleId">Null for a manual hold. What the rule means is in Restricted knowledge, not here.</param>
public sealed record RiskHoldView(string Id, DateTimeOffset PlacedAt, string? RuleId, DateOnly ReviewBy, string EvidenceId);

/// <summary>Who the merchant is, which processors its payments go to, and whether its payouts are on hold.</summary>
public sealed class MerchantProfileTool(IMerchantData merchants, IProcessorData processors, IOptions<ToolOptions> options) : InvestigationTool<MerchantProfileRequest>
{
    public override string Name => ToolNames.MerchantProfile;

    public override string Description =>
        "A merchant's profile: the processors its payments are routed to, and any risk hold on its payouts with the rule that placed it.";

    public override string Source => DataSources.Merchants;

    protected override async Task<ToolResult> RunCoreAsync(MerchantProfileRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryId(request.MerchantId, "merchant_id", out var merchantId, out var error))
        {
            return Invalid(error!);
        }

        var backoff = options.Value.RetryBackoff;
        var merchant = await DataSourceCall.RunAsync(token => merchants.FindAsync(merchantId, token), DataSources.Merchants, backoff, cancellationToken);
        if (merchant is null)
        {
            return new ToolResult.Failed(ToolErrorCode.NotFound, $"No merchant has the id {merchantId}.");
        }

        var evidence = new List<EvidenceItem> { new RecordEvidence(EvidenceIds.Merchant(merchant.Id), merchant.Name) };
        var routed = new List<RoutedProcessor>();
        foreach (var processorId in merchant.ProcessorIds)
        {
            var processor = await DataSourceCall.RunAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, backoff, cancellationToken);
            var name = processor?.Name ?? processorId;
            routed.Add(new RoutedProcessor(processorId, name, EvidenceIds.Processor(processorId)));
            evidence.Add(new RecordEvidence(EvidenceIds.Processor(processorId), name));
        }

        RiskHoldView? hold = null;
        if (merchant.RiskHold is { } riskHold)
        {
            hold = new RiskHoldView(riskHold.Id, riskHold.PlacedAt, riskHold.RuleId, riskHold.ReviewBy, EvidenceIds.Hold(riskHold.Id));
            evidence.Add(new RecordEvidence(hold.EvidenceId, $"Risk hold {riskHold.Id}"));
        }

        return new ToolResult.Ok(new MerchantProfile(merchant.Id, merchant.Name, EvidenceIds.Merchant(merchant.Id), routed, hold), evidence, []);
    }
}
