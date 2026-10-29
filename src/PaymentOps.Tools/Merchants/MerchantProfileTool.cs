using Microsoft.Extensions.Options;

using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;

namespace PaymentOps.Tools.Merchants;

public sealed record MerchantProfileRequest(string? MerchantId);

public sealed record MerchantProfile(string Id, string Name, string EvidenceId, IReadOnlyList<RoutedProcessor> Processors, RiskHoldView? RiskHold);

/// <param name="EvidenceId">Null when the processor has no record: there's nothing to cite.</param>
public sealed record RoutedProcessor(string Id, string Name, string? EvidenceId);

/// <param name="RuleId">Null for a manual hold. What the rule means is in Restricted knowledge, not here.</param>
public sealed record RiskHoldView(string Id, DateTimeOffset PlacedAt, string? RuleId, DateOnly ReviewBy, string EvidenceId);

/// <summary>Who the merchant is, which processors its payments go to, and whether its payouts are on hold.</summary>
public sealed class MerchantProfileTool(IMerchantData merchants, IProcessorData processors, IOptions<ToolOptions> options)
    : InvestigationTool<MerchantProfileRequest>(options)
{
    public override string Name => ToolNames.MerchantProfile;

    public override string Description =>
        "A merchant's profile: the processors its payments are routed to, and any risk hold on its payouts with the rule that placed it.";

    public override string Source => DataSources.Merchants;

    protected override async Task<ToolResult> RunCoreAsync(MerchantProfileRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        var merchantId = ToolArguments.Id(request.MerchantId, "merchant_id");
        if (await ReadAsync(token => merchants.FindAsync(merchantId, token), DataSources.Merchants, cancellationToken) is not { } merchant)
        {
            return NotFound("merchant", merchantId);
        }

        var evidence = Collect();
        var routed = new List<RoutedProcessor>();
        foreach (var processorId in merchant.ProcessorIds)
        {
            var processor = await ReadAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, cancellationToken);
            routed.Add(processor is null
                ? new RoutedProcessor(processorId, processorId, null)
                : new RoutedProcessor(processorId, UntrustedText.Wrap(processor.Name), evidence.Record(EvidenceIds.Processor(processorId), processor.Name)));
        }

        var hold = merchant.RiskHold is { } h
            ? new RiskHoldView(h.Id, h.PlacedAt, h.RuleId, h.ReviewBy, evidence.Record(EvidenceIds.Hold(h.Id), $"Risk hold {h.Id}"))
            : null;
        // Names come from onboarding, outside this system, so the model reads them as data to be screened.
        return evidence.Ok(new MerchantProfile(merchant.Id, UntrustedText.Wrap(merchant.Name), evidence.Record(EvidenceIds.Merchant(merchant.Id), merchant.Name), routed, hold));
    }
}
