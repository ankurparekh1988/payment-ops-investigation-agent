---
docId: settlement-pending-investigation
title: Settlement pending investigation
docType: runbook
---

# Settlement pending investigation

## When to use this runbook

Use this runbook when a merchant's payments are still pending settlement after the point their processor normally settles them, and there is no processor outage to explain it.

## Work out when it should have settled

### Find the capture time

Take the capture time of the oldest pending payment. Use the processor's recorded capture time where we have it, because that is what the processor uses to decide the batch.

### Apply the cut-off

Look up the processor's configured cut-off and the time zone it's in. Convert the capture time into that time zone before comparing. Captures near midnight or near the cut-off are often misjudged because of the time zone alone.

### Apply the settlement calendar

Count forward over settlement days, not calendar days. Weekends and settlement system closure days don't count. A capture just after the cut-off on the day before a long weekend can legitimately settle several calendar days later.

## Compare with what happened

### It isn't late

If the expected settlement date hasn't arrived yet, nothing is wrong. Tell the merchant the expected date and why: the capture missed the cut-off, a closure day intervened, or both.

### It is late

If the expected settlement date has passed and the payments are still pending, check whether the batch for that date arrived. A missing batch is handled by the missing settlement batch runbook. If the batch arrived without these payments, raise it with the processor and include the payment references and capture times.

## Explaining it to the merchant

Merchants usually count calendar days from the moment of sale. Explain the actual cause in plain terms, for example that the payment was taken after the day's processing cut-off and the next working day was a public holiday. Give the expected date, not just the reason.
