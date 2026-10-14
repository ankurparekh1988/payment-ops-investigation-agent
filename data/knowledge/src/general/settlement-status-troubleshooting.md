---
docId: settlement-status-troubleshooting
title: Settlement status troubleshooting
docType: runbook
---

# Settlement status troubleshooting

## How to use this guide

A payment moves through several stages before the merchant is paid: the processor accepts it, we capture it, the processor includes it in a settlement batch, and we pay the merchant from that batch. When a merchant asks where their money is, find the stage each payment has reached, then use the section that matches.

## Accepted but not settled

### Captured before the cut-off

If the payment was captured before the processor's configured cut-off and the batch for that day has arrived without it, compare our capture time with the processor's. The processor decides the batch from its own capture time, and a payment we captured just before the cut-off can still land in the next batch.

### Captured after the cut-off

The payment belongs to the next settlement day's batch. This is expected, and the payout follows that batch. Check whether the next day was a closure day, because that pushes it out again.

### No batch at all

If no batch arrived for that settlement date, the problem is the batch, not the payment. Follow the missing settlement batch runbook.

## Settled but not paid out

### The merchant has a hold

A risk hold pauses payouts while payments carry on. If the merchant has a hold, don't investigate the settlement further. Say that payouts are paused for an account review and refer to the risk hold review policy for anything more.

### The payout was held for reconciliation

If the batch total for the merchant didn't match our capture records, that merchant's payout is held while the mismatch is reviewed. Check the processor's operating guide for the usual causes before raising it.

### The payout failed

A payout can fail at the merchant's bank, for example because of closed or changed account details. That is a merchant account issue, not a settlement issue, and goes to merchant support.

## Many merchants at once

When the same symptom shows up for many merchants with the same processor, look at the processor before the individual payments. Check its status and our error events for that processor, and follow the payout delay during a processor outage runbook if there's an incident.
