---
docId: risk-hold-review-policy
title: Risk hold review policy
docType: policy
---

# Risk hold review policy

## Purpose

This policy explains what a risk hold on a merchant's payouts means, how Risk & Compliance reviews it, and what Operations may say to the merchant while it's in place.

The hold itself, the rule that placed it and the rule's current thresholds are recorded in the risk system. This policy explains how to interpret and handle them; it doesn't repeat them.

## What a hold does

A hold pauses payouts only. The merchant can still take payments, and the funds stay in the merchant's balance until the hold is released. A hold never reverses a payment or a payout that has already been made.

## Risk rules

Each automatic hold names the rule that placed it. Read the rule id from the hold, then use the section for that rule.

### RF-12 chargeback rate

The merchant's chargeback rate has risen above the configured limit. This usually signals a fulfilment or fraud problem on the merchant's side, and it can lead to scheme fines for us. The review looks at the disputed payments and the merchant's explanation.

### RF-37 volume spike

The merchant's processing volume is far above its own recent average. A spike can be legitimate, such as a seasonal sale or a new sales channel, so the review first asks the merchant's account manager whether one was expected. An unexplained spike is treated as possible account takeover or bust-out fraud.

### RF-51 expired verification

The merchant's verification documents have expired. The hold is released as soon as Risk & Compliance accepts new documents, and no other review is needed.

### Manual holds

Risk & Compliance can place a hold manually after an investigation. A manual hold has no rule id, and its reason is recorded only in the risk case.

## Reviewing a hold

### Who reviews

Holds placed by RF-12 and RF-37, and every manual hold, are reviewed by Risk & Compliance. Operations can't release a hold, and shouldn't promise the merchant a release date.

### Outcomes

The reviewer releases the hold, keeps it with a new review date, or moves the merchant to a rolling reserve. Each outcome is recorded with the reason, and Operations is told the outcome but not the reason.

### Escalation

A hold that is past its review date is escalated to the head of Risk & Compliance. Operations should raise any merchant complaint about a long hold with Risk & Compliance rather than with the merchant's account manager.

## Talking to the merchant

### What Operations may say

Operations may tell the merchant that payouts are paused for a routine account review, that payments are unaffected, and that Risk & Compliance will contact them.

### What Operations must not say

Don't tell the merchant which rule placed the hold, what its thresholds are, or what the review looks at. A merchant who knows the thresholds can stay just under them. For RF-51, Operations may say that updated verification documents are needed, because the merchant has to act.
