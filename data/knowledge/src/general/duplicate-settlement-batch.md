---
docId: duplicate-settlement-batch
title: Duplicate settlement batch
docType: runbook
---

# Duplicate settlement batch

## When to use this runbook

Use this runbook when two settlement batches from the same processor appear to cover the same payments, or when a merchant reports being paid twice for the same day.

## Tell a duplicate from a split batch

### Compare the batches

Two batches are a true duplicate only if they have the same processor, the same settlement date and the same transactions. Compare the transaction references, not just the totals: two batches with equal totals can contain different payments.

### Signs of a split batch

A processor that splits a large batch sends two or more files with the same settlement date, but each transaction appears in only one of them. Together they add up to the batch. That is expected and needs no action. The processor's operating guide says whether it splits batches.

### Signs of a resend

A resent batch usually follows a delivery failure or a processor incident and repeats every transaction of the earlier file. Check the processor status and our error events around the first delivery: a failed acknowledgement from our side often makes the processor send the file again.

## If it is a duplicate

### Stop the second payout

If the payouts from the duplicate haven't gone out, hold them for the affected merchants only. Payouts from the original batch go ahead.

### If the money has already gone out

Don't reverse anything yourself. Merchant payouts can only be reversed by Finance Operations, and a reversal needs the merchant to be told first. Raise an incident with the batch references and the list of affected merchants, with an Operations Engineer's approval, and Finance Operations takes it from there.

### Find the cause

Record whether the duplicate came from the processor resending or from us processing the same file twice. Processing our own file twice is a platform defect and goes to the engineering on-call as well.

## Talking to merchants

Tell merchants who were paid twice that a duplicate payout was made in error and that Finance Operations will contact them about it. Don't tell them to send money back on their own.
