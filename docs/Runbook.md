# Runbook

What to do when the agent may have been steered or may have shown something it shouldn't. Logs hold ids, counts and timings, never content, so the investigation id on the result's footer is how everything is traced.

**Finding an investigation's logs.** In the environment's Log Analytics workspace:

```kusto
AppTraces
| where Message has "<investigation id>"
| project TimeGenerated, SeverityLevel, Message
| order by TimeGenerated asc
```

The relevant events: `3001` assessed, `3002` not assessed, `3005` untrusted values screened and withheld (counts per tool), `3006` blocked by the content filter (prompt or completion side), `3101` an approval decision.

## Suspected prompt injection

Signs: a result with a gap saying text was withheld because it looked like an instruction; an assessment or proposed ticket that doesn't follow from its evidence; a proposal for a processor or merchants the question wasn't about.

1. **Don't approve.** Reject the proposal on its card. A pending proposal also expires after 15 minutes.
2. **Find the source.** Note the investigation id. Event `3005` names the tool whose text was withheld; the result's gaps name the source. For knowledge, open the cited section's source page, which shows the original text to an authorized person. For data, look up the merchant, processor or incident in its system of record.
3. **If the screen flagged it,** the control worked: the model never read the text. Correct or remove the source. Knowledge changes go through a pull request to `data/knowledge/` and the evaluation gate; data belongs to its owning system.
4. **If the screen didn't flag it** and the result was steered, treat it as a miss. Record the text and the investigation id, add a case with that text to the evaluation, and raise it with the maintainer before the next behaviour change merges.
5. **If many values are unscreenable** (event `3005` with unscreenable counts, gaps saying text couldn't be checked), Prompt Shields is failing or throttled. Investigations continue on structured data with text withheld; check the Foundry account's health and the app identity's *Prompt Shields User* assignment.

## Suspected disclosure

Signs: a General user sees Restricted knowledge; a ticket's title or summary carries bank details or personal data; an answer quotes something the asker shouldn't have.

1. **Stop the spread.** Reject a pending proposal. A created ticket goes to whoever owns the ticket system to be withdrawn.
2. **Confirm who saw what.** From the investigation id, find the signed-in user and their roles in Entra; check the section's classification in the corpus and the user's membership of the Restricted group.
3. **Find the cause.** A misclassified section is fixed in the corpus by pull request. A wrong group membership or app role is fixed in Entra. Text that passed the ticket content policy means a rule is missing; add it with a test.
4. **Record it.** Note the investigation id, what was seen and by whom, the cause and the fix. The data here is synthetic, so no notification duty applies; with real data, follow the organisation's incident process.
