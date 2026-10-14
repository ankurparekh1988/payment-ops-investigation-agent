---
docId: northwind-acquiring-operating-guide
title: Northwind Acquiring operating guide
docType: processor-doc
---

# Northwind Acquiring operating guide

## How Northwind Acquiring settles

Northwind Acquiring acquires card payments mainly for our larger merchants. Unlike processors that release funds as soon as the batch closes, Northwind holds them for a further period before releasing them, so its payouts run behind those of other processors for the same capture date.

Its cut-off, time zone, holding period and delivery times are configured per processor. Read them from the processor configuration; this guide describes how Northwind behaves around them.

## Late captures

### Northwind uses its own clock

Northwind assigns a payment to a batch using the time the payment reached Northwind, in the time zone of its configured cut-off. A capture we sent shortly before the cut-off can still miss it if it was delayed in transit.

### Closure days

Northwind doesn't close a batch on a settlement system closure day, and its holding period counts settlement days only, so closure days extend it. A capture just after the cut-off before a long weekend can therefore take noticeably longer to pay out than the same capture with another processor.

## Settlement files

### One file per batch

Northwind never splits a batch. Two files with the same settlement date from Northwind are a resend or an error, never a split batch, and should be checked with the duplicate settlement batch runbook.

### Corrections

Northwind corrects a batch by sending a separate adjustment file later, rather than resending the batch. An adjustment file changes the totals for some merchants and needs to be reconciled with the original batch, not paid on its own.

## When something looks wrong

### A batch that seems a day late

Before treating a Northwind payout as late, check that the holding period was counted. Most reported Northwind delays are payouts that were on time.

### Contact

Raise settlement problems with Northwind's partner operations team. Northwind expects the settlement date and the merchant references in every request.
