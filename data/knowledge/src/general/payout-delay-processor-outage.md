---
docId: payout-delay-processor-outage
title: Payout delay during a processor outage
docType: runbook
---

# Payout delay during a processor outage

## When to use this runbook

Use this runbook when a merchant reports a late payout and the settlement batch from the processor that handled their payments is missing, late or incomplete, while batches from other processors arrived as expected.

If batches from every processor are affected, the problem is more likely on our side. Follow the incident escalation procedure instead.

## Confirm the outage

### Find the processor

Start from the merchant's payments for the day the payout should have settled, and note which processor each one was routed to. A merchant can be routed to more than one processor, so check them all. Only the processor whose batch is missing is in scope.

### Check the processor status

Look up incidents the processor reported for that day. An incident counts as an outage for this runbook only if it affected settlement or batch file delivery. An incident that only affected card authorizations explains declined payments, not a late payout.

A processor can have a settlement problem it hasn't reported yet. If the status shows no incident but the batch is still missing, carry on with the next two checks before deciding.

### Compare the outage window with the cut-off

Look up the processor's configured settlement cut-off and the time zone it's in. Don't rely on a cut-off you remember; processors change them, and so do we.

- If the outage started after the cut-off, the batch should already have closed. A missing batch then points to a file delivery problem rather than a batch that never closed.
- If the outage covered the cut-off, the batch either closed late or didn't close at all. Both delay the payout by at least one settlement day.
- If the outage ended before the cut-off, the outage is unlikely to explain the delay. Look for another cause.

### Check the error events

Look for batch delivery and settlement errors for the merchant and processor in the same window. Repeated timeouts or rejections from one processor, with nothing similar from the others, confirm the problem is on the processor's side.

## Decide the next action

### The batch closed late

The payout moves to the next settlement day. Tell the merchant the new expected date and refer to the processor incident. No ticket is needed.

### The batch didn't close

Raise an incident with the processor and include the batch date, the affected merchants and the processor's incident reference if there is one. Creating the incident needs an Operations Engineer's approval.

### Many merchants are affected

If the same missing batch affects many merchants, involve the payments operations lead before replying to anyone, so merchants get one consistent notice instead of separate answers.

## After the outage

Check that the delayed batch arrives in the next settlement run and that the merchant's payout is in it. If the batch still hasn't arrived after the processor reports the incident resolved, treat it as a missing settlement batch.
