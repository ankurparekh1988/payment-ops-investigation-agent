---
docId: incident-escalation-procedure
title: Incident escalation procedure
docType: policy
---

# Incident escalation procedure

## Purpose

This procedure says when a payments problem becomes an incident, who raises it, and who must be told. It applies to problems with processors and with our own platform.

## Raising an incident

### When to raise one

Raise an incident when a settlement batch didn't close or didn't arrive, when payouts were made in error, or when the same failure affects many merchants. A delay that the processor has already explained, or that a closure day explains, is not an incident.

### Who may raise one

Anyone in Operations can prepare an incident, but it is created only after an Operations Engineer approves it. The approver checks the evidence and the affected merchants before approving.

### What it must contain

Include the processor, the settlement date or window, the affected merchants, the evidence you found (batches, error events, processor status) and the processor's own reference if there is one. Don't include card or bank details.

## Processor incidents

When the evidence points at one processor, raise the incident against that processor. Keep checking the processor's status, and update the incident when they report progress.

## Platform incidents

### Recognising one

If batches or payouts from several processors are affected at the same time, assume the problem is on our side until shown otherwise. Processors rarely fail together.

### What to do

Page the engineering on-call straight away, as well as raising the incident. Don't contact processors about it until engineering has ruled out our own systems.

## Who to tell

### Payments operations lead

Tell the payments operations lead about any incident that affects many merchants or involves money paid in error. They decide whether merchants get a single notice.

### Finance Operations

Tell Finance Operations about anything involving duplicate or incorrect payouts. Only they can reverse or adjust a payout.

### Risk & Compliance

Tell Risk & Compliance if a problem looks like fraud or account takeover rather than a failure. They decide what happens next, and Operations doesn't discuss it with the merchant.
