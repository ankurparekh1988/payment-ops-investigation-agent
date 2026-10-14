---
docId: fabrikam-settlement-outage-postmortem
title: Fabrikam Pay settlement outage postmortem
docType: incident-report
---

# Fabrikam Pay settlement outage postmortem

## Summary

Fabrikam Pay's settlement file service failed for most of an afternoon while its card authorization service kept working normally. Payments were approved all day, so the problem wasn't noticed until merchants reported missing payouts the next morning. The batch closed late and payouts for the affected merchants moved one settlement day.

This report describes the incident and what we changed. It records the pattern so it's recognised faster next time.

## What happened

### The symptom

Authorization success rates for Fabrikam Pay stayed normal throughout. Fabrikam Pay's status initially reported only "degraded file delivery", which nobody connected with settlement.

### What we missed

Our error events showed repeated timeouts on settlement file transfers from Fabrikam Pay from early afternoon onwards. Nothing else was failing, so the timeouts weren't escalated. No other processor showed the same errors.

### The impact

Fabrikam Pay's batch closed after its cut-off once the service recovered. Payouts from that batch moved to the next settlement day for every merchant routed through Fabrikam Pay that day. Merchants routed through other processors weren't affected.

## Why it took long to spot

We judged processor health by authorizations. A processor can approve payments normally while its settlement side is down, and the two have to be checked separately.

## What we changed

### Investigation

The payout delay during a processor outage runbook now starts from the settlement batch and the processor's status for settlement, not from authorization rates.

### Recognising the pattern

Repeated settlement file timeouts from one processor, with healthy authorizations and no errors from other processors, are now treated as a likely settlement outage. Check the processor's status for settlement specifically, and expect its batch to close late.

### Telling merchants

When a batch is known to be late, Operations tells affected merchants before they ask, with the new expected payout date.
