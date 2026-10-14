---
docId: missing-settlement-batch
title: Missing settlement batch
docType: runbook
---

# Missing settlement batch

## When to use this runbook

Use this runbook when a processor's settlement batch for a settlement date hasn't arrived, and the processor's expected delivery time for that batch has passed. If the batch is late because of a processor outage you already know about, start with the payout delay during a processor outage runbook and come back here only if the batch still doesn't arrive.

## Confirm the batch is missing

### Check what was received

List the batches received from the processor around the settlement date, not just on it. A batch filed under the wrong date, or one that closed a day late, looks missing when you search a single day.

### Rule out a closure day

Check the settlement calendar for the date. If it was a settlement system closure day, no batch is expected: the captures roll into the next batch. Nothing is missing, and the closure days policy explains what to tell merchants.

### Rule out a partial delivery

Some processors split a batch across more than one file. If one file arrived and its totals don't cover the batch, the rest may still be on its way. Check the processor's operating guide for how it delivers split batches.

## Find out why

### Ask whether the processor sent it

Check the processor's status for a settlement or file delivery incident. If there's none, check our error events for failed file transfers or rejected files from that processor in the delivery window. A file we received but rejected is not missing; it needs fixing and reprocessing.

### Look at other processors

If batches from other processors arrived normally, the problem is probably with this processor or our connection to it. If several processors' batches are missing, treat it as a platform incident and follow the incident escalation procedure.

## Act

### The processor confirms a delay

Record the processor's reference and the new expected delivery time. Affected merchants' payouts move with the batch. Tell them the payout is delayed and that we'll confirm the new date once the batch arrives; don't promise a date the processor hasn't given.

### The processor says it was sent

Ask for the file name, size and delivery time they have on record, and compare it with our transfer logs. If the file reached us and was rejected, raise it with the engineering on-call. If it never reached us, ask the processor to resend.

### Nobody can find it

Raise an incident with the processor. Creating it needs an Operations Engineer's approval. Include the settlement date, the expected batch, what was and wasn't received, and the error events you found.

## Close out

When the batch arrives, check that its totals match our capture records before payouts go out, and that it hasn't arrived twice. A batch resent after a delay is a common source of duplicates.
