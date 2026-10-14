---
docId: fabrikam-pay-operating-guide
title: Fabrikam Pay operating guide
docType: processor-doc
---

# Fabrikam Pay operating guide

## How Fabrikam Pay settles

Fabrikam Pay acquires card payments for many of our small and mid-sized merchants. It settles once per settlement day: it closes a batch at its cut-off, then sends us one settlement file for that batch. We pay merchants from the file, not from our own capture records.

The cut-off, the expected delivery time of the file and the point at which a file counts as late are all configured per processor. Read them from the processor configuration; this guide describes how Fabrikam Pay behaves around them.

## Late captures

### What rolls forward

A payment captured after the cut-off belongs to the next settlement day's batch. Fabrikam Pay uses the capture time it recorded, not ours, so a payment we captured just before the cut-off can still land in the next batch if our capture reached Fabrikam Pay after it.

### Closure days

Fabrikam Pay doesn't close a batch on a settlement system closure day. Captures from that day join the next batch, so the batch after a closure day is usually larger than normal and pays out one settlement day later than merchants expect.

## Settlement files

### A late file

A late file usually means Fabrikam Pay closed the batch on time but delivery failed or was delayed. Fabrikam Pay normally resends the same file once delivery recovers, so wait for the resend before treating the batch as missing.

### A partial file

Fabrikam Pay sometimes sends a file that covers only part of the batch, followed by a second file with the rest. Both files carry the same batch date. Pay out only after the batch totals in the files add up, and don't mistake the second file for a duplicate.

### A file that never arrives

If no file arrives and Fabrikam Pay reports no incident, contact Fabrikam Pay partner operations before assuming the batch is lost. Follow the missing settlement batch runbook from there.

## Reconciliation mismatches

### Reading a mismatch

We reconcile each merchant's total in the file against our capture records for the same batch. A small mismatch is most often a capture that rolled into the next batch, so check the next day's file before raising it.

### Mismatches across many merchants

A mismatch that affects many merchants in the same file usually means the file is partial or the batch closed at an unexpected time. Hold the payout for the affected merchants only and raise it with Fabrikam Pay partner operations.
